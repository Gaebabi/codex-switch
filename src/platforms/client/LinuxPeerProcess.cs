using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal sealed class LinuxPeerProcess : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Credentials { public int Pid; public uint Uid, Gid; }
    [StructLayout(LayoutKind.Sequential)] private struct Poll { public int File; public short Events, Returned; }
    private readonly SafeFileHandle _pidfd;
    private readonly LinuxExecutable _image;
    private readonly string _home;
    private readonly ulong _started;
    public int Id { get; }
    private LinuxPeerProcess(SafeFileHandle pidfd, LinuxExecutable image, int id, string home, ulong started)
    { _pidfd = pidfd; _image = image; Id = id; _home = home; _started = started; }
    internal static IOException Refused() => new("The Linux desktop IPC peer is not the approved same-user packaged client for this home.");
    public static LinuxPeerProcess Open(Socket socket, LinuxExecutable image, string home)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux peer verification requires Linux.");
        uint length = 12;
        if (getsockopt(socket.Handle.ToInt32(), 1, 17, out Credentials credentials, ref length) != 0 || length != 12 ||
            credentials.Pid <= 0 || credentials.Uid != LinuxPrivateFiles.UserId || credentials.Uid == 0) throw Refused();
        length = 4;
        if (getsockopt(socket.Handle.ToInt32(), 1, 77, out int descriptor, ref length) != 0) throw Refused();
        var pidfd = new SafeFileHandle(new IntPtr(descriptor), true);
        try
        {
            if (length != 4 || descriptor < 0) throw Refused();
            var value = new LinuxPeerProcess(pidfd, image, credentials.Pid, home, StartTime(credentials.Pid));
            value.Verify(); return value;
        }
        catch { pidfd.Dispose(); throw; }
    }
    private static string Read(int id, string name, int maximum)
    {
        using var file = new FileStream("/proc/" + id + "/" + name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var count = file.Read(buffer); if (count == 0) break;
            if (bytes.Length + count > maximum) throw Refused(); bytes.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }
    internal static ulong StartTime(int id)
    {
        var stat = Read(id, "stat", 4096); var end = stat.LastIndexOf(')');
        if (end < 0) throw Refused();
        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var started) || started == 0) throw Refused();
        return started;
    }
    internal static void VerifyArguments(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] != LinuxClientDiscovery.DesktopPath) throw Refused();
        foreach (var argument in arguments.Skip(1))
        {
            if (argument is "--ozone-platform=x11" or "--ozone-platform-hint=auto") continue;
            const string prefix = "codex://threads/";
            if (!argument.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(argument[prefix.Length..], "D", out _)) throw Refused();
        }
    }
    internal static void VerifyEnvironment(string[] environment, string home)
    {
        string? codex = null, userHome = null;
        foreach (var entry in environment)
        {
            var split = entry.IndexOf('='); if (split < 1) throw Refused(); var key = entry[..split]; var value = entry[(split + 1)..];
            if (key.StartsWith("LD_", StringComparison.Ordinal) || key.StartsWith("NODE_", StringComparison.Ordinal) || key.StartsWith("ELECTRON_", StringComparison.Ordinal) ||
                key is "GLIBC_TUNABLES" or "GCONV_PATH" or "OAI_APP_DEV_SERVER_URL" or "VITE_DEV_SERVER_URL") throw Refused();
            if (key == "CODEX_HOME") { if (codex != null) throw Refused(); codex = value; }
            if (key == "HOME") { if (userHome != null) throw Refused(); userHome = value; }
        }
        if (userHome == null || !Path.IsPathFullyQualified(userHome) || (codex ?? Path.Combine(userHome, ".codex")) != home) throw Refused();
    }
    public void Verify()
    {
        ObjectDisposedException.ThrowIf(_pidfd.IsClosed, this);
        var state = new Poll { File = _pidfd.DangerousGetHandle().ToInt32(), Events = 1 };
        if (poll(ref state, 1, 0) != 0 || state.Returned != 0 || !_image.IsProcessImage((uint)Id) || StartTime(Id) != _started) throw Refused();
        var pid = Read(Environment.ProcessId, "fdinfo/" + _pidfd.DangerousGetHandle().ToInt32(), 4096).Split('\n').SingleOrDefault(line => line.StartsWith("Pid:", StringComparison.Ordinal));
        if (pid == null || !int.TryParse(pid[4..].Trim(), out var original) || original != Id) throw Refused();
        var status = Read(Id, "status", 16384).Split('\n');
        var uid = status.SingleOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal));
        var traced = status.SingleOrDefault(line => line.StartsWith("TracerPid:", StringComparison.Ordinal));
        if (uid == null || traced == null || traced[10..].Trim() != "0" ||
            uid[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is not { Length: 4 } ids ||
            ids.Any(id => !uint.TryParse(id, out var value) || value != LinuxPrivateFiles.UserId)) throw Refused();
        var arguments = Read(Id, "cmdline", 32768).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (!LinuxDesktopLaunch.Verify(Id, _home, _started, arguments))
        {
            VerifyArguments(arguments);
            VerifyEnvironment(Read(Id, "environ", 1024 * 1024).Split('\0', StringSplitOptions.RemoveEmptyEntries), _home);
        }
        if (StartTime(Id) != _started || poll(ref state, 1, 0) != 0) throw Refused();
    }
    public void Dispose() => _pidfd.Dispose();
    [DllImport("libc", SetLastError = true)] private static extern int getsockopt(int socket, int level, int option, out Credentials value, ref uint length);
    [DllImport("libc", SetLastError = true)] private static extern int getsockopt(int socket, int level, int option, out int value, ref uint length);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref Poll descriptors, nuint count, int timeout);
}
