using CodexAccountDesk;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class DesktopBackendContracts
{
    internal static async Task Run(string nativeExecutable, string backend, string root, Action<string, bool> check)
    {
        var rejectedStable = false;
        try { DesktopBackendCatalog.RequireCompatibleClient(JsonSerializer.SerializeToElement(new { userAgent = "codex_cli_rs/0.167.0 (Windows)" })); }
        catch (InvalidDataException) { rejectedStable = true; }
        check("The stable CLI with a different tool contract cannot enable the desktop backend", rejectedStable);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4)); var cancellation = deadline.Token;
        var home = Path.Combine(root, "native-home"); var directory = Path.Combine(root, "switch-data");
        Directory.CreateDirectory(home); Directory.CreateDirectory(directory);
        await using var native = new NativeInferenceFixture();
        await using var custom = new NativeInferenceFixture();
        var auth = "{\"OPENAI_API_KEY\":\"fictional-native-key\"}";
        var nativeCatalog = Path.Combine(home, "native.json");
        var nativeMetadata = ProviderCatalog.Generic("fixture-native"); nativeMetadata["context_window"] = 256000;
        File.WriteAllText(nativeCatalog, new JsonObject { ["models"] = new JsonArray(nativeMetadata) }.ToJsonString());
        var config = "model=\"fixture-native\"\nmodel_provider=\"openai\"\ncli_auth_credentials_store=\"file\"\nopenai_base_url=" + JsonSerializer.Serialize(native.BaseUrl) +
            "\nmodel_catalog_json=" + JsonSerializer.Serialize(nativeCatalog) + "\n[features]\nshell_tool=false\ncode_mode=false\nenable_request_compression=false\n";
        File.WriteAllText(Path.Combine(home, "auth.json"), auth); File.WriteAllText(Path.Combine(home, "config.toml"), config);
        var model = new ProviderModel { UpstreamId = "fixture-custom", Metadata = ProviderCatalog.Generic("fixture-custom") };
        model.Metadata["context_window"] = 96000;
        var provider = new ModelProvider { Name = "Fictional custom", BaseUrl = custom.BaseUrl, ApiKey = "fictional-custom-key", Models = new() { model } };
        var store = new ModelProviderStore(directory); store.Update(settings => { settings.Providers.Add(provider); settings.SelectedRoute = model.Alias(provider.Id); });
        var second = new ModelProviderStore(directory);
        store.Update(settings => settings.LastClientVersion = "fixture-first-writer");
        second.Update(settings => settings.RouterEnabled = true);
        check("Independent store writers preserve the latest unrelated values", store.Read().LastClientVersion == "fixture-first-writer" && store.Read().RouterEnabled);
        var historyA = new GatewayHistoryStore(directory); var historyB = new GatewayHistoryStore(directory);
        var conversation = GatewayHistoryStore.Conversation(Guid.NewGuid().ToString(), new("fixture", null, true));
        var tokenA = historyA.CreatePortable(conversation, new("fixture", "one"), "first summary");
        var tokenB = historyB.CreatePortable(conversation, new("fixture", "two"), "second summary");
        check("Independent history writers preserve both checkpoints", historyA.Read(tokenB, conversation)?.Summary == "second summary" && historyB.Read(tokenA, conversation)?.Summary == "first summary");
        await using var manager = new ModelProviderManager(directory, (_, _) => null, codexHome: home);
        await manager.EnableDesktopBackendAsync(nativeExecutable, cancellation);
        var bootstrap = Path.Combine(root, "backend.json");
        File.WriteAllText(bootstrap, JsonSerializer.Serialize(new DesktopBackendHost.Configuration(nativeExecutable, home, directory)));
        var originalEnvironment = Environment.GetEnvironmentVariable("CODEX_SWITCH_BACKEND_CONFIG");
        Environment.SetEnvironmentVariable("CODEX_SWITCH_BACKEND_CONFIG", bootstrap);
        try
        {
            await using var rpc = new CodexRpc(backend, home, preserveProvider: true);
            rpc.DiagnosticMessage += line => Console.Error.WriteLine(line);
            await rpc.InitializeAsync(cancellation);
            var pid = rpc.OwnedProcessId;
            async Task<JsonElement[]> Models(bool hidden = false)
            {
                var results = new List<JsonElement>(); string? cursor = null;
                do
                {
                    var page = await rpc.RequestAsync("model/list", new { includeHidden = hidden, cursor, limit = 1 }, cancellation);
                    results.AddRange(page.GetProperty("data").EnumerateArray().Select(item => item.Clone()));
                    cursor = page.GetProperty("nextCursor").GetString();
                } while (cursor != null);
                return results.ToArray();
            }
            var alias = model.Alias(provider.Id);
            var list = await Models();
            check("One paginated native picker contains the native model and fixed custom alias", list.Select(item => item.GetProperty("model").GetString()).SequenceEqual(new[] { "fixture-native", alias }));
            check("Custom entries never replace the native default", list.Single(item => item.GetProperty("isDefault").GetBoolean()).GetProperty("model").GetString() == "fixture-native");
            check("Catalog confirmation follows the completed native list response", await DesktopBackendCatalog.ConfirmAsync(directory, DesktopBackendCatalog.Revision(store.Read()), cancellation) == ModelCatalogRefreshState.Confirmed);
            var observed = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, DesktopBackendCatalog.ObservedFileName)))!;
            var nativePid = observed["nativePid"]!.GetValue<int>();
            var started = await rpc.RequestAsync("thread/start", new { model = "fixture-native", cwd = home, approvalPolicy = "never", sandbox = "read-only" }, cancellation);
            var thread = started.GetProperty("thread").GetProperty("id").GetString()!;
            async Task Turn(string selected)
            {
                var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Notification(string method, JsonElement value)
                { if (method == "turn/completed" && value.GetProperty("threadId").GetString() == thread) done.TrySetResult(value.Clone()); }
                rpc.Notification += Notification;
                try
                {
                    await rpc.RequestAsync("turn/start", new { threadId = thread, model = selected, effort = ModelRouting.Owned(selected) ? "none" : "low", input = new[] { new { type = "text", text = "Reply with fixture-ok." } } }, cancellation);
                    var result = await done.Task.WaitAsync(cancellation);
                    if (result.GetProperty("turn").GetProperty("status").GetString() != "completed") throw new Exception("Backend fixture turn failed: " + result.GetRawText());
                }
                finally { rpc.Notification -= Notification; }
            }
            await Turn("fixture-native");
            var nativeCalls = native.Upstream.Requests.Count;
            await Turn(alias);
            check("The same conversation changes to custom inference without routing through native HTTP", custom.Upstream.Requests.Count == 1 && native.Upstream.Requests.Count == nativeCalls);
            check("Custom inference uses the custom credential and upstream ID", custom.Upstream.Requests.Single().Token == provider.ApiKey && JsonNode.Parse(custom.Upstream.Requests.Single().Body!)!["model"]!.GetValue<string>() == model.UpstreamId);
            var nativeBody = native.Upstream.Requests.Last().Body!;
            var customBody = custom.Upstream.Requests.Single().Body!;
            bool CodeMode(string body) => JsonNode.Parse(body)!["tools"]!.AsArray().Any(tool => tool?["name"]?.GetValue<string>() == "exec" && tool?["type"]?.GetValue<string>() == "custom");
            check("Custom inference uses native code-mode execution while the native model keeps its original tool contract", !CodeMode(nativeBody) && CodeMode(customBody));
            await rpc.RequestAsync("thread/settings/update", new { threadId = thread, model = "fixture-native", effort = "low" }, cancellation);
            await Turn("fixture-native");
            check("The same conversation returns to direct native inference", native.Upstream.Requests.Count == nativeCalls + 1 && custom.Upstream.Requests.Count == 1);
            await manager.SetModelEnabledAsync(alias, false, cancellation);
            check("Hide removes only the custom picker entry immediately", (await Models()).Select(item => item.GetProperty("model").GetString()).SequenceEqual(new[] { "fixture-native" }));
            await Turn(alias);
            check("Hidden model aliases retain their existing conversation route", custom.Upstream.Requests.Count == 2);
            await manager.SetModelEnabledAsync(alias, true, cancellation);
            check("Show restores the same alias without restarting", (await Models()).Any(item => item.GetProperty("model").GetString() == alias));
            var extra = new ProviderModel { UpstreamId = "fixture-added", Metadata = ProviderCatalog.Generic("fixture-added") };
            store.Update(settings => settings.Providers.Single().Models.Add(extra));
            await DesktopBackendCatalog.PublishAsync(directory, store.Read(), cancellation);
            check("A newly registered model appears in the running backend", (await Models()).Any(item => item.GetProperty("model").GetString() == extra.Alias(provider.Id)));
            await manager.RemoveAsync(provider.Id, cancellation);
            check("Deletion removes the complete custom set while preserving native models", (await Models()).Length == 1);
            var refused = false;
            try { await Turn(alias); } catch (CodexRequestException error) { refused = error.Message.Contains("removed", StringComparison.OrdinalIgnoreCase); }
            check("A removed fixed alias fails explicitly without retargeting the request", refused && custom.Upstream.Requests.Count == 2);
            await manager.DisposeAsync();
            await Turn("fixture-native");
            check("Native inference remains available after the Switch manager is stopped", native.Upstream.Requests.Count == nativeCalls + 2);
            await Models();
            var final = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, DesktopBackendCatalog.ObservedFileName)))!;
            check("Model edits and provider changes keep both backend process identities", pid == rpc.OwnedProcessId && final["nativePid"]!.GetValue<int>() == nativePid && !Process.GetProcessById(nativePid).HasExited);
            check("Native authentication and root configuration remain byte-for-byte unchanged", File.ReadAllText(Path.Combine(home, "auth.json")) == auth && File.ReadAllText(Path.Combine(home, "config.toml")) == config);
            store.Update(settings => settings.Providers.Add(provider));
            await using var contextManager = new ModelProviderManager(directory, (_, _) => null, codexHome: home);
            await DesktopBackendCatalog.PublishAsync(directory, store.Read(), cancellation);
            await ModelContextContracts.Run(rpc, contextManager, native, custom, alias, home, cancellation, check);
            await Models();
            var contextFinal = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, DesktopBackendCatalog.ObservedFileName)))!;
            check("Context and compaction changes preserve both process identities and original authentication/configuration", pid == rpc.OwnedProcessId && contextFinal["nativePid"]!.GetValue<int>() == nativePid &&
                File.ReadAllText(Path.Combine(home, "auth.json")) == auth && File.ReadAllText(Path.Combine(home, "config.toml")) == config);
        }
        finally { Environment.SetEnvironmentVariable("CODEX_SWITCH_BACKEND_CONFIG", originalEnvironment); }
    }
}
