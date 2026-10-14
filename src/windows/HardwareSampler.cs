using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class HardwareResourceMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly HardwareHistory _history = new();
    private Task? _run;
    private HardwareUsage _current = HardwareUsage.Pending;
    public HardwareUsage Current => Volatile.Read(ref _current);
    public event Action<HardwareUsage>? Changed;
    public void Start() { _run ??= Task.Run(RunAsync); }
    private async Task RunAsync()
    {
        using var sampler = new HardwareSampler();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var usage = _history.Observe(sampler.Sample()); Volatile.Write(ref _current, usage); Changed?.Invoke(usage);
                await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); if (_run != null) await _run; _stop.Dispose(); }
}

internal sealed class HardwareSampler : IDisposable
{
    private const uint DoubleFormat = 0x200, MoreData = 0x800007d2;
    private readonly IntPtr _query, _cpu, _engines, _vram;
    private readonly uint _openStatus;
    private int _collections;
    private DateTimeOffset _adapterRefresh;
    private (string Id, string Name, ulong Capacity)[] _adapters = Array.Empty<(string, string, ulong)>();
    private readonly WindowsGpuTemperature _gpuTemperature = new();
    private readonly WindowsWmiTemperature _wmiTemperature = new();
    public HardwareSampler()
    {
        _openStatus = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (_openStatus == 0)
        {
            _cpu = Add(@"\Processor Information(_Total)\% Processor Time");
            _engines = Add(@"\GPU Engine(*)\Utilization Percentage");
            _vram = Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
        }
    }
    private IntPtr Add(string path) => PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;
    public HardwareUsage Sample()
    {
        var watch = Stopwatch.StartNew(); var now = DateTimeOffset.UtcNow;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ram = GlobalMemoryStatusEx(ref memory) ? new ResourceReading(HardwareMath.Percentage(memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical),
            memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical) : new ResourceReading(null, Problem: "메모리 조회 불가");
        var collected = _openStatus == 0 && PdhCollectQueryData(_query) == 0;
        if (collected) _collections++;
        var cpu = collected && _collections > 1 ? ReadCpu() : new ResourceReading(null, Problem: collected ? "사용률 측정 중…" : "성능 카운터 조회 불가");
        if (now >= _adapterRefresh) { _adapters = ReadAdapters(); _adapterRefresh = now.AddMinutes(1); }
        var engineReadings = collected && _collections > 1 ? ReadArray(_engines) : Array.Empty<(string, double)>();
        var engineUsage = HardwareMath.EngineUsage(engineReadings);
        var memoryUsage = new Dictionary<string, ulong>();
        foreach (var (name, value) in collected ? ReadArray(_vram) : Array.Empty<(string, double)>())
        {
            var instance = HardwareMath.ParseInstance(name);
            if (instance == null || !double.IsFinite(value) || value < 0 || value >= ulong.MaxValue) continue;
            var id = instance.Value.Adapter;
            memoryUsage[id] = checked(memoryUsage.GetValueOrDefault(id) + (ulong)value);
        }
        _wmiTemperature.Refresh(now);
        var wmi = _wmiTemperature;
        cpu = cpu with { Temperature = wmi.Sensors.Length == 0 ? wmi.Status : TemperatureSensors.Resource(wmi.Sensors, "Cpu", wmi.Source, wmi.CheckedAt) };
        ram = ram with { Temperature = wmi.Sensors.Length == 0 ? wmi.Status : TemperatureSensors.Resource(wmi.Sensors, "Memory", wmi.Source, wmi.CheckedAt) };
        var adapters = _adapters.Select(a => new GpuResource(a.Id, a.Name, a.Capacity,
            engineUsage.TryGetValue(a.Id, out var busy) ? busy : null, memoryUsage.TryGetValue(a.Id, out var bytes) ? bytes : null)).ToArray();
        for (var index = 0; index < adapters.Length; index++)
        {
            var adapter = adapters[index]; var temperatures = _gpuTemperature.Read(adapter.Id);
            var core = temperatures.Core; var gpuMemory = temperatures.Memory;
            if (core.State != TemperatureState.Available && wmi.Sensors.Length > 0) core = TemperatureSensors.Gpu(wmi.Sensors, adapters, adapter, false, wmi.Source, wmi.CheckedAt);
            if (gpuMemory.State != TemperatureState.Available && wmi.Sensors.Length > 0) gpuMemory = TemperatureSensors.Gpu(wmi.Sensors, adapters, adapter, true, wmi.Source, wmi.CheckedAt);
            adapters[index] = adapter with { Temperature = core, MemoryTemperature = gpuMemory };
        }
        var busiest = adapters.Where(a => a.Percent != null).OrderByDescending(a => a.Percent).FirstOrDefault();
        var fullest = adapters.Where(a => a.UsedBytes != null && HardwareMath.Percentage(a.UsedBytes.Value, a.Capacity) != null)
            .OrderByDescending(a => HardwareMath.Percentage(a.UsedBytes!.Value, a.Capacity)).FirstOrDefault();
        var gpu = busiest == null ? new ResourceReading(null, Problem: "GPU 조회 불가") : new ResourceReading(busiest.Percent, Device: busiest.Name, Temperature: busiest.Temperature);
        var vram = fullest == null ? new ResourceReading(null, Problem: "VRAM 조회 불가") : new ResourceReading(HardwareMath.Percentage(fullest.UsedBytes!.Value, fullest.Capacity), fullest.UsedBytes, fullest.Capacity, fullest.Name, Temperature: fullest.MemoryTemperature);
        return new(now, cpu, ram, gpu, vram, adapters, watch.Elapsed.TotalMilliseconds);
    }
    private ResourceReading ReadCpu()
    {
        if (_cpu == IntPtr.Zero) return new(null, Problem: "성능 카운터 조회 불가");
        var result = PdhGetFormattedCounterValue(_cpu, DoubleFormat, out _, out var reading);
        return result == 0 && reading.Status <= 1 && double.IsFinite(reading.Value) && reading.Value is >= 0 and <= 100
            ? new(reading.Value) : new(null, Problem: "성능 카운터 조회 불가");
    }
    private static (string Name, double Value)[] ReadArray(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return Array.Empty<(string, double)>();
        uint size = 0, count = 0;
        var status = PdhGetFormattedCounterArrayW(counter, DoubleFormat, ref size, ref count, IntPtr.Zero);
        for (var attempt = 0; status == MoreData && size > 0 && attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, DoubleFormat, ref size, ref count, buffer);
                if (status != 0) continue;
                var readings = new List<(string, double)>(); var stride = Marshal.SizeOf<CounterItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, checked(i * stride)));
                    if (item.Value.Status <= 1 && Marshal.PtrToStringUni(item.Name) is string name) readings.Add((name, item.Value.Value));
                }
                return readings.ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return Array.Empty<(string, double)>();
    }
    private static (string Id, string Name, ulong Capacity)[] ReadAdapters()
    {
        var interfaceId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref interfaceId, out var factory) < 0) return Array.Empty<(string, string, ulong)>();
        try
        {
            var enumerate = GetMethod<EnumAdapters>(factory, 12); var adapters = new List<(string, string, ulong)>();
            for (uint index = 0; ; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0) break;
                try
                {
                    if (GetMethod<GetDescription>(adapter, 10)(adapter, out var description) < 0 || (description.Flags & 2) != 0) continue;
                    adapters.Add((description.LuidHigh.ToString("x8") + ":" + description.LuidLow.ToString("x8"), description.Description.Trim(), description.DedicatedVideoMemory));
                }
                finally { Marshal.Release(adapter); }
            }
            return adapters.ToArray();
        }
        finally { Marshal.Release(factory); }
    }
    private static T GetMethod<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));
    public void Dispose() { _gpuTemperature.Dispose(); if (_query != IntPtr.Zero) PdhCloseQuery(_query); }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, SubSystem, Revision;
        public ulong DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid interfaceId, out IntPtr factory);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, ref uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
