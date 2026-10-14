using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CodexAccountDesk;

internal static class DropdownTheme
{
    public static ContextMenu Create(FrameworkElement owner, Burn0Theme theme)
    {
        var menu = new ContextMenu { PlacementTarget = owner, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popup, target, _) => new[] {
                new CustomPopupPlacement(new Point(target.Width - popup.Width + 12, target.Height - 8), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(target.Width - popup.Width + 12, -popup.Height + 8), PopupPrimaryAxis.Horizontal) },
            UseLayoutRounding = true, SnapsToDevicePixels = true,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag) };
        Install(menu.Resources, theme);
        RoutedEventHandler ownerUnloaded = (_, _) => menu.IsOpen = false;
        menu.Opened += (_, _) => owner.Unloaded += ownerUnloaded;
        menu.Closed += (_, _) => owner.Unloaded -= ownerUnloaded;
        return menu;
    }

    public static void Install(ResourceDictionary resources, Burn0Theme theme)
    {
        var panel = new Style(typeof(ContextMenu));
        panel.Setters.Add(new Setter(Control.BackgroundProperty, theme.Color("elevated")));
        panel.Setters.Add(new Setter(Control.ForegroundProperty, theme.Color("text")));
        panel.Setters.Add(new Setter(Control.FontFamilyProperty, theme.Font));
        panel.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        panel.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        panel.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 184.0));
        panel.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 360.0));
        panel.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 360.0));
        panel.Setters.Add(new Setter(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle));
        var panelTemplate = new ControlTemplate(typeof(ContextMenu));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(FrameworkElement.MarginProperty, new Thickness(12));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(theme.Number("popup-radius")));
        surface.SetValue(Border.PaddingProperty, new Thickness(6));
        surface.SetBinding(Border.BackgroundProperty, ParentBinding("Background"));
        surface.SetValue(UIElement.EffectProperty, new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4,
            Opacity = theme.Dark ? 0.32 : 0.12, RenderingBias = RenderingBias.Quality });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(Control.FocusableProperty, false);
        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
        scroll.AppendChild(items); surface.AppendChild(scroll); panelTemplate.VisualTree = surface;
        panel.Setters.Add(new Setter(Control.TemplateProperty, panelTemplate));

        var row = new Style(typeof(MenuItem));
        row.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        row.Setters.Add(new Setter(Control.ForegroundProperty, theme.Color("text")));
        row.Setters.Add(new Setter(Control.FontFamilyProperty, theme.Font));
        row.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        row.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 10, 6)));
        row.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32.0));
        row.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        row.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var rowTemplate = new ControlTemplate(typeof(MenuItem));
        var rowSurface = new FrameworkElementFactory(typeof(Border));
        rowSurface.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        rowSurface.SetBinding(Border.BackgroundProperty, ParentBinding("Background"));
        rowSurface.SetBinding(Border.PaddingProperty, ParentBinding("Padding"));
        var content = new FrameworkElementFactory(typeof(DockPanel));
        var checkSlot = new FrameworkElementFactory(typeof(Grid));
        checkSlot.SetValue(FrameworkElement.WidthProperty, 20.0); checkSlot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        checkSlot.SetValue(DockPanel.DockProperty, Dock.Left);
        var check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)); check.Name = "check";
        check.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M3,7 L6,10 L12,3"));
        check.SetValue(System.Windows.Shapes.Shape.StrokeProperty, theme.Color("accent"));
        check.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.8);
        check.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        check.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        checkSlot.AppendChild(check); content.AppendChild(checkSlot);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, ParentBinding("Header"));
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.LineHeightProperty, 18.0);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.AppendChild(label); rowSurface.AppendChild(content); rowTemplate.VisualTree = rowSurface;
        var checkedRow = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        checkedRow.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "check")); rowTemplate.Triggers.Add(checkedRow);
        row.Setters.Add(new Setter(Control.TemplateProperty, rowTemplate));
        var selected = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, theme.Color("selected"))); row.Triggers.Add(selected);
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, theme.Color("hover"))); row.Triggers.Add(highlighted);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); row.Triggers.Add(disabled);

        var separator = new Style(typeof(Separator));
        var separatorTemplate = new ControlTemplate(typeof(Separator));
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(FrameworkElement.HeightProperty, 1.0); line.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5));
        line.SetValue(Border.BackgroundProperty, theme.Color("divider")); separatorTemplate.VisualTree = line;
        separator.Setters.Add(new Setter(Control.TemplateProperty, separatorTemplate));
        resources[typeof(ContextMenu)] = panel; resources[typeof(MenuItem)] = row;
        resources[typeof(Separator)] = separator; resources[MenuItem.SeparatorStyleKey] = separator;
    }

    private static Binding ParentBinding(string property) => new(property) { RelativeSource = RelativeSource.TemplatedParent };
}
