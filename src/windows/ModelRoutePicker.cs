using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace CodexAccountDesk;

internal sealed class ModelRoutePicker : Grid
{
    private readonly ModelProviderManager _manager;
    private readonly Burn0Theme _theme;
    private readonly Button _models = new() { MinHeight = 30, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 5, 10, 5) };
    private readonly TextBlock _caption = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label;
    private readonly Action<Exception> _failed;
    private readonly Action? _selected;
    internal Button Models => _models;
    internal ModelRoutePicker(Burn0Theme theme, ModelProviderManager manager, Action<Exception> failed, Action? selected = null)
    {
        _theme = theme; _manager = manager; _failed = failed; _selected = selected; Margin = new Thickness(0, 0, 0, 6);
        ColumnDefinitions.Add(new() { Width = GridLength.Auto }); ColumnDefinitions.Add(new());
        _label = new TextBlock { Foreground = theme.Color("muted"), FontSize = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_label); _models.Name = "ModelRoutePicker"; _models.FontSize = 12;
        _models.Foreground = _caption.Foreground = theme.Color("text"); _models.Background = theme.Color("surface"); _models.BorderBrush = theme.Color("divider"); _models.BorderThickness = new Thickness(1);
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        border.AppendChild(presenter); _models.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var style = new Style(typeof(Button)); var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .55)); style.Triggers.Add(disabled);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, theme.Color("accent"))); style.Triggers.Add(focus); _models.Style = style;
        var content = new Grid(); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.Children.Add(_caption);
        var arrow = new TextBlock { Text = "▾", Foreground = theme.Color("muted"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(arrow, 1); content.Children.Add(arrow); _models.Content = content;
        Grid.SetColumn(_models, 1); Children.Add(_models);
        void Open() { var menu = CreateMenu(); _models.ContextMenu = menu; menu.IsOpen = true; }
        _models.Click += (_, _) => Open(); _models.PreviewKeyDown += (_, args) => { if (args.Key == Key.Down) { Open(); args.Handled = true; } };
        Refresh();
    }
    internal ContextMenu CreateMenu()
    {
        var menu = DropdownTheme.Create(_models, _theme); var selected = _manager.Settings.SelectedRoute;
        foreach (var choice in _manager.RoutingChoices)
        {
            var item = new MenuItem { Header = choice.Label, Tag = choice.Id, IsCheckable = true, IsChecked = choice.Id == selected };
            item.Click += (_, _) =>
            {
                try { _manager.SelectRoute(choice.Id); menu.IsOpen = false; Refresh(); _selected?.Invoke(); }
                catch (Exception error) { menu.IsOpen = false; Refresh(); _failed(error); }
            };
            menu.Items.Add(item);
        }
        return menu;
    }
    internal void Refresh()
    {
        if (_models.ContextMenu?.IsOpen == true) return;
        var settings = _manager.Settings; var choices = _manager.RoutingChoices;
        Visibility = choices.Count == 0 && settings.SelectedRoute == null ? Visibility.Collapsed : Visibility.Visible;
        _label.Text = Loc.T(settings.Connected && settings.RouterEnabled ? "router.model" : "router.setup_model");
        _caption.Text = choices.FirstOrDefault(choice => choice.Id == settings.SelectedRoute)?.Label ?? Loc.T(settings.SelectedRoute == null ? "router.choose" : "router.unavailable");
        _models.Tag = settings.SelectedRoute;
        AutomationProperties.SetName(_models, Loc.T("router.model") + ": " + _caption.Text);
        _models.ToolTip = _caption.Text + "\n" + Loc.T(settings.Connected && settings.RouterEnabled ? "router.next_request" : "router.first_connection");
    }
}
