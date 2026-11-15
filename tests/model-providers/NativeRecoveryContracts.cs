using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class NativeRecoveryContracts
{
    internal static async Task RunNative(string executable, string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "native-recovery-home"); var directory = Path.Combine(root, "native-recovery-store");
        Directory.CreateDirectory(home); Directory.CreateDirectory(directory);
        await using var control = new WorkspaceFixture();
        var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fixture@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-recovery-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + claims + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        File.WriteAllText(Path.Combine(home, "auth.json"), auth);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model=\"native-model\"\ncli_auth_credentials_store=\"file\"\nchatgpt_base_url=" + JsonSerializer.Serialize(control.BaseUrl) + "\n[features]\nshell_tool=false\ncode_mode=false\nenable_request_compression=false\n");
        var config = File.ReadAllBytes(Path.Combine(home, "config.toml"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var cancellation = timeout.Token;
        var upstream = new RecoveryUpstream();
        using var http = new HttpClient(upstream);
        GatewayIdentity? Authenticate(string? header, string? account) => GatewayIdentity.Authenticate(header, account, () => auth);
        var store = new ModelProviderStore(directory);
        string id;
        await using (var initialGateway = new ModelGateway(store, Authenticate, http, directory, () => home))
        {
            initialGateway.Start();
            await using var rpc = new CodexRpc(executable, home, true, new Dictionary<string, string> { ["openai_base_url"] = JsonSerializer.Serialize(initialGateway.BaseUrl) });
            await rpc.InitializeAsync(cancellation);
            id = (await rpc.RequestAsync("thread/start", new { cwd = home, model = "native-model", approvalPolicy = "never", sandbox = "read-only" }, cancellation)).GetProperty("thread").GetProperty("id").GetString()!;
            var finished = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            rpc.Notification += (method, value) => { if (method == "turn/completed" && CodexAccountDesk.JsonValue.String(value, "threadId") == id) finished.TrySetResult(value.Clone()); };
            await rpc.RequestAsync("turn/start", new { threadId = id, input = new[] { new { type = "text", text = "Remember RECOVERED_CONTEXT_63" } } }, cancellation);
            var turn = await finished.Task.WaitAsync(cancellation);
            if (turn.GetProperty("turn").GetProperty("status").GetString() != "completed")
                throw new Exception("Fictional native fixture failed: " + turn.GetRawText() + "; gateway=" + initialGateway.LastError);
            check("The disposable native conversation is durably created by an actual turn", turn.GetProperty("turn").GetProperty("status").GetString() == "completed");
        }
        var identity = NativeHistoryRecovery.ReadIdentity(home);
        var portable = new GatewayHistoryStore(directory).CreatePortable(GatewayHistoryStore.Conversation(id, identity), new("openai", "native-model"), "RECOVERED_CONTEXT_63");
        upstream.OutputToken = portable;
        await using (var gateway = new ModelGateway(store, Authenticate, http, directory, () => home))
        {
            gateway.Start();
            await using var rpc = new CodexRpc(executable, home, true, new Dictionary<string, string> { ["openai_base_url"] = JsonSerializer.Serialize(gateway.BaseUrl) });
            await rpc.InitializeAsync(cancellation);
            await rpc.RequestAsync("thread/resume", new { threadId = id, excludeTurns = true }, cancellation);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rpc.Notification += (method, value) =>
            {
                if (CodexAccountDesk.JsonValue.String(value, "threadId") != id) return;
                if (method == "error") done.TrySetException(new Exception(value.GetRawText()));
                if (method == "item/completed" && CodexAccountDesk.JsonValue.String(value.GetProperty("item"), "type") == "contextCompaction") done.TrySetResult();
            };
            await rpc.RequestAsync("thread/compact/start", new { threadId = id }, cancellation);
            await done.Task.WaitAsync(cancellation);
        }
        var candidates = NativeHistoryRecovery.Find(home, directory, cancellation, id);
        check("Actual native persisted history reproduces the legacy Switch checkpoint", candidates.Count == 1 && candidates[0].Checkpoints.SequenceEqual(new[] { portable }));
        var vault = Path.Combine(directory, "model-history.vault"); var savedVault = vault + ".fixture-backup";
        File.Move(vault, savedVault);
        var missingRefused = false;
        try { NativeHistoryRecovery.Find(home, directory, cancellation, id); }
        catch (InvalidDataException) { missingRefused = true; }
        finally { File.Move(savedVault, vault); }
        check("Recovery preflight rejects an unavailable protected summary before changing configuration", missingRefused && File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config));
        File.Move(vault, savedVault);
        new GatewayHistoryStore(directory).CreatePortable(GatewayHistoryStore.Conversation(Guid.NewGuid().ToString(), identity), new("openai", "native-model"), "Unrelated fixture summary");
        var partialRefused = false;
        try { NativeHistoryRecovery.Find(home, directory, cancellation, id); }
        catch (InvalidDataException) { partialRefused = true; }
        finally { File.Delete(vault); File.Move(savedVault, vault); }
        check("A partially restored store cannot hide an unresolved checkpoint from migration preflight", partialRefused);
        var files = Directory.EnumerateFiles(Path.Combine(home, "sessions"), "*.jsonl", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        upstream.FailCompaction = true;
        var failed = false;
        try { await NativeHistoryRecovery.RecoverAsync(executable, home, directory, store, Authenticate, http, candidates, cancellation); }
        catch (InvalidOperationException) { failed = true; }
        check("A rejected upstream compaction preserves the recoverable checkpoint and root connection", failed && NativeHistoryRecovery.Find(home, directory, cancellation, id).Count == 1 && File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config));
        upstream.FailCompaction = false;
        upstream.OutputToken = "native-encrypted-fixture";
        var destinationCandidates = NativeHistoryRecovery.Find(home, directory, cancellation, id, "native-model");
        check("Explicit native handoff retains the exact conversation and checkpoint set", destinationCandidates.Count == 1 && destinationCandidates[0].ThreadId == id && destinationCandidates[0].Checkpoints.SequenceEqual(candidates[0].Checkpoints));
        var invalidDestination = false;
        try { NativeHistoryRecovery.Find(home, directory, cancellation, id, "cs_invalid_destination"); }
        catch (ArgumentException) { invalidDestination = true; }
        check("Native handoff refuses a custom destination before compaction", invalidDestination);
        await NativeHistoryRecovery.RecoverAsync(executable, home, directory, store, Authenticate, http, destinationCandidates, cancellation, "native-model");
        check("Actual native recovery durably replaces the active checkpoint through official compaction", NativeHistoryRecovery.Find(home, directory, cancellation, id).Count == 0 && upstream.Posts.Last()["input"]!.ToJsonString().Contains("RECOVERED_CONTEXT_63") && !upstream.Posts.Last().ToJsonString().Contains(GatewayHistoryStore.Prefix));
        check("Native recovery preserves every original history byte, root config and sign-in", files.All(pair => File.ReadAllBytes(pair.Key).AsSpan().StartsWith(pair.Value)) && File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config) && File.ReadAllText(Path.Combine(home, "auth.json")) == auth);
        await using (var inspect = new CodexRpc(executable, home, preserveProvider: true))
        {
            await inspect.InitializeAsync(cancellation);
            var turns = await inspect.RequestAsync("thread/turns/list", new { threadId = id, limit = 1 }, cancellation);
            check("Recovery waits for the native turn to finish before closing its client", turns.GetProperty("data")[0].GetProperty("status").GetString() == "completed");
        }
        var count = upstream.Posts.Count;
        await NativeHistoryRecovery.RecoverAsync(executable, home, directory, store, Authenticate, http, candidates, cancellation);
        check("Native recovery rerun performs no inference after durable completion", count == upstream.Posts.Count);
        var customModel = new ProviderModel { UpstreamId = "reasoning-fixture", Metadata = ProviderCatalog.Generic("reasoning-fixture") };
        var customProvider = new ModelProvider { Name = "Reasoning fixture", BaseUrl = "https://custom.example.invalid/v1", ApiKey = "fictional-custom-key", Models = new() { customModel } };
        store.Update(next => next.Providers.Add(customProvider));
        string customThread;
        upstream.ForeignReasoning = true;
        upstream.ForeignTool = true;
        await using (var gateway = new ModelGateway(store, Authenticate, http, directory, () => home))
        {
            gateway.Start();
            await using var rpc = new CodexRpc(executable, home, true, new Dictionary<string, string> { ["openai_base_url"] = JsonSerializer.Serialize(gateway.BaseUrl) });
            await rpc.InitializeAsync(cancellation);
            customThread = (await rpc.RequestAsync("thread/start", new { cwd = home, model = customModel.Alias(customProvider.Id), approvalPolicy = "never", sandbox = "read-only" }, cancellation)).GetProperty("thread").GetProperty("id").GetString()!;
            var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            rpc.Notification += (method, value) => { if (method == "turn/completed" && CodexAccountDesk.JsonValue.String(value, "threadId") == customThread) done.TrySetResult(value.Clone()); };
            await rpc.RequestAsync("turn/start", new { threadId = customThread, effort = "none", input = new[] { new { type = "text", text = "Keep CUSTOM_VISIBLE_MESSAGE_71" } } }, cancellation);
            check("The actual native engine persists a custom provider reasoning item", (await done.Task.WaitAsync(cancellation)).GetProperty("turn").GetProperty("status").GetString() == "completed");
        }
        upstream.ForeignReasoning = false;
        var reasoningCandidates = NativeHistoryRecovery.Find(home, directory, cancellation, customThread, "native-model");
        check("Native handoff detects provider-specific reasoning even without a portable checkpoint", reasoningCandidates.Count == 1 && reasoningCandidates[0].Checkpoints.Length == 0 && reasoningCandidates[0].ForeignReasoning?.Length == 1);
        check("Native handoff identifies custom output IDs from the persisted provider turn", reasoningCandidates.Single().ForeignItemIds?.Length >= 1);
        var reasoningFiles = Directory.EnumerateFiles(Path.Combine(home, "sessions"), "*.jsonl", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        upstream.OutputToken = "native-reasoning-fixture";
        await NativeHistoryRecovery.RecoverAsync(executable, home, directory, store, Authenticate, http, reasoningCandidates, cancellation, "native-model");
        check("Official native compaction converts the foreign reasoning boundary while preserving visible history", NativeHistoryRecovery.Find(home, directory, cancellation, customThread, "native-model").Count == 0 &&
            upstream.Posts.Last()["input"]!.ToJsonString().Contains("CUSTOM_VISIBLE_MESSAGE_71") && !upstream.Posts.Last().ToJsonString().Contains("Fictional provider-specific reasoning fixture."));
        var imported = upstream.Posts.Last()["input"]!.AsArray().OfType<JsonObject>().ToArray();
        var importedCall = imported.Single(item => item["type"]?.GetValue<string>() == "custom_tool_call");
        check("Actual native compaction imports the foreign call without its item ID and retains its result link", !importedCall.ContainsKey("id") && importedCall["call_id"]!.GetValue<string>() == "foreign-call" &&
            imported.Any(item => item["type"]?.GetValue<string>() == "custom_tool_call_output" && item["call_id"]?.GetValue<string>() == "foreign-call"));
        check("Reasoning handoff appends official history without changing original bytes, settings or authentication", reasoningFiles.All(pair => File.ReadAllBytes(pair.Key).AsSpan().StartsWith(pair.Value)) &&
            File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config) && File.ReadAllText(Path.Combine(home, "auth.json")) == auth);
        count = upstream.Posts.Count;
        await NativeHistoryRecovery.RecoverAsync(executable, home, directory, store, Authenticate, http, reasoningCandidates, cancellation, "native-model");
        check("A completed reasoning handoff is durable and idempotent", upstream.Posts.Count == count);
    }

    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "native-recovery"); Directory.CreateDirectory(directory);
        var identity = new GatewayIdentity("fictional-recovery-token", "fictional-recovery-account", false);
        var store = new ModelProviderStore(directory);
        var upstream = new RecoveryUpstream();
        var client = new HttpClient(upstream);
        GatewayIdentity? Authenticate(string? header, string? account) => header == "Bearer " + identity.Token && account == identity.AccountId ? identity : null;
        var session = Guid.NewGuid().ToString();
        var history = new GatewayHistoryStore(directory);
        var token = history.CreatePortable(GatewayHistoryStore.Conversation(session, identity), new("openai", "native-model"), "RECOVERED_CONTEXT_63 with original permissions.");
        store.Update(next => { next.IsolatedProfile = true; next.Port = 0; });
        var snapshot = File.ReadAllBytes(Path.Combine(directory, "model-providers.vault"));
        var candidate = new NativeHistoryRecovery.Candidate(session, "native-model", "fictional-rollout", new[] { token });
        await using var gateway = new ModelGateway(store, Authenticate, client, directory, () => null, candidate);
        gateway.Start();
        using var local = new HttpClient();
        async Task<HttpResponseMessage> Send(string target, string model, bool compact, bool legacy = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, gateway.BaseUrl + (legacy ? "/responses/compact" : "/responses"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
            request.Headers.Add("ChatGPT-Account-ID", identity.AccountId); request.Headers.Add("session_id", target); request.Headers.Add("version", "0.167.0");
            var input = new JsonArray(new JsonObject { ["type"] = "compaction", ["encrypted_content"] = token },
                new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = "LATER_MESSAGE_92" });
            if (compact && !legacy) input.Add(new JsonObject { ["type"] = "compaction_trigger" });
            request.Content = new StringContent(new JsonObject { ["model"] = model, ["stream"] = !legacy, ["input"] = input }.ToJsonString(), Encoding.UTF8, "application/json");
            return await local.SendAsync(request);
        }
        using (var other = await Send(Guid.NewGuid().ToString(), "native-model", true))
            check("Recovery refuses another conversation before inference", other.StatusCode == HttpStatusCode.BadRequest && upstream.Posts.Count == 0);
        using (var ordinary = await Send(session, "native-model", false))
            check("Recovery cannot resume ordinary work or execute tools", ordinary.StatusCode == HttpStatusCode.BadRequest && upstream.Posts.Count == 0);
        using (var custom = await Send(session, "cs_unregistered", true))
            check("Recovery refuses a different model before inference", custom.StatusCode == HttpStatusCode.BadRequest && upstream.Posts.Count == 0);
        using (var result = await Send(session, "native-model", true))
        {
            var text = await result.Content.ReadAsStringAsync();
            check("Native V2 compaction preserves the upstream encrypted output", result.IsSuccessStatusCode && text == RecoveryUpstream.Stream);
            var input = upstream.Posts.Single()["input"]!.AsArray();
            check("Native recovery expands only the checkpoint at its original position", input.Count == 3 && input[0]!["type"]!.GetValue<string>() == "message" && input[0]!.ToJsonString().Contains("RECOVERED_CONTEXT_63") &&
                input[1]!["content"]!.GetValue<string>() == "LATER_MESSAGE_92" && input[2]!["type"]!.GetValue<string>() == "compaction_trigger" && !input.ToJsonString().Contains(GatewayHistoryStore.Prefix));
        }
        using (var result = await Send(session, "native-model", true, true))
            check("Legacy native compact preserves the upstream response instead of inventing a checkpoint", result.IsSuccessStatusCode && await result.Content.ReadAsStringAsync() == RecoveryUpstream.CompactJson);
        check("Recovery does not change the provider profile, port or credentials", File.ReadAllBytes(Path.Combine(directory, "model-providers.vault")).SequenceEqual(snapshot));
        using var foreign = JsonDocument.Parse("""{"type":"reasoning","id":"rs_foreign","summary":[],"content":[{"type":"reasoning_text","text":"fictional private provider state"}]}""");
        using var foreignCall = JsonDocument.Parse("""{"type":"custom_tool_call","id":"fc_foreign","name":"exec","call_id":"retained","input":"visible program"}""");
        var reasoningCandidate = new NativeHistoryRecovery.Candidate(session, "native-model", "fictional-rollout", Array.Empty<string>(), new[] { NativeReasoningHandoff.Key(foreign.RootElement) }, new[] { NativeReasoningHandoff.OutputKey(foreignCall.RootElement) });
        await using (var reasoningGateway = new ModelGateway(store, Authenticate, client, directory, () => null, reasoningCandidate))
        {
            reasoningGateway.Start();
            var input = new JsonArray(JsonNode.Parse(foreign.RootElement.GetRawText()),
                JsonNode.Parse("""{"type":"reasoning","id":"rs_native","summary":[],"encrypted_content":"original-native-state"}"""),
                JsonNode.Parse("""{"type":"message","role":"user","content":"visible task"}"""),
                JsonNode.Parse(foreignCall.RootElement.GetRawText()),
                JsonNode.Parse("""{"type":"custom_tool_call_output","call_id":"retained","output":"visible result"}"""),
                JsonNode.Parse("""{"type":"function_call","id":"fc_native","name":"lookup","call_id":"native","arguments":"{}"}"""),
                JsonNode.Parse("""{"type":"compaction_trigger"}"""));
            async Task<HttpResponseMessage> ReasoningRequest()
            {
                var request = new HttpRequestMessage(HttpMethod.Post, reasoningGateway.BaseUrl + "/responses");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
                request.Headers.Add("ChatGPT-Account-ID", identity.AccountId); request.Headers.Add("session_id", session); request.Headers.Add("version", "0.167.0");
                request.Content = new StringContent(new JsonObject { ["model"] = "native-model", ["stream"] = true, ["input"] = input.DeepClone() }.ToJsonString(), Encoding.UTF8, "application/json");
                return await local.SendAsync(request);
            }
            using (var result = await ReasoningRequest())
            {
                var forwarded = upstream.Posts.Last()["input"]!.AsArray();
                var expectedCall = input[3]!.DeepClone().AsObject(); expectedCall.Remove("id");
                check("Native compaction excludes only fingerprint-matched foreign state and preserves native reasoning, messages and tool pairs", result.IsSuccessStatusCode && forwarded.Count == 6 &&
                    JsonNode.DeepEquals(forwarded[0], input[1]) && JsonNode.DeepEquals(forwarded[1], input[2]) && JsonNode.DeepEquals(forwarded[2], expectedCall) && JsonNode.DeepEquals(forwarded[3], input[4]) && JsonNode.DeepEquals(forwarded[4], input[5]));
            }
            input[3]!["input"] = "changed program";
            var beforeCalls = upstream.Posts.Count;
            using (var result = await ReasoningRequest())
                check("Changed foreign tool content is rejected before native inference", result.StatusCode == HttpStatusCode.BadRequest && upstream.Posts.Count == beforeCalls);
            input[3]!["input"] = "visible program";
            input[0]!["content"]![0]!["text"] = "changed after preflight";
            var beforePosts = upstream.Posts.Count;
            using (var result = await ReasoningRequest())
                check("Changed foreign reasoning is rejected before native inference", result.StatusCode == HttpStatusCode.BadRequest && upstream.Posts.Count == beforePosts);
        }
        var sessions = Path.Combine(root, "recovery-chain", "sessions"); Directory.CreateDirectory(sessions);
        var home = Path.GetDirectoryName(sessions)!;
        var parent = Guid.NewGuid().ToString(); var file = Path.Combine(sessions, "rollout-" + session + "_" + parent + ".jsonl");
        string Meta(object? historyBase = null, string? account = "fictional-recovery-account") => JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = session, model_provider = "openai", creator_account_id = account, history_base = historyBase } }) + "\n";
        string Compact(string value) => JsonSerializer.Serialize(new { type = "compacted", payload = new { replacement_history = new[] { new { type = "compaction", encrypted_content = value } } } }) + "\n";
        var prefix = Meta() + Compact(token); File.WriteAllText(file, prefix + Compact("later-outside-history-bound"));
        var child = Path.Combine(sessions, "rollout-" + session + "_" + Guid.NewGuid() + ".jsonl");
        File.WriteAllText(child, Meta(new { thread_id = parent, end_byte_offset = Encoding.UTF8.GetByteCount(prefix) }));
        var found = NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None);
        check("Recovery follows the exact segmented history and excludes records after its boundary", found.Tokens.SequenceEqual(new[] { token }));
        File.AppendAllText(child, Compact("real-native-compaction"));
        check("A newer native checkpoint makes recovery idempotent without editing old history", NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None).Tokens.Length == 0);
        var foreignRecord = JsonSerializer.Serialize(new { type = "response_item", payload = foreign.RootElement }) + "\n";
        File.AppendAllText(child, JsonSerializer.Serialize(new { type = "turn_context", payload = new { model = "cs_fictional" } }) + "\n" + foreignRecord);
        check("Foreign reasoning after the latest native checkpoint requires a new handoff", NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None).ForeignReasoning.SequenceEqual(new[] { NativeReasoningHandoff.Key(foreign.RootElement) }));
        File.AppendAllText(child, Compact("next-native-compaction"));
        check("Official replacement history retires only the previous active reasoning state", NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None).ForeignReasoning.Length == 0);
        File.AppendAllText(child, JsonSerializer.Serialize(new { type = "response_item", payload = foreignCall.RootElement }) + "\n");
        var onlyIds = NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None);
        check("Foreign output IDs require handoff even with no foreign reasoning", onlyIds.ForeignReasoning.Length == 0 && onlyIds.ForeignItemIds.SequenceEqual(new[] { NativeReasoningHandoff.OutputKey(foreignCall.RootElement) }));
        File.AppendAllText(child, Compact("ids-retired"));
        check("Official compaction retires foreign item IDs without rewriting old records", NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None).ForeignItemIds.Length == 0);
        File.WriteAllText(child, Meta(new { thread_id = parent, end_byte_offset = Encoding.UTF8.GetByteCount(prefix) }, "different-account"));
        var refused = false;
        try { NativeHistoryRecovery.ReadLatest(home, child, session, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
        check("A cross-account history chain is refused", refused);
        var linkedHome = Path.Combine(root, "linked-recovery-home"); Directory.CreateDirectory(linkedHome);
        var physical = Path.Combine(root, "linked-recovery-storage"); Directory.CreateDirectory(physical);
        var alias = Path.Combine(linkedHome, "sessions"); LinkDirectory(alias, physical);
        try
        {
            var stored = Path.Combine(physical, "rollout-" + session + ".jsonl"); File.WriteAllText(stored, Meta() + Compact(token));
            check("Recovery accepts the native physical path beneath a configured history link", NativeHistoryRecovery.ReadLatest(linkedHome, stored, session, CancellationToken.None).Tokens.SequenceEqual(new[] { token }));
            check("Recovery resolves the configured history alias to the same record", NativeHistoryRecovery.ReadLatest(linkedHome, Path.Combine(alias, Path.GetFileName(stored)), session, CancellationToken.None).Tokens.SequenceEqual(new[] { token }));
            var linkedChild = Path.Combine(physical, "rollout-" + session + "_" + Guid.NewGuid() + ".jsonl");
            File.WriteAllText(linkedChild, Meta(new { thread_id = session, end_byte_offset = new FileInfo(stored).Length }));
            check("Recovery follows bounded parent segments through the configured linked root", NativeHistoryRecovery.ReadLatest(linkedHome, linkedChild, session, CancellationToken.None).Tokens.SequenceEqual(new[] { token }));
            File.WriteAllText(stored, Meta() + JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", content = token } }) + "\n");
            check("Mentioning a checkpoint in a linked native chat is not a recovery checkpoint", NativeHistoryRecovery.ReadLatest(linkedHome, stored, session, CancellationToken.None).Tokens.Length == 0);
            refused = false;
            try { NativeHistoryRecovery.ReadLatest(linkedHome, file, session, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
            check("A history file outside the configured linked roots is still refused", refused);
            var nested = Path.Combine(physical, "unconfigured-link"); LinkDirectory(nested, sessions);
            try
            {
                refused = false;
                try { NativeHistoryRecovery.ReadLatest(linkedHome, Path.Combine(nested, Path.GetFileName(file)), session, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
                check("A nested link cannot escape the configured history root", refused);
            }
            finally { Directory.Delete(nested); }
        }
        finally { Directory.Delete(alias); }
    }

    private static void LinkDirectory(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path $env:CODEX_TEST_HISTORY_LINK -Value $env:CODEX_TEST_HISTORY_TARGET -ErrorAction Stop | Out-Null");
        start.Environment["CODEX_TEST_HISTORY_LINK"] = link; start.Environment["CODEX_TEST_HISTORY_TARGET"] = target;
        using var process = System.Diagnostics.Process.Start(start)!;
        var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("History junction fixture creation failed: " + error);
    }

    internal sealed class RecoveryUpstream : HttpMessageHandler
    {
        internal const string CompactJson = "{\"id\":\"resp_native\",\"object\":\"response.compaction\",\"output\":[{\"id\":\"cmp_native\",\"type\":\"compaction\",\"encrypted_content\":\"native-encrypted-fixture\"}]}";
        internal static readonly string Stream = "data: " + JsonSerializer.Serialize(new { type = "response.output_item.done", output_index = 0, item = new { id = "cmp_native", type = "compaction", encrypted_content = "native-encrypted-fixture" } }) + "\n\ndata: " + JsonSerializer.Serialize(new { type = "response.completed", response = new { id = "resp_native", status = "completed", output = new[] { new { id = "cmp_native", type = "compaction", encrypted_content = "native-encrypted-fixture" } } } }) + "\n\n";
        internal List<JsonObject> Posts { get; } = new();
        internal string OutputToken { get; set; } = "native-encrypted-fixture";
        internal bool FailCompaction { get; set; }
        internal bool ForeignReasoning { get; set; }
        internal bool ForeignTool { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("native-model")) }.ToJsonString(), Encoding.UTF8, "application/json") };
            Posts.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject());
            var compact = request.RequestUri!.AbsolutePath.EndsWith("/compact");
            if (!compact && !Posts.Last()["input"]!.AsArray().OfType<JsonObject>().Any(item => item["type"]?.GetValue<string>() == "compaction_trigger"))
            {
                if (ForeignTool)
                {
                    ForeignTool = false;
                    var item = new { type = "custom_tool_call", id = "fc_foreign", name = "fictional_unregistered_tool", call_id = "foreign-call", input = "fictional program" };
                    var stream = "data: " + JsonSerializer.Serialize(new { type = "response.output_item.done", output_index = 0, item }) + "\n\ndata: " + JsonSerializer.Serialize(new { type = "response.completed", response = new { id = "resp_foreign_tool", status = "completed", output = new[] { item } } }) + "\n\n";
                    return new(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
                }
                return new(HttpStatusCode.OK) { Content = new StringContent(Upstreams.Stream(Posts.Last()["model"]!.GetValue<string>(), foreignReasoning: ForeignReasoning), Encoding.UTF8, "text/event-stream") };
            }
            if (FailCompaction) return new(HttpStatusCode.BadRequest) { Content = new StringContent("Fictional compaction rejected") };
            return new(HttpStatusCode.OK) { Content = new StringContent((compact ? CompactJson : Stream).Replace("native-encrypted-fixture", OutputToken), Encoding.UTF8, compact ? "application/json" : "text/event-stream") };
        }
    }
}
