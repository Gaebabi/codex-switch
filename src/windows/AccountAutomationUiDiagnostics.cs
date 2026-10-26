using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal static class AccountAutomationUiDiagnostics
{
    // No Window.Show, native capture, browser, Codex discovery or live account access.
    public static void AddTests(Action<string, Action> test, Action<bool, string> assert, string directory)
    {
        test("Tray-only startup parses without requesting a window or changing data paths", () =>
        {
            var options = Program.Options.Parse(new[] { "--start-hidden", "--data-dir", directory });
            assert(options.StartHidden && options.DataDirectory == System.IO.Path.GetFullPath(directory) && !options.ExitExisting && options.SwitchAccount == null,
                "Hidden startup changed its operation or data identity");
            var refused = false;
            try { Program.Options.Parse(new[] { "--start-hidden", "--switch-account", "fictional" }); } catch (ArgumentException) { refused = true; }
            assert(refused, "Hidden startup accepted an account-switch action");
        });
        test("Missing Codex home stays unavailable and monitor shutdown completes", () =>
        {
            var monitor = new DesktopSessionMonitor(System.IO.Path.Combine(directory, "missing-monitor-home"));
            monitor.Start();
            System.Threading.Tasks.Task.Delay(100).GetAwaiter().GetResult();
            monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            assert(!monitor.Current.Available, "A missing home became an available desktop");
        });
        test("Warning activation requests one reauthentication and retains hover details", () =>
        {
            var requests = 0;
            var button = new AccountWarningButton("fictional@example.invalid", "Fictional expired login", new(false), () => requests++);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            assert(requests == 1 && !((ToolTip)button.ToolTip).IsOpen, "Warning click did not request one login or left a popup open");
            assert(((ToolTip)button.ToolTip).Content.ToString()!.Contains("Fictional expired login", StringComparison.Ordinal), "Full diagnostic disappeared");
        });
        test("Toolbar preferences save On/Off and threshold while preserving the recovery plan", () =>
        {
            if (Application.Current == null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var vault = new AccountVault(System.IO.Path.Combine(directory, "automation-ui"));
            var pending = new AccountSwitchPlan { SourceAccountKey = "source", TargetAccountKey = "target", Automatic = true, DesktopRestartRequired = true };
            vault.Data.PendingSwitch = pending;
            var service = new AccountService(vault, System.IO.Path.Combine(directory, "absent-automation-home"));
            var window = new MainWindow(service, new Program.Options { UiEvidenceDirectory = directory, CodexHome = service.CodexHome, DataDirectory = vault.DirectoryPath });
            var menu = window.BuildAutomaticSwitchMenu();
            var enabled = (MenuItem)menu.Items[0]; enabled.IsChecked = true; enabled.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            assert(new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchEnabled, "On was not persisted");
            var threshold = menu.Items.OfType<MenuItem>().Last(); threshold.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            assert(new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchThreshold == 20, "Threshold was not persisted");
            var custom = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == Loc.T("auto.custom"));
            custom.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            assert(new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchUsePriority, "Custom priority mode was not persisted");
            var optimal = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == Loc.T("auto.optimal"));
            optimal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            assert(!new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchUsePriority, "Automatic selection mode was not persisted");
            enabled.IsChecked = false; enabled.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var loaded = new AccountVault(vault.DirectoryPath).Data;
            assert(!loaded.AutomaticSwitchEnabled && loaded.PendingSwitch?.Id == pending.Id && loaded.PendingSwitch.DesktopRestartRequired, "Off discarded the pending recovery");
            window.Close();
        });
        test("Recovery remains reachable on the target before account application", () =>
        {
            if (Application.Current == null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var vault = new AccountVault(System.IO.Path.Combine(directory, "recovery-ui"));
            vault.Data.Accounts.AddRange(SyntheticDashboard.Accounts());
            vault.Data.Accounts[1].Error = "Fictional expired login";
            vault.Data.PendingSwitch = new() { SourceAccountKey = "synthetic-0", TargetAccountKey = "synthetic-1", DesktopRestartRequired = true };
            var service = new AccountService(vault, System.IO.Path.Combine(directory, "absent-recovery-home"));
            typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, "synthetic-0");
            var window = new MainWindow(service, new Program.Options { UiEvidenceDirectory = directory, CodexHome = service.CodexHome, DataDirectory = vault.DirectoryPath });
            typeof(MainWindow).GetField("_ready", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            typeof(MainWindow).GetMethod("RenderAccounts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var root = (FrameworkElement)window.Content; window.Content = null;
            root.Measure(new Size(600, 400)); root.Arrange(new Rect(0, 0, 600, 400)); root.UpdateLayout();
            bool HasRecovery(DependencyObject root)
            {
                if (root is Button { IsEnabled: true } button && button.Content is StackPanel stack && stack.Children.OfType<TextBlock>().Any(t => t.Text == Loc.T("switch.recover"))) return true;
                return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Any(i => HasRecovery(VisualTreeHelper.GetChild(root, i)));
            }
            assert(HasRecovery(root), "Pending target lost its recovery action while source is current");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_switching", flags)!.SetValue(window, true);
            typeof(MainWindow).GetField("_queuedKey", flags)!.SetValue(window, "synthetic-1");
            typeof(MainWindow).GetField("_switchPhase", flags)!.SetValue(window, SwitchPhase.Launching);
            foreach (var attempt in new[] { 1, 2 })
            {
                vault.Data.PendingSwitch!.AutomaticRecoveryAttempts = attempt;
                typeof(MainWindow).GetMethod("RenderAccounts", flags)!.Invoke(window, null);
                root.Measure(new Size(600, 400)); root.Arrange(new Rect(0, 0, 600, 400)); root.UpdateLayout();
                bool HasSwitching(DependencyObject element)
                {
                    if (element is Button { IsEnabled: false } button && button.Content is StackPanel stack && stack.Children.OfType<TextBlock>().Any(t => t.Text == Loc.T("전환 중"))) return true;
                    return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(element)).Any(i => HasSwitching(VisualTreeHelper.GetChild(element, i)));
                }
                assert(!HasRecovery(root) && HasSwitching(root), "Automatic retry exposed Recover or an enabled switch action");
            }
            typeof(MainWindow).GetField("_switching", flags)!.SetValue(window, false);
            typeof(MainWindow).GetField("_queuedKey", flags)!.SetValue(window, null);
            typeof(MainWindow).GetMethod("RenderAccounts", flags)!.Invoke(window, null);
            root.Measure(new Size(600, 400)); root.Arrange(new Rect(0, 0, 600, 400)); root.UpdateLayout();
            assert(HasRecovery(root), "Exhausted retries lost manual recovery");
            void ActivateWarning(DependencyObject element)
            {
                if (element is AccountWarningButton warning) warning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) ActivateWarning(VisualTreeHelper.GetChild(element, i));
            }
            ActivateWarning(root);
            assert(window.EvidenceReloginAccount == "synthetic-1", "Warning callback lost the requested account identity");
            window.Close();
        });
    }
}
