using CodexAccountDesk;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ReasoningNativeContracts
{
    internal static async Task Run(string executable, string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "reasoning-native-home"); Directory.CreateDirectory(home);
        await using var workspace = new WorkspaceFixture();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fictional@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-reasoning-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + payload + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), auth);
        var native = ProviderCatalog.Generic("native-fixture"); var original = native.ToJsonString();
        var nativeCatalog = Path.Combine(home, "native-catalog.json");
        await File.WriteAllTextAsync(nativeCatalog, new JsonObject { ["models"] = new JsonArray(native) }.ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "model_provider=\"openai\"\nmodel=\"native-fixture\"\nmodel_catalog_json=" + JsonSerializer.Serialize(nativeCatalog) + "\ncli_auth_credentials_store=\"file\"\nchatgpt_base_url=" + JsonSerializer.Serialize(workspace.BaseUrl) + "\n");
        GatewayIdentity? Authenticate(string? token, string? account) => GatewayIdentity.Authenticate(token, account, () => auth);
        var upstream = new ReasoningControlContracts.ReasoningUpstream { IncludeSummaryCapability = false };
        var directory = Path.Combine(root, "reasoning-native-store");
        var catalogPath = Path.Combine(directory, "model-providers-desktop-catalog.json");
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream), home);
        var qwen = await manager.SaveAsync(null, "Native Qwen", "https://qwen.example.invalid/v1", "fictional-qwen-key", CancellationToken.None);
        var gemma = await manager.SaveAsync(null, "Native Gemma", "https://gemma.example.invalid/v1", "fictional-gemma-key", CancellationToken.None);
        manager.SelectRoute(qwen.Models.Single().Alias(qwen.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var cancellation = timeout.Token;
        var configBefore = await File.ReadAllTextAsync(Path.Combine(home, "config.toml"), cancellation);
        await DesktopProfileContracts.SeedLegacyAsync(executable, home, directory, manager, native, cancellation);
        check("A legacy combined-profile fixture uses the dynamic gateway catalog", LiveModelCatalog.Enabled(manager.Settings) &&
            !(await File.ReadAllTextAsync(Path.Combine(home, "config.toml"), cancellation)).Contains("model_catalog_json"));
        async Task<JsonElement> List(CodexRpc rpc) => await rpc.RequestAsync("model/list", new { includeHidden = true }, cancellation);
        string[] Efforts(JsonElement catalog) => catalog.GetProperty("data").EnumerateArray().Single(model => model.GetProperty("model").GetString() == ModelRouting.Model)
            .GetProperty("supportedReasoningEfforts").EnumerateArray().Select(level => level.GetProperty("reasoningEffort").GetString()!).ToArray();
        await using var legacyStatic = new CodexRpc(executable, home, preserveProvider: true,
            configOverrides: new Dictionary<string, string> { ["model_catalog_json"] = JsonSerializer.Serialize(catalogPath) });
        await legacyStatic.InitializeAsync(cancellation); await List(legacyStatic);
        await using var current = new CodexRpc(executable, home, preserveProvider: true); await current.InitializeAsync(cancellation);
        var qwenCatalog = await List(current);
        check("The actual native picker exposes Qwen Low, Medium and XHigh in the existing Switch entry", Efforts(qwenCatalog).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        check("Native model listing retains the legacy ID and both fixed Switch entries", qwenCatalog.GetProperty("data").GetArrayLength() == 4 && qwenCatalog.GetProperty("data")[0].GetProperty("model").GetString() == "native-fixture");
        var cachePath = Path.Combine(home, "models_cache.json");
        var cacheBefore = JsonNode.Parse(await File.ReadAllTextAsync(cachePath, cancellation))!.AsObject();
        var notifications = 0;
        var invalidatedBeforeNotification = true;
        manager.ModelCatalogChanged = async _ =>
        {
            notifications++; invalidatedBeforeNotification &= !File.Exists(cachePath);
            await List(current);
            return true;
        };
        await manager.SelectRouteAsync(gemma.Models.Single().Alias(gemma.Id), cancellation);
        check("Live invalidation removes the stale cache before notifying the desktop", notifications == 1 && invalidatedBeforeNotification);
        var gemmaCatalog = await List(current);
        var cacheAfter = JsonNode.Parse(await File.ReadAllTextAsync(cachePath, cancellation))!.AsObject();
        check("The native client regenerates its own opaque cache identity and version", JsonNode.DeepEquals(cacheBefore["identity"], cacheAfter["identity"]) && JsonNode.DeepEquals(cacheBefore["client_version"], cacheAfter["client_version"]));
        check("The same running native client immediately exposes Gemma None and Medium", Efforts(gemmaCatalog).SequenceEqual(new[] { "none", "medium" }));
        check("A previously loaded static client still needs the explicit one-time profile migration", Efforts(await List(legacyStatic)).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        check("Live refresh preserves the native picker entry byte for byte", gemmaCatalog.GetProperty("data")[0].GetRawText() == qwenCatalog.GetProperty("data")[0].GetRawText());
        await manager.SelectRouteAsync(qwen.Models.Single().Alias(qwen.Id), cancellation);
        check("Switching back updates the same native client without reinitialization", Efforts(await List(current)).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        await manager.SaveReasoningAsync(qwen.Id, qwen.Models.Single().UpstreamId, new() { Levels = new() { "low", "high" }, Default = "high" }, cancellation);
        check("Saving reasoning overrides updates the same native client's control", Efforts(await List(current)).SequenceEqual(new[] { "low", "high" }));
        await manager.SaveReasoningAsync(qwen.Id, qwen.Models.Single().UpstreamId, null, cancellation);
        check("Clearing an override restores provider capabilities live", Efforts(await List(current)).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        await manager.SaveAsync(qwen.Id, "Native Qwen", "https://qwen.example.invalid/v1", "fictional-qwen-key", cancellation);
        check("Provider rediscovery also invalidates the live model list", notifications == 5 && Efforts(await List(current)).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        var staleCache = await File.ReadAllBytesAsync(cachePath, cancellation);
        var raceNotifications = 0;
        manager.ModelCatalogChanged = async _ =>
        {
            if (++raceNotifications == 1) await File.WriteAllBytesAsync(cachePath, staleCache, cancellation);
            else await List(current);
            return true;
        };
        await manager.SelectRouteAsync(gemma.Models.Single().Alias(gemma.Id), cancellation);
        await manager.CatalogRefreshCompletion.WaitAsync(cancellation);
        check("A late previous catalog response cannot leave the live native client on stale capabilities", raceNotifications == 2 &&
            Efforts(await List(current)).SequenceEqual(new[] { "none", "medium" }));
        await manager.SelectRouteAsync(qwen.Models.Single().Alias(qwen.Id), cancellation);
        await manager.CatalogRefreshCompletion.WaitAsync(cancellation);
        staleCache = await File.ReadAllBytesAsync(cachePath, cancellation);
        raceNotifications = 0;
        manager.ModelCatalogChanged = async _ =>
        {
            if (++raceNotifications > 1) await List(current);
            return true;
        };
        await manager.SelectRouteAsync(gemma.Models.Single().Alias(gemma.Id), cancellation);
        check("Delayed native refresh remains pending after the foreground selection completes", manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Pending && !manager.CatalogRefreshCompletion.IsCompleted);
        await Task.Delay(700, cancellation);
        await File.WriteAllBytesAsync(cachePath, staleCache, cancellation);
        await manager.CatalogRefreshCompletion.WaitAsync(cancellation);
        check("A stale cache published beyond 500 ms is replaced through the same native client", raceNotifications == 2 &&
            manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Confirmed && Efforts(await List(current)).SequenceEqual(new[] { "none", "medium" }));
        var files = Directory.GetFiles(Path.Combine(root, "reasoning-native-store"), "*desktop-catalog.json");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(files.Single(), cancellation))!["models"]!.AsArray();
        check("Refreshing selected-model reasoning does not change any native model document", saved.OfType<JsonObject>().Single(model => model["slug"]!.GetValue<string>() == "native-fixture").ToJsonString() == original);
        check("Native reasoning setup and refresh preserve the original sign-in file", await File.ReadAllTextAsync(Path.Combine(home, "auth.json"), cancellation) == auth);
        check("Actual native startup and route refresh accept provider metadata with the optional summary flag absent", saved.OfType<JsonObject>().Single(model => model["slug"]!.GetValue<string>() == ModelRouting.Model)["supports_reasoning_summary_parameter"]!.GetValue<bool>() == false);
        manager.ModelCatalogChanged = async _ => { await List(current); return true; };
        var gemmaId = gemma.Models.Single().Alias(gemma.Id);
        async Task<string[]> Visible() => (await current.RequestAsync("model/list", new { includeHidden = false }, cancellation))
            .GetProperty("data").EnumerateArray().Select(item => item.GetProperty("model").GetString()!).ToArray();
        check("The normal native picker lists both fixed models and hides the legacy selector", (await Visible()).Order().SequenceEqual(new[] { "native-fixture", gemmaId, qwen.Models.Single().Alias(qwen.Id) }.Order()));
        await manager.SetModelEnabledAsync(gemmaId, false, cancellation); await manager.CatalogRefreshCompletion;
        check("Disabling a model immediately removes it from the same native picker", !(await Visible()).Contains(gemmaId) && manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Confirmed);
        await manager.SaveAsync(gemma.Id, "Renamed Gemma", gemma.BaseUrl, "", cancellation); await manager.CatalogRefreshCompletion;
        check("Renaming and rediscovery keep a model hidden and update its native description", !(await Visible()).Contains(gemmaId) &&
            (await List(current)).GetProperty("data").EnumerateArray().Single(item => item.GetProperty("model").GetString() == gemmaId).GetProperty("description").GetString()!.Contains("Renamed Gemma"));
        await manager.SetModelEnabledAsync(gemmaId, true, cancellation); await manager.CatalogRefreshCompletion;
        check("Re-enabling a model restores the same fixed ID without reinitializing Codex", (await Visible()).Contains(gemmaId));
        var extra = await manager.SaveAsync(null, "Added model provider", qwen.BaseUrl, "fictional-added-key", cancellation); await manager.CatalogRefreshCompletion;
        var extraId = extra.Models.Single().Alias(extra.Id);
        check("Adding a provider model immediately adds its own fixed entry", (await Visible()).Contains(extraId));
        await manager.RemoveAsync(extra.Id, cancellation); await manager.CatalogRefreshCompletion;
        check("Deleting a provider removes its entry from the same native process and confirms the full set", !(await Visible()).Contains(extraId) && manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Confirmed);
        check("List mutations preserve native authentication and the native model document", await File.ReadAllTextAsync(Path.Combine(home, "auth.json"), cancellation) == auth &&
            (await List(current)).GetProperty("data")[0].GetRawText() == qwenCatalog.GetProperty("data")[0].GetRawText());
        var damaged = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath, cancellation))!.AsObject();
        damaged["models"]!.AsArray().OfType<JsonObject>().Single(model => model["slug"]!.GetValue<string>() == ModelRouting.Model)["supports_reasoning_summary_parameter"] = null;
        var damagedBytes = JsonSerializer.SerializeToUtf8Bytes(damaged);
        await File.WriteAllBytesAsync(catalogPath, damagedBytes, cancellation);
        var rejected = false;
        try { await CatalogValidation.ReadModelListAsync(executable, damagedBytes, ModelRouting.Model, cancellation); }
        catch (Exception error) when (error is IOException or CodexRequestException) { rejected = true; }
        check("The actual Codex parser reproduces the reported null boolean startup failure", rejected);
        await manager.DisposeAsync();
        new ModelProviderStore(directory).Update(settings => settings.DesktopAppliedConfig!["model_catalog_json"] = catalogPath);
        var configPath = Path.Combine(home, "config.toml");
        await File.WriteAllTextAsync(configPath, "model_catalog_json=" + JsonSerializer.Serialize(catalogPath) + "\n" + await File.ReadAllTextAsync(configPath, cancellation), cancellation);
        var legacyUpstream = new ReasoningControlContracts.ReasoningUpstream { IncludeSummaryCapability = false };
        await using var recovery = new ModelProviderManager(directory, Authenticate, new HttpClient(legacyUpstream));
        await recovery.ValidateDesktopProfileAsync(executable, home, CodexModelProfile.Native, cancellation);
        await recovery.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Native, cancellation);
        await using (var restored = new CodexRpc(executable, home, preserveProvider: true))
        {
            await restored.InitializeAsync(cancellation);
            var restoredList = await List(restored);
            check("Default restoration remains usable when a legacy static Switch catalog cannot parse", restoredList.GetProperty("data").GetArrayLength() == 1 && !recovery.Settings.DesktopProfile);
        }
        var restoredConfig = await File.ReadAllTextAsync(Path.Combine(home, "config.toml"), cancellation);
        legacyUpstream.InvalidNativeShell = true; rejected = false;
        try { await recovery.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Switch, cancellation); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("separate Codex Switch terminal"); }
        check("Retired combined-profile activation preserves config, prior catalog and connection state", rejected &&
            await File.ReadAllTextAsync(Path.Combine(home, "config.toml"), cancellation) == restoredConfig &&
            (await File.ReadAllBytesAsync(catalogPath, cancellation)).SequenceEqual(damagedBytes) && !recovery.Settings.DesktopProfile);
    }
}
