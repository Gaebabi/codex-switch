using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class Burn0Theme
{
    private readonly Dictionary<string, string> _tokens = new();
    public bool Dark { get; }
    public Burn0Theme(bool dark)
    {
        Dark = dark;
        var css = ReadResource("Burn0Tokens.css");
        LoadBlock(Regex.Match(css, @":root\s*\{([\s\S]*?)\}").Groups[1].Value);
        if (dark) LoadBlock(Regex.Match(css, @":root\.dark\s*\{([\s\S]*?)\}").Groups[1].Value);
    }
    private void LoadBlock(string block)
    {
        foreach (Match token in Regex.Matches(block, @"--burn0-([a-z-]+)\s*:\s*([^;]+);")) _tokens[token.Groups[1].Value] = token.Groups[2].Value.Trim();
    }
    public SolidColorBrush Color(string name)
    {
        var value = _tokens[name];
        System.Windows.Media.Color color;
        if (value.StartsWith("rgb(var(--burn0-", StringComparison.Ordinal))
        {
            var key = Regex.Match(value, @"var\(--burn0-([a-z-]+)\)").Groups[1].Value;
            var components = _tokens[key].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            color = System.Windows.Media.Color.FromRgb(byte.Parse(components[0]), byte.Parse(components[1]), byte.Parse(components[2]));
        }
        else color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(value);
        var result = new SolidColorBrush(color); result.Freeze(); return result;
    }
    public double Number(string name) => double.Parse(_tokens[name].Replace("px", "").Replace("ms", ""), CultureInfo.InvariantCulture);
    public FontFamily Font => new(_tokens["font"].Split(',')[0].Trim(' ', '"') + ", Segoe UI, Malgun Gothic");
    public static string ReadResource(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("CodexAccountDesk." + suffix) ?? throw new FileNotFoundException("Burn0 디자인 리소스가 없습니다.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static UIElement Icon(string name, Brush brush, double size = 18, bool windowControl = false)
    {
        using var document = JsonDocument.Parse(ReadResource("Burn0Icons.json"));
        var group = new DrawingGroup();
        var pen = new Pen(brush, windowControl ? 2 : 1.75) { StartLineCap = windowControl ? PenLineCap.Flat : PenLineCap.Round,
            EndLineCap = windowControl ? PenLineCap.Flat : PenLineCap.Round, LineJoin = windowControl ? PenLineJoin.Miter : PenLineJoin.Round };
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        double Value(JsonElement attributes, string key) => attributes.TryGetProperty(key, out var v) ? double.Parse(v.GetString()!, CultureInfo.InvariantCulture) : 0;
        foreach (var element in document.RootElement.GetProperty(name).EnumerateArray())
        {
            var kind = element[0].GetString(); var attributes = element[1];
            Geometry geometry = kind switch
            {
                "path" => Geometry.Parse(attributes.GetProperty("d").GetString()!),
                "rect" => new RectangleGeometry(new Rect(Value(attributes, "x"), Value(attributes, "y"), Value(attributes, "width"), Value(attributes, "height")), Value(attributes, "rx"), Value(attributes, "ry")),
                "circle" => new EllipseGeometry(new Point(Value(attributes, "cx"), Value(attributes, "cy")), Value(attributes, "r"), Value(attributes, "r")),
                "line" => new LineGeometry(new Point(Value(attributes, "x1"), Value(attributes, "y1")), new Point(Value(attributes, "x2"), Value(attributes, "y2"))),
                _ => throw new InvalidDataException("지원되지 않는 Burn0 아이콘 도형입니다.")
            };
            group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }
        group.Freeze(); return new Image { Source = new DrawingImage(group), Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }
}
