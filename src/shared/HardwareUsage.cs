using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodexAccountDesk;

internal enum ResourcePressure { Unknown, Normal, Warning, Critical }
internal sealed record ResourceReading(double? Percent, ulong? UsedBytes = null, ulong? TotalBytes = null, string? Device = null, string? Problem = null, TemperatureReading? Temperature = null);
internal sealed record GpuResource(string Id, string Name, ulong Capacity, double? Percent, ulong? UsedBytes, TemperatureReading? Temperature = null, TemperatureReading? MemoryTemperature = null);
internal sealed record HardwareObservation(DateTimeOffset At, double? Cpu, double? Memory, double? Gpu, double? Vram);
internal sealed record HardwareUsage(DateTimeOffset CheckedAt, ResourceReading Cpu, ResourceReading Memory, ResourceReading Gpu, ResourceReading Vram,
    GpuResource[] Adapters, double SampleMilliseconds, HardwareObservation[]? History = null)
{
    public static HardwareUsage Pending => new(DateTimeOffset.MinValue, new(null), new(null), new(null), new(null), Array.Empty<GpuResource>(), 0);
}
internal static class HardwareMath
{
    public const double WarningPercent = 70, CriticalPercent = 85, ReleaseMargin = 5;
    private static readonly Regex Instance = new(@"luid_0x([a-f0-9]{1,8})_0x([a-f0-9]{1,8})_phys_(\d+)(?:_eng_(\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static (string Adapter, string? Engine)? ParseInstance(string name)
    {
        var match = Instance.Match(name);
        if (!match.Success) return null;
        var adapter = match.Groups[1].Value.PadLeft(8, '0').ToLowerInvariant() + ":" + match.Groups[2].Value.PadLeft(8, '0').ToLowerInvariant();
        return (adapter, match.Groups[4].Success ? match.Groups[3].Value + ":" + match.Groups[4].Value : null);
    }
    public static Dictionary<string, double> EngineUsage(IEnumerable<(string Name, double Value)> readings)
    {
        var engines = new Dictionary<(string Adapter, string Engine), double>();
        foreach (var (name, value) in readings)
        {
            var instance = ParseInstance(name);
            if (instance?.Engine == null || !double.IsFinite(value) || value < 0) continue;
            var key = (instance.Value.Adapter, instance.Value.Engine);
            engines[key] = engines.GetValueOrDefault(key) + value;
        }
        return engines.GroupBy(e => e.Key.Adapter).ToDictionary(g => g.Key, g => Math.Min(100, g.Max(e => e.Value)));
    }
    public static double? Percentage(ulong used, ulong total) => total == 0 || used > total ? null : 100.0 * used / total;
    public static ResourcePressure Pressure(double? value, ResourcePressure previous)
    {
        if (value == null || !double.IsFinite(value.Value) || value < 0 || value > 100) return ResourcePressure.Unknown;
        if (value >= CriticalPercent || previous == ResourcePressure.Critical && value >= CriticalPercent - ReleaseMargin) return ResourcePressure.Critical;
        if (value >= WarningPercent || (previous is ResourcePressure.Warning or ResourcePressure.Critical) && value >= WarningPercent - ReleaseMargin) return ResourcePressure.Warning;
        return ResourcePressure.Normal;
    }
}
