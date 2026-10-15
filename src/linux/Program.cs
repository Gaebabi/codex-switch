using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CodexAccountDesk;

internal static class Program
{
    internal const string PreviewMessage = "msg.linux_preview_accounts_unavailable";
    internal static int DiagnosticExitCode { get; set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        { Console.Error.WriteLine("This source preview requires Linux x64 or ARM64."); return 1; }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountDesk");
        var home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        string? evidence = null, queueReport = null; var explicitHome = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--help") { Console.WriteLine("CodexSwitch [--data-dir <directory>] [--codex-home <directory>] [--verify-ui <directory> | --verify-queues <report>]. Queue diagnostics require an explicit --codex-home. Linux source preview; account/client/handoff support is still being verified."); return 0; }
            if (i + 1 >= args.Length) throw new ArgumentException("Command-line option needs a value.");
            var name = args[i]; var value = Path.GetFullPath(args[++i]);
            switch (name) { case "--data-dir": data = value; break; case "--codex-home": home = value; explicitHome = true; break; case "--verify-ui": evidence = value; break; case "--verify-queues": queueReport = value; break; default: throw new ArgumentException("Unknown command-line option."); }
        }
        if (queueReport != null)
        {
            if (!explicitHome || evidence != null) throw new ArgumentException("Queue verification requires an explicit home and cannot be combined with UI verification.");
            return QueueDiagnostics.VerifyAsync(home, queueReport).GetAwaiter().GetResult();
        }
        using var single = new Mutex(true, "CodexAccountDesk-" + SecureFiles.Hash(Environment.UserName + "\n" + (evidence ?? data))[..24], out var first);
        if (!first) return 0;
        try
        {
            var host = new DesktopHost(data, home, "Codex Switch", new UnavailableLinuxResources(), null, KeyModifiers.Control, "/",
                new DesktopAppearance("DejaVu Sans", () => true, 8), () => DiagnosticExitCode, PreviewMessage);
            var builder = AppBuilder.Configure(() => new DeskApp(host, evidence == null ? null : SyntheticDashboard.Accounts(),
                evidence == null ? null : window => LinuxUiEvidence.RenderAsync(window, evidence))).UsePlatformDetect().LogToTrace();
            if (evidence != null) builder.With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software } });
            return builder.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally { VaultProtection.ReleaseKeys(); }
    }
}

internal sealed class UnavailableLinuxResources : IResourceMonitor
{
    private static ResourceReading Reading => new(null, Problem: "msg.linux_preview_resources_unavailable", Temperature: TemperatureReading.Missing(TemperatureState.PlatformUnsupported, "Linux"));
    public HardwareUsage Current { get; } = new(DateTimeOffset.UtcNow, Reading, Reading, Reading, Reading, Array.Empty<GpuResource>(), 0);
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
