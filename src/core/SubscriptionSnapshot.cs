using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccountDesk;

internal sealed class SubscriptionSnapshot
{
    public string AccountId { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public bool WillRenew { get; set; }
    public DateTimeOffset? RenewsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CancelsAt { get; set; }
    [JsonIgnore] public DateTimeOffset? NextDate => WillRenew ? RenewsAt : ExpiresAt ?? CancelsAt;

    public static SubscriptionSnapshot Parse(JsonElement response, string expectedAccountId, DateTimeOffset checkedAt)
    {
        if (!response.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Object ||
            !accounts.TryGetProperty(expectedAccountId, out var selected) || selected.ValueKind != JsonValueKind.Object ||
            !selected.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object ||
            JsonValue.String(account, "account_id") != expectedAccountId)
            throw new InvalidDataException("Subscription response does not match the requested account.");
        var entitlement = selected.GetProperty("entitlement");
        var active = JsonValue.Bool(entitlement, "has_active_subscription") ?? throw new InvalidDataException("Subscription activity is missing.");
        var result = new SubscriptionSnapshot { AccountId = expectedAccountId, CheckedAt = checkedAt };
        if (!active) return result;
        result.WillRenew = JsonValue.Bool(selected.GetProperty("last_active_subscription"), "will_renew")
            ?? throw new InvalidDataException("Subscription renewal status is missing.");
        result.RenewsAt = Date(entitlement, "renews_at");
        result.ExpiresAt = Date(entitlement, "expires_at");
        result.CancelsAt = Date(entitlement, "cancels_at");
        return result;
    }
    private static DateTimeOffset? Date(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var date) || date.ValueKind == JsonValueKind.Null) return null;
        if (date.ValueKind == JsonValueKind.String && date.TryGetDateTimeOffset(out var parsed)) return parsed;
        throw new InvalidDataException("Subscription date has an invalid format.");
    }
}
