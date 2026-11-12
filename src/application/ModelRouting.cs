using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexAccountDesk;

internal sealed record ModelRouteChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

internal static class ModelRouting
{
    internal const string Model = "codex-switch";
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
            var actual = ProviderReasoning.Metadata(selected, native?.GetValueOrDefault(selected.UpstreamId));
            foreach (var key in new[] { "default_reasoning_level", "supported_reasoning_levels", "supports_reasoning_summary_parameter" })
                model[key] = actual[key]?.DeepClone();
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
    internal void SelectRoute(string id)
    {
        _store.Update(settings =>
        {
            if (!ModelRouting.Registered(settings, id)) throw new InvalidOperationException("Select a currently registered model in Codex Switch.");
            settings.SelectedRoute = id;
        });
        RefreshRoutingReasoning(Settings);
    }
    private void RefreshRoutingReasoning(ModelProviderSettings settings)
    {
        if (!settings.Connected || !settings.RouterEnabled) return;
        var path = settings.DesktopProfile ? DesktopCatalogPath : _catalogPath;
        if (!File.Exists(path)) throw new InvalidDataException("The connected model catalog is unavailable. Refresh the connection before changing reasoning capabilities.");
        var before = SecureFiles.ReadPrivateBytes(path);
        if (before.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The connected model catalog exceeds its bound.");
        var catalog = JsonNode.Parse(before) as JsonObject;
        if (catalog?["models"] is not JsonArray models) throw new InvalidDataException("The connected model catalog is invalid.");
        var entries = models.OfType<JsonObject>().Where(model => model["slug"]?.GetValue<string>() == ModelRouting.Model).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("The connected model catalog must contain exactly one Switch entry.");
        var native = settings.IsolatedProfile && !settings.DesktopProfile ? null : models.OfType<JsonObject>()
            .Where(model => model["slug"]?.GetValue<string>() != ModelRouting.Model)
            .ToDictionary(model => model["slug"]!.GetValue<string>(), model => model, StringComparer.Ordinal);
        var selected = ModelRouting.Metadata(settings, native);
        foreach (var key in new[] { "default_reasoning_level", "supported_reasoning_levels", "supports_reasoning_summary_parameter" })
            entries[0][key] = selected[key]?.DeepClone();
        var after = JsonSerializer.SerializeToUtf8Bytes(catalog);
        if (after.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The connected model catalog exceeds its bound.");
        if (!before.AsSpan().SequenceEqual(after)) SecureFiles.AtomicWrite(path, after);
    }
}
