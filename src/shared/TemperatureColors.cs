using System;
using System.Linq;

namespace CodexAccountDesk;

internal enum TemperatureKind { Cpu, Memory, Gpu, Vram, Disk }
internal sealed record TemperatureLimits(double Warning, double Critical, bool Reported)
{
    public static TemperatureLimits? Device(double warning, double critical) => double.IsFinite(warning) && double.IsFinite(critical) &&
        warning > 0 && warning < critical && critical <= 200 ? new(warning, critical, true) : null;
}
internal static class TemperatureColors
{
    public const double ReleaseMargin = 3;
    public static TemperatureLimits Limits(TemperatureReading? reading, TemperatureKind kind) => reading?.Limits ?? kind switch {
        TemperatureKind.Cpu => new(75, 90, false), TemperatureKind.Memory => new(60, 75, false), TemperatureKind.Gpu => new(80, 90, false),
        TemperatureKind.Vram => new(90, 105, false), _ => new(55, 70, false) };
    public static ResourcePressure Pressure(TemperatureReading? reading, TemperatureKind kind, ResourcePressure previous, DateTimeOffset now)
    {
        if (!TemperaturePresentation.Fresh(reading, now)) return ResourcePressure.Unknown;
        if (reading!.Components is { Length: > 0 } values) return values.Max(value => Pressure(value, kind, previous, now));
        var limits = Limits(reading, kind); var value = reading.Celsius!.Value;
        if (value >= limits.Critical || previous == ResourcePressure.Critical && value >= limits.Critical - ReleaseMargin) return ResourcePressure.Critical;
        if (value >= limits.Warning || (previous is ResourcePressure.Warning or ResourcePressure.Critical) && value >= limits.Warning - ReleaseMargin) return ResourcePressure.Warning;
        return ResourcePressure.Normal;
    }
    public static string Color(ResourcePressure pressure) => pressure switch { ResourcePressure.Critical => "danger", ResourcePressure.Warning => "warning", ResourcePressure.Unknown => "muted", _ => "text" };
    public static string Tooltip(TemperatureReading? reading, TemperatureKind kind)
    {
        if (reading?.Components is { Length: > 0 } values)
            return string.Join("\n", values.Select(value => value.Source + "\n" + Tooltip(value, kind)));
        var limits = Limits(reading, kind);
        return Loc.F("thermal.color_limits", limits.Warning, limits.Critical) + "\n" + Loc.T(limits.Reported ? "thermal.reported_limits" : "thermal.default_limits") +
            "\n" + Loc.F("thermal.color_release", ReleaseMargin);
    }
}
