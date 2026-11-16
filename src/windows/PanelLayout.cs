using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace CodexAccountDesk;

internal static class PanelLayout
{
    internal const double BodySize = 12, CaptionSize = 11, SectionSize = 12, ValueSize = 32, TimeValueSize = 24;
    internal static Border Surface(UIElement content, Burn0Theme theme, double padding = 12) => new()
    {
        Child = content, Padding = new Thickness(padding), Background = theme.Color("surface"),
        BorderBrush = theme.Color("divider"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8)
    };
    internal static Border Rule(Burn0Theme theme, double top = 12, double bottom = 12) => new()
    { Height = 1, Background = theme.Color("divider"), Margin = new Thickness(0, top, 0, bottom) };
    internal static TextBlock Heading(string text, Burn0Theme theme) => new()
    { Text = text, FontFamily = theme.Font, FontSize = SectionSize, FontWeight = FontWeights.SemiBold,
        Foreground = theme.Color("text"), Margin = new Thickness(0, 0, 0, 8) };
    internal static UIElement Section(string title, UIElement content, Burn0Theme theme)
    {
        var panel = new StackPanel(); panel.Children.Add(Heading(title, theme)); panel.Children.Add(content);
        return Surface(panel, theme);
    }
    internal static void Input(Control input, Burn0Theme theme)
    {
        input.FontFamily = theme.Font; input.FontSize = BodySize; input.MinHeight = 34;
        input.Padding = new Thickness(8, 6, 8, 6); input.Foreground = theme.Color("text");
        input.Background = theme.Color("canvas"); input.BorderBrush = theme.Color("divider"); input.BorderThickness = new Thickness(1);
    }
    internal static Grid TableHeading(Burn0Theme theme, params (string Title, GridLength Width)[] columns)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        for (var index = 0; index < columns.Length; index++)
        {
            row.ColumnDefinitions.Add(new() { Width = columns[index].Width });
            var text = new TextBlock { Text = columns[index].Title, FontSize = CaptionSize, FontWeight = FontWeights.SemiBold,
                Foreground = theme.Color("muted"), ToolTip = columns[index].Title, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 4, 0) };
            Grid.SetColumn(text, index); row.Children.Add(text);
        }
        return row;
    }
    internal static UIElement Gauge(double percent, Burn0Theme theme)
    {
        var value = Math.Clamp(percent, 0, 100);
        var track = new Grid { Height = 12, ClipToBounds = true, Background = theme.Color("divider"), FlowDirection = FlowDirection.LeftToRight };
        track.ColumnDefinitions.Add(new() { Width = new GridLength(value, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new() { Width = new GridLength(100 - value, GridUnitType.Star) });
        track.Children.Add(new Border { Background = theme.Color(value <= 10 ? "danger" : value <= 25 ? "warning" : "accent") });
        for (var index = 1; index <= 3; index++)
        {
            var fraction = index / 4.0;
            var tick = new Border { Width = 1, Background = theme.Color("text"), Opacity = .2, HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, _) => tick.Margin = new Thickness(track.ActualWidth * fraction, 0, 0, 0);
            Grid.SetColumnSpan(tick, 2); track.Children.Add(tick);
        }
        AutomationProperties.SetName(track, Loc.T("잔여량") + " " + value.ToString("0.#", Loc.Culture) + "%");
        return new Border { CornerRadius = new CornerRadius(4), Child = track, ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0) };
    }
    internal static UIElement Caption(Window window, Burn0Theme theme)
    {
        var bar = new DockPanel { Height = 40, Background = theme.Color("chrome") };
        var close = new Button { Width = 46, Height = 40, Padding = new Thickness(0), Background = Brushes.Transparent,
            ToolTip = Loc.T("닫기"), Content = Burn0Theme.Icon("window-close", theme.Color("muted"), 12, true) };
        if (window.Resources["CaptionButton"] is Style style) close.Style = style;
        close.Click += (_, _) => window.Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        AutomationProperties.SetName(close, Loc.T("닫기")); DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close);
        bar.Children.Add(new TextBlock { Text = window.Title, FontSize = BodySize, FontWeight = FontWeights.SemiBold,
            Foreground = theme.Color("text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        return bar;
    }
}
