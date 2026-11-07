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
    public HistoryRange? Range { get; set; }
    public CreditTrack? Credits { get; set; }
    private UsageChartSeries _series = new(Array.Empty<UsageObservation>(), new Dictionary<UsageObservation, ChartCreditUse>());
    public UsageTrendChart(UsageWindow window, UsageObservation[] history, Burn0Theme theme, int hours, DateTimeOffset now, bool simple = false)
    {
        _window = window; _history = history; _theme = theme; _hours = hours; _now = now;
        _forecast = UsagePredictor.Evaluate(window, history, now); _simple = simple;
        Height = simple ? 156 : 180; MinWidth = 120; ClipToBounds = true;
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
        var left = 88.0; var right = ActualWidth - 8; var top = 38.0; var bottom = 127.0;
        if (right <= left) return;
        var range = HistoryRange.Resolve(_hours, Range, _now); var start = range.Start; var end = range.End;
        _series = UsageChartPresentation.Build(_history, Credits);
        var visible = _series.Points.Where(p => range.Contains(p.At)).ToArray();
        var maximum = UsageChartPresentation.Maximum(visible);
        if (maximum > 100)
        {
            var fill = _theme.Color("credit").Clone(); fill.Opacity = 0.08;
            drawing.DrawRectangle(fill, null, new Rect(left, top, right - left, (bottom - top) * (1 - UsageChartPresentation.Fraction(100, maximum))));
            DrawText(drawing, UsageChartPresentation.Legend(visible.LastOrDefault(), _series), right, 0, "credit", 11, true);
            if (visible.Any(p => _series.Credits.ContainsKey(p))) DrawText(drawing, Loc.T(visible.Any(p => _series.Credits.TryGetValue(p, out var c) && c.Estimated > 0) ? "history.credit_estimate_help" : "history.credit_amount_help"), right, 17, "muted", 10, true);
        }
        else if (_series.MissingCreditAmounts?.Any(range.Contains) == true) DrawText(drawing, Loc.T("history.credit_amount_unavailable"), right, 0, "credit", 11, true);
        if (!_simple) { DrawText(drawing, Loc.T("사용량 추이"), 0, 0, "muted", 11); if (maximum <= 100) DrawText(drawing, Loc.T("실측"), right, 0, "accent", 11, true); }
        var ticks = (_simple ? new[] { 0.0, 100 } : new[] { 0.0, 50, 100 }).AsEnumerable();
        if (maximum > 100) ticks = ticks.Concat(new[] { 100 + (maximum - 100) / 2, maximum });
        foreach (var value in ticks.Distinct())
        {
            var y = bottom - (bottom - top) * UsageChartPresentation.Fraction(value, maximum);
            drawing.DrawLine(new Pen(_theme.Color("divider"), 1), new Point(left, y), new Point(right, y));
            DrawText(drawing, UsageChartPresentation.Tick(value), left - 7, y - 7, value > 100 ? "credit" : "muted", 10, true);
        }
        var seconds = Math.Max(1, (end - start).TotalSeconds);
        Point Position(UsageObservation point) => new(left + (right - left) * (point.At - start).TotalSeconds / seconds,
            bottom - (bottom - top) * UsageChartPresentation.Fraction(point.UsedPercent, maximum));
        UsageObservation? previous = null;
        foreach (var observation in visible)
        {
            if (previous != null && previous.Segment == observation.Segment && (observation.At - previous.At <= TimeSpan.FromMinutes(30) ||
                _series.Credits.TryGetValue(observation, out var estimate) && estimate.EstimatedInterval))
                foreach (var span in UsageChartPresentation.Split(previous, observation))
                {
                    var pen = new Pen(_theme.Color(span.Credits ? "credit" : "accent"), 2);
                    if (_series.Credits.TryGetValue(observation, out var credit) && credit.EstimatedInterval) pen.DashStyle = DashStyles.Dash;
                    drawing.DrawLine(pen, Position(span.Start), Position(span.End));
                }
            else drawing.DrawEllipse(_theme.Color(observation.UsedPercent > 100 ? "credit" : "accent"), null, Position(observation), 2, 2);
            _hits.Add((Position(observation), observation)); previous = observation;
        }
        if (visible.Length < 2)
            DrawText(drawing, Loc.T("조회 기록이 쌓이면 추이를 볼 수 있습니다."), (left + right) / 2, 53, "muted", 11, centered: true);
        if (visible.Length > 0)
        {
            var last = Position(visible[^1]);
            drawing.DrawEllipse(_theme.Color(visible[^1].UsedPercent > 100 ? "credit" : "accent"), new Pen(_theme.Color("surface"), 2), last, 4, 4);
        }
        var format = end - start >= TimeSpan.FromHours(12) ? "MM/dd HH:mm" : "HH:mm";
        DrawText(drawing, UsagePresentation.KoreanTime(start).ToString(format, Loc.Culture), left, bottom + 6, "muted", 10);
        DrawText(drawing, UsagePresentation.KoreanTime(end).ToString(format, Loc.Culture), right, bottom + 6, "muted", 10, true);
        if (!_simple) DrawTimeline(drawing, left, right, 159);
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
        if (position.Y < 35 || position.Y > 132 || _hits.Count == 0) { ToolTip = null; return; }
        var nearest = _hits.MinBy(p => Math.Abs(p.Position.X - position.X));
        ToolTip = UsageChartPresentation.Details(nearest.Observation, _series);
    }
    protected override void OnMouseLeave(MouseEventArgs arguments) { base.OnMouseLeave(arguments); ToolTip = null; }
}
