using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexAccountDesk;

internal static class CreditEvidence
{
    internal static async Task CaptureAsync(MainWindow owner, AccountService service, string directory)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var options = (Program.Options)typeof(MainWindow).GetField("_options", fields)!.GetValue(owner)!;
        if (options.UiEvidenceDirectory == null || !Path.GetFileName(Path.GetDirectoryName(service.Vault.DirectoryPath))!.StartsWith("codex-ui-evidence-", StringComparison.Ordinal) || service.Accounts.Length != 4)
            throw new InvalidOperationException("Credit evidence requires the disposable native UI fixture.");
        Directory.CreateDirectory(directory);
        var checks = new List<string>(); var images = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void Capture(object window, string name)
        {
            var type = window is Window ? typeof(Window) : typeof(IntPtr);
            typeof(NativeUiEvidence).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { type, typeof(string) }, null)!
                .Invoke(null, new[] { window, Path.Combine(directory, name) }); images.Add(name);
        }
        void Render() { typeof(MainWindow).GetMethod("RenderAccounts", fields)!.Invoke(owner, null); typeof(MainWindow).GetMethod("UpdateControls", fields)!.Invoke(owner, null); owner.UpdateLayout(); }
        ContextMenu Menu()
        {
            var button = Descendants<Button>(owner).Single(b => b.Name == "AutomaticSwitchButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return button.ContextMenu!;
        }
        void Popup(ContextMenu menu, string name) => Capture((PresentationSource.FromVisual(menu) as HwndSource ?? throw new InvalidOperationException("Credit menu has no native HWND")).Handle, name);
        var data = service.Vault.Data; var accounts = data.Accounts.ToArray();
        var saved = accounts.Select(p => (p.Usage, p.CreditHistory, p.SubscriptionActiveUntil, p.Error, p.RequiresApply)).ToArray();
        var oldAllow = data.AllowCreditUse; var oldEnabled = data.AutomaticSwitchEnabled; var oldCustom = data.AutomaticSwitchUsePriority;
        var oldThreshold = data.AutomaticSwitchThreshold; var oldAttempt = data.LastAutomaticSwitchAttempt; var oldPending = data.PendingSwitch;
        Check(oldAllow, "disposable legacy preferences allow credits by default");
        var current = service.CurrentKey; var oldReady = typeof(MainWindow).GetField("_ready", fields)!.GetValue(owner);
        typeof(MainWindow).GetField("_ready", fields)!.SetValue(owner, true);
        typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, accounts[0].Key);
        try
        {
            foreach (var language in new[] { "en", "ko" }) foreach (var dark in new[] { true, false })
            {
                var now = DateTimeOffset.UtcNow; var prefix = language + "-" + (dark ? "dark" : "light") + "-";
                for (var index = 0; index < accounts.Length; index++)
                {
                    accounts[index].Error = null; accounts[index].RequiresApply = false;
                    accounts[index].Usage = new() { CheckedAt = now, OrdinaryUsageAllowed = true, Buckets = new() { new() { Id = "codex", CreditBalance = "1000", UnlimitedCredits = false,
                        Primary = new() { UsedPercent = index == 1 ? 20 : index == 2 ? 10 : 100, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } };
                }
                accounts[1].SubscriptionActiveUntil = now.AddDays(-1); accounts[2].SubscriptionActiveUntil = now.AddDays(2);
                data.PendingSwitch = null; data.AllowCreditUse = true; data.AutomaticSwitchEnabled = false; data.AutomaticSwitchUsePriority = false; data.AutomaticSwitchThreshold = 10; data.LastAutomaticSwitchAttempt = null;
                service.Vault.Save();
                owner.PrepareUiEvidence(language, dark); owner.Width = 600; owner.Height = 440; Render();
                var menu = Menu(); await Task.Delay(120);
                var toggle = menu.Items.OfType<MenuItem>().Single(i => i.Name == "AllowCreditUse");
                Check(toggle.IsChecked && new AccountVault(service.Vault.DirectoryPath).Data.AllowCreditUse, prefix + "default credit permission is visibly On");
                Popup(menu, prefix + "allowed.png");
                toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(80);
                Check(!data.AllowCreditUse && !new AccountVault(service.Vault.DirectoryPath).Data.AllowCreditUse, prefix + "actual credit toggle persists Off");
                Check(AccountAutomation.CreditStopTarget(service, false, now)?.Key == accounts[0].Key, prefix + "disabled automatic switching permits protective stop selection");
                Popup(menu, prefix + "protected.png"); menu.IsOpen = false;
                data.AutomaticSwitchEnabled = true; Render(); menu = Menu(); await Task.Delay(120);
                Check(AccountAutomation.Next(service, now)?.Key == accounts[1].Key && ((MenuItem)menu.Items[1]).Header as string == Loc.F("auto.next", accounts[1].DisplayName) && ((MenuItem)menu.Items[1]).Opacity == 1, prefix + "past but usable subscription is the visible next account");
                Popup(menu, prefix + "past-expiry.png"); menu.IsOpen = false;
                accounts[0].Usage!.Buckets[0].Primary!.ResetsAt = now.ToUnixTimeSeconds();
                Render(); menu = Menu(); await Task.Delay(120);
                Check(AccountAutomation.Next(service, now)?.Key == accounts[1].Key &&
                    AutomaticSwitchPolicy.Select(service.Accounts, 10, now)?.Key == accounts[1].Key &&
                    AccountAutomation.CreditStopTarget(service, false, now) == null &&
                    ((MenuItem)menu.Items[1]).Header as string == Loc.F("auto.next", accounts[1].DisplayName),
                    prefix + "fresh zero awaiting reset keeps the visible eligible handoff instead of a credit pause");
                Popup(menu, prefix + "reset-boundary.png"); menu.IsOpen = false;
                foreach (var account in accounts) account.Usage!.Buckets[0].Primary!.UsedPercent = 100;
                Check(AccountAutomation.CreditStopTarget(service, false, now)?.Key == accounts[0].Key, prefix + "no eligible quota requests a protected stop");
                data.PendingSwitch = new() { SourceAccountKey = accounts[0].Key, TargetAccountKey = accounts[0].Key, CreditProtectionPause = true,
                    PreventDepletedSourceRelaunch = true, AccountApplied = true, Phase = SwitchPhase.CreditPaused, DesktopRestartRequired = true,
                    Chats = new() { new() { Id = "fictional-credit-chat", OriginalTitle = "Fictional saved work", NameSaved = true } } };
                typeof(MainWindow).GetMethod("SetStatus", fields)!.Invoke(owner, new object[] { "credits.paused", true, Array.Empty<object?>() }); Render();
                await owner.Dispatcher.InvokeAsync(owner.UpdateLayout, DispatcherPriority.Render); await Task.Delay(120);
                Check(Descendants<AccountTankCard>(owner).All(card => ((TextBlock)typeof(AccountTankCard).GetField("_number", fields)!.GetValue(card)!).Text == "0") &&
                    Descendants<Button>(owner).Any(b => b.Tag as string == "account-switch-action" && b.IsEnabled && Descendants<TextBlock>(b).Any(t => t.Text == Loc.T("switch.recover"))), prefix + "saved paused chats retain an enabled recovery action");
                Capture(owner, prefix + "paused.png");
                var profile = accounts[0]; profile.CreditHistory = new(); var balance = 1000m;
                for (var i = 0; i <= 288; i++)
                {
                    if (i > 0) balance -= i % 13 == 0 ? 1.5m : 0.25m;
                    if (i == 144) balance += 100;
                    CreditHistory.Record(profile, new() { CheckedAt = now.AddMinutes((i - 288) * 5), Buckets = new() { new() { Id = "codex", UnlimitedCredits = false, CreditBalance = i == 230 ? null : balance.ToString(CultureInfo.InvariantCulture) } } });
                }
                profile.Usage!.Buckets[0].CreditBalance = balance.ToString(CultureInfo.InvariantCulture);
                var history = new AccountDetailWindow(owner, _ => { }, () => Task.CompletedTask) { ShowInTaskbar = false };
                try
                {
                    history.Refresh(service.Accounts.Single(p => p.Key == profile.Key), new Burn0Theme(dark), owner.Resources, 24); history.Show(); history.UpdateLayout();
                    var picker = Descendants<Button>(history).Single(b => b.Name == "HistoryMetricPicker"); picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var credit = picker.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Header as string == Loc.T("credits.history"));
                    credit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); picker.ContextMenu.IsOpen = false; history.UpdateLayout(); await Task.Delay(120);
                    var chart = Descendants<CreditTrendChart>(history).Single(); var scroll = Descendants<ScrollViewer>(history).Single();
                    Check(chart.ActualWidth > 200 && chart.ActualHeight >= 140 && scroll.ExtentWidth <= scroll.ViewportWidth + 1, prefix + "actual credit graph and metrics fit without horizontal overflow");
                    Check(Descendants<TextBlock>(history).Any(t => t.Text == Loc.T("credits.balance")) && Descendants<TextBlock>(history).Any(t => t.Text == Loc.T("credits.observed")) && CreditHistory.ObservedUse(profile.CreditHistory.Single(), 24, DateTimeOffset.UtcNow) > 0,
                        prefix + "credit units and measured decreases replace percentage forecasts");
                    Capture(history, prefix + "history.png");
                    profile.CreditHistory = new() { new() { BucketId = "codex", Points = new() { new(now, balance, null, null, 0) } } };
                    history.SelectCredits(service.Accounts.Single(p => p.Key == profile.Key), new Burn0Theme(dark), owner.Resources, 24); history.UpdateLayout(); await Task.Delay(80);
                    Check(CreditHistory.ObservedUse(profile.CreditHistory.Single(), 24, DateTimeOffset.UtcNow) == null, prefix + "one credit balance cannot fabricate consumption");
                    Capture(history, prefix + "empty.png");
                    history.Width = 560; history.Height = 380; history.UpdateLayout(); await Task.Delay(80);
                    var minimum = Descendants<ScrollViewer>(history).Single(); minimum.ScrollToEnd(); history.UpdateLayout();
                    Check(minimum.ExtentWidth <= minimum.ViewportWidth + 1 && minimum.VerticalOffset > 0, prefix + "minimum credit window preserves horizontal fit and reachable footer");
                    minimum.ScrollToHome(); history.UpdateLayout(); await Task.Delay(80); Capture(history, prefix + "minimum.png");
                }
                finally { history.Close(); }
            }
        }
        finally
        {
            for (var i = 0; i < accounts.Length; i++) { accounts[i].Usage = saved[i].Usage; accounts[i].CreditHistory = saved[i].CreditHistory; accounts[i].SubscriptionActiveUntil = saved[i].SubscriptionActiveUntil; accounts[i].Error = saved[i].Error; accounts[i].RequiresApply = saved[i].RequiresApply; }
            data.AllowCreditUse = oldAllow; data.AutomaticSwitchEnabled = oldEnabled; data.AutomaticSwitchUsePriority = oldCustom; data.AutomaticSwitchThreshold = oldThreshold; data.LastAutomaticSwitchAttempt = oldAttempt; data.PendingSwitch = oldPending;
            typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, current);
            typeof(MainWindow).GetField("_ready", fields)!.SetValue(owner, oldReady);
        }
        File.WriteAllText(Path.Combine(directory, "credit-receipt.json"), JsonSerializer.Serialize(new { schema = 1, os = "Windows", architecture = "X64", runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            version = typeof(Program).Assembly.GetName().Version!.ToString(3), passed = checks.Count, failed = 0, checks, images,
            scope = "Actual owned native Windows windows/popups with fictional credits, quota and persisted paused-state inputs. No account login/switch, billing mutation, live Codex shutdown or Continue. Normal owned-process shutdown is a separate native fixture test." }));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
