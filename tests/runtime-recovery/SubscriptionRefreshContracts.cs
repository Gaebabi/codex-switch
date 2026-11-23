using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class SubscriptionRefreshContracts
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return respond(request, cancellation); }
    }
    public static HttpClient InstallClient(object service, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        InstallClient(service, (request, _) => Task.FromResult(respond(request)));
    private static HttpClient InstallClient(object service, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var client = new HttpClient(new Handler(respond));
        var property = service.GetType().GetProperty("SubscriptionClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        property.SetValue(service, Activator.CreateInstance(property.PropertyType, client));
        return client;
    }
    public static HttpResponseMessage Response(string accountId, DateTimeOffset? date) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { accounts = new Dictionary<string, object> {
            [accountId] = new { account = new { account_id = accountId }, entitlement = new { has_active_subscription = date != null, renews_at = date, expires_at = date?.AddHours(6), cancels_at = (string?)null }, last_active_subscription = new { will_renew = true } }
        } }), Encoding.UTF8, "application/json")
    };
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var application = Assembly.Load("CodexSwitch.Application");
        var serviceType = application.GetType("CodexAccountDesk.AccountService", true)!;
        var vaultType = application.GetType("CodexAccountDesk.AccountVault", true)!;
        var home = Path.Combine(root, "subscription-home"); Directory.CreateDirectory(home);
        var path = Path.Combine(home, "auth.json");
        var now = DateTimeOffset.UtcNow; var oldEnd = now.AddDays(16); var newEnd = now.AddDays(24);
        var claims = new Dictionary<string, object> { ["sub"] = "subscription-fixture", ["email"] = "subscription@example.test", ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["https://api.openai.com/auth"] = new { chatgpt_plan_type = "promax", chatgpt_subscription_active_until = oldEnd, chatgpt_subscription_last_checked = now.AddDays(-15) } };
        var token = "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", last_refresh = now,
            tokens = new { account_id = "subscription-fixture-workspace", id_token = token, access_token = "fictional-valid-access", refresh_token = "fictional-refresh" } });
        File.WriteAllText(path, auth);
        var vaultPath = Path.Combine(root, "subscription-vault");
        var vault = Activator.CreateInstance(vaultType, vaultPath)!;
        var service = Activator.CreateInstance(serviceType, new object?[] { vault, home, null, null, null })!;
        var mode = "success"; var requestValid = false;
        var billingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBilling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = InstallClient(service, async (request, cancellation) =>
        {
            requestValid = request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == "https://chatgpt.com/backend-api/accounts/check/v4-2023-04-27" &&
                request.Headers.Authorization?.Parameter == "fictional-valid-access" && request.Headers.GetValues("ChatGPT-Account-ID").Single() == "subscription-fixture-workspace" && request.Headers.UserAgent.ToString().StartsWith("CodexSwitch/");
            if (mode == "failure")
            {
                billingEntered.TrySetResult();
                await allowBilling.Task.WaitAsync(cancellation);
            }
            return mode switch {
                "failure" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "mismatch" => Response("different-account", newEnd),
                _ => Response("subscription-fixture-workspace", mode == "inactive" ? null : newEnd)
            };
        });
        Task Call(string method, params object?[] values) => (Task)serviceType.GetMethod(method)!.Invoke(service, values)!;
        object Account() => ((Array)serviceType.GetProperty("Accounts")!.GetValue(service)!).GetValue(0)!;
        object? Value(string name) => Account().GetType().GetProperty(name)!.GetValue(Account());
        try
        {
            await Call("InitializeAsync", CancellationToken.None);
            check(Value("SubscriptionActiveUntil") == null, "A fresh ID token containing stale subscription claims is not treated as billing data");
            await Call("RefreshAllAsync", null, CancellationToken.None);
            check(requestValid && Value("Error") == null && Value("SubscriptionError") == null && Value("Usage") != null && Equals(Value("SubscriptionActiveUntil"), newEnd), "Actual service loads current billing with the selected identity and keeps usage available");
            check(File.ReadAllText(path) == auth, "Billing refresh does not rotate or rewrite canonical credentials");
            var saved = Activator.CreateInstance(vaultType, vaultPath)!;
            var data = vaultType.GetProperty("Data")!.GetValue(saved)!;
            var accounts = (System.Collections.IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
            var loaded = accounts.Cast<object>().Single();
            check(Equals(loaded.GetType().GetProperty("SubscriptionActiveUntil")!.GetValue(loaded), newEnd), "Authoritative billing survives encrypted vault reload with old claims intact");
            await Call("RefreshAllAsync", null, CancellationToken.None);
            check(Equals(Value("SubscriptionActiveUntil"), newEnd), "Canonical credential synchronization cannot restore the old token date");
            var usage = Value("Usage"); var checkedAt = Value("SubscriptionLastChecked"); mode = "failure";
            var pending = Call("RefreshAllAsync", null, CancellationToken.None);
            bool quotaVisible;
            try
            {
                await billingEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                quotaVisible = !pending.IsCompleted && Value("Error") == null && !ReferenceEquals(usage, Value("Usage")) && Equals(Value("SubscriptionLastChecked"), checkedAt);
            }
            finally { allowBilling.TrySetResult(); }
            await pending;
            check(quotaVisible && Value("Error") == null && Value("SubscriptionError") != null && Equals(Value("SubscriptionLastChecked"), checkedAt) && Equals(Value("SubscriptionActiveUntil"), newEnd), "Fresh quota is visible while billing is pending, and a billing failure preserves the last successful date");
            mode = "mismatch";
            await Call("RefreshAllAsync", null, CancellationToken.None);
            check(Value("SubscriptionError") != null && Equals(Value("SubscriptionActiveUntil"), newEnd), "Foreign billing never replaces the selected subscription");
            mode = "inactive";
            await Call("RefreshAllAsync", null, CancellationToken.None);
            check(Value("SubscriptionError") == null && Value("SubscriptionActiveUntil") == null, "A successful inactive result clears the previous billing date");
        }
        finally { await Call("StopAsync"); }
    }
}
