using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CodexAccountDesk;

var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop client contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Rejected<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected client rejection was not observed."); }
Check("Lifecycle/discovery/trust share one UI/domain-independent assembly", () =>
{
    var assembly = typeof(RuntimeLocator).Assembly;
    Assert(assembly.GetName().Name == "CodexSwitch.DesktopClient" && typeof(MacDesktop).Assembly == assembly &&
        typeof(DesktopShutdown).Assembly == assembly && typeof(OwnedProcessJob).Assembly == assembly);
    Assert(!assembly.GetReferencedAssemblies().Any(a => a.Name is "CodexSwitch" or "CodexSwitch.Core" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
});
Check("Invalid chat IDs are rejected before any launch", () =>
    Rejected<InvalidDataException>(() => RuntimeLocator.OpenDesktopThread("", "not-a-chat-id")));
if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
{
    Check("Absent app launch is refused without a fallback", () =>
        Rejected<FileNotFoundException>(() => RuntimeLocator.LaunchDesktop(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-client"))));
    Check("Absent app thread-open is refused", () =>
        Rejected<FileNotFoundException>(() => RuntimeLocator.OpenDesktopThread(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-client"), Guid.NewGuid().ToString())));
    if (OperatingSystem.IsWindows())
    {
        Check("Empty Windows executable cannot report a running client", () => Assert(!RuntimeLocator.IsDesktopRunning("")));
        Check("Windows process image lookup keeps the live executable identity and excludes an exited process", () =>
        {
            var image = WindowsProcessImage.Read(Environment.ProcessId);
            Assert(image.HasValue && image.Value.CreationTime > 0 && image.Value.MatchesPath(Environment.ProcessPath!));
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            var start = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "/d", "/q", "/c", "exit", "0" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The disposable process did not start.");
            Assert(process.WaitForExit(5000) && WindowsProcessImage.Read(process.Id) == null);
        });
        Check("Windows discovery distinguishes the desktop from a CLI running inside its installation", () =>
        {
            const string package = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1117.3838.0_x64__2p2nqsd0c76g0\app";
            const string unpackaged = @"C:\Users\fixture\AppData\Local\OpenAI\Codex";
            Assert(RuntimeLocator.IsWindowsDesktopExecutable(Path.Combine(package, "ChatGPT.exe")));
            Assert(RuntimeLocator.IsWindowsDesktopExecutable(Path.Combine(package, "Codex.exe")));
            Assert(!RuntimeLocator.IsWindowsDesktopExecutable(Path.Combine(package, "resources", "codex.exe")));
            Assert(RuntimeLocator.IsWindowsDesktopExecutable(Path.Combine(unpackaged, "ChatGPT.exe")));
            Assert(!RuntimeLocator.IsWindowsDesktopExecutable(Path.Combine(unpackaged, "bin", "version", "codex.exe")));
        });
        Check("Cancelled Windows discovery refuses even valid explicit paths", () =>
        {
            const string cli = "CODEX_ACCOUNT_DESK_CLI";
            const string desktop = "CODEX_ACCOUNT_DESK_DESKTOP";
            var previousCli = Environment.GetEnvironmentVariable(cli);
            var previousDesktop = Environment.GetEnvironmentVariable(desktop);
            try
            {
                Environment.SetEnvironmentVariable(cli, Environment.ProcessPath!);
                Environment.SetEnvironmentVariable(desktop, Environment.ProcessPath!);
                Rejected<OperationCanceledException>(() => RuntimeLocator.FindAsync(new CancellationToken(true)).GetAwaiter().GetResult());
            }
            finally
            {
                Environment.SetEnvironmentVariable(cli, previousCli);
                Environment.SetEnvironmentVariable(desktop, previousDesktop);
            }
        });
    }
}
else
{
    if (OperatingSystem.IsLinux())
    {
        Check("Unknown Linux package metadata cannot admit discovery", () =>
            Rejected<IOException>(() => LinuxClientDiscovery.VerifyPackageStatus("", "unverified", "unknown")));
        Check("Empty Linux executable cannot report a running client", () => Assert(!RuntimeLocator.IsDesktopRunning("")));
    }
    else
    {
        Check("Unsupported discovery rejects before native calls", () =>
            Rejected<PlatformNotSupportedException>(() => RuntimeLocator.FindAsync(CancellationToken.None).GetAwaiter().GetResult()));
        Check("Unsupported running probe cannot enter Windows enumeration", () =>
            Rejected<PlatformNotSupportedException>(() => RuntimeLocator.IsDesktopRunning("")));
    }
    Check("Unsupported launch cannot start an arbitrary executable", () =>
        Rejected<PlatformNotSupportedException>(() => RuntimeLocator.LaunchDesktop("/bin/false")));
    Check("Unsupported thread-open cannot start an arbitrary executable", () =>
        Rejected<PlatformNotSupportedException>(() => RuntimeLocator.OpenDesktopThread("/bin/false", Guid.NewGuid().ToString())));
    Check("Unsupported shutdown cannot enter Restart Manager", () =>
        Rejected<PlatformNotSupportedException>(() => DesktopShutdown.CloseAsync("/bin/false", CancellationToken.None).GetAwaiter().GetResult()));
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks,
    scope = "Client assembly and refusal contracts only; no installed client discovery, real process launch/quit, personal home/account or native handoff acceptance." }));
