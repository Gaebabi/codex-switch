using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NodeValue = System.Text.Json.Nodes.JsonValue;

namespace CodexAccountDesk;

internal sealed partial class ModelProviderManager
{
    private string DesktopCatalogPath => Path.Combine(Path.GetDirectoryName(_catalogPath)!, "model-providers-desktop-catalog.json");

    internal Task ValidateDesktopProfileAsync(string executable, string home, CodexModelProfile profile, CancellationToken cancellation) =>
        ApplyDesktopProfileCoreAsync(executable, home, profile, cancellation, validateOnly: true);

    internal Task ApplyDesktopProfileAsync(string executable, string home, CodexModelProfile profile, CancellationToken cancellation) =>
        ApplyDesktopProfileCoreAsync(executable, home, profile, cancellation, validateOnly: false);

    private async Task ApplyDesktopProfileCoreAsync(string executable, string home, CodexModelProfile profile, CancellationToken cancellation, bool validateOnly)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile != CodexModelProfile.Native)
            throw new InvalidOperationException("Custom models require the separate Codex Switch terminal profile. The native OpenAI connection cannot be redirected through Switch.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await CancelLiveCatalogRefreshAsync().ConfigureAwait(false);
            var settings = Settings;
            await using var control = CatalogValidation.CreateHome();
            var controlCatalog = Path.Combine(control.Path, "catalog.json");
            SecureFiles.AtomicWrite(controlCatalog, ProviderCatalog.Serialize(new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("codex-switch-management")) }));
            await using var rpc = new CodexRpc(executable, home, preserveProvider: true,
                configOverrides: new Dictionary<string, string> { ["model_catalog_json"] = JsonSerializer.Serialize(controlCatalog) });
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
            await RequireUnmanagedRoutingAsync(rpc, cancellation).ConfigureAwait(false);
            var snapshot = await ReadUserConfigAsync(rpc, home, cancellation).ConfigureAwait(false);
            var changes = DesktopRestoreChanges(settings, snapshot.Config);
            var native = (JsonObject)snapshot.Config.DeepClone();
            foreach (var (key, value) in changes) SetConfigValue(native, key, value);
            var currentModel = native["model"]?.GetValue<string>();
            if (currentModel == ModelRouting.Model || currentModel?.StartsWith("cs_", StringComparison.Ordinal) == true)
            {
                changes["model"] = null; native.Remove("model");
                changes["model_reasoning_effort"] = null; native.Remove("model_reasoning_effort");
            }
            _home = Path.GetFullPath(home);
            var nativeCatalog = native["model_catalog_json"]?.GetValue<string>();
            var validationCatalog = nativeCatalog == null ? null : SecureFiles.ReadPrivateBytes(Path.GetFullPath(nativeCatalog, home));
            var list = await CatalogValidation.ReadModelListAsync(executable, validationCatalog, native["model"]?.GetValue<string>(), cancellation).ConfigureAwait(false);
            if (InvalidNativeEffort(native, list)) changes["model_reasoning_effort"] = null;
            var directory = Path.GetDirectoryName(_catalogPath)!;
            var histories = NativeHistoryRecovery.Find(home, directory, cancellation);
            if (validateOnly) return;
            await NativeHistoryRecovery.RecoverAsync(executable, home, directory, _store, _authenticate, _http, histories, cancellation).ConfigureAwait(false);
            if (changes.Count > 0)
            {
                await WriteConfigAsync(rpc, home, snapshot.Version, changes, cancellation, controlCatalog).ConfigureAwait(false);
                await VerifyDesktopProfileWriteAsync(executable, home, changes, cancellation, requireDynamicCatalog: false).ConfigureAwait(false);
            }
            if (settings.DesktopProfile || settings.DesktopAppliedConfig != null) LiveModelCatalog.Invalidate(home);
            _store.Update(next =>
            {
                next.RootMigrationCompleted = true;
                next.DesktopProfile = false;
                next.Connected = settings.IsolatedProfile && settings.ConnectionWriteConfirmed && settings.CustomProfilePath != null &&
                    File.Exists(settings.CustomProfilePath) && File.ReadAllText(settings.CustomProfilePath) == settings.CustomProfileContent;
                next.ConnectionWriteConfirmed = next.Connected;
                next.DesktopOriginalConfig = null; next.DesktopAppliedConfig = null;
            });
        }
        finally { _changes.Release(); }
    }

    private static async Task VerifyDesktopProfileWriteAsync(string executable, string home, Dictionary<string, JsonNode?> changes, CancellationToken cancellation, bool requireDynamicCatalog)
    {
        await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
        await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
        var snapshot = await ReadUserConfigAsync(rpc, home, cancellation).ConfigureAwait(false);
        foreach (var (key, expected) in changes)
            if (!JsonNode.DeepEquals(ValueAt(snapshot.Config, key), expected))
                throw new InvalidOperationException("Codex profile settings changed before confirmation. No chat was continued.");
        var read = await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation).ConfigureAwait(false);
        var origins = read.GetProperty("origins");
        if (requireDynamicCatalog &&
            (read.GetProperty("config").TryGetProperty("model_catalog_json", out var effectiveCatalog) && effectiveCatalog.ValueKind != JsonValueKind.Null ||
             origins.TryGetProperty("model_catalog_json", out _)))
            throw new InvalidOperationException("Another Codex configuration layer supplies a static model catalog. The live model connection was not confirmed; no chat was continued.");
        foreach (var (key, expected) in changes)
        {
            if (expected == null) continue;
            if (!origins.TryGetProperty(key, out var origin) || !origin.TryGetProperty("name", out var name) ||
                JsonValue.String(name, "type") != "user" || JsonValue.String(name, "profile") != null ||
                JsonValue.String(name, "file") is not string file ||
                !string.Equals(Path.GetFullPath(file), Path.GetFullPath(Path.Combine(home, "config.toml")), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("A higher-priority Codex policy overrides the connection settings. No chat was continued.");
        }
    }

    internal static void RequireDynamicCatalogLayers(JsonElement read, string home, string controlCatalog)
    {
        var userConfig = Path.GetFullPath(Path.Combine(home, "config.toml"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var layer in read.GetProperty("layers").EnumerateArray())
        {
            if (JsonValue.String(layer, "disabledReason") != null) continue;
            var name = layer.GetProperty("name");
            if (JsonValue.String(name, "type") == "user" && JsonValue.String(name, "profile") == null &&
                JsonValue.String(name, "file") is string file && string.Equals(Path.GetFullPath(file), userConfig, comparison)) continue;
            var config = layer.GetProperty("config");
            if (!config.TryGetProperty("model_catalog_json", out var catalog) || catalog.ValueKind == JsonValueKind.Null) continue;
            if (JsonValue.String(name, "type") == "sessionFlags" && catalog.ValueKind == JsonValueKind.String &&
                string.Equals(Path.GetFullPath(catalog.GetString()!), Path.GetFullPath(controlCatalog), comparison)) continue;
            throw new InvalidOperationException("Another Codex configuration layer supplies a static model catalog. Remove that override before applying the live model profile; no settings were overwritten.");
        }
    }

    internal static Dictionary<string, JsonNode?> DesktopRestoreChanges(ModelProviderSettings settings, JsonObject current)
    {
        var original = settings.DesktopOriginalConfig;
        var applied = settings.DesktopAppliedConfig;
        if (applied == null && !settings.RootMigrationCompleted)
        { original = settings.OriginalConfig; applied = settings.AppliedConfig; }
        var changes = new Dictionary<string, JsonNode?>();
        if (applied == null)
        {
            if (settings.Connected && !settings.IsolatedProfile && !settings.RootMigrationCompleted)
                throw new InvalidOperationException("The original Codex connection settings are missing. No profile was overwritten.");
            return changes;
        }
        if (original == null) throw new InvalidOperationException("The original Codex profile is incomplete.");
        foreach (var (key, value) in applied)
        {
            if (!original.TryGetValue(key, out var before)) throw new InvalidOperationException("The original Codex profile is incomplete.");
            var actual = ValueAt(current, key);
            if (JsonNode.DeepEquals(actual, before)) continue;
            if (!JsonNode.DeepEquals(actual, value)) throw new InvalidOperationException("Codex profile settings changed outside Switch. Review them before restoring; no settings were overwritten.");
            changes[key] = before?.DeepClone();
        }
        return changes;
    }

    internal static bool InvalidNativeEffort(JsonObject config, JsonElement list)
    {
        var effort = config["model_reasoning_effort"]?.GetValue<string>();
        if (effort == null) return false;
        var model = config["model"]?.GetValue<string>();
        foreach (var entry in list.GetProperty("data").EnumerateArray())
        {
            var id = JsonValue.String(entry, "model");
            if (id == ModelRouting.Model || id?.StartsWith("cs_", StringComparison.Ordinal) == true) continue;
            if (model != null ? id != model : !entry.TryGetProperty("isDefault", out var selected) || selected.ValueKind != JsonValueKind.True) continue;
            return !entry.GetProperty("supportedReasoningEfforts").EnumerateArray().Any(level => JsonValue.String(level, "reasoningEffort") == effort);
        }
        return false;
    }

    private static void SetConfigValue(JsonObject config, string path, JsonNode? value)
    {
        var parts = path.Split('.'); var node = config;
        foreach (var part in parts[..^1])
        {
            if (node[part] is not JsonObject child) { child = new JsonObject(); node[part] = child; }
            node = child;
        }
        if (value == null) node.Remove(parts[^1]); else node[parts[^1]] = value.DeepClone();
    }
}
