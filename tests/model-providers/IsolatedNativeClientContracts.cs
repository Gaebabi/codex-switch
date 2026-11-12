using CodexAccountDesk;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class IsolatedNativeClientContracts
{
    internal static async Task Run(string executable, string home, ModelProviderManager manager, Upstreams upstreams, Action<string, bool> check)
    {
        Console.WriteLine("Native isolation phase: prepare fictional profile and independent upstream");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300)); var cancellation = timeout.Token;
        await using var control = new WorkspaceFixture();
        await using var native = new NativeInferenceFixture();
        var authPath = Path.Combine(home, "auth.json"); var configPath = Path.Combine(home, "config.toml");
        var nativeCatalogPath = Path.Combine(home, "native-catalog.json");
        var nativeCatalog = new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("fixture-openai")) }.ToJsonString();
        await File.WriteAllTextAsync(nativeCatalogPath, nativeCatalog, cancellation);
        var jwtPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fictional@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-openai-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + jwtPayload + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        await File.WriteAllTextAsync(authPath, auth, cancellation);
        var config = "# Fictional independent native client\nmodel_provider = \"openai\"\nmodel = \"fixture-openai\"\ncli_auth_credentials_store = \"file\"\nopenai_base_url = " + JsonSerializer.Serialize(native.BaseUrl) + "\nchatgpt_base_url = " + JsonSerializer.Serialize(control.BaseUrl) + "\nmodel_catalog_json = " + JsonSerializer.Serialize(nativeCatalogPath) + "\n[features]\nresponses_websockets_v2 = false\nenable_request_compression = false\nshell_tool = false\ncode_mode = false\n";
        await File.WriteAllTextAsync(configPath, config, cancellation);
        var aliases = manager.Settings.Providers.Take(2).Select(p => p.Models.Single().Alias(p.Id)).ToArray();
        manager.SelectRoute(aliases[0]);
        await manager.ConnectAsync(executable, home, cancellation);
        check("Actual CLI connection creates only an owned isolated profile", manager.Settings.IsolatedProfile && manager.Settings.AppliedConfig == null && File.Exists(manager.Settings.CustomProfilePath));
        var profile = await File.ReadAllTextAsync(manager.Settings.CustomProfilePath!, cancellation);
        var overrides = new Dictionary<string, string>();
        foreach (var line in profile.Split('\n').TakeWhile(line => !line.StartsWith('[')))
        {
            var separator = line.IndexOf('=');
            if (separator > 0 && !line.StartsWith('#')) overrides[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        var catalogPath = JsonSerializer.Deserialize<string>(overrides["model_catalog_json"])!;
        var catalog = await File.ReadAllTextAsync(catalogPath, cancellation);
        var catalogModels = JsonNode.Parse(catalog)!["models"]!.AsArray();
        check("Dedicated process catalog contains exactly one Switch model and no credentials", catalogModels.Count == 1 && catalogModels[0]!["slug"]!.GetValue<string>() == ModelRouting.Model && !catalog.Contains("fictional-openai-token") && manager.Settings.Providers.All(p => !catalog.Contains(p.ApiKey)));
        await manager.ConnectAsync(executable, home, cancellation);
        check("Repeated connection leaves the profile and native configuration stable", profile == await File.ReadAllTextAsync(manager.Settings.CustomProfilePath!, cancellation) && config == await File.ReadAllTextAsync(configPath, cancellation));
        await using var normal = new CodexRpc(executable, home, preserveProvider: true);
        await using var custom = new CodexRpc(executable, home, preserveProvider: true, configOverrides: overrides);
        await Task.WhenAll(normal.InitializeAsync(cancellation), custom.InitializeAsync(cancellation));
        check("Default and custom app servers are distinct concurrently running processes", normal.OwnedProcessId != custom.OwnedProcessId);
        var normalList = await normal.RequestAsync("model/list", new { includeHidden = false }, cancellation);
        var customList = await custom.RequestAsync("model/list", new { includeHidden = false }, cancellation);
        string[] Models(JsonElement list) => list.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("model").GetString()!).ToArray();
        check("Actual native picker remains native-only", Models(normalList).SequenceEqual(new[] { "fixture-openai" }));
        check("Actual dedicated picker remains custom-only", Models(customList).SequenceEqual(new[] { ModelRouting.Model }));
        async Task<string> Thread(CodexRpc client, string expectedModel)
        {
            var result = await client.RequestAsync("thread/start", new { cwd = home, ephemeral = true, approvalPolicy = "never", sandbox = "read-only", config = new Dictionary<string, object> { ["mcp_servers"] = new Dictionary<string, object>() } }, cancellation);
            check("New process retains original provider identity and its own default: " + expectedModel, result.GetProperty("modelProvider").GetString() == "openai" && result.GetProperty("model").GetString() == expectedModel);
            return result.GetProperty("thread").GetProperty("id").GetString()!;
        }
        var normalId = await Thread(normal, "fixture-openai"); var customId = await Thread(custom, ModelRouting.Model);
        async Task<bool> Turn(CodexRpc client, string id, string? model = null, string? effort = null)
        {
            var complete = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Notification(string method, JsonElement value) { if (method == "turn/completed" && value.GetProperty("threadId").GetString() == id) complete.TrySetResult(value.Clone()); }
            client.Notification += Notification;
            try
            {
                await client.RequestAsync("turn/start", new { threadId = id, model, effort, input = new[] { new { type = "text", text = "Reply with fixture-ok." } } }, cancellation);
                var result = await complete.Task.WaitAsync(cancellation);
                var success = result.GetProperty("turn").GetProperty("status").GetString() == "completed";
                if (!success) Console.WriteLine("Fictional reasoning turn result: " + result.GetRawText());
                return success;
            }
            finally { client.Notification -= Notification; }
        }
        Console.WriteLine("Native isolation phase: concurrent app-server turns and live custom selection");
        var concurrent = await Task.WhenAll(Turn(normal, normalId), Turn(custom, customId));
        check("Concurrent actual native and custom turns both complete", concurrent.All(success => success));
        check("Native inference bypasses Switch while custom inference uses its selected provider", native.Upstream.Requests.Any(r => r.Method == "POST") && upstreams.Requests.Last().Host == "a.example.invalid");
        foreach (var selection in new[] { aliases[1], aliases[0] })
        {
            manager.SelectRoute(selection);
            check("Same dedicated process and chat uses changed custom selection", await Turn(custom, customId));
            check("Selected custom backend receives the actual request", upstreams.Requests.Last().Host == (selection == aliases[0] ? "a.example.invalid" : "b.example.invalid"));
        }
        var added = await manager.SaveAsync(null, "Added while custom process runs", "https://c.example.invalid/v1", "fictional-added-key", cancellation);
        manager.SelectRoute(added.Models.Single().Alias(added.Id));
        check("A newly registered provider works without restarting either process", await Turn(custom, customId) && upstreams.Requests.Last().Host == "c.example.invalid");
        manager.SelectRoute(aliases[0]);
        var first = manager.Settings.Providers[0];
        await manager.SaveReasoningAsync(first.Id, first.Models.Single().UpstreamId, new ProviderReasoningOptions { Levels = new() { "low", "high" }, Default = "low" }, cancellation);
        var beforeReasoningChange = upstreams.Requests.Count(request => request.Method == "POST");
        check("A reasoning choice from a stale native catalog is refused before inference", !await Turn(custom, customId) && upstreams.Requests.Count(request => request.Method == "POST") == beforeReasoningChange);
        check("An explicitly selected supported effort applies to the same dedicated chat", await Turn(custom, customId, effort: "low") && JsonNode.Parse(upstreams.Requests.Last().Body!)!["reasoning"]!["effort"]!.GetValue<string>() == "low");
        var updatedCatalog = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath, cancellation))!["models"]!.AsArray();
        check("Live reasoning changes update only the fixed Switch capability range and preserve the native catalog", updatedCatalog.Count == 1 && updatedCatalog[0]!["slug"]!.GetValue<string>() == ModelRouting.Model && updatedCatalog[0]!["supported_reasoning_levels"]!.AsArray().Select(level => level!["effort"]!.GetValue<string>()).SequenceEqual(new[] { "low", "high" }) && nativeCatalog == await File.ReadAllTextAsync(nativeCatalogPath, cancellation));
        Console.WriteLine("Native isolation phase: bounded custom 507 and stream recovery");
        var nativeBeforeRecovery = native.Upstream.Requests.Count;
        var recoveryStart = upstreams.Requests.Count;
        upstreams.StorageFailures = 3;
        check("Actual dedicated CLI completes one custom turn after three gateway HTTP 507 retries", await Turn(custom, customId));
        var recovered = upstreams.Requests.Skip(recoveryStart).Where(request => request.Method == "POST").ToArray();
        check("Actual dedicated CLI recovery sends exactly the original plus three attempts to its selected provider", recovered.Length == 4 && recovered.All(request => request.Host == "a.example.invalid") && recovered.Select(request => request.Body).Distinct().Count() == 1);
        var exhaustionStart = upstreams.Requests.Count;
        upstreams.StorageFailures = 10;
        check("Actual dedicated CLI receives terminal failure when custom HTTP 507 recovery is exhausted", !await Turn(custom, customId));
        var exhausted = upstreams.Requests.Skip(exhaustionStart).Where(request => request.Method == "POST").ToArray();
        check("Actual dedicated CLI cannot multiply the custom gateway's three-retry budget", exhausted.Length == 4 && exhausted.All(request => request.Host == "a.example.invalid") && exhausted.Select(request => request.Body).Distinct().Count() == 1);
        upstreams.StorageFailures = 0;
        var streamStart = upstreams.Requests.Count(request => request.Method == "POST");
        upstreams.StreamFailures = 1;
        check("Actual dedicated CLI persists a failed custom turn after a response stream failure", !await Turn(custom, customId));
        check("Post-response stream failure is not automatically replayed by the gateway", upstreams.Requests.Count(request => request.Method == "POST") - streamStart == 1);
        check("Actual dedicated CLI completes a following turn in the same chat after stream failure", await Turn(custom, customId));
        check("Custom recovery and follow-up never fall back to native OpenAI", native.Upstream.Requests.Count == nativeBeforeRecovery && upstreams.Requests.Last().Host == "a.example.invalid");
        Console.WriteLine("Native isolation phase: concurrent default exec and V2 profile exec");
        var normalCount = native.Upstream.Requests.Count;
        var customCount = upstreams.Requests.Count;
        var executions = await Task.WhenAll(Exec(executable, home, false, cancellation), Exec(executable, home, true, cancellation));
        check("Actual exec loads generated V2 profile concurrently with default exec", executions.All(result => result.Success) && native.Upstream.Requests.Count > normalCount && upstreams.Requests.Count > customCount);
        check("Default exec and profile exec keep independent requested models", JsonNode.Parse(native.Upstream.Requests.Last().Body!)!["model"]!.GetValue<string>() == "fixture-openai" && JsonNode.Parse(upstreams.Requests.Last().Body!)!["model"]!.GetValue<string>() == "shared-model");
        Console.WriteLine("Native isolation phase: stop Switch and observe native independence");
        var ownedProfile = manager.Settings.CustomProfilePath!;
        await manager.DisconnectAsync(executable, home, cancellation);
        check("Disconnect stops only Switch and removes its owned profile", !manager.Running && !File.Exists(ownedProfile));
        check("Already-running native chat completes with Switch stopped", await Turn(normal, normalId));
        var offlineCustomCount = upstreams.Requests.Count;
        var offlineNativeCount = native.Upstream.Requests.Count;
        await ObserveOfflineTransport(custom, customId, check, cancellation);
        check("Offline custom transport sends no upstream request and never falls back to native", upstreams.Requests.Count == offlineCustomCount && native.Upstream.Requests.Count == offlineNativeCount);
        check("New native CLI process completes with Switch stopped", (await Exec(executable, home, false, cancellation)).Success);
        check("Native root configuration, catalog and sign-in bytes remain unchanged", config == await File.ReadAllTextAsync(configPath, cancellation) && nativeCatalog == await File.ReadAllTextAsync(nativeCatalogPath, cancellation) && auth == await File.ReadAllTextAsync(authPath, cancellation));
        // Preserve the enclosing suite's active-gateway precondition for its removal checks.
        await manager.ConnectAsync(executable, home, cancellation);
        Console.WriteLine("Native isolation phase: guarded legacy connect and disconnect migration");
        await LegacyMigration(executable, home, auth, config, check, cancellation);
        Console.WriteLine("Native isolation phase: complete");
    }

    private static async Task ObserveOfflineTransport(CodexRpc client, string threadId, Action<string, bool> check, CancellationToken cancellation)
    {
        var observed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? turnId = null;
        void Notification(string method, JsonElement value)
        {
            if (!value.TryGetProperty("threadId", out var target) || target.GetString() != threadId) return;
            if (method == "error") observed.TrySetResult(value.Clone());
            if (method == "turn/completed") completed.TrySetResult(value.Clone());
        }
        client.Notification += Notification;
        try
        {
            var started = await client.RequestAsync("turn/start", new { threadId, input = new[] { new { type = "text", text = "Reply with fixture-ok." } } }, cancellation);
            turnId = started.GetProperty("turn").GetProperty("id").GetString()!;
            var notification = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
            var error = notification.GetProperty("error");
            var message = error.GetProperty("message").GetString() ?? "";
            var transport = error.TryGetProperty("codexErrorInfo", out var info) && info.ValueKind == JsonValueKind.Object &&
                (info.TryGetProperty("httpConnectionFailed", out _) || info.TryGetProperty("responseStreamConnectionFailed", out _) || info.TryGetProperty("responseStreamDisconnected", out _));
            transport |= message.Contains("connect", StringComparison.OrdinalIgnoreCase) || message.Contains("error sending request", StringComparison.OrdinalIgnoreCase) || message.Contains("10061", StringComparison.Ordinal);
            check("Stopped custom listener reports an actual transport refusal with retry state", transport && notification.GetProperty("turnId").GetString() == turnId && notification.TryGetProperty("willRetry", out var retry) && retry.ValueKind is JsonValueKind.True or JsonValueKind.False);
            Console.WriteLine("Native isolation offline transport: willRetry=" + notification.GetProperty("willRetry").GetBoolean());
        }
        finally
        {
            // Bound this observation, not the CLI's retry policy. Only this fixture-owned turn is interrupted.
            if (turnId != null && !completed.Task.IsCompleted)
                await client.RequestAsync("turn/interrupt", new { threadId, turnId }, cancellation);
            client.Notification -= Notification;
        }
    }

    private static async Task<(bool Success, int Exit)> Exec(string executable, string home, bool profile, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = home, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", "--ephemeral", "--skip-git-repo-check", "--json", "--sandbox", "read-only" }) start.ArgumentList.Add(argument);
        if (profile) { start.ArgumentList.Add("--profile"); start.ArgumentList.Add("codex-switch"); }
        start.ArgumentList.Add("Reply with fixture-ok."); start.Environment["CODEX_HOME"] = home;
        foreach (var key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL", "OPENAI_IDENTITY_TOKEN_FILE", "OPENAI_IDENTITY_TOKEN_AUDIENCE" }) start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new IOException("Could not start fixture CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation); var stderr = process.StandardError.ReadToEndAsync(cancellation);
        try { await process.WaitForExitAsync(cancellation); await stderr; return (process.ExitCode == 0 && (await stdout).Contains("turn.completed", StringComparison.Ordinal), process.ExitCode); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    private static async Task LegacyMigration(string executable, string parent, string auth, string originalText, Action<string, bool> check, CancellationToken cancellation)
    {
        foreach (var operation in new[] { "connect", "disconnect" })
        foreach (var state in new[] { "applied", "original", "divergent", "divergent-alias" })
        {
            var divergent = state.StartsWith("divergent", StringComparison.Ordinal);
            var home = Path.Combine(parent, "legacy-" + operation + "-" + state); Directory.CreateDirectory(home);
            var directory = Path.Combine(home, "switch-store"); var upstream = new Upstreams();
            GatewayIdentity? Authenticate(string? bearer, string? account) => GatewayIdentity.Authenticate(bearer, account, () => auth);
            string alias;
            await using (var seed = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream)))
            { var provider = await seed.SaveAsync(null, "Legacy fixture", "https://a.example.invalid/v1", "fictional-legacy-key", cancellation); alias = provider.Models.Single().Alias(provider.Id); seed.SelectRoute(alias); }
            var store = new ModelProviderStore(directory);
            store.Update(settings => { settings.Connected = true; settings.OriginalConfig = new() { ["model"] = System.Text.Json.Nodes.JsonValue.Create("fixture-openai") }; settings.AppliedConfig = new() { ["model"] = System.Text.Json.Nodes.JsonValue.Create(ModelRouting.Model) }; });
            var current = originalText.Replace("model = \"fixture-openai\"", "model = \"" + (state == "applied" ? ModelRouting.Model : state == "original" ? "fixture-openai" : state == "divergent-alias" ? "cs_owner_edited_model" : "owner-edited-model") + "\"");
            var path = Path.Combine(home, "config.toml"); await File.WriteAllTextAsync(path, current, cancellation); await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), auth, cancellation);
            await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(new Upstreams()));
            var refused = false;
            try
            {
                if (operation == "connect") await manager.ConnectAsync(executable, home, cancellation);
                else await manager.DisconnectAsync(executable, home, cancellation);
            }
            catch (InvalidOperationException) when (divergent) { refused = true; }
            if (divergent) check("Actual legacy " + operation + " refuses " + state + " values without changing root bytes", refused && current == await File.ReadAllTextAsync(path, cancellation));
            else
            {
                var restored = await File.ReadAllTextAsync(path, cancellation);
                check("Actual legacy " + operation + " accepts recorded " + state + " values", manager.Settings.RootMigrationCompleted && restored.Contains("model = \"fixture-openai\"") && restored.Contains("# Fictional independent native client"));
                check("Legacy migration retains its encrypted recovery receipt", manager.Settings.OriginalConfig != null && manager.Settings.AppliedConfig != null);
                if (state == "original") check("Already-restored root migration makes no write", current == restored);
            }
        }
    }
}
