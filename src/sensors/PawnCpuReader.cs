using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexSwitch.Sensors;

internal sealed class PawnCpuReader : IDisposable
{
    private readonly SafeFileHandle? _device;
    private readonly string _state;
    private readonly bool _amd, _intel, _package;
    private readonly Mutex? _pci;
    public PawnCpuReader()
    {
        _state = "PlatformUnsupported";
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || !X86Base.IsSupported) return;
        var (eax, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var vendor = Encoding.ASCII.GetString(BitConverter.GetBytes(ebx)) + Encoding.ASCII.GetString(BitConverter.GetBytes(edx)) + Encoding.ASCII.GetString(BitConverter.GetBytes(ecx));
        var processor = X86Base.CpuId(1, 0);
        var signature = processor.Eax;
        _amd = vendor == "AuthenticAMD" && CpuRegisters.Family(signature) is 0x17 or 0x19 or 0x1a;
        _intel = vendor == "GenuineIntel" && CpuRegisters.Family(signature) == 6 && eax >= 6 && (X86Base.CpuId(6, 0).Eax & 1) != 0;
        _package = _intel && (X86Base.CpuId(6, 0).Eax & (1 << 6)) != 0;
        if (!_amd && !_intel) return;
        _device = CreateFileW(@"\\?\GLOBALROOT\Device\PawnIO", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_device.IsInvalid)
        {
            _state = Marshal.GetLastWin32Error() switch { 2 or 3 => "ProviderMissing", 5 => "AccessDenied", _ => "ReadFailed" }; return;
        }
        try
        {
        using var module = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexSwitch.Sensors.modules." + (_amd ? "AMDFamily17.bin" : "IntelMSR.bin")) ??
            throw new InvalidDataException("Embedded CPU module is missing.");
        using var buffer = new MemoryStream(); module.CopyTo(buffer); var bytes = buffer.ToArray();
        var expected = _amd ? "CD59598344B54A23178CCF9223239F60A3111BD86FF2C7BEA2C9D1CEF89E095B" : "F3AB69F0A2686813DE2F47EFCDA6271E176C4EE50FAB5F107254F7D6A4E77361";
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expected) throw new InvalidDataException("CPU module hash mismatch.");
        if (!DeviceIoControl(_device, 0xa1b22084, bytes, bytes.Length, Array.Empty<byte>(), 0, out var loaded, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (loaded != 0) throw new InvalidDataException("Unexpected CPU module response.");
        if (_amd) _pci = new Mutex(false, @"Global\Access_PCI");
        _state = "Available";
        }
        catch { _device.Dispose(); throw; }
    }
    public CpuSample Read()
    {
        if (_state != "Available") return CpuSensorProtocol.Missing(_state);
        double? value; string name;
        if (_amd)
        {
            var owned = false;
            try
            {
                try { owned = _pci!.WaitOne(50); }
                catch (AbandonedMutexException) { owned = true; return CpuSensorProtocol.Missing("ReadFailed"); }
                if (!owned) return CpuSensorProtocol.Missing("SensorUnavailable");
                value = CpuRegisters.Amd((uint)Execute("ioctl_read_smn", 0x59800)); name = "AMD CPU (Tctl)";
            }
            finally { if (owned) _pci!.ReleaseMutex(); }
        }
        else
        {
            var affinity = new GroupAffinity { Mask = 1 };
            if (!SetThreadGroupAffinity(GetCurrentThread(), ref affinity, out var previous)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var target = Execute("ioctl_read_msr", 0x1a2);
                var status = Execute("ioctl_read_msr", _package ? 0x1b1u : 0x19cu); name = _package ? "Intel CPU package" : "Intel CPU core";
                value = CpuRegisters.Intel(target, status);
            }
            finally { if (!SetThreadGroupAffinity(GetCurrentThread(), ref previous, out _)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        return value.HasValue ? new(1, "Available", value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), name) : CpuSensorProtocol.Missing("SensorUnavailable");
    }
    private ulong Execute(string function, ulong register)
    {
        var input = new byte[40]; Encoding.ASCII.GetBytes(function).CopyTo(input, 0); BitConverter.GetBytes(register).CopyTo(input, 32); var output = new byte[8];
        if (!DeviceIoControl(_device!, 0xa1b22104, input, input.Length, output, output.Length, out var count, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (count != 8) throw new InvalidDataException("Truncated CPU register response.");
        return BitConverter.ToUInt64(output);
    }
    public void Dispose() { _pci?.Dispose(); _device?.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct GroupAffinity { public ulong Mask; public ushort Group, Reserved1, Reserved2, Reserved3; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadGroupAffinity(IntPtr thread, ref GroupAffinity affinity, out GroupAffinity previous);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int length, byte[] output, int capacity, out int returned, IntPtr overlapped);
}
