using CodexAccountDesk;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonValue = CodexAccountDesk.JsonValue;

internal static class HistoryNativeContracts
{
    internal static async Task Run(string executable, string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "native-portable-home"); Directory.CreateDirectory(home);
        await using var control = new WorkspaceFixture();
        var upstream = new NativeHistoryUpstream();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fictional@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-openai-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + payload + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), auth);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "model_provider=\"openai\"\nmodel=\"native-model\"\ncli_auth_credentials_store=\"file\"\nchatgpt_base_url=" + JsonSerializer.Serialize(control.BaseUrl) + "\n[features]\nresponses_websockets_v2=false\nenable_request_compression=false\nshell_tool=false\ncode_mode=false\n");
        GatewayIdentity? Authenticate(string? token, string? account) => GatewayIdentity.Authenticate(token, account, () => auth);
        await using var manager = new ModelProviderManager(Path.Combine(root, "native-portable-store"), Authenticate, new HttpClient(upstream));
        var a = await manager.SaveAsync(null, "Native history A", "https://a.example.invalid/v1", "fictional-history-a", CancellationToken.None);
        var b = await manager.SaveAsync(null, "Native history B", "https://b.example.invalid/v1", "fictional-history-b", CancellationToken.None);
        manager.SelectRoute(a.Models.Single().Alias(a.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var cancellation = timeout.Token;
        await manager.ConnectAsync(executable, home, cancellation);
        var profile = await File.ReadAllTextAsync(manager.Settings.CustomProfilePath!, cancellation);
        var overrides = new Dictionary<string, string>();
        foreach (var line in profile.Split('\n').TakeWhile(line => !line.StartsWith('[')))
        { var equal = line.IndexOf('='); if (equal > 0 && !line.StartsWith('#')) overrides[line[..equal].Trim()] = line[(equal + 1)..].Trim(); }
        await using var rpc = new CodexRpc(executable, home, true, overrides); await rpc.InitializeAsync(cancellation);
        var thread = await rpc.RequestAsync("thread/start", new { cwd = home, ephemeral = true, model = ModelRouting.Model, approvalPolicy = "never", sandbox = "read-only" }, cancellation);
        var id = thread.GetProperty("thread").GetProperty("id").GetString()!;
        async Task<bool> Turn(string message)
        {
            var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Notification(string method, JsonElement value) { if (method == "turn/completed" && value.GetProperty("threadId").GetString() == id) done.TrySetResult(value.Clone()); }
            rpc.Notification += Notification;
            try
            {
                await rpc.RequestAsync("turn/start", new { threadId = id, input = new[] { new { type = "text", text = message } } }, cancellation);
                var finished = await done.Task.WaitAsync(cancellation);
                var success = finished.GetProperty("turn").GetProperty("status").GetString() == "completed";
                if (!success) Console.WriteLine("Fictional native turn failure: " + finished.GetRawText() + "; gateway=" + manager.LastError);
                return success;
            }
            finally { rpc.Notification -= Notification; }
        }
        check("An actual native thread starts on the source provider", await Turn("Remember NATIVE_HISTORY_91. Reply fixture-ok."));
        Console.WriteLine("Native history phase: source turn complete; requesting compaction");
        var compacted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Compaction(string method, JsonElement value)
        {
            if (method is "error" or "item/completed" or "thread/compacted") Console.WriteLine("Fictional compact event: " + method + " " + value.GetRawText());
            if (method == "error" && value.GetProperty("threadId").GetString() == id) compacted.TrySetException(new Exception("Native compaction refused: " + value.GetProperty("error").GetRawText()));
            if (method == "item/completed" && value.GetProperty("threadId").GetString() == id && JsonValue.String(value.GetProperty("item"), "type") == "contextCompaction") compacted.TrySetResult();
        }
        rpc.Notification += Compaction;
        try
        {
            await rpc.RequestAsync("thread/compact/start", new { threadId = id }, cancellation);
            await compacted.Task.WaitAsync(cancellation);
        }
        finally { rpc.Notification -= Compaction; }
        check("Actual native manual compaction accepts the gateway-owned checkpoint result", upstream.Summaries == 1);
        manager.SelectRoute(b.Models.Single().Alias(b.Id));
        check("The same actual native thread completes after switching provider with compressed history", await Turn("Continue using NATIVE_HISTORY_91. Reply fixture-ok."));
        var last = upstream.Requests.Last();
        check("The destination receives a readable source summary and no gateway checkpoint token", last.Host == "b.example.invalid" && last.Body.Contains("NATIVE_HISTORY_91") && !last.Body.Contains(GatewayHistoryStore.Prefix));
    }
    private sealed class NativeHistoryUpstream : HttpMessageHandler
    {
        internal List<(string Host, string Body)> Requests { get; } = new();
        internal int Summaries;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get)
            {
                var data = request.RequestUri!.Host == "chatgpt.com" ? new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("native-model")) } : new JsonObject { ["data"] = new JsonArray(new JsonObject { ["id"] = "history-model" }) };
                return new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            var body = await request.Content!.ReadAsStringAsync(cancellation); Requests.Add((request.RequestUri!.Host, body));
            var json = JsonNode.Parse(body)!;
            if (json["instructions"]?.GetValue<string>() == ModelHistoryHandoff.SummaryInstructions)
            {
                Summaries++;
                var response = new { id = "summary", status = "completed", output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "NATIVE_HISTORY_91 is the verified marker. Continue the existing task without changing permissions." } } } } };
                var content = new ByteArrayContent(Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(new { type = "response.completed", response }) + "\n\n"));
                return new(HttpStatusCode.OK) { Content = content };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(Upstreams.Stream("history-model"), Encoding.UTF8, "text/event-stream") };
        }
    }
}
