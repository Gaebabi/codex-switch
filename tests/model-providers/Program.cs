using CodexAccountDesk;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var checks = new List<string>();
void Check(string name, bool value) { if (!value) throw new Exception(name); checks.Add(name); }
Check("Full chat endpoint normalizes to the API base URL", ProviderCatalog.Endpoint("https://openrouter.ai/api/v1/chat/completions").AbsoluteUri == "https://openrouter.ai/api/v1/");
Check("Responses and model-list endpoints normalize without changing their authority", ProviderCatalog.Endpoint("https://example.invalid/proxy/v1/responses/").AbsoluteUri == "https://example.invalid/proxy/v1/" && ProviderCatalog.Endpoint("https://example.invalid/v1/models").AbsoluteUri == "https://example.invalid/v1/");
using (var source = new MemoryStream(Encoding.UTF8.GetBytes("12345678")))
using (var exact = await GatewayRequestBody.ReadAsync(source, -1, 8, new GatewayBodyBudget(), true, CancellationToken.None))
    Check("An unknown-length body at the exact bound is retained without closing its source", exact.Bytes.Length == 8 && source.CanRead && Encoding.UTF8.GetString(exact.Bytes.Span) == "12345678");
using (var source = new MemoryStream(new byte[9]))
{
    var oversized = false;
    try { using var unexpected = await GatewayRequestBody.ReadAsync(source, -1, 8, new GatewayBodyBudget(), true, CancellationToken.None); }
    catch (InvalidDataException) { oversized = true; }
    Check("An unknown-length body is rejected at the first byte above its bound", oversized && source.Position == 9);
}
using (var source = new MemoryStream(new byte[9]))
{
    var oversized = false;
    try { using var unexpected = await GatewayRequestBody.ReadAsync(source, 9, 8, new GatewayBodyBudget(), true, CancellationToken.None); }
    catch (InvalidDataException) { oversized = true; }
    Check("An oversized declared body is rejected without reading its source", oversized && source.Position == 0);
}
using (var cancelled = new CancellationTokenSource())
using (var source = new MemoryStream(new byte[8]))
{
    cancelled.Cancel(); var stopped = false;
    try { using var unexpected = await GatewayRequestBody.ReadAsync(source, 8, 8, new GatewayBodyBudget(), true, cancelled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Check("Request buffering honors cancellation", stopped);
}
async Task Refuses(string name, Func<Task> action)
{ try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException) { Check(name, true); return; } throw new Exception(name); }
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexSwitchModelProviders", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
if (args.Contains("--reasoning-only"))
{
    try
    {
        await ReasoningControlContracts.Run(root, Check);
        if (args.Length == 3 && args[1] == "--codex") await ReasoningNativeContracts.Run(args[2], root, Check);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Native reasoning metadata and exact selected-strength forwarding; fictional providers." }));
    }
    finally { Directory.Delete(root, true); }
    return;
}
if (args.Contains("--history-only"))
{
    try
    {
        await HistoryTransferContracts.Run(root, Check);
        if (args.Length == 3 && args[1] == "--codex") await HistoryNativeContracts.Run(args[2], root, Check);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Protected portable history and source-only summary contracts; disposable conversations." }));
    }
    finally { Directory.Delete(root, true); }
    return;
}
if (args.Length == 2 && args[0] == "--desktop-profiles-only")
{
    try
    {
        await DesktopProfileContracts.Run(args[1], root, Check);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Isolated native desktop-profile contracts with fictional upstreams." }));
    }
    finally { Directory.Delete(root, true); }
    return;
}
if (args.Contains("--retry-only"))
{
    try
    {
        await GatewayRetryContracts.Run(root, Check);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Isolated HTTP 507 recovery; no external inference or owner home." }));
    }
    finally { Directory.Delete(root, true); }
    return;
}
if (args.Contains("--stress"))
{
    try
    {
        await GatewayLoadContracts.Run(root, Check, true);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Isolated generated-byte gateway stress; no external inference or owner home." }));
    }
    finally { Directory.Delete(root, true); }
    return;
}
var nativeHome = Path.Combine(root, "native-home"); Directory.CreateDirectory(nativeHome);
var handler = new Upstreams();
GatewayIdentity? Authenticate(string? auth, string? account) => GatewayIdentity.Authenticate(auth, account, () => "{\"tokens\":{\"access_token\":\"fictional-openai-token\",\"account_id\":\"fictional-account\"}}");
try
{
    await using var manager = new ModelProviderManager(Path.Combine(root, "providers"), Authenticate, new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });
    var a = await manager.SaveAsync(null, "Provider A", "https://a.example.invalid/v1", "fictional-key-a", CancellationToken.None);
    var b = await manager.SaveAsync(null, "Provider B", "https://b.example.invalid/v1", "fictional-key-b", CancellationToken.None);
    var aliasA = a.Models.Single().Alias(a.Id); var aliasB = b.Models.Single().Alias(b.Id);
    Check("Equal upstream model IDs have distinct stable aliases", aliasA != aliasB);
    Check("Unset reasoning options preserve the existing model document shape", !JsonSerializer.Serialize(a.Models.Single()).Contains("ReasoningOverride"));
    var encrypted = await File.ReadAllBytesAsync(Path.Combine(root, "providers", "model-providers.vault"));
    Check("Stored keys and provider names are encrypted", !Encoding.UTF8.GetString(encrypted).Contains("fictional-key-a") && !Encoding.UTF8.GetString(encrypted).Contains("Provider A"));
    await manager.SaveAsync(a.Id, "Provider A edited", a.BaseUrl, "", CancellationToken.None);
    Check("Empty edit retains the key and alias", manager.Settings.Providers.Single(p => p.Id == a.Id).ApiKey == "fictional-key-a" && manager.Settings.Providers.Single(p => p.Id == a.Id).Models.Single().Alias(a.Id) == aliasA);
    await Refuses("Custom providers cannot impersonate the OpenAI display name", () => manager.SaveAsync(null, "OpenAI", "https://a.example.invalid/v1", "fictional-key", CancellationToken.None));
    await Refuses("Provider display names remain distinguishable", () => manager.SaveAsync(null, "Provider A edited", "https://a.example.invalid/v1", "fictional-key", CancellationToken.None));
    await Refuses("Changing destination requires a replacement key", () => manager.SaveAsync(a.Id, a.Name, "https://other.example.invalid/v1", "", CancellationToken.None));
    await Refuses("Remote HTTP refuses before transmitting a key", () => manager.SaveAsync(null, "Unsafe", "http://example.invalid/v1", "fictional-key", CancellationToken.None));
    await Refuses("Credential-bearing URLs refuse", () => manager.SaveAsync(null, "Unsafe", "https://user:pass@example.invalid/v1", "fictional-key", CancellationToken.None));
    await Refuses("Redirected discovery refuses", () => manager.SaveAsync(null, "Redirect", "https://redirect.example.invalid/v1", "fictional-key", CancellationToken.None));
    await Refuses("Unknown selected model IDs refuse without replacing a provider", () => manager.SaveAsync(a.Id, a.Name, a.BaseUrl, "", CancellationToken.None, new[] { "not-returned" }));
    await manager.SaveAsync(b.Id, b.Name, b.BaseUrl, "", CancellationToken.None, new[] { "shared-model" });
    Check("Explicit model selection is retained", manager.Settings.Providers.Single(p => p.Id == b.Id).IncludedModelIds!.SequenceEqual(new[] { "shared-model" }));
    Check("Failed edits preserve both providers", manager.Settings.Providers.Count == 2);
    manager.Start();
    using var client = ProviderCatalog.CreateHttpClient(); client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-openai-token");
    client.DefaultRequestHeaders.Add("ChatGPT-Account-ID", "fictional-account");
    async Task<HttpResponseMessage> Send(string model, string session, JsonArray? input = null, string? previous = null, string? effort = null, Guid? turn = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses");
        request.Headers.Add("session_id", session);
        var body = new JsonObject { ["model"] = model, ["stream"] = true, ["input"] = input ?? new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "fixture" }), ["previous_response_id"] = previous, ["reasoning"] = effort == null ? null : new JsonObject { ["effort"] = effort, ["summary"] = "auto" } };
        if (turn != null) body["client_metadata"] = new JsonObject { ["x-codex-turn-metadata"] = JsonSerializer.Serialize(new { turn_id = turn }) };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
    using (var catalog = await client.GetAsync(manager.BaseUrl + "/models?client_version=0.163.0"))
    {
        var models = JsonNode.Parse(await catalog.Content.ReadAsStringAsync())!["models"]!.AsArray();
        Check("Catalog retains OpenAI and both custom routes", models.Count == 3 && models.Any(m => m!["slug"]!.GetValue<string>() == "fixture-openai") && models.Any(m => m!["slug"]!.GetValue<string>() == aliasA));
        Check("Picker preserves original OpenAI names and labels custom providers", models.Single(m => m!["slug"]!.GetValue<string>() == "fixture-openai")!["display_name"]!.GetValue<string>() == "fixture-openai" && models.Single(m => m!["slug"]!.GetValue<string>() == aliasA)!["display_name"]!.GetValue<string>() == "shared-model (Provider A edited)" && models.Single(m => m!["slug"]!.GetValue<string>() == aliasB)!["display_name"]!.GetValue<string>() == "shared-model (Provider B)");
    }
    var identifiedTurn = Guid.NewGuid();
    using (var result = await Send(aliasA, "identified-turn", turn: identifiedTurn)) { await result.Content.ReadAsStringAsync(); Check("An identified native turn request reaches its provider", result.IsSuccessStatusCode); }
    var beforeRepeatedTurn = handler.Requests.Count(r => r.Method == "POST");
    using (var result = await Send(aliasA, "identified-turn", turn: identifiedTurn))
        Check("An automatic repeat of the same turn input cannot duplicate upstream work", result.StatusCode == HttpStatusCode.BadRequest && handler.Requests.Count(r => r.Method == "POST") == beforeRepeatedTurn);
    using (var result = await Send(aliasA, "identified-turn", turn: Guid.NewGuid())) { await result.Content.ReadAsStringAsync(); Check("A new user turn may send the same input", result.IsSuccessStatusCode); }
    using (var result = await Send(aliasA, "identified-turn", new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "tool follow-up" }), turn: identifiedTurn)) { await result.Content.ReadAsStringAsync(); Check("Changed full-input tool follow-ups remain usable within a turn", result.IsSuccessStatusCode); }
    var beforeUnknownEffort = handler.Requests.Count(r => r.Method == "POST");
    using (var result = await Send(aliasA, "unknown-effort", effort: "high")) Check("Unknown reasoning support refuses a chosen strength before inference", result.StatusCode == HttpStatusCode.BadRequest && handler.Requests.Count(r => r.Method == "POST") == beforeUnknownEffort);
    handler.MatchingReasoning = true;
    using (var catalog = await client.GetAsync(manager.BaseUrl + "/models?client_version=0.163.0"))
    {
        var models = JsonNode.Parse(await catalog.Content.ReadAsStringAsync())!["models"]!.AsArray();
        var matched = models.Single(m => m!["slug"]!.GetValue<string>() == aliasA)!;
        Check("Exact OpenAI model IDs supply the custom reasoning control without changing text-only capability", matched["default_reasoning_level"]!.GetValue<string>() == "medium" && matched["supported_reasoning_levels"]!.AsArray().Select(l => l!["effort"]!.GetValue<string>()).SequenceEqual(new[] { "low", "medium", "high", "xhigh" }) && matched["input_modalities"]!.AsArray().Count == 1);
    }
    foreach (var effort in new[] { "low", "medium", "high", "xhigh" })
    {
        using var result = await Send(aliasA, "exact-reasoning", effort: effort); await result.Content.ReadAsStringAsync();
        Check("Exact-match custom route forwards selected reasoning: " + effort, result.IsSuccessStatusCode && JsonNode.Parse(handler.Requests.Last().Body!)!["reasoning"]!["effort"]!.GetValue<string>() == effort);
    }
    var overrideOptions = new ProviderReasoningOptions { Levels = new() { "low", "medium", "high" }, Default = "high" };
    await manager.SaveReasoningAsync(a.Id, "shared-model", overrideOptions, CancellationToken.None);
    overrideOptions.Levels.Clear();
    await manager.SaveAsync(a.Id, "Provider A edited", a.BaseUrl, "", CancellationToken.None);
    Check("Model reasoning overrides are snapshotted and survive rediscovery", manager.Settings.Providers.Single(p => p.Id == a.Id).Models.Single().ReasoningOverride!.Levels.Count == 3);
    await Refuses("Invalid reasoning defaults cannot replace saved model settings", () => manager.SaveReasoningAsync(a.Id, "shared-model", new ProviderReasoningOptions { Levels = new() { "low" }, Default = "high" }, CancellationToken.None));
    var postsBeforeUnsupported = handler.Requests.Count(r => r.Method == "POST");
    using (var result = await Send(aliasA, "unsupported-reasoning", effort: "xhigh")) Check("Configured subset refuses unsupported reasoning without inference", result.StatusCode == HttpStatusCode.BadRequest && handler.Requests.Count(r => r.Method == "POST") == postsBeforeUnsupported);
    using (var result = await Send(aliasA, "manual-reasoning", effort: "high"))
    {
        await result.Content.ReadAsStringAsync();
        Check("Manual reasoning forwards effort and removes only an unsupported summary option", result.IsSuccessStatusCode && JsonNode.Parse(handler.Requests.Last().Body!)!["reasoning"]!.AsObject().Count == 1);
    }
    var reasoningReload = new ModelProviderStore(Path.Combine(root, "providers")).Read().Providers.Single(p => p.Id == a.Id).Models.Single().ReasoningOverride;
    Check("Per-model reasoning levels and default survive encrypted reload", reasoningReload?.Default == "high" && reasoningReload.Levels.Count == 3);
    await manager.SaveReasoningAsync(a.Id, "shared-model", null, CancellationToken.None);
    Check("Disabling a reasoning override restores automatic metadata", manager.Settings.Providers.Single(p => p.Id == a.Id).Models.Single().ReasoningOverride == null);
    handler.MatchingReasoning = false;
    using (var refreshed = await client.GetAsync(manager.BaseUrl + "/models?client_version=0.163.0")) { }
    using (var anonymous = new HttpClient())
    using (var result = await anonymous.GetAsync(manager.BaseUrl + "/models?client_version=0.163.0")) Check("Anonymous loopback requests refuse", result.StatusCode == HttpStatusCode.Unauthorized);
    var routesBeforeImages = JsonSerializer.Serialize(manager.Settings.ConversationRoutes);
    var fictionalImage = "{\"model\":\"gpt-image-fixture\",\"prompt\":\"허구 이미지\",\"background\":\"transparent\",\"images\":[{\"image_url\":\"data:image/png;base64,Zml4dHVyZQ==\"}]}";
    foreach (var endpoint in new[] { "images/generations", "images/edits" })
    {
        using var result = await client.PostAsync(manager.BaseUrl + "/" + endpoint, new StringContent(fictionalImage, Encoding.UTF8, "application/json"));
        Check("Built-in image route completes: " + endpoint, result.IsSuccessStatusCode && (await result.Content.ReadAsStringAsync()).Contains("fixture-image"));
        var forwarded = handler.Requests.Last();
        Check("Built-in images retain original login, exact body and endpoint: " + endpoint, forwarded.Host == "chatgpt.com" && forwarded.Path == "/backend-api/codex/" + endpoint && forwarded.Token == "fictional-openai-token" && forwarded.Account == "fictional-account" && forwarded.Body == fictionalImage);
        Check("Built-in image request IDs survive forwarding: " + endpoint, result.Headers.GetValues("x-codex-imagegen-request-id").Single() == "fixture-image-request");
    }
    Check("Images leave conversation provider provenance unchanged", JsonSerializer.Serialize(manager.Settings.ConversationRoutes) == routesBeforeImages);
    using (var anonymous = new HttpClient())
    using (var result = await anonymous.PostAsync(manager.BaseUrl + "/images/generations", new StringContent(fictionalImage, Encoding.UTF8, "application/json")))
        Check("Anonymous image requests are refused", result.StatusCode == HttpStatusCode.Unauthorized);
    using (var browser = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/images/edits"))
    {
        browser.Headers.Add("Origin", "https://browser.example.invalid"); browser.Content = new StringContent(fictionalImage, Encoding.UTF8, "application/json");
        using var result = await client.SendAsync(browser); Check("Browser-origin image edits are refused", result.StatusCode == HttpStatusCode.Forbidden);
    }
    handler.ImageFailure = true;
    var beforeFailedImage = handler.Requests.Count;
    using (var result = await client.PostAsync(manager.BaseUrl + "/images/generations", new StringContent(fictionalImage, Encoding.UTF8, "application/json")))
        Check("Image failures are not retried or echoed with credentials", result.StatusCode == HttpStatusCode.TooManyRequests && handler.Requests.Count == beforeFailedImage + 1 && !(await result.Content.ReadAsStringAsync()).Contains("fictional-key"));
    handler.ImageFailure = false;
    var largeBody = "{\"model\":\"fixture-openai\",\"input\":\"" + new string('x', 65 * 1024 * 1024) + "\"}";
    using (var result = await client.PostAsync(manager.BaseUrl + "/responses", new StringContent(largeBody, Encoding.UTF8, "application/json")))
    {
        await result.Content.ReadAsStringAsync();
        Check("A full-input conversation above 64 MiB is forwarded unchanged", result.IsSuccessStatusCode && handler.Requests.Last().Body == largeBody);
    }
    using (var chunked = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses/compact"))
    {
        chunked.Headers.Add("session_id", "large-portable-compaction");
        chunked.Headers.TransferEncodingChunked = true;
        chunked.Content = new StringContent(largeBody, Encoding.UTF8, "application/json");
        using var result = await client.SendAsync(chunked); await result.Content.ReadAsStringAsync();
        Check("Chunked portable compaction above 64 MiB keeps the entire original input in the source summary request", result.IsSuccessStatusCode && JsonNode.Parse(handler.Requests.Last().Body!)!["input"]![0]!["content"]!.GetValue<string>() == JsonNode.Parse(largeBody)!["input"]!.GetValue<string>() && handler.Requests.Last().Path.EndsWith("/responses"));
    }
    using (var result = await client.PostAsync(manager.BaseUrl + "/images/edits", new StringContent(largeBody, Encoding.UTF8, "application/json")))
    {
        await result.Content.ReadAsStringAsync();
        Check("Image edit data above 64 MiB is retained by the same bounded gateway", result.IsSuccessStatusCode && handler.Requests.Last().Body == largeBody);
    }
    var pendingStreams = new List<(HttpResponseMessage Response, GatedStream Upstream)>();
    try
    {
        for (var i = 0; i < 2; i++)
        {
            handler.StreamNext = true; var slow = handler.Slow = new GatedStream();
            using var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses") { Content = new StringContent(largeBody, Encoding.UTF8, "application/json") };
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            pendingStreams.Add((response, slow));
        }
        using var following = await client.PostAsync(manager.BaseUrl + "/responses", new StringContent(largeBody, Encoding.UTF8, "application/json"));
        Check("Finished large uploads release admission while their response streams are still open", following.IsSuccessStatusCode && pendingStreams.All(p => p.Response.IsSuccessStatusCode && !p.Upstream.Finish.Task.IsCompleted));
    }
    finally
    {
        foreach (var pending in pendingStreams) { pending.Upstream.Finish.TrySetResult(); await pending.Response.Content.ReadAsStringAsync(); pending.Response.Dispose(); }
    }
    var beforeInvalidImage = handler.Requests.Count;
    foreach (var invalidJson in new[] { "{\"model\":\"fixture-openai\",\"model\":\"cs_missing\"}", "{\"model\":false}", "[1,2]", "{\"model\":\"fixture-openai\",\"input\":[],\"input\":[{\"type\":\"compaction\",\"encrypted_content\":\"opaque\"}]}" })
    {
        using var result = await client.PostAsync(manager.BaseUrl + "/responses", new StringContent(invalidJson, Encoding.UTF8, "application/json"));
        Check("Ambiguous or malformed request JSON refuses before upstream: " + invalidJson, result.StatusCode == HttpStatusCode.BadRequest && handler.Requests.Count == beforeInvalidImage);
    }
    using (var invalid = new StringContent("{}", Encoding.UTF8, "application/jsonp"))
    using (var result = await client.PostAsync(manager.BaseUrl + "/images/generations", invalid))
        Check("Invalid image media type reports 415 without contacting upstream", result.StatusCode == HttpStatusCode.UnsupportedMediaType && handler.Requests.Count == beforeInvalidImage);
    using (var oversized = new TcpClient())
    {
        var endpoint = new Uri(manager.BaseUrl!); await oversized.ConnectAsync(endpoint.Host, endpoint.Port);
        await oversized.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST " + endpoint.AbsolutePath + "/responses HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nAuthorization: Bearer fictional-openai-token\r\nChatGPT-Account-ID: fictional-account\r\nContent-Type: application/json\r\nContent-Length: " + (ModelGateway.MaximumRequestBytes + 1L) + "\r\nConnection: close\r\n\r\n"));
        using var reader = new StreamReader(oversized.GetStream());
        var refusal = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check("Oversized declared requests report the explicit 512 MiB bound before upstream", refusal.Contains("413") && refusal.Contains("512 MiB") && handler.Requests.Count == beforeInvalidImage);
    }
    // Incomplete uploads verify admission without allocating large test payloads.
    var pendingUploads = Enumerable.Range(0, 8).Select(_ => new TcpClient()).ToArray();
    try
    {
        var endpoint = new Uri(manager.BaseUrl!);
        foreach (var pending in pendingUploads)
        {
            await pending.ConnectAsync(endpoint.Host, endpoint.Port);
            await pending.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST " + endpoint.AbsolutePath + "/responses HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nAuthorization: Bearer fictional-openai-token\r\nChatGPT-Account-ID: fictional-account\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n1\r\n{\r\n"));
        }
        using (var small = await Send("fixture-openai", "large-admission-small"))
            Check("Small requests remain usable while eight unknown-length uploads are pending", small.IsSuccessStatusCode);
        using var third = new TcpClient(); await third.ConnectAsync(endpoint.Host, endpoint.Port);
        var beforeBusy = handler.Requests.Count;
        await third.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST " + endpoint.AbsolutePath + "/responses HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nAuthorization: Bearer fictional-openai-token\r\nChatGPT-Account-ID: fictional-account\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"));
        using var reader = new StreamReader(third.GetStream());
        var busy = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check("A ninth large upload refuses before buffering or contacting upstream", busy.Contains("503") && busy.Contains("Retry-After: 1") && handler.Requests.Count == beforeBusy);
    }
    finally { foreach (var pending in pendingUploads) pending.Dispose(); }
    await using (var api = new ModelProviderManager(Path.Combine(root, "api-images"), (authorization, account) => GatewayIdentity.Authenticate(authorization, account, () => "{\"OPENAI_API_KEY\":\"fictional-image-api-key\"}"), new HttpClient(handler)))
    {
        api.Start(); using var apiClient = new HttpClient(); apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-image-api-key");
        using var result = await apiClient.PostAsync(api.BaseUrl + "/images/edits", new StringContent(fictionalImage, Encoding.UTF8, "application/json"));
        Check("Explicit API-key login uses the OpenAI Images API with its own identity", result.IsSuccessStatusCode && handler.Requests.Last().Host == "api.openai.com" && handler.Requests.Last().Path == "/v1/images/edits" && handler.Requests.Last().Token == "fictional-image-api-key" && handler.Requests.Last().Account == null);
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get, manager.BaseUrl + "/models?client_version=0.163.0"))
    { request.Headers.Add("Origin", "https://browser.example.invalid"); using var result = await client.SendAsync(request); Check("Browser-origin requests refuse", result.StatusCode == HttpStatusCode.Forbidden); }
    using (var result = await Send("cs_missing", "missing")) Check("Removed or unknown aliases never fall back to OpenAI", result.StatusCode == HttpStatusCode.NotFound);
    var unknownBefore = handler.Requests.Count(r => r.Method == "POST");
    using (var result = await Send("unregistered-custom-model", "unknown-raw")) Check("Unregistered raw model IDs do not fall through to OpenAI", result.StatusCode == HttpStatusCode.NotFound);
    Check("Unknown raw routes never transmit the conversation to an upstream", handler.Requests.Count(r => r.Method == "POST") == unknownBefore);
    using (var result = await Send(aliasA, "stateful", previous: "another-provider-response")) Check("Stateful provider references refuse without replay loss", result.StatusCode == HttpStatusCode.BadRequest);
    using (var result = await Send(aliasA, "compacted", new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = "opaque-summary" })))
        Check("Encrypted compaction refuses instead of losing conversation context", result.StatusCode == HttpStatusCode.BadRequest);
    var compactedSource = new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = "source-opaque-summary" });
    using (var result = await Send("fixture-openai", "handoff-source", (JsonArray)compactedSource.DeepClone()))
        Check("The original provider accepts its existing encrypted compaction", result.IsSuccessStatusCode);
    var routesBeforeRejectedSwitch = JsonSerializer.Serialize(manager.Settings.ConversationRoutes);
    var generationsBeforeRejectedSwitch = handler.Requests.Count(r => r.Method == "POST");
    using (var result = await Send(aliasA, "handoff-source", (JsonArray)compactedSource.DeepClone()))
        Check("A cross-provider encrypted compaction refuses before inference", result.StatusCode == HttpStatusCode.BadRequest && handler.Requests.Count(r => r.Method == "POST") == generationsBeforeRejectedSwitch);
    Check("A refused provider switch cannot poison the original conversation route", JsonSerializer.Serialize(manager.Settings.ConversationRoutes) == routesBeforeRejectedSwitch);
    using (var result = await Send("fixture-openai", "handoff-source", (JsonArray)compactedSource.DeepClone()))
        Check("The original provider remains usable to prepare a text handoff after refusal", result.IsSuccessStatusCode && handler.Requests.Last().Body!.Contains("source-opaque-summary"));
    handler.FailNext = true;
    using (var result = await Send(aliasA, "handoff-source"))
        Check("An upstream refusal does not commit a provider transition", result.StatusCode == HttpStatusCode.TooManyRequests && JsonSerializer.Serialize(manager.Settings.ConversationRoutes) == routesBeforeRejectedSwitch);
    using (var result = await Send("fixture-openai", "handoff-source", (JsonArray)compactedSource.DeepClone()))
        Check("The source remains usable after an upstream failure", result.IsSuccessStatusCode);
    var handoff = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Portable text handoff: preserve task constraints; verified file state; next step: finish fixture." });
    using (var result = await Send(aliasA, "handoff-new-chat", (JsonArray)handoff.DeepClone()))
        Check("A new provider conversation accepts the complete plain-text handoff", result.IsSuccessStatusCode && handler.Requests.Last().Body!.Contains("preserve task constraints; verified file state; next step: finish fixture."));
    handoff.Add(new JsonObject { ["role"] = "user", ["content"] = "Continue from the verified next step." });
    using (var result = await Send(aliasA, "handoff-new-chat", (JsonArray)handoff.DeepClone()))
        Check("Following up on the new provider retains the handoff and next request", result.IsSuccessStatusCode && handler.Requests.Last().Body!.Contains("Portable text handoff") && handler.Requests.Last().Body!.Contains("Continue from the verified next step."));
    using (var result = await Send("fixture-openai", "", (JsonArray)compactedSource.DeepClone()))
        Check("Unknown conversation provenance still refuses opaque compaction", result.StatusCode == HttpStatusCode.BadRequest);
    var misleadingHandoff = (JsonArray)compactedSource.DeepClone();
    misleadingHandoff.Add(new JsonObject { ["role"] = "user", ["content"] = "This is a summary" });
    using (var result = await Send(aliasA, "handoff-still-opaque", misleadingHandoff))
    {
        var refusal = await result.Content.ReadAsStringAsync();
        Check("A summary label cannot bypass opaque-history protection", result.StatusCode == HttpStatusCode.BadRequest);
        Check("The refusal provides an actionable source-summary-new-chat recovery path", refusal.Contains("Models") && refusal.Contains("original provider") && refusal.Contains("new chat"));
    }
    handler.Slow = new GatedStream();
    handler.StreamNext = true;
    using (var result = await Send(aliasA, "streaming"))
    using (var reader = new StreamReader(await result.Content.ReadAsStreamAsync()))
    {
        var firstLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check("SSE is forwarded before upstream completion", firstLine!.Contains("response.output_text.delta") && !handler.Slow.Finish.Task.IsCompleted);
        handler.Slow.Finish.TrySetResult();
        Check("SSE terminal completion survives", (await reader.ReadToEndAsync()).Contains("response.completed"));
    }
    handler.FailNext = true;
    var failuresBefore = handler.Requests.Count;
    using (var result = await Send(aliasA, "upstream-failure"))
        Check("Upstream errors remain errors without exposing their echoed credential", result.StatusCode == HttpStatusCode.TooManyRequests && !(await result.Content.ReadAsStringAsync()).Contains("fictional-key"));
    Check("The gateway never retries a generation", handler.Requests.Count == failuresBefore + 1);
    var before = handler.Requests.Count;
    var simultaneous = await Task.WhenAll(Send(aliasA, "chat-a"), Send(aliasB, "chat-b"), Send("fixture-openai", "chat-openai"));
    foreach (var result in simultaneous) { Check("Concurrent Responses requests complete", result.IsSuccessStatusCode && (await result.Content.ReadAsStringAsync()).Contains("response.completed")); result.Dispose(); }
    var routed = handler.Requests.ToArray().Skip(before).Where(r => r.Method == "POST").ToArray();
    Check("Concurrent chats stay on distinct routes", routed.Any(r => r.Host == "a.example.invalid") && routed.Any(r => r.Host == "b.example.invalid") && routed.Any(r => r.Host == "chatgpt.com"));
    Check("Custom providers receive only their own keys", routed.Where(r => r.Host == "a.example.invalid").All(r => r.Token == "fictional-key-a" && r.Account == null) && routed.Where(r => r.Host == "b.example.invalid").All(r => r.Token == "fictional-key-b" && r.Account == null));
    Check("OpenAI retains original sign-in and account attribution", routed.Single(r => r.Host == "chatgpt.com").Token == "fictional-openai-token" && routed.Single(r => r.Host == "chatgpt.com").Account == "fictional-account");
    var unchanged = "{\"model\":\"fixture-openai\",\"input\":\"한글 직접 전달\"}";
    using (var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + "/responses"))
    {
        request.Headers.Add("session_id", "unchanged-native-body"); request.Content = new StringContent(unchanged, Encoding.UTF8, "application/json");
        using var result = await client.SendAsync(request); await result.Content.ReadAsStringAsync();
        Check("Unchanged OpenAI requests retain their original UTF-8 body bytes", handler.Requests.Last().Body == unchanged);
    }
    var toolHistory = new JsonArray(new JsonObject { ["type"] = "function_call", ["call_id"] = "fixture-call", ["name"] = "fixture_tool", ["arguments"] = "{}" }, new JsonObject { ["type"] = "function_call_output", ["call_id"] = "fixture-call", ["output"] = "fixture-result" }, new JsonObject { ["type"] = "reasoning", ["encrypted_content"] = "provider-opaque" });
    using (var result = await Send(aliasA, "chat-a", toolHistory)) { await result.Content.ReadAsStringAsync(); }
    var body = handler.Requests.Last().Body!;
    Check("Tool call identities and results survive cross-provider replay", body.Contains("fixture-call") && body.Contains("fixture-result") && !body.Contains("provider-opaque"));
    using (var result = await Send("fixture-openai", "chat-a", (JsonArray)toolHistory.DeepClone())) { await result.Content.ReadAsStringAsync(); }
    Check("Returning to OpenAI still excludes other-provider opaque history", !handler.Requests.Last().Body!.Contains("provider-opaque"));
    using (var result = await Send("fixture-openai", "new-native", (JsonArray)toolHistory.DeepClone())) { await result.Content.ReadAsStringAsync(); }
    Check("Unchanged native OpenAI conversation retains its own reasoning", handler.Requests.Last().Body!.Contains("provider-opaque"));
    handler.BlockNext = true;
    var inFlight = Send(aliasA, "inflight"); await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await manager.SaveAsync(a.Id, a.Name, a.BaseUrl, "fictional-key-a-new", CancellationToken.None);
    handler.Release.TrySetResult(); using (var result = await inFlight) await result.Content.ReadAsStringAsync();
    Check("An in-flight request keeps the key it started with", handler.BlockedKey == "fictional-key-a");
    using (var result = await Send(aliasA, "next-request")) await result.Content.ReadAsStringAsync();
    Check("Key edits apply to the next request without restarting the listener", handler.Requests.Last().Token == "fictional-key-a-new");
    Check("Current OpenAI token authenticates and wrong account refuses", Authenticate("Bearer fictional-openai-token", "fictional-account") != null && Authenticate("Bearer fictional-openai-token", "other-account") == null);
    Check("Blank API keys cannot authenticate an empty bearer", GatewayIdentity.Authenticate("Bearer ", null, () => "{\"OPENAI_API_KEY\":\"\"}") == null);
    Check("Blank API key fields do not shadow a valid ChatGPT token", GatewayIdentity.Authenticate("Bearer fictional-openai-token", null, () => "{\"OPENAI_API_KEY\":\"\",\"tokens\":{\"access_token\":\"fictional-openai-token\"}}") != null);
    var reloaded = new ModelProviderStore(Path.Combine(root, "providers")).Read();
    Check("Encrypted provider updates and mixed-route state survive reload", reloaded.Providers.Single(p => p.Id == a.Id).ApiKey == "fictional-key-a-new" && reloaded.ConversationRoutes.Values.Any(route => route.Mixed));
    if (args.Length == 2 && args[0] == "--codex")
    {
        await NativeClient.Run(args[1], nativeHome, manager, handler, Check);
        await DesktopProfileContracts.Run(args[1], root, Check);
        await HistoryNativeContracts.Run(args[1], root, Check);
        await ReasoningNativeContracts.Run(args[1], root, Check);
    }
    await manager.RemoveAsync(b.Id, CancellationToken.None);
    using (var result = await Send(aliasB, "removed")) Check("Removing a provider fails stale choices explicitly", result.StatusCode == HttpStatusCode.NotFound);
    var cancellationHandler = new Upstreams { BlockDiscovery = true };
    var cancelPath = Path.Combine(root, "cancelled-save");
    var cancellationManager = new ModelProviderManager(cancelPath, Authenticate, new HttpClient(cancellationHandler));
    var saving = cancellationManager.SaveAsync(null, "Cancelled", "https://a.example.invalid/v1", "fictional-cancelled-key", CancellationToken.None);
    await cancellationHandler.DiscoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cancellationManager.DisposeAsync();
    var cancelled = false;
    try { await saving; } catch (OperationCanceledException) { cancelled = true; }
    Check("Shutdown cancels and drains discovery before disposing state without saving a partial key", cancelled && !File.Exists(Path.Combine(cancelPath, "model-providers.vault")));
    await GatewayLoadContracts.Run(root, Check, args.Contains("--stress"));
    await GatewayRetryContracts.Run(root, Check);
    await ProviderEditorContracts.Run(root, Check);
    await ModelRoutingContracts.Run(root, Check);
    await IsolatedProfileContracts.Run(root, Check);
    await HistoryTransferContracts.Run(root, Check);
    await ReasoningControlContracts.Run(root, Check);
    Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, nativeClient = args.Contains("--codex"), framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, scope = "Disposable encrypted storage, loopback gateway and fictional upstreams; no owner home, account or desktop." }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    if (Path.GetFileName(root).Length == 32 && Guid.TryParseExact(Path.GetFileName(root), "N", out _) && Directory.Exists(root)) Directory.Delete(root, true);
}

internal sealed record Captured(string Method, string Host, string? Token, string? Account, string? Body, string Path);
internal sealed class Upstreams : HttpMessageHandler
{
    internal ConcurrentQueue<Captured> Requests { get; } = new();
    internal bool BlockNext;
    internal bool StreamNext;
    internal bool FailNext;
    internal bool BlockDiscovery;
    internal bool MatchingReasoning;
    internal bool ImageFailure;
    internal int StorageFailures;
    internal int StreamFailures;
    internal bool InvalidNativeLabel;
    internal JsonObject? NativeMetadata;
    internal Func<Task>? BeforeNextResponse;
    internal TaskCompletionSource DiscoveryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal GatedStream? Slow;
    internal string? BlockedKey;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellation);
        Requests.Enqueue(new Captured(request.Method.Method, request.RequestUri!.Host, request.Headers.Authorization?.Parameter,
            request.Headers.TryGetValues("ChatGPT-Account-ID", out var ids) ? ids.Single() : null, body, request.RequestUri.AbsolutePath));
        if (request.RequestUri.AbsolutePath.Contains("/images/", StringComparison.Ordinal))
        {
            if (ImageFailure) return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("fictional-key-must-not-be-echoed") };
            var image = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"b64_json\":\"fixture-image\"}]}", Encoding.UTF8, "application/json") };
            image.Headers.Add("x-codex-imagegen-request-id", "fixture-image-request"); return image;
        }
        if (request.RequestUri.Host == "redirect.example.invalid") return new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://other.example.invalid/models") } };
        if (request.Method == HttpMethod.Get)
        {
            if (BlockDiscovery) { DiscoveryEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); }
            var data = request.RequestUri.Host == "chatgpt.com" ? new JsonObject { ["models"] = new JsonArray(NativeMetadata?.DeepClone() ?? ProviderCatalog.Generic("fixture-openai")) } : new JsonObject { ["data"] = new JsonArray(new JsonObject { ["id"] = "shared-model" }) };
            if (InvalidNativeLabel && request.RequestUri.Host == "chatgpt.com") data["models"]![0]!["display_name"] = new string('x', 513);
            if (MatchingReasoning && request.RequestUri.Host == "chatgpt.com")
            {
                var matchingModel = ProviderCatalog.Generic("shared-model");
                matchingModel["default_reasoning_level"] = "medium";
                matchingModel["supported_reasoning_levels"] = new JsonArray(new[] { "low", "medium", "high", "xhigh" }.Select(level => (JsonNode)new JsonObject { ["effort"] = level, ["description"] = level }).ToArray());
                data["models"]!.AsArray().Add(matchingModel);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json") };
        }
        if (BlockNext)
        { BlockNext = false; BlockedKey = request.Headers.Authorization?.Parameter; Entered.TrySetResult(); await Release.Task.WaitAsync(cancellation); }
        if (BeforeNextResponse != null) { var action = BeforeNextResponse; BeforeNextResponse = null; await action(); }
        if (FailNext) { FailNext = false; return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("fictional-key-a-new") }; }
        if (StorageFailures > 0) { StorageFailures--; return new((HttpStatusCode)507) { Content = new StringContent("fictional-key-must-not-be-echoed") }; }
        if (StreamFailures > 0)
        {
            StreamFailures--;
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.failed\",\"response\":{\"id\":\"fixture-stream-error\",\"status\":\"failed\",\"error\":{\"code\":\"fixture_stream_failure\",\"message\":\"An error occurred while processing your request. Request ID fixture-stream.\"}}}\n\n", Encoding.UTF8, "text/event-stream") };
        }
        if (StreamNext)
        {
            StreamNext = false; var content = new StreamContent(Slow!); content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return new(HttpStatusCode.OK) { Content = content };
        }
        var model = JsonNode.Parse(body!)!["model"]!.GetValue<string>();
        if (NativeMetadata != null && request.RequestUri.Host == "chatgpt.com" && JsonNode.Parse(body!)?["reasoning"]?["effort"]?.GetValue<string>() is string effort &&
            !NativeMetadata["supported_reasoning_levels"]!.AsArray().Any(level => level!["effort"]!.GetValue<string>() == effort))
            return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"message\":\"Unsupported reasoning effort\",\"param\":\"reasoning.effort\"}}", Encoding.UTF8, "application/json") };
        return new(HttpStatusCode.OK) { Content = new StringContent(Stream(model), Encoding.UTF8, "text/event-stream") };
    }
    internal static string Stream(string model)
    {
        var id = "fixture_" + Guid.NewGuid().ToString("N"); var itemId = "message_" + Guid.NewGuid().ToString("N");
        var part = new { type = "output_text", text = "fixture-ok", annotations = Array.Empty<object>() };
        var item = new { id = itemId, type = "message", role = "assistant", status = "completed", content = new[] { part } };
        var response = new { id, @object = "response", status = "completed", model, output = new[] { item }, usage = new { input_tokens = 1, output_tokens = 1, total_tokens = 2, input_tokens_details = new { cached_tokens = 0 }, output_tokens_details = new { reasoning_tokens = 0 } } };
        string Event(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
        return Event(new { type = "response.created", response = new { id, status = "in_progress", model, output = Array.Empty<object>() } }) +
            Event(new { type = "response.output_item.added", output_index = 0, item = new { id = itemId, type = "message", role = "assistant", status = "in_progress", content = Array.Empty<object>() } }) +
            Event(new { type = "response.content_part.added", output_index = 0, content_index = 0, item_id = itemId, part = new { type = "output_text", text = "", annotations = Array.Empty<object>() } }) +
            Event(new { type = "response.output_text.delta", output_index = 0, content_index = 0, item_id = itemId, delta = "fixture-ok" }) +
            Event(new { type = "response.output_item.done", output_index = 0, item }) + Event(new { type = "response.completed", response });
    }
}

internal static class NativeClient
{
    internal static Task Run(string executable, string home, ModelProviderManager manager, Upstreams upstreams, Action<string, bool> check)
        => IsolatedNativeClientContracts.Run(executable, home, manager, upstreams, check);
}
