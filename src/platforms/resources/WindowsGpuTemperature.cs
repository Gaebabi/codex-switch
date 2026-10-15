using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal sealed class WindowsGpuTemperature : IDisposable
{
    private readonly IntPtr _nvml;
    private readonly Shutdown? _shutdown;
    private readonly DeviceCount? _deviceCount;
    private readonly DeviceByIndex? _deviceByIndex;
    private readonly PciInformation? _pciInformation;
    private readonly FieldValues? _fieldValues;
    private readonly bool _initialized;
    private readonly Dictionary<string, TemperatureLimits?> _limits = new();
    public WindowsGpuTemperature()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows GPU temperature requires Windows.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
        if (!File.Exists(path)) return;
        _nvml = LoadLibraryExW(path, IntPtr.Zero, 0x900);
        if (_nvml == IntPtr.Zero) return;
        var initialize = Export<Initialize>("nvmlInit_v2"); _shutdown = Export<Shutdown>("nvmlShutdown");
        _deviceCount = Export<DeviceCount>("nvmlDeviceGetCount_v2"); _deviceByIndex = Export<DeviceByIndex>("nvmlDeviceGetHandleByIndex_v2");
        _pciInformation = Export<PciInformation>("nvmlDeviceGetPciInfo_v3"); _fieldValues = Export<FieldValues>("nvmlDeviceGetFieldValues");
        _initialized = initialize != null && _shutdown != null && _deviceCount != null && _deviceByIndex != null && _pciInformation != null && _fieldValues != null && initialize() == 0;
    }
    private T? Export<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(_nvml, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
    public (TemperatureReading Core, TemperatureReading Memory) Read(string id, uint vendor)
    {
        var at = DateTimeOffset.UtcNow;
        var missing = TemperatureReading.Missing(TemperatureState.SensorUnavailable, "Windows GPU driver", at: at);
        if (id.Length != 17 || id[8] != ':' || !uint.TryParse(id.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high) ||
            !uint.TryParse(id.AsSpan(9, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)) return (missing, missing);
        var open = new OpenAdapter { Low = low, High = high };
        var status = D3DKMTOpenAdapterFromLuid(ref open);
        if (status != 0) return (Failure(status, at), missing);
        try
        {
            var count = 0u;
            if (Query(open.Handle, 30, ref count) != 0 || count != 1) return (missing, missing);
            var data = new AdapterPerformance(); status = Query(open.Handle, 62, ref data);
            var core = status == 0 && data.Temperature > 0 ? TemperatureReading.From(data.Temperature / 10.0, "Windows GPU driver", id, at) : status != 0 ? Failure(status, at) : missing;
            if (!_limits.TryGetValue(id, out var limits))
            {
                var caps = new AdapterCapabilities();
                limits = Query(open.Handle, 63, ref caps) == 0 ? TemperatureLimits.Device(caps.TemperatureWarning / 10.0, caps.TemperatureMax / 10.0) : null;
                _limits[id] = limits;
            }
            core = core with { Limits = limits };
            var memory = missing;
            var address = new AdapterAddress();
            if (vendor == 0x10de && _initialized && Query(open.Handle, 6, ref address) == 0)
            {
                if (Device(address, out var device))
                {
                    var field = new NvmlField { Id = 82 };
                    var queryStatus = _fieldValues!(device, 1, ref field);
                    if (queryStatus == 0 && field.Status == 0)
                    {
                        double? value = field.Type switch { 0 => BitConverter.Int64BitsToDouble(unchecked((long)field.Value)), 1 or 2 => (uint)field.Value,
                            3 => field.Value, 4 => unchecked((long)field.Value), 5 => unchecked((int)field.Value), 6 => (ushort)field.Value, _ => null };
                        memory = value.HasValue ? TemperatureReading.From(value.Value, "NVIDIA NVML · VRAM", id, at) : TemperatureReading.Missing(TemperatureState.InvalidData, "NVIDIA NVML", at: at);
                    }
                    else memory = TemperatureReading.Missing(queryStatus == 4 || field.Status == 4 ? TemperatureState.AccessDenied : TemperatureState.SensorUnavailable,
                        "NVIDIA NVML · VRAM", code: "NVML " + (queryStatus == 0 ? field.Status : queryStatus), at: at);
                }
            }
            return (core, memory);
        }
        finally { var handle = open.Handle; D3DKMTCloseAdapter(ref handle); }
    }
    private bool Device(AdapterAddress address, out IntPtr selected)
    {
        selected = IntPtr.Zero;
        if (_deviceCount!(out var count) != 0 || count > 64) return false;
        var matches = 0;
        for (uint index = 0; index < count; index++)
        {
            if (_deviceByIndex!(index, out var device) != 0 || _pciInformation!(device, out var pci) != 0) return false;
            var dot = pci.BusId.LastIndexOf('.');
            if (dot < 0 || !uint.TryParse(pci.BusId.AsSpan(dot + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var function)) return false;
            if (pci.Bus == address.Bus && pci.Device == address.Device && function == address.Function) { selected = device; matches++; }
        }
        return matches == 1;
    }
    private static TemperatureReading Failure(int status, DateTimeOffset at) => TemperatureReading.Missing(status == unchecked((int)0xc0000022) ? TemperatureState.AccessDenied : TemperatureState.SensorUnavailable,
        "Windows GPU driver", code: "NTSTATUS 0x" + unchecked((uint)status).ToString("x8", CultureInfo.InvariantCulture), at: at);
    private static int Query<T>(uint handle, int type, ref T value) where T : struct
    {
        var size = Marshal.SizeOf<T>(); var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            var request = new QueryAdapter { Handle = handle, Type = type, Data = pointer, Size = (uint)size };
            var status = D3DKMTQueryAdapterInfo(ref request);
            if (status == 0) value = Marshal.PtrToStructure<T>(pointer);
            return status;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public void Dispose() { if (_initialized) _shutdown!(); if (_nvml != IntPtr.Zero) NativeLibrary.Free(_nvml); }
    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { public uint Low, High, Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct QueryAdapter { public uint Handle; public int Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterAddress { public uint Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterPerformance
    { public uint PhysicalAdapter; public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOc, MemoryBandwidth, PcieBandwidth; public uint FanRpm, Power, Temperature; public byte PowerOverride; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterCapabilities
    { public uint PhysicalAdapter; public ulong MaxMemoryBandwidth, MaxPcieBandwidth; public uint MaxFanRpm, TemperatureMax, TemperatureWarning; }
    [StructLayout(LayoutKind.Sequential)] private struct NvmlField { public uint Id, Scope; public long Timestamp, Latency; public int Type, Status; public ulong Value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] private struct PciInfo
    { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string LegacyId; public uint Domain, Bus, Device, DeviceId, SubSystemId; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceByIndex(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PciInformation(IntPtr device, out PciInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FieldValues(IntPtr device, int count, ref NvmlField values);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapter query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint adapter);
}
