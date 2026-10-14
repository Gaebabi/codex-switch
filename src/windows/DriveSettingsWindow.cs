using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CodexAccountDesk;

internal sealed class DriveSettingsWindow : Window
{
    private sealed record Row(string Root, CheckBox Enabled, TextBox Name);
    private readonly List<Row> _rows = new();
    private readonly StackPanel _list = new();
    private readonly Burn0Theme _theme;
    public DrivePreference[] Selection => _rows.Where(r => r.Enabled.IsChecked == true).Select(r => new DrivePreference { Root = r.Root, Label = r.Name.Text.Trim() }).ToArray();
    public DriveSettingsWindow(Window owner, Burn0Theme theme, DrivePreference[] preferences)
    {
        Owner = owner; _theme = theme; Width = 420; Height = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner.Left < -10000) { WindowStartupLocation = WindowStartupLocation.Manual; Left = owner.Left; Top = owner.Top; ShowInTaskbar = false; }
        WindowStyle = WindowStyle.None; Title = Loc.T("드라이브 설정"); Background = theme.Color("canvas"); Foreground = theme.Color("text"); FontFamily = theme.Font;
        WindowFrame.Apply(this); Resources = owner.Resources;
        var root = new DockPanel();
        var title = new Border { Height = 40, Background = theme.Color("chrome"), Padding = new Thickness(14, 0, 0, 0), Child = new TextBlock { Text = Title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold } };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        var cancel = new Button { Content = Loc.T("취소"), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = Loc.T("저장") }; save.Click += (_, _) => DialogResult = true; actions.Children.Add(cancel); actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var hint = new TextBlock { Text = Loc.T("표시할 드라이브와 이름을 선택하고 화살표로 순서를 바꾸세요."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 10, 14, 8), FontSize = 12 };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 12, 0) });
        var roots = preferences.Select(p => p.Root).Concat(DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d => d.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in roots)
        {
            var preference = preferences.FirstOrDefault(p => string.Equals(p.Root, path, StringComparison.OrdinalIgnoreCase));
            _rows.Add(new(path, new CheckBox { Content = path[..2], IsChecked = preference != null, Foreground = theme.Color("text"), VerticalAlignment = VerticalAlignment.Center, Width = 65 },
                new TextBox { Text = preference?.Label ?? path[..2], MaxLength = 16, Foreground = theme.Color("text"), Background = theme.Color("surface"), BorderThickness = new Thickness(0), Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center }));
        }
        Render(); Content = root;
    }
    private void Render()
    {
        _list.Children.Clear();
        foreach (var row in _rows.ToArray())
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) }; grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            if (row.Enabled.Parent is Panel oldCheck) oldCheck.Children.Remove(row.Enabled);
            if (row.Name.Parent is Panel oldName) oldName.Children.Remove(row.Name);
            grid.Children.Add(row.Enabled); Grid.SetColumn(row.Name, 1); grid.Children.Add(row.Name);
            var arrows = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var direction in new[] { -1, 1 })
            {
                var button = new Button { Content = direction == -1 ? "↑" : "↓", Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), IsEnabled = _rows.IndexOf(row) + direction >= 0 && _rows.IndexOf(row) + direction < _rows.Count };
                button.Click += (_, _) => { var index = _rows.IndexOf(row); _rows.RemoveAt(index); _rows.Insert(index + direction, row); Render(); }; arrows.Children.Add(button);
            }
            Grid.SetColumn(arrows, 2); grid.Children.Add(arrows); _list.Children.Add(grid);
        }
    }
}
