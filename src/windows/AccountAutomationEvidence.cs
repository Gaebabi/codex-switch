using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexAccountDesk;

internal static class AccountAutomationEvidence
{
    internal static async Task CaptureAsync(MainWindow window, AccountService service, string directory)
    {
        var root = Path.GetFileName(Path.GetDirectoryName(service.Vault.DirectoryPath));
        if (root == null || !root.StartsWith("codex-ui-evidence-", StringComparison.Ordinal) || service.Accounts.Length != 4)
            throw new InvalidOperationException("Only the disposable fictional native UI vault is allowed");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var checks = new List<string>(); var pictures = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        void Capture(object owner, string name)
        {
            var type = owner is Window ? typeof(Window) : typeof(IntPtr);
            typeof(NativeUiEvidence).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { type, typeof(string) }, null)!.Invoke(null, new[] { owner, Path.Combine(directory, name) });
            pictures.Add(name);
        }
        void Render() { typeof(MainWindow).GetMethod("RenderAccounts", fields)!.Invoke(window, null); typeof(MainWindow).GetMethod("UpdateControls", fields)!.Invoke(window, null); window.UpdateLayout(); }
        ContextMenu OpenMenu()
        {
            var button = Descendants<Button>(window).Single(b => b.Name == "AutomaticSwitchButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return button.ContextMenu ?? throw new InvalidOperationException("Account switching menu did not open");
        }
        var accounts = service.Vault.Data.Accounts.ToArray();
        var originals = accounts.Select(p => (p.Plan, p.SubscriptionActiveUntil, p.Error)).ToArray();
        var data = service.Vault.Data;
        var oldEnabled = data.AutomaticSwitchEnabled; var oldCustom = data.AutomaticSwitchUsePriority;
        var oldThreshold = data.AutomaticSwitchThreshold; var oldPriority = data.AutomaticSwitchPriority.ToArray();
        var ready = (bool)typeof(MainWindow).GetField("_ready", fields)!.GetValue(window)!;
        typeof(MainWindow).GetField("_ready", fields)!.SetValue(window, true);
        try
        {
            // The preceding warning scenario intentionally leaves a global error visible.
            // Reset only that fictional status before measuring this independent scenario.
            typeof(MainWindow).GetMethod("SetStatus", fields)!.Invoke(window, new object[] { "", false, Array.Empty<object?>() });
            for (var index = 0; index < accounts.Length; index++)
            {
                accounts[index].Plan = new[] { "prolite", "pro", "promax", "free" }[index];
                accounts[index].SubscriptionActiveUntil = index == 3 ? null : DateTimeOffset.UtcNow.AddDays(3 + index);
                accounts[index].Error = index == 3 ? "Fictional expired login. Sign in again." : null;
            }
            foreach (var dark in new[] { true, false })
            {
                var theme = dark ? "dark" : "light";
                data.AutomaticSwitchEnabled = false; data.AutomaticSwitchUsePriority = false;
                data.AutomaticSwitchThreshold = 10; data.AutomaticSwitchPriority.Clear();
                foreach (var language in new[] { "en", "ko" })
                {
                    window.PrepareUiEvidence(language, dark); window.Width = 500; window.Height = 360; Render(); await Task.Delay(160);
                    var name = language + "-" + theme + "-account-automation-";
                    var cards = Descendants<AccountTankCard>(window).ToArray();
                    var observed = cards.Select(c => JsonSerializer.SerializeToElement(c.ProbeBounds())).ToArray();
                    Check(observed.Select(c => c.GetProperty("displayedPlan").GetString()).ToHashSet().SetEquals(new[] { "Pro 100", "Pro 200", "Pro 500", "Free" }) && observed.All(c => c.GetProperty("metadataFits").GetBoolean()), name + "numbered tiers and metadata fit");
                    var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToArray();
                    Check(texts.Contains(Loc.T("subscription.ends")) && texts.Contains(Loc.T("subscription.left")), name + "subscription date and days have headings");
                    var button = Descendants<Button>(window).Single(b => b.Name == "AutomaticSwitchButton");
                    var sessions = Descendants<SessionUsagePanel>(window).Single();
                    var a = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    var b = sessions.TransformToAncestor(window).TransformBounds(new Rect(sessions.RenderSize));
                    Check(button.IsEnabled && ((TextBlock)button.Content).Text == Loc.T("auto.off") + " ▾" && !a.IntersectsWith(b) && a.Right <= window.ActualWidth, name + "explicit Off label remains separate from counters");
                    typeof(MainWindow).GetProperty(nameof(MainWindow.EvidenceReloginAccount), fields)!.SetValue(window, null);
                    Descendants<AccountWarningButton>(window).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(window.EvidenceReloginAccount == accounts[3].Key, name + "warning requests only the selected fictional login");
                    Capture(window, name + "500x360.png");
                }
                window.PrepareUiEvidence("en", dark); window.Width = 600; window.Height = 440; Render();
                var prefix = "en-" + theme + "-account-automation-";
                var menu = OpenMenu(); var enabled = (MenuItem)menu.Items[0];
                Check(!enabled.IsChecked, prefix + "automatic switching starts Off");
                enabled.IsChecked = true; enabled.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                Check(new AccountVault(service.Vault.DirectoryPath).Data.AutomaticSwitchEnabled, prefix + "On persists through the actual menu action");
                var card = Descendants<AccountTankCard>(window).Single(c => ((TextBlock)typeof(AccountTankCard).GetField("_plan", fields)!.GetValue(c)!).Text == "Free");
                card.MenuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var accountMenu = card.MenuButton.ContextMenu!;
                var priority = accountMenu.Items.OfType<MenuItem>().Single(i => i.Items.Count == 4);
                ((MenuItem)priority.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); accountMenu.IsOpen = false;
                Check(AccountAutomation.OrderedAccounts(service)[0].Key == accounts[3].Key && new AccountVault(service.Vault.DirectoryPath).Data.AutomaticSwitchUsePriority, prefix + "account menu persists custom first priority");
                menu = OpenMenu();
                var threshold = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == Loc.F("auto.threshold", 20));
                threshold.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                Check(new AccountVault(service.Vault.DirectoryPath).Data.AutomaticSwitchThreshold == 20, prefix + "threshold selection persists");
                Render(); await Task.Delay(120); Capture(window, prefix + "enabled.png");
                menu = OpenMenu(); await Task.Delay(120);
                Check(((MenuItem)menu.Items[0]).IsChecked && menu.Items.OfType<MenuItem>().Single(i => i.Header as string == Loc.T("auto.custom")).IsChecked && menu.Items.OfType<MenuItem>().Single(i => i.Header as string == Loc.F("auto.threshold", 20)).IsChecked, prefix + "reopened menu reflects On custom mode and threshold");
                Capture(((HwndSource)PresentationSource.FromVisual(menu)!).Handle, prefix + "menu.png");
                var ranking = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == Loc.T("auto.order"));
                ranking.IsSubmenuOpen = true; await Task.Delay(120);
                Check(((MenuItem)ranking.Items[0]).Header as string == Loc.F("auto.ranked_account", 1, accounts[3].DisplayName), prefix + "ranked account submenu reflects the chosen order");
                Capture(((HwndSource)PresentationSource.FromVisual((MenuItem)ranking.Items[0])!).Handle, prefix + "order.png");
                menu.IsOpen = false;
            }
            File.WriteAllText(Path.Combine(directory, "account-automation-receipt.json"), JsonSerializer.Serialize(new {
                schema = 1, os = "Windows", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                appVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(3), passed = checks.Count, failed = 0, checks, images = pictures,
                scope = "Actual own Windows app and menu HWNDs. Fictional account/quota/subscription inputs; browser intent intercepted by the existing evidence mode. No live login, Codex shutdown, Continue, personal screen or account access." }));
        }
        finally
        {
            for (var index = 0; index < accounts.Length; index++) (accounts[index].Plan, accounts[index].SubscriptionActiveUntil, accounts[index].Error) = originals[index];
            data.AutomaticSwitchEnabled = oldEnabled; data.AutomaticSwitchUsePriority = oldCustom; data.AutomaticSwitchThreshold = oldThreshold;
            data.AutomaticSwitchPriority = oldPriority.ToList(); service.Vault.Save();
            typeof(MainWindow).GetField("_ready", fields)!.SetValue(window, ready);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
