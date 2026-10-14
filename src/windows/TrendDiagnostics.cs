using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal static class TrendDiagnostics
{
    public static void CaptureScenarios(string directory)
    {
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        foreach (var language in new[] { "ko", "en" })
        foreach (var dark in new[] { true, false })
        foreach (var scenario in new[] { "depletion-first", "reset-first", "collecting" })
        {
            Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
            var resetAt = now.AddHours(scenario == "reset-first" ? 6 : 18).ToUnixTimeSeconds();
            var rate = scenario == "reset-first" ? 2 : 7;
            var points = scenario == "collecting" ? new[] { new UsageObservation(now, 52, resetAt, 0) } : Enumerable.Range(0, 121)
                .Select(i => new UsageObservation(now.AddMinutes(i - 120), 52 - rate * 2 + rate * i / 60, resetAt, 0)).ToArray();
            var quota = new UsageWindow { UsedPercent = 52, Minutes = 10080, ResetsAt = resetAt };
            var body = new StackPanel { Margin = new Thickness(24) };
            body.Children.Add(new TextBlock { Text = "Illustrative observations · " + scenario, FontSize = 12, Foreground = theme.Color("muted"), Margin = new Thickness(0, 0, 0, 16) });
            body.Children.Add(new TextBlock { Text = quota.PeriodLabel + "  ·  " + Loc.F("사용 {0:0.#}%  ·  잔여 {1:0.#}%", quota.UsedPercent, quota.Remaining), FontSize = 14, Foreground = theme.Color("text"), Margin = new Thickness(0, 0, 0, 14) });
            var outlook = new UsageOutlook(quota, points, theme, 24); outlook.Update(now); body.Children.Add(outlook);
            var window = new Window { Width = 720, Height = 435, Left = -12000, Top = -12000, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                Background = theme.Color("surface"), FontFamily = theme.Font, Content = body, UseLayoutRounding = true, SnapsToDevicePixels = true };
            window.Show(); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap(720, 435, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, language + "-" + (dark ? "dark" : "light") + "-" + scenario + ".png"))) encoder.Save(output);
            window.Close();
        }
        File.WriteAllText(Path.Combine(directory, "scope.json"), JsonSerializer.Serialize(new { scope = "Illustrative observations for visual validation only; no real account or usage state was modified.", generatedAtUtc = now, count = 12 }));
        Loc.SetLanguage("ko");
    }
}
