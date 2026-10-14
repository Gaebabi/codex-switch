using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Platform;

namespace CodexAccountDesk;

internal static class Program
{
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CodexAccountDesk");
    public static string CodexHome { get; private set; } = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string? PreviewDirectory { get; private set; }
    public static string? WindowProbePath { get; private set; }
    public static int DiagnosticExitCode { get; set; }
    [STAThread]
    public static int Main(string[] args)
    {
        string? nativeReport = null, queueReport = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--help") { Console.WriteLine("CodexSwitch [--data-dir <directory>] [--codex-home <directory>] [--verify-macos <report> | --verify-queues <report> | --preview-macos <directory> | --probe-window <report>]"); return 0; }
            if (i + 1 >= args.Length) throw new ArgumentException("Command-line option needs a value.");
            var value = Path.GetFullPath(args[++i]);
            switch (args[i - 1]) { case "--data-dir": DataDirectory = value; break; case "--codex-home": CodexHome = value; break;
                case "--verify-macos": nativeReport = value; break; case "--verify-queues": queueReport = value; break; case "--preview-macos": PreviewDirectory = value; break;
                case "--probe-window": WindowProbePath = value; break;
                default: throw new ArgumentException("Unknown command-line option."); }
        }
        if (!OperatingSystem.IsMacOS()) { Console.Error.WriteLine("Use the Windows WPF project on Windows. This application is the macOS edition."); return 1; }
        if ((nativeReport == null ? 0 : 1) + (queueReport == null ? 0 : 1) + (PreviewDirectory == null ? 0 : 1) + (WindowProbePath == null ? 0 : 1) > 1)
            throw new ArgumentException("Choose one diagnostic mode.");
        if (nativeReport != null) { try { return MacDiagnostics.VerifyAsync(nativeReport).GetAwaiter().GetResult(); } finally { VaultProtection.ReleaseKeys(); } }
        if (queueReport != null) return QueueDiagnostics.VerifyAsync(CodexHome, queueReport).GetAwaiter().GetResult();
        using var single = new Mutex(true, "CodexAccountDesk-" + SecureFiles.Hash(Environment.UserName + "\n" + (WindowProbePath ?? PreviewDirectory ?? DataDirectory))[..24], out var first);
        if (!first) return 0;
        try { return AppBuilder.Configure<DeskApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown); }
        finally { VaultProtection.ReleaseKeys(); }
    }
}

internal sealed class DeskApp : Application
{
    public override void Initialize() { Name = "Codex Switch"; Loc.SetLanguage("en"); Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new DeskWindow(Program.PreviewDirectory == null && Program.WindowProbePath == null ? null : MacDiagnostics.Profiles(4)); desktop.MainWindow = window;
            if (Program.WindowProbePath != null) window.Title = "Codex Switch — Window probe";
            var activation = this.TryGetFeature<IActivatableLifetime>();
            if (activation != null) activation.Activated += (_, args) => { if (args.Kind == ActivationKind.Reopen) { window.Show(); window.Activate(); } };
            desktop.ShutdownRequested += (_, args) => { if (!window.Exiting) { args.Cancel = true; _ = window.ExitAsync(); } };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
