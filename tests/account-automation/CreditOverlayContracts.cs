using CodexAccountDesk;
using System.Text.Json;

internal static class CreditOverlayContracts
{
    public static void Run(Action<string, bool> check)
    {
        var at = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        UsageObservation[] Quota(params double[] values) => values.Select((v, i) => new UsageObservation(at.AddMinutes(i), v, null, 0)).ToArray();
        CreditTrack Balances(params decimal?[] values) => new() { BucketId = "codex", Points = values.Select((v, i) =>
            new CreditObservation(at.AddMinutes(i), v, i == 0 || !v.HasValue || !values[i - 1].HasValue ? null : Math.Max(0, values[i - 1]!.Value - v.Value),
                i == 0 || !v.HasValue || !values[i - 1].HasValue ? null : at.AddMinutes(i - 1), i > 1 && !values[i - 1].HasValue ? 1 : 0)).ToList() };
        var raw = Quota(100, 100, 100); var track = Balances(1000, 999, 998.5m);
        var before = JsonSerializer.Serialize(new { raw, track });
        var series = UsageChartPresentation.Build(raw, track);
        check("Capped quota continues in actual credit units without a percentage conversion", series.Points[^1].UsedPercent == 101.5);
        var detail = series.Credits[series.Points[^1]];
        check("Credit overflow retains exact consumption and its observation start", detail.Used == 1.5m && detail.Since == at && detail.Balance == 998.5m);
        check("Credit rendering never rewrites quota, balances, history or forecast inputs", JsonSerializer.Serialize(new { raw, track }) == before && raw.All(p => p.UsedPercent == 100));
        check("Credit-less histories retain their original observations", UsageChartPresentation.Build(raw, null).Points.SequenceEqual(raw));
        var ordinary = UsageChartPresentation.Build(Quota(70, 80, 90), track);
        check("Balance changes before ordinary exhaustion do not become quota overflow", ordinary.Credits.Count == 0 && ordinary.Points.Max(p => p.UsedPercent) == 90);
        var toppedUp = UsageChartPresentation.Build(Quota(100, 100, 100, 100), Balances(100, 90, 120, 110));
        check("Top-ups do not subtract already observed credit use", toppedUp.Credits[toppedUp.Points[^1]].Used == 20 && toppedUp.Points[^1].UsedPercent == 120);
        var unknown = UsageChartPresentation.Build(Quota(100, 100, 100, 100, 100), Balances(100, 90, null, 80, 79));
        check("Missing intervals are estimated separately from observed consumption", unknown.Credits[unknown.Points[^1]].Used == 11 && unknown.Credits[unknown.Points[^1]].Estimated == 10 && unknown.Credits[unknown.Points[2]].EstimatedInterval);
        var resetQuota = Quota(100, 100, 20, 100, 100);
        for (var i = 2; i < resetQuota.Length; i++) resetQuota[i] = resetQuota[i] with { Segment = 1 };
        var reset = UsageChartPresentation.Build(resetQuota, Balances(100, 90, 80, 80, 76));
        check("A newly exhausted reset cycle starts its credit consumption at zero", reset.Credits[reset.Points[^1]].Since == at.AddMinutes(3) && reset.Credits[reset.Points[^1]].Used == 4 && reset.Points[^1].UsedPercent == 104 && reset.Points[^1].Segment != reset.Points[0].Segment);
        var stale = Balances(100, 90); stale.Points[1] = stale.Points[1] with { At = at.AddHours(1) };
        check("Stale quota cannot attribute later credit spending", UsageChartPresentation.Build(Quota(100), stale).Points.Max(p => p.UsedPercent) == 100);
        var zero = UsageChartPresentation.Build(raw, Balances(0, -1, -2));
        check("Actual credit amounts need no positive percentage denominator", zero.Credits[zero.Points[^1]].Used == 2 && zero.Points[^1].UsedPercent == 102);
        var negative = UsageChartPresentation.Build(Quota(100, 100), Balances(100, -10));
        check("Actual consumption retains reported negative balances", negative.Points[^1].UsedPercent == 210 && negative.Credits[negative.Points[^1]].Balance == -10);
        var direct = Quota(100, 130, 150);
        var measured = UsageChartPresentation.Build(direct, Balances(100, 99.5m, 98.5m));
        check("Reported quota percentages cannot fabricate or duplicate credit quantities", measured.Points[^1].UsedPercent == 101.5 && direct[^1].UsedPercent == 150);
        var unavailable = UsageChartPresentation.Build(direct, null);
        check("Quota overage without balance records reports unavailable credit amounts", unavailable.Points.Max(p => p.UsedPercent) == 100 && unavailable.MissingCreditAmounts?.Length == 2);
        var range = new HistoryRange(at.AddMinutes(1), at.AddMinutes(3));
        check("Range filtering retains the same accumulated credit amount", series.Points.Where(p => range.Contains(p.At)).Last().UsedPercent == series.Points[^1].UsedPercent);
        check("Small real credit amounts occupy a visible separately scaled band", UsageChartPresentation.Fraction(100.0005, 100.001) - UsageChartPresentation.Fraction(100, 100.001) > 0.18);
        var profile = new AccountProfile { CreditHistory = new() { track, track.Copy() } };
        check("Ambiguous matching credit tracks cannot be merged into consumption", CreditPresentation.ForQuota(profile, "codex") == null);
        check("Upper axis, legend and tooltip use credit quantities instead of percent", !UsageChartPresentation.Tick(101.5).Contains('%') &&
            !UsageChartPresentation.Legend(series.Points[^1], series).Contains('%') && !UsageChartPresentation.Details(series.Points[^1], series).Contains('%') && UsageChartPresentation.Tick(100).Contains('%'));
        check("Fully observed intervals are not estimated again", CreditHistory.GapEstimates(track).Length == 0);
        var gaps = Balances(100, null, 90, 88);
        var estimates = CreditHistory.GapEstimates(gaps);
        check("Gap estimates require recorded numeric balances at both endpoints", estimates.Length == 1 && estimates[0].Since == at && estimates[0].At == at.AddMinutes(2) && estimates[0].Amount == 10);
        check("No pre-history, trailing unknown or net top-up usage is extrapolated", CreditHistory.GapEstimates(Balances(null, 100, null)).Length == 0 && CreditHistory.GapEstimates(Balances(100, null, 120)).Length == 0);
        var partial = Balances(100, 85); partial.Points[1] = partial.Points[1] with { ObservedDecrease = 5, Since = at.AddSeconds(30) };
        check("Partially observed gap totals subtract already counted consumption", CreditHistory.GapEstimates(partial).Single().Amount == 10);
        check("Custom ranges cannot claim an estimate crossing their start", CreditHistory.EstimatedUse(gaps, new(at.AddMinutes(1), at.AddMinutes(4))) == 0 && CreditHistory.EstimatedUse(gaps, new(at, at.AddMinutes(4))) == 10);
        var gapBefore = JsonSerializer.Serialize(gaps); CreditHistory.GapEstimates(gaps); UsageChartPresentation.Build(Quota(100, 100, 100, 100), gaps);
        check("Gap backfill is a derived layer and leaves every original observation intact", JsonSerializer.Serialize(gaps) == gapBefore);
        var recoveredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var restored = new AccountProfile { Key = "recovery-fixture", CreditHistory = new() { new() { BucketId = "codex", Points = new() { new(recoveredAt.AddHours(1), 90, null, null, 0) } } },
            RecoveredCreditHistory = new() { new("codex", recoveredAt, "100", new string('a', 64)) } };
        var original = JsonSerializer.Serialize(restored.CreditHistory);
        var merged = CreditPresentation.ForQuota(restored, "codex")!;
        check("Recovered snapshot balances supplement a copy without replacing original records", merged.Points.Count == 2 && JsonSerializer.Serialize(restored.CreditHistory) == original && CreditHistory.GapEstimates(merged).Single().Amount == 10);
        var roundtrip = JsonSerializer.Deserialize<AccountProfile>(JsonSerializer.Serialize(restored))!;
        check("Recovered balances retain provenance through the existing account JSON format", roundtrip.Key == restored.Key && roundtrip.RecoveredCreditHistory.SequenceEqual(restored.RecoveredCreditHistory));
        restored.RecoveredCreditHistory.Add(new("codex", recoveredAt.AddHours(1), "999", new string('b', 64)));
        check("Original observations win over recovered data at the same timestamp", CreditPresentation.Track(restored, null).Points.Last().Balance == 90);
        restored.RecoveredCreditHistory.Add(new("codex", recoveredAt, "110", new string('c', 64)));
        check("Conflicting recovered balance anchors stay unavailable instead of being merged", CreditPresentation.ForQuota(restored, "codex")!.Points.Count == 1);
    }
}
