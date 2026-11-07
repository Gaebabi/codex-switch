using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CodexAccountDesk;

internal sealed class CreditChart : Control
{
    public required CreditTrack Track { get; set; }
    public required DeskTheme Palette { get; set; }
    public int Hours { get; set; } = 24;
    public HistoryRange? Range { get; set; }
    private HistoryRange VisibleRange => HistoryRange.Resolve(Hours, Range, _now);
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private Rect Plot => new(58, 30, Math.Max(0, Bounds.Width - 66), Math.Max(0, Bounds.Height - 58));
    public void Update(DateTimeOffset now)
    { var changed = now.ToUnixTimeSeconds() / 60 != _now.ToUnixTimeSeconds() / 60; _now = now; if (changed) InvalidateVisual(); }
    public override void Render(DrawingContext dc)
    {
        if (Plot.Width <= 0) return;
        var range = VisibleRange; var points = CreditHistory.IntervalsForRange(Track, range); var estimates = CreditHistory.EstimatesForRange(Track, range);
        var maximum = CreditPresentation.Maximum(points, estimates);
        var plot = Plot; var start = range.Start; var end = range.End;
        AutomationProperties.SetName(this, Loc.T("credits.chart"));
        AutomationProperties.SetHelpText(this, points.Length == 0 ? Loc.T("credits.empty") : CreditPresentation.Details(points[^1]));
        Text(Loc.T(estimates.Length > 0 ? "credits.chart_estimates" : "credits.chart"), 0, 0, 12); Text(Loc.T("credits.units"), plot.Right, 0, 11, true);
        foreach (var ratio in new[] { 0.0, 0.5, 1.0 })
        {
            var y = plot.Bottom - plot.Height * ratio;
            dc.DrawLine(new Pen(Palette.Brush("divider"), 1), new Point(plot.Left, y), new Point(plot.Right, y));
            Text((maximum * ratio).ToString("G4", Loc.Culture), plot.Left - 8, y - 7, 11, true);
        }
        double X(DateTimeOffset at) => plot.Left + Math.Clamp((at - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds), 0, 1) * plot.Width;
        foreach (var point in points.Where(p => p.ObservedDecrease.HasValue && p.Since.HasValue))
        {
            var left = X(point.Since!.Value); var right = X(point.At); var height = plot.Height * Math.Clamp((double)point.ObservedDecrease!.Value / maximum, 0, 1);
            dc.DrawRectangle(Palette.Brush("accent"), null, new Rect(left, plot.Bottom - Math.Max(1, height), Math.Max(1, right - left - 1), Math.Max(1, height)));
        }
        foreach (var estimate in estimates)
        {
            var x = X(estimate.Since); var width = Math.Max(1, X(estimate.At) - x - 1);
            var height = Math.Max(1, plot.Height * (double)estimate.Amount / maximum);
            var rect = new Rect(x, plot.Bottom - height, width, height);
            using (dc.PushOpacity(0.10)) dc.DrawRectangle(Palette.Brush("credit"), null, rect);
            dc.DrawRectangle(null, new Pen(Palette.Brush("credit"), 1.5) { DashStyle = DashStyle.Dash }, rect);
        }
        if (!points.Any(p => p.ObservedDecrease.HasValue) && estimates.Length == 0) Text(Loc.T("credits.empty"), plot.Left + 8, plot.Top + plot.Height / 2 - 10, 11);
        Text(UsagePresentation.KoreanTime(start).ToString(Hours >= 24 ? "MM/dd HH:mm" : "HH:mm", Loc.Culture), plot.Left, plot.Bottom + 8, 11);
        Text(UsagePresentation.KoreanTime(end).ToString(Hours >= 24 || Range != null ? "MM/dd HH:mm" : "HH:mm", Loc.Culture), plot.Right, plot.Bottom + 8, 11, true);
        void Text(string text, double x, double y, double size, bool right = false)
        {
            var value = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, new Typeface(Palette.Appearance.FontFamily), size, Palette.Brush("muted"));
            dc.DrawText(value, new Point(right ? x - value.Width : x, y));
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var position = e.GetPosition(this); var range = VisibleRange;
        var point = Plot.Contains(position) ? CreditHistory.IntervalsForRange(Track, range).MinBy(p => Math.Abs(Plot.Left + (p.At - range.Start).TotalSeconds / Math.Max(1, (range.End - range.Start).TotalSeconds) * Plot.Width - position.X)) : null;
        var at = range.Start.AddTicks((long)((range.End - range.Start).Ticks * Math.Clamp((position.X - Plot.Left) / Math.Max(1, Plot.Width), 0, 1)));
        var estimate = Plot.Contains(position) ? CreditHistory.EstimatesForRange(Track, range).FirstOrDefault(p => p.Since <= at && p.At >= at) : null;
        ToolTip.SetTip(this, estimate != null ? CreditPresentation.EstimateDetails(estimate) : point == null ? null : CreditPresentation.Details(point));
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); ToolTip.SetTip(this, null); }
}
