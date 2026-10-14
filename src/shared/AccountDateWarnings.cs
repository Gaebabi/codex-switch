using System;

namespace CodexAccountDesk;

internal static class AccountDateWarnings
{
    public static bool Subscription(DateTimeOffset? until, DateTimeOffset now) => Within(until, now, TimeSpan.FromDays(3));
    public static bool Reset(long? until, DateTimeOffset now) => Within(until.HasValue ? DateTimeOffset.FromUnixTimeSeconds(until.Value) : null, now, TimeSpan.FromDays(2));
    private static bool Within(DateTimeOffset? until, DateTimeOffset now, TimeSpan window) =>
        until is DateTimeOffset end && end >= now && end - now <= window;
}
