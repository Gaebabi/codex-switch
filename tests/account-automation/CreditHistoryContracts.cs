using CodexAccountDesk;
using System.Text.Json;

internal static class CreditHistoryContracts
{
    public static void Run(Action<string, bool> check)
    {
        var at = DateTimeOffset.Parse("2026-10-29T00:00:00Z");
        var profile = new AccountProfile { Key = "credit-fixture" };
        UsageSnapshot Snapshot(int minute, string? balance, bool? unlimited = false, string id = "codex") => new()
        { CheckedAt = at.AddMinutes(minute), Buckets = new() { new() { Id = id, CreditBalance = balance, UnlimitedCredits = unlimited } } };
        void Record(int minute, string? balance) => UsageHistory.Record(profile, Snapshot(minute, balance));
        Record(0, "100.25"); Record(1, "98.75"); Record(2, "98.75");
        var track = profile.CreditHistory.Single();
        check("Credit history records actual decimal balances and observed decreases independently of quota percentages",
            track.Points.Count == 3 && track.Points[1].ObservedDecrease == 1.50m && track.Points[2].ObservedDecrease == 0 && CreditHistory.ObservedUse(track, 24, at.AddMinutes(2)) == 1.50m);
        check("A first balance cannot invent previous credit consumption", track.Points[0].ObservedDecrease == null);
        Record(3, "148.75"); Record(4, "147.50");
        check("Top-ups never become negative consumption or erase earlier observed decreases", track.Points[^2].ObservedDecrease == 0 && CreditHistory.ObservedUse(track, 24, at.AddMinutes(4)) == 2.75m);
        var saved = JsonSerializer.Serialize(track);
        Record(4, "0"); Record(2, "0");
        check("Duplicate and out-of-order snapshots cannot rewrite credit observations", JsonSerializer.Serialize(track) == saved);
        Record(5, null); Record(6, "140");
        check("Missing balance stays unknown and breaks the consumption interval", track.Points[^2].Balance == null && track.Points[^1].ObservedDecrease == null && track.Points[^1].Segment != track.Points[^3].Segment);
        Record(15, "130");
        check("Long collection gaps cannot invent a precise credit-use interval", track.Points[^1].ObservedDecrease == null);
        CreditHistory.Record(profile, Snapshot(16, "0", true));
        check("Unlimited credits never fabricate a zero balance", track.Points[^1].Balance == null);
        foreach (var value in new[] { "NaN", "Infinity", "12,5", "1e1000" })
            check("Invalid credit balance remains unavailable: " + value, CreditHistory.Balance(new() { CreditBalance = value }) == null);
        check("Credit numbers use invariant decimal precision including exponent notation", CreditHistory.Balance(new() { CreditBalance = "1.25e2" }) == 125m);
        Record(17, "0"); Record(18, "-0.25");
        check("Zero and reported negative balances remain actual values, without clamping to fabricated zero", track.Points[^2].Balance == 0 && track.Points[^1].Balance == -0.25m && track.Points[^1].ObservedDecrease == 0.25m);
        var copy = track.Copy(); copy.Points.Clear();
        check("Credit snapshots copy their lists without losing persisted observations", track.Points.Count > 0);
        var roundtrip = JsonSerializer.Deserialize<AccountProfile>(JsonSerializer.Serialize(profile))!;
        check("Credit history round-trips without replacing existing account identity", roundtrip.Key == profile.Key && JsonSerializer.Serialize(roundtrip.CreditHistory) == JsonSerializer.Serialize(profile.CreditHistory));
        check("Legacy accounts load an empty optional credit history", JsonSerializer.Deserialize<AccountProfile>("{\"Key\":\"old\"}")!.CreditHistory.Count == 0);
        var multiple = new AccountProfile();
        var snapshot = Snapshot(0, "100"); snapshot.Buckets.Add(new() { Id = "other", CreditBalance = "50" });
        CreditHistory.Record(multiple, snapshot);
        check("Separate credit buckets never merge their balances", multiple.CreditHistory.Count == 2);
        snapshot = Snapshot(1, "90"); snapshot.Buckets.Add(new() { Id = "codex", CreditBalance = "80" });
        CreditHistory.Record(multiple, snapshot);
        check("Duplicate bucket identities remain ambiguous rather than double counting credit use", multiple.CreditHistory.All(t => t.Points[^1].Balance == null));
        var compacted = new AccountProfile { CreditHistory = new() { new() { BucketId = "codex" } } };
        for (var i = 0; i <= 4320; i++)
            compacted.CreditHistory[0].Points.Add(new(at.AddDays(-3).AddMinutes(i), 500m - i * 0.01m, i == 0 ? null : 0.01m, i == 0 ? null : at.AddDays(-3).AddMinutes(i - 1), 0));
        CreditHistory.Record(compacted, Snapshot(1, "456.79"));
        check("Older-resolution compaction preserves observed consumption across top-level refreshes", compacted.CreditHistory[0].Points.Count < 4321 && CreditHistory.ObservedUse(compacted.CreditHistory[0], 168, at.AddMinutes(1)) == 43.21m);
        var bounded = new AccountProfile { CreditHistory = new() { new() { BucketId = "codex" } } };
        for (var i = 0; i < 12000; i++) bounded.CreditHistory[0].Points.Add(new(at.AddSeconds(i - 12000), 100, null, null, i));
        CreditHistory.Record(bounded, Snapshot(0, "99"));
        check("Credit storage remains bounded under fragmented observations", bounded.CreditHistory[0].Points.Count <= CreditHistory.MaximumPoints);
        var quota = new AccountProfile { History = new() { new() { BucketId = "codex", Slot = "primary", Points = new() { new(at, 25, null, 0) } } } };
        var quotaBefore = JsonSerializer.Serialize(quota.History);
        UsageHistory.Record(quota, Snapshot(1, "10"));
        check("Adding credit observations preserves unrelated quota history", JsonSerializer.Serialize(quota.History) == quotaBefore);
        var retired = new AccountProfile(); CreditHistory.Record(retired, Snapshot(0, "10"));
        CreditHistory.Record(retired, new() { CheckedAt = at.AddMinutes(1) });
        CreditHistory.Record(retired, new() { CheckedAt = at.AddMinutes(2) });
        check("Repeated missing balances keep one gap marker rather than unbounded unknown records", retired.CreditHistory.Single().Points.Count == 2);
        CreditHistory.Record(retired, new() { CheckedAt = at.AddDays(31) });
        check("Retired credit bucket histories expire instead of being kept alive by missing reports", retired.CreditHistory.Count == 0);
        check("Credit selectors cannot collide with opaque quota bucket and slot identifiers", !CreditPresentation.Selected("credits:primary") && !CreditPresentation.Selected("credits:future:secondary") && CreditPresentation.Selected(CreditPresentation.Key(new() { BucketId = "future:primary" })));
    }
}
