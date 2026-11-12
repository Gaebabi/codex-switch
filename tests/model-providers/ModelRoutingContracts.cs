using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ModelRoutingContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "fixed-router"); var upstream = new Upstreams();
        GatewayIdentity? Authenticate(string? auth, string? account) => GatewayIdentity.Authenticate(auth, account,
            () => "{\"tokens\":{\"access_token\":\"fictional-router-token\",\"account_id\":\"fictional-router-account\"}}");
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream) { Timeout = Timeout.InfiniteTimeSpan });
        var a = await manager.SaveAsync(null, "Router A", "https://a.example.invalid/v1", "fictional-router-key-a", CancellationToken.None);
        var b = await manager.SaveAsync(null, "Router B", "https://b.example.invalid/v1", "fictional-router-key-b", CancellationToken.None);
        var aliasA = a.Models.Single().Alias(a.Id); var aliasB = b.Models.Single().Alias(b.Id);
        manager.Start();
        using var client = ProviderCatalog.CreateHttpClient(); client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-router-token");
        client.DefaultRequestHeaders.Add("ChatGPT-Account-ID", "fictional-router-account");
        async Task<HttpResponseMessage> Send(string session, Guid? turn = null, JsonArray? input = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses");
            request.Headers.Add("session_id", session);
            var body = new JsonObject { ["model"] = ModelRouting.Model, ["stream"] = true,
                ["input"] = input ?? new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "fictional-router-input" }) };
            if (turn != null) body["client_metadata"] = new JsonObject { ["x-codex-turn-metadata"] = JsonSerializer.Serialize(new { turn_id = turn }) };
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        using (var missing = await Send("unset"))
            check("A fixed model without an explicit selection refuses before upstream inference", missing.StatusCode == HttpStatusCode.BadRequest && !upstream.Requests.Any(request => request.Method == "POST"));
        var catalog = Path.Combine(directory, "model-providers-catalog.json");
        await File.WriteAllTextAsync(catalog, "fictional-unchanged-catalog");
        manager.SelectRoute(aliasA);
        var reloaded = new ModelProviderStore(directory).Read();
        check("Actual route selection survives encrypted reload without preparing a Codex connection", reloaded.SelectedRoute == aliasA && !reloaded.Connected);
        var refused = false;
        try { manager.SelectRoute("unregistered-route"); } catch (InvalidOperationException) { refused = true; }
        check("Invalid selection preserves the current route", refused && manager.Settings.SelectedRoute == aliasA);
        await manager.SaveReasoningAsync(a.Id, a.Models.Single().UpstreamId,
            new ProviderReasoningOptions { Levels = new() { "medium", "high" }, Default = "high" }, CancellationToken.None);
        using (var response = await Send("a"))
        {
            check("The virtual model completes through the selected provider", response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("response.completed"));
            var actual = upstream.Requests.Last(); var body = JsonNode.Parse(actual.Body!)!;
            check("The actual model, own key and saved reasoning default replace the virtual envelope", actual.Host == "a.example.invalid" && actual.Token == "fictional-router-key-a" && actual.Account == null && body["model"]!.GetValue<string>() == "shared-model" && body["reasoning"]!["effort"]!.GetValue<string>() == "high");
        }
        using (var upload = new GatedUpload())
        using (var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses") { Content = upload })
        {
            request.Headers.Add("session_id", "upload-in-progress");
            var previousAdmission = manager.LastRequestAt;
            var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            try
            {
                await upload.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var admission = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (manager.LastRequestAt == previousAdmission) await Task.Delay(10, admission.Token);
                manager.SelectRoute(aliasB); upload.Continue.TrySetResult();
                using var completed = await response; await completed.Content.ReadAsStringAsync();
                check("An admitted request keeps A even when B is selected during body upload", upstream.Requests.Last().Host == "a.example.invalid" && upstream.Requests.Last().Token == "fictional-router-key-a");
            }
            finally { upload.Continue.TrySetResult(); }
        }
        manager.SelectRoute(aliasA);
        upstream.Slow = new GatedStream(); upstream.StreamNext = true;
        using (var streaming = await Send("old-stream"))
        using (var reader = new StreamReader(await streaming.Content.ReadAsStreamAsync()))
        {
            check("The original fixed-model response is streaming before a selection change", (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!.Contains("response.output_text.delta") && !upstream.Slow.Finish.Task.IsCompleted);
            manager.SelectRoute(aliasB);
            using (var next = await Send("new-stream")) await next.Content.ReadAsStringAsync();
            check("A new fixed-model request uses B while A remains in flight", upstream.Requests.Last().Host == "b.example.invalid" && !upstream.Slow.Finish.Task.IsCompleted);
            upstream.Slow.Finish.TrySetResult();
            check("Switching does not terminate the original A stream", (await reader.ReadToEndAsync()).Contains("response.completed"));
        }
        manager.SelectRoute(aliasA); upstream.StorageFailures = 3;
        upstream.BeforeNextResponse = async () =>
        {
            manager.SelectRoute(aliasB);
            await manager.SaveAsync(a.Id, a.Name, a.BaseUrl, "fictional-router-key-a-updated", CancellationToken.None);
        };
        var start = upstream.Requests.Count; var duplicateTurn = Guid.NewGuid();
        using (var retried = await Send("pinned-retry", duplicateTurn))
            check("A fixed-model request completes after its bounded 507 retries", retried.IsSuccessStatusCode && (await retried.Content.ReadAsStringAsync()).Contains("response.completed"));
        var retries = upstream.Requests.Skip(start).Where(request => request.Method == "POST").ToArray();
        check("All retry attempts retain the original model route and credential after selection/key edits", retries.Length == 4 && retries.All(request => request.Host == "a.example.invalid" && request.Token == "fictional-router-key-a" && JsonNode.Parse(request.Body!)!["model"]!.GetValue<string>() == "shared-model"));
        start = upstream.Requests.Count;
        using (var duplicate = await Send("pinned-retry", duplicateTurn))
            check("A client duplicate cannot retarget the same virtual request to B", duplicate.StatusCode == HttpStatusCode.BadRequest && upstream.Requests.Skip(start).All(request => request.Method != "POST"));
        using (var next = await Send("new-after-retry")) await next.Content.ReadAsStringAsync();
        check("The following new request uses the saved B selection", upstream.Requests.Last().Host == "b.example.invalid" && upstream.Requests.Last().Token == "fictional-router-key-b");
        check("Selecting real models never rewrites the fixed model catalog", await File.ReadAllTextAsync(catalog) == "fictional-unchanged-catalog");
        start = upstream.Requests.Count;
        var image = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "input_image", ["image_url"] = "data:image/png;base64,ZmFrZQ==" }) });
        using (var unsupported = await Send("image-unsupported", input: image))
            check("A text-only selected model refuses images without transmitting or removing them", unsupported.StatusCode == HttpStatusCode.BadRequest && upstream.Requests.Skip(start).All(request => request.Method != "POST"));
        var savedChoices = manager.Settings.OpenAiModels.ToArray(); upstream.InvalidNativeLabel = true;
        using (var malformed = await client.GetAsync(manager.BaseUrl + "/models?client_version=0.167.0"))
            check("Malformed discovery cannot persist an unreadable routing cache or lose saved providers", !malformed.IsSuccessStatusCode && new ModelProviderStore(directory).Read().OpenAiModels.SequenceEqual(savedChoices) && manager.Settings.Providers.Count == 2);
        upstream.InvalidNativeLabel = false;
        using (var response = await client.GetAsync(manager.BaseUrl + "/models?client_version=0.167.0")) response.EnsureSuccessStatusCode();
        check("Switch choices contain only registered custom models after OpenAI discovery", manager.RoutingChoices.Select(choice => choice.Id).SequenceEqual(new[] { aliasA, aliasB }));
        refused = false;
        try { manager.SelectRoute("fixture-openai"); } catch (InvalidOperationException) { refused = true; }
        check("Native OpenAI selection belongs to Codex and cannot replace the saved custom route", refused && manager.Settings.SelectedRoute == aliasB);
        using (var native = await client.PostAsync(manager.BaseUrl + "/responses", new StringContent("{\"model\":\"fixture-openai\",\"stream\":true,\"input\":[]}", Encoding.UTF8, "application/json")))
            native.EnsureSuccessStatusCode();
        check("Native OpenAI requests keep their own model and authentication independent of the Switch choice", upstream.Requests.Last().Host == "chatgpt.com" && upstream.Requests.Last().Token == "fictional-router-token" && upstream.Requests.Last().Account == "fictional-router-account" && JsonNode.Parse(upstream.Requests.Last().Body!)!["model"]!.GetValue<string>() == "fixture-openai" && manager.Settings.SelectedRoute == aliasB);
        manager.SelectRoute(aliasB); await manager.RemoveAsync(b.Id, CancellationToken.None); start = upstream.Requests.Count;
        using (var removed = await Send("removed-selection"))
            check("Deleting the selected provider refuses explicitly without a fallback", removed.StatusCode == HttpStatusCode.BadRequest && manager.Settings.SelectedRoute == aliasB && upstream.Requests.Skip(start).All(request => request.Method != "POST"));
        check("Automatic continuation refuses a removed virtual selection", !StreamRecoveryPolicy.RegisteredModel(ModelRouting.Model, manager.Settings));
        manager.SelectRoute(aliasA);
        check("Automatic continuation recognizes a registered virtual selection", StreamRecoveryPolicy.RegisteredModel(ModelRouting.Model, manager.Settings));
    }

    private sealed class GatedUpload : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes("{\"model\":\"codex-switch\",\"stream\":true,\"input\":[{\"role\":\"user\",\"content\":\"fictional delayed upload\"}]}");
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GatedUpload() => Headers.ContentType = new MediaTypeHeaderValue("application/json");
        protected override bool TryComputeLength(out long length) { length = _bytes.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellation)
        {
            var split = _bytes.Length / 2;
            await stream.WriteAsync(_bytes.AsMemory(0, split), cancellation); await stream.FlushAsync(cancellation); Started.TrySetResult();
            await Continue.Task.WaitAsync(cancellation); await stream.WriteAsync(_bytes.AsMemory(split), cancellation);
        }
    }
}
