using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class HistoryTransferContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "history-transfer");
        var upstream = new HistoryUpstream();
        var identity = new GatewayIdentity("fictional-history-token", "fictional-history-account", false);
        bool signedIn = true;
        GatewayIdentity? Authenticate(string? auth, string? account) => signedIn && auth == "Bearer fictional-history-token" ? identity : null;
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream));
        var a = await manager.SaveAsync(null, "History A", "https://a.example.invalid/v1", "fictional-history-a", CancellationToken.None);
        var b = await manager.SaveAsync(null, "History B", "https://b.example.invalid/v1", "fictional-history-b", CancellationToken.None);
        var aliasA = a.Models.Single().Alias(a.Id); var aliasB = b.Models.Single().Alias(b.Id);
        manager.SelectRoute(aliasA); manager.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        client.DefaultRequestHeaders.Add("version", "0.167.0");
        using (var handshake = new HttpRequestMessage(HttpMethod.Get, manager.BaseUrl + "/responses"))
        {
            handshake.Headers.Add("Connection", "Upgrade"); handshake.Headers.Add("Upgrade", "websocket");
            handshake.Headers.Add("Sec-WebSocket-Version", "13"); handshake.Headers.Add("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
            using var negotiation = await client.SendAsync(handshake);
            check("WebSocket negotiation requests native HTTP fallback without sending inference", (int)negotiation.StatusCode == 426 && upstream.Requests.Count == 0);
        }
        using (var ordinaryGet = await client.GetAsync(manager.BaseUrl + "/responses"))
            check("A non-WebSocket GET remains an unsupported endpoint", ordinaryGet.StatusCode == HttpStatusCode.NotFound);
        async Task<(HttpStatusCode Code, string Body)> Send(string session, JsonArray input, bool compact = false, string? model = null, Guid? turn = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, manager.BaseUrl + (compact ? "/responses/compact" : "/responses"));
            request.Headers.Add("session_id", session);
            var body = new JsonObject { ["model"] = model ?? ModelRouting.Model, ["stream"] = true, ["input"] = input.DeepClone() };
            if (turn != null) body["client_metadata"] = new JsonObject { ["x-codex-turn-metadata"] = JsonSerializer.Serialize(new { turn_id = turn }) };
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        JsonArray Conversation() => new(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = "Keep the exact marker HISTORY_MARKER_73; approval applies only to local source edits." },
            new JsonObject { ["type"] = "function_call", ["call_id"] = "fictional-call", ["name"] = "read_file", ["arguments"] = "{}" },
            new JsonObject { ["type"] = "function_call_output", ["call_id"] = "fictional-call", ["output"] = "Observed source fact: file revision 73." });
        upstream.SummaryMediaType = null;
        var compacted = await Send("portable", Conversation(), true);
        check("Portable compaction succeeds through a source-only tools-disabled summary", compacted.Code == HttpStatusCode.OK && upstream.Summaries == 1 && upstream.Requests.Last().Host == "a.example.invalid" && upstream.Requests.Last().Body["tools"]!.AsArray().Count == 0 && upstream.Requests.Last().Body["tool_choice"]!.GetValue<string>() == "none");
        check("A requested source event stream is parsed without a Content-Type header", compacted.Code == HttpStatusCode.OK && upstream.Summaries == 1);
        upstream.SummaryMediaType = "text/event-stream";
        var output = JsonNode.Parse(compacted.Body)!["output"]!.AsArray();
        var token = output.Single()!["encrypted_content"]!.GetValue<string>();
        check("The client receives only a locally-owned opaque checkpoint token", token.StartsWith(GatewayHistoryStore.Prefix) && !compacted.Body.Contains("HISTORY_MARKER_73"));
        var protectedBytes = File.ReadAllBytes(Path.Combine(directory, "model-history.vault"));
        check("Portable summaries and authorization details are encrypted at rest", !Encoding.UTF8.GetString(protectedBytes).Contains("HISTORY_MARKER_73") && !Encoding.UTF8.GetString(protectedBytes).Contains("approval"));
        manager.SelectRoute(aliasB);
        var next = (JsonArray)output.DeepClone(); next.Add(new JsonObject { ["role"] = "user", ["content"] = "Continue after the checkpoint." });
        var continued = await Send("portable", next);
        var received = upstream.Requests.Last();
        check("The same conversation changes providers after compaction without a manual handoff", continued.Code == HttpStatusCode.OK && received.Host == "b.example.invalid" && received.Body.ToJsonString().Contains("HISTORY_MARKER_73") && received.Body.ToJsonString().Contains("Continue after the checkpoint.") && !received.Body.ToJsonString().Contains(GatewayHistoryStore.Prefix));
        check("A protected checkpoint survives independent store reload", new GatewayHistoryStore(directory).Read(token, GatewayHistoryStore.Conversation("portable", identity))?.Summary?.Contains("HISTORY_MARKER_73") == true);
        var before = upstream.Requests.Count;
        var wrongChat = await Send("another-conversation", next);
        check("A checkpoint cannot be replayed into a different conversation", wrongChat.Code == HttpStatusCode.BadRequest && upstream.Requests.Count == before);
        var unknown = await Send("portable", new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = GatewayHistoryStore.Prefix + new string('a', 32) }));
        check("A missing portable checkpoint never silently discards the missing context", unknown.Code == HttpStatusCode.BadRequest && unknown.Body.Contains("unavailable"));

        manager.SelectRoute(aliasA); upstream.EmitOpaque = true; upstream.ResponseMediaType = null;
        var original = await Send("source-opaque", Conversation());
        check("The original provider's opaque compaction stream is forwarded unchanged", original.Code == HttpStatusCode.OK && original.Body.Contains("fictional-opaque-a"));
        upstream.ResponseMediaType = "text/event-stream";
        manager.SelectRoute(aliasB);
        var opaqueHistory = new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = "fictional-opaque-a" },
            new JsonObject { ["type"] = "function_call_output", ["call_id"] = "tail-call", ["output"] = "TAIL_TOOL_OUTPUT" },
            new JsonObject { ["role"] = "user", ["content"] = "Latest user request" });
        before = upstream.Requests.Count;
        var bridged = await Send("source-opaque", opaqueHistory);
        var bridgeRequests = upstream.Requests.Skip(before).ToArray();
        check("Existing encrypted compaction is summarized only by its recorded source", bridged.Code == HttpStatusCode.OK && bridgeRequests.Length == 2 && bridgeRequests[0].Host == "a.example.invalid" && bridgeRequests[0].Body.ToJsonString().Contains("fictional-opaque-a") && bridgeRequests[1].Host == "b.example.invalid" && !bridgeRequests[1].Body.ToJsonString().Contains("fictional-opaque-a"));
        check("Headerless streaming inference retains opaque checkpoint provenance for the source-only handoff", bridged.Code == HttpStatusCode.OK && bridgeRequests.Length == 2);
        check("Handoff replacement preserves subsequent tool outputs and the latest request", bridgeRequests[1].Body.ToJsonString().Contains("TAIL_TOOL_OUTPUT") && bridgeRequests[1].Body.ToJsonString().Contains("Latest user request"));
        before = upstream.Summaries;
        var repeated = await Send("source-opaque", opaqueHistory);
        check("Confirmed summaries are reused instead of making repeated source calls", repeated.Code == HttpStatusCode.OK && upstream.Summaries == before);
        manager.SelectRoute(aliasA);
        var returned = await Send("source-opaque", opaqueHistory);
        check("Returning to the original provider remains possible after mixed-route history", returned.Code == HttpStatusCode.OK && upstream.Requests.Last().Host == "a.example.invalid");

        manager.SelectRoute(aliasA); upstream.EmitOpaque = true; upstream.Opaque = "ambiguous-summary";
        await Send("failed-summary", Conversation()); manager.SelectRoute(aliasB); upstream.FailSummary = true;
        var failedInput = new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = "ambiguous-summary" }); var turn = Guid.NewGuid();
        var failed = await Send("failed-summary", failedInput, turn: turn); before = upstream.Requests.Count;
        var duplicate = await Send("failed-summary", failedInput, turn: turn);
        check("Failed source summaries preserve context and automatic duplicates cannot resend inference", failed.Code == HttpStatusCode.BadRequest && duplicate.Code == HttpStatusCode.BadRequest && upstream.Requests.Count == before && !upstream.Requests.Last().Body.ToJsonString().Contains("Latest user request"));
        upstream.FailSummary = false;
        var explicitRetry = await Send("failed-summary", failedInput, turn: Guid.NewGuid());
        check("A new explicit turn can retry a failed source handoff", explicitRetry.Code == HttpStatusCode.OK);

        manager.SelectRoute(aliasA);
        var trigger = Conversation(); trigger.Add(new JsonObject { ["type"] = "compaction_trigger" });
        var v2 = await Send("v2-compaction", trigger);
        check("Current native compaction triggers produce a completed one-item checkpoint stream", v2.Code == HttpStatusCode.OK && v2.Body.Contains("response.output_item.done") && v2.Body.Contains("\"type\":\"compaction\"") && !upstream.Requests.Last().Body.ToJsonString().Contains("compaction_trigger"));
        upstream.SummaryMediaType = "application/json";
        var jsonSummary = await Send("json-summary", Conversation(), true);
        check("Explicit JSON source summaries still require and accept confirmed completion", jsonSummary.Code == HttpStatusCode.OK && jsonSummary.Body.Contains(GatewayHistoryStore.Prefix));
        upstream.SummaryMediaType = null; upstream.TruncateSummary = true;
        var truncated = await Send("truncated-summary", Conversation(), true);
        check("A headerless stream without confirmed completion cannot create a checkpoint", truncated.Code == HttpStatusCode.BadRequest && truncated.Body.Contains("before confirmed completion") && !truncated.Body.Contains(GatewayHistoryStore.Prefix));
        upstream.TruncateSummary = false;
        upstream.SummaryMediaType = "text/html";
        var unsupported = await Send("unsupported-summary", Conversation(), true);
        check("An explicit unsupported source response format fails without guessing or dropping context", unsupported.Code == HttpStatusCode.BadRequest && unsupported.Body.Contains("unsupported response format"));
        signedIn = false; before = upstream.Requests.Count;
        var signedOut = await Send("signed-out", Conversation(), true);
        check("Changed sign-in refuses before a source summary is sent", signedOut.Code == HttpStatusCode.Unauthorized && upstream.Requests.Count == before);
    }

    private sealed class HistoryUpstream : HttpMessageHandler
    {
        internal sealed record Request(string Host, JsonObject Body);
        internal List<Request> Requests { get; } = new();
        internal int Summaries;
        internal bool EmitOpaque, FailSummary;
        internal string? SummaryMediaType = "text/event-stream";
        internal string? ResponseMediaType = "text/event-stream";
        internal bool TruncateSummary;
        internal string Opaque = "fictional-opaque-a";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get)
            {
                var document = request.RequestUri!.Host == "chatgpt.com" ? new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("native-model")) } : new JsonObject { ["data"] = new JsonArray(new JsonObject { ["id"] = "history-model" }) };
                return new(HttpStatusCode.OK) { Content = new StringContent(document.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();
            Requests.Add(new(request.RequestUri!.Host, body));
            var summary = body["instructions"]?.GetValue<string>() == ModelHistoryHandoff.SummaryInstructions;
            if (summary) Summaries++;
            if (summary && FailSummary) return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("fictional-secret-never-echo") };
            object output;
            if (EmitOpaque && !summary) { EmitOpaque = false; output = new { id = "cmp_fixture", type = "compaction", encrypted_content = Opaque }; }
            else output = new { id = "msg_fixture", type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = summary ? "Verified handoff HISTORY_MARKER_73. Local edits only; no push approval. File revision 73. Next: continue the requested source fix." : "fixture-ok" } } };
            var complete = new { id = "response_" + Guid.NewGuid().ToString("N"), status = "completed", model = "history-model", output = new[] { output } };
            var stream = "data: " + JsonSerializer.Serialize(new { type = "response.output_item.done", item = output }) + "\n\ndata: " + JsonSerializer.Serialize(new { type = "response.completed", response = complete }) + "\n\n";
            if (summary && SummaryMediaType == "application/json")
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(complete), Encoding.UTF8, "application/json") };
            if (summary && TruncateSummary) stream = "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = "Unconfirmed summary" }) + "\n\n";
            var content = new StringContent(stream, Encoding.UTF8, "text/event-stream");
            var mediaType = summary ? SummaryMediaType : ResponseMediaType;
            content.Headers.ContentType = mediaType == null ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return new(HttpStatusCode.OK) { Content = content };
        }
    }
}
