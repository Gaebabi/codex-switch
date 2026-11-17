using CodexAccountDesk;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FolderIndexUiContracts
{
    internal static void Verify(string output, List<string> checks)
    {
        var before = Loc.Language;
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { false, true })
            foreach (var width in new[] { 500, 800 })
            {
                Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
                var requested = new List<string>(); var panel = new DriveUsagePanel(() => { }, _ => { }); panel.IndexPermissionRequested += requested.Add;
                var readings = new[]
                {
                    new DriveReading(@"C:\", "System", 156L << 30, 512L << 30, Index: new(FolderIndexAccess.Ready, 4200, 165000)),
                    new DriveReading(@"D:\", "Projects", 318L << 30, 1024L << 30, Index: new(FolderIndexAccess.JournalMissing, 1200, 42000)),
                    new DriveReading(@"E:\", "Archive", 1240L << 30, 2048L << 30, Index: new(FolderIndexAccess.Ready, 8500, 240000))
                };
                var canvas = new Grid { Width = width, Height = 74, Background = theme.Color("canvas") }; panel.Margin = new Thickness(12, 20, 12, 0); canvas.Children.Add(panel);
                void Layout() { canvas.Measure(new Size(width, 74)); canvas.Arrange(new Rect(0, 0, width, 74)); canvas.UpdateLayout(); }
                panel.Refresh(readings, theme); Layout();
                var action = Find<Button>(panel).Single(b => b.Name == "FolderIndexPermissionButton" && b.Visibility == Visibility.Visible);
                if (!panel.Fits || action.ActualWidth < 16 || System.Windows.Automation.AutomationProperties.GetName(action) != Loc.T("folders.enable_index")) throw new Exception("Index permission action is clipped or unnamed.");
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (!requested.SequenceEqual(new[] { @"D:\" })) throw new Exception("Permission action targeted another volume.");
                panel.SetIndexPermissionBusy(@"D:\", true); panel.Refresh(readings, theme); Layout();
                if (action.IsEnabled || !ReferenceEquals(action, Find<Button>(panel).Single(b => b.Name == "FolderIndexPermissionButton" && b.Visibility == Visibility.Visible))) throw new Exception("An in-progress permission action was replaced or re-enabled.");
                panel.SetIndexPermissionBusy(@"D:\", false); if (!action.IsEnabled) throw new Exception("Permission cancellation did not release the action.");
                Capture(canvas, Path.Combine(output, $"folder-index-lock-{language}-{dark}-{width}.png"), width);
                readings[1] = readings[1] with { Index = new(FolderIndexAccess.Ready, 1224, 42800), MeasuringFolders = true };
                panel.Refresh(readings, theme); Layout();
                if (action.Visibility != Visibility.Collapsed || Find<System.Windows.Shapes.Ellipse>(panel).Count(e => e.Name == "FolderScanSpinner" && e.Visibility == Visibility.Visible) != 1) throw new Exception("Granted access must replace the lock with only the measured drive's activity.");
                readings[1] = readings[1] with { MeasuringFolders = false }; readings[2] = readings[2] with { Index = new(FolderIndexAccess.Unsupported, 8500, 240000) };
                panel.Refresh(readings, theme); Layout();
                if (Find<Button>(panel).Any(b => b.Name == "FolderIndexPermissionButton" && b.Visibility == Visibility.Visible) || Find<System.Windows.Shapes.Ellipse>(panel).Any(e => e.Name == "FolderScanSpinner" && e.Visibility == Visibility.Visible)) throw new Exception("Idle/unsupported states must not request pointless elevation or keep spinning.");
                checks.Add($"{language}/{dark}/{width}: index lock targets one drive, stays disabled while requesting, recovers after cancellation and clears with idle activity.");
            }
        }
        finally { Loc.SetLanguage(before); }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(FrameworkElement root, string output, int width)
    {
        var image = new RenderTargetBitmap(width, 74, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(output); encoder.Save(file);
    }
}
