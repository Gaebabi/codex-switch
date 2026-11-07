using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexAccountDesk;

internal static class AccountWarningEvidence
{
    internal static async Task CaptureAsync(MainWindow window, AccountService service, string directory, bool baseline = false)
    {
        var checks = new List<string>(); var pictures = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        void Call(string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
        void Capture(object owner, string name)
        {
            var type = owner is Window ? typeof(Window) : typeof(IntPtr);
            typeof(NativeUiEvidence).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { type, typeof(string) }, null)!.Invoke(null, new[] { owner, Path.Combine(directory, name) });
            pictures.Add(name);
        }
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        var rootName = Path.GetFileName(Path.GetDirectoryName(service.Vault.DirectoryPath));
        if (rootName == null || !rootName.StartsWith("codex-ui-evidence-", StringComparison.Ordinal) || service.Vault.Data.Accounts.Count != 4)
            throw new InvalidOperationException("Only the isolated fictional UI evidence vault is allowed");
        var accounts = service.Vault.Data.Accounts.ToArray(); var oldErrors = accounts.Select(a => a.Error).ToArray(); var oldUsage = accounts[1].Usage;
        var drives = service.Vault.Data.Drives.ToArray();
        service.Vault.Data.Drives.Clear(); service.Vault.Data.Drives.Add(new() { Root = "C:\\", Label = "Synthetic disk" });
        try
        {
            foreach (var dark in new[] { true, false })
            {
                var name = "en-" + (dark ? "dark" : "light") + "-account-warning-";
                accounts[1].Error = "The saved login is unavailable. Sign in again."; accounts[1].Usage = null;
                accounts[2].Error = string.Join(" ", Enumerable.Repeat("Fictional refresh failure. Cached usage is retained; retry after the service becomes available.", 6));
                window.PrepareUiEvidence("en", dark); window.UpdateLayout();
                var panel = Only(Descendants<DriveUsagePanel>(window), "Disk monitor");
                panel.Refresh(new[] { new DriveReading("C:\\", "Synthetic disk", 80L * 1073741824, 250L * 1073741824) }, new Burn0Theme(dark));
                Call("SetRefreshStatus");
                foreach (var size in new[] { (600, 440), (500, 360) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(120);
                    var warnings = Descendants<Button>(window).Where(b => b.Name == "AccountWarningButton").ToArray();
                    Check(warnings.Length == (baseline ? 0 : 2), baseline ? "Original cards have no dedicated warning action" : "Only the two failed accounts expose a warning control");
                    Check(panel.IsVisible != baseline, baseline ? "Original warning replaces the disk monitor" : "Account refresh failures do not replace the disk monitor");
                    if (!baseline)
                    {
                        var hardware = Only(Descendants<HardwareUsagePanel>(window), "Hardware monitor");
                        Check(Bounds(hardware).Bottom <= Bounds(panel).Top && Bounds(panel).Bottom <= window.ActualHeight, "Hardware and disk rows fit without overlap at minimum size");
                        foreach (var warning in warnings)
                        {
                            var card = Ancestor<AccountTankCard>(warning); var title = (Grid)warning.Parent;
                            var label = Only(title.Children.OfType<TextBlock>(), "Account name label");
                            Check(warning.IsVisible && warning.IsEnabled && warning.Focusable && Bounds(warning).Right <= Bounds(label).Left + .5 && Bounds(warning).Top < Bounds(card).Top + 35,
                                "Each warning is a reachable compact control beside its account name");
                            Check(AutomationProperties.GetName(warning).Contains(label.Text, StringComparison.Ordinal) && AutomationProperties.GetHelpText(warning).Length > 0 &&
                                ReferenceEquals(warning.Style, window.Resources[typeof(Button)]),
                                "Warning name and full explanation are available to assistive technology");
                        }
                    }
                    Capture(window, name + size.Item1 + "x" + size.Item2 + ".png");
                }
                if (baseline) continue;
                window.Width = 600; window.Height = 440; window.UpdateLayout();
                var buttons = Descendants<Button>(window).Where(b => b.Name == "AccountWarningButton").ToArray();
                var longWarning = Only(buttons.Where(b => AutomationProperties.GetHelpText(b).Contains("Fictional refresh failure.", StringComparison.Ordinal)), "Long account error action");
                var tip = (ToolTip)longWarning.ToolTip;
                try
                {
                    longWarning.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    await Task.Delay(150);
                    Check(tip.IsOpen && !ToolTipService.GetIsEnabled(longWarning), "Hover opens the single account tooltip without a second system tooltip owner");
                    longWarning.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
                    Check(!tip.IsOpen, "Leaving a hover-only warning closes its explanation");
                    longWarning.Focus(); longWarning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                    longWarning.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
                    tip.ApplyTemplate();
                    // This same diagnostic is compiled against the unmodified historical
                    // app for baseline:true. Resolve the new hook only in this after branch.
                    var requestedLogin = typeof(MainWindow).GetProperty("EvidenceReloginAccount", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("Current native app has no account-specific login evidence hook");
                    Check(!tip.IsOpen && requestedLogin.GetValue(window) as string == accounts[2].Key,
                        "Click requests browser reauthentication for the selected account without opening a real login in evidence mode");
                    longWarning.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    await Task.Delay(150); tip.ApplyTemplate();
                    Check(tip.IsOpen && tip.Template.FindName("tipSurface", tip) is Border && tip.ActualWidth <= 340.5,
                        "Hover retains the themed wrapped full explanation and reauthentication hint");
                    var native = PresentationSource.FromVisual(tip) as HwndSource ?? throw new InvalidOperationException("Account tooltip has no native popup");
                    Capture(native.Handle, name + "tooltip.png");
                    longWarning.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(longWarning), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Check(!tip.IsOpen, "Escape dismisses the account explanation");
                    longWarning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    accounts[2].Error = null; Call("RenderAccounts"); window.UpdateLayout(); await Task.Delay(100);
                    Check(!tip.IsOpen && Descendants<Button>(window).Count(b => b.Name == "AccountWarningButton") == 1, "Successful refresh removes only that warning and closes its old popup");
                    accounts[1].Error = null; accounts[1].Usage = oldUsage; Call("RenderAccounts"); Call("SetRefreshStatus"); window.UpdateLayout();
                    Check(!Descendants<Button>(window).Any(b => b.Name == "AccountWarningButton") && panel.IsVisible, "Healthy accounts restore without stale warnings or hidden disks");
                    Call("SetStatus", "Synthetic application warning: operation needs attention.", true, Array.Empty<object?>()); window.UpdateLayout();
                    var status = Only(Descendants<TextBlock>(window).Where(t => t.Text == "Synthetic application warning: operation needs attention."), "Global warning label");
                    var globalLayoutsFit = true;
                    foreach (var size in new[] { (600, 440), (500, 360) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
                        globalLayoutsFit &= panel.IsVisible && status.IsVisible && Bounds(panel).Bottom <= Bounds(status).Top && Bounds(status).Bottom <= window.ActualHeight;
                    }
                    Check(globalLayoutsFit, "Global operation warnings fit below visible disks at normal and minimum size");
                    window.Width = 600; window.Height = 440; window.UpdateLayout();
                    Capture(window, name + "global.png");
                }
                finally { tip.IsOpen = false; }
            }
            File.WriteAllText(Path.Combine(directory, "account-warning-receipt.json"), JsonSerializer.Serialize(new {
                schema = 1, os = "Windows", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                appVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(3), baseline, failed = 0, passed = checks.Count, checks, images = pictures,
                scope = "Actual own Windows app/popup HWNDs, fictional failed/cached/healthy account and disk inputs only. No owner account, desktop, authentication, network or hardware action." }));
        }
        finally
        {
            for (var i = 0; i < accounts.Length; i++) accounts[i].Error = oldErrors[i]; accounts[1].Usage = oldUsage;
            service.Vault.Data.Drives.Clear(); service.Vault.Data.Drives.AddRange(drives);
        }
    }
    private static T Only<T>(IEnumerable<T> source, string description)
    { var values = source.ToArray(); return values.Length == 1 ? values[0] : throw new InvalidOperationException(description + ": expected one native control, observed " + values.Length); }
    private static T Ancestor<T>(DependencyObject owner) where T : DependencyObject
    { for (var parent = VisualTreeHelper.GetParent(owner); parent != null; parent = VisualTreeHelper.GetParent(parent)) if (parent is T value) return value; throw new InvalidOperationException("Expected account ancestor"); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
