using CodexAccountDesk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class SettingsUiContracts
{
    internal static void Verify(AccountService service, string output, List<string> checks)
    {
        var languageBefore = Loc.Language;
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { true, false })
            {
                Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
                var owner = new MainWindow(service, new Program.Options { DataDirectory = service.Vault.DirectoryPath, CodexHome = service.CodexHome, UiEvidenceDirectory = output });
                owner.PrepareUiEvidence(language, dark);
                new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
                var provider = new ModelProvidersWindow(owner, theme, service, _ => { }, _ => { });
                var providerRoot = Render(provider, output, $"settings-provider-{language}-{dark}", 540, 580);
                var key = (PasswordBox)typeof(ModelProvidersWindow).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;
                if (key.FontSize != PanelLayout.BodySize || key.MinHeight != 34) throw new Exception("Provider input typography diverged from settings scale.");
                var drive = new DriveSettingsWindow(owner, theme, new[] { new DrivePreference { Root = @"D:\", Label = "Projects", Folders = new[] { new FolderPreference { Path = @"D:\Projects", Color = 0 } } } });
                var driveRoot = Render(drive, output, $"settings-drives-{language}-{dark}", 620, 460);
                if (drive.Selection.Length != 1 || drive.Selection[0].Folders![0].Path != @"D:\Projects") throw new Exception("Drive sections changed saved selection.");
                var now = DateTimeOffset.UtcNow;
                var profile = new AccountProfile { Key = "settings-fixture", Alias = "Demo account", Plan = "pro", Usage = new()
                { CheckedAt = now, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 42, Minutes = 10080, ResetsAt = now.AddDays(5).ToUnixTimeSeconds() }, CreditBalance = "1500" } } } };
                var detail = new AccountDetailWindow(owner, _ => { }, () => Task.CompletedTask);
                detail.Refresh(profile, theme, owner.Resources, 168);
                var detailRoot = Render(detail, output, $"settings-statistics-{language}-{dark}", 620, 520);
                if (!HasMetricScale(detailRoot)) throw new Exception("Statistics lost the common remaining-value scale.");
                provider.Close(); drive.Close(); detail.Close(); owner.Close();
                checks.Add($"{language}/{dark}: provider, drive and statistics surfaces retain typography, selected data and actual metric scale.");
            }
        }
        finally { Loc.SetLanguage(languageBefore); }
    }
    private static bool HasMetricScale(DependencyObject root)
    {
        if (root is TextBlock text && text.FontSize == PanelLayout.ValueSize && text.Text == "58%") return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (HasMetricScale(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
    private static FrameworkElement Render(Window window, string output, string name, int width, int height)
    {
        var root = (FrameworkElement)window.Content; window.Content = null;
        root.Resources = window.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(root, window.FontFamily);
        System.Windows.Documents.TextElement.SetForeground(root, window.Foreground);
        root.Width = width; root.Height = height; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        return root;
    }
}
