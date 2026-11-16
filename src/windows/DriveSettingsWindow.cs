using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace CodexAccountDesk;

internal sealed class DriveSettingsWindow : Window
{
    private sealed record FolderRow(TextBox Path, Button Color);
    private sealed record Row(string Root, CheckBox Enabled, TextBox Name, List<FolderRow> Folders);
    private readonly List<Row> _rows = new();
    private readonly StackPanel _list = new();
    private readonly Burn0Theme _theme;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 14, 8) };
    public DrivePreference[] Selection => _rows.Where(r => r.Enabled.IsChecked == true).Select(r => new DrivePreference { Root = r.Root, Label = r.Name.Text.Trim(),
        Folders = r.Folders.Count == 0 ? null : FolderUsage.Validate(r.Root, r.Folders.Select(f => new FolderPreference { Path = f.Path.Text.Trim(), Color = (int)f.Color.Tag }).ToArray()) }).ToArray();
    public DriveSettingsWindow(Window owner, Burn0Theme theme, DrivePreference[] preferences)
    {
        Owner = owner; _theme = theme; Width = 620; Height = 460; MinWidth = 540; MinHeight = 340; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner.Left < -10000) { WindowStartupLocation = WindowStartupLocation.Manual; Left = owner.Left; Top = owner.Top; ShowInTaskbar = false; }
        WindowStyle = WindowStyle.None; Title = Loc.T("드라이브 설정"); Background = theme.Color("canvas"); Foreground = theme.Color("text"); FontFamily = theme.Font;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag);
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        WindowFrame.Apply(this); Resources = owner.Resources; FontSize = PanelLayout.BodySize; UseLayoutRounding = true; SnapsToDevicePixels = true;
        var root = new DockPanel();
        var title = PanelLayout.Caption(this, theme);
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        var cancel = new Button { Content = Loc.T("취소"), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = Loc.T("저장"), Style = (Style)owner.Resources["PrimaryButton"], Background = theme.Color("accent"), Foreground = theme.Color("canvas") }; save.Click += (_, _) =>
        {
            try { _ = Selection; DialogResult = true; }
            catch (ArgumentException problem) { _error.Text = Loc.T(problem.Message.StartsWith("folders.", StringComparison.Ordinal) ? problem.Message : "folders.invalid"); }
            catch (NotSupportedException) { _error.Text = Loc.T("folders.invalid"); }
            catch (PathTooLongException) { _error.Text = Loc.T("folders.invalid"); }
        }; actions.Children.Add(cancel); actions.Children.Add(save);
        var footer = new Border { Child = actions, BorderBrush = theme.Color("divider"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _error.Foreground = theme.Color("danger"); DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        var hint = new TextBlock { Text = Loc.T("folders.hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 10, 14, 8), FontSize = 12 };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 12, 0) });
        var roots = preferences.Select(p => p.Root).Concat(DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network).Select(d => d.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in roots)
        {
            var preference = preferences.FirstOrDefault(p => string.Equals(p.Root, path, StringComparison.OrdinalIgnoreCase));
            _rows.Add(new(path, new CheckBox { Content = path[..2], IsChecked = preference != null, Foreground = theme.Color("text"), VerticalAlignment = VerticalAlignment.Center, Width = 65, FlowDirection = FlowDirection.LeftToRight },
                new TextBox { Text = preference?.Label ?? path[..2], MaxLength = 16, Foreground = theme.Color("text"), Background = theme.Color("surface"), BorderThickness = new Thickness(0), Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight },
                (preference?.Folders ?? Array.Empty<FolderPreference>()).Select(Folder).ToList()));
        }
        Render(); Content = root;
    }
    private void Render()
    {
        _list.Children.Clear();
        foreach (var row in _rows.ToArray())
        {
            var section = new StackPanel();
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
            Grid.SetColumn(arrows, 2); grid.Children.Add(arrows); section.Children.Add(grid);
            PanelLayout.Input(row.Name, _theme); row.Enabled.FontSize = PanelLayout.BodySize; row.Enabled.FontWeight = FontWeights.SemiBold;
            if (row.Folders.Count > 0)
            {
                section.Children.Add(PanelLayout.Rule(_theme, 8, 0));
                section.Children.Add(PanelLayout.TableHeading(_theme, ("", new GridLength(48)), (Loc.T("folders.path"), new GridLength(1, GridUnitType.Star)), ("", new GridLength(68))));
            }
            foreach (var folder in row.Folders.ToArray())
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                line.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); line.ColumnDefinitions.Add(new());
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                if (folder.Color.Parent is Panel oldColor) oldColor.Children.Remove(folder.Color);
                if (folder.Path.Parent is Panel oldPath) oldPath.Children.Remove(folder.Path);
                line.Children.Add(folder.Color); Grid.SetColumn(folder.Path, 1); line.Children.Add(folder.Path);
                var browse = new Button { Content = "…", Width = 30, Margin = new Thickness(4, 0, 0, 0), ToolTip = Loc.T("folders.browse") };
                AutomationProperties.SetName(browse, Loc.T("folders.browse"));
                browse.Click += (_, _) =>
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("folders.browse"), InitialDirectory = row.Root };
                    if (dialog.ShowDialog(this) == true) folder.Path.Text = dialog.FolderName;
                };
                var remove = new Button { Content = "×", Width = 30, Margin = new Thickness(4, 0, 0, 0), ToolTip = Loc.T("삭제") };
                AutomationProperties.SetName(remove, Loc.T("삭제")); remove.Click += (_, _) => { row.Folders.Remove(folder); Render(); };
                Grid.SetColumn(browse, 2); line.Children.Add(browse); Grid.SetColumn(remove, 3); line.Children.Add(remove); section.Children.Add(line);
            }
            var add = new Button { Content = Loc.T("folders.add"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), IsEnabled = row.Folders.Count < FolderUsage.Colors.Length };
            add.Click += (_, _) =>
            {
                var color = Enumerable.Range(0, FolderUsage.Colors.Length).FirstOrDefault(i => row.Folders.All(f => (int)f.Color.Tag != i));
                row.Folders.Add(Folder(new() { Color = color })); row.Enabled.IsChecked = true; Render(); row.Folders[^1].Path.Focus();
            };
            section.Children.Add(add);
            var group = PanelLayout.Surface(section, _theme); group.Margin = new Thickness(0, 0, 0, 12); _list.Children.Add(group);
        }
    }
    private FolderRow Folder(FolderPreference preference)
    {
        var path = new TextBox { Text = preference.Path, Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            Background = _theme.Color("surface"), Foreground = _theme.Color("text"), FlowDirection = FlowDirection.LeftToRight, ToolTip = Loc.T("folders.path") };
        AutomationProperties.SetName(path, Loc.T("folders.path"));
        PanelLayout.Input(path, _theme);
        var swatch = new Border { Width = 18, Height = 12, Background = FolderBrush(preference.Color), VerticalAlignment = VerticalAlignment.Center };
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(swatch); face.Children.Add(new TextBlock { Text = "⌄", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var color = new Button { Content = face, Tag = preference.Color, Padding = new Thickness(4, 0, 4, 0) };
        var menu = DropdownTheme.Create(color, _theme); color.ContextMenu = menu;
        void Select(int index)
        {
            color.Tag = index; swatch.Background = FolderBrush(index);
            color.ToolTip = Loc.F("folders.color", index + 1); AutomationProperties.SetName(color, (string)color.ToolTip);
            foreach (MenuItem choice in menu.Items) choice.IsChecked = (int)choice.Tag == index;
        }
        foreach (var index in Enumerable.Range(0, FolderUsage.Colors.Length))
        {
            var item = new MenuItem { Header = "●  " + Loc.F("folders.color", index + 1), Tag = index, Foreground = FolderBrush(index), IsCheckable = true };
            AutomationProperties.SetName(item, Loc.F("folders.color", index + 1)); item.Click += (_, _) => Select(index); menu.Items.Add(item);
        }
        Select(preference.Color); color.Click += (_, _) => menu.IsOpen = true; return new(path, color);
    }
    internal static SolidColorBrush FolderBrush(int color) => new((Color)ColorConverter.ConvertFromString(FolderUsage.Colors[color]));
}
