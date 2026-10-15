using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AButton = Avalonia.Controls.Button;

namespace CodexAccountDesk;

internal sealed class DeskTheme
{
    private readonly Dictionary<string, string> _tokens = new();
    public bool Dark { get; }
    public DesktopAppearance Appearance { get; }
    public DeskTheme(bool dark, DesktopAppearance appearance)
    {
        Dark = dark; Appearance = appearance;
        using var stream = typeof(DeskTheme).Assembly.GetManifestResourceStream("CodexAccountDesk.Burn0Tokens.css")!;
        using var reader = new StreamReader(stream); var css = reader.ReadToEnd();
        Load(Regex.Match(css, @":root\s*\{([\s\S]*?)\}").Groups[1].Value);
        if (dark) Load(Regex.Match(css, @":root\.dark\s*\{([\s\S]*?)\}").Groups[1].Value);
    }
    private void Load(string block) { foreach (Match token in Regex.Matches(block, @"--burn0-([a-z-]+)\s*:\s*([^;]+);")) _tokens[token.Groups[1].Value] = token.Groups[2].Value.Trim(); }
    public SolidColorBrush Brush(string name)
    {
        var value = _tokens[name];
        if (value.StartsWith("rgb(var(--burn0-", StringComparison.Ordinal))
        {
            var key = Regex.Match(value, @"var\(--burn0-([a-z-]+)\)").Groups[1].Value;
            var parts = _tokens[key].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new(Color.FromRgb(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2])));
        }
        return new(Color.Parse(value));
    }
    public void Apply(Window window)
    {
        window.Background = Brush("canvas"); window.Foreground = Brush("text"); window.FontFamily = new FontFamily(Appearance.FontFamily);
        window.RequestedThemeVariant = Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Styles.Clear();
        MacScrollTheme.Install(window.Resources, this);
        window.Resources[typeof(ToolTip)] = MacToolTipTheme.For(this);
        window.Styles.Add(new Style(selector => selector.OfType<Button>()) { Setters = {
            new Setter(AButton.BackgroundProperty, Brush("surface")), new Setter(AButton.ForegroundProperty, Brush("text")),
            new Setter(AButton.BorderThicknessProperty, new Thickness(0)), new Setter(AButton.CornerRadiusProperty, new CornerRadius(8)),
            new Setter(AButton.HorizontalContentAlignmentProperty, HorizontalAlignment.Center), new Setter(AButton.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            new Setter(AButton.MinHeightProperty, 26.0), new Setter(AButton.PaddingProperty, new Thickness(8, 3)), new Setter(AButton.FontSizeProperty, 12.0) } });
        window.Styles.Add(new Style(selector => selector.OfType<AButton>().Class(":pointerover")) { Setters = { new Setter(AButton.BackgroundProperty, Brush("hover")) } });
        window.Styles.Add(new Style(selector => selector.OfType<AButton>().Class(":disabled")) { Setters = { new Setter(AButton.OpacityProperty, 0.5) } });
        window.Styles.Add(new Style(selector => selector.OfType<AButton>().Class("title-action")) { Setters = {
            new Setter(AButton.BackgroundProperty, Brushes.Transparent), new Setter(AButton.WidthProperty, 28.0), new Setter(AButton.HeightProperty, 28.0),
            new Setter(AButton.PaddingProperty, new Thickness(0)), new Setter(AButton.VerticalAlignmentProperty, VerticalAlignment.Center) } });
        window.Styles.Add(new Style(selector => selector.OfType<AButton>().Class("title-action").Class(":pointerover")) { Setters = { new Setter(AButton.BackgroundProperty, Brush("hover")) } });
    }
    public TextBlock Text(string value, double size = 12, string color = "text") => new() { Text = value, FontSize = size, Foreground = Brush(color), TextTrimming = TextTrimming.CharacterEllipsis };
    public ContextMenu Menu(Control owner)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight, VerticalOffset = 4 };
        MacScrollTheme.Install(menu.Resources, this);
        menu.Template = new FuncControlTemplate<ContextMenu>((parent, scope) =>
        {
            var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            scope.Register(items.Name, items);
            items.Bind(ItemsPresenter.ItemsPanelProperty, new Binding("ItemsPanel") { Source = parent });
            var scroll = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AllowAutoHide = false };
            return new Border { Background = Brush("elevated"), CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Child = scroll };
        });
        menu.Styles.Add(new Style(selector => selector.OfType<ContextMenu>()) { Setters = {
            new Setter(ContextMenu.BackgroundProperty, Brush("elevated")), new Setter(ContextMenu.ForegroundProperty, Brush("text")),
            new Setter(ContextMenu.BorderThicknessProperty, new Thickness(0)), new Setter(ContextMenu.CornerRadiusProperty, new CornerRadius(12)),
            new Setter(ContextMenu.PaddingProperty, new Thickness(6)), new Setter(ContextMenu.FontSizeProperty, 13.0),
            new Setter(ContextMenu.MinWidthProperty, 160.0), new Setter(ContextMenu.MaxWidthProperty, 336.0), new Setter(ContextMenu.MaxHeightProperty, 336.0) } });
        menu.Styles.Add(new Style(selector => selector.OfType<MenuItem>()) { Setters = {
            new Setter(MenuItem.ForegroundProperty, Brush("text")), new Setter(MenuItem.BackgroundProperty, Brushes.Transparent),
            new Setter(MenuItem.PaddingProperty, new Thickness(8, 6, 10, 6)), new Setter(MenuItem.MinHeightProperty, 32.0),
            new Setter(MenuItem.FontSizeProperty, 13.0), new Setter(MenuItem.CornerRadiusProperty, new CornerRadius(6)) } });
        menu.Styles.Add(new Style(selector => selector.OfType<MenuItem>().Class(":pointerover")) { Setters = { new Setter(MenuItem.BackgroundProperty, Brush("hover")) } });
        menu.Styles.Add(new Style(selector => selector.OfType<MenuItem>().Class(":checked")) { Setters = { new Setter(MenuItem.BackgroundProperty, Brush("selected")) } });
        menu.Styles.Add(new Style(selector => selector.OfType<MenuItem>().Class(":disabled")) { Setters = { new Setter(MenuItem.OpacityProperty, 0.45) } });
        menu.Styles.Add(new Style(selector => selector.OfType<Separator>()) { Setters = {
            new Setter(Separator.BackgroundProperty, Brush("divider")), new Setter(Separator.MarginProperty, new Thickness(8, 5)), new Setter(Separator.HeightProperty, 1.0) } });
        EventHandler<VisualTreeAttachmentEventArgs> detached = (_, _) => menu.Close();
        menu.Opened += (_, _) => owner.DetachedFromVisualTree += detached;
        menu.Closed += (_, _) => owner.DetachedFromVisualTree -= detached;
        return menu;
    }
    public Button Button(string value, Action action) { var button = new Button { Content = value }; button.Click += (_, _) => action(); return button; }
    public Button IconButton(DeskButtonIconKind kind, string label, Action action)
    {
        var button = Button("", action); button.Content = new DeskButtonIcon(kind, Brush("text"));
        AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); return button;
    }
}
