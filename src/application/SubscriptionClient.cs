using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class SubscriptionClient
{
    private static readonly HttpClient Shared = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _http;
    public SubscriptionClient(HttpClient? http = null) => _http = http ?? Shared;
    public async Task<SubscriptionSnapshot> ReadAsync(string authJson, CancellationToken cancellation)
    {
        var identity = AuthDocument.Parse(authJson);
        using var auth = JsonDocument.Parse(authJson);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/accounts/check/v4-2023-04-27");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.RootElement.GetProperty("tokens").GetProperty("access_token").GetString());
        request.Headers.Add("ChatGPT-Account-ID", identity.AccountId);
        request.Headers.UserAgent.ParseAdd("CodexSwitch/" + typeof(SubscriptionClient).Assembly.GetName().Version);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        return SubscriptionSnapshot.Parse(document.RootElement, identity.AccountId, DateTimeOffset.UtcNow);
    }
}
