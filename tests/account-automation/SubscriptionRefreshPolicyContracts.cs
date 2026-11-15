using System.Text.Json;
using System.Text.Json.Nodes;
using CodexAccountDesk;

internal static class SubscriptionRefreshPolicyContracts
{
    public static void Run(Action<string, bool> check)
    {
        var now = DateTimeOffset.Parse("2026-11-15T00:00:00Z");
        var renews = DateTimeOffset.Parse("2026-12-08T17:53:27Z");
        var expires = renews.AddHours(6);
        var response = JsonNode.Parse(JsonSerializer.Serialize(new { accounts = new Dictionary<string, object>
        {
            ["selected"] = new { account = new { account_id = "selected" }, entitlement = new
            { has_active_subscription = true, renews_at = renews, expires_at = expires, cancels_at = (DateTimeOffset?)null }, last_active_subscription = new { will_renew = true } },
            ["other"] = new { account = new { account_id = "other" } }
        } }))!;
        SubscriptionSnapshot Parse() => SubscriptionSnapshot.Parse(JsonSerializer.SerializeToElement(response), "selected", now);
        bool Rejects() { try { Parse(); return false; } catch (InvalidDataException) { return true; } }
        var selected = response["accounts"]!["selected"]!;
        check("Billing renewal is distinct from the later entitlement expiry", Parse().NextDate == renews && Parse().ExpiresAt == expires);
        check("Checked timestamp records the successful billing query", Parse().CheckedAt == now);
        check("Renewal countdown crosses UTC midnight using Korean dates", SubscriptionPresentation.Days(Parse().NextDate, now) == "24");
        selected["last_active_subscription"]!["will_renew"] = false;
        check("Cancelled subscriptions use the reported expiration", Parse().NextDate == expires);
        selected["entitlement"]!["expires_at"] = null;
        selected["entitlement"]!["cancels_at"] = System.Text.Json.Nodes.JsonValue.Create(renews);
        check("Cancellation date is used when no expiration is provided", Parse().NextDate == renews);
        selected["last_active_subscription"]!["will_renew"] = true;
        selected["entitlement"]!["renews_at"] = null;
        check("Missing renewal does not become a fabricated expiry", Parse().NextDate == null);
        selected["entitlement"]!["renews_at"] = "invalid";
        check("Malformed reported date is rejected", Rejects());
        selected["entitlement"]!["has_active_subscription"] = false;
        check("An inactive subscription clears previous dates", Parse().NextDate == null);
        selected["account"]!["account_id"] = "other";
        check("An account mismatch is rejected even under the selected map key", Rejects());
        response["accounts"]!.AsObject().Remove("selected");
        check("An unrelated account is never selected as a fallback", Rejects());
    }
}
