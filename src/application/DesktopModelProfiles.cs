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

    internal async Task ApplyDesktopProfileAsync(string executable, string home, CodexModelProfile profile, CancellationToken cancellation)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var settings = Settings;
            if (profile == CodexModelProfile.Switch && !ModelRouting.Registered(settings, settings.SelectedRoute))
                throw new InvalidOperationException("Select an available custom model in Codex Switch before changing profiles.");
            await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
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
            var list = await rpc.RequestAsync("model/list", new { includeHidden = true }, cancellation).ConfigureAwait(false);
            if (InvalidNativeEffort(native, list)) changes["model_reasoning_effort"] = null;
            _home = Path.GetFullPath(home);
            Dictionary<string, JsonNode?>? original = null, applied = null;
            if (profile == CodexModelProfile.Switch)
            {
                var version = rpc.Initialization is JsonElement hello ? ModelGateway.ClientVersion(JsonValue.String(hello, "userAgent")) : null;
                if (version == null) throw new InvalidOperationException("The Codex version is unavailable. No profile was applied.");
                Start();
                await WriteCatalogAsync(version, cancellation, true, true, true).ConfigureAwait(false);
                applied = new()
                {
                    ["model_provider"] = NodeValue.Create("openai"),
                    ["openai_base_url"] = NodeValue.Create(_gateway!.BaseUrl),
                    ["model_catalog_json"] = NodeValue.Create(DesktopCatalogPath),
                    ["features.responses_websockets_v2"] = NodeValue.Create(false),
                    ["features.enable_request_compression"] = NodeValue.Create(false)
                };
                original = applied.Keys.ToDictionary(key => key, key => ValueAt(native, key)?.DeepClone());
                foreach (var (key, value) in applied) changes[key] = value?.DeepClone();
                _store.Update(next =>
                {
                    next.DesktopOriginalConfig = original; next.DesktopAppliedConfig = applied;
                    next.DesktopProfile = true; next.RouterEnabled = true; next.Connected = true; next.ConnectionWriteConfirmed = false;
                });
            }
            if (changes.Count > 0) await WriteConfigAsync(rpc, home, snapshot.Version, changes, cancellation).ConfigureAwait(false);
            _store.Update(next =>
            {
                next.RootMigrationCompleted = true;
                next.DesktopProfile = profile == CodexModelProfile.Switch;
                next.Connected = next.DesktopProfile || settings.IsolatedProfile && settings.ConnectionWriteConfirmed && settings.CustomProfilePath != null &&
                    File.Exists(settings.CustomProfilePath) && File.ReadAllText(settings.CustomProfilePath) == settings.CustomProfileContent;
                next.ConnectionWriteConfirmed = next.Connected;
                if (!next.DesktopProfile) { next.DesktopOriginalConfig = null; next.DesktopAppliedConfig = null; }
            });
        }
        finally { _changes.Release(); }
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
