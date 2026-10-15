using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal sealed class TankSurface : Control
{
    private readonly DeskTheme _theme;
    private readonly double? _remaining;
    private readonly bool _active;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _motion = new() { Interval = TimeSpan.FromMilliseconds(60) };
    public TankSurface(DeskTheme theme, double? remaining, bool active)
    {
        _theme = theme; _remaining = remaining; _active = active; IsHitTestVisible = false;
        _motion.Tick += (_, _) => { if (TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized }) InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => { if (_active && _remaining is > 0 and < 100) _motion.Start(); };
        DetachedFromVisualTree += (_, _) => _motion.Stop();
    }
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(_theme.Brush("surface"), null, rect, 8, 8);
        if (_remaining is not double remaining || remaining <= 0 || rect.Height <= 0) return;
        var color = _theme.Brush(remaining < 15 ? "warning" : "accent").Color;
        var fill = new SolidColorBrush(Color.FromArgb((byte)(_theme.Dark ? 45 : 24), color.R, color.G, color.B));
        var water = rect.Height * (1 - remaining / 100);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(0, rect.Height), true);
            path.LineTo(new Point(0, water));
            for (var i = 0; i <= 32; i++)
            {
                var x = rect.Width * i / 32;
                var wave = _active && remaining < 100 ? Math.Sin(i / 32.0 * Math.PI * 2 + _clock.Elapsed.TotalSeconds * 1.6) * 1.8 : 0;
                path.LineTo(new Point(x, water + wave));
            }
            path.LineTo(new Point(rect.Width, rect.Height)); path.EndFigure(true);
        }
        using (context.PushClip(new RoundedRect(rect, 8))) context.DrawGeometry(fill, null, geometry);
    }
}

internal sealed class PercentChart : Control
{
    public ResourceTrendPoint[] Points { get; set; } = Array.Empty<ResourceTrendPoint>();
    public required DeskTheme Palette { get; set; }
    public string ColorName { get; set; } = "accent";
    public override void Render(DrawingContext context)
    {
        var points = Points;
        if (points.Length == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var start = points[0].At; var span = Math.Max(1, (points[^1].At - start).TotalSeconds);
        var color = Palette.Brush(ColorName); var pen = new Pen(color, 1.2);
        Point Position(ResourceTrendPoint point) => new((point.At - start).TotalSeconds / span * Bounds.Width, (1 - point.Percent!.Value / 100) * Bounds.Height);
        foreach (var segment in ResourceTrend.Segments(points))
        {
            if (segment.Length == 1) { context.DrawEllipse(color, null, Position(segment[0]), 1.5, 1.5); continue; }
            var area = new StreamGeometry();
            using (var path = area.Open())
            {
                path.BeginFigure(new Point(Position(segment[0]).X, Bounds.Height), true);
                foreach (var point in segment) path.LineTo(Position(point));
                path.LineTo(new Point(Position(segment[^1]).X, Bounds.Height)); path.EndFigure(true);
            }
            var c = color.Color; context.DrawGeometry(new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)), null, area);
            for (var i = 1; i < segment.Length; i++) context.DrawLine(pen, Position(segment[i - 1]), Position(segment[i]));
        }
    }
}

internal sealed class HistoryChart : Control
{
    public UsageObservation[] Points { get; set; } = Array.Empty<UsageObservation>();
    public required DeskTheme Palette { get; set; }
    public int Hours { get; set; } = 24;
    public override void Render(DrawingContext context)
    {
        var now = DateTimeOffset.UtcNow; var start = now.AddHours(-Hours);
        var points = Points.Where(p => p.At >= start).ToArray();
        var pen = new Pen(Palette.Brush("accent"), 2);
        Point Position(UsageObservation point) => new(Math.Clamp((point.At - start).TotalHours / Hours, 0, 1) * Bounds.Width, point.UsedPercent / 100 * Bounds.Height);
        for (var i = 0; i < points.Length; i++)
        {
            if (i > 0 && points[i].Segment == points[i - 1].Segment && points[i].At - points[i - 1].At <= TimeSpan.FromMinutes(20))
                context.DrawLine(pen, Position(points[i - 1]), Position(points[i]));
            else context.DrawEllipse(Palette.Brush("accent"), null, Position(points[i]), 2, 2);
        }
    }
}
