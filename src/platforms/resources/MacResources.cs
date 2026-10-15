using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class MacResourceMonitor : IResourceMonitor
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;
    private HardwareUsage _current = HardwareUsage.Pending;
    public HardwareUsage Current => Volatile.Read(ref _current);
    public event Action? Changed;
    public void Start()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Mac resource monitoring requires macOS.");
        _run ??= Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        using var sampler = new MacResourceSampler(); var history = new HardwareHistory();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                Volatile.Write(ref _current, history.Observe(sampler.Sample())); Changed?.Invoke();
                await Task.Delay(2000, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); if (_run != null) await _run; _stop.Dispose(); }
}

internal sealed class MacResourceSampler : IDisposable
{
    private const string System = "/usr/lib/libSystem.B.dylib";
    private const string Iokit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private readonly uint _host;
    private uint[]? _lastCpu;
    private GpuResource[] _gpu = Array.Empty<GpuResource>();
    private DateTimeOffset _lastCapacity;
    private readonly Dictionary<string, ulong> _capacities = new();
    public MacResourceSampler()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Mac resource sampling requires macOS.");
        _host = mach_host_self();
    }
    public HardwareUsage Sample()
    {
        var clock = Stopwatch.StartNew(); var now = DateTimeOffset.UtcNow;
        var unavailable = TemperatureReading.Missing(TemperatureState.PlatformUnsupported, "macOS", at: now);
        var cpu = Cpu() with { Temperature = unavailable }; var memory = Memory() with { Temperature = unavailable }; _gpu = Gpu(now);
        var busiest = _gpu.Where(g => g.Percent.HasValue).OrderByDescending(g => g.Percent).FirstOrDefault();
        var fullest = _gpu.Where(g => g.UsedBytes.HasValue && g.Capacity > 0).OrderByDescending(g => HardwareMath.Percentage(g.UsedBytes!.Value, g.Capacity)).FirstOrDefault();
        return new(now, cpu, memory, new(busiest?.Percent, Device: busiest?.Name, Problem: busiest == null ? "GPU 사용률을 제공하지 않는 드라이버입니다." : null, Temperature: unavailable),
            fullest == null ? new(null, Problem: "전용 VRAM 정보가 없습니다. Apple Silicon은 통합 메모리를 사용합니다.", Temperature: unavailable) :
                new(HardwareMath.Percentage(fullest.UsedBytes!.Value, fullest.Capacity), fullest.UsedBytes, fullest.Capacity, fullest.Name, Temperature: unavailable), _gpu, clock.Elapsed.TotalMilliseconds);
    }
    private ResourceReading Cpu()
    {
        var ticks = new uint[4]; uint count = 4;
        if (host_statistics(_host, 3, ticks, ref count) != 0 || count != 4) return new(null, Problem: "CPU 수치를 읽지 못했습니다.");
        var previous = _lastCpu; _lastCpu = ticks;
        if (previous == null) return new(null);
        ulong total = 0; uint idle = 0;
        for (var i = 0; i < 4; i++) { var delta = unchecked(ticks[i] - previous[i]); total += delta; if (i == 2) idle = delta; }
        return new(total == 0 ? null : 100.0 * (total - idle) / total);
    }
    private ResourceReading Memory()
    {
        ulong total = 0; nuint length = 8; var statistics = new uint[64]; uint count = 64;
        if (sysctlbyname("hw.memsize", ref total, ref length, IntPtr.Zero, 0) != 0 || host_page_size(_host, out var pageSize) != 0 ||
            host_statistics64(_host, 4, statistics, ref count) != 0 || count < 34) return new(null, Problem: "메모리 수치를 읽지 못했습니다.");
        var available = ((ulong)statistics[0] + statistics[2]) * pageSize;
        if (available > total) return new(null, Problem: "메모리 수치를 읽지 못했습니다.");
        var used = total - available;
        return new(HardwareMath.Percentage(used, total), used, total, "MEM", "회수 가능한 free/inactive 메모리를 제외한 물리 사용량입니다.");
    }
    private GpuResource[] Gpu(DateTimeOffset now)
    {
        var devices = new List<GpuResource>();
        if (IOServiceGetMatchingServices(0, IOServiceMatching("IOAccelerator"), out var iterator) != 0) return Array.Empty<GpuResource>();
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (IORegistryEntryCreateCFProperties(service, out var properties, IntPtr.Zero, 0) != 0) continue;
                    try
                    {
                        IORegistryEntryGetRegistryEntryID(service, out var id);
                        var nameBuffer = new byte[128]; IORegistryEntryGetName(service, nameBuffer);
                        var name = Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0'); var key = id.ToString();
                        var stats = MacCf.Value(properties, "PerformanceStatistics");
                        if (!MacCf.IsDictionary(stats)) { devices.Add(new(key, name, 0, null, null)); continue; }
                        var percent = MacCf.Number(stats, "Device Utilization %") ?? MacCf.Number(stats, "GPU Activity(%)");
                        if (percent is < 0 or > 100 || percent.HasValue && !double.IsFinite(percent.Value)) percent = null;
                        var rawUsed = MacCf.Number(stats, "vramUsedBytes") ?? MacCf.Number(stats, "In use vid memory");
                        ulong? used = rawUsed is >= 0 && rawUsed < ulong.MaxValue ? (ulong)rawUsed.Value : null;
                        if (!_capacities.ContainsKey(key) || now - _lastCapacity >= TimeSpan.FromSeconds(60)) _capacities[key] = Capacity(service);
                        devices.Add(new(key, name, _capacities[key], percent, used));
                    }
                    finally { MacCf.Release(properties); }
                }
                finally { IOObjectRelease(service); }
            }
            if (now - _lastCapacity >= TimeSpan.FromSeconds(60)) _lastCapacity = now;
        }
        finally { IOObjectRelease(iterator); }
        return devices.ToArray();
    }
    private static ulong Capacity(uint entry)
    {
        var current = entry; var owned = false;
        try
        {
            for (var depth = 0; depth < 6; depth++)
            {
                if (IORegistryEntryCreateCFProperties(current, out var properties, IntPtr.Zero, 0) == 0)
                {
                    try
                    {
                        var value = MacCf.Value(properties, "VRAM,totalMB"); var megabytes = MacCf.Number(value);
                        if (MacCf.IsData(value))
                        {
                            var bytes = MacCf.Bytes(value);
                            megabytes = bytes.Length == 4 ? BitConverter.ToUInt32(bytes) : bytes.Length == 8 ? BitConverter.ToUInt64(bytes) : null;
                        }
                        if (megabytes is > 0 && megabytes < ulong.MaxValue / 1048576.0) return (ulong)(megabytes.Value * 1048576);
                    }
                    finally { MacCf.Release(properties); }
                }
                if (IORegistryEntryGetParentEntry(current, "IOService", out var parent) != 0) break;
                if (owned) IOObjectRelease(current); current = parent; owned = true;
            }
            return 0;
        }
        finally { if (owned) IOObjectRelease(current); }
    }
    public void Dispose()
    {
        var system = NativeLibrary.Load(System);
        try { mach_port_deallocate((uint)Marshal.ReadInt32(NativeLibrary.GetExport(system, "mach_task_self_")), _host); }
        finally { NativeLibrary.Free(system); }
    }
    [DllImport(System)] private static extern uint mach_host_self();
    [DllImport(System)] private static extern int mach_port_deallocate(uint task, uint port);
    [DllImport(System)] private static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref uint count);
    [DllImport(System)] private static extern int host_statistics64(uint host, int flavor, [Out] uint[] info, ref uint count);
    [DllImport(System)] private static extern int host_page_size(uint host, out uint pageSize);
    [DllImport(System)] private static extern int sysctlbyname([MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref ulong value, ref nuint length, IntPtr input, nuint inputLength);
    [DllImport(Iokit)] private static extern IntPtr IOServiceMatching([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Iokit)] private static extern int IOServiceGetMatchingServices(uint port, IntPtr matching, out uint iterator);
    [DllImport(Iokit)] private static extern uint IOIteratorNext(uint iterator);
    [DllImport(Iokit)] private static extern int IOObjectRelease(uint handle);
    [DllImport(Iokit)] private static extern int IORegistryEntryCreateCFProperties(uint entry, out IntPtr properties, IntPtr allocator, uint options);
    [DllImport(Iokit)] private static extern int IORegistryEntryGetRegistryEntryID(uint entry, out ulong id);
    [DllImport(Iokit)] private static extern int IORegistryEntryGetName(uint entry, [Out] byte[] name);
    [DllImport(Iokit)] private static extern int IORegistryEntryGetParentEntry(uint entry, [MarshalAs(UnmanagedType.LPUTF8Str)] string plane, out uint parent);
}
