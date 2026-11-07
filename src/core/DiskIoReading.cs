using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal enum DiskIoState { Pending, Available, Unavailable, AccessDenied, InvalidData, PlatformUnsupported }
internal enum DiskIoProfile { Unknown, NoSeekPenalty, SeekPenalty, Mixed }
internal sealed record DiskIoChannel(double? BytesPerSecond, double? LatencyMilliseconds, ResourcePressure Pressure = ResourcePressure.Unknown);
internal sealed record DiskIoReading(DateTimeOffset CheckedAt, DiskIoState State, DiskIoProfile Profile, DiskIoChannel Read, DiskIoChannel Write,
    double? BusyPercent = null, double? ScaleBytesPerSecond = null)
{
    public static DiskIoReading Missing(DiskIoState state, DiskIoProfile profile = DiskIoProfile.Unknown) => new(DateTimeOffset.UtcNow, state, profile, new(null, null), new(null, null));
    public static bool Fresh(DiskIoReading? reading, DateTimeOffset now) => reading?.State == DiskIoState.Available &&
        now - reading.CheckedAt <= TimeSpan.FromSeconds(10) && reading.CheckedAt - now <= TimeSpan.FromSeconds(5);
    public static bool Rate(double? value) => value.HasValue && double.IsFinite(value.Value) && value >= 0;
    public static (double Warning, double Critical)? Limits(DiskIoProfile profile) => profile switch
    { DiskIoProfile.NoSeekPenalty => (10, 20), DiskIoProfile.SeekPenalty => (25, 50), _ => null };
    public static string RateLabel(double? value)
    {
        if (!Rate(value)) return "—";
        var rate = value!.Value;
        return rate >= 1073741824 ? (rate / 1073741824).ToString("0.#", Loc.Culture) + " GiB/s" : rate >= 1048576 ?
            (rate / 1048576).ToString("0.#", Loc.Culture) + " MiB/s" : rate >= 1024 ? (rate / 1024).ToString("0.#", Loc.Culture) + " KiB/s" : rate.ToString("0", Loc.Culture) + " B/s";
    }
    public static double Fill(DiskIoReading? reading, bool write, DateTimeOffset now) => Fresh(reading, now) &&
        Rate(write ? reading!.Write.BytesPerSecond : reading!.Read.BytesPerSecond) && reading!.ScaleBytesPerSecond is > 0 and double scale && double.IsFinite(scale) ?
        Math.Clamp((write ? reading.Write.BytesPerSecond!.Value : reading.Read.BytesPerSecond!.Value) / scale, 0, 1) : 0;
    public static string Tooltip(DiskIoReading? reading, DateTimeOffset now)
    {
        if (!Fresh(reading, now)) return Loc.T(reading?.State switch { DiskIoState.AccessDenied => "io.denied", DiskIoState.PlatformUnsupported => "io.unsupported", DiskIoState.Pending => "io.pending", _ => "io.unavailable" });
        var profile = Loc.T(reading!.Profile switch { DiskIoProfile.NoSeekPenalty => "io.no_seek", DiskIoProfile.SeekPenalty => "io.seek", DiskIoProfile.Mixed => "io.mixed", _ => "io.unknown_profile" });
        var text = Loc.F("io.rates", RateLabel(reading.Read.BytesPerSecond), RateLabel(reading.Write.BytesPerSecond)) + "\n" + profile;
        text += "\n" + Loc.F("io.latency", reading.Read.LatencyMilliseconds?.ToString("0.#", Loc.Culture) ?? "—", reading.Write.LatencyMilliseconds?.ToString("0.#", Loc.Culture) ?? "—");
        text += "\n" + Loc.F("io.busy", reading.BusyPercent?.ToString("0.#", Loc.Culture) ?? "—");
        text += "\n" + Loc.F("io.scale", RateLabel(reading.ScaleBytesPerSecond));
        var limits = Limits(reading.Profile);
        text += "\n" + (limits.HasValue ? Loc.F("io.policy", limits.Value.Warning, limits.Value.Critical) : Loc.T("io.no_limits"));
        return text + "\n" + Loc.T("io.not_health");
    }
}

internal sealed class DiskIoHistory
{
    private readonly Queue<(DateTimeOffset At, double Rate)> _peaks = new();
    private readonly DateTimeOffset?[] _warning = new DateTimeOffset?[2], _critical = new DateTimeOffset?[2];
    private DateTimeOffset? _last;
    private DiskIoProfile _profile;
    public DiskIoReading Observe(DiskIoReading reading)
    {
        var at = reading.CheckedAt;
        if (reading.State != DiskIoState.Available || !DiskIoReading.Rate(reading.Read.BytesPerSecond) || !DiskIoReading.Rate(reading.Write.BytesPerSecond) ||
            !ValidLatency(reading.Read.LatencyMilliseconds) || !ValidLatency(reading.Write.LatencyMilliseconds) ||
            reading.BusyPercent is double busy && (!double.IsFinite(busy) || busy < 0 || busy > 100))
        { Reset(); return reading with { State = reading.State == DiskIoState.Available ? DiskIoState.InvalidData : reading.State, Read = new(null,null), Write = new(null,null), ScaleBytesPerSecond = null }; }
        if (_last.HasValue && (at <= _last || at - _last > TimeSpan.FromSeconds(10) || reading.Profile != _profile)) Reset();
        _profile = reading.Profile; _last = at;
        _peaks.Enqueue((at, Math.Max(reading.Read.BytesPerSecond!.Value, reading.Write.BytesPerSecond!.Value)));
        while (_peaks.Count > 0 && at - _peaks.Peek().At > TimeSpan.FromSeconds(60)) _peaks.Dequeue();
        return reading with { ScaleBytesPerSecond = Math.Max(1024, _peaks.Max(p => p.Rate)), Read = Channel(reading, false), Write = Channel(reading, true) };
    }
    private DiskIoChannel Channel(DiskIoReading reading, bool write)
    {
        var index = write ? 1 : 0; var channel = write ? reading.Write : reading.Read; var limits = DiskIoReading.Limits(reading.Profile);
        if (limits == null || reading.BusyPercent == null || channel.LatencyMilliseconds == null)
        { _warning[index] = _critical[index] = null; return channel with { Pressure = ResourcePressure.Unknown }; }
        var latency = channel.LatencyMilliseconds.Value;
        if (reading.BusyPercent < 80 || channel.BytesPerSecond is not > 0)
        { _warning[index] = _critical[index] = null; return channel with { Pressure = ResourcePressure.Normal }; }
        _warning[index] = latency >= limits.Value.Warning ? _warning[index] ?? reading.CheckedAt : null;
        _critical[index] = latency >= limits.Value.Critical ? _critical[index] ?? reading.CheckedAt : null;
        var pressure = _critical[index].HasValue && reading.CheckedAt - _critical[index]!.Value >= TimeSpan.FromSeconds(60) ? ResourcePressure.Critical :
            _warning[index].HasValue && reading.CheckedAt - _warning[index]!.Value >= TimeSpan.FromSeconds(60) ? ResourcePressure.Warning : ResourcePressure.Normal;
        return channel with { Pressure = pressure };
    }
    private static bool ValidLatency(double? value) => value == null || double.IsFinite(value.Value) && value >= 0;
    private void Reset() { _peaks.Clear(); Array.Clear(_warning); Array.Clear(_critical); _last = null; }
}
