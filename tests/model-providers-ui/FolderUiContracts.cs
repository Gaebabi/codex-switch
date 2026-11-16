using CodexAccountDesk;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FolderUiContracts
{
    internal static void Verify(AccountService service, string output, List<string> checks)
    {
        var languageBefore = Loc.Language;
        try
        {
            foreach (var language in new[] { "ko", "en" })
            foreach (var dark in new[] { true, false })
            {
                Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
                var owner = new MainWindow(service, new Program.Options { DataDirectory = service.Vault.DirectoryPath, CodexHome = service.CodexHome, UiEvidenceDirectory = output });
                owner.PrepareUiEvidence(language, dark);
                new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
                var folders = new[] { new FolderPreference { Path = @"D:\Projects", Color = 0 }, new FolderPreference { Path = @"D:\Media", Color = 1 } };
                var dialog = new DriveSettingsWindow(owner, theme, new[] { new DrivePreference { Root = @"D:\", Label = "Data", Folders = folders } });
                if (!dialog.Selection.Single().Folders!.SequenceEqual(folders)) throw new Exception("Saved folder paths/colors were lost.");
                var content = (FrameworkElement)dialog.Content; dialog.Content = null;
                Capture(content, Path.Combine(output, $"folders-settings-{language}-{dark}.png"), theme, 620, 460, dialog);
                foreach (var picker in Find<Button>(content).Where(b => b.Tag is int))
                    if (!Find<Border>(picker).Any(b => b.ActualWidth == 18 && b.ActualHeight == 12 && b.Background is SolidColorBrush brush && brush.Color == DriveSettingsWindow.FolderBrush((int)picker.Tag).Color))
                        throw new Exception("The selected folder color is not visible in the closed picker.");
                var firstPicker = Find<Button>(content).First(b => b.Tag is int);
                ((MenuItem)firstPicker.ContextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (dialog.Selection.Single().Folders![0].Color != 2) throw new Exception("Choosing a color did not update the saved preference.");
                Find<Button>(content).First(b => Equals(b.Content, Loc.T("folders.add"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                content.UpdateLayout();
                if (Find<Button>(content).Count(b => b.Tag is int) != 3) throw new Exception("Add-folder did not create a path/color row.");
                var paths = Find<TextBox>(content).Where(t => Equals(t.ToolTip, Loc.T("folders.path"))).ToArray();
                paths[^1].Text = @"D:\Projects\Nested";
                try { _ = dialog.Selection; throw new Exception("Overlapping folders were accepted."); }
                catch (ArgumentException error) when (error.Message == "folders.overlap") { }
                Find<Button>(content).First(b => Equals(b.Content, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (dialog.Selection.Single().Folders!.Length != 2 || dialog.Selection.Single().Folders![0].Color != 1) throw new Exception("Removal changed remaining folder colors.");
                checks.Add($"{language}/{dark}: production folder settings load, add, overlap refusal and removal preserve paths/colors.");
                const long gib = 1073741824;
                var reading = new DriveReading(@"D:\", "Data", 300 * gib, 1000 * gib);
                var before = new DriveUsagePanel(() => { }); before.Refresh(new[] { reading }, theme);
                var opened = new List<string>();
                var after = new DriveUsagePanel(() => { }, opened.Add);
                var tracked = reading with { Folders = new[] { new FolderReading(folders[0].Path, 0, 240 * gib, FolderState.Available), new FolderReading(folders[1].Path, 1, 120 * gib, FolderState.Partial, 1) } };
                after.Refresh(new[] { tracked }, theme);
                var surface = new StackPanel { Background = theme.Color("canvas"), Margin = new Thickness(16) };
                surface.Children.Add(new TextBlock { Text = language == "ko" ? "폴더 미등록 드라이브" : "Drive without tracked folders", Foreground = theme.Color("text"), Margin = new Thickness(0, 0, 0, 6) }); surface.Children.Add(before);
                surface.Children.Add(new TextBlock { Text = language == "ko" ? "폴더별 점유량" : "Tracked folder allocation", Foreground = theme.Color("text"), Margin = new Thickness(0, 16, 0, 6) }); surface.Children.Add(after);
                var tip = (ResourceToolTip)after.Children.OfType<Grid>().First().ToolTip;
                var legend = (FrameworkElement)tip.Content; tip.Content = null; legend.Margin = new Thickness(8, 18, 8, 0); surface.Children.Add(legend);
                Capture(surface, Path.Combine(output, $"folders-gauge-{language}-{dark}.png"), theme, 620, 600);
                if (!before.Fits || !after.Fits || after.ActualHeight != 34) throw new Exception("Folder segments obscure the footer.");
                var text = string.Join("\n", Find<TextBlock>(legend).Select(t => t.Text));
                if (!text.Contains(folders[0].Path) || !text.Contains("24.0%") || !text.Contains(Loc.T("folders.partial")) || !text.Contains(Loc.T("folders.other"))) throw new Exception("Folder legend lost paths, percentages or states.");
                checks.Add($"{language}/{dark}: production gauge and tooltip render folder shares, partial state, other used space and free space.");
                var segments = Find<Button>(after).Where(b => b.Tag is string).ToArray();
                if (segments.Length != 4) throw new Exception("The gauge lost an actionable capacity segment.");
                foreach (var segment in segments) segment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!opened.SequenceEqual(new[] { folders[0].Path, folders[1].Path, @"D:\", @"D:\" })) throw new Exception("Gauge clicks opened a different folder or drive.");
                var folderTip = (ResourceToolTip)segments[0].ToolTip;
                var hover = (FrameworkElement)folderTip.Content; folderTip.Content = null;
                Capture(hover, Path.Combine(output, $"folders-hover-{language}-{dark}.png"), theme, 340, 145);
                var hoverText = string.Join("\n", Find<TextBlock>(hover).Select(t => t.Text));
                if (!hoverText.Contains(folders[0].Path) || !hoverText.Contains("24.0%") || !hoverText.Contains(Loc.T("folders.open"))) throw new Exception("Compact hover omitted its folder, share or click hint.");
                after.Refresh(new[] { tracked with { Free = 299 * gib } }, theme);
                if (!segments.SequenceEqual(Find<Button>(after).Where(b => b.Tag is string)) || !ReferenceEquals(folderTip, segments[0].ToolTip)) throw new Exception("Periodic updates replaced the hovered segment or tooltip.");
                checks.Add($"{language}/{dark}: compact hover, exact folder/root click targets and stable controls survive periodic refresh.");
                var busy = new DriveUsagePanel(() => { }, _ => { });
                busy.Refresh(new[] { tracked with { MeasuringFolders = true } }, theme);
                Capture(busy, Path.Combine(output, $"folders-scanning-{language}-{dark}.png"), theme, 500, 34);
                var spinner = Find<System.Windows.Shapes.Ellipse>(busy).Single(e => e.Name == "FolderScanSpinner");
                if (spinner.Visibility != Visibility.Visible || spinner.RenderSize.Width <= 0 || !busy.Fits)
                    throw new Exception("A measured drive must show its capacity-adjacent scan indicator without clipping.");
                busy.Refresh(new[] { tracked }, theme);
                if (spinner.Visibility != Visibility.Collapsed || !ReferenceEquals(spinner, Find<System.Windows.Shapes.Ellipse>(busy).Single(e => e.Name == "FolderScanSpinner")))
                    throw new Exception("A completed scan must hide its stable indicator.");
                checks.Add($"{language}/{dark}: drive scan spinner appears during measurement and clears without replacing controls.");
                var now = DateTimeOffset.UtcNow;
                foreach (var width in new[] { 500, 800 })
                {
                    var footer = new StackPanel { Margin = new Thickness(12), Background = theme.Color("canvas") };
                    var hardware = new HardwareUsagePanel { Margin = new Thickness(0, 0, 0, 6) };
                    hardware.Refresh(new(now, new(10), new(34), new(92), new(null), Array.Empty<GpuResource>(), 0,
                        new[] { new HardwareObservation(now.AddSeconds(-2), 18, 32, 80, null), new HardwareObservation(now, 10, 34, 92, null) }), theme);
                    var drives = new DriveUsagePanel(() => { }, _ => { });
                    drives.Refresh(new[] {
                        new DriveReading(@"C:\", "C:", 19 * gib, 250 * gib),
                        new DriveReading(@"D:\", "D:", 118 * gib, 500 * gib, Folders: new[] { new FolderReading(@"D:\Git", 0, 310 * gib, FolderState.Available), new FolderReading(@"D:\Caches", 2, 40 * gib, FolderState.Available) }),
                        new DriveReading(@"W:\", "W:", 620 * gib, 2000 * gib, Folders: new[] { new FolderReading(@"W:\Builds", 3, 760 * gib, FolderState.Available), new FolderReading(@"W:\Caches", 1, 21 * gib, FolderState.Available) }) }, theme);
                    footer.Children.Add(hardware); footer.Children.Add(drives);
                    Capture(footer, Path.Combine(output, $"gauges-footer-{language}-{dark}-{width}.png"), theme, width, 110);
                    if (!drives.Fits || !Find<ResourceTrendSurface>(hardware).All(graph => graph.ActualHeight == 12)) throw new Exception("Resource bars overlap their metadata in the compact footer.");
                }
                dialog.Close(); owner.Close();
            }
        }
        finally { Loc.SetLanguage(languageBefore); }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(FrameworkElement content, string path, Burn0Theme theme, int width, int height, Window? window = null)
    {
        var canvas = new Grid { Background = theme.Color("canvas"), Width = width, Height = height };
        if (window != null)
        {
            canvas.Resources = window.Resources;
            System.Windows.Documents.TextElement.SetForeground(canvas, window.Foreground);
            System.Windows.Documents.TextElement.SetFontFamily(canvas, window.FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(canvas, window.FontSize);
        }
        canvas.Children.Add(content);
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        canvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
