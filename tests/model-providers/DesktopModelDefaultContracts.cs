using CodexAccountDesk;

internal static class DesktopModelDefaultContracts
{
    internal static async Task Run(CodexRpc rpc, string backend, string nativeExecutable, string home, string directory,
        string alias, CancellationToken cancellation, Action<string, bool> check)
    {
        var configPath = Path.Combine(home, "config.toml");
        var original = File.ReadAllBytes(configPath);
        var auth = File.ReadAllBytes(Path.Combine(home, "auth.json"));
        var store = new ModelProviderStore(directory);
        async Task<string> Version()
        {
            var read = await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation);
            return read.GetProperty("layers").EnumerateArray().Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "user" &&
                JsonValue.String(layer.GetProperty("name"), "profile") == null).GetProperty("version").GetString()!;
        }
        var version = await Version();
        var saved = await rpc.RequestAsync("config/batchWrite", new
        {
            expectedVersion = version, filePath = configPath, reloadUserConfig = true,
            edits = new object[]
            {
                new { keyPath = "model", value = alias, mergeStrategy = "replace" },
                new { keyPath = "model_reasoning_effort", value = "none", mergeStrategy = "replace" }
            }
        }, cancellation);
        check("Desktop custom model and effort persistence leave the native configuration byte-identical",
            JsonValue.String(saved, "status") == "ok" && File.ReadAllBytes(configPath).SequenceEqual(original));
        check("Desktop custom defaults are stored in the existing protected provider store", store.Read().DesktopModelDefaults?["model"]?.GetValue<string>() == alias &&
            store.Read().DesktopModelDefaults?["model_reasoning_effort"]?.GetValue<string>() == "none");
        var visible = await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation);
        check("Only the desktop effective model reflects custom defaults; native layer provenance is preserved",
            visible.GetProperty("config").GetProperty("model").GetString() == alias &&
            visible.GetProperty("layers").EnumerateArray().Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "user").GetProperty("config").GetProperty("model").GetString() == "fixture-native");
        await using (var native = new CodexRpc(nativeExecutable, home, preserveProvider: true))
        {
            await native.InitializeAsync(cancellation);
            var direct = await native.RequestAsync("config/read", new { includeLayers = false }, cancellation);
            check("A simultaneous ordinary native client retains its native model and effort",
                direct.GetProperty("config").GetProperty("model").GetString() == "fixture-native" &&
                direct.GetProperty("config").GetProperty("model_reasoning_effort").GetString() == "max");
        }
        await using (var reopened = new CodexRpc(backend, home, preserveProvider: true))
        {
            await reopened.InitializeAsync(cancellation);
            var read = await reopened.RequestAsync("config/read", new { includeLayers = false }, cancellation);
            check("Restarted desktop adapters restore custom defaults without changing native settings", read.GetProperty("config").GetProperty("model").GetString() == alias);
            var thread = await reopened.RequestAsync("thread/start", new { cwd = home, approvalPolicy = "never", sandbox = "read-only" }, cancellation);
            check("A desktop conversation without an explicit model uses its scoped custom default", thread.GetProperty("model").GetString() == alias &&
                thread.GetProperty("reasoningEffort").GetString() == "none");
        }
        var rejected = false;
        try
        {
            await rpc.RequestAsync("config/value/write", new { keyPath = "model", value = ModelRouting.Model, mergeStrategy = "replace", expectedVersion = "stale-version" }, cancellation);
        }
        catch (CodexRequestException) { rejected = true; }
        check("Stale native config versions cannot replace protected desktop defaults", rejected && store.Read().DesktopModelDefaults?["model"]?.GetValue<string>() == alias);
        rejected = false;
        try
        {
            await rpc.RequestAsync("config/value/write", new { keyPath = "model", value = "cs_removed", mergeStrategy = "replace", expectedVersion = await Version() }, cancellation);
        }
        catch (CodexRequestException) { rejected = true; }
        check("Removed aliases cannot become new desktop defaults", rejected && store.Read().DesktopModelDefaults?["model"]?.GetValue<string>() == alias);
        var effortSaved = await rpc.RequestAsync("config/value/write", new { keyPath = "model_reasoning_effort", value = "low", mergeStrategy = "replace", expectedVersion = await Version() }, cancellation);
        check("Single-value custom effort writes remain scoped to the desktop", JsonValue.String(effortSaved, "status") == "ok" &&
            File.ReadAllBytes(configPath).SequenceEqual(original) && store.Read().DesktopModelDefaults?["model_reasoning_effort"]?.GetValue<string>() == "low");
        var mixed = await rpc.RequestAsync("config/batchWrite", new
        {
            expectedVersion = await Version(), edits = new object[]
            {
                new { keyPath = "model", value = alias, mergeStrategy = "replace" },
                new { keyPath = "model_reasoning_effort", value = "none", mergeStrategy = "replace" },
                new { keyPath = "hide_agent_reasoning", value = true, mergeStrategy = "replace" }
            }
        }, cancellation);
        var nativeRead = await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation);
        var raw = nativeRead.GetProperty("layers").EnumerateArray().Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "user").GetProperty("config");
        check("Mixed desktop writes persist unrelated settings through the native versioned writer", JsonValue.String(mixed, "status") == "ok" &&
            raw.GetProperty("hide_agent_reasoning").GetBoolean() && raw.GetProperty("model").GetString() == "fixture-native" && raw.GetProperty("model_reasoning_effort").GetString() == "max");
        await rpc.RequestAsync("config/batchWrite", new
        {
            expectedVersion = await Version(), edits = new object[]
            {
                new { keyPath = "model", value = "fixture-native", mergeStrategy = "replace" },
                new { keyPath = "model_reasoning_effort", value = "max", mergeStrategy = "replace" },
                new { keyPath = "hide_agent_reasoning", value = (object?)null, mergeStrategy = "replace" }
            }
        }, cancellation);
        check("Selecting a native default clears desktop-only state and restores the original native configuration",
            store.Read().DesktopModelDefaults == null && File.ReadAllBytes(configPath).SequenceEqual(original));
        check("Desktop model persistence never changes native credentials", File.ReadAllBytes(Path.Combine(home, "auth.json")).SequenceEqual(auth));
    }
}
