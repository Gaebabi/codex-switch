using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal static class MacToolTipTheme
{
    private static readonly ConditionalWeakTable<DeskTheme, ControlTheme> Themes = new();
    public static ControlTheme For(DeskTheme theme) => Themes.GetValue(theme, palette =>
    {
        var result = new ControlTheme(typeof(ToolTip));
        result.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, new FontFamily(palette.Appearance.FontFamily)));
        result.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, palette.Brush("text")));
        result.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 12.0));
        result.Setters.Add(new Setter(Layoutable.MaxWidthProperty, 340.0));
        result.Setters.Add(new Setter(InputElement.IsHitTestVisibleProperty, false));
        result.Setters.Add(new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToolTip>((tip, _) =>
        {
            var content = new ContentPresenter { Name = "PART_ContentPresenter", ContentTemplate = new FuncDataTemplate<object>((value, _) => value is Control control ? control : new TextBlock { Text = value?.ToString(), TextWrapping = TextWrapping.Wrap, LineHeight = 18 }) };
            content.Bind(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var surface = new Border { Name = "tipSurface", Margin = new Thickness(8), Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(8),
                Background = palette.Brush("elevated"), BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, OffsetY = 2, Color = Color.FromArgb(palette.Dark ? (byte)82 : (byte)31, 0, 0, 0) }), Child = content };
            ((IPopupHostProvider)tip).PopupHostChanged += host =>
            {
                if (host == null) { surface.Transitions = null; surface.Opacity = palette.Appearance.ReducedMotion() ? 1 : 0; return; }
                surface.Transitions = null; surface.Opacity = palette.Appearance.ReducedMotion() ? 1 : 0;
                if (surface.Opacity == 1) return;
                surface.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new QuadraticEaseOut() } };
                Dispatcher.UIThread.Post(() => surface.Opacity = 1, DispatcherPriority.Render);
            };
            return surface;
        })));
        return result;
    });
    public static void Resource(Control owner, string title, string value, string details, DeskTheme theme, string valueColor)
    {
        if (ToolTip.GetTip(owner) is not MacResourceToolTip tip)
        {
            tip = new MacResourceToolTip(); ToolTip.SetTip(owner, tip);
            ToolTip.SetShowDelay(owner, 0); ToolTip.SetBetweenShowDelay(owner, 0); ToolTip.SetPlacement(owner, PlacementMode.Top); ToolTip.SetVerticalOffset(owner, 2);
            owner.DetachedFromVisualTree += (_, _) => ToolTip.SetIsOpen(owner, false);
        }
        tip.Refresh(title, value, details, theme, valueColor);
    }
}

internal sealed class MacResourceToolTip : ToolTip
{
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _details = new() { FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    public MacResourceToolTip()
    {
        Width = 320; var body = new StackPanel(); var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(_title); Grid.SetColumn(_value, 1); heading.Children.Add(_value); body.Children.Add(heading); body.Children.Add(_details); Content = body;
    }
    public void Refresh(string title, string value, string details, DeskTheme theme, string valueColor)
    {
        Theme = MacToolTipTheme.For(theme); _title.Text = title; _value.Text = value; _details.Text = details;
        _title.Foreground = theme.Brush("text"); _value.Foreground = theme.Brush(valueColor); _details.Foreground = theme.Brush("muted");
    }
}
