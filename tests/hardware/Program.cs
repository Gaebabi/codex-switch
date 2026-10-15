using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodexAccountDesk;

var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Hardware service contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Unsupported(Action action)
{ try { action(); } catch (PlatformNotSupportedException) { return; } throw new InvalidOperationException("Wrong-OS native entry point did not reject."); }
void Validate(HardwareUsage usage)
{
    Assert(usage.CheckedAt > DateTimeOffset.MinValue && double.IsFinite(usage.SampleMilliseconds) && usage.SampleMilliseconds >= 0);
    foreach (var value in new[] { usage.Cpu, usage.Memory, usage.Gpu, usage.Vram })
    {
        Assert(value.Percent == null || double.IsFinite(value.Percent.Value) && value.Percent >= 0 && value.Percent <= 100);
        if (value.Temperature?.State == TemperatureState.Available) Assert(value.Temperature.Celsius.HasValue && TemperatureReading.Valid(value.Temperature.Celsius.Value));
        else Assert(value.Temperature?.Celsius == null);
    }
    Assert(usage.Memory.UsedBytes.HasValue && usage.Memory.TotalBytes > 0 && usage.Memory.UsedBytes <= usage.Memory.TotalBytes);
}
Check("Collectors share one UI-independent hardware assembly", () =>
{
    var assembly = typeof(DriveMonitor).Assembly;
    Assert(assembly.GetName().Name == "CodexSwitch.Hardware" && typeof(HardwareSampler).Assembly == assembly && typeof(MacResourceSampler).Assembly == assembly &&
        typeof(TemperatureSensors).Assembly == assembly && typeof(WindowsDiskTemperature).Assembly == assembly);
    Assert(!assembly.GetReferencedAssemblies().Any(a => a.Name is "CodexSwitch" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
});
Check("Drive preferences preserve existing JSON field names, values and defaults", () =>
{
    Assert(JsonSerializer.Serialize(new DrivePreference()) == "{\"Root\":\"\",\"Label\":\"\"}");
    const string json = "{\"Root\":\"/\",\"Label\":\"fixture volume\"}";
    Assert(JsonSerializer.Serialize(JsonSerializer.Deserialize<DrivePreference>(json)) == json);
});
await using var drives = new DriveMonitor();
Check("Unstarted drives stay unavailable and malformed roots reject before collection", () =>
{
    Assert(drives.Current.Length == 0);
    try { drives.Configure(new[] { new DrivePreference { Root = "../private" } }); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Relative root accepted.");
});
await using var windows = new HardwareResourceMonitor();
await using var mac = new MacResourceMonitor();
Check("Unstarted native monitors preserve pending unknown readings", () => Assert(windows.Current.Cpu.Percent == null && windows.Current.Memory.Percent == null && mac.Current.Cpu.Percent == null && mac.Current.Memory.Percent == null));
Check("Malformed Windows drive root stays invalid on every OS", () => Assert(WindowsDiskTemperature.Read("../").State == TemperatureState.InvalidData));
if (OperatingSystem.IsWindows())
{
    Check("Native Windows read-only sample preserves actual memory and bounded-or-unknown usage", () => { using var sampler = new HardwareSampler(); Validate(sampler.Sample()); });
}
else
{
    Check("Windows monitor refuses synchronously before background native sampling", () => Unsupported(windows.Start));
    Check("Windows sampler/GPU/WMI refuse before native library access", () =>
    { Unsupported(() => new HardwareSampler()); Unsupported(() => new WindowsGpuTemperature()); Unsupported(() => new WindowsWmiTemperature().Refresh(DateTimeOffset.UtcNow)); });
    Check("Windows disk temperature reports explicit unsupported without Celsius", () =>
    { var value = WindowsDiskTemperature.Read("C:\\"); Assert(value.State == TemperatureState.PlatformUnsupported && value.Celsius == null); });
}
if (OperatingSystem.IsMacOS())
{
    Check("Native Mac read-only sample preserves actual memory and unsupported temperature", () =>
    { using var sampler = new MacResourceSampler(); var usage = sampler.Sample(); Validate(usage); Assert(new[] { usage.Cpu, usage.Memory, usage.Gpu, usage.Vram }.All(r => r.Temperature?.State == TemperatureState.PlatformUnsupported)); });
}
else
{
    Check("Mac monitor refuses synchronously before background native sampling", () => Unsupported(mac.Start));
    Check("Mac sampler refuses before acquiring a Mach host port", () => Unsupported(() => new MacResourceSampler()));
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks,
    scope = "Production hardware module, actual supported-OS read-only sampler, JSON compatibility and wrong-OS rejection; no device names, personal homes, credentials or client/account actions exported." }));
