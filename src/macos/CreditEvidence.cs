using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexAccountDesk;

internal static class CreditEvidence
{
    public static async Task CaptureAsync(DeskWindow owner, string directory)
    {
        if (!Program.NativeUiEvidence || !OperatingSystem.IsMacOS()) throw new InvalidOperationException("Use isolated native Mac evidence.");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(Path.GetTempPath(), "mac-self-test-credits-" + Guid.NewGuid().ToString("N"));
        var vault = new AccountVault(temporary); var home = Path.Combine(temporary, "absent-home");
        var service = new AccountService(vault, home);
        var checks = new List<string>(); var images = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        async Task Capture(TopLevel window, string name) { await NativeUiCapture.SaveAsync(window, Path.Combine(directory, name)); images.Add(name); }
        async Task Ready(TopLevel window) { await Task.Delay(120); await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render); }
        ContextMenu Menu()
        {
            typeof(DeskWindow).GetMethod("OpenAutomaticSwitchMenu", fields)!.Invoke(owner, null);
            return owner.AutomaticSwitchButton.ContextMenu ?? throw new InvalidOperationException("Credit menu did not open.");
        }
        async Task Popup(ContextMenu menu, string name) => await Capture(TopLevel.GetTopLevel(menu) ?? throw new InvalidOperationException("Credit popup has no native root"), name);
        var oldService = typeof(DeskWindow).GetField("_service", fields)!.GetValue(owner);
        var oldReady = typeof(DeskWindow).GetField("_ready", fields)!.GetValue(owner);
        var oldStatus = typeof(DeskWindow).GetField("_statusMessage", fields)!.GetValue(owner);
        Check(vault.Data.AllowCreditUse, "disposable legacy preferences allow credits by default");
        try
        {
            typeof(DeskWindow).GetField("_service", fields)!.SetValue(owner, service);
            typeof(DeskWindow).GetField("_ready", fields)!.SetValue(owner, true);
            foreach (var language in new[] { "en", "ko" }) foreach (var dark in new[] { true, false })
            {
                var now = DateTimeOffset.UtcNow; var prefix = language + "-" + (dark ? "dark" : "light") + "-";
                vault.Data.Accounts.Clear();
                for (var i = 0; i < 3; i++)
                {
                    var id = "credit-native-fixture-" + i;
                    var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = id, email = "demo@example.invalid" }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = id, id_token = "e30." + claims + ".fictional", access_token = "fictional-access", refresh_token = "fictional-refresh" } });
                    var profile = vault.Upsert(auth); profile.Alias = i == 0 ? "Credit demo" : "Demo " + (i + 1); profile.Plan = "pro"; profile.IsCurrent = i == 0;
                    profile.Usage = new() { CheckedAt = now, OrdinaryUsageAllowed = true, Buckets = new() { new() { Id = "codex", CreditBalance = "1000", UnlimitedCredits = false,
                        Primary = new() { UsedPercent = i == 0 ? 100 : i == 1 ? 20 : 10, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } };
                    profile.SubscriptionActiveUntil = i == 1 ? now.AddDays(-1) : now.AddDays(2);
                }
                var accounts = vault.Data.Accounts.ToArray(); var current = accounts[0];
                typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, current.Key);
                vault.Data.Theme = dark ? "dark" : "light"; vault.Data.Language = language; vault.Data.TrendHours = 24;
                vault.Data.AllowCreditUse = true; vault.Data.AutomaticSwitchEnabled = false; vault.Data.AutomaticSwitchUsePriority = false; vault.Data.LastAutomaticSwitchAttempt = null; vault.Data.PendingSwitch = null; vault.Save();
                owner.Width = 600; owner.Height = 440; owner.Preview(language, dark, accounts); await Ready(owner);
                var menu = Menu(); await Ready(owner);
                var toggle = menu.ItemsSource!.Cast<object>().OfType<MenuItem>().Select(i => i.Header).OfType<ToggleSwitch>().Single(t => t.Name == "AllowCreditUse");
                Check(toggle.IsChecked == true && new AccountVault(vault.DirectoryPath).Data.AllowCreditUse, prefix + "default credit permission is visibly On");
                await Popup(menu, prefix + "allowed.png"); toggle.IsChecked = false; menu.Close(); await Ready(owner);
                Check(!vault.Data.AllowCreditUse && !new AccountVault(vault.DirectoryPath).Data.AllowCreditUse, prefix + "actual credit toggle persists Off");
                Check(AccountAutomation.CreditStopTarget(service, false, now)?.Key == current.Key, prefix + "disabled automatic switching permits protective stop selection");
                menu = Menu(); await Ready(owner); await Popup(menu, prefix + "protected.png"); menu.Close();
                vault.Data.AutomaticSwitchEnabled = true; owner.Preview(language, dark, accounts); menu = Menu(); await Ready(owner);
                var next = menu.ItemsSource!.Cast<object>().OfType<MenuItem>().Single(i => i.Header as string == Loc.F("auto.next", accounts[1].DisplayName));
                Check(AccountAutomation.Next(service, now)?.Key == accounts[1].Key && next.IsEnabled == false, prefix + "past but usable subscription is the visible next account");
                await Popup(menu, prefix + "past-expiry.png"); menu.Close();
                foreach (var account in accounts) account.Usage!.Buckets[0].Primary!.UsedPercent = 100;
                Check(AccountAutomation.CreditStopTarget(service, false, now)?.Key == current.Key, prefix + "no eligible quota requests a protected stop");
                vault.Data.PendingSwitch = new() { SourceAccountKey = current.Key, TargetAccountKey = current.Key, CreditProtectionPause = true, AccountApplied = true,
                    PreventDepletedSourceRelaunch = true, Phase = SwitchPhase.CreditPaused, DesktopRestartRequired = true, Chats = new() { new() { Id = "fictional-credit-chat", OriginalTitle = "Fictional saved work", NameSaved = true } } };
                typeof(DeskWindow).GetField("_statusMessage", fields)!.SetValue(owner, Loc.T("credits.paused")); owner.Preview(language, dark, accounts); await Ready(owner);
                Check(owner.GetVisualDescendants().OfType<Button>().Any(b => b.IsEnabled && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T("switch.recover"))), prefix + "saved paused chats retain an enabled recovery action");
                await Capture(owner, prefix + "paused.png");
                current.CreditHistory.Clear(); var balance = 1000m;
                for (var i = 0; i <= 288; i++)
                {
                    if (i > 0) balance -= i % 13 == 0 ? 1.5m : 0.25m;
                    if (i == 144) balance += 100;
                    CreditHistory.Record(current, new() { CheckedAt = now.AddMinutes((i - 288) * 5), Buckets = new() { new() { Id = "codex", UnlimitedCredits = false, CreditBalance = i == 230 ? null : balance.ToString(CultureInfo.InvariantCulture) } } });
                }
                current.Usage!.Buckets[0].CreditBalance = balance.ToString(CultureInfo.InvariantCulture);
                var history = new HistoryWindow(service, current.Key, new DeskTheme(dark, owner.Appearance)) { ShowInTaskbar = false };
                try
                {
                    history.Show(owner); await Ready(history);
                    var picker = history.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HistoryQuota"); picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var credit = picker.ContextMenu!.ItemsSource!.Cast<object>().OfType<MenuItem>().Single(i => i.Header as string == Loc.T("credits.history"));
                    credit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); picker.ContextMenu.Close(); await Ready(history);
                    var chart = history.GetVisualDescendants().OfType<CreditChart>().Single(); var scroll = history.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Check(chart.Bounds.Width > 200 && chart.Bounds.Height >= 140 && scroll.Extent.Width <= scroll.Viewport.Width + 1, prefix + "actual credit graph and metrics fit without horizontal overflow");
                    Check(history.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T("credits.balance")) && history.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T("credits.observed")) && CreditHistory.ObservedUse(current.CreditHistory.Single(), 24, DateTimeOffset.UtcNow) > 0, prefix + "credit units and measured decreases replace percentage forecasts");
                    await Capture(history, prefix + "history.png");
                    current.CreditHistory = new() { new() { BucketId = "codex", Points = new() { new(now, balance, null, null, 0) } } };
                    history.SelectCredits(); await Ready(history);
                    Check(CreditHistory.ObservedUse(current.CreditHistory.Single(), 24, DateTimeOffset.UtcNow) == null, prefix + "one credit balance cannot fabricate consumption");
                    await Capture(history, prefix + "empty.png");
                    history.Width = 500; history.Height = 300; await Ready(history);
                    var minimum = history.GetVisualDescendants().OfType<ScrollViewer>().First(); minimum.Offset = new Vector(0, minimum.Extent.Height); await Ready(history);
                    Check(minimum.Extent.Width <= minimum.Viewport.Width + 1 && minimum.Offset.Y > 0, prefix + "minimum credit window preserves horizontal fit and reachable footer");
                    minimum.Offset = default; await Ready(history); await Capture(history, prefix + "minimum.png");
                }
                finally { history.Close(); }
            }
            Check(!Directory.Exists(home), "credit fixture leaves its absent Codex home untouched");
        }
        finally
        {
            typeof(DeskWindow).GetField("_service", fields)!.SetValue(owner, oldService); typeof(DeskWindow).GetField("_ready", fields)!.SetValue(owner, oldReady);
            typeof(DeskWindow).GetField("_statusMessage", fields)!.SetValue(owner, oldStatus);
            VaultProtection.ReleaseKeys(); MacKeychain.DeleteDiagnosticKey(temporary); Directory.Delete(temporary, true);
        }
        File.WriteAllText(Path.Combine(directory, "credit-receipt.json"), JsonSerializer.Serialize(new { schema = 1, os = "macOS", architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
            version = typeof(Program).Assembly.GetName().Version!.ToString(3), passed = checks.Count, failed = 0, checks, images,
            scope = "Actual owned native Mac windows/popups with fictional credits, quota and paused-state inputs; software rendering. No owner account/login, network, client shutdown or billing mutation. Normal shutdown is separately verified with an owned Windows process fixture." }));
    }
}
