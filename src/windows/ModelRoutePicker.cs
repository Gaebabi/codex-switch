using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class ModelRoutePicker : Grid
{
    private readonly ModelProviderManager _manager;
    private readonly Burn0Theme _theme;
    private readonly Button _models = new() { Width = 30, Height = 30, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Action<Exception> _failed;
    private readonly Action? _selected, _manage;
    internal Button Models => _models;
    internal ModelRoutePicker(Burn0Theme theme, ModelProviderManager manager, Action<Exception> failed, Action? selected = null, Action? manage = null)
    {
        _theme = theme; _manager = manager; _failed = failed; _selected = selected; _manage = manage;
        HorizontalAlignment = HorizontalAlignment.Left;
        _models.Name = "ModelRoutePicker";
        _models.BorderThickness = new Thickness(1);
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        border.AppendChild(presenter); _models.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .55)); style.Triggers.Add(disabled);
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, theme.Color("hover"))); style.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, theme.Color("accent"))); style.Triggers.Add(focus); _models.Style = style;
        Children.Add(_models);
        void Open() { var menu = CreateMenu(); _models.ContextMenu = menu; menu.IsOpen = true; }
        _models.Click += (_, _) => Open(); _models.PreviewKeyDown += (_, args) => { if (args.Key == Key.Down) { Open(); args.Handled = true; } };
        Refresh();
    }
    internal ContextMenu CreateMenu()
    {
        var menu = DropdownTheme.Create(_models, _theme);
        foreach (var provider in _manager.Settings.Providers)
        {
            var heading = new MenuItem { Header = provider.Name, IsEnabled = false };
            var section = new FrameworkElementFactory(typeof(TextBlock));
            section.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Header") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            section.SetValue(TextBlock.ForegroundProperty, _theme.Color("muted")); section.SetValue(TextBlock.FontSizeProperty, 11.0);
            section.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); section.SetValue(MarginProperty, new Thickness(8, 8, 8, 3));
            heading.Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = section }; menu.Items.Add(heading);
            foreach (var model in provider.Models)
            {
                var id = model.Alias(provider.Id);
                var item = DropdownTheme.Switch(model.UpstreamId, model.EnabledInCodex, _theme); item.Tag = id;
                item.ToolTip = Loc.T("router.visibility_help"); AutomationProperties.SetName(item, model.UpstreamId + " — " + Loc.T("router.visible"));
                item.Click += async (_, _) =>
                {
                    var enabled = item.IsChecked; menu.IsEnabled = false;
                    try { await _manager.SetModelEnabledAsync(id, enabled, CancellationToken.None); _selected?.Invoke(); }
                    catch (Exception error) { _failed(error); }
                    finally
                    {
                        item.IsChecked = _manager.Settings.Providers.SelectMany(p => p.Models.Select(m => (p, m))).Single(pair => pair.m.Alias(pair.p.Id) == id).m.EnabledInCodex;
                        menu.IsEnabled = true; Refresh();
                    }
                };
                menu.Items.Add(item);
            }
        }
        if (_manage != null)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var settings = new MenuItem { Header = Loc.T("providers.title") };
            settings.Click += (_, _) => { menu.IsOpen = false; _manage(); }; menu.Items.Add(settings);
        }
        return menu;
    }
    internal void Refresh()
    {
        var settings = _manager.Settings; var count = settings.Providers.SelectMany(provider => provider.Models).Count(model => model.EnabledInCodex);
        Visibility = settings.Providers.Count == 0 && _manage == null ? Visibility.Collapsed : Visibility.Visible;
        var brush = _theme.Color(count == 0 ? "muted" : "text");
        _models.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M4 5H20M4 12H20M4 19H20 M9 3V7M15 10V14M9 17V21"),
            Stroke = brush, StrokeThickness = 1.75, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, Width = 16, Height = 18, IsHitTestVisible = false };
        var label = Loc.F("router.visible_count", count);
        AutomationProperties.SetName(_models, label);
        _models.ToolTip = label + "\n" + Loc.T("router.visibility_help") +
            (_manager.CatalogRefreshStatus.DisplayMessage is string status ? "\n" + status : "");
    }
}
