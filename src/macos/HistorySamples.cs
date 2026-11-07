using System;
using System.Linq;

namespace CodexAccountDesk;

internal static class HistorySamples
{
    public static AccountProfile Create(string scenario)
    {
        var now = DateTimeOffset.UtcNow;
        var window = new UsageWindow { UsedPercent = scenario == "single" ? 100 : 27.4, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() };
        var profile = new AccountProfile { Key = "synthetic-history", Alias = "Demo", Email = "demo@example.invalid", Plan = "pro",
            Usage = scenario == "unavailable" ? null : new UsageSnapshot { CheckedAt = now,
                Buckets = new() { new UsageBucket { Id = "codex", Label = "Codex", Primary = window } } } };
        if (scenario is "empty" or "unavailable") return profile;
        profile.History.Add(new UsageTrack { BucketId = "codex", Slot = "primary", Minutes = window.Minutes,
            Points = scenario == "single" ? new() { new(now, 100, window.ResetsAt, 0) } : Enumerable.Range(0, 289)
                .Select(i => new UsageObservation(now.AddMinutes(-1440 + i * 5), 4 + 23.4 * i / 288, window.ResetsAt, 0)).ToList() });
        if (scenario == "multiple")
        {
            var secondary = new UsageWindow { UsedPercent = 49.8, Minutes = 300, ResetsAt = now.AddHours(3).ToUnixTimeSeconds() };
            profile.Usage!.Buckets[0].Secondary = secondary;
            profile.History.Add(new UsageTrack { BucketId = "codex", Slot = "secondary", Minutes = 300,
                Points = new() { new(now.AddMinutes(-5), 48, secondary.ResetsAt, 0), new(now, 49.8, secondary.ResetsAt, 0) } });
        }
        return profile;
    }
}
