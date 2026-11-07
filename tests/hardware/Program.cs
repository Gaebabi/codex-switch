using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Buffers.Binary;
using System.Threading.Tasks;
using CodexAccountDesk;

var checks = new List<string>();
object? nativeDiskIo = null;
var ioAt = DateTimeOffset.UtcNow;
DiskIoReading SampleIo(double read = 1024, double write = 512, double readMs = 2, double writeMs = 3, double busy = 90, DiskIoProfile profile = DiskIoProfile.NoSeekPenalty) =>
    new(ioAt, DiskIoState.Available, profile, new(read, readMs), new(write, writeMs), busy);
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
Check("Drive cache identity retains native case sensitivity on Mac and Linux", () =>
{
    var cache = new Dictionary<string,int>(DriveMonitor.RootComparer) { ["/media/A"] = 1, ["/media/a"] = 2 };
    Assert(cache.Count == (OperatingSystem.IsWindows() ? 1 : 2));
    if (!OperatingSystem.IsWindows()) drives.Configure(new[]{new DrivePreference{Root="/media/A"},new DrivePreference{Root="/media/a"}});
});
Check("Disk I/O uses real capability bytes, not model strings or presumed interface speed", () =>
{
    var seek=new byte[12];var device=new byte[40];
    BinaryPrimitives.WriteUInt32LittleEndian(seek,12);BinaryPrimitives.WriteUInt32LittleEndian(seek.AsSpan(4),12);
    BinaryPrimitives.WriteUInt32LittleEndian(device,40);BinaryPrimitives.WriteUInt32LittleEndian(device.AsSpan(4),40);BinaryPrimitives.WriteUInt32LittleEndian(device.AsSpan(28),17);
    Assert(WindowsDiskCapabilities.Parse(seek,device)==DiskIoProfile.NoSeekPenalty);seek[8]=1;
    Assert(WindowsDiskCapabilities.Parse(seek,device)==DiskIoProfile.SeekPenalty);seek[8]=2;Assert(WindowsDiskCapabilities.Parse(seek,device)==DiskIoProfile.Unknown);
    seek[8]=0;BinaryPrimitives.WriteUInt32LittleEndian(device.AsSpan(28),14);Assert(WindowsDiskCapabilities.Parse(seek,device)==DiskIoProfile.Unknown);
    Assert(WindowsDiskCapabilities.Parse(seek[..8],device)==DiskIoProfile.Unknown);
});
Check("Unknown or mixed backing devices never invent a performance profile", () =>
{
    Assert(WindowsDiskCapabilities.Combine(Array.Empty<DiskIoProfile>())==DiskIoProfile.Unknown);
    Assert(WindowsDiskCapabilities.Combine(new[]{DiskIoProfile.NoSeekPenalty,DiskIoProfile.Unknown})==DiskIoProfile.Unknown);
    Assert(WindowsDiskCapabilities.Combine(new[]{DiskIoProfile.NoSeekPenalty,DiskIoProfile.SeekPenalty})==DiskIoProfile.Mixed);
    Assert(WindowsDiskCapabilities.Combine(new[]{DiskIoProfile.SeekPenalty,DiskIoProfile.SeekPenalty})==DiskIoProfile.SeekPenalty);
});
Check("Disk I/O rates preserve idle zero, binary units and unknown states", () =>
{
    Assert(DiskIoReading.RateLabel(0)=="0 B/s"&&DiskIoReading.RateLabel(1024)=="1 KiB/s"&&DiskIoReading.RateLabel(1048576)=="1 MiB/s");
    Assert(DiskIoReading.RateLabel(-1)=="—"&&DiskIoReading.RateLabel(double.NaN)=="—"&&DiskIoReading.RateLabel(null)=="—");
});
Check("Read/write bars share a real observed scale without calling it rated capacity", () =>
{
    var history=new DiskIoHistory();var first=history.Observe(SampleIo());Assert(first.ScaleBytesPerSecond==1024);
    Assert(DiskIoReading.Fill(first,false,ioAt)==1&&DiskIoReading.Fill(first,true,ioAt)==0.5);
    var peak=history.Observe(SampleIo(4096,2048) with {CheckedAt=ioAt.AddSeconds(2)});Assert(peak.ScaleBytesPerSecond==4096);
    var next=history.Observe(SampleIo() with {CheckedAt=ioAt.AddSeconds(4)});Assert(next.ScaleBytesPerSecond==4096);
});
Check("Expired peak windows and collection gaps reset the scale instead of retaining old hardware", () =>
{
    var history=new DiskIoHistory();history.Observe(SampleIo(8192));DiskIoReading value=SampleIo();
    for(var t=2;t<=64;t+=2)value=history.Observe(SampleIo() with{CheckedAt=ioAt.AddSeconds(t)});
    Assert(value.ScaleBytesPerSecond==1024);
    value=history.Observe(SampleIo(512,256) with{CheckedAt=ioAt.AddSeconds(100)});Assert(value.ScaleBytesPerSecond==1024&&value.Read.Pressure==ResourcePressure.Normal);
});
Check("Fast throughput alone does not trigger danger; transient latency spikes stay normal", () =>
{
    var history=new DiskIoHistory();var value=history.Observe(SampleIo(5e9,3e9,2,3));Assert(value.Read.Pressure==ResourcePressure.Normal);
    value=history.Observe(SampleIo(4096,2048,100,100) with{CheckedAt=ioAt.AddSeconds(2)});Assert(value.Read.Pressure==ResourcePressure.Normal);
    value=history.Observe(SampleIo() with{CheckedAt=ioAt.AddSeconds(4)});Assert(value.Read.Pressure==ResourcePressure.Normal);
});
Check("Each channel needs sixty sustained seconds and actual busy time for latency pressure", () =>
{
    var history=new DiskIoHistory();DiskIoReading value=SampleIo();
    for(var t=0;t<=60;t+=2)value=history.Observe(SampleIo(1024,512,15,25) with{CheckedAt=ioAt.AddSeconds(t)});
    Assert(value.Read.Pressure==ResourcePressure.Warning&&value.Write.Pressure==ResourcePressure.Critical);
    value=history.Observe(SampleIo() with{CheckedAt=ioAt.AddSeconds(62)});Assert(value.Read.Pressure==ResourcePressure.Normal&&value.Write.Pressure==ResourcePressure.Normal);
});
Check("The real seek-penalty profile selects different documented latency policy", () =>
{
    var history=new DiskIoHistory();DiskIoReading value=SampleIo();
    for(var t=0;t<=60;t+=2)value=history.Observe(SampleIo(readMs:20,writeMs:30,profile:DiskIoProfile.SeekPenalty) with{CheckedAt=ioAt.AddSeconds(t)});
    Assert(value.Read.Pressure==ResourcePressure.Normal&&value.Write.Pressure==ResourcePressure.Warning);
});
Check("Unknown/mixed capabilities and missing busy/latency never claim a safe or dangerous state", () =>
{
    foreach(var profile in new[]{DiskIoProfile.Unknown,DiskIoProfile.Mixed})
    {var history=new DiskIoHistory();var value=history.Observe(SampleIo(readMs:500,writeMs:500,profile:profile));Assert(value.Read.Pressure==ResourcePressure.Unknown);}
    Assert(new DiskIoHistory().Observe(SampleIo() with{BusyPercent=null}).Read.Pressure==ResourcePressure.Unknown);
});
Check("Invalid and stale disk readings cannot retain active bars or prior pressure", () =>
{
    foreach(var sample in new[]{SampleIo(-1),SampleIo(double.NaN),SampleIo(readMs:-1),SampleIo(busy:101)})
    {var value=new DiskIoHistory().Observe(sample);Assert(value.State==DiskIoState.InvalidData&&value.Read.BytesPerSecond==null);}
    var good=new DiskIoHistory().Observe(SampleIo());Assert(DiskIoReading.Fill(good,false,ioAt.AddSeconds(11))==0);
    Assert(!DiskIoReading.Fresh(good,ioAt.AddSeconds(-6)));
});
Check("All nine catalogues explain throughput, profile and policy without losing Celsius", () =>
{
    foreach(var language in new[]{"en","ko","ja","zh-CN","zh-TW","es","fr","de","pt-BR"})
    {Loc.SetLanguage(language);var text=DiskIoReading.Tooltip(new DiskIoHistory().Observe(SampleIo()),ioAt);Assert(text.Contains("MiB/s")||text.Contains("KiB/s"));Assert(!text.Contains("io.policy"));}
    Loc.SetLanguage("en");
});
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
    Check("Malformed disk I/O roots reject before native access",()=>{try{using var sampler=new WindowsDiskIoSampler();sampler.Sample(new[]{"../private"});}catch(ArgumentException){return;}throw new InvalidOperationException("Invalid I/O root accepted");});
    using(var io=new WindowsDiskIoSampler())
    {
        var root=Path.GetPathRoot(Environment.SystemDirectory)!;io.Sample(new[]{root});await Task.Delay(1100);var sample=io.Sample(new[]{root})[root];
        nativeDiskIo = new { state = sample.State.ToString(), profile = sample.Profile.ToString(), readAvailable = DiskIoReading.Rate(sample.Read.BytesPerSecond),
            writeAvailable = DiskIoReading.Rate(sample.Write.BytesPerSecond), busyAvailable = sample.BusyPercent.HasValue };
        Check("Actual local disk counters are read without generated I/O or device identities",()=>{Assert(sample.State is DiskIoState.Available or DiskIoState.Unavailable or DiskIoState.AccessDenied);if(sample.State==DiskIoState.Available)Assert(DiskIoReading.Rate(sample.Read.BytesPerSecond)&&DiskIoReading.Rate(sample.Write.BytesPerSecond));});
    }
    Check("Native Windows read-only sample preserves actual memory and bounded-or-unknown usage", () => { using var sampler = new HardwareSampler(); Validate(sampler.Sample()); });
}
else
{
    Check("Windows disk I/O refuses wrong-OS sampling before native access",()=>Unsupported(()=>{using var sampler=new WindowsDiskIoSampler();sampler.Sample(Array.Empty<string>());}));
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
var report = JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, nativeDiskIo,
    scope = "Production hardware module, actual supported-OS read-only sampler, JSON compatibility and wrong-OS rejection; no device names, personal homes, credentials or client/account actions exported." });
if (args.Length != 0)
{
    if (args.Length != 2 || args[0] != "--report") throw new ArgumentException("Use --report <output>.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);File.WriteAllText(args[1],report);
}
Console.WriteLine(report);
