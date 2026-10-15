using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal sealed class LinuxExecutable : IDisposable
{
    private const int CloseOnExec = 0x80000, NonBlocking = 0x800;
    private static int DirectoryFlag => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x4000 : 0x10000;
    private static int NoFollow => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    private readonly SafeFileHandle _handle;
    private readonly Stat _verified;
    public string Path { get; }
    public string Sha256 { get; }
    public string ExecutionPath
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            if (!Same(_verified, Inspect(_handle))) throw Refused();
            return "/proc/" + Environment.ProcessId + "/fd/" + Descriptor(_handle);
        }
    }
    private LinuxExecutable(SafeFileHandle handle, string path, string sha256, Stat verified)
    { _handle = handle; Path = path; Sha256 = sha256; _verified = verified; }
    private static IOException Refused() => new("Linux client trust verification failed. Select an approved installation or explicitly trust the exact native executable checksum.");
    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Linux client verification requires native Linux x64 or ARM64.");
        if (getuid() != geteuid()) throw Refused();
    }
    private static int Descriptor(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    private static SafeFileHandle Handle(int descriptor) => descriptor < 0 ? throw Refused() : new(new IntPtr(descriptor), true);
    private static Stat Inspect(SafeFileHandle handle)
    {
        if (statx(Descriptor(handle), "", 0x1000, 0x7ff, out var value) != 0 || (value.Mask & 0x7ff) != 0x7ff) throw Refused();
        return value;
    }
    private static bool Same(Stat left, Stat right) => left.Inode == right.Inode && left.DeviceMajor == right.DeviceMajor && left.DeviceMinor == right.DeviceMinor &&
        left.Uid == right.Uid && left.Mode == right.Mode && left.Size == right.Size && left.ChangeSeconds == right.ChangeSeconds && left.ChangeNanoseconds == right.ChangeNanoseconds &&
        left.ModifiedSeconds == right.ModifiedSeconds && left.ModifiedNanoseconds == right.ModifiedNanoseconds;
    private static void VerifyOwner(Stat value, bool system, bool directory)
    {
        if ((value.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || value.Uid != 0 && (system || value.Uid != geteuid())) throw Refused();
        var rootStickyDirectory = !system && directory && value.Uid == 0 && (value.Mode & 0x200) != 0;
        if ((value.Mode & 0x12) != 0 && !rootStickyDirectory) throw Refused();
        if (!directory && (value.Mode & 0xc00) != 0) throw Refused();
    }
    private static SafeFileHandle Open(string path, bool system)
    {
        RequireLinux(); if (!System.IO.Path.IsPathFullyQualified(path)) throw Refused();
        var full = System.IO.Path.GetFullPath(path); var names = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0 || full != path) throw Refused();
        var current = Handle(open("/", DirectoryFlag | NoFollow | CloseOnExec));
        try
        {
            VerifyOwner(Inspect(current), system, true);
            for (var index = 0; index < names.Length; index++)
            {
                var directory = index != names.Length - 1;
                var next = Handle(openat(Descriptor(current), names[index], NoFollow | CloseOnExec | NonBlocking | (directory ? DirectoryFlag : 0), 0));
                try { VerifyOwner(Inspect(next), system, directory); }
                catch { next.Dispose(); throw; }
                current.Dispose(); current = next;
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }
    public static string ReadSystemText(string path, CancellationToken cancellation = default)
    {
        using var handle = Open(path, true); var value = Inspect(handle);
        if (value.Size > 16 * 1024 * 1024) throw Refused();
        var bytes = new byte[(int)value.Size]; var count = 0;
        while (count < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = RandomAccess.Read(handle, bytes.AsSpan(count), count); if (read == 0) throw Refused(); count += read;
        }
        if (!Same(value, Inspect(handle))) throw Refused();
        return new System.Text.UTF8Encoding(false, true).GetString(bytes);
    }
    public static LinuxExecutable Verify(string path, string expectedSha256, bool system, CancellationToken cancellation = default)
    {
        if (expectedSha256.Length != 64) throw Refused();
        foreach (var value in expectedSha256) if (!char.IsAsciiHexDigit(value)) throw Refused();
        var handle = Open(path, system);
        try
        {
            var before = Inspect(handle);
            if (before.Size > 1024UL * 1024 * 1024) throw Refused();
            var capabilities = fgetxattr(Descriptor(handle), "security.capability", IntPtr.Zero, 0);
            if (capabilities >= 0 || Marshal.GetLastPInvokeError() is not (61 or 95)) throw Refused();
            Span<byte> header = stackalloc byte[64];
            if (RandomAccess.Read(handle, header, 0) != header.Length || !header[..6].SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46, 2, 1 }) ||
                header[6] != 1 || header[7] is not (0 or 3) || (header[16] | header[17] << 8) is not (2 or 3) ||
                (header[18] | header[19] << 8) != (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 183 : 62) || (before.Mode & 0x49) == 0) throw Refused();
            var programOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[32..]); var programCount = BinaryPrimitives.ReadUInt16LittleEndian(header[56..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(header[52..]) != 64 || BinaryPrimitives.ReadUInt16LittleEndian(header[54..]) != 56 ||
                programCount is 0 or > 4096 || programOffset < 64 || programOffset > before.Size || (ulong)programCount * 56 > before.Size - programOffset) throw Refused();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var bytes = new byte[128 * 1024]; long offset = 0;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = RandomAccess.Read(handle, bytes, offset); if (count == 0) break;
                hash.AppendData(bytes, 0, count); offset = checked(offset + count);
            }
            var digest = hash.GetHashAndReset();
            if ((ulong)offset != before.Size || !CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(expectedSha256)) || !Same(before, Inspect(handle))) throw Refused();
            return new LinuxExecutable(handle, path, Convert.ToHexString(digest).ToLowerInvariant(), before);
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() => _handle.Dispose();
    public bool IsProcessImage(uint processId)
    {
        using var image = Handle(open("/proc/" + processId + "/exe", CloseOnExec | NonBlocking));
        return Same(_verified, Inspect(_handle)) && Same(_verified, Inspect(image));
    }
    [DllImport("libc", SetLastError = true)] private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int openat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
    [DllImport("libc")] private static extern uint getuid();
    [DllImport("libc")] private static extern uint geteuid();
    [DllImport("libc", SetLastError = true)] private static extern nint fgetxattr(int descriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr value, nuint size);
}
