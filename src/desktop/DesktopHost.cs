using System;
using Avalonia.Input;

namespace CodexAccountDesk;

internal sealed record DesktopAppearance(string FontFamily, Func<bool> ReducedMotion);

internal sealed record DesktopHost(string DataDirectory, string CodexHome, string WindowTitle,
    IResourceMonitor Resources, Func<IDesktopMenuBar>? CreateMenuBar, KeyModifiers CommandModifier,
    string RootVolumeLabel, DesktopAppearance Appearance, Func<int> ExitCode);

internal interface IDesktopMenuBar : IDisposable
{
    void Add(string key, string label, Action action);
    void Separator();
    void SetItem(string key, string label, bool enabled);
    void Update(string title, string tooltip);
    MenuBarState Probe();
}

internal sealed record MenuBarState(string Title, bool Visible, bool Template, int MenuItems, double X, double Y, double Width, double Height);
