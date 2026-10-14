using System;
using System.Linq;

namespace CodexAccountDesk;

internal static class SyntheticDashboard
{
    public static AccountProfile[] Accounts() => Enumerable.Range(0, 4).Select(index => new AccountProfile {
        Key = "synthetic-" + index, Alias = "Demo account " + (index + 1), Email = "demo" + (index + 1) + "@example.invalid",
        Plan = index % 2 == 0 ? "pro" : "plus", IsCurrent = index == 0,
        Usage = new UsageSnapshot { CheckedAt = DateTimeOffset.UtcNow, AvailableResets = index,
            Buckets = new() { new UsageBucket { Id = "codex", Label = "Codex", CreditBalance = "12000",
                Primary = new UsageWindow { UsedPercent = new[] { 27.4, 91.2, 100.0, 0.0 }[index], Minutes = 10080 } } } }
    }).ToArray();
}
