using CodexAccountDesk;

internal static class HistoryRangeContracts
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero);
        var from = new DateTime(2026, 10, 2); var through = new DateTime(2026, 10, 4);
        check("30-day preset includes observations beyond seven days", HistoryRange.Resolve(720, null, now).Contains(now.AddDays(-20)));
        check("Custom calendar dates include the full last Korean day", HistoryRange.TryDates(from, through, now, out var range) &&
            range!.Start == new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero) &&
            range.End == new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero) && range.Contains(range.End.AddTicks(-1)) && !range.Contains(range.End));
        check("Custom historical bounds remain fixed as the clock advances", HistoryRange.Resolve(24, range, now.AddDays(1)) == range);
        check("Rolling history includes the current observation", HistoryRange.Resolve(24, null, now).Contains(now));
        check("Today's custom range ends at the latest observation", HistoryRange.TryDates(now.Date, now.Date, now, out var today) && HistoryRange.Resolve(24, today, now).Contains(now));
        foreach (var dates in new[] { ((DateTime?)null, (DateTime?)through), ((DateTime?)through, (DateTime?)from), ((DateTime?)from, (DateTime?)now.AddDays(1).Date) })
            check("Missing, reversed or future custom dates are rejected: " + dates, !HistoryRange.TryDates(dates.Item1, dates.Item2, now, out _));
        var track = new CreditTrack { Points = new()
        {
            new(range!.Start, 99, 1, range.Start.AddMinutes(-1), 0),
            new(range.Start.AddMinutes(1), 97, 2, range.Start, 0),
            new(range.End, 94, 3, range.End.AddMinutes(-1), 0)
        } };
        check("Custom credit totals exclude boundary-spanning and next-day intervals", CreditHistory.ObservedUse(track, range) == 2);
        var start = new UsageObservation(now.AddMinutes(-2), 90, null, 0);
        var end = new UsageObservation(now, 130, null, 0);
        var spans = UsageChartPresentation.Split(start, end).ToArray();
        check("Quota crossing joins exactly at 100 before continuing in credits", spans.Length == 2 && !spans[0].Credits && spans[1].Credits &&
            spans[0].End == spans[1].Start && spans[1].Start.UsedPercent == 100 && spans[1].End.UsedPercent == 130 && spans[1].Start.At == now.AddSeconds(-90));
        check("Credit scale keeps observations above 100 visible", UsageChartPresentation.Maximum(new[] { start, end }) >= 130 && UsageChartPresentation.Maximum(new[] { start }) == 100);
        check("Credit coloring starts at the exact quota boundary", UsageChartPresentation.Split(start with { UsedPercent = 100 }, end).Single().Credits);
        check("Reset-like descending crossings retain the correct colors", UsageChartPresentation.Split(end, start).Select(s => s.Credits).SequenceEqual(new[] { true, false }));
        check("Unavailable numeric readings never create chart spans", !UsageChartPresentation.Split(start with { UsedPercent = double.NaN }, end).Any());
        var profile = new AccountProfile();
        UsageHistory.Record(profile, new() { CheckedAt = now, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 130, Minutes = 300 } } } });
        check("History retains the actual above-quota observation", profile.History.Single().Points.Single().UsedPercent == 130);
    }
}
