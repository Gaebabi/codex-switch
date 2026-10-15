using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;

namespace CodexAccountDesk;

internal sealed class DeskApp : Application
{
    private readonly DesktopHost _host;
    private readonly AccountProfile[]? _preview;
    private readonly Func<DeskWindow, Task>? _diagnostic;
    public DeskApp(DesktopHost host, AccountProfile[]? preview = null, Func<DeskWindow, Task>? diagnostic = null)
    { _host = host; _preview = preview; _diagnostic = diagnostic; }
    public override void Initialize() { Name = "Codex Switch"; Loc.SetLanguage("en"); Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new DeskWindow(_host, _preview, _diagnostic); desktop.MainWindow = window;
            var activation = this.TryGetFeature<IActivatableLifetime>();
            if (activation != null) activation.Activated += (_, args) => { if (args.Kind == ActivationKind.Reopen) { window.Show(); window.Activate(); } };
            desktop.ShutdownRequested += (_, args) => { if (!window.Exiting) { args.Cancel = true; _ = window.ExitAsync(); } };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
