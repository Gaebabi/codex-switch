using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record ModelRouteChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

internal static class ModelRouting
{
    internal const string Model = "codex-switch";
    internal static bool Owned(string? id) => id == Model || id?.StartsWith("cs_", StringComparison.Ordinal) == true;
    internal static JsonArray Catalog(ModelProviderSettings settings, IReadOnlyDictionary<string, JsonObject>? native = null)
    {
        var legacy = Metadata(settings, native);
        legacy["visibility"] = "hide";
        var entries = new JsonArray(legacy);
        foreach (var provider in settings.Providers)
        foreach (var item in provider.Models)
        {
            var actual = ProviderReasoning.Metadata(item, native?.GetValueOrDefault(item.UpstreamId));
            ModelContextOptions.ApplyMetadata(actual, item.ContextOverride);
            ProviderReasoning.ValidateMetadata(actual);
            var metadata = ProviderCatalog.Generic(item.Alias(provider.Id));
            foreach (var field in actual) metadata[field.Key] = field.Value?.DeepClone();
            metadata["slug"] = item.Alias(provider.Id);
            metadata["display_name"] = settings.DesktopBackendEnabled
                ? item.UpstreamId + " (" + provider.Name + ")"
                : "Codex Switch (" + item.UpstreamId + ")";
            metadata["description"] = provider.Name + " · " + item.UpstreamId;
            if (settings.DesktopBackendEnabled)
            {
                metadata["tool_mode"] = "code_mode_only";
                metadata["supports_search_tool"] = true;
            }
            metadata["visibility"] = item.EnabledInCodex ? "list" : "hide";
            metadata["priority"] = int.MaxValue;
            metadata["upgrade"] = null;
            metadata["auto_review_model_override"] = null;
            entries.Add(metadata);
        }
        return entries;
    }
    internal static List<ModelRouteChoice> Choices(ModelProviderSettings settings) => settings.Providers
        .SelectMany(provider => provider.Models.Select(model =>
            new ModelRouteChoice(model.Alias(provider.Id), model.UpstreamId + " (" + provider.Name + ")"))).ToList();
    internal static bool Registered(ModelProviderSettings settings, string? route) =>
        route != null && Choices(settings).Any(choice => choice.Id == route);
    internal static void ValidateChoices(List<ModelRouteChoice>? choices)
    {
        if (choices == null || choices.Count > 256 || choices.Any(choice => choice == null || string.IsNullOrWhiteSpace(choice.Id) || choice.Id.Length > 256 ||
            choice.Id.Any(char.IsControl) || string.IsNullOrWhiteSpace(choice.Label) || choice.Label.Length > 512) ||
            choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != choices.Count)
            throw new InvalidDataException("The OpenAI model routing choices are invalid.");
    }
    internal static JsonObject Metadata(ModelProviderSettings? settings = null, IReadOnlyDictionary<string, JsonObject>? native = null)
    {
        var model = ProviderCatalog.Generic(Model);
        model["display_name"] = "Codex Switch";
        model["priority"] = int.MaxValue;
        model["description"] = "Select the actual model in Codex Switch. New requests use the saved selection.";
        model["default_reasoning_level"] = "medium";
        model["supported_reasoning_levels"] = new JsonArray();
        ProviderModel? selected = null;
        if (settings != null)
            selected = settings.Providers.SelectMany(provider => provider.Models.Select(item => (Provider: provider, Model: item)))
                .FirstOrDefault(item => item.Model.Alias(item.Provider.Id) == settings.SelectedRoute).Model;
        if (selected != null)
        {
            model["display_name"] = "Codex Switch (" + selected.UpstreamId + ")";
            var actual = ProviderReasoning.Metadata(selected, native?.GetValueOrDefault(selected.UpstreamId));
            ProviderReasoning.ValidateMetadata(actual);
            foreach (var key in new[] { "default_reasoning_level", "supported_reasoning_levels", "supports_reasoning_summary_parameter" })
                if (actual.TryGetPropertyValue(key, out var value)) model[key] = value?.DeepClone();
            ModelContextOptions.ApplyMetadata(model, selected.ContextOverride);
        }
        model["input_modalities"] = new JsonArray("text", "image");
        model["upgrade"] = null;
        model["auto_review_model_override"] = null;
        return model;
    }
    internal static void ApplyReasoning(JsonObject body, JsonObject metadata)
    {
        if (body["reasoning"] != null && body["reasoning"] is not JsonObject) return;
        if (body["reasoning"] is JsonObject chosen && chosen["effort"] != null) return;
        var effort = metadata["default_reasoning_level"]?.GetValue<string>();
        if (effort != null && metadata["supported_reasoning_levels"] is JsonArray levels && levels.Any(level => level?["effort"]?.GetValue<string>() == effort))
        {
            if (body["reasoning"] is not JsonObject reasoning) body["reasoning"] = reasoning = new JsonObject();
            reasoning["effort"] = effort;
        }
    }
}

internal sealed partial class ModelProviderManager
{
    internal IReadOnlyList<ModelRouteChoice> RoutingChoices => ModelRouting.Choices(Settings);
    internal async Task SetModelEnabledAsync(string id, bool enabled, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _changes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            _store.Update(settings => settings.Providers.SelectMany(provider => provider.Models.Select(model => (provider, model)))
                .Single(pair => pair.model.Alias(pair.provider.Id) == id).model.EnabledInCodex = enabled);
            RefreshRoutingReasoning(Settings);
            await NotifyLiveCatalogAsync(lifetime.Token).ConfigureAwait(false);
        }
        finally { _changes.Release(); }
    }
    internal void SelectRoute(string id) => SelectRouteAsync(id, CancellationToken.None).GetAwaiter().GetResult();
    internal async Task SelectRouteAsync(string id, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _changes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            var candidate = Settings;
            if (!ModelRouting.Registered(candidate, id)) throw new InvalidOperationException("Select a currently registered model in Codex Switch.");
            candidate.SelectedRoute = id;
            ModelRouting.Metadata(candidate);
            _store.Update(settings => settings.SelectedRoute = id);
            RefreshRoutingReasoning(Settings);
            await NotifyLiveCatalogAsync(lifetime.Token).ConfigureAwait(false);
        }
        finally { _changes.Release(); }
    }
    private void RefreshRoutingReasoning(ModelProviderSettings settings)
    {
        if (settings.DesktopBackendEnabled) return;
        if (!settings.Connected || !settings.RouterEnabled) return;
        var path = settings.DesktopProfile ? DesktopCatalogPath : _catalogPath;
        if (!File.Exists(path)) throw new InvalidDataException("The connected model catalog is unavailable. Refresh the connection before changing reasoning capabilities.");
        var before = SecureFiles.ReadPrivateBytes(path);
        if (before.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The connected model catalog exceeds its bound.");
        var catalog = JsonNode.Parse(before) as JsonObject;
        if (catalog?["models"] is not JsonArray models) throw new InvalidDataException("The connected model catalog is invalid.");
        var native = settings.IsolatedProfile && !settings.DesktopProfile ? null : models.OfType<JsonObject>()
            .Where(model => !ModelRouting.Owned(model["slug"]?.GetValue<string>()))
            .ToDictionary(model => model["slug"]!.GetValue<string>(), model => model, StringComparer.Ordinal);
        for (var index = models.Count - 1; index >= 0; index--)
            if (ModelRouting.Owned(models[index]?["slug"]?.GetValue<string>())) models.RemoveAt(index);
        foreach (var entry in ModelRouting.Catalog(settings, native)) models.Add(entry!.DeepClone());
        var after = ProviderCatalog.Serialize(catalog!);
        if (!before.AsSpan().SequenceEqual(after)) SecureFiles.AtomicWrite(path, after);
    }
}
