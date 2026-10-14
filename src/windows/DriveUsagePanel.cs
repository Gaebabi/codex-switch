using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class DriveUsagePanel : Grid
{
    private readonly Button _settings = new() { Width = 22, MinHeight = 20, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly List<DriveCell> _cells = new();
    public DriveUsagePanel(Action settings)
    { Height = 24; Margin = new Thickness(0, 0, 8, 0); _settings.Click += (_, _) => settings(); }
    public void Refresh(DriveReading[] readings, Burn0Theme theme)
    {
        if (!_cells.Select(cell => cell.Root).SequenceEqual(readings.Select(reading => reading.Root)))
        {
            Children.Clear(); ColumnDefinitions.Clear(); _cells.Clear();
            foreach (var reading in readings)
            {
                ColumnDefinitions.Add(new()); var cell = new DriveCell(reading.Root);
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
        private readonly Border _track = new() { CornerRadius = new CornerRadius(3) }, _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
        private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0, 4, 0) };
        private double _freePercent;
        public DriveCell(string root)
        {
            Root = root; Margin = new Thickness(0, 2, 4, 2);
            Children.Add(_track); Children.Add(_fill); Children.Add(_text);
            SizeChanged += (_, _) => _fill.Width = ActualWidth * _freePercent / 100;
        }
        public void Refresh(DriveReading reading, Burn0Theme theme)
        {
            _freePercent = reading.FreePercent ?? 0;
            var color = reading.FreePercent is double free ? free <= 15 ? "danger" : free <= 30 ? "warning" : "accent" : "muted";
            _track.Background = theme.Color("surface"); _fill.Background = new SolidColorBrush(theme.Color(color).Color) { Opacity = 0.19 }; _fill.Width = ActualWidth * _freePercent / 100;
            var label = string.IsNullOrWhiteSpace(reading.Label) ? reading.Root[..2] : reading.Label;
            var value = reading.Free.HasValue ? (reading.Free.Value / 1073741824.0).ToString("0.#", Loc.Culture) + " GiB" : "—";
            _text.Text = label + " " + value; _text.FontFamily = theme.Font; _text.Foreground = theme.Color(color is "warning" or "danger" ? color : "text");
            var details = reading.Root + "\n" + (reading.Free.HasValue ? Loc.F("남음 {0:0.##} / {1:0.##} GiB\n잔여 {2:0.0}%", reading.Free.Value / 1073741824.0, reading.Total!.Value / 1073741824.0, reading.FreePercent) : Loc.T("드라이브 조회 불가"));
            ToolTipTheme.Resource(this, label, value, details, theme, color is "warning" or "danger" ? color : "text");
        }
    }
}
