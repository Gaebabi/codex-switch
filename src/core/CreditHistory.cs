using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodexAccountDesk;

// Observed decreases are not a billing ledger: simultaneous grants, expiry or
// adjustments can affect a balance. Unknown intervals never invent consumption.
internal sealed record CreditObservation(DateTimeOffset At, decimal? Balance, decimal? ObservedDecrease, DateTimeOffset? Since, int Segment);
internal sealed record CreditGapEstimate(DateTimeOffset Since, DateTimeOffset At, decimal Amount);
internal sealed record RecoveredCreditReading(string BucketId, DateTimeOffset At, string Balance, string SourceSha256);

internal sealed class CreditTrack
{
    public string BucketId { get; set; } = "";
    public List<CreditObservation> Points { get; set; } = new();
    public CreditTrack Copy() => new() { BucketId = BucketId, Points = Points.ToList() };
}

internal static class CreditHistory
{
    // Bounded balance differences only: no extrapolation before/after records.
    // Known consumption in a partially observed interval is subtracted once.
    public static CreditGapEstimate[] GapEstimates(CreditTrack track)
    {
        var estimates = new List<CreditGapEstimate>(); CreditObservation? previous = null;
        foreach (var point in track.Points.GroupBy(p => p.At).Where(g => g.Count() == 1).Select(g => g.Single()).OrderBy(p => p.At))
        {
            if (point.Balance is not decimal balance) continue;
            if (previous?.Balance is decimal prior && point.At > previous.At)
            {
                var fullyObserved = point.ObservedDecrease >= 0 && point.Since <= previous.At && point.Since <= point.At;
                if (!fullyObserved)
                {
                    try
                    {
                        var known = point.ObservedDecrease >= 0 && point.Since >= previous.At && point.Since <= point.At ? point.ObservedDecrease.Value : 0;
                        var amount = prior - balance - known;
                        if (amount > 0) estimates.Add(new(previous.At, point.At, amount));
                    }
                    catch (OverflowException) { /* An unrepresentable difference remains unavailable. */ }
                }
            }
            previous = point;
        }
        return estimates.ToArray();
    }
    public static decimal? EstimatedUse(CreditTrack track, HistoryRange range)
    {
        try { return EstimatesForRange(track, range).Sum(p => p.Amount); }
        catch (OverflowException) { return null; }
    }
    public static CreditGapEstimate[] EstimatesForRange(CreditTrack track, HistoryRange range) =>
        GapEstimates(track).Where(p => p.Since >= range.Start && range.Contains(p.At)).ToArray();
    public const int MaximumPoints = 8192;
    public static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(5);
    public static decimal? Balance(UsageBucket bucket) => bucket.UnlimitedCredits == true ? null :
        decimal.TryParse(bucket.CreditBalance, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    public static void Record(AccountProfile profile, UsageSnapshot snapshot)
    {
        if (snapshot.CheckedAt == DateTimeOffset.MinValue) return;
        var unique = snapshot.Buckets.Where(b => !string.IsNullOrWhiteSpace(b.Id)).GroupBy(b => b.Id, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        foreach (var bucket in unique.Values.Where(b => b.CreditBalance != null || b.UnlimitedCredits.HasValue))
            if (!profile.CreditHistory.Any(t => t.BucketId == bucket.Id)) profile.CreditHistory.Add(new() { BucketId = bucket.Id });
        foreach (var track in profile.CreditHistory)
        {
            var last = track.Points.LastOrDefault();
            if (last != null && snapshot.CheckedAt <= last.At) continue;
            var balance = unique.TryGetValue(track.BucketId, out var bucket) ? Balance(bucket) : null;
            // One unknown marker breaks continuity. Repeated absence must not
            // keep retired meter IDs alive forever or fill storage with nulls.
            if (last != null && last.Balance == null && balance == null) { Compact(track, snapshot.CheckedAt); continue; }
            var segment = last?.Segment ?? 0;
            decimal? decrease = null; DateTimeOffset? since = null;
            if (balance.HasValue && last?.Balance is decimal previous && snapshot.CheckedAt - last.At <= MaximumGap)
            {
                try { decrease = previous > balance.Value ? previous - balance.Value : 0; since = last.At; }
                catch (OverflowException) { segment++; }
            }
            else if (last != null) segment++;
            track.Points.Add(new(snapshot.CheckedAt, balance, decrease, since, segment));
            Compact(track, snapshot.CheckedAt);
        }
        profile.CreditHistory.RemoveAll(t => t.Points.Count == 0);
    }

    private static void Compact(CreditTrack track, DateTimeOffset now)
    {
        var points = new List<CreditObservation>();
        long previousBucket = long.MinValue;
        foreach (var point in track.Points.Where(p => p.At >= now.AddDays(-30)))
        {
            var seconds = point.At >= now.AddDays(-2) ? 60 : 900;
            var bucket = point.At.ToUnixTimeSeconds() / seconds;
            var previous = points.LastOrDefault();
            if (previous != null && previousBucket == bucket && previous.Segment == point.Segment)
            {
                decimal? sum = null;
                try { if (previous.ObservedDecrease.HasValue || point.ObservedDecrease.HasValue) sum = (previous.ObservedDecrease ?? 0) + (point.ObservedDecrease ?? 0); }
                catch (OverflowException) { /* An unrepresentable observed total stays explicitly unknown. */ }
                points[^1] = point with { ObservedDecrease = sum, Since = sum.HasValue ? previous.Since ?? point.Since : null };
            }
            else points.Add(point);
            previousBucket = bucket;
        }
        track.Points = points.TakeLast(MaximumPoints).ToList();
    }

    public static CreditObservation[] ForRange(CreditTrack track, int hours, DateTimeOffset now) =>
        track.Points.Where(p => p.At >= now.AddHours(-Math.Clamp(hours, 1, 720)) && p.At <= now).ToArray();

    public static decimal? ObservedUse(CreditTrack track, int hours, DateTimeOffset now)
        => ObservedUse(track, HistoryRange.Resolve(hours, null, now));

    public static decimal? ObservedUse(CreditTrack track, HistoryRange range)
    {
        var points = IntervalsForRange(track, range);
        if (points.Length == 0) return null;
        try { return points.Sum(p => p.ObservedDecrease!.Value); }
        catch (OverflowException) { return null; }
    }
    public static CreditObservation[] IntervalsForRange(CreditTrack track, int hours, DateTimeOffset now)
        => IntervalsForRange(track, HistoryRange.Resolve(hours, null, now));

    public static CreditObservation[] IntervalsForRange(CreditTrack track, HistoryRange range) =>
        track.Points.Where(p => range.Contains(p.At) && p.Since >= range.Start &&
            p.Since <= p.At && p.ObservedDecrease >= 0).ToArray();
}
