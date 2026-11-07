using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CodexAccountDesk;

internal sealed class SessionUsagePanel : Grid
{
    private readonly TextBlock[] _labels = new TextBlock[4], _values = new TextBlock[4];
    private readonly StackPanel[] _groups = new StackPanel[4];
    public string Text => string.Join("   ", _labels.Select((label, index) => label.Text + " " + _values[index].Text));
    public SessionUsagePanel()
    {
        Height = 20; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        for (var index = 0; index < _groups.Length; index++)
        {
            ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            _groups[index] = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, index < _groups.Length - 1 ? 12 : 0, 0) };
            _labels[index] = new() { FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center };
            _values[index] = new() { FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            _groups[index].Children.Add(_labels[index]); _groups[index].Children.Add(_values[index]);
            SetColumn(_groups[index], index); Children.Add(_groups[index]);
        }
    }
    public void Refresh(SessionActivity activity, Burn0Theme theme)
    {
        var labels = new[] { "실행", "큐", "목표", "sessions.errors" };
        var values = new int?[] { activity.Available ? activity.Running : null, activity.Available ? activity.Queued : null, activity.Available ? activity.Goals : null, activity.Available ? activity.Errors : null };
        for (var index = 0; index < _groups.Length; index++)
        {
            _labels[index].Text = Loc.T(labels[index]); _labels[index].FontFamily = theme.Font; _labels[index].Foreground = theme.Color("muted");
            _values[index].Text = values[index]?.ToString(Loc.Culture) ?? "—"; _values[index].FontFamily = theme.Font;
            _values[index].Foreground = theme.Color(index == 3 && values[index] > 0 ? "warning" : values[index].HasValue ? "text" : "muted");
        }
        _groups[3].ToolTip = Loc.T("sessions.errors_help");
    }
    public bool Fits() => _groups.Select((group, index) => _labels[index].DesiredSize.Width + _values[index].DesiredSize.Width <= group.ActualWidth + 0.5).All(fits => fits) &&
        _labels.Concat(_values).All(element => element.ActualHeight <= 20.5);
}
