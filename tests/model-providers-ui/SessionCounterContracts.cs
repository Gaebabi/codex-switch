using CodexAccountDesk;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class SessionCounterContracts
{
    internal static void Verify(string output, List<string> checks)
    {
        foreach (var language in new[] { "en", "ko", "ja", "zh-CN", "zh-TW", "es", "fr", "de", "pt-BR" })
        foreach (var dark in new[] { true, false })
        foreach (var count in new int?[] { null, 0, 3 })
        {
            Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
            var panel = new SessionUsagePanel();
            panel.Refresh(new SessionActivity(true, 3, 0, 6, Queued: 0, Goals: null, Errors: count), theme);
            panel.Measure(new Size(476, 30)); panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height)); panel.UpdateLayout();
            if (panel.Children.Count != 4 || !panel.Fits() || panel.ActualWidth > 476 || panel.ActualHeight > 21) throw new Exception("Four session counters must fit the minimum window.");
            var cell = (StackPanel)panel.Children[3]; var value = (TextBlock)cell.Children[1]; var label = (TextBlock)cell.Children[0];
            if (label.Text != Loc.T("sessions.errors") || value.Text != (count?.ToString(Loc.Culture) ?? "—")) throw new Exception("Error count/unknown translation is incorrect.");
            var expected = ((SolidColorBrush)theme.Color(count > 0 ? "warning" : count.HasValue ? "text" : "muted")).Color;
            if (((SolidColorBrush)value.Foreground).Color != expected || cell.ToolTip == null) throw new Exception("Errors must use warning color and explain their scope.");
            if (count == 3 && language is "en" or "ko")
            {
                var background = new Border { Background = theme.Color("canvas"), Padding = new Thickness(12, 8, 12, 8), Child = panel };
                background.Measure(new Size(500, 40)); background.Arrange(new Rect(0, 0, 500, 40)); background.UpdateLayout();
                var bitmap = new RenderTargetBitmap(500, 40, 96, 96, PixelFormats.Pbgra32); bitmap.Render(background);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "session-errors-" + language + "-" + (dark ? "dark" : "light") + ".png")); encoder.Save(file);
            }
            checks.Add(language + "/" + (dark ? "dark" : "light") + "/errors " + (count?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + ": current count, warning color and four-counter bounds");
        }
    }
}
