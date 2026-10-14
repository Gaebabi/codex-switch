using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace CodexAccountDesk;

internal static class MacScrollTheme
{
    public static void Install(IResourceDictionary resources, DeskTheme palette)
    {
        var thumbTheme = new ControlTheme(typeof(Thumb));
        thumbTheme.Setters.Add(new Setter(Thumb.BackgroundProperty, palette.Brush("muted")));
        thumbTheme.Setters.Add(new Setter(Thumb.TemplateProperty, new FuncControlTemplate<Thumb>((thumb, _) =>
        {
            var surface = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(3) };
            surface.Bind(Border.BackgroundProperty, new Binding("Background") { Source = thumb });
            return surface;
        })));
        thumbTheme.Children.Add(new Style(s => s.Nesting().Class(":pointerover")) { Setters = { new Setter(Thumb.BackgroundProperty, palette.Brush("text")) } });
        thumbTheme.Children.Add(new Style(s => s.Nesting().Class(":pressed")) { Setters = { new Setter(Thumb.BackgroundProperty, palette.Brush("accent")) } });

        var pageTheme = new ControlTheme(typeof(RepeatButton));
        pageTheme.Setters.Add(new Setter(RepeatButton.FocusableProperty, false));
        pageTheme.Setters.Add(new Setter(RepeatButton.TemplateProperty, new FuncControlTemplate<RepeatButton>((_, _) => new Border { Background = Brushes.Transparent })));
        var theme = new ControlTheme(typeof(ScrollBar));
        theme.Setters.Add(new Setter(ScrollBar.AllowAutoHideProperty, false));
        theme.Setters.Add(new Setter(ScrollBar.BackgroundProperty, Brushes.Transparent));
        theme.Setters.Add(new Setter(ScrollBar.TemplateProperty, new FuncControlTemplate<ScrollBar>((bar, scope) =>
        {
            var vertical = bar.Orientation == Orientation.Vertical;
            var decrease = new RepeatButton { Name = "PART_PageUpButton", Theme = pageTheme };
            var increase = new RepeatButton { Name = "PART_PageDownButton", Theme = pageTheme };
            scope.Register(decrease.Name, decrease); scope.Register(increase.Name, increase);
            var thumb = new Thumb { Theme = thumbTheme, MinHeight = vertical ? 24 : 0, MinWidth = vertical ? 0 : 24 };
            var track = new Track { Orientation = bar.Orientation, IsDirectionReversed = vertical,
                DecreaseButton = decrease, IncreaseButton = increase, Thumb = thumb };
            track.Bind(Track.MinimumProperty, new Binding("Minimum") { Source = bar });
            track.Bind(Track.MaximumProperty, new Binding("Maximum") { Source = bar });
            track.Bind(Track.ViewportSizeProperty, new Binding("ViewportSize") { Source = bar });
            track.Bind(Track.ValueProperty, new Binding("Value") { Source = bar, Mode = BindingMode.TwoWay });
            return new Border { Background = Brushes.Transparent, Width = vertical ? 12 : double.NaN,
                Height = vertical ? double.NaN : 12, Child = track };
        })));
        resources[typeof(ScrollBar)] = theme;
    }
}
