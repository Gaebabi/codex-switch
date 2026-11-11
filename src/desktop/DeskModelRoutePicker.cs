using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CodexAccountDesk;

internal sealed class DeskModelRoutePicker : Grid
{
    private readonly ModelProviderManager _manager;
    private readonly DeskTheme _theme;
    private readonly Button _models = new() { MinHeight = 30, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(7) };
    private readonly TextBlock _caption = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label;
    private readonly Action<Exception> _failed;
    private readonly Action? _selected;
    internal DeskModelRoutePicker(DeskTheme theme, ModelProviderManager manager, Action<Exception> failed, Action? selected = null)
    {
        _theme = theme; _manager = manager; _failed = failed; _selected = selected; ColumnDefinitions = new ColumnDefinitions("Auto,*"); Margin = new Thickness(0, 0, 0, 6);
        _label = theme.Text("", 12, "muted"); _label.Margin = new Thickness(0, 0, 10, 0); _label.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_label); _models.Name = "ModelRoutePicker"; _models.FontSize = 12;
        _models.Foreground = _caption.Foreground = theme.Brush("text"); _models.Background = theme.Brush("surface");
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; content.Children.Add(_caption);
        var arrow = theme.Text("▾", 12); arrow.Margin = new Thickness(10, 0, 0, 0); arrow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(arrow, 1); content.Children.Add(arrow); _models.Content = content;
        Grid.SetColumn(_models, 1); Children.Add(_models);
        void Open()
        {
            var menu = _theme.Menu(_models); var selectedId = _manager.Settings.SelectedRoute;
            foreach (var choice in _manager.RoutingChoices)
            {
                var item = new MenuItem { Header = choice.Label, ToggleType = MenuItemToggleType.Radio, IsChecked = choice.Id == selectedId };
                item.Click += (_, _) =>
                {
                    try { _manager.SelectRoute(choice.Id); menu.Close(); Refresh(); _selected?.Invoke(); }
                    catch (Exception error) { menu.Close(); Refresh(); _failed(error); }
                };
                menu.Items.Add(item);
            }
            _models.ContextMenu = menu; menu.Open(_models);
        }
        _models.Click += (_, _) => Open(); _models.KeyDown += (_, args) => { if (args.Key == Key.Down) { Open(); args.Handled = true; } };
        Refresh();
    }
    internal void Refresh()
    {
        if (_models.ContextMenu?.IsOpen == true) return;
        var settings = _manager.Settings; var choices = _manager.RoutingChoices;
        IsVisible = choices.Count != 0 || settings.SelectedRoute != null;
        _label.Text = Loc.T(settings.Connected && settings.RouterEnabled ? "router.model" : "router.setup_model");
        _caption.Text = choices.FirstOrDefault(choice => choice.Id == settings.SelectedRoute)?.Label ?? Loc.T(settings.SelectedRoute == null ? "router.choose" : "router.unavailable");
        AutomationProperties.SetName(_models, Loc.T("router.model") + ": " + _caption.Text);
        ToolTip.SetTip(_models, _caption.Text + "\n" + Loc.T(settings.Connected && settings.RouterEnabled ? "router.next_request" : "router.first_connection"));
    }
}
