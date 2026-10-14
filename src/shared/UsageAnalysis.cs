using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal sealed record UsageObservation(DateTimeOffset At, double UsedPercent, long? ResetsAt, int Segment);

internal sealed class UsageTrack
{
    public string BucketId { get; set; } = "";
    public string Slot { get; set; } = "";
    public long? Minutes { get; set; }
    public List<UsageObservation> Points { get; set; } = new();
    public UsageTrack Copy() => new() { BucketId = BucketId, Slot = Slot, Minutes = Minutes, Points = Points.ToList() };
}

internal static class UsageHistory
{
    public static void Record(AccountProfile profile, UsageSnapshot snapshot)
    {
        foreach (var bucket in snapshot.Buckets)
        {
            RecordWindow(bucket.Id, "primary", bucket.Primary);
            RecordWindow(bucket.Id, "secondary", bucket.Secondary);
        }
        foreach (var track in profile.History) { NormalizeSegments(track); Compact(track, snapshot.CheckedAt); }
        profile.History.RemoveAll(t => t.Points.Count == 0);

        void RecordWindow(string bucketId, string slot, UsageWindow? window)
        {
            if (window == null) return;
            var track = profile.History.Find(t => t.BucketId == bucketId && t.Slot == slot && t.Minutes == window.Minutes);
            if (track == null)
            {
                track = new UsageTrack { BucketId = bucketId, Slot = slot, Minutes = window.Minutes };
                profile.History.Add(track);
            }
            var last = track.Points.LastOrDefault();
            if (last != null && snapshot.CheckedAt <= last.At) return;
            var segment = last?.Segment ?? 0;
            if (last != null && StartsNewSegment(last, snapshot.CheckedAt, window.UsedPercent, window.ResetsAt)) segment++;
            track.Points.Add(new UsageObservation(snapshot.CheckedAt, window.UsedPercent, window.ResetsAt, segment));
        }
    }
    private static bool StartsNewSegment(UsageObservation previous, DateTimeOffset at, double used, long? resetsAt) =>
        used < previous.UsedPercent || previous.ResetsAt is long deadline && at.ToUnixTimeSeconds() >= deadline && resetsAt != previous.ResetsAt;
    private static void NormalizeSegments(UsageTrack track)
    {
        var segment = 0;
        for (var index = 0; index < track.Points.Count; index++)
        {
            var point = track.Points[index];
            if (index > 0 && StartsNewSegment(track.Points[index - 1], point.At, point.UsedPercent, point.ResetsAt)) segment++;
            if (point.Segment != segment) track.Points[index] = point with { Segment = segment };
        }
    }
    private static void Compact(UsageTrack track, DateTimeOffset now)
    {
        var retained = new List<UsageObservation>();
        long previousBucket = long.MinValue;
        foreach (var point in track.Points.Where(p => p.At >= now.AddDays(-30)))
        {
            var resolutionSeconds = point.At >= now.AddDays(-2) ? 60 : 900;
            var bucket = point.At.ToUnixTimeSeconds() / resolutionSeconds;
            var last = retained.LastOrDefault();
            if (last != null && previousBucket == bucket && last.Segment == point.Segment)
                retained[^1] = point;
            else retained.Add(point);
            previousBucket = bucket;
        }
        track.Points = retained;
    }
    public static UsageObservation[] ForWindow(AccountProfile profile, string bucketId, string slot, UsageWindow window) =>
        profile.History.Find(t => t.BucketId == bucketId && t.Slot == slot && t.Minutes == window.Minutes)?.Points.ToArray() ?? Array.Empty<UsageObservation>();
}

internal enum ForecastState { Collecting, InsufficientChange, NoGrowth, Stale, ResetElapsed, Exhausted, DepletesFirst, ResetsFirst, Projected }

internal sealed record UsageForecast(ForecastState State, DateTimeOffset? ExhaustsAt = null, double? RatePerHour = null,
    TimeSpan ObservedSpan = default, int SampleCount = 0, double? UsedAtReset = null, int ActivityBlocks = 0)
{
    public bool IsEstimate => RatePerHour.HasValue;
    public bool IsRisk => State is ForecastState.DepletesFirst or ForecastState.Exhausted;
}

internal sealed record UsageTrendModel(ForecastState State, DateTimeOffset? LastAt = null, double? RatePerHour = null,
    TimeSpan ObservedSpan = default, int SampleCount = 0, int ActivityBlocks = 0);

internal static class UsagePredictor
{
    private static readonly long BlockTicks = TimeSpan.FromMinutes(10).Ticks;
    private static readonly long MaximumGapTicks = TimeSpan.FromMinutes(5).Ticks;
    private static readonly double Alpha = 1 - Math.Pow(0.5, 1.0 / 4);
    private sealed class Block { public long Coverage; public double Growth; }

    public static UsageTrendModel Learn(UsageWindow window, IReadOnlyList<UsageObservation> history)
    {
        var last = history.LastOrDefault();
        if (last == null) return new(ForecastState.Collecting);
        var recent = history.Where(p => p.Segment == last.Segment && p.At >= last.At.AddHours(-24)).ToArray();
        var blocks = new SortedDictionary<long, Block>();
        for (var index = 1; index < recent.Length; index++)
        {
            var first = recent[index - 1]; var next = recent[index];
            var start = first.At.UtcTicks; var end = next.At.UtcTicks; var elapsed = end - start;
            if (elapsed <= 0 || elapsed > MaximumGapTicks || next.UsedPercent < first.UsedPercent) continue;
            var growth = next.UsedPercent - first.UsedPercent;
            while (start < end)
            {
                var key = start / BlockTicks; var edge = Math.Min(end, (key + 1) * BlockTicks);
                if (!blocks.TryGetValue(key, out var block)) { block = new Block(); blocks.Add(key, block); }
                block.Coverage += edge - start; block.Growth += growth * ((edge - start) / (double)elapsed);
                start = edge;
            }
        }
        var complete = blocks.Values.Where(b => b.Coverage == BlockTicks).ToArray();
        var active = complete.Where(b => b.Growth > 0).ToArray();
        var observed = TimeSpan.FromTicks(active.Length * BlockTicks);
        if (active.Length == 0) return new(complete.Length > 0 ? ForecastState.NoGrowth : ForecastState.Collecting,
            last.At, ObservedSpan: observed, SampleCount: recent.Length);
        if (active.Length < 3) return new(ForecastState.Collecting, last.At, ObservedSpan: observed, SampleCount: recent.Length, ActivityBlocks: active.Length);
        if (active.Sum(b => b.Growth) < 0.1) return new(ForecastState.InsufficientChange, last.At, ObservedSpan: observed, SampleCount: recent.Length, ActivityBlocks: active.Length);
        var rates = active.Select(b => b.Growth * 6).ToArray();
        var rate = rates.Take(3).OrderBy(r => r).ElementAt(1);
        foreach (var nextRate in rates.Skip(3))
            rate += Alpha * Math.Clamp(nextRate - rate, -rate * 0.5, rate * 0.5);
        return new(ForecastState.Projected, last.At, rate, observed, recent.Length, active.Length);
    }
    public static UsageForecast Evaluate(UsageWindow window, IReadOnlyList<UsageObservation> history, DateTimeOffset now) => Evaluate(window, Learn(window, history), now);
    public static UsageForecast Evaluate(UsageWindow window, UsageTrendModel model, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        if (window.ResetsAt is long reset && DateTimeOffset.FromUnixTimeSeconds(reset) <= now) return new(ForecastState.ResetElapsed);
        if (model.LastAt is DateTimeOffset last && now - last >= TimeSpan.FromMinutes(15)) return new(ForecastState.Stale);
        if (window.UsedPercent >= 100 && model.LastAt.HasValue) return new(ForecastState.Exhausted);
        if (model.RatePerHour is not double rate) return new(model.State, ObservedSpan: model.ObservedSpan, SampleCount: model.SampleCount, ActivityBlocks: model.ActivityBlocks);
        var hours = window.Remaining / rate;
        var resetAt = window.ResetsAt is long timestamp ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : (DateTimeOffset?)null;
        var projectedUse = resetAt.HasValue ? Math.Clamp(window.UsedPercent + rate * (resetAt.Value - now).TotalHours, 0, 100) : (double?)null;
        if (hours > (DateTimeOffset.MaxValue - now).TotalHours)
            return new(resetAt.HasValue ? ForecastState.ResetsFirst : ForecastState.InsufficientChange,
                RatePerHour: resetAt.HasValue ? rate : null, ObservedSpan: model.ObservedSpan, SampleCount: model.SampleCount, UsedAtReset: projectedUse, ActivityBlocks: model.ActivityBlocks);
        var exhaustedAt = now.AddHours(hours);
        var state = resetAt == null ? ForecastState.Projected
            : exhaustedAt < resetAt ? ForecastState.DepletesFirst : ForecastState.ResetsFirst;
        return new(state, exhaustedAt, rate, model.ObservedSpan, model.SampleCount, projectedUse, model.ActivityBlocks);
    }
}

internal static class UsagePresentation
{
    public static DateTimeOffset KoreanTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time,
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Korea Standard Time" : "Asia/Seoul"));
    public static string TimeLeft(DateTimeOffset target, DateTimeOffset now)
    {
        var minutes = (long)Math.Ceiling((target - now).TotalMinutes);
        if (minutes <= 0) return Loc.T("시각 도달 · 갱신으로 확인");
        return minutes >= 1440 ? Loc.F("약 {0}일 {1}시간 {2}분 후", minutes / 1440, minutes % 1440 / 60, minutes % 60)
            : minutes >= 60 ? Loc.F("약 {0}시간 {1}분 후", minutes / 60, minutes % 60) : Loc.F("약 {0}분 후", minutes);
    }
    public static string Outlook(UsageForecast forecast) => Loc.T(forecast.State switch
    {
        ForecastState.Collecting => "예측 기록 수집 중",
        ForecastState.InsufficientChange => "사용량 변화를 기다리는 중",
        ForecastState.NoGrowth => "최근 사용 증가 없음",
        ForecastState.Stale => "새 조회가 필요합니다",
        ForecastState.ResetElapsed => "리셋 시각 도달 · 갱신 대기",
        ForecastState.Exhausted => "사용 한도에 도달했습니다",
        ForecastState.DepletesFirst => "리셋 전에 소진 예상",
        ForecastState.ResetsFirst => "리셋이 먼저 옵니다",
        _ => "최근 속도로 소진 예상"
    });
    public static string Basis(UsageForecast forecast) => forecast.IsEstimate
        ? Loc.F("사용 구간 {0}개 · 시간당 {1:0.##}%p · 계속 사용 기준", forecast.ActivityBlocks, forecast.RatePerHour)
        : Loc.T(forecast.State switch
        {
            ForecastState.Collecting => "사용량이 증가한 10분 구간 3개가 필요합니다.",
            ForecastState.InsufficientChange => "소모 속도를 계산할 만큼 변화가 쌓이면 예측합니다.",
            ForecastState.NoGrowth => "관측 구간에서 사용량이 늘지 않아 소진 시각을 계산하지 않습니다.",
            ForecastState.Stale => "마지막 조회가 15분 이상 지났습니다. 새로 고침해 주세요.",
            ForecastState.ResetElapsed => "새 사용량 조회 후 현재 구간을 다시 계산합니다.",
            _ => "실제 조회값 기준"
        });
}
