using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal static class AutomaticSwitchPolicy
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(2);
    public static bool ValidThreshold(int threshold) => threshold is 5 or 10 or 15 or 20;
    public static bool Exhausted(AccountProfile profile, DateTimeOffset now)
    {
        if (!Fresh(profile, now)) return false;
        var windows = Windows(profile);
        // A fresh reported zero is protective while a reset timestamp awaits an
        // update. Unknown or ambiguous numbers cannot stop a client.
        return windows is { Count: > 0 } && windows.Values.All(Numeric) && windows.Values.Any(w => w.Remaining == 0);
    }
    public static bool OrdinaryAvailable(AccountProfile profile, DateTimeOffset now, bool allowPendingCredentials = false)
    {
        if (!Fresh(profile, now, allowPendingCredentials) || profile.Usage!.OrdinaryUsageAllowed == false ||
            profile.Usage.Buckets.Any(b => b.LimitState != null || b.SpendControlReached == true)) return false;
        var windows = Windows(profile);
        return windows is { Count: > 0 } && windows.Values.All(w => Valid(w, now) && w.Remaining > 0);
    }

    // Compare the same quota buckets/windows, independently of the card's display selection.
    public static AccountProfile? Select(IEnumerable<AccountProfile> accounts, int threshold, DateTimeOffset now, IReadOnlyList<string>? priority = null)
        => Choose(accounts, threshold, now, priority, true);

    // Read-only forecast: use identical candidate guards without requiring low source quota.
    public static AccountProfile? Next(IEnumerable<AccountProfile> accounts, int threshold, DateTimeOffset now, IReadOnlyList<string>? priority = null)
        => Choose(accounts, threshold, now, priority, false);

    private static AccountProfile? Choose(IEnumerable<AccountProfile> accounts, int threshold, DateTimeOffset now, IReadOnlyList<string>? priority, bool requireTrigger)
    {
        if (!ValidThreshold(threshold)) return null;
        var profiles = accounts.ToArray();
        var active = profiles.Where(p => p.IsCurrent).ToArray();
        var current = active.Length == 1 ? active[0] : null;
        if (current == null || !Fresh(current, now)) return null;
        var observed = Windows(current);
        if (observed == null) return null;
        var windows = observed.Select(w => (Key: w.Key, Window: w.Value)).ToArray();
        if (windows.Length == 0 || windows.Any(w => !Valid(w.Window, now)) || requireTrigger && windows.Min(w => w.Window.Remaining) >= threshold) return null;
        return profiles.Where(p => !p.IsCurrent && Fresh(p, now) && p.Usage!.OrdinaryUsageAllowed != false &&
                !p.Usage.Buckets.Any(b => b.LimitState != null || b.SpendControlReached == true))
            .Select(p => new { Profile = p, Windows = Windows(p) })
            .Where(p => p.Windows != null && windows.All(w => p.Windows.TryGetValue(w.Key, out var candidate) && Valid(candidate, now) &&
                candidate.Minutes == w.Window.Minutes && candidate.Remaining >= threshold + 5) &&
                p.Windows.Values.All(w => Valid(w, now) && w.Remaining >= threshold + 5))
            .OrderBy(p => priority == null ? 0 : Rank(priority, p.Profile.Key))
            .ThenBy(p => priority == null && p.Profile.SubscriptionActiveUntil is DateTimeOffset until ? until : DateTimeOffset.MaxValue)
            .ThenByDescending(p => p.Windows!.Values.Min(w => w.Remaining))
            .ThenBy(p => p.Profile.Key, StringComparer.Ordinal).Select(p => p.Profile).FirstOrDefault();
    }
    private static int Rank(IReadOnlyList<string> priority, string key)
    {
        for (var i = 0; i < priority.Count; i++) if (priority[i] == key) return i;
        return int.MaxValue;
    }
    private static bool Fresh(AccountProfile profile, DateTimeOffset now, bool allowPendingCredentials = false) =>
        profile.Error == null && !profile.IsRefreshing && (!profile.RequiresApply || allowPendingCredentials) && profile.Usage != null &&
        now >= profile.Usage.CheckedAt && now - profile.Usage.CheckedAt <= MaximumAge;
    private static bool Numeric(UsageWindow window) => window.Minutes is > 0 && double.IsFinite(window.UsedPercent) && window.UsedPercent is >= 0 and <= 100;
    private static bool Valid(UsageWindow window, DateTimeOffset now) => Numeric(window) && (window.ResetsAt == null || window.ResetsAt > now.ToUnixTimeSeconds());
    private static Dictionary<string, UsageWindow>? Windows(AccountProfile profile)
    {
        var buckets = new HashSet<string>(StringComparer.Ordinal);
        var windows = new Dictionary<string, UsageWindow>(StringComparer.Ordinal);
        foreach (var bucket in profile.Usage!.Buckets)
        {
            // Ambiguous observations cannot identify matching quotas. Retain the original
            // snapshot; refuse this profile instead of selecting a duplicate or throwing.
            if (string.IsNullOrWhiteSpace(bucket.Id) || !buckets.Add(bucket.Id)) return null;
            if (bucket.Primary != null) windows.Add(bucket.Id + ":primary", bucket.Primary);
            if (bucket.Secondary != null) windows.Add(bucket.Id + ":secondary", bucket.Secondary);
        }
        return windows;
    }
}
