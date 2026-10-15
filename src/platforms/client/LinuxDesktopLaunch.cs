using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class LinuxDesktopLaunch : IDisposable
{
    private static readonly ConcurrentDictionary<int, LinuxDesktopLaunch> Owned = new();
    private readonly LinuxClientInstallation _installation;
    private readonly Process _process;
    private readonly object _sync = new();
    private bool _disposed;
    private readonly string? _display, _authority, _userHome;
    internal string Home { get; }
    internal string ArgumentZero { get; }
    internal ulong Started { get; }
    public int Id { get; }
    public bool ExitedNormally => _process.HasExited && _process.ExitCode == 0;
    private LinuxDesktopLaunch(LinuxClientInstallation installation, Process process, string home, string argumentZero, ulong started, ProcessStartInfo start)
    {
        _installation = installation; _process = process; Id = process.Id; Home = home; ArgumentZero = argumentZero; Started = started;
        start.Environment.TryGetValue("DISPLAY", out _display); start.Environment.TryGetValue("XAUTHORITY", out _authority); start.Environment.TryGetValue("HOME", out _userHome);
    }
    public static LinuxDesktopLaunch Start(string home, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux desktop launch requires Linux.");
        LinuxPrivateFiles.CreateDirectory(home);
        LinuxDesktopConnection.VerifyHome(home);
        var installation = LinuxClientDiscovery.Open(null, cancellation);
        Process? process = null;
        try
        {
            using var manifest = LinuxClientDiscovery.ApprovedManifest();
            var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
            LinuxExecutable.VerifySystemData("/usr/lib/chatgpt/resources/app.asar", manifest.RootElement.GetProperty("architectures").GetProperty(architecture).GetProperty("asarSha256").GetString()!, cancellation);
            var executable = installation.Desktop!.ExecutionPath;
            var start = CreateStart(executable, home);
            cancellation.ThrowIfCancellationRequested();
            process = Process.Start(start) ?? throw new IOException("The approved Linux desktop could not be started.");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var launched = new LinuxDesktopLaunch(installation, process, home, executable, LinuxPeerProcess.StartTime(process.Id), start);
            if (!Owned.TryAdd(process.Id, launched)) throw LinuxPeerProcess.Refused();
            return launched;
        }
        catch { process?.Dispose(); installation.Dispose(); throw; }
    }
    private static ProcessStartInfo CreateStart(string executable, string home)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = home, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear();
        foreach (var name in new[] { "HOME", "PATH", "LANG", "LC_ALL", "LC_CTYPE", "DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS",
            "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "XDG_SESSION_TYPE", "DESKTOP_SESSION",
            "SSH_AUTH_SOCK", "PULSE_SERVER", "PIPEWIRE_REMOTE", "TZ" })
            if (Environment.GetEnvironmentVariable(name) is string value) start.Environment[name] = value;
        start.Environment["CODEX_HOME"] = home;
        LinuxPeerProcess.VerifyEnvironment(start.Environment.Select(entry => entry.Key + "=" + entry.Value).ToArray(), home);
        return start;
    }
    internal void VerifyOriginal()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process.HasExited || LinuxPeerProcess.StartTime(Id) != Started || !_installation.Desktop!.IsProcessImage((uint)Id)) throw LinuxPeerProcess.Refused();
        }
    }
    public async Task OpenThreadAsync(string id, CancellationToken cancellation)
    {
        if (!Guid.TryParseExact(id, "D", out _)) throw new InvalidDataException("Invalid desktop thread ID.");
        VerifyOriginal();
        await using var before = await LinuxDesktopConnection.ConnectAsync(Home, cancellation).ConfigureAwait(false);
        if (LinuxDesktopConnection.PeerId(before) != Id || _display != Environment.GetEnvironmentVariable("DISPLAY") ||
            _authority != Environment.GetEnvironmentVariable("XAUTHORITY") || _userHome != Environment.GetEnvironmentVariable("HOME")) throw LinuxPeerProcess.Refused();
        var start = CreateStart(_installation.Desktop!.ExecutionPath, Home);
        start.ArgumentList.Add("codex://threads/" + id);
        cancellation.ThrowIfCancellationRequested();
        using var request = Process.Start(start) ?? throw new IOException("The approved Linux thread-open process could not be started.");
        request.BeginOutputReadLine(); request.BeginErrorReadLine();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await request.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        VerifyOriginal();
        if (request.ExitCode != 0) throw new IOException("The approved Linux desktop did not accept the thread-open request.");
        before.Flush();
    }
    internal static bool Verify(int id, string home, ulong started, string[] arguments)
    {
        if (!Owned.TryGetValue(id, out var launched)) return false;
        lock (launched._sync)
        {
            if (launched._disposed || launched.Home != home || launched.Started != started || launched._process.HasExited ||
                arguments.Length != 1 || arguments[0] != launched.ArgumentZero && arguments[0] != LinuxClientDiscovery.DesktopPath ||
                !launched._installation.Desktop!.IsProcessImage((uint)id)) throw LinuxPeerProcess.Refused();
            LinuxPeerProcess.VerifyArguments(arguments[0] == launched.ArgumentZero ? new[] { LinuxClientDiscovery.DesktopPath } : arguments);
            return true;
        }
    }
    public void WaitForExit(CancellationToken cancellation) => _process.WaitForExitAsync(cancellation).GetAwaiter().GetResult();
    private bool OriginalListenerReady(CancellationToken cancellation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process.HasExited || LinuxPeerProcess.StartTime(Id) != Started || !_installation.Desktop!.IsProcessImage((uint)Id)) throw LinuxPeerProcess.Refused();
            var expected = Path.Combine(Home, "ipc/ipc.sock"); var listening = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines("/proc/" + Id + "/net/unix"))
            {
                cancellation.ThrowIfCancellationRequested(); var fields = line.Split((char[]?)null, 8, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 8 && fields[7] == expected && fields[4] == "0001" &&
                    uint.TryParse(fields[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var flags) && (flags & 0x10000) != 0 &&
                    ulong.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var inode) && inode != 0)
                    listening.Add("socket:[" + inode.ToString(CultureInfo.InvariantCulture) + "]");
            }
            if (listening.Count == 0) return false;
            foreach (var descriptor in Directory.EnumerateFiles("/proc/" + Id + "/fd"))
            {
                cancellation.ThrowIfCancellationRequested();
                if (new FileInfo(descriptor).LinkTarget is string target && listening.Contains(target)) return true;
            }
            return false;
        }
    }
    public async Task WaitForReadyAsync(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { while (!OriginalListenerReady(deadline.Token)) await Task.Delay(50, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("The original Linux desktop has not opened its native IPC listener; existing socket paths and account data are unchanged."); }
    }
    public async Task CloseAsync(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await WaitForReadyAsync(deadline.Token).ConfigureAwait(false);
            await using var stream = await LinuxDesktopConnection.ConnectAsync(Home, deadline.Token).ConfigureAwait(false);
            if (LinuxDesktopConnection.PeerId(stream) != Id || _display == null || _display != Environment.GetEnvironmentVariable("DISPLAY") ||
                _authority != Environment.GetEnvironmentVariable("XAUTHORITY") || _userHome != Environment.GetEnvironmentVariable("HOME")) throw LinuxPeerProcess.Refused();
            using var children = LinuxProcessChildren.Capture(Id, deadline.Token);
            await Task.Run(() =>
            {
                using var windows = new LinuxX11Quit(_display, deadline.Token);
                windows.Request(Id, stream.Flush);
            }, deadline.Token).ConfigureAwait(false);
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (_process.ExitCode != 0) throw new IOException("The intended Linux desktop did not exit normally; account credentials must remain unchanged.");
            await children.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("The intended Linux desktop has not completed normal shutdown; account credentials must remain unchanged."); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; Owned.TryRemove(new System.Collections.Generic.KeyValuePair<int, LinuxDesktopLaunch>(Id, this));
            _process.Dispose(); _installation.Dispose();
        }
    }
}
