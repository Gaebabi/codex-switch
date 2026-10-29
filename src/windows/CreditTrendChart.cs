using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class CreditTrendChart : FrameworkElement
{
    private readonly CreditTrack _track;
    private readonly Burn0Theme _theme;
    private readonly int _hours;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public CreditTrendChart(CreditTrack track, Burn0Theme theme, int hours)
    { _track = track; _theme = theme; _hours = hours; Height = 160; MinWidth = 120; ClipToBounds = true; }
    public void Update(DateTimeOffset now)
    { var changed = now.ToUnixTimeSeconds() / 60 != _now.ToUnixTimeSeconds() / 60; _now = now; if (changed) InvalidateVisual(); }
    private Rect Plot => new(58, 28, Math.Max(0, ActualWidth - 66), Math.Max(0, ActualHeight - 56));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Plot.Width <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var plot = Plot; var start = _now.AddHours(-Math.Clamp(_hours, 1, 720));
        var points = CreditHistory.IntervalsForRange(_track, _hours, _now); var maximum = CreditPresentation.Maximum(points);
        Text(Loc.T("credits.chart"), 0, 0, 11); Text(Loc.T("credits.units"), plot.Right, 0, 10, true);
        foreach (var ratio in new[] { 0.0, 0.5, 1.0 })
        {
            var y = plot.Bottom - plot.Height * ratio;
            dc.DrawLine(new Pen(_theme.Color("divider"), 1), new Point(plot.Left, y), new Point(plot.Right, y));
            Text((maximum * ratio).ToString("G4", Loc.Culture), plot.Left - 8, y - 7, 10, true);
        }
        double X(DateTimeOffset at) => plot.Left + Math.Clamp((at - start).TotalSeconds / (_now - start).TotalSeconds, 0, 1) * plot.Width;
        foreach (var point in points.Where(p => p.ObservedDecrease.HasValue && p.Since.HasValue))
        {
            var left = X(point.Since!.Value); var right = X(point.At); var height = plot.Height * Math.Clamp((double)point.ObservedDecrease!.Value / maximum, 0, 1);
            dc.DrawRectangle(_theme.Color("accent"), null, new Rect(left, plot.Bottom - Math.Max(1, height), Math.Max(1, right - left - 1), Math.Max(1, height)));
        }
        if (!points.Any(p => p.ObservedDecrease.HasValue)) Text(Loc.T("credits.empty"), plot.Left + 8, plot.Top + plot.Height / 2 - 10, 11);
        Text(UsagePresentation.KoreanTime(start).ToString(_hours >= 24 ? "MM/dd HH:mm" : "HH:mm", Loc.Culture), plot.Left, plot.Bottom + 8, 10);
        Text(UsagePresentation.KoreanTime(_now).ToString(_hours >= 24 ? "MM/dd HH:mm" : "HH:mm", Loc.Culture), plot.Right, plot.Bottom + 8, 10, true);
        void Text(string text, double x, double y, double size, bool right = false)
        {
            var value = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, new Typeface(_theme.Font.Source), size, _theme.Color("muted"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(value, new Point(right ? x - value.Width : x, y));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var position = e.GetPosition(this); var start = _now.AddHours(-Math.Clamp(_hours, 1, 720));
        var point = Plot.Contains(position) ? CreditHistory.IntervalsForRange(_track, _hours, _now).MinBy(p => Math.Abs(Plot.Left + (p.At - start).TotalSeconds / (_now - start).TotalSeconds * Plot.Width - position.X)) : null;
        ToolTip = point == null ? null : CreditPresentation.Details(point);
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); ToolTip = null; }
}
