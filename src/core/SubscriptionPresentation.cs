using System;
using System.Globalization;

namespace CodexAccountDesk;

internal static class SubscriptionPresentation
{
    // Official numbered tiers, not internal entitlement names or an inferred charge.
    // Pricing/dots docs and official desktop plan-name messages checked 2026-10-24:
    // prolite -> Pro 100, pro -> Pro 200, promax -> Pro 500.
    public static string Plan(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "free" => "Free", "go" => "Go", "plus" => "Plus",
        "prolite" or "pro_lite" or "pro_100" => "Pro 100",
        "pro" or "pro_200" => "Pro 200",
        "promax" or "pro_max" or "pro_500" => "Pro 500",
        "team" or "business" or "chatgptteamplan" => "Business",
        "enterprise" => "Enterprise", "edu" or "education" => "Edu",
        null or "" or "unknown" => "—",
        _ => raw.Trim()
    };
    public static string Days(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until == null) return "—";
        var days = (UsagePresentation.KoreanTime(until.Value).Date - UsagePresentation.KoreanTime(now).Date).Days;
        return days == 0 ? Loc.T("오늘") : days > 0 ? days.ToString(CultureInfo.InvariantCulture) : Loc.F("subscription.expired_days", -days);
    }
}
