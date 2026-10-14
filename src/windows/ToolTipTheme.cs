using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CodexAccountDesk;

internal static class ToolTipTheme
{
    private static readonly ConditionalWeakTable<Burn0Theme, Style> Styles = new();
    public static Style StyleFor(Burn0Theme theme) => Styles.GetValue(theme, BuildStyle);
    public static void Install(ResourceDictionary resources, Burn0Theme theme) => resources[typeof(ToolTip)] = StyleFor(theme);

    private static Style BuildStyle(Burn0Theme theme)
    {
        var style = new Style(typeof(ToolTip));
        style.Setters.Add(new Setter(Control.BackgroundProperty, theme.Color("elevated")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, theme.Color("text")));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, theme.Font));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 340.0));
        style.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
        var textTemplate = new DataTemplate(typeof(string));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(TextBlock.LineHeightProperty, 18.0);
        textTemplate.VisualTree = text; style.Resources[new DataTemplateKey(typeof(string))] = textTemplate;
        var template = new ControlTemplate(typeof(ToolTip));
        var surface = new FrameworkElementFactory(typeof(Border)); surface.Name = "tipSurface";
        surface.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); surface.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));
        surface.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        surface.SetValue(UIElement.EffectProperty, new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = theme.Dark ? 0.32 : 0.12 });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        surface.AppendChild(content); template.VisualTree = surface;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new EventSetter(ToolTip.OpenedEvent, new RoutedEventHandler(Opened)));
        return style;
    }

    private static void Opened(object sender, RoutedEventArgs args)
    {
        var tip = (ToolTip)sender; tip.ApplyTemplate();
        var surface = (Border)tip.Template.FindName("tipSurface", tip);
        AnimateEntrance(surface, SystemParameters.ClientAreaAnimation);
    }
    private static void AnimateEntrance(Border surface, bool animate)
    {
        surface.BeginAnimation(UIElement.OpacityProperty, null); surface.Opacity = animate ? 0 : 1;
        var shift = new TranslateTransform(0, animate ? 3 : 0); surface.RenderTransform = shift;
        if (!animate) return;
        var duration = TimeSpan.FromMilliseconds(120);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(3, 0, duration) { EasingFunction = easing });
    }

    public static void Resource(FrameworkElement owner, string title, string value, string details, Burn0Theme theme, string valueColor)
    {
        if (owner.ToolTip is not ResourceToolTip tip)
        {
            tip = new ResourceToolTip { PlacementTarget = owner, Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = (popup, target, _) => new[] {
                    new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height + 2), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height - 2), PopupPrimaryAxis.Horizontal) } };
            ToolTipService.SetPlacement(owner, PlacementMode.Custom);
            ToolTipService.SetInitialShowDelay(owner, 0); ToolTipService.SetBetweenShowDelay(owner, 0); ToolTipService.SetShowDuration(owner, int.MaxValue);
            owner.ToolTip = tip;
            owner.Unloaded += (_, _) => tip.IsOpen = false;
        }
        tip.Refresh(title, value, details, theme, valueColor);
    }
}

internal sealed class ResourceToolTip : ToolTip
{
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _details = new() { FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    public ResourceToolTip()
    {
        Width = 320; UseLayoutRounding = true; SnapsToDevicePixels = true;
        var body = new StackPanel(); var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(_title); Grid.SetColumn(_value, 1); heading.Children.Add(_value); body.Children.Add(heading); body.Children.Add(_details); Content = body;
    }
    public void Refresh(string title, string value, string details, Burn0Theme theme, string valueColor)
    {
        Style = ToolTipTheme.StyleFor(theme); FontFamily = theme.Font;
        _title.Text = title; _title.Foreground = theme.Color("text"); _value.Text = value; _value.Foreground = theme.Color(valueColor);
        _details.Text = details; _details.Foreground = theme.Color("muted");
        System.Windows.Automation.AutomationProperties.SetName(this, title + " " + value + "\n" + details);
    }
}
