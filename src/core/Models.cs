using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccountDesk;

internal sealed class AccountProfile
{
    public string Key { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Email { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Plan { get; set; } = "unknown";
    public string AuthJson { get; set; } = "";
    [JsonIgnore] public DateTimeOffset? SubscriptionActiveUntil { get; set; }
    [JsonIgnore] public DateTimeOffset? SubscriptionLastChecked { get; set; }
    public bool RequiresApply { get; set; }
    public UsageSnapshot? Usage { get; set; }
    public List<UsageTrack> History { get; set; } = new();
    public List<CreditTrack> CreditHistory { get; set; } = new();
    [JsonIgnore] public bool IsCurrent { get; set; }
    [JsonIgnore] public string? Error { get; set; }
    [JsonIgnore] public bool IsRefreshing { get; set; }
    [JsonIgnore] public long CredentialRevision { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? (Email.Length > 0 ? Email : Loc.T("이메일 정보 없음")) : Alias;
}

internal sealed class UsageSnapshot
{
    public DateTimeOffset CheckedAt { get; set; }
    public List<UsageBucket> Buckets { get; set; } = new();
    public int? AvailableResets { get; set; }
    public bool? OrdinaryUsageAllowed { get; set; }
    [JsonIgnore] public double? LeastRemaining => Buckets.SelectMany(b => new[] { b.Primary, b.Secondary })
        .Where(w => w != null).Select(w => (double?)w!.Remaining).Min();

    public static UsageSnapshot Parse(JsonElement result, string expectedAccountId)
    {
        var reportedId = JsonValue.String(result, "accountId");
        if (!string.IsNullOrEmpty(reportedId) && !string.Equals(reportedId, expectedAccountId, StringComparison.Ordinal))
            throw new InvalidOperationException("서버가 다른 계정의 사용량을 반환했습니다. 다시 로그인해 주세요.");
        var snapshot = new UsageSnapshot { CheckedAt = DateTimeOffset.UtcNow, OrdinaryUsageAllowed = JsonValue.Bool(result, "ordinaryUsageAllowed") };
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object && buckets.EnumerateObject().Any())
        {
            foreach (var bucket in buckets.EnumerateObject()) snapshot.Buckets.Add(UsageBucket.Parse(bucket.Name, bucket.Value));
        }
        else if (result.TryGetProperty("rateLimits", out var single) && single.ValueKind == JsonValueKind.Object)
            snapshot.Buckets.Add(UsageBucket.Parse(JsonValue.String(single, "limitId") ?? "codex", single));
        if (result.TryGetProperty("rateLimitResetCredits", out var resets) && resets.ValueKind == JsonValueKind.Object)
            snapshot.AvailableResets = JsonValue.Int(resets, "availableCount");
        return snapshot;
    }
}

internal sealed class UsageBucket
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Plan { get; set; }
    public UsageWindow? Primary { get; set; }
    public UsageWindow? Secondary { get; set; }
    public string? CreditBalance { get; set; }
    public bool? UnlimitedCredits { get; set; }
    public string? LimitState { get; set; }
    public bool? SpendControlReached { get; set; }
    public static UsageBucket Parse(string id, JsonElement value)
    {
        var bucket = new UsageBucket { Id = id, Label = JsonValue.String(value, "limitName") ?? id,
            Plan = JsonValue.String(value, "planType"), Primary = UsageWindow.Parse(value, "primary"), Secondary = UsageWindow.Parse(value, "secondary"),
            LimitState = JsonValue.String(value, "rateLimitReachedType"), SpendControlReached = JsonValue.Bool(value, "spendControlReached") };
        if (value.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            bucket.CreditBalance = JsonValue.String(credits, "balance");
            bucket.UnlimitedCredits = JsonValue.Bool(credits, "unlimited");
        }
        return bucket;
    }
}

internal sealed class UsageWindow
{
    public double UsedPercent { get; set; }
    public long? Minutes { get; set; }
    public long? ResetsAt { get; set; }
    [JsonIgnore] public double Remaining => Math.Clamp(100 - UsedPercent, 0, 100);
    [JsonIgnore] public string PeriodLabel => Minutes switch
    {
        null => Loc.T("사용 한도"), 10080 => Loc.T("주간"), 1440 => Loc.T("일간"), 60 => Loc.T("1시간"), 1 => Loc.T("1분"),
        var m when m % 1440 == 0 => Loc.F("{0}일", m / 1440),
        var m when m % 60 == 0 => Loc.F("{0}시간", m / 60), var m => Loc.F("{0}분", m)
    };
    [JsonIgnore] public string ResetLabel => ResetsAt is long time
        ? Loc.F("{0:MM/dd HH:mm} 초기화 (한국 시간)", TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(time), TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time")))
        : Loc.T("초기화 시간 정보 없음");
    public string TimeUntilReset(DateTimeOffset now)
    {
        if (ResetsAt == null) return Loc.T("남은 시간 정보 없음");
        var difference = DateTimeOffset.FromUnixTimeSeconds(ResetsAt.Value) - now;
        if (difference <= TimeSpan.Zero) return Loc.T("리셋 시각 도달 · 갱신 대기");
        var totalMinutes = (long)Math.Floor(difference.TotalMinutes);
        return totalMinutes == 0 ? Loc.T("리셋까지 0일 0시간 · 1분 미만") : Loc.F("리셋까지 {0}일 {1}시간 {2:00}분", totalMinutes / 1440, totalMinutes % 1440 / 60, totalMinutes % 60);
    }
    public static UsageWindow? Parse(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        if (!value.TryGetProperty("usedPercent", out var used) || !used.TryGetDouble(out var percent) || !double.IsFinite(percent))
            throw new InvalidOperationException("사용량 응답의 백분율 형식이 올바르지 않습니다.");
        return new UsageWindow { UsedPercent = percent, Minutes = JsonValue.Long(value, "windowDurationMins"), ResetsAt = JsonValue.Long(value, "resetsAt") };
    }
}

internal static class AuthDocument
{
    public static AccountProfile Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var mode = JsonValue.String(root, "auth_mode");
        if (mode != null && mode != "chatgpt") throw new InvalidOperationException("ChatGPT로 로그인한 Codex 계정만 등록할 수 있습니다.");
        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("ChatGPT 로그인 정보가 없습니다. Codex에서 로그인하거나 계정 추가를 선택해 주세요.");
        var accountId = JsonValue.String(tokens, "account_id");
        var idToken = JsonValue.String(tokens, "id_token");
        var accessToken = JsonValue.String(tokens, "access_token");
        var refreshToken = JsonValue.String(tokens, "refresh_token");
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("로그인 정보에 필수 토큰이 없습니다. 계정 추가로 다시 로그인해 주세요.");
        using var claimsDocument = DecodeClaims(idToken);
        var claims = claimsDocument.RootElement;
        var subject = JsonValue.String(claims, "sub");
        if (string.IsNullOrWhiteSpace(subject)) throw new InvalidOperationException("로그인 정보에 사용자 식별자가 없습니다.");
        var email = JsonValue.String(claims, "email") ?? "";
        var plan = "unknown";
        DateTimeOffset? subscriptionUntil = null, subscriptionLastChecked = null;
        if (claims.TryGetProperty("https://api.openai.com/auth", out var authClaims) && authClaims.ValueKind == JsonValueKind.Object)
        {
            plan = JsonValue.String(authClaims, "chatgpt_plan_type") ?? plan;
            subscriptionUntil = JsonValue.Timestamp(authClaims, "chatgpt_subscription_active_until");
            subscriptionLastChecked = JsonValue.Timestamp(authClaims, "chatgpt_subscription_last_checked");
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId + "\n" + subject))).ToLowerInvariant();
        return new AccountProfile { Key = key, Email = email, AccountId = accountId, Plan = plan, AuthJson = json,
            SubscriptionActiveUntil = subscriptionUntil, SubscriptionLastChecked = subscriptionLastChecked };
    }
    private static JsonDocument DecodeClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) throw new InvalidOperationException("로그인 토큰의 형식이 올바르지 않습니다.");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload));
    }
}

internal static class JsonValue
{
    public static DateTimeOffset? Timestamp(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTimeOffset(out var value) ? value : null;
    public static string? String(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static bool? Bool(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
    public static long? Long(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var value) ? value : null;
    public static int? Int(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) ? value : null;
}
