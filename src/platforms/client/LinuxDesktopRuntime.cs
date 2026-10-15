using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class LinuxDesktopOwnershipException : InvalidOperationException
{
    public LinuxDesktopOwnershipException() : base("Quit the existing Codex normally, then use Start Codex here. An unmanaged desktop cannot be switched safely.") { }
}

internal sealed class LinuxDesktopRuntime : IDisposable
{
    private readonly string _home;
    private LinuxDesktopLaunch? _launch;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public LinuxDesktopRuntime(string home) { _home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)); }
    internal static int[] Running(CancellationToken cancellation = default)
    {
        var found = new List<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(directory), out var id)) continue;
            try
            {
                var status = LinuxPeerProcess.Read(id, "status", 16384).Split('\n');
                var uid = status.SingleOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal));
                if (uid == null || uid[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is not { Length: 4 } ids ||
                    ids.Any(value => !uint.TryParse(value, out var actual) || actual != LinuxPrivateFiles.UserId)) continue;
                if (new FileInfo(directory + "/exe").LinkTarget != LinuxClientDiscovery.DesktopPath) continue;
                var arguments = LinuxPeerProcess.Read(id, "cmdline", 32768).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (!arguments.Skip(1).Any(value => value.StartsWith("--type=", StringComparison.Ordinal))) found.Add(id);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return found.ToArray();
    }
    public bool RequireOwnedOrStopped(CancellationToken cancellation)
    {
        var running = Running(cancellation);
        if (running.Length == 0) return false;
        if (_launch == null || running.Length != 1 || running[0] != _launch.Id) throw new LinuxDesktopOwnershipException();
        _launch.VerifyOriginal();
        return true;
    }
    public async Task StartAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!RequireOwnedOrStopped(cancellation))
            {
                _launch?.Dispose(); _launch = null;
                _launch = LinuxDesktopLaunch.Start(_home, cancellation);
            }
            await _launch!.WaitForReadyAsync(cancellation).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async Task CloseAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (RequireOwnedOrStopped(cancellation)) await _launch!.CloseAsync(cancellation).ConfigureAwait(false);
            if (Running(cancellation).Length != 0) throw new LinuxDesktopOwnershipException();
            _launch?.Dispose(); _launch = null;
        }
        finally { _gate.Release(); }
    }
    public async Task OpenThreadAsync(string id, CancellationToken cancellation)
    {
        if (!Guid.TryParseExact(id, "D", out _)) throw new InvalidDataException("Invalid desktop thread ID.");
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!RequireOwnedOrStopped(cancellation)) throw new LinuxDesktopOwnershipException();
            await _launch!.OpenThreadAsync(id, cancellation).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { _launch?.Dispose(); _launch = null; }
}
