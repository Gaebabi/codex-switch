using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal readonly record struct WindowsProcessImage(string Path, long CreationTime)
{
    private const uint QueryLimitedInformation = 0x1000, Synchronize = 0x00100000;
    private const uint WaitSignaled = 0, WaitTimeout = 258;
    private const int InvalidParameter = 87, InsufficientBuffer = 122, MoreData = 234;
    private const int MaximumPathCapacity = 32768;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WNetGetConnectionW(string localName, StringBuilder remoteName, ref int length);

    public bool MatchesPath(string executable)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(Path, executable, comparison)) return true;
        // Windows reports the UNC image path even when it was started from a mapped drive.
        if (!Path.StartsWith(@"\\", StringComparison.Ordinal) || executable.Length < 3 ||
            executable[1] != ':' || executable[2] is not ('\\' or '/')) return false;

        var remote = new StringBuilder(512);
        var length = remote.Capacity;
        var drive = executable[..2];
        var result = WNetGetConnectionW(drive, remote, ref length);
        if (result == MoreData && length > remote.Capacity && length <= MaximumPathCapacity)
        {
            remote.EnsureCapacity(length);
            length = remote.Capacity;
            result = WNetGetConnectionW(drive, remote, ref length);
        }
        return result == 0 && string.Equals(Path, System.IO.Path.Combine(remote.ToString(), executable[3..]), comparison);
    }

    public static WindowsProcessImage? Read(int processId)
    {
        using var process = OpenProcess(QueryLimitedInformation | Synchronize, false, processId);
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == InvalidParameter) return null; // The enumerated process has already exited.
            throw new Win32Exception(error);
        }

        if (HasExited(process)) return null;
        // The image path is available from the process handle without reading loader modules.
        for (var capacity = 512; ; capacity = MaximumPathCapacity)
        {
            var path = new StringBuilder(capacity);
            var size = path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref size))
            {
                var error = Marshal.GetLastWin32Error();
                if (HasExited(process)) return null;
                if (error == InsufficientBuffer && capacity < MaximumPathCapacity) continue;
                throw new Win32Exception(error);
            }
            if (!GetProcessTimes(process, out var creation, out _, out _, out _))
            {
                var error = Marshal.GetLastWin32Error();
                if (HasExited(process)) return null;
                throw new Win32Exception(error);
            }
            return HasExited(process) ? null : new WindowsProcessImage(path.ToString(), creation);
        }
    }

    private static bool HasExited(SafeProcessHandle process) => WaitForSingleObject(process, 0) switch
    {
        WaitSignaled => true,
        WaitTimeout => false,
        _ => throw new Win32Exception(Marshal.GetLastWin32Error())
    };
}
