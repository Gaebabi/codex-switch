using CodexAccountDesk;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class HomeLayoutContracts
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Verify(AccountService service, string output, List<string> checks)
    {
        var originals = service.Vault.Data.Accounts.ToArray();
        var originalCurrent = service.CurrentKey;
        var originalAutomatic = service.Vault.Data.AutomaticSwitchEnabled;
        var now = DateTimeOffset.UtcNow;
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { true, false })
            foreach (var count in new[] { 0, 1, 4, 8 })
            {
                service.Vault.Data.Accounts.Clear();
                for (var i = 0; i < count; i++)
                {
                    var profile = new AccountProfile { Key = "home-" + i, Alias = i == 1 ? "A long fictional account name for layout verification" : "Demo " + (i + 1), IsCurrent = i == 0,
                        Plan = "pro", Error = i == 2 ? "Fictional login needs attention" : null,
                        SubscriptionActiveUntil = i % 2 == 0 ? now.AddDays(2) : null,
                        Usage = new() { CheckedAt = now, AvailableResets = i, Buckets = new() { new() { Id = "codex", CreditBalance = "1000",
                            Primary = new() { UsedPercent = i == 1 ? 95 : 30, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } } };
                    service.Vault.Data.Accounts.Add(profile);
                }
                typeof(AccountService).GetProperty("CurrentKey")!.SetValue(service, count == 0 ? null : "home-0");
                var window = new MainWindow(service, new Program.Options { DataDirectory = service.Vault.DirectoryPath, CodexHome = service.CodexHome, UiEvidenceDirectory = output });
                typeof(MainWindow).GetField("_ready", Private)!.SetValue(window, true);
                window.PrepareUiEvidence(language, dark);
                foreach (var size in new[] { new Size(500, 360), new Size(800, 560) })
                {
                    var root = (FrameworkElement)window.Content;
                    root.Width = size.Width; root.Height = size.Height;
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    typeof(MainWindow).GetMethod("LayoutCards", Private)!.Invoke(window, null);
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    var cards = (List<AccountTankCard>)typeof(MainWindow).GetField("_tankCards", Private)!.GetValue(window)!;
                    var accountScroll = (ScrollViewer)typeof(MainWindow).GetField("_scroll", Private)!.GetValue(window)!;
                    if (cards.Count != count) throw new Exception("Account count changed in home layout.");
                    foreach (var card in cards)
                    {
                        var cardBounds = card.TransformToAncestor(accountScroll).TransformBounds(new Rect(card.RenderSize));
                        if (count <= 4 && (cardBounds.Top < -1 || cardBounds.Bottom > accountScroll.ActualHeight + 1)) throw new Exception("Four or fewer tank cards must remain fully visible without scrolling.");
                        if (card.Children[0] is not TankGaugeSurface gauge || gauge.Remaining is null) throw new Exception("Tank representation was replaced.");
                        var geometry = JsonSerializer.SerializeToElement(card.ProbeBounds());
                        if (!geometry.GetProperty("actionsFit").GetBoolean() || !geometry.GetProperty("numberFits").GetBoolean()) throw new Exception("Tank number or account action is clipped: " + language + "/" + count + "/" + size + "/" + card.ActualWidth + "x" + card.ActualHeight + "/" + geometry);
                        foreach (var text in Descendants(card).OfType<TextBlock>().Where(Displayed))
                        {
                            var bounds = text.TransformToAncestor(card).TransformBounds(new Rect(text.RenderSize));
                            if (bounds.Left < -1 || bounds.Right > card.ActualWidth + 1 || bounds.Top < -1 || bounds.Bottom > card.ActualHeight + 1)
                                throw new Exception("Home text leaves its tank: " + text.Text);
                        }
                    }
                    if (count == 8 && size.Width == 500)
                    {
                        var scroll = (ScrollViewer)typeof(MainWindow).GetField("_scroll", Private)!.GetValue(window)!;
                        if (scroll.ScrollableHeight <= 0) throw new Exception("Many accounts need reachable vertical overflow instead of compressed cards.");
                        scroll.ScrollToBottom(); root.UpdateLayout();
                        if (scroll.VerticalOffset <= 0) throw new Exception("The last accounts cannot be reached by scrolling.");
                        scroll.ScrollToTop(); root.UpdateLayout();
                    }
                    var gear = (Button)typeof(MainWindow).GetField("_homeActions", Private)!.GetValue(window)!;
                    var gearBounds = gear.TransformToAncestor(root).TransformBounds(new Rect(gear.RenderSize));
                    if (gearBounds.Right > size.Width || gearBounds.Top < 0 || gearBounds.Bottom > 40 || gear.ActualWidth < 28) throw new Exception("Settings is outside the titlebar.");
                    service.Vault.Data.AutomaticSwitchEnabled = true;
                    typeof(MainWindow).GetMethod("UpdateControls", Private)!.Invoke(window, null); root.UpdateLayout();
                    var state = (TextBlock)typeof(MainWindow).GetField("_automaticState", Private)!.GetValue(window)!;
                    var counter = (FrameworkElement)typeof(MainWindow).GetField("_sessionCount", Private)!.GetValue(window)!;
                    var stateBounds = state.TransformToAncestor(root).TransformBounds(new Rect(state.RenderSize));
                    var counterBounds = counter.TransformToAncestor(root).TransformBounds(new Rect(counter.RenderSize));
                    if (state.Visibility != Visibility.Visible || state.Text != Loc.T("auto.on") || stateBounds.Right > size.Width || stateBounds.IntersectsWith(counterBounds))
                        throw new Exception("Enabled automatic switching must remain legible outside Settings.");
                    service.Vault.Data.AutomaticSwitchEnabled = originalAutomatic;
                    typeof(MainWindow).GetMethod("UpdateControls", Private)!.Invoke(window, null); root.UpdateLayout();
                    checks.Add(language + "/" + (dark ? "dark" : "light") + "/" + count + " accounts/" + size.Width + "x" + size.Height + ": tank, number, text and actions fit; gear reachable");
                }
                if (count == 4)
                {
                    var menu = window.HomeActionsMenu();
                    menu.Measure(new Size(340, 520)); menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, "home-settings-" + language + "-" + (dark ? "dark" : "light") + ".png")); encoder.Save(stream);
                }
            }
        }
        finally
        {
            service.Vault.Data.Accounts.Clear(); service.Vault.Data.Accounts.AddRange(originals);
            service.Vault.Data.AutomaticSwitchEnabled = originalAutomatic;
            typeof(AccountService).GetProperty("CurrentKey")!.SetValue(service, originalCurrent);
        }
    }
    private static bool Displayed(TextBlock text)
    {
        for (DependencyObject? element = text; element != null; element = VisualTreeHelper.GetParent(element))
            if (element is UIElement control && control.Visibility != Visibility.Visible) return false;
        return text.ActualWidth > 0 && text.ActualHeight > 0;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
