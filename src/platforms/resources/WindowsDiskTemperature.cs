using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal static class WindowsDiskTemperature
{
    private const uint StorageQueryProperty = 0x002d1400, VolumeDiskExtents = 0x00560000;
    internal static uint[] ParseExtents(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8) throw new FormatException("Truncated volume disk extents.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (count == 0 || count > 128 || bytes.Length < 8L + count * 24L) throw new FormatException("Invalid volume disk extent count.");
        var disks = new HashSet<uint>();
        for (var index = 0; index < count; index++) disks.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8 + index * 24, 4)));
        return disks.Order().ToArray();
    }
    internal static TemperatureReading ParseDescriptor(ReadOnlySpan<byte> bytes, string source, DateTimeOffset at)
    {
        if (bytes.Length < 24) return TemperatureReading.Missing(TemperatureState.InvalidData, source, at: at);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes); var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        if (version < 40 || size < 24 || size > bytes.Length || count > 128 || size < 24L + count * 16L)
            return TemperatureReading.Missing(TemperatureState.InvalidData, source, at: at);
        var sensors = new List<(ushort Index, short Value)>();
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(24 + index * 16, 16); var value = BinaryPrimitives.ReadInt16LittleEndian(entry[2..]);
            if (value == short.MinValue) continue;
            if (!TemperatureReading.Valid(value)) return TemperatureReading.Missing(TemperatureState.InvalidData, source, at: at);
            sensors.Add((BinaryPrimitives.ReadUInt16LittleEndian(entry), value));
        }
        if (sensors.Select(s => s.Index).Distinct().Count() != sensors.Count) return TemperatureReading.Missing(TemperatureState.InvalidData, source, at: at);
        if (sensors.Count == 0) return TemperatureReading.Missing(TemperatureState.SensorUnavailable, source, at: at);
        var composite = sensors.Where(s => s.Index == 0).ToArray();
        var selected = composite.Length == 1 ? composite[0].Value : sensors.Max(s => s.Value);
        var detail = string.Join("; ", sensors.Select(s => "sensor " + s.Index + ": " + s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " °C"));
        return TemperatureReading.From(selected, source, detail, at) with { Limits = TemperatureLimits.Device(
            BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]), BinaryPrimitives.ReadInt16LittleEndian(bytes[8..])) };
    }
    public static TemperatureReading Read(string root)
    {
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1..] != ":\\") return TemperatureReading.Missing(TemperatureState.InvalidData);
        if (!OperatingSystem.IsWindows()) return TemperatureReading.Missing(TemperatureState.PlatformUnsupported, "Windows storage");
        var at = DateTimeOffset.UtcNow;
        var extents = Query(@"\\.\" + root[..2], VolumeDiskExtents, Array.Empty<byte>(), out var error);
        if (extents == null) return Failure(error, "Windows storage", at);
        uint[] disks;
        try { disks = ParseExtents(extents); }
        catch (FormatException) { return TemperatureReading.Missing(TemperatureState.InvalidData, "Windows storage", at: at); }
        var readings = disks.Select(disk => ReadDisk(disk, at)).ToArray();
        return Aggregate(readings);
    }
    internal static TemperatureReading Aggregate(TemperatureReading[] readings)
    {
        if (readings.Length == 0) return TemperatureReading.Missing(TemperatureState.InvalidData, "Windows storage");
        if (readings.Length == 1) return readings[0];
        var coverage = readings.Count(r => r.State == TemperatureState.Available);
        var detail = string.Join("\n", readings.Select(r => r.Source + ": " + (r.Celsius.HasValue ? r.Celsius.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " °C" : r.State.ToString())));
        if (coverage == 0) return readings.FirstOrDefault(r => r.State == TemperatureState.AccessDenied) ?? readings[0];
        return TemperatureReading.Highest(readings, "Windows storage (" + coverage + "/" + readings.Length + ")", detail, TemperatureScope.BackingDisks);
    }
    private static TemperatureReading ReadDisk(uint disk, DateTimeOffset at)
    {
        var query = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(query, 52);
        var source = "Windows storage · disk " + disk;
        var bytes = Query(@"\\.\PhysicalDrive" + disk, StorageQueryProperty, query, out var error);
        return bytes == null ? Failure(error, source, at) : ParseDescriptor(bytes, source, at);
    }
    private static TemperatureReading Failure(int error, string source, DateTimeOffset at) => TemperatureReading.Missing(
        error == 5 ? TemperatureState.AccessDenied : error is 1 or 50 or 87 ? TemperatureState.SensorUnavailable : TemperatureState.ReadFailed,
        source, code: "Win32 " + error, at: at);
    private static byte[]? Query(string path, uint code, byte[] input, out int error)
    {
        using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) { error = Marshal.GetLastWin32Error(); return null; }
        var output = new byte[4096];
        if (!DeviceIoControl(handle, code, input, input.Length, output, output.Length, out var returned, IntPtr.Zero)) { error = Marshal.GetLastWin32Error(); return null; }
        if (returned < 0 || returned > output.Length) { error = 13; return null; }
        error = 0; return output[..returned];
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, [Out] byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
