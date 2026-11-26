using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexAccountDesk;

internal sealed class AccountCardHeader : Grid
{
    public StackPanel Actions { get; }
    private readonly TextBlock _name, _metadata;
    private readonly bool _showCompactStatus;
    private readonly Dictionary<Button, (Thickness Padding, double Height, double Font)> _sizes = new();
    public AccountCardHeader(string name, string metadata, Burn0Theme theme, StackPanel actions, bool current = false, string? warning = null, System.Action? relogin = null, bool showCompactStatus = false)
    {
        Actions = actions;
        _showCompactStatus = showCompactStatus;
        ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var labels = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        _name = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = theme.Color("text"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name + "\n" + metadata, FlowDirection = FlowDirection.LeftToRight };
        _metadata = new TextBlock { Text = metadata, FontSize = 11, Foreground = theme.Color(current ? "accent" : "muted"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var title = new Grid(); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new());
        if (warning != null) title.Children.Add(new AccountWarningButton(name, warning, theme, relogin));
        _name.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(_name, 1); title.Children.Add(_name);
        labels.Children.Add(title); labels.Children.Add(_metadata); Children.Add(labels);
        foreach (Button button in actions.Children) _sizes[button] = (button.Padding, button.MinHeight, button.FontSize);
        Grid.SetColumn(actions, 1); Children.Add(actions);
    }
    internal void EnableReordering(Burn0Theme theme, System.Action<UIElement> start)
    {
        foreach (UIElement child in Children) SetColumn(child, GetColumn(child) + 1);
        ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        var dots = new UniformGrid { Rows = 3, Columns = 2, Width = 8, Height = 13, IsHitTestVisible = false };
        for (var i = 0; i < 6; i++)
            dots.Children.Add(new Ellipse { Width = 2, Height = 2, Fill = theme.Color("muted") });
        var grip = new Border { Width = 16, Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll, ToolTip = Loc.T("dashboard.drag_account"), Child = dots };
        Children.Add(grip);
        Point? origin = null;
        grip.MouseLeftButtonDown += (_, args) =>
        {
            if (grip.CaptureMouse()) { origin = args.GetPosition(grip); args.Handled = true; }
        };
        grip.LostMouseCapture += (_, _) => origin = null;
        grip.MouseLeftButtonUp += (_, _) => grip.ReleaseMouseCapture();
        grip.MouseMove += (_, args) =>
        {
            if (origin is not { } from || args.LeftButton != MouseButtonState.Pressed) return;
            var distance = args.GetPosition(grip) - from;
            if (System.Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            grip.ReleaseMouseCapture();
            start(grip);
        };
    }

    public void SetCompact(bool compact)
    {
        if (!ReferenceEquals(Actions.Parent, this))
        {
            if (Actions.Parent is Panel previous) previous.Children.Remove(Actions);
            Children.Add(Actions);
        }
        _name.FontSize = compact ? 12 : 14; _metadata.Visibility = compact && !_showCompactStatus || string.IsNullOrEmpty(_metadata.Text) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var entry in _sizes)
        {
            entry.Key.MinHeight = compact ? 24 : entry.Value.Height;
            entry.Key.Padding = compact ? new Thickness(4, 2, 4, 2) : entry.Value.Padding;
            entry.Key.FontSize = compact ? 11 : entry.Value.Font;
        }
    }
}
