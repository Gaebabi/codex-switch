using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CodexAccountDesk;

internal static class DeskDialogs
{
    private static Window Dialog(DeskTheme theme, string title, double height = 150)
    {
        var window = new Window { Title = title, Width = 430, Height = height, MinWidth = 360, MinHeight = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = height > 150 };
        theme.Apply(window); return window;
    }
    public static Task<string?> Prompt(Window owner, DeskTheme theme, string title, string value)
    {
        var window = Dialog(theme, title); var text = new TextBox { Text = value };
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 12 }; body.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = theme.Button(Loc.T("취소"), () => window.Close()); cancel.IsCancel = true;
        var save = theme.Button(Loc.T("저장"), () => window.Close(text.Text ?? "")); save.IsDefault = true;
        actions.Children.Add(cancel); actions.Children.Add(save); body.Children.Add(actions); window.Content = body;
        window.Opened += (_, _) => { text.Focus(); text.SelectAll(); }; return window.ShowDialog<string?>(owner);
    }
    public static async Task<bool> Confirm(Window owner, DeskTheme theme, string title, string value)
    {
        var window = Dialog(theme, title); var body = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        body.Children.Add(theme.Text(value, 14)); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = theme.Button(Loc.T("취소"), () => window.Close(false)); cancel.IsCancel = true;
        actions.Children.Add(cancel); actions.Children.Add(theme.Button(Loc.T("삭제"), () => window.Close(true)));
        body.Children.Add(actions); window.Content = body; return await window.ShowDialog<bool>(owner);
    }
    public static Task<DrivePreference[]?> Volumes(Window owner, DeskTheme theme, DrivePreference[] saved)
    {
        var window = Dialog(theme, Loc.T("드라이브 설정"), 420);
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(16) }; var rows = new StackPanel { Spacing = 8 };
        var known = new Dictionary<string, string>(StringComparer.Ordinal) { ["/"] = "Macintosh HD" };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
            known[drive.RootDirectory.FullName] = drive.RootDirectory.FullName == "/" ? "Macintosh HD" : Path.GetFileName(drive.RootDirectory.FullName.TrimEnd('/'));
        foreach (var preference in saved) known[preference.Root] = preference.Label;
        var ordered = saved.Select(p => p.Root).Concat(known.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var fields = new List<(string Root, CheckBox Enabled, TextBox Name, Grid Row)>();
        void Move(string path, int offset)
        {
            var index = fields.FindIndex(f => f.Root == path); var next = index + offset;
            if (next < 0 || next >= fields.Count) return;
            var field = fields[index]; fields.RemoveAt(index); fields.Insert(next, field); rows.Children.Remove(field.Row); rows.Children.Insert(next, field.Row);
        }
        foreach (var path in ordered)
        {
            var selection = saved.FirstOrDefault(p => p.Root == path); var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100,Auto,Auto") };
            var enabled = new CheckBox { IsChecked = selection != null, Content = path }; row.Children.Add(enabled);
            var name = new TextBox { Text = selection?.Label ?? known[path], Watermark = path }; Grid.SetColumn(name, 1); row.Children.Add(name); rows.Children.Add(row);
            var up = theme.IconButton(DeskButtonIconKind.Up, Loc.T("위로"), () => Move(path, -1)); Grid.SetColumn(up, 2); row.Children.Add(up);
            var down = theme.IconButton(DeskButtonIconKind.Down, Loc.T("아래로"), () => Move(path, 1)); Grid.SetColumn(down, 3); row.Children.Add(down);
            fields.Add((path, enabled, name, row));
        }
        root.Children.Add(new ScrollViewer { Content = rows });
        var save = theme.Button(Loc.T("저장"), () => window.Close(fields.Where(f => f.Enabled.IsChecked == true).Select(f =>
            new DrivePreference { Root = f.Root, Label = string.IsNullOrWhiteSpace(f.Name.Text) ? f.Root : f.Name.Text.Trim() }).ToArray()));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = theme.Button(Loc.T("취소"), () => window.Close()); cancel.IsCancel = true; save.IsDefault = true;
        actions.Children.Add(cancel); actions.Children.Add(save); Grid.SetRow(actions, 1); root.Children.Add(actions); window.Content = root;
        return window.ShowDialog<DrivePreference[]?>(owner);
    }
}
