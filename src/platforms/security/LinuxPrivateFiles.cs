using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal static class LinuxPrivateFiles
{
    private const int CloseOnExec = 0x80000, Create = 0x40, Exclusive = 0x80;
    private static int DirectoryFlag => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x4000 : 0x10000;
    private static int NoFollow => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;
    private const uint RequiredStat = 0x0b, DirectoryMode = 0x1c0, FileMode = 0x180;
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat
    { [FieldOffset(0)] public uint Mask; [FieldOffset(20)] public uint Uid; [FieldOffset(28)] public ushort Mode; }
    internal static uint UserId { get { RequireLinux(); return geteuid(); } }
    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Linux private storage requires a declared x64 or ARM64 provider.");
    }
    private static IOException Failure() => new("Linux private storage operation failed (errno " + Marshal.GetLastPInvokeError() + ").");
    private static SafeFileHandle Handle(int descriptor) => descriptor < 0 ? throw Failure() : new(new IntPtr(descriptor), true);
    private static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    private static void Verify(SafeFileHandle handle, bool directory)
    {
        if (statx(Fd(handle), "", 0x1000, RequiredStat, out var value) != 0) throw Failure();
        var type = directory ? 0x4000 : 0x8000; var mode = directory ? DirectoryMode : FileMode;
        if ((value.Mask & RequiredStat) != RequiredStat || value.Uid != UserId || (value.Mode & 0xf000) != type || (value.Mode & 0xfff) != mode)
            throw new IOException(directory ? "Linux private directory must be owned by this user with mode0700 and contain no symbolic links." :
                "Linux private file must be owned by this user with mode0600 and contain no symbolic links.");
    }
    private static SafeFileHandle OpenDirectory(string path, bool create)
    {
        RequireLinux(); var full = Path.GetFullPath(path); var segments = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new IOException("The filesystem root cannot be an account storage directory.");
        var current = Handle(open("/", DirectoryFlag | NoFollow | CloseOnExec));
        try
        {
            foreach (var name in segments)
            {
                var next = openat(Fd(current), name, DirectoryFlag | NoFollow | CloseOnExec, 0);
                if (next < 0 && Marshal.GetLastPInvokeError() == 2 && create)
                {
                    if (mkdirat(Fd(current), name, DirectoryMode) != 0 && Marshal.GetLastPInvokeError() != 17) throw Failure();
                    next = openat(Fd(current), name, DirectoryFlag | NoFollow | CloseOnExec, 0);
                }
                var opened = Handle(next); current.Dispose(); current = opened;
            }
            Verify(current, true); return current;
        }
        catch { current.Dispose(); throw; }
    }
    public static void CreateDirectory(string path) { using var directory = OpenDirectory(path, true); }
    private static SafeFileHandle? OpenFile(SafeFileHandle directory, string name, bool absentAllowed)
    {
        var descriptor = openat(Fd(directory), name, NoFollow | CloseOnExec, 0);
        if (descriptor < 0 && Marshal.GetLastPInvokeError() == 2 && absentAllowed) return null;
        var handle = Handle(descriptor);
        try { Verify(handle, false); return handle; } catch { handle.Dispose(); throw; }
    }
    public static bool Exists(string directory, string name)
    { using var parent = OpenDirectory(directory, false); using var file = OpenFile(parent, Leaf(name), true); return file != null; }
    private static string Leaf(string name) => name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(new[] { '/', '\0' }) < 0 ? name : throw new IOException("A private file name is required.");
    public static byte[] Read(string path)
    {
        var full = Path.GetFullPath(path); using var directory = OpenDirectory(Path.GetDirectoryName(full)!, false);
        using var handle = OpenFile(directory, Leaf(Path.GetFileName(full)), false)!;
        using var stream = new FileStream(handle, FileAccess.Read); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    public static void Write(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path); var name = Leaf(Path.GetFileName(full));
        using var directory = OpenDirectory(Path.GetDirectoryName(full)!, true);
        using (OpenFile(directory, name, true)) { }
        var staging = ".account-desk-" + Guid.NewGuid().ToString("N") + ".tmp"; var moved = false;
        using var temporary = Handle(openat(Fd(directory), staging, 1 | Create | Exclusive | NoFollow | CloseOnExec, FileMode));
        try
        {
            Verify(temporary, false);
            using (var stream = new FileStream(temporary, FileAccess.Write)) { stream.Write(bytes); stream.Flush(true); }
            if (renameat(Fd(directory), staging, Fd(directory), name) != 0) throw Failure();
            moved = true;
            if (fsync(Fd(directory)) != 0) throw Failure();
        }
        finally { if (!moved && unlinkat(Fd(directory), staging, 0) != 0 && Marshal.GetLastPInvokeError() != 2) throw Failure(); }
    }
    public static SafeFileHandle Lock(string path)
    {
        using var directory = OpenDirectory(path, false);
        var handle = Handle(openat(Fd(directory), ".vault-key.lock", 2 | Create | NoFollow | CloseOnExec, FileMode));
        try
        {
            Verify(handle, false); var clock = Stopwatch.StartNew();
            while (flock(Fd(handle), 2 | 4) != 0)
            {
                if (Marshal.GetLastPInvokeError() != 11) throw Failure();
                if (clock.Elapsed >= TimeSpan.FromSeconds(10)) throw new LinuxVaultException(LinuxKeyFailure.Busy);
                Thread.Sleep(25);
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int openat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int mkdirat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
    [DllImport("libc", SetLastError = true)] private static extern int renameat(int oldDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldName, int newDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string newName);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern int flock(int descriptor, int operation);
    [DllImport("libc")] private static extern uint geteuid();
}
