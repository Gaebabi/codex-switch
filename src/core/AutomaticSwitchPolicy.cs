using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexAccountDesk;

internal static class AutomaticSwitchPolicy
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(2);
    public static bool ValidThreshold(int threshold) => threshold is 5 or 10 or 15 or 20;

    // Compare the same quota buckets/windows, independently of the card's display selection.
    public static AccountProfile? Select(IEnumerable<AccountProfile> accounts, int threshold, DateTimeOffset now, IReadOnlyList<string>? priority = null)
    {
        if (!ValidThreshold(threshold)) return null;
        var profiles = accounts.ToArray();
        var active = profiles.Where(p => p.IsCurrent).ToArray();
        var current = active.Length == 1 ? active[0] : null;
        if (current == null || !Fresh(current, now)) return null;
        var windows = Windows(current).ToArray();
        if (windows.Length == 0 || windows.Any(w => !Valid(w.Window, now)) || windows.Min(w => w.Window.Remaining) >= threshold) return null;
        return profiles.Where(p => !p.IsCurrent && Fresh(p, now) && p.Usage!.OrdinaryUsageAllowed != false &&
                !p.Usage.Buckets.Any(b => b.LimitState != null || b.SpendControlReached == true))
            .Select(p => new { Profile = p, Windows = Windows(p).ToDictionary(w => w.Key, w => w.Window) })
            .Where(p => windows.All(w => p.Windows.TryGetValue(w.Key, out var candidate) && Valid(candidate, now) &&
                candidate.Minutes == w.Window.Minutes && candidate.Remaining >= threshold + 5) &&
                p.Windows.Values.All(w => Valid(w, now) && w.Remaining >= threshold + 5))
            .OrderBy(p => priority == null ? 0 : Rank(priority, p.Profile.Key))
            .ThenByDescending(p => p.Windows.Values.Min(w => w.Remaining))
            .ThenBy(p => p.Profile.Key, StringComparer.Ordinal).Select(p => p.Profile).FirstOrDefault();
    }
    private static int Rank(IReadOnlyList<string> priority, string key)
    {
        for (var i = 0; i < priority.Count; i++) if (priority[i] == key) return i;
        return int.MaxValue;
    }
    private static bool Fresh(AccountProfile profile, DateTimeOffset now) =>
        profile.Error == null && !profile.IsRefreshing && !profile.RequiresApply && profile.Usage != null &&
        now >= profile.Usage.CheckedAt && now - profile.Usage.CheckedAt <= MaximumAge;
    private static bool Valid(UsageWindow window, DateTimeOffset now) => window.Minutes is > 0 && double.IsFinite(window.UsedPercent) &&
        window.UsedPercent is >= 0 and <= 100 && (window.ResetsAt == null || window.ResetsAt > now.ToUnixTimeSeconds());
    private static IEnumerable<(string Key, UsageWindow Window)> Windows(AccountProfile profile) =>
        profile.Usage!.Buckets.SelectMany(b => new[] {
            b.Primary == null ? ((string, UsageWindow)?)null : (b.Id + ":primary", b.Primary),
            b.Secondary == null ? ((string, UsageWindow)?)null : (b.Id + ":secondary", b.Secondary) }).OfType<(string, UsageWindow)>();
}
