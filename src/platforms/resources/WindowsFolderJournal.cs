using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal enum FolderIndexAccess { Ready, PermissionRequired, JournalMissing, Unsupported, Unavailable }
internal sealed record FolderJournalCursor(ulong JournalId, long Usn);
internal sealed record FolderJournalStatus(FolderIndexAccess Access, uint Volume = 0, ulong JournalId = 0,
    long FirstUsn = 0, long NextUsn = 0, int Error = 0);
internal sealed record FolderJournalChange(uint Volume, ulong FileId, ulong ParentId, uint Reason, FileAttributes Attributes, string Name, long Usn);

internal static class WindowsFolderJournal
{
    private const uint QueryJournal = 0x900f4, ReadUnprivilegedJournal = 0x903ab, CreateJournal = 0x900e7;
    private static readonly UnicodeEncoding Names = new(false, false, true);
    internal static bool LocalRoot(string root) => root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1..] == ":\\";
    internal static FolderJournalStatus Probe(string root)
    {
        if (!OperatingSystem.IsWindows() || !LocalRoot(root) || GetDriveTypeW(root) is not (2 or 3))
            return new(FolderIndexAccess.Unsupported);
        var name = new StringBuilder(32);
        if (!GetVolumeInformationW(root, null, 0, out var volume, out _, out _, name, name.Capacity))
            return Failure(Marshal.GetLastWin32Error());
        if (!string.Equals(name.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase)) return new(FolderIndexAccess.Unsupported);
        using var handle = Open(root, 0x20);
        if (handle.IsInvalid) return Failure(Marshal.GetLastWin32Error());
        var data = new byte[128];
        if (!DeviceIoControl(handle, QueryJournal, null, 0, data, data.Length, out var returned, IntPtr.Zero))
            return Failure(Marshal.GetLastWin32Error());
        if (returned < 56) throw new InvalidDataException("The folder change-journal header is incomplete.");
        var id = BinaryPrimitives.ReadUInt64LittleEndian(data);
        var first = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8));
        var next = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(16));
        var lowest = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(24));
        if (first < 0 || next < first || lowest < 0) throw new InvalidDataException("The folder change-journal range is invalid.");
        try { Read(handle, new ReadRequest { StartUsn = next, ReasonMask = 0x7fff7fff, JournalId = id, MinVersion = 2, MaxVersion = 2 }, new byte[4096]); }
        catch (IOException error) when (error.InnerException is Win32Exception native) { return Failure(native.NativeErrorCode); }
        return new(FolderIndexAccess.Ready, volume, id, Math.Max(first, lowest), next);
    }
    private static FolderJournalStatus Failure(int error) => new(error switch
    {
        5 => FolderIndexAccess.PermissionRequired,
        1179 => FolderIndexAccess.JournalMissing,
        1 or 50 => FolderIndexAccess.Unsupported,
        _ => FolderIndexAccess.Unavailable
    }, Error: error);
    internal static bool Covers(FolderJournalStatus status, FolderJournalCursor cursor) =>
        status.Access == FolderIndexAccess.Ready && status.JournalId == cursor.JournalId &&
        cursor.Usn >= status.FirstUsn && cursor.Usn <= status.NextUsn;

    internal static FolderJournalCursor Replay(string root, FolderJournalStatus status, FolderJournalCursor cursor,
        FolderScanBudget budget, Action<FolderJournalChange> changed, CancellationToken cancellation)
    {
        if (!Covers(status, cursor)) throw new InvalidDataException("The saved folder change-journal range is no longer available.");
        if (cursor.Usn == status.NextUsn) return cursor;
        using var handle = budget.Query(() => Open(root, 0x20), cancellation);
        if (handle.IsInvalid) throw new IOException("Cannot open the folder change journal.", new Win32Exception(Marshal.GetLastWin32Error()));
        var current = cursor.Usn;
        var output = new byte[262144];
        while (current < status.NextUsn)
        {
            var request = new ReadRequest { StartUsn = current, ReasonMask = 0x7fff7fff, JournalId = cursor.JournalId, MinVersion = 2, MaxVersion = 2 };
            var read = budget.Query(() => Read(handle, request, output), cancellation);
            if (read < 8) throw new InvalidDataException("The folder change-journal response is incomplete.");
            var next = BinaryPrimitives.ReadInt64LittleEndian(output);
            if (next <= current) throw new InvalidDataException("The folder change journal did not advance.");
            foreach (var change in Parse(output.AsSpan(8, read - 8), status.Volume))
            {
                cancellation.ThrowIfCancellationRequested();
                if (change.Usn >= current && change.Usn < status.NextUsn) changed(change);
            }
            current = Math.Min(next, status.NextUsn);
        }
        return new(cursor.JournalId, current);
    }
    private static int Read(SafeFileHandle handle, ReadRequest request, byte[] output)
    {
        var input = new byte[Marshal.SizeOf<ReadRequest>()];
        var native = Marshal.AllocHGlobal(input.Length);
        try { Marshal.StructureToPtr(request, native, false); Marshal.Copy(native, input, 0, input.Length); }
        finally { Marshal.FreeHGlobal(native); }
        if (!DeviceIoControl(handle, ReadUnprivilegedJournal, input, input.Length, output, output.Length, out var read, IntPtr.Zero))
            throw new IOException("Cannot read the folder change journal.", new Win32Exception(Marshal.GetLastWin32Error()));
        return read;
    }
    internal static FolderJournalChange[] Parse(ReadOnlySpan<byte> data, uint volume)
    {
        var result = new List<FolderJournalChange>();
        while (!data.IsEmpty)
        {
            if (data.Length < 60) throw new InvalidDataException("Invalid folder change-record boundary.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (length < 60 || length > data.Length || length % 8 != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 2)
                throw new InvalidDataException("Unsupported folder change-record format.");
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[56..]);
            var nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[58..]);
            if (nameLength % 2 != 0 || nameOffset < 60 || nameOffset > length || nameLength > length - nameOffset)
                throw new InvalidDataException("Invalid folder change-record name.");
            string name;
            try { name = Names.GetString(data.Slice(nameOffset, nameLength)); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid Unicode in a folder change record.", error); }
            result.Add(new(volume, BinaryPrimitives.ReadUInt64LittleEndian(data[8..]), BinaryPrimitives.ReadUInt64LittleEndian(data[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[40..]), (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(data[52..]),
                name, BinaryPrimitives.ReadInt64LittleEndian(data[24..])));
            data = data[length..];
        }
        return result.ToArray();
    }
    internal static int Enable(string root)
    {
        if (!OperatingSystem.IsWindows() || !LocalRoot(root)) return 87;
        var status = Probe(root);
        if (status.Access == FolderIndexAccess.Ready) return 0;
        if (status.Access is not (FolderIndexAccess.JournalMissing or FolderIndexAccess.PermissionRequired)) return status.Error == 0 ? 50 : status.Error;
        using var handle = Open(root, 0xc0000000);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error();
        var input = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(input, 128UL * 1024 * 1024);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(8), 32UL * 1024 * 1024);
        return DeviceIoControl(handle, CreateJournal, input, input.Length, Array.Empty<byte>(), 0, out _, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();
    }
    private static SafeFileHandle Open(string root, uint access) => CreateFileW(@"\\.\" + root[..2], access, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
    [StructLayout(LayoutKind.Sequential)] private struct ReadRequest
    { public long StartUsn; public uint ReasonMask, ReturnOnlyOnClose; public ulong Timeout, BytesToWaitFor, JournalId; public ushort MinVersion, MaxVersion; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? label, int labelLength, out uint serial, out uint maximumComponent, out uint flags, StringBuilder filesystem, int filesystemLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, int inputLength, byte[] output, int outputLength, out int returned, IntPtr overlapped);
}
