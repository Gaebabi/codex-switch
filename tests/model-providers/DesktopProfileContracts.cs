using CodexAccountDesk;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class DesktopProfileContracts
{
    internal static async Task Run(string executable, string root, Action<string, bool> check)
    {
        Console.WriteLine("Native desktop-profile phase: picker, reasoning and restoration");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var cancellation = timeout.Token;
        var home = Path.Combine(root, "desktop-profile-home"); Directory.CreateDirectory(home);
        await using var control = new WorkspaceFixture();
        await using var native = new NativeInferenceFixture();
        var metadata = ProviderCatalog.Generic("fixture-openai");
        metadata["default_reasoning_level"] = "medium";
        metadata["supported_reasoning_levels"] = new JsonArray(new[] { "low", "medium", "high", "xhigh", "max" }
            .Select(level => (JsonNode)new JsonObject { ["effort"] = level, ["description"] = level }).ToArray());
        native.Upstream.NativeMetadata = (JsonObject)metadata.DeepClone();
        using (var probe = ProviderCatalog.CreateHttpClient())
        using (var invalid = await probe.PostAsync(native.BaseUrl + "/responses", new StringContent("{\"model\":\"fixture-openai\",\"reasoning\":{\"effort\":\"none\"},\"input\":[]}", Encoding.UTF8, "application/json"), cancellation))
            check("The native fixture rejects the originally reported unsupported none effort", invalid.StatusCode == System.Net.HttpStatusCode.BadRequest);
        var upstream = new Upstreams { NativeMetadata = (JsonObject)metadata.DeepClone() };
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fictional@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-openai-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + payload + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), auth, cancellation);
        GatewayIdentity? Authenticate(string? token, string? account) => GatewayIdentity.Authenticate(token, account, () => auth);
        var directory = Path.Combine(root, "desktop-profile-store");
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream), home);
        var provider = await manager.SaveAsync(null, "Desktop custom", "https://a.example.invalid/v1", "fictional-desktop-key", cancellation);
        manager.SelectRoute(provider.Models.Single().Alias(provider.Id));
        var nativeCatalogPath = Path.Combine(home, "native-catalog.json");
        await File.WriteAllTextAsync(nativeCatalogPath, new JsonObject { ["models"] = new JsonArray(metadata.DeepClone()) }.ToJsonString(), cancellation);
        var configPath = Path.Combine(home, "config.toml");
        var config = "# Native choices and unrelated settings must survive\nmodel_provider = \"openai\"\nmodel = \"fixture-openai\"\nmodel_reasoning_effort = \"none\"\ncli_auth_credentials_store = \"file\"\nopenai_base_url = " + JsonSerializer.Serialize(native.BaseUrl) +
            "\nchatgpt_base_url = " + JsonSerializer.Serialize(control.BaseUrl) + "\nmodel_catalog_json = " + JsonSerializer.Serialize(nativeCatalogPath) +
            "\n[features]\nresponses_websockets_v2 = false\nenable_request_compression = false\nshell_tool = false\ncode_mode = false\n";
        await File.WriteAllTextAsync(configPath, config, cancellation);
        foreach (var validate in new[] { true, false })
        {
            var blocked = false;
            try
            {
                if (validate) await manager.ValidateDesktopProfileAsync(executable, home, CodexModelProfile.Switch, cancellation);
                else await manager.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Switch, cancellation);
            }
            catch (InvalidOperationException error) { blocked = error.Message.Contains("separate Codex Switch terminal"); }
            check("Retired combined profile is rejected before " + (validate ? "restart preflight" : "application"), blocked &&
                await File.ReadAllTextAsync(configPath, cancellation) == config && !manager.Running && !manager.Settings.Connected);
        }
        await SeedLegacyAsync(executable, home, directory, manager, metadata, cancellation);
        var connectedConfig = await File.ReadAllTextAsync(configPath, cancellation);
        check("Desktop profile retains the native default and unrelated TOML settings", connectedConfig.Contains("model = \"fixture-openai\"") && connectedConfig.Contains("# Native choices") && connectedConfig.Contains("shell_tool = false"));
        check("Desktop profile removes an effort unsupported by the selected native model", !connectedConfig.Contains("model_reasoning_effort"));
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "model-providers-desktop-catalog.json"), cancellation))!["models"]!.AsArray();
        check("Desktop catalog preserves the native document, hidden legacy ID and fixed Switch entry", catalog.Count == 3 && JsonNode.DeepEquals(catalog[0], metadata) && catalog[1]!["slug"]!.GetValue<string>() == ModelRouting.Model);
        check("The virtual entry uses the selected provider's own reasoning default", catalog[1]!["default_reasoning_level"]!.GetValue<string>() == ProviderReasoning.Metadata(provider.Models.Single(), null)["default_reasoning_level"]!.GetValue<string>());
        check("Legacy recovery retains the original native endpoint receipt", manager.Settings.DesktopOriginalConfig!["openai_base_url"]!.GetValue<string>() == native.BaseUrl);
        await using (var rpc = new CodexRpc(executable, home, preserveProvider: true))
        {
            await rpc.InitializeAsync(cancellation);
            var list = await rpc.RequestAsync("model/list", new { includeHidden = false }, cancellation);
            check("Legacy picker keeps native models and the fixed custom alias", list.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("model").GetString()).SequenceEqual(new[] { "fixture-openai", provider.Models.Single().Alias(provider.Id) }));
            check("Actual Codex catalog reports the selected provider's reasoning default", list.GetProperty("data").EnumerateArray().Last().GetProperty("defaultReasoningEffort").GetString() == ProviderReasoning.Metadata(provider.Models.Single(), null)["default_reasoning_level"]!.GetValue<string>());
            var thread = await rpc.RequestAsync("thread/start", new { cwd = home, ephemeral = true, model = ModelRouting.Model, approvalPolicy = "never", sandbox = "read-only",
                config = new Dictionary<string, object> { ["mcp_servers"] = new Dictionary<string, object>() } }, cancellation);
            var id = thread.GetProperty("thread").GetProperty("id").GetString()!;
            Console.WriteLine("Native desktop-profile explicit thread effort: " + thread.GetProperty("reasoningEffort").GetRawText());
            check("Actual virtual thread has no stale explicit reasoning override", thread.GetProperty("reasoningEffort").ValueKind == JsonValueKind.Null);
            check("Actual virtual turn reaches only the selected custom provider", await Turn(rpc, id, ModelRouting.Model, cancellation) && upstream.Requests.Last().Host == "a.example.invalid" && upstream.Requests.Last().Token == "fictional-desktop-key");
            check("Returning to a native model completes without the unsupported none error", await Turn(rpc, id, "fixture-openai", cancellation) && upstream.Requests.Last().Host == "chatgpt.com" && JsonNode.Parse(upstream.Requests.Last().Body!)?["reasoning"]?["effort"]?.GetValue<string>() != "none");
        }
        var originalConnected = await File.ReadAllTextAsync(configPath, cancellation);
        var divergent = originalConnected.Replace(manager.BaseUrl!, "http://127.0.0.1:1/backend-api/codex", StringComparison.Ordinal);
        await File.WriteAllTextAsync(configPath, divergent, cancellation);
        var refused = false;
        try { await manager.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Native, cancellation); } catch (InvalidOperationException) { refused = true; }
        check("Profile restoration refuses concurrent owner edits without replacing them", refused && await File.ReadAllTextAsync(configPath, cancellation) == divergent);
        await File.WriteAllTextAsync(configPath, originalConnected, cancellation);
        await manager.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Native, cancellation);
        check("Restoration invalidates the combined native cache", !File.Exists(Path.Combine(home, "models_cache.json")));
        check("Profile changes retain authentication and the saved custom provider", await File.ReadAllTextAsync(Path.Combine(home, "auth.json"), cancellation) == auth && manager.Settings.Providers.Single().Id == provider.Id && manager.Settings.SelectedRoute == provider.Models.Single().Alias(provider.Id));
        await manager.DisposeAsync();
        await using var normal = new CodexRpc(executable, home, preserveProvider: true);
        await normal.InitializeAsync(cancellation);
        var read = await normal.RequestAsync("config/read", new { includeLayers = true }, cancellation);
        var restored = read.GetProperty("layers").EnumerateArray().Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "user" &&
            string.Equals(layer.GetProperty("name").GetProperty("file").GetString(), configPath, StringComparison.OrdinalIgnoreCase) &&
            (!layer.GetProperty("name").TryGetProperty("profile", out var selectedProfile) || selectedProfile.ValueKind == JsonValueKind.Null)).GetProperty("config");
        check("Default profile restores native endpoint and catalog as read by Codex", restored.GetProperty("openai_base_url").GetString() == native.BaseUrl && restored.GetProperty("model_catalog_json").GetString() == nativeCatalogPath && restored.GetProperty("model").GetString() == "fixture-openai");
        var nativeThread = await normal.RequestAsync("thread/start", new { cwd = home, ephemeral = true, approvalPolicy = "never", sandbox = "read-only",
            config = new Dictionary<string, object> { ["mcp_servers"] = new Dictionary<string, object>() } }, cancellation);
        check("Restored default completes with Switch stopped", await Turn(normal, nativeThread.GetProperty("thread").GetProperty("id").GetString()!, "fixture-openai", cancellation));
    }

    internal static async Task SeedLegacyAsync(string executable, string home, string directory, ModelProviderManager manager, JsonObject native, CancellationToken cancellation)
    {
        // Reproduce an installation made by the retired combined profile; production cannot create it.
        manager.Start();
        await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
        await rpc.InitializeAsync(cancellation);
        var configPath = Path.Combine(home, "config.toml");
        var read = await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation);
        var layer = read.GetProperty("layers").EnumerateArray().Single(item =>
            item.GetProperty("name").GetProperty("type").GetString() == "user" &&
            CodexAccountDesk.JsonValue.String(item.GetProperty("name"), "profile") == null);
        var config = JsonNode.Parse(layer.GetProperty("config").GetRawText())!.AsObject();
        var applied = new Dictionary<string, JsonNode?>
        {
            ["model_provider"] = "openai", ["openai_base_url"] = manager.BaseUrl,
            ["model_catalog_json"] = null, ["features.responses_websockets_v2"] = false,
            ["features.enable_request_compression"] = false
        };
        JsonNode? Original(string key) => key.Split('.').Aggregate((JsonNode?)config, (node, part) => node?[part])?.DeepClone();
        var store = (ModelProviderStore)typeof(ModelProviderManager).GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(manager)!;
        store.Update(settings =>
        {
            settings.DesktopOriginalConfig = applied.Keys.ToDictionary(key => key, Original);
            settings.DesktopAppliedConfig = applied; settings.DesktopProfile = true;
            settings.Connected = true; settings.ConnectionWriteConfirmed = true; settings.RouterEnabled = true;
            settings.LastClientVersion = ModelGateway.ClientVersion(CodexAccountDesk.JsonValue.String(rpc.Initialization!.Value, "userAgent"));
        });
        var catalog = new JsonArray(native.DeepClone());
        foreach (var model in ModelRouting.Catalog(manager.Settings)) catalog.Add(model!.DeepClone());
        await File.WriteAllBytesAsync(Path.Combine(directory, "model-providers-desktop-catalog.json"), ProviderCatalog.Serialize(new JsonObject { ["models"] = catalog }), cancellation);
        var edits = applied.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone());
        if (config["model_reasoning_effort"]?.GetValue<string>() == "none") edits["model_reasoning_effort"] = null;
        await rpc.RequestAsync("config/batchWrite", new
        {
            filePath = configPath, expectedVersion = layer.GetProperty("version").GetString(), reloadUserConfig = false,
            edits = edits.Select(pair => new { keyPath = pair.Key, value = pair.Value, mergeStrategy = "replace" }).ToArray()
        }, cancellation);
        LiveModelCatalog.Invalidate(home);
    }

    private static async Task<bool> Turn(CodexRpc rpc, string id, string model, CancellationToken cancellation)
    {
        var complete = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Notification(string method, JsonElement value) { if (method == "turn/completed" && value.GetProperty("threadId").GetString() == id) complete.TrySetResult(value.Clone()); }
        rpc.Notification += Notification;
        try
        {
            var catalog = await rpc.RequestAsync("model/list", new { includeHidden = true }, cancellation);
            var selected = catalog.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("model").GetString() == model);
            var effort = selected.GetProperty("defaultReasoningEffort").GetString();
            await rpc.RequestAsync("turn/start", new { threadId = id, model, effort, input = new[] { new { type = "text", text = "Reply with fixture-ok." } } }, cancellation);
            return (await complete.Task.WaitAsync(cancellation)).GetProperty("turn").GetProperty("status").GetString() == "completed";
        }
        finally { rpc.Notification -= Notification; }
    }
}
