using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CodexAccountDesk;

internal sealed class DriveUsagePanel : Grid
{
    private readonly Button _settings = new() { Width = 22, MinHeight = 20, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly List<DriveCell> _cells = new();
    private readonly Action<string> _openDirectory;
    private readonly HashSet<string> _indexBusy = new(DriveMonitor.RootComparer);
    public event Action<string>? IndexPermissionRequested;
    public event Action<string>? IndexRebuildRequested;
    internal void SetIndexPermissionBusy(string root, bool busy)
    {
        if (busy) _indexBusy.Add(root); else _indexBusy.Remove(root);
        foreach (var cell in _cells.Where(c => DriveMonitor.RootComparer.Equals(c.Root, root))) cell.SetIndexBusy(busy);
    }
    public DriveUsagePanel(Action settings, Action<string>? openDirectory = null)
    { Height = 34; Margin = new Thickness(0, 0, 8, 0); _settings.Click += (_, _) => settings(); _openDirectory = openDirectory ?? OpenDirectory; }
    private static void OpenDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    internal bool Fits => _cells.All(cell => cell.Fits);
    public void Refresh(DriveReading[] readings, Burn0Theme theme)
    {
        if (!_cells.Select(cell => cell.Root).SequenceEqual(readings.Select(reading => reading.Root)))
        {
            Children.Clear(); ColumnDefinitions.Clear(); _cells.Clear();
            foreach (var reading in readings)
            {
                ColumnDefinitions.Add(new()); var cell = new DriveCell(reading.Root, _openDirectory, root => IndexPermissionRequested?.Invoke(root), root => IndexRebuildRequested?.Invoke(root));
                SetColumn(cell, _cells.Count); _cells.Add(cell); Children.Add(cell);
            }
        }
        if (_settings.Parent == null)
        { ColumnDefinitions.Add(new() { Width = GridLength.Auto }); SetColumn(_settings, readings.Length); Children.Add(_settings); }
        _settings.Content = Burn0Theme.Icon("more", theme.Color("muted"), 14); _settings.Foreground = theme.Color("muted"); _settings.ToolTip = Loc.T("드라이브 설정");
        for (var index = 0; index < readings.Length; index++) { _cells[index].Refresh(readings[index], theme); _cells[index].SetIndexBusy(_indexBusy.Contains(readings[index].Root)); }
    }

    private sealed class DriveCell : Grid
    {
        public string Root { get; }
        public bool Fits => _text.ActualWidth >= 20 && _temperature.ActualWidth + 22 < ActualWidth && _ioFills.All(fill => fill.Height is >= 0 and <= 16);
        private readonly Border _track = new() { CornerRadius = new CornerRadius(4) };
        private readonly Grid _segments = new() { ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
        private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0, 4, 0) };
        private readonly Run _capacity = new();
        private readonly Button _indexPermission = new() { Name = "FolderIndexPermissionButton", Width = 17, Height = 16, MinHeight = 0, Padding = new Thickness(1),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        private readonly Action<string> _rebuildIndex;
        private bool _indexSetupBusy;
        private string? _indexMenuTheme, _indexMenuLanguage;
        private readonly Ellipse _spinner = Burn0Theme.LoadingSpinner();
        private readonly TextBlock _temperature = new() { FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) };
        private ResourcePressure _temperaturePressure;
        private readonly Border[] _ioTracks = new Border[2], _ioFills = new Border[2];
        private readonly Action<string> _openDirectory;
        private readonly List<SegmentButton> _buttons = new();
        public DriveCell(string root, Action<string> openDirectory, Action<string> enableIndex, Action<string> rebuildIndex)
        {
            Root = root; _openDirectory = openDirectory; _rebuildIndex = rebuildIndex; Margin = new Thickness(0, 2, 4, 2);
            _text.Inlines.Add(_capacity);
            _indexPermission.Click += (_, _) => enableIndex(Root);
            _spinner.Name = "FolderScanSpinner";
            _spinner.Margin = new Thickness(4, 0, 0, 0);
            UseLayoutRounding = true;
            RowDefinitions.Add(new() { Height = new GridLength(16) });
            RowDefinitions.Add(new() { Height = new GridLength(2) });
            RowDefinitions.Add(new() { Height = new GridLength(12) });
            SetRow(_track, 2); SetRow(_segments, 2); Children.Add(_track); Children.Add(_segments);
            var content = new Grid(); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            _text.IsHitTestVisible = false; _temperature.IsHitTestVisible = false;
            content.Children.Add(_text);
            var indexActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            indexActions.Children.Add(_spinner); indexActions.Children.Add(_indexPermission);
            Grid.SetColumn(indexActions, 1); content.Children.Add(indexActions);
            Grid.SetColumn(_temperature, 2); content.Children.Add(_temperature); Children.Add(content);
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var throughput = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            for (var index = 0; index < 2; index++)
            {
                var track = new Grid { Width = 3, Height = 16, Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0) };
                _ioTracks[index] = new Border { CornerRadius = new CornerRadius(1) };
                _ioFills[index] = new Border { CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom };
                track.Children.Add(_ioTracks[index]); track.Children.Add(_ioFills[index]); throughput.Children.Add(track);
            }
            Grid.SetColumn(throughput, 3); content.Children.Add(throughput);
            _segments.SizeChanged += (_, _) => _segments.Clip = new RectangleGeometry(new Rect(_segments.RenderSize), 4, 4);
        }
        public void Refresh(DriveReading reading, Burn0Theme theme)
        {
            var color = reading.FreePercent is double free ? free <= 15 ? "danger" : free <= 30 ? "warning" : "accent" : "muted";
            _track.Background = theme.Color("surface");
            var folders = reading.Folders ?? Array.Empty<FolderReading>();
            var segments = FolderUsage.Segments(reading);
            if (segments.Length == 0) segments = new[] { new DriveSegment(reading.Root, 1, null) };
            if (!_buttons.Select(b => b.Label).SequenceEqual(segments.Select(s => s.Label)))
            {
                _segments.Children.Clear(); _segments.ColumnDefinitions.Clear(); _buttons.Clear();
                foreach (var segment in segments)
                {
                    var button = new SegmentButton(segment.Label, segment.Color.HasValue ? segment.Label : Root, _openDirectory);
                    _segments.ColumnDefinitions.Add(new()); Grid.SetColumn(button, _buttons.Count); _buttons.Add(button); _segments.Children.Add(button);
                }
            }
            for (var i = 0; i < segments.Length; i++)
            {
                _segments.ColumnDefinitions[i].Width = new GridLength(segments[i].Bytes, GridUnitType.Star);
                _buttons[i].Refresh(segments[i], reading, theme);
            }
            var label = string.IsNullOrWhiteSpace(reading.Label) ? reading.Root[..2] : reading.Label;
            var value = reading.Free.HasValue ? (reading.Free.Value / 1073741824.0).ToString("0.#", Loc.Culture) + " GiB" : "—";
            _capacity.Text = label + " " + value; _text.FontFamily = theme.Font; _text.Foreground = theme.Color(color is "warning" or "danger" ? color : "text");
            _spinner.Stroke = theme.Color("accent");
            _spinner.Visibility = reading.MeasuringFolders ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_spinner, Loc.T("folders.scanning"));
            var needsPermission = reading.Index?.Access is FolderIndexAccess.PermissionRequired or FolderIndexAccess.JournalMissing;
            _indexPermission.Visibility = needsPermission ? Visibility.Visible : Visibility.Collapsed;
            _indexPermission.Content = Burn0Theme.Icon("lock", theme.Color("warning"), 12);
            _indexPermission.ToolTip = Loc.T("folders.permission_hint");
            AutomationProperties.SetName(_indexPermission, Loc.T("folders.enable_index"));
            SetIndexBusy(_indexSetupBusy);
            AutomationProperties.SetName(_text, _capacity.Text + (reading.MeasuringFolders ? " · " + Loc.T("folders.scanning") : ""));
            var now = DateTimeOffset.UtcNow; _temperature.Text = TemperaturePresentation.Label(reading.Temperature, now); _temperature.FontFamily = theme.Font;
            _temperaturePressure = TemperatureColors.Pressure(reading.Temperature, TemperatureKind.Disk, _temperaturePressure, now);
            _temperature.Foreground = theme.Color(TemperatureColors.Color(_temperaturePressure));
            for (var index = 0; index < 2; index++)
            {
                var channel = index == 0 ? reading.Io?.Read : reading.Io?.Write;
                var fresh = DiskIoReading.Fresh(reading.Io, now);
                var ioColor = !fresh ? "muted" : channel?.Pressure switch { ResourcePressure.Warning => "warning", ResourcePressure.Critical => "danger", _ => index == 0 ? "accent" : "success" };
                _ioTracks[index].Background = theme.Color("divider");
                _ioFills[index].Background = theme.Color(ioColor); _ioFills[index].Height = 16 * DiskIoReading.Fill(reading.Io, index == 1, now);
                AutomationProperties.SetName(_ioTracks[index].Parent as DependencyObject, Loc.T(index == 0 ? "io.read" : "io.write") + ": " + (fresh ? DiskIoReading.RateLabel(channel?.BytesPerSecond) : "—"));
            }
            var details = reading.Root + "\n" + (reading.Free.HasValue ? Loc.F("남음 {0:0.##} / {1:0.##} GiB\n잔여 {2:0.0}%", reading.Free.Value / 1073741824.0, reading.Total!.Value / 1073741824.0, reading.FreePercent) : Loc.T("드라이브 조회 불가"));
            ToolTipTheme.Resource(this, label, value, details, theme, color is "warning" or "danger" ? color : "text");
            var legend = new StackPanel();
            if (reading.Index != null)
            {
                legend.Children.Add(new TextBlock { Text = Loc.F("folders.index_progress", reading.Index.Directories, reading.Index.Files),
                    FontSize = 11, Foreground = theme.Color("muted"), TextWrapping = TextWrapping.Wrap });
                if (reading.Index.Access != FolderIndexAccess.Ready)
                    legend.Children.Add(new TextBlock { Text = Loc.T(needsPermission ? "folders.permission_hint" : reading.Index.Access == FolderIndexAccess.Unsupported ? "folders.index_unsupported" : "folders.index_failed") +
                        (reading.Index.Problem == null ? "" : " · " + reading.Index.Problem), FontSize = 11, Foreground = theme.Color("warning"), TextWrapping = TextWrapping.Wrap });
                var menuTheme = theme.Color("canvas").ToString();
                if (ContextMenu == null || _indexMenuTheme != menuTheme || _indexMenuLanguage != Loc.Language)
                {
                    var menu = DropdownTheme.Create(this, theme);
                    var refresh = new MenuItem { Header = Loc.T("folders.rebuild_index") }; refresh.Click += (_, _) => _rebuildIndex(Root); menu.Items.Add(refresh); ContextMenu = menu;
                    _indexMenuTheme = menuTheme; _indexMenuLanguage = Loc.Language;
                }
                ((MenuItem)ContextMenu.Items[0]).IsEnabled = !needsPermission;
            }
            foreach (var folder in folders)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                line.Children.Add(new Border { Width = 8, Height = 8, Background = DriveSettingsWindow.FolderBrush(folder.Color), Margin = new Thickness(0, 0, 6, 0) });
                var amount = folder.Bytes.HasValue ? (folder.Bytes.Value / 1073741824.0).ToString("0.###", Loc.Culture) + " GiB" + (reading.Total > 0 ? " · " + (100.0 * folder.Bytes.Value / reading.Total.Value).ToString("0.0", Loc.Culture) + "%" : "") : "—";
                var state = folder.State switch { FolderState.Scanning => "folders.scanning", FolderState.Partial => "folders.partial", FolderState.Missing => "folders.missing", FolderState.Unavailable => "folders.unavailable", FolderState.Unsupported => "folders.unsupported", FolderState.Cached => "folders.cached", _ => null };
                var text = folder.Path + "\n" + amount + (state == null ? "" : " · " + Loc.T(state));
                if (folder.Skipped > 0) text += " · " + Loc.F("folders.skipped", folder.Skipped);
                line.Children.Add(new TextBlock { Text = text, Width = 258, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = theme.Color("text") }); legend.Children.Add(line);
            }
            foreach (var segment in folders.Length == 0 ? Array.Empty<DriveSegment>() : FolderUsage.Segments(reading).Where(s => s.Color == null))
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                line.Children.Add(new Border { Width = 8, Height = 8, Background = theme.Color(segment.Label == "folders.free" ? "success" : "muted"), Margin = new Thickness(0, 0, 6, 0) });
                line.Children.Add(new TextBlock { Text = Loc.T(segment.Label) + " · " + (segment.Bytes / 1073741824.0).ToString("0.#", Loc.Culture) + " GiB", Foreground = theme.Color("text"), FontSize = 11 }); legend.Children.Add(line);
            }
            if (folders.Length > 0) legend.Children.Add(new TextBlock { Text = Loc.T("folders.measurement"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = theme.Color("muted"), FontSize = 11 });
            ((ResourceToolTip)ToolTip).SetSupplement(folders.Length == 0 ? null : legend);
            AutomationProperties.SetName(this, details + "\n" + string.Join("\n", folders.Select(f => f.Path + ": " + (f.Bytes?.ToString(Loc.Culture) ?? "—") + " " + f.State)));
        }
        internal void SetIndexBusy(bool busy) { _indexSetupBusy = busy; _indexPermission.IsEnabled = !busy; }
    }

    private sealed class SegmentButton : Button
    {
        public string Label { get; }
        private readonly Border _fill = new(), _pattern = new();
        private readonly TextBlock _share = new() { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        public SegmentButton(string label, string path, Action<string> openDirectory)
        {
            Label = label; Tag = path; Padding = new Thickness(0); BorderThickness = new Thickness(0); MinHeight = 0;
            Background = Brushes.Transparent; Cursor = System.Windows.Input.Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentProperty));
            Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
            var surface = new Grid { Background = Brushes.Transparent }; surface.Children.Add(_fill); surface.Children.Add(_pattern); surface.Children.Add(_share); Content = surface;
            SizeChanged += (_, _) => _share.Visibility = ActualWidth >= 40 && _share.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Click += (_, _) => openDirectory(path);
        }
        public void Refresh(DriveSegment segment, DriveReading reading, Burn0Theme theme)
        {
            var plain = segment.Label == reading.Root;
            var brush = segment.Color is int color ? DriveSettingsWindow.FolderBrush(color) : theme.Color(segment.Label == "folders.free" ? "success" : "muted");
            _fill.Background = plain || segment.Label == "folders.free" ? Brushes.Transparent
                : new SolidColorBrush(brush.Color) { Opacity = segment.Color.HasValue ? 1 : theme.Dark ? 0.55 : 0.48 };
            var separator = segment.Label == "folders.free" ? 0 : 1;
            _fill.BorderBrush = theme.Color("canvas"); _fill.BorderThickness = new Thickness(0, 0, separator, 0);
            _pattern.Margin = new Thickness(0, 0, separator, 0);
            if (segment.Color.HasValue)
            {
                var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(Colors.White) { Opacity = 0.24 }, 2),
                    Geometry.Parse("M -1,9 L 9,-1 M -1,1 L 1,-1 M 7,9 L 9,7"));
                _pattern.Background = new DrawingBrush(drawing) { TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, 8, 8), ViewboxUnits = BrushMappingMode.Absolute };
            }
            else _pattern.Background = Brushes.Transparent;
            _share.Text = segment.Color.HasValue && reading.Total > 0 ? (100.0 * segment.Bytes / reading.Total.Value).ToString("0.#", Loc.Culture) + "%" : "";
            _share.Visibility = ActualWidth >= 40 && _share.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var folder = reading.Folders?.FirstOrDefault(f => f.Path == segment.Label);
            var bytes = plain ? reading.Free : folder?.Bytes ?? segment.Bytes;
            var value = bytes.HasValue ? (bytes.Value / 1073741824.0).ToString("0.###", Loc.Culture) + " GiB" : "—";
            var title = folder != null ? Path.GetFileName(folder.Path) : plain ? reading.Label : Loc.T(segment.Label);
            var details = (string)Tag;
            if (bytes.HasValue && reading.Total > 0) details += "\n" + (100.0 * bytes.Value / reading.Total.Value).ToString("0.0", Loc.Culture) + "%";
            if (plain) details += " · " + Loc.T("folders.free");
            if (folder?.State == FolderState.Partial) details += " · " + Loc.T("folders.partial");
            details += "\n" + Loc.T("folders.open");
            ToolTipTheme.Resource(this, title, value, details, theme, "text");
            AutomationProperties.SetName(this, title + " " + value + "\n" + details);
        }
    }
}
