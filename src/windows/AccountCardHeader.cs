using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CodexAccountDesk;

internal sealed class AccountCardHeader : Grid
{
    public StackPanel Actions { get; }
    private readonly TextBlock _name, _metadata;
    private readonly Dictionary<Button, (Thickness Padding, double Height, double Font)> _sizes = new();
    public AccountCardHeader(string name, string metadata, Burn0Theme theme, StackPanel actions, bool current = false, string? warning = null, System.Action? relogin = null)
    {
        Actions = actions;
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
    public void SetCompact(bool compact)
    {
        if (!ReferenceEquals(Actions.Parent, this))
        {
            if (Actions.Parent is Panel previous) previous.Children.Remove(Actions);
            Children.Add(Actions);
        }
        _name.FontSize = compact ? 12 : 14; _metadata.Visibility = compact || string.IsNullOrEmpty(_metadata.Text) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var entry in _sizes)
        {
            entry.Key.MinHeight = compact ? 24 : entry.Value.Height;
            entry.Key.Padding = compact ? new Thickness(4, 2, 4, 2) : entry.Value.Padding;
            entry.Key.FontSize = compact ? 11 : entry.Value.Font;
        }
    }
}
