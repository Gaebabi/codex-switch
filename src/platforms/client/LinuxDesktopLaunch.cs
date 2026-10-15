using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private LinuxDesktopLaunch(LinuxClientInstallation installation, Process process, string home, string argumentZero, ulong started)
    {
        _installation = installation; _process = process; Id = process.Id; Home = home; ArgumentZero = argumentZero; Started = started;
        _display = Environment.GetEnvironmentVariable("DISPLAY"); _authority = Environment.GetEnvironmentVariable("XAUTHORITY"); _userHome = Environment.GetEnvironmentVariable("HOME");
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
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = home, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment.Clear();
            foreach (var name in new[] { "HOME", "PATH", "LANG", "LC_ALL", "LC_CTYPE", "DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS",
                "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "XDG_SESSION_TYPE", "DESKTOP_SESSION",
                "SSH_AUTH_SOCK", "PULSE_SERVER", "PIPEWIRE_REMOTE", "TZ" })
                if (Environment.GetEnvironmentVariable(name) is string value) start.Environment[name] = value;
            start.Environment["CODEX_HOME"] = home;
            LinuxPeerProcess.VerifyEnvironment(start.Environment.Select(entry => entry.Key + "=" + entry.Value).ToArray(), home);
            cancellation.ThrowIfCancellationRequested();
            process = Process.Start(start) ?? throw new IOException("The approved Linux desktop could not be started.");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var launched = new LinuxDesktopLaunch(installation, process, home, executable, LinuxPeerProcess.StartTime(process.Id));
            if (!Owned.TryAdd(process.Id, launched)) throw LinuxPeerProcess.Refused();
            return launched;
        }
        catch { process?.Dispose(); installation.Dispose(); throw; }
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
    public async Task CloseAsync(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var stream = await LinuxDesktopConnection.ConnectAsync(Home, deadline.Token).ConfigureAwait(false);
            if (LinuxDesktopConnection.PeerId(stream) != Id || _display == null || _display != Environment.GetEnvironmentVariable("DISPLAY") ||
                _authority != Environment.GetEnvironmentVariable("XAUTHORITY") || _userHome != Environment.GetEnvironmentVariable("HOME")) throw LinuxPeerProcess.Refused();
            using var children = LinuxProcessChildren.Capture(Id, deadline.Token);
            await Task.Run(() =>
            {
                using var windows = new LinuxX11Close(_display, deadline.Token);
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
