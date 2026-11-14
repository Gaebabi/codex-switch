using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

internal static class LiveGatewayCatalogContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "live-gateway-catalog");
        var store = new ModelProviderStore(directory);
        var provider = new ModelProvider { Name = "Live fixture", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-key",
            Models = new() { new ProviderModel { UpstreamId = "custom-fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("custom-fixture") } } };
        store.Update(settings =>
        {
            settings.Providers.Add(provider); settings.SelectedRoute = provider.Models[0].Alias(provider.Id);
            settings.RouterEnabled = settings.Connected = settings.ConnectionWriteConfirmed = settings.DesktopProfile = true;
            settings.DesktopAppliedConfig = new() { ["model_catalog_json"] = null };
        });
        GatewayIdentity? Authenticate(string? token, string? account) => token is "Bearer fictional-a" or "Bearer fictional-b"
            ? new(token[7..], account ?? "fictional-account", false) : null;
        var upstream = new CatalogUpstream();
        using var http = new HttpClient(upstream);
        await using var gateway = new ModelGateway(store, Authenticate, http, directory, () => null);
        gateway.Start();
        using var client = new HttpClient();
        async Task<JsonObject> Read(string version = "0.167.0", string token = "fictional-a", string account = "fictional-account")
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, gateway.BaseUrl + "/models?client_version=" + version);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("ChatGPT-Account-ID", account);
            using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        }
        var first = await Read();
        check("The virtual model retains its stable ID and names the selected backend in parentheses",
            first["models"]![1]!["slug"]!.GetValue<string>() == ModelRouting.Model &&
            first["models"]![1]!["display_name"]!.GetValue<string>() == "Codex Switch (custom-fixture)");
        var vault = Path.Combine(directory, "model-providers.vault");
        var saved = await File.ReadAllBytesAsync(vault);
        var warm = await Read();
        check("Live catalog cache hits reuse complete native documents without rewriting the provider vault", upstream.Calls == 1 &&
            JsonNode.DeepEquals(first, warm) && (await File.ReadAllBytesAsync(vault)).SequenceEqual(saved) && warm["future_native_field"]!["retained"]!.GetValue<bool>());
        store.Update(settings => settings.Providers[0].Models[0].ReasoningOverride = new() { Levels = new() { "low", "high" }, Default = "high" });
        var changed = await Read();
        var models = changed["models"]!.AsArray();
        check("Warm native catalogs append fresh selected-model metadata exactly once", upstream.Calls == 1 && models.Count == 3 &&
            JsonNode.DeepEquals(models[0], first["models"]![0]) && models[1]!["default_reasoning_level"]!.GetValue<string>() == "high");
        store.Update(settings =>
        {
            var second = new ProviderModel { UpstreamId = "second-fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("second-fixture") };
            settings.Providers[0].Models.Add(second);
            settings.SelectedRoute = second.Alias(settings.Providers[0].Id);
        });
        var renamed = await Read();
        check("A warm catalog updates the backend name without modifying native models or the virtual ID", upstream.Calls == 1 &&
            renamed["models"]![1]!["display_name"]!.GetValue<string>() == "Codex Switch (second-fixture)" &&
            renamed["models"]![1]!["slug"]!.GetValue<string>() == ModelRouting.Model && JsonNode.DeepEquals(renamed["models"]![0], first["models"]![0]));
        await Read("0.168.0");
        check("Different native client versions do not reuse an incompatible cached catalog", upstream.Calls == 2);
        await Read("0.168.0", "fictional-b");
        await Read("0.168.0", "fictional-b", "fictional-other-account");
        check("Native catalog caching is isolated by both token and account", upstream.Calls == 4);
        upstream.Revision = 2;
        await gateway.ReadCatalogAsync(new("fictional-a", "fictional-account", false), "0.167.0", CancellationToken.None, true, true);
        var refreshed = await Read();
        check("Explicit discovery refreshes upstream metadata and subsequent warm reads retain the new document", upstream.Calls == 5 &&
            refreshed["models"]![0]!["description"]!.GetValue<string>() == "revision 2");
    }

    private sealed class CatalogUpstream : HttpMessageHandler
    {
        internal int Calls;
        internal int Revision = 1;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            var native = ProviderCatalog.Generic("native-fixture"); native["description"] = "revision " + Revision;
            var document = new JsonObject { ["models"] = new JsonArray(native), ["future_native_field"] = new JsonObject { ["retained"] = true } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(document.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
}
