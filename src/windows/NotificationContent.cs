using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class NotificationContent : StackPanel
{
    private readonly Button _dismiss;
    private readonly Border _track, _fill;
    private readonly ScaleTransform _remaining = new(1, 1);
    private readonly Brush _accent, _warning;

    internal NotificationContent(TextBlock text, Button dismiss, Burn0Theme theme, Button? operation = null)
    {
        _dismiss = dismiss;
        _accent = theme.Color("accent"); _warning = theme.Color("warning");
        var row = new DockPanel();
        DockPanel.SetDock(dismiss, Dock.Right);
        row.Children.Add(dismiss);
        if (operation != null)
        {
            DockPanel.SetDock(operation, Dock.Right);
            row.Children.Add(operation);
        }
        row.Children.Add(text);
        Children.Add(row);
        _fill = new Border { Background = _accent, CornerRadius = new CornerRadius(1.5), RenderTransform = _remaining };
        _track = new Border { Child = _fill, Height = 3, Margin = new Thickness(0, 5, 0, 0),
            Background = theme.Color("divider"), CornerRadius = new CornerRadius(1.5),
            FlowDirection = FlowDirection.LeftToRight, IsHitTestVisible = false };
        Children.Add(_track);
        SetRemaining(null, false);
    }

    internal void SetRemaining(double? fraction, bool warning)
    {
        _track.Visibility = _dismiss.Visibility = fraction.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (fraction is not double value) return;
        _remaining.ScaleX = value;
        _fill.Background = warning ? _warning : _accent;
    }

    internal void UpdateGauge(double fraction) => _remaining.ScaleX = fraction;
}
