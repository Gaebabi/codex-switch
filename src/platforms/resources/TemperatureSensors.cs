using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal sealed record TemperatureSensor(string HardwareId, string HardwareName, string HardwareType, string Name, double? Celsius);
internal static class TemperatureSensors
{
    public static TemperatureReading Resource(IEnumerable<TemperatureSensor> sensors, string type, string source, DateTimeOffset at) =>
        Select(sensors.Where(s => string.Equals(s.HardwareType, type, StringComparison.OrdinalIgnoreCase)), source, at);
    public static TemperatureReading Gpu(IEnumerable<TemperatureSensor> sensors, IReadOnlyList<GpuResource> adapters, GpuResource adapter, bool memory, string source, DateTimeOffset at)
    {
        if (adapters.Count(a => string.Equals(a.Name, adapter.Name, StringComparison.OrdinalIgnoreCase)) != 1) return TemperatureReading.Missing(TemperatureState.SensorUnavailable, source, at: at);
        var devices = sensors.Where(s => s.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) && string.Equals(s.HardwareName, adapter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (devices.Select(s => s.HardwareId).Distinct(StringComparer.Ordinal).Count() != 1) return TemperatureReading.Missing(TemperatureState.SensorUnavailable, source, at: at);
        var names = memory ? new[] { "GPU Memory", "GPU Memory Junction", "Memory Junction" } : new[] { "GPU Core", "GPU Temperature" };
        return Select(devices.Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)), source, at);
    }
    private static TemperatureReading Select(IEnumerable<TemperatureSensor> sensors, string source, DateTimeOffset at)
    {
        var values = sensors.Where(s => s.Celsius.HasValue).ToArray();
        if (values.Any(s => !TemperatureReading.Valid(s.Celsius!.Value))) return TemperatureReading.Missing(TemperatureState.InvalidData, source, at: at);
        var detail = string.Join("\n", values.Select(s => s.HardwareName + " · " + s.Name));
        return TemperatureReading.Highest(values.Select(s => TemperatureReading.From(s.Celsius!.Value, source, s.Name, at)), source, detail,
            values.Length > 1 ? TemperatureScope.HighestSensor : TemperatureScope.Sensor);
    }
}
