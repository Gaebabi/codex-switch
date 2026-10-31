using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal sealed record UsageChartSpan(UsageObservation Start, UsageObservation End, bool Credits);
internal sealed record ChartCreditUse(decimal Used, DateTimeOffset Since, decimal Balance, decimal Estimated = 0, bool EstimatedInterval = false)
{
    public decimal Total => Used + Estimated;
}
internal sealed record UsageChartSeries(UsageObservation[] Points, IReadOnlyDictionary<UsageObservation, ChartCreditUse> Credits,
    DateTimeOffset[]? MissingCreditAmounts = null);

internal static class UsageChartPresentation
{
    private sealed class CreditCycle
    {
        public required DateTimeOffset At;
        public decimal Used, Estimated;
        public CreditObservation? Previous;
    }
    private sealed record DisplayPoint(UsageObservation Point, int SourceSegment, bool BreakBefore = false, ChartCreditUse? Credit = null);

    // A quota can stay exactly100 while the independently observed balance falls.
    // The display coordinate above100 is an offset in actual credit units,
    // never a percentage or conversion of source quota. Source records stay intact.
    public static UsageChartSeries Build(UsageObservation[] history, CreditTrack? credits)
    {
        var quota = history.Where(p => double.IsFinite(p.UsedPercent)).OrderBy(p => p.At).ToArray();
        var empty = new Dictionary<UsageObservation, ChartCreditUse>();
        UsageChartSeries WithoutAmounts() => new(quota.Select(p => p with { UsedPercent = Math.Min(100, p.UsedPercent) }).ToArray(), empty,
            quota.Where(p => p.UsedPercent > 100).Select(p => p.At).ToArray());
        if (credits == null || quota.Length == 0) return WithoutAmounts();
        var cycles = new Dictionary<int, CreditCycle>();
        var estimates = CreditHistory.GapEstimates(credits).ToDictionary(p => p.At);
        var display = new List<DisplayPoint>();
        UsageObservation? At(DateTimeOffset at)
        {
            var low = 0; var high = quota.Length - 1; var found = -1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (quota[middle].At <= at) { found = middle; low = middle + 1; } else high = middle - 1;
            }
            return found >= 0 && at - quota[found].At <= TimeSpan.FromMinutes(20) ? quota[found] : null;
        }
        foreach (var credit in credits.Points.GroupBy(p => p.At).Where(g => g.Count() == 1).Select(g => g.Single()).OrderBy(p => p.At))
        {
            var point = At(credit.At);
            if (point == null || point.UsedPercent < 100) continue;
            if (!cycles.TryGetValue(point.Segment, out var cycle))
            {
                if (credit.Balance == null) continue;
                cycle = new() { At = credit.At };
                cycles.Add(point.Segment, cycle);
            }
            var previous = cycle.Previous;
            var since = credit.Since is DateTimeOffset at ? At(at) : null;
            var known = credit.ObservedDecrease >= 0 && credit.Since >= cycle.At && credit.Since <= credit.At &&
                since?.Segment == point.Segment && since.UsedPercent >= 100;
            if (known)
            {
                try { cycle.Used += credit.ObservedDecrease!.Value; }
                catch (OverflowException) { cycle.Previous = credit; continue; }
            }
            var estimated = estimates.TryGetValue(credit.At, out var gap) && gap.Since >= cycle.At &&
                At(gap.Since) is UsageObservation anchor && anchor.Segment == point.Segment && anchor.UsedPercent >= 100;
            if (estimated)
            {
                try { cycle.Estimated += gap!.Amount; }
                catch (OverflowException) { cycle.Previous = credit; continue; }
            }
            cycle.Previous = credit;
            if (credit.Balance is not decimal balance) continue;
            decimal total;
            try { total = cycle.Used + cycle.Estimated; }
            catch (OverflowException) { continue; }
            var info = new ChartCreditUse(cycle.Used, cycle.At, balance, cycle.Estimated, estimated);
            var observation = point with { At = credit.At, UsedPercent = 100 + (double)total };
            var discontinuity = !estimated && previous != null && (!known || previous.Segment != credit.Segment || credit.Since > previous.At || previous.Balance == null);
            display.Add(new(observation, point.Segment, discontinuity, info));
        }
        if (display.Count == 0) return WithoutAmounts();
        display.AddRange(quota.Where(p => !cycles.TryGetValue(p.Segment, out var cycle) || p.At < cycle.At)
            .Select(p => new DisplayPoint(p with { UsedPercent = Math.Min(100, p.UsedPercent) }, p.Segment)));
        var points = new List<UsageObservation>(); var metadata = new Dictionary<UsageObservation, ChartCreditUse>();
        DisplayPoint? last = null; var segment = 0;
        foreach (var item in display.OrderBy(p => p.Point.At))
        {
            if (last != null && (item.SourceSegment != last.SourceSegment || item.BreakBefore)) segment++;
            var point = item.Point with { Segment = segment }; points.Add(point);
            if (item.Credit != null) metadata[point] = item.Credit;
            last = item;
        }
        return new(points.ToArray(), metadata, quota.Where(p => p.UsedPercent > 100 && !cycles.ContainsKey(p.Segment)).Select(p => p.At).ToArray());
    }

    public static double Maximum(IEnumerable<UsageObservation> points)
    {
        var largest = points.Where(p => double.IsFinite(p.UsedPercent)).Select(p => p.UsedPercent).DefaultIfEmpty(100).Max();
        if (largest <= 100) return 100;
        var excess = largest - 100;
        var step = Math.Pow(10, Math.Floor(Math.Log10(excess)));
        var rounded = 100 + Math.Ceiling(excess * 1.1 / step) * step;
        return double.IsFinite(rounded) ? rounded : largest;
    }

    // Quota percentages and credit quantities use separate labeled scales.
    public static double Fraction(double value, double maximum) => maximum <= 100 ? Math.Max(0, value) / 100 :
        value <= 100 ? Math.Max(0, value) / 100 * 0.62 : 0.62 + (value - 100) / (maximum - 100) * 0.38;
    public static string Legend(UsageObservation? last, UsageChartSeries series) => last != null && series.Credits.TryGetValue(last, out var credit)
        ? credit.Estimated > 0 ? Loc.F("history.credit_amount_estimated_legend", CreditPresentation.Number(credit.Total), CreditPresentation.Number(credit.Estimated))
            : Loc.F("history.credit_amount_legend", CreditPresentation.Number(credit.Used)) : Loc.T("history.credit_range");

    public static IEnumerable<UsageChartSpan> Split(UsageObservation start, UsageObservation end)
    {
        if (!double.IsFinite(start.UsedPercent) || !double.IsFinite(end.UsedPercent)) yield break;
        if (start.UsedPercent < 100 && end.UsedPercent > 100 || start.UsedPercent > 100 && end.UsedPercent < 100)
        {
            var ratio = (100 - start.UsedPercent) / (end.UsedPercent - start.UsedPercent);
            var boundary = start with { At = start.At.AddTicks((long)((end.At - start.At).Ticks * ratio)), UsedPercent = 100 };
            yield return new(start, boundary, start.UsedPercent > 100);
            yield return new(boundary, end, end.UsedPercent > 100);
        }
        else yield return new(start, end, start.UsedPercent > 100 || end.UsedPercent > 100);
    }

    public static string Tick(double value) => value > 100
        ? Loc.F("history.credit_amount_tick", (value - 100).ToString(value - 100 is < 0.0001 or > 1000000 ? "G4" : "0.####", Loc.Culture))
        : value.ToString("0.#", Loc.Culture) + "%";
    public static string Details(UsageObservation point, UsageChartSeries series) => series.Credits.TryGetValue(point, out var credit)
        ? credit.Estimated > 0 ? Loc.F("history.credit_amount_estimated_reading", UsagePresentation.KoreanTime(point.At), CreditPresentation.Number(credit.Total),
            CreditPresentation.Number(credit.Used), CreditPresentation.Number(credit.Estimated), UsagePresentation.KoreanTime(credit.Since))
            : Loc.F("history.credit_amount_reading", UsagePresentation.KoreanTime(point.At), CreditPresentation.Number(credit.Used),
                CreditPresentation.Number(credit.Balance), UsagePresentation.KoreanTime(credit.Since)) : Details(point);
    public static string Details(UsageObservation point) => point.UsedPercent > 100
        ? Loc.T("history.credit_amount_unavailable")
        : Loc.F("실측 · {0:MM/dd HH:mm:ss} (한국 시간)\n사용 {1:0.##}%", UsagePresentation.KoreanTime(point.At), point.UsedPercent);
}
