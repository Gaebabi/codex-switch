using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class UsageTrendChart : FrameworkElement
{
    private readonly UsageWindow _window;
    private readonly UsageObservation[] _history;
    private readonly Burn0Theme _theme;
    private readonly int _hours;
    private readonly bool _simple;
    private readonly List<(Point Position, UsageObservation Observation)> _hits = new();
    private DateTimeOffset _now;
    private UsageForecast _forecast;
    public UsageTrendChart(UsageWindow window, UsageObservation[] history, Burn0Theme theme, int hours, DateTimeOffset now, bool simple = false)
    {
        _window = window; _history = history; _theme = theme; _hours = hours; _now = now;
        _forecast = UsagePredictor.Evaluate(window, history, now); _simple = simple;
        Height = simple ? 116 : 156; MinWidth = 120; ClipToBounds = true;
    }
    public void Update(DateTimeOffset now, UsageForecast forecast)
    {
        var redraw = now.ToUnixTimeSeconds() / 60 != _now.ToUnixTimeSeconds() / 60 || forecast != _forecast;
        _now = now; _forecast = forecast;
        if (redraw) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        _hits.Clear();
        var left = 33.0; var right = ActualWidth - 8; var top = 25.0; var bottom = 91.0;
        if (right <= left) return;
        if (!_simple) { DrawText(drawing, Loc.T("사용량 추이"), 0, 0, "muted", 11); DrawText(drawing, Loc.T("실측"), right, 0, "accent", 11, true); }
        foreach (var value in _simple ? new[] { 0, 100 } : new[] { 0, 50, 100 })
        {
            var y = bottom - (bottom - top) * value / 100;
            drawing.DrawLine(new Pen(_theme.Color("divider"), 1), new Point(left, y), new Point(right, y));
            DrawText(drawing, value + "%", left - 7, y - 7, "muted", 10, true);
        }
        var visible = _history.Where(p => p.At >= _now.AddHours(-_hours) && p.At <= _now).ToArray();
        var start = visible.Length == 0 ? _now.AddHours(-_hours) : visible[0].At.AddMinutes(-1);
        if (_now - start < TimeSpan.FromMinutes(5)) start = _now.AddMinutes(-5);
        if (start < _now.AddHours(-_hours)) start = _now.AddHours(-_hours);
        var seconds = (_now - start).TotalSeconds;
        Point Position(UsageObservation point) => new(left + (right - left) * (point.At - start).TotalSeconds / seconds,
            bottom - (bottom - top) * Math.Clamp(point.UsedPercent, 0, 100) / 100);
        var segment = new List<Point>();
        UsageObservation? previous = null;
        foreach (var observation in visible)
        {
            if (previous != null && (previous.Segment != observation.Segment || observation.At - previous.At > TimeSpan.FromMinutes(30)))
            { DrawSegment(drawing, segment, bottom); segment.Clear(); }
            var point = Position(observation); segment.Add(point); _hits.Add((point, observation)); previous = observation;
        }
        DrawSegment(drawing, segment, bottom);
        if (visible.Length < 2)
            DrawText(drawing, Loc.T("조회 기록이 쌓이면 추이를 볼 수 있습니다."), (left + right) / 2, 53, "muted", 11, centered: true);
        if (visible.Length > 0)
        {
            var last = Position(visible[^1]);
            drawing.DrawEllipse(_theme.Color("accent"), new Pen(_theme.Color("surface"), 2), last, 4, 4);
        }
        var format = _now - start >= TimeSpan.FromHours(12) ? "MM/dd HH:mm" : "HH:mm";
        DrawText(drawing, UsagePresentation.KoreanTime(start).ToString(format, Loc.Culture), left, bottom + 6, "muted", 10);
        DrawText(drawing, UsagePresentation.KoreanTime(_now).ToString(format, Loc.Culture), right, bottom + 6, "muted", 10, true);
        if (!_simple) DrawTimeline(drawing, left, right, 127);
    }
    private void DrawSegment(DrawingContext drawing, List<Point> points, double bottom)
    {
        if (points.Count == 0) return;
        if (points.Count == 1) { drawing.DrawEllipse(_theme.Color("accent"), null, points[0], 2, 2); return; }
        var area = new StreamGeometry();
        using (var path = area.Open())
        {
            path.BeginFigure(new Point(points[0].X, bottom), true, true);
            path.PolyLineTo(points, true, false); path.LineTo(new Point(points[^1].X, bottom), true, false);
        }
        var fill = _theme.Color("accent").Clone(); fill.Opacity = 0.10; drawing.DrawGeometry(fill, null, area);
        var line = new StreamGeometry();
        using (var path = line.Open()) { path.BeginFigure(points[0], false, false); path.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        drawing.DrawGeometry(null, new Pen(_theme.Color("accent"), 2) { LineJoin = PenLineJoin.Round }, line);
    }
    private void DrawTimeline(DrawingContext drawing, double left, double right, double y)
    {
        var reset = _window.ResetsAt is long time ? DateTimeOffset.FromUnixTimeSeconds(time) : (DateTimeOffset?)null;
        if (reset <= _now) reset = null;
        var exhausted = _forecast.ExhaustsAt > _now ? _forecast.ExhaustsAt : null;
        var end = reset ?? exhausted;
        if (end == null)
        {
            DrawText(drawing, Loc.T("소진 · 리셋 예측 대기"), left, y - 3, "muted", 11); return;
        }
        drawing.DrawLine(new Pen(_theme.Color("divider"), 2), new Point(left, y), new Point(right, y));
        drawing.DrawEllipse(_theme.Color("muted"), null, new Point(left, y), 3, 3);
        DrawText(drawing, Loc.T("지금"), left, y + 9, "muted", 10);
        if (exhausted.HasValue && (reset == null || exhausted < reset))
        {
            var x = left + (right - left) * (exhausted.Value - _now).TotalSeconds / (end.Value - _now).TotalSeconds;
            var projection = new Pen(_theme.Color("warning"), 2) { DashStyle = DashStyles.Dash };
            drawing.DrawLine(projection, new Point(left, y), new Point(x, y));
            drawing.DrawEllipse(_theme.Color("warning"), null, new Point(x, y), 4, 4);
            DrawText(drawing, Loc.T("예상 소진"), Math.Clamp(x, left + 30, right - 30), y - 18, "warning", 11, centered: true);
        }
        else if (_forecast.State == ForecastState.ResetsFirst)
            drawing.DrawLine(new Pen(_theme.Color("success"), 2) { DashStyle = DashStyles.Dash }, new Point(left, y), new Point(right, y));
        if (reset.HasValue)
        {
            drawing.DrawLine(new Pen(_theme.Color("success"), 2), new Point(right, y - 5), new Point(right, y + 5));
            DrawText(drawing, Loc.T("리셋"), right, y + 9, "success", 10, true);
        }
    }
    private void DrawText(DrawingContext drawing, string text, double x, double y, string color, double size, bool right = false, bool centered = false)
    {
        var formatted = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, new Typeface(_theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, _theme.Color(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(formatted, new Point(right ? x - formatted.Width : centered ? x - formatted.Width / 2 : x, y));
    }
    protected override void OnMouseMove(MouseEventArgs arguments)
    {
        base.OnMouseMove(arguments);
        var position = arguments.GetPosition(this);
        if (position.Y < 20 || position.Y > 96 || _hits.Count == 0) { ToolTip = null; return; }
        var nearest = _hits.MinBy(p => Math.Abs(p.Position.X - position.X));
        ToolTip = Loc.F("실측 · {0:MM/dd HH:mm:ss} (한국 시간)\n사용 {1:0.##}%", UsagePresentation.KoreanTime(nearest.Observation.At), nearest.Observation.UsedPercent);
    }
    protected override void OnMouseLeave(MouseEventArgs arguments) { base.OnMouseLeave(arguments); ToolTip = null; }
}
