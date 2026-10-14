using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal enum TemperatureState { Available, Pending, SensorUnavailable, ProviderMissing, AccessDenied, ReadFailed, PlatformUnsupported, InvalidData }
internal enum TemperatureScope { Sensor, HighestSensor, BackingDisks }
internal sealed record TemperatureReading(double? Celsius, DateTimeOffset CheckedAt, string Source, string Detail, TemperatureState State, string? Code = null, TemperatureScope Scope = TemperatureScope.Sensor)
{
    public static bool Valid(double value) => double.IsFinite(value) && value is >= -50 and <= 200;
    public static TemperatureReading From(double value, string source, string detail, DateTimeOffset? at = null) =>
        Valid(value) ? new(value, at ?? DateTimeOffset.UtcNow, source, detail, TemperatureState.Available) : Missing(TemperatureState.InvalidData, source, detail, at: at);
    public static TemperatureReading Missing(TemperatureState state, string source = "", string detail = "", string? code = null, DateTimeOffset? at = null) =>
        new(null, at ?? DateTimeOffset.UtcNow, source, detail, state, code);
    public static TemperatureReading Highest(IEnumerable<TemperatureReading> readings, string source, string detail, TemperatureScope scope = TemperatureScope.HighestSensor)
    {
        var values = readings.Where(r => r.State == TemperatureState.Available && r.Celsius.HasValue && Valid(r.Celsius.Value)).ToArray();
        return values.Length == 0 ? Missing(TemperatureState.SensorUnavailable, source, detail) :
            new(values.Max(r => r.Celsius), values.Min(r => r.CheckedAt), source, detail, TemperatureState.Available, Scope: scope);
    }
}
internal static class TemperaturePresentation
{
    public static bool Fresh(TemperatureReading? reading, DateTimeOffset now) => reading is { State: TemperatureState.Available, Celsius: double value } &&
        TemperatureReading.Valid(value) && now - reading.CheckedAt <= TimeSpan.FromSeconds(30) && reading.CheckedAt - now <= TimeSpan.FromSeconds(5);
    public static string Label(TemperatureReading? reading, DateTimeOffset now) => Fresh(reading, now) ? reading!.Celsius!.Value.ToString("0", Loc.Culture) + "°C" : "—°C";
    public static string Tooltip(TemperatureReading? reading, DateTimeOffset now)
    {
        var text = Fresh(reading, now) ? Loc.F("thermal.value", reading!.Celsius) : Loc.T("thermal.unavailable");
        if (reading is { State: TemperatureState.Available } && !Fresh(reading, now)) text += "\n" + Loc.T("thermal.stale");
        else if (reading == null || reading.State != TemperatureState.Available)
            text += "\n" + Loc.T(reading?.State switch {
                TemperatureState.ProviderMissing => "thermal.provider_missing", TemperatureState.AccessDenied => "thermal.access_denied",
                TemperatureState.ReadFailed => "thermal.read_failed", TemperatureState.PlatformUnsupported => "thermal.platform_unsupported",
                TemperatureState.InvalidData => "thermal.invalid_data", _ => "thermal.sensor_unavailable" });
        if (!string.IsNullOrEmpty(reading?.Source)) text += "\n" + reading.Source;
        if (reading?.Scope == TemperatureScope.HighestSensor) text += "\n" + Loc.T("thermal.highest_sensor");
        else if (reading?.Scope == TemperatureScope.BackingDisks) text += "\n" + Loc.T("thermal.backing_disks");
        if (!string.IsNullOrEmpty(reading?.Detail)) text += "\n" + reading.Detail;
        if (!string.IsNullOrEmpty(reading?.Code)) text += "\n" + reading.Code;
        return text;
    }
}
