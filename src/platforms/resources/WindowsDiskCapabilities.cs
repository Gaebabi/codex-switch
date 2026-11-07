using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal static class WindowsDiskCapabilities
{
    internal static DiskIoProfile Parse(ReadOnlySpan<byte> seek, ReadOnlySpan<byte> device)
    {
        if (seek.Length < 12 || device.Length < 36 || BinaryPrimitives.ReadUInt32LittleEndian(seek) < 12 || BinaryPrimitives.ReadUInt32LittleEndian(seek[4..]) < 12 ||
            BinaryPrimitives.ReadUInt32LittleEndian(seek[4..]) > seek.Length || seek[8] > 1 || BinaryPrimitives.ReadUInt32LittleEndian(device) < 36 ||
            BinaryPrimitives.ReadUInt32LittleEndian(device[4..]) < 36 || BinaryPrimitives.ReadUInt32LittleEndian(device[4..]) > device.Length) return DiskIoProfile.Unknown;
        var bus = BinaryPrimitives.ReadUInt32LittleEndian(device[28..]);
        if (bus is not (1 or 3 or 7 or 10 or 11 or 17)) return DiskIoProfile.Unknown;
        return seek[8] == 1 ? DiskIoProfile.SeekPenalty : DiskIoProfile.NoSeekPenalty;
    }
    internal static DiskIoProfile Combine(IEnumerable<DiskIoProfile> profiles)
    {
        var values = profiles.ToArray();
        if (values.Length == 0 || values.Any(v => v == DiskIoProfile.Unknown)) return DiskIoProfile.Unknown;
        return values.Distinct().Count() == 1 ? values[0] : DiskIoProfile.Mixed;
    }
    public static DiskIoProfile Read(string root)
    {
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1..] != ":\\") throw new ArgumentException("Invalid volume root.");
        if (!OperatingSystem.IsWindows()) return DiskIoProfile.Unknown;
        var bytes = WindowsDiskTemperature.Query(@"\\.\" + root[..2], 0x00560000, Array.Empty<byte>(), out _);
        if (bytes == null) return DiskIoProfile.Unknown;
        uint[] disks;
        try { disks = WindowsDiskTemperature.ParseExtents(bytes); }
        catch (FormatException) { return DiskIoProfile.Unknown; }
        return Combine(disks.Select(disk =>
        {
            var query = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(query, 7);
            var seek = WindowsDiskTemperature.Query(@"\\.\PhysicalDrive" + disk, 0x002d1400, query, out _);
            BinaryPrimitives.WriteUInt32LittleEndian(query, 0);
            var device = WindowsDiskTemperature.Query(@"\\.\PhysicalDrive" + disk, 0x002d1400, query, out _);
            return seek == null || device == null ? DiskIoProfile.Unknown : Parse(seek, device);
        }));
    }
}
