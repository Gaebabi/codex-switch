using CodexAccountDesk;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ModelContextContracts
{
    internal static async Task Run(CodexRpc rpc, ModelProviderManager manager, NativeInferenceFixture native, NativeInferenceFixture custom,
        string alias, string home, CancellationToken cancellation, Action<string, bool> check)
    {
        var invalid = 0;
        foreach (var values in new[] { ("0", ""), ("-1", ""), ("1.5", ""), ("100", "101"), ("abc", ""), (long.MaxValue.ToString(), "") })
            try { ModelContextOptions.Parse(values.Item1, values.Item2); } catch (ArgumentException) { invalid++; }
        check("Invalid context sizes and compaction thresholds are rejected before saving", invalid == 6);
        native.Upstream.AllowNativeCompaction = true;
        native.Upstream.InputTokens = custom.Upstream.InputTokens = 1000;
        async Task<string> Start(string model)
        {
            var result = await rpc.RequestAsync("thread/start", new { model, cwd = home, approvalPolicy = "never", sandbox = "read-only" }, cancellation);
            return result.GetProperty("thread").GetProperty("id").GetString()!;
        }
        async Task<(long? Window, int Compactions)> Turn(string thread, string model)
        {
            var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            long? window = null; var compactions = 0; var sawUsage = false;
            void Notification(string method, JsonElement value)
            {
                if (!value.TryGetProperty("threadId", out var id) || id.GetString() != thread) return;
                if (method == "thread/tokenUsage/updated")
                {
                    var usage = value.GetProperty("tokenUsage"); sawUsage = true;
                    window = usage.TryGetProperty("modelContextWindow", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : null;
                }
                if (method == "item/completed" && value.GetProperty("item").GetProperty("type").GetString() == "contextCompaction") compactions++;
                if (method == "turn/completed") done.TrySetResult(value.Clone());
            }
            rpc.Notification += Notification;
            try
            {
                await rpc.RequestAsync("turn/start", new { threadId = thread, model, effort = "none", input = new[] { new { type = "text", text = "Reply with fixture-ok and retain this context-setting check." } } }, cancellation);
                var result = await done.Task.WaitAsync(cancellation);
                if (result.GetProperty("turn").GetProperty("status").GetString() != "completed" || !sawUsage)
                    throw new Exception("Context fixture turn did not complete with native token usage: " + result.GetRawText());
                return (window, compactions);
            }
            finally { rpc.Notification -= Notification; }
        }
        var nativeThread = await Start("fixture-native");
        var original = await Turn(nativeThread, "fixture-native");
        await manager.SaveContextAsync(ModelProviderManager.NativeContextTarget, new(128000, 100000), cancellation);
        check("An existing native conversation immediately uses the saved context size", (await Turn(nativeThread, "fixture-native")).Window == 121600);
        await manager.SaveContextAsync(ModelProviderManager.NativeContextTarget, new(64000, 50000), cancellation);
        check("A second native context edit applies to the same conversation without a restart", (await Turn(nativeThread, "fixture-native")).Window == 60800);
        await manager.SaveContextAsync(ModelProviderManager.NativeContextTarget, new(64000, 64), cancellation);
        var compacted = await Turn(nativeThread, "fixture-native");
        check("Lowering the native compaction threshold executes actual native automatic compaction", compacted.Compactions > 0 && native.Upstream.Requests.Any(request =>
            request.Path.EndsWith("/compact", StringComparison.Ordinal) || JsonNode.Parse(request.Body!)?["input"] is JsonArray input && input.Any(item => item?["type"]?.GetValue<string>() == "compaction_trigger")));
        await manager.SaveContextAsync(ModelProviderManager.NativeContextTarget, null, cancellation);
        var nativeRestored = await Turn(nativeThread, "fixture-native");
        check("Clearing native overrides restores the original context and compaction behavior", nativeRestored.Window == original.Window && nativeRestored.Compactions == 0);
        var customThread = await Start(alias);
        var customOriginal = await Turn(customThread, alias);
        await manager.SaveContextAsync(alias, new(128000, 100000), cancellation);
        check("Registered models use their own live context sizes", (await Turn(customThread, alias)).Window == 121600);
        await manager.SaveContextAsync(alias, new(64000, 40000), cancellation);
        check("Custom context changes reach already loaded conversations", (await Turn(customThread, alias)).Window == 60800);
        custom.Upstream.BlockNext = true;
        var active = Turn(customThread, alias);
        await custom.Upstream.Entered.Task.WaitAsync(cancellation);
        await manager.SaveContextAsync(alias, new(32000, 20000), cancellation);
        custom.Upstream.Release.TrySetResult();
        check("Saving during inference preserves the active request and applies on the next turn", (await active).Window == 60800 && (await Turn(customThread, alias)).Window == 30400);
        await manager.SaveContextAsync(alias, new(32000, 64), cancellation);
        check("Custom automatic compaction uses the newly saved threshold", (await Turn(customThread, alias)).Compactions > 0);
        await manager.SaveContextAsync(alias, null, cancellation);
        var customRestored = await Turn(customThread, alias);
        check("Clearing custom overrides restores provider context and compaction metadata", customRestored.Window == customOriginal.Window && customRestored.Compactions == 0);
    }
}
