using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal static class TankDiagnostics
{
    public static void CaptureScenarios(string directory)
    {
        Directory.CreateDirectory(directory); var now = DateTimeOffset.UtcNow;
        foreach (var language in new[] { "ko", "en" })
        foreach (var dark in new[] { true, false })
        foreach (var unavailable in new[] { false, true })
        {
            Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
            var body = new DockPanel { Margin = new Thickness(18) };
            var caption = new TextBlock { Text = "Illustrative gauge levels · no real accounts", FontSize = 12, Foreground = theme.Color("muted"), Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(caption, Dock.Top); body.Children.Add(caption);
            var tiles = new WrapPanel();
            var index = 0;
            foreach (var remaining in unavailable ? new double?[] { null, 20 } : new double?[] { 100, 52, 9, 0 })
            {
                var profile = new AccountProfile { IsCurrent = !unavailable && index == 1, SubscriptionActiveUntil = index == 3 || remaining == null ? null : now.AddDays(index + 5), SubscriptionLastChecked = now,
                    Plan = remaining == null ? "unknown" : new[] { "pro", "plus", "prolite", "unknown" }[index], Usage = remaining == null ? null : new UsageSnapshot { CheckedAt = now, AvailableResets = new int?[] { 2, 1, 0, null }[index], Buckets = new()
                    { new() { Id = "codex", Primary = new() { Minutes = 10080, UsedPercent = 100 - remaining.Value, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } } };
                if (profile.Usage != null) { profile.Usage.Buckets[0].CreditBalance = new string?[] { "1250.5", "0", null, "12500" }[index]; profile.Usage.Buckets[0].UnlimitedCredits = index == 2; }
                if (unavailable && remaining != null) profile.Usage!.Buckets[0].Secondary = new() { Minutes = 300, UsedPercent = 25, ResetsAt = now.AddHours(4).ToUnixTimeSeconds() };
                if (profile.Usage != null) UsageHistory.Record(profile, profile.Usage);
                var header = new AccountCardHeader(remaining == null ? "Unavailable" : remaining.Value + "% sample", "", theme, new StackPanel());
                var detail = new Button { Content = "···", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = theme.Color("text"), Padding = new Thickness(5, 0, 5, 0) }; header.Actions.Children.Add(detail);
                var card = new AccountTankCard(profile, AccountTankCard.SelectQuota(profile), theme, header, detail, _ => { }) { Width = 360, Height = 264, Margin = new Thickness(0, 0, 12, 12) };
                tiles.Children.Add(card);
                index++;
            }
            body.Children.Add(tiles);
            var window = new Window { Width = 780, Height = unavailable ? 370 : 620, Left = -12000, Top = -12000, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                Background = theme.Color("canvas"), FontFamily = theme.Font, Content = body, UseLayoutRounding = true, SnapsToDevicePixels = true };
            window.Show(); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, language + "-" + (dark ? "dark" : "light") + (unavailable ? "-unavailable" : "-levels") + ".png"))) encoder.Save(output);
            window.Close();
        }
        File.WriteAllText(Path.Combine(directory, "scope.json"), JsonSerializer.Serialize(new { scope = "Illustrative gauge levels, unknown quota, and multiple quota windows; no real account state was modified.", count = 8 }));
        Loc.SetLanguage("ko");
    }
}
