using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal sealed class HardwareHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    private readonly List<HardwareObservation> _points = new();
    public HardwareUsage Observe(HardwareUsage usage)
    {
        if (_points.Count > 0 && usage.CheckedAt < _points[^1].At) _points.Clear();
        if (_points.Count == 0 || usage.CheckedAt > _points[^1].At)
            _points.Add(new(usage.CheckedAt, Valid(usage.Cpu.Percent), Valid(usage.Memory.Percent), Valid(usage.Gpu.Percent), Valid(usage.Vram.Percent)));
        _points.RemoveAll(point => point.At < usage.CheckedAt - Window);
        if (_points.Count > 61) _points.RemoveRange(0, _points.Count - 61);
        return usage with { History = _points.ToArray() };
    }
    private static double? Valid(double? value) => value is double actual && double.IsFinite(actual) && actual is >= 0 and <= 100 ? actual : null;
}

internal sealed record ResourceTrendPoint(DateTimeOffset At, double? Percent);
internal static class ResourceTrend
{
    public static ResourceTrendPoint[][] Segments(IEnumerable<ResourceTrendPoint> points)
    {
        var segments = new List<ResourceTrendPoint[]>(); var current = new List<ResourceTrendPoint>();
        foreach (var point in points)
        {
            if (point.Percent == null || current.Count > 0 && point.At - current[^1].At > TimeSpan.FromSeconds(6))
            {
                if (current.Count > 0) { segments.Add(current.ToArray()); current.Clear(); }
            }
            if (point.Percent != null) current.Add(point);
        }
        if (current.Count > 0) segments.Add(current.ToArray());
        return segments.ToArray();
    }
}
