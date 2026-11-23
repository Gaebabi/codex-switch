using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal enum FolderState { Scanning, Available, Partial, Missing, Unavailable, Unsupported, Cached }
internal sealed record FolderReading(string Path, int Color, long? Bytes, FolderState State, int Skipped = 0, DateTimeOffset? CheckedAt = null);
internal sealed record DriveSegment(string Label, long Bytes, int? Color);

internal static partial class FolderUsage
{
    public static readonly string[] Colors = { "#5189E8", "#D48132", "#45A58C", "#AF77CE", "#CE6581", "#73A146", "#4CABB8", "#B49C35" };
    public static FolderPreference[] Validate(string root, FolderPreference[]? folders)
    {
        var result = (folders ?? Array.Empty<FolderPreference>()).Select(folder =>
        {
            if (folder.Color < 0 || folder.Color >= Colors.Length || !Path.IsPathFullyQualified(folder.Path))
                throw new ArgumentException("folders.invalid");
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Path));
            if (OperatingSystem.IsWindows() && path.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith('.') || part.EndsWith(' ')))
                throw new ArgumentException("folders.invalid");
            if (!Inside(root, path)) throw new ArgumentException("folders.drive");
            return folder with { Path = path };
        }).ToArray();
        if (result.Length > Colors.Length) throw new ArgumentException("folders.limit");
        for (var i = 0; i < result.Length; i++)
            for (var j = 0; j < i; j++)
                if (DriveMonitor.RootComparer.Equals(result[i].Path, result[j].Path) || Inside(result[i].Path, result[j].Path) || Inside(result[j].Path, result[i].Path))
                    throw new ArgumentException("folders.overlap");
        return result;
    }
    private static bool Inside(string parent, string child) => child.Length > parent.Length && child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public static DriveSegment[] Segments(DriveReading drive)
    {
        if (drive.Total is not > 0 || drive.Free is not >= 0) return Array.Empty<DriveSegment>();
        var free = Math.Min(drive.Free.Value, drive.Total.Value); var remaining = drive.Total.Value - free;
        var result = new List<DriveSegment>();
        foreach (var folder in drive.Folders ?? Array.Empty<FolderReading>())
        {
            var bytes = Math.Min(Math.Max(folder.Bytes ?? 0, 0), remaining);
            if (bytes > 0) result.Add(new(folder.Path, bytes, folder.Color));
            remaining -= bytes;
        }
        result.Add(new("folders.other", remaining, null)); result.Add(new("folders.free", free, null));
        return result.ToArray();
    }
    public static Dictionary<string, FolderReading[]> Scan(DrivePreference[] preferences, CancellationToken cancellation, Action<string, FolderReading>? measured = null, Cache? cache = null, Action<string, bool>? activity = null)
    {
        cache ??= new();
        cache.Configure(preferences);
        cache.BeginScan();
        var result = new Dictionary<string, FolderReading[]>(DriveMonitor.RootComparer);
        foreach (var drive in preferences)
        {
            result[drive.Root] = (drive.Folders ?? Array.Empty<FolderPreference>()).Select(folder =>
            {
                if (cache.TryReuse(folder.Path, out var saved)) return saved with { Color = folder.Color };
                var announced = false;
                try
                {
                    var reading = Read(drive.Root, folder, cancellation, cache, () => { if (!announced) { announced = true; activity?.Invoke(drive.Root, true); } });
                    measured?.Invoke(drive.Root, reading); return reading;
                }
                finally { if (announced) activity?.Invoke(drive.Root, false); }
            }).ToArray();
        }
        cache.Complete(preferences, result, cancellation);
        return result;
    }
    private static FolderReading Read(string root, FolderPreference folder, CancellationToken cancellation, Cache cache, Action measuring)
    {
        if (!OperatingSystem.IsWindows()) return new(folder.Path, folder.Color, null, FolderState.Unsupported);
        cancellation.ThrowIfCancellationRequested();
        var work = cache.Pending(folder.Path);
        if (work == null || cache.NeedsRootCheck(folder.Path))
        {
            cache.Begin(folder.Path);
            uint volume;
            try
            {
                for (var directory = new DirectoryInfo(folder.Path); directory != null && !DriveMonitor.RootComparer.Equals(directory.FullName, root); directory = directory.Parent)
                {
                    var path = directory.FullName;
                    if ((cache.Query(() => File.GetAttributes(path), cancellation) & FileAttributes.ReparsePoint) != 0)
                        return Failed(FolderState.Unavailable);
                }
                var info = cache.Query(() =>
                {
                    using var handle = CreateFileW(NativePath(folder.Path), 0x81, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var value))
                        throw new IOException("Folder volume cannot be read.", new Win32Exception(Marshal.GetLastWin32Error()));
                    return value;
                }, cancellation);
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    return Failed(FolderState.Unavailable);
                volume = info.Volume;
                cache.Root(folder.Path, volume, ((ulong)info.IdHigh << 32) | info.IdLow);
            }
            catch (FileNotFoundException) { return Failed(FolderState.Missing); }
            catch (DirectoryNotFoundException) { return Failed(FolderState.Missing); }
            catch (IOException) { return Failed(FolderState.Unavailable); }
            catch (UnauthorizedAccessException) { return Failed(FolderState.Unavailable); }
            cache.Trust(folder.Path);
            if (cache.TryReuse(folder.Path, out var saved)) return saved with { Color = folder.Color };
            work = cache.Pending(folder.Path) ?? cache.Start(folder.Path, volume);
        }
        var processed = 0;
        var slice = Stopwatch.StartNew();
        while (work.Pending.Count != 0 && processed < cache.DirectoriesPerSlice && !work.Invalidated &&
            (cache.DirectoriesPerSlice == int.MaxValue || slice.Elapsed < TimeSpan.FromMilliseconds(100)))
        {
            cancellation.ThrowIfCancellationRequested();
            var path = work.Pending.Pop();
            if (!DriveMonitor.RootComparer.Equals(path, folder.Path) && !cache.KnownDirectory(path)) continue;
            if (!work.Seen.Add(path)) continue;
            try
            {
                var native = cache.NeedsRead(path);
                if (native) measuring();
                var entries = cache.Entries(new DirectoryInfo(path), work.Volume, cancellation);
                foreach (var entry in entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { work.Skipped++; continue; }
                    if ((entry.Attributes & FileAttributes.Directory) != 0) work.Pending.Push(entry.Path);
                    else if (entry.Failed) { work.Skipped++; work.Partial = true; }
                }
                if (native) processed++;
            }
            catch (OperationCanceledException) { work.Seen.Remove(path); work.Pending.Push(path); throw; }
            catch (IOException) { cache.FailDirectory(path); work.Skipped++; work.Partial = true; processed++; }
            catch (UnauthorizedAccessException) { cache.FailDirectory(path); work.Skipped++; work.Partial = true; processed++; }
        }
        var finished = work.Pending.Count == 0 && !work.Invalidated;
        if (finished) cache.Finish(folder.Path);
        return new(folder.Path, folder.Color, cache.Bytes(folder.Path),
            !finished ? FolderState.Scanning : work.Partial ? FolderState.Partial : FolderState.Available, work.Skipped, DateTimeOffset.UtcNow);
        FolderReading Failed(FolderState state)
        {
            cache.Reset(folder.Path); cache.Finish(folder.Path);
            return new(folder.Path, folder.Color, null, state, CheckedAt: DateTimeOffset.UtcNow);
        }
    }
    internal readonly record struct FolderEntry(string Path, FileAttributes Attributes, long? Bytes, uint Volume, ulong Id, bool Resolved = false, bool Failed = false);

    [StructLayout(LayoutKind.Explicit, Size = 104)] private struct DirectoryEntry
    {
        [FieldOffset(0)] public uint Next;
        [FieldOffset(48)] public long AllocationSize;
        [FieldOffset(56)] public FileAttributes Attributes;
        [FieldOffset(60)] public uint NameBytes;
        [FieldOffset(96)] public ulong Id;
    }
    private static IEnumerable<FolderEntry> Entries(DirectoryInfo directory, uint volume, CancellationToken cancellation, Cache cache)
    {
        using var handle = cache.Query(() =>
        {
            var opened = CreateFileW(NativePath(directory.FullName), 0x81, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (!opened.IsInvalid) return opened;
            var error = Marshal.GetLastWin32Error(); opened.Dispose();
            throw new IOException("Folder directory cannot be read.", new Win32Exception(error));
        }, cancellation);
        if (!cache.Query(() => GetAttributeTagInfo(handle, 9, out var attributes, 8) && (attributes.Attributes & FileAttributes.ReparsePoint) == 0 && (attributes.Attributes & FileAttributes.Directory) != 0, cancellation))
            throw new IOException("Folder directory changed during measurement.");
        const int size = 65536; var buffer = Marshal.AllocHGlobal(size); var first = true;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var query = cache.Query(() =>
                {
                    var success = GetDirectoryInfo(handle, first ? 11 : 10, buffer, size);
                    return (Success: success, Error: success ? 0 : Marshal.GetLastWin32Error());
                }, cancellation);
                if (!query.Success)
                {
                    var error = query.Error;
                    if (error == 18) yield break;
                    if (first && error is 1 or 50 or 87)
                    {
                        if (!cache.Query(() => GetAttributeTagInfo(handle, 9, out var tag, 8) && (tag.Attributes & FileAttributes.ReparsePoint) == 0 && (tag.Attributes & FileAttributes.Directory) != 0, cancellation))
                            throw new IOException("Folder directory changed during measurement.");
                        using var entries = cache.Query(() => directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }).GetEnumerator(), cancellation);
                        while (cache.Query(entries.MoveNext, cancellation))
                        { var entry = entries.Current; yield return new(entry.FullName, entry.Attributes, null, 0, 0); }
                        yield break;
                    }
                    throw new IOException("Folder directory enumeration failed.", new Win32Exception(error));
                }
                first = false; var offset = 0;
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (offset < 0 || offset > size - 104) throw new IOException("Invalid directory entry boundary.");
                    var entry = Marshal.PtrToStructure<DirectoryEntry>(IntPtr.Add(buffer, offset));
                    var remaining = size - offset;
                    if (entry.NameBytes == 0 || entry.NameBytes % 2 != 0 || entry.NameBytes > remaining - 104 || entry.AllocationSize < 0 ||
                        (entry.Next != 0 && (entry.Next < 104 + entry.NameBytes || entry.Next > remaining || entry.Next % 8 != 0)))
                        throw new IOException("Invalid directory allocation record.");
                    var name = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + 104), (int)entry.NameBytes / 2)!;
                    if (name is not ("." or "..")) yield return new(Path.Combine(directory.FullName, name), entry.Attributes, entry.Id == 0 ? null : entry.AllocationSize, volume, entry.Id);
                    if (entry.Next == 0) break;
                    offset += (int)entry.Next;
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo
    { public long AllocationSize, EndOfFile; public uint Links; public byte DeletePending, Directory; }
    [StructLayout(LayoutKind.Sequential)] private struct CompressionInfo
    { public long Size; public ushort Format; public byte UnitShift, ChunkShift, ClusterShift, Reserved1, Reserved2, Reserved3; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out StandardInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static extern bool GetCompressionInfo(SafeFileHandle handle, int kind, out CompressionInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static extern bool GetDirectoryInfo(SafeFileHandle handle, int kind, IntPtr buffer, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public FileAttributes Attributes; public uint Tag; }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static extern bool GetAttributeTagInfo(SafeFileHandle handle, int kind, out AttributeTagInfo info, uint size);
    private static string NativePath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path :
        path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    internal static (long Bytes, uint Volume, ulong Id, uint Links) Allocation(string path)
    {
        using var handle = CreateFileW(NativePath(path), 0x80, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 1, out var standard, (uint)Marshal.SizeOf<StandardInfo>()) || !GetFileInformationByHandle(handle, out var info))
            throw new IOException("Folder allocation cannot be read.", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || standard.AllocationSize < 0)
            throw new IOException("Folder entry changed during measurement.");
        var bytes = standard.AllocationSize;
        if ((info.Attributes & (uint)(FileAttributes.Compressed | FileAttributes.SparseFile)) != 0)
        {
            if (!GetCompressionInfo(handle, 8, out var compressed, (uint)Marshal.SizeOf<CompressionInfo>()))
                throw new IOException("Compressed allocation cannot be read.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (compressed.Size < 0) throw new IOException("Invalid compressed allocation.");
            bytes = compressed.Size;
        }
        return (bytes, info.Volume, ((ulong)info.IdHigh << 32) | info.IdLow, info.Links);
    }
}
