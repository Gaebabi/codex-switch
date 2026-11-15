using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace CodexAccountDesk;

internal sealed class DriveUsagePanel : Grid
{
    private readonly Button _settings = new() { Width = 22, MinHeight = 20, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly List<DriveCell> _cells = new();
    private readonly Action<string> _openDirectory;
    public DriveUsagePanel(Action settings, Action<string>? openDirectory = null)
    { Height = 24; Margin = new Thickness(0, 0, 8, 0); _settings.Click += (_, _) => settings(); _openDirectory = openDirectory ?? OpenDirectory; }
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
                ColumnDefinitions.Add(new()); var cell = new DriveCell(reading.Root, _openDirectory);
                SetColumn(cell, _cells.Count); _cells.Add(cell); Children.Add(cell);
            }
        }
        if (_settings.Parent == null)
        { ColumnDefinitions.Add(new() { Width = GridLength.Auto }); SetColumn(_settings, readings.Length); Children.Add(_settings); }
        _settings.Content = Burn0Theme.Icon("more", theme.Color("muted"), 14); _settings.Foreground = theme.Color("muted"); _settings.ToolTip = Loc.T("드라이브 설정");
        for (var index = 0; index < readings.Length; index++) _cells[index].Refresh(readings[index], theme);
    }

    private sealed class DriveCell : Grid
    {
        public string Root { get; }
        public bool Fits => _text.ActualWidth >= 20 && _temperature.ActualWidth + 22 < ActualWidth && _ioFills.All(fill => fill.Height is >= 0 and <= 16);
        private readonly Border _track = new() { CornerRadius = new CornerRadius(3) }, _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
        private readonly Grid _segments = new() { ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
        private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0, 4, 0) };
        private readonly TextBlock _temperature = new() { FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) };
        private ResourcePressure _temperaturePressure;
        private readonly Border[] _ioTracks = new Border[2], _ioFills = new Border[2];
        private double _freePercent;
        private readonly Action<string> _openDirectory;
        private readonly List<SegmentButton> _buttons = new();
        public DriveCell(string root, Action<string> openDirectory)
        {
            Root = root; _openDirectory = openDirectory; Margin = new Thickness(0, 2, 4, 2);
            Children.Add(_track); Children.Add(_fill); Children.Add(_segments);
            var content = new Grid { IsHitTestVisible = false }; content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.Children.Add(_text); Grid.SetColumn(_temperature, 1); content.Children.Add(_temperature); Children.Add(content);
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var throughput = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            for (var index = 0; index < 2; index++)
            {
                var track = new Grid { Width = 3, Height = 16, Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0) };
                _ioTracks[index] = new Border { CornerRadius = new CornerRadius(1) };
                _ioFills[index] = new Border { CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom };
                track.Children.Add(_ioTracks[index]); track.Children.Add(_ioFills[index]); throughput.Children.Add(track);
            }
            Grid.SetColumn(throughput, 2); content.Children.Add(throughput);
            SizeChanged += (_, _) => _fill.Width = ActualWidth * _freePercent / 100;
        }
        public void Refresh(DriveReading reading, Burn0Theme theme)
        {
            _freePercent = reading.FreePercent ?? 0;
            var color = reading.FreePercent is double free ? free <= 15 ? "danger" : free <= 30 ? "warning" : "accent" : "muted";
            _track.Background = theme.Color("surface"); _fill.Background = new SolidColorBrush(theme.Color(color).Color) { Opacity = 0.19 }; _fill.Width = ActualWidth * _freePercent / 100;
            var folders = reading.Folders ?? Array.Empty<FolderReading>();
            _fill.Visibility = folders.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var segments = folders.Length == 0 ? new[] { new DriveSegment(reading.Root, 1, null) } : FolderUsage.Segments(reading);
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
            _text.Text = label + " " + value; _text.FontFamily = theme.Font; _text.Foreground = theme.Color(color is "warning" or "danger" ? color : "text");
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
            foreach (var folder in folders)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                line.Children.Add(new Border { Width = 8, Height = 8, Background = DriveSettingsWindow.FolderBrush(folder.Color), Margin = new Thickness(0, 0, 6, 0) });
                var amount = folder.Bytes.HasValue ? (folder.Bytes.Value / 1073741824.0).ToString("0.###", Loc.Culture) + " GiB" + (reading.Total > 0 ? " · " + (100.0 * folder.Bytes.Value / reading.Total.Value).ToString("0.0", Loc.Culture) + "%" : "") : "—";
                var state = folder.State switch { FolderState.Scanning => "folders.scanning", FolderState.Partial => "folders.partial", FolderState.Missing => "folders.missing", FolderState.Unavailable => "folders.unavailable", FolderState.Unsupported => "folders.unsupported", _ => null };
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
    }

    private sealed class SegmentButton : Button
    {
        public string Label { get; }
        private readonly Border _fill = new(), _stripe = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom };
        public SegmentButton(string label, string path, Action<string> openDirectory)
        {
            Label = label; Tag = path; Padding = new Thickness(0); BorderThickness = new Thickness(0); MinHeight = 0;
            Background = Brushes.Transparent; Cursor = System.Windows.Input.Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentProperty));
            Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
            var surface = new Grid { Background = Brushes.Transparent }; surface.Children.Add(_fill); surface.Children.Add(_stripe); Content = surface;
            Click += (_, _) => openDirectory(path);
        }
        public void Refresh(DriveSegment segment, DriveReading reading, Burn0Theme theme)
        {
            var plain = segment.Label == reading.Root;
            var brush = segment.Color is int color ? DriveSettingsWindow.FolderBrush(color) : theme.Color(segment.Label == "folders.free" ? "success" : "muted");
            _fill.Background = plain ? Brushes.Transparent : new SolidColorBrush(brush.Color) { Opacity = segment.Color.HasValue ? 0.25 : 0.09 };
            _stripe.Background = plain ? Brushes.Transparent : brush; _stripe.Opacity = segment.Color.HasValue ? 1 : 0.3;
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
