using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class ResourceTrendSurface : FrameworkElement
{
    private Brush _track = Brushes.Transparent, _fill = Brushes.Transparent;
    private Pen _line = new(Brushes.Transparent, 1);
    private ResourceTrendPoint[] _points = Array.Empty<ResourceTrendPoint>();
    private ResourceTrendPoint[][] _segments = Array.Empty<ResourceTrendPoint[]>();
    public double? Percent { get; private set; }
    public int SampleCount => _points.Length;
    public int SegmentCount => _segments.Length;
    public double DurationSeconds => _points.Length > 1 ? (_points[^1].At - _points[0].At).TotalSeconds : 0;
    public void Refresh(ResourceTrendPoint[] points, double? percent, ResourcePressure pressure, Burn0Theme theme)
    {
        _points = points; _segments = ResourceTrend.Segments(points);
        Percent = pressure == ResourcePressure.Unknown ? null : percent;
        _track = theme.Color("surface");
        var color = theme.Color(pressure switch { ResourcePressure.Critical => "danger", ResourcePressure.Warning => "warning", ResourcePressure.Unknown => "muted", _ => "accent" }).Color;
        var fill = new SolidColorBrush(color) { Opacity = theme.Dark ? 0.26 : 0.19 };
        fill.Freeze(); _fill = fill; InvalidateVisual();
        var stroke = new SolidColorBrush(color) { Opacity = 0.7 }; stroke.Freeze();
        _line = new(stroke, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; _line.Freeze();
    }
    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(RenderSize);
        context.PushClip(new RectangleGeometry(bounds, 3, 3));
        context.DrawRectangle(_track, null, bounds);
        if (bounds.Width > 2 && bounds.Height > 2 && _points.Length > 0)
        {
            var duration = DurationSeconds;
            Point Project(ResourceTrendPoint point) => new(duration > 0 ? 1 + (point.At - _points[0].At).TotalSeconds / duration * (bounds.Width - 2) : bounds.Width - 1,
                1 + (100 - point.Percent!.Value) / 100 * (bounds.Height - 2));
            foreach (var segment in _segments)
            {
                var points = segment.Select(Project).ToArray();
                if (points.Length == 1) { context.DrawEllipse(_line.Brush, null, points[0], 0.8, 0.8); continue; }
                var area = new StreamGeometry();
                using (var geometry = area.Open())
                {
                    geometry.BeginFigure(new(points[0].X, bounds.Height - 1), true, true);
                    geometry.PolyLineTo(points, true, false);
                    geometry.LineTo(new(points[^1].X, bounds.Height - 1), true, false);
                }
                area.Freeze(); context.DrawGeometry(_fill, null, area);
                var line = new StreamGeometry();
                using (var geometry = line.Open()) { geometry.BeginFigure(points[0], false, false); geometry.PolyLineTo(points.Skip(1).ToArray(), true, false); }
                line.Freeze(); context.DrawGeometry(null, _line, line);
            }
        }
        context.Pop();
    }
}
