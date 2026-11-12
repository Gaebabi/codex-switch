using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ReasoningControlContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var upstream = new ReasoningUpstream();
        var directory = Path.Combine(root, "reasoning-control");
        new ModelProviderStore(directory).Update(settings => settings.RouterEnabled = true);
        GatewayIdentity? Authenticate(string? value, string? account) => value == "Bearer fictional-reasoning-token" ? new("fictional-reasoning-token", "fictional-reasoning-account", false) : null;
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream));
        var qwen = await manager.SaveAsync(null, "Qwen fixture", "https://qwen.example.invalid/v1", "fictional-qwen-key", CancellationToken.None);
        var gemma = await manager.SaveAsync(null, "Gemma fixture", "https://gemma.example.invalid/v1", "fictional-gemma-key", CancellationToken.None);
        manager.SelectRoute(qwen.Models.Single().Alias(qwen.Id)); manager.Start();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-reasoning-token");
        client.DefaultRequestHeaders.Add("version", "0.167.0");
        async Task<JsonObject> Catalog()
        {
            var bytes = await client.GetStringAsync(manager.BaseUrl + "/models?client_version=0.167.0");
            return JsonNode.Parse(bytes)!["models"]!.AsArray().OfType<JsonObject>().Single(model => model["slug"]!.GetValue<string>() == ModelRouting.Model);
        }
        string[] Levels(JsonObject model) => model["supported_reasoning_levels"]!.AsArray().Select(level => level!["effort"]!.GetValue<string>()).ToArray();
        var catalog = await Catalog();
        check("The native Switch entry advertises the selected Qwen model's exact reasoning steps", Levels(catalog).SequenceEqual(new[] { "low", "medium", "xhigh" }) && catalog["default_reasoning_level"]!.GetValue<string>() == "medium");
        check("Reasoning descriptions and provider metadata are preserved independently", catalog["supported_reasoning_levels"]![2]!["description"]!.GetValue<string>() == "Deep fixture reasoning" && qwen.Models.Single().Metadata["slug"]!.GetValue<string>() == "qwen-fixture");
        async Task<HttpResponseMessage> Send(JsonNode? reasoning)
        {
            var body = new JsonObject { ["model"] = ModelRouting.Model, ["stream"] = true, ["input"] = new JsonArray(), ["reasoning"] = reasoning };
            return await client.PostAsync(manager.BaseUrl + "/responses", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        }
        using (var chosen = await Send(new JsonObject { ["effort"] = "xhigh", ["summary"] = "auto" }))
            check("A chosen native reasoning strength reaches the provider instead of being overwritten by Medium", chosen.IsSuccessStatusCode && upstream.Requests.Last()["reasoning"]!["effort"]!.GetValue<string>() == "xhigh" && upstream.Requests.Last()["reasoning"]!["summary"]!.GetValue<string>() == "auto");
        using (var defaults = await Send(null))
            check("The model metadata default is used only when no strength was selected", defaults.IsSuccessStatusCode && upstream.Requests.Last()["reasoning"]!["effort"]!.GetValue<string>() == "medium");
        var count = upstream.Requests.Count;
        using (var unsupported = await Send(new JsonObject { ["effort"] = "none" }))
            check("A stale unsupported native strength is refused before provider inference", unsupported.StatusCode == HttpStatusCode.BadRequest && upstream.Requests.Count == count);
        using (var malformed = await Send(System.Text.Json.Nodes.JsonValue.Create(7)))
            check("Malformed reasoning is not silently replaced with a model default", malformed.StatusCode == HttpStatusCode.BadRequest && upstream.Requests.Count == count);
        manager.SelectRoute(gemma.Models.Single().Alias(gemma.Id)); catalog = await Catalog();
        check("Switching models exposes Gemma's declared None and Medium steps without invented levels", Levels(catalog).SequenceEqual(new[] { "none", "medium" }));
        using (var none = await Send(new JsonObject { ["effort"] = "none" }))
            check("A model that advertises None receives the explicitly selected None strength", none.IsSuccessStatusCode && upstream.Requests.Last()["reasoning"]!["effort"]!.GetValue<string>() == "none");
    }

    internal sealed class ReasoningUpstream : HttpMessageHandler
    {
        internal List<JsonObject> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get)
            {
                var qwen = request.RequestUri!.Host == "qwen.example.invalid"; var native = request.RequestUri.Host == "chatgpt.com";
                var model = ProviderCatalog.Generic(native ? "native-fixture" : qwen ? "qwen-fixture" : "gemma-fixture");
                if (!native)
                {
                    model["default_reasoning_level"] = "medium";
                    model["supports_reasoning_summary_parameter"] = true;
                    model["supported_reasoning_levels"] = new JsonArray((qwen ? new[] { "low", "medium", "xhigh" } : new[] { "none", "medium" })
                        .Select(level => (JsonNode)new JsonObject { ["effort"] = level, ["description"] = level == "xhigh" ? "Deep fixture reasoning" : level }).ToArray());
                }
                return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["models"] = new JsonArray(model) }.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject());
            return new(HttpStatusCode.OK) { Content = new StringContent(Upstreams.Stream("reasoning-fixture"), Encoding.UTF8, "text/event-stream") };
        }
    }
}
