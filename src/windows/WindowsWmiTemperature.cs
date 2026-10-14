using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal sealed class WindowsWmiTemperature
{
    private string? _namespace;
    private DateTimeOffset _next;
    public TemperatureSensor[] Sensors { get; private set; } = Array.Empty<TemperatureSensor>();
    public TemperatureReading Status { get; private set; } = TemperatureReading.Missing(TemperatureState.ProviderMissing);
    public DateTimeOffset CheckedAt { get; private set; }
    public string Source => _namespace ?? "WMI sensors";
    public void Refresh(DateTimeOffset now)
    {
        if (now < _next) return;
        CheckedAt = now; _next = now.AddSeconds(5); Sensors = Array.Empty<TemperatureSensor>();
        var candidates = _namespace == null ? new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" } : new[] { _namespace };
        var failures = new List<TemperatureReading>();
        foreach (var name in candidates)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\" + name, new ConnectionOptions { Timeout = TimeSpan.FromSeconds(1) });
                scope.Connect();
                var hardware = new Dictionary<string, (string Name, string Type)>(StringComparer.Ordinal);
                foreach (var row in Query(scope, "SELECT Identifier,Name,HardwareType FROM Hardware"))
                    if (!hardware.TryAdd(Text(row[0]), (Text(row[1]), Text(row[2])))) throw new InvalidDataException("Duplicate WMI hardware identity.");
                var values = new List<TemperatureSensor>();
                foreach (var row in Query(scope, "SELECT Parent,Name,Value FROM Sensor WHERE SensorType='Temperature'"))
                {
                    var parent = Text(row[0]); var label = Text(row[1]);
                    if (row[2] is not (null or float or double or int or uint or short or ushort or long or ulong)) throw new InvalidDataException("WMI temperature must be numeric.");
                    if (hardware.TryGetValue(parent, out var device)) values.Add(new(parent, device.Name, device.Type, label, row[2] == null ? null : Convert.ToDouble(row[2], CultureInfo.InvariantCulture)));
                }
                Sensors = values.ToArray(); _namespace = name; Status = TemperatureReading.Missing(TemperatureState.SensorUnavailable, name, at: now); return;
            }
            catch (ManagementException error)
            {
                Status = TemperatureReading.Missing(error.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass ? TemperatureState.ProviderMissing :
                    error.ErrorCode == ManagementStatus.AccessDenied ? TemperatureState.AccessDenied : TemperatureState.ReadFailed, name, code: error.ErrorCode.ToString(), at: now);
            }
            catch (UnauthorizedAccessException) { Status = TemperatureReading.Missing(TemperatureState.AccessDenied, name, at: now); }
            catch (COMException error) { Status = TemperatureReading.Missing(TemperatureState.ReadFailed, name, code: "HRESULT 0x" + error.HResult.ToString("x8", CultureInfo.InvariantCulture), at: now); }
            catch (InvalidDataException) { Status = TemperatureReading.Missing(TemperatureState.InvalidData, name, at: now); }
            failures.Add(Status);
        }
        Status = failures.FirstOrDefault(r => r.State == TemperatureState.AccessDenied) ?? failures.FirstOrDefault(r => r.State == TemperatureState.InvalidData) ??
            failures.FirstOrDefault(r => r.State == TemperatureState.ReadFailed) ?? failures[0];
        _namespace = null; _next = now.AddSeconds(60);
    }
    private static string Text(object? value) => value is string text && text.Length is > 0 and <= 256 ? text : throw new InvalidDataException("Invalid WMI sensor metadata.");
    private static object?[][] Query(ManagementScope scope, string query)
    {
        using var search = new ManagementObjectSearcher(scope, new ObjectQuery(query), new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromMilliseconds(500) });
        using var rows = search.Get(); var values = new List<object?[]>();
        var properties = query.Contains("FROM Hardware", StringComparison.Ordinal) ? new[] { "Identifier", "Name", "HardwareType" } : new[] { "Parent", "Name", "Value" };
        foreach (ManagementBaseObject row in rows)
        {
            using (row)
            {
                if (values.Count >= 256) throw new InvalidDataException("Too many WMI sensor rows.");
                values.Add(properties.Select(property => row[property]).ToArray());
            }
        }
        return values.ToArray();
    }
}
