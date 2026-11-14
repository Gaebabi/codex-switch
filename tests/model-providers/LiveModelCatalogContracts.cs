using CodexAccountDesk;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class LiveModelCatalogContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "live-catalog-cache"); Directory.CreateDirectory(home);
        var path = Path.Combine(home, "models_cache.json");
        LiveModelCatalog.Invalidate(home);
        check("An absent native model cache stays absent until Codex fetches it", !File.Exists(path));
        var cache = new JsonObject { ["fetched_at"] = DateTimeOffset.UtcNow.ToString("O"), ["client_version"] = "0.167.0",
            ["identity"] = "opaque-native-identity", ["etag"] = "opaque-etag", ["future_field"] = new JsonObject { ["retain"] = true },
            ["models"] = new JsonArray(ProviderCatalog.Generic("native-fixture")) };
        await File.WriteAllTextAsync(path, cache.ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(home, "unrelated.json"), cache.ToJsonString());
        LiveModelCatalog.Invalidate(home);
        check("Invalidation removes only the regenerable cache and never writes a stale native snapshot", !File.Exists(path) &&
            await File.ReadAllTextAsync(Path.Combine(home, "unrelated.json")) == cache.ToJsonString());
        foreach (var invalid in new[] { "{", "{\"models\":[]}", "{\"models\":[],\"client_version\":\"0.167.0\",\"fetched_at\":7}" })
        {
            await File.WriteAllTextAsync(path, invalid);
            var refused = false;
            try { LiveModelCatalog.Invalidate(home); }
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException) { refused = true; }
            check("An unknown cache format is refused without replacing the original bytes", refused && await File.ReadAllTextAsync(path) == invalid);
        }
        var settings = new ModelProviderSettings { Connected = true, ConnectionWriteConfirmed = true, DesktopProfile = true,
            DesktopAppliedConfig = new() { ["model_catalog_json"] = "legacy.json" } };
        check("Legacy static and disconnected profiles do not claim live model refresh", !LiveModelCatalog.Enabled(settings) && !LiveModelCatalog.Enabled(new()));
        settings.DesktopAppliedConfig["model_catalog_json"] = null;
        check("Live refresh requires the explicitly applied dynamic desktop profile", LiveModelCatalog.Enabled(settings));
        settings.ConnectionWriteConfirmed = false;
        check("An unconfirmed profile write cannot claim a live desktop catalog", !LiveModelCatalog.Enabled(settings));
        var control = Path.Combine(home, "control.json");
        object Layer(string type, string catalog, string? disabled = null) => new
        {
            name = new { type, file = Path.Combine(home, "config.toml"), profile = (string?)null },
            config = new { model_catalog_json = catalog }, disabledReason = disabled
        };
        var allowed = new[] { Layer("user", "legacy.json"), Layer("sessionFlags", control), Layer("system", "disabled.json", "untrusted") };
        ModelProviderManager.RequireDynamicCatalogLayers(JsonSerializer.SerializeToElement(new { layers = allowed }), home, control);
        check("Dynamic migration removes only the base-user override and ignores disabled layers and its own control catalog", true);
        foreach (var type in new[] { "system", "project", "sessionFlags" })
        {
            var refused = false;
            try { ModelProviderManager.RequireDynamicCatalogLayers(JsonSerializer.SerializeToElement(new { layers = allowed.Append(Layer(type, "inherited.json")) }), home, control); }
            catch (InvalidOperationException) { refused = true; }
            check("An enabled inherited static catalog blocks dynamic migration before any write", refused);
        }
        foreach (var invalid in new[] { "null", "[]", "{}" })
        {
            await File.WriteAllTextAsync(path, invalid);
            var refused = false;
            try { await LiveModelCatalog.ConfirmAsync(home, new JsonArray(ModelRouting.Metadata()), _ => Task.FromResult(true), CancellationToken.None); }
            catch (InvalidDataException) { refused = true; }
            check("A complete cache with the wrong root or model shape fails explicitly", refused && await File.ReadAllTextAsync(path) == invalid);
        }
        var previous = ModelRouting.Metadata(); previous["display_name"] = "Codex Switch (previous)";
        var expected = (JsonObject)previous.DeepClone(); expected["display_name"] = "Codex Switch (selected)";
        cache["models"] = new JsonArray(previous);
        await File.WriteAllTextAsync(path, cache.ToJsonString());
        var notifications = 0;
        var result = await LiveModelCatalog.ConfirmAsync(home, new JsonArray(expected.DeepClone()), async token =>
        {
            notifications++;
            cache["models"] = new JsonArray(expected.DeepClone());
            await File.WriteAllTextAsync(path, cache.ToJsonString(), token);
            return true;
        }, CancellationToken.None);
        check("Identical reasoning capabilities do not confirm a stale selected-model name", result == ModelCatalogRefreshState.Confirmed && notifications == 1);
        await RefreshOwnership(root, check);
    }

    private static async Task RefreshOwnership(string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "live-catalog-ownership-home"); Directory.CreateDirectory(home);
        var directory = Path.Combine(root, "live-catalog-ownership-store");
        var provider = new ModelProvider { Name = "Live fixture", BaseUrl = "https://live.example.invalid/v1", ApiKey = "fictional-live-key" };
        foreach (var effort in new[] { "low", "high" })
            provider.Models.Add(new ProviderModel { UpstreamId = effort, Metadata = ProviderCatalog.Generic(effort),
                ReasoningOverride = new() { Levels = new() { effort }, Default = effort } });
        var low = provider.Models[0].Alias(provider.Id); var high = provider.Models[1].Alias(provider.Id);
        var store = new ModelProviderStore(directory);
        store.Update(settings =>
        {
            settings.Providers.Add(provider); settings.SelectedRoute = low;
            settings.Connected = settings.ConnectionWriteConfirmed = settings.DesktopProfile = settings.RouterEnabled = true;
            settings.DesktopAppliedConfig = new() { ["model_catalog_json"] = null };
        });
        var catalogPath = Path.Combine(directory, "model-providers-desktop-catalog.json");
        await File.WriteAllTextAsync(catalogPath, new JsonObject { ["models"] = new JsonArray(ModelRouting.Metadata(store.Read())) }.ToJsonString());
        var cachePath = Path.Combine(home, "models_cache.json");
        byte[] Cache(JsonArray entries) => JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["fetched_at"] = DateTimeOffset.UtcNow.ToString("O"), ["client_version"] = "0.167.0", ["identity"] = "fixture-native-cache",
            ["models"] = entries.DeepClone()
        });
        await using var manager = new ModelProviderManager(directory, (_, _) => null, codexHome: home);
        JsonArray Current() => JsonNode.Parse(File.ReadAllBytes(catalogPath))!["models"]!.AsArray();
        manager.ModelCatalogChanged = _ => Task.FromResult(true);
        await manager.SelectRouteAsync(high, CancellationToken.None);
        var superseded = manager.CatalogRefreshCompletion;
        check("A connected desktop without a cache remains pending without blocking the picker for ten seconds",
            manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Pending && !superseded.IsCompleted);
        manager.ModelCatalogChanged = async token => { await File.WriteAllBytesAsync(cachePath, Cache(Current()), token); return true; };
        await manager.SelectRouteAsync(low, CancellationToken.None);
        await manager.CatalogRefreshCompletion;
        check("A newer selection cancels and drains the previous confirmation before confirming its own catalog",
            superseded.IsCanceled && manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Confirmed && manager.Settings.SelectedRoute == low);

        var notifications = 0;
        var stale = Cache(Current());
        manager.ModelCatalogChanged = _ => ++notifications == 1 ? Task.FromResult(true) : Task.FromException<bool>(new IOException("fixture-live-refresh-error"));
        await manager.SelectRouteAsync(high, CancellationToken.None);
        await File.WriteAllBytesAsync(cachePath, stale);
        await manager.CatalogRefreshCompletion;
        check("A background notification failure is observable through refresh status and LastError",
            manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Failed && manager.LastError == "fixture-live-refresh-error");

        manager.ModelCatalogChanged = _ => Task.FromResult(false);
        await manager.SelectRouteAsync(low, CancellationToken.None);
        check("An unavailable desktop yields a deferred outcome rather than confirmed success",
            manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.Deferred && manager.CatalogRefreshCompletion.IsCompleted && manager.LastError == null);
        manager.ModelCatalogChanged = _ => Task.FromResult(true);
        await manager.SelectRouteAsync(high, CancellationToken.None);
        var disposing = manager.CatalogRefreshCompletion;
        await manager.DisposeAsync();
        check("Manager disposal cancels and drains pending confirmation before releasing its synchronization primitives",
            disposing.IsCanceled && manager.CatalogRefreshCompletion.IsCompleted && manager.CatalogRefreshStatus.State == ModelCatalogRefreshState.None && !File.Exists(cachePath));
    }
}
