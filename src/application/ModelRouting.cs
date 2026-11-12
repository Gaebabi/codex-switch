using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal static JsonObject Metadata()
    {
        var model = ProviderCatalog.Generic(Model);
        model["display_name"] = "Codex Switch";
        model["priority"] = int.MaxValue;
        model["description"] = "Select the actual model in Codex Switch. New requests use the saved selection.";
        model["default_reasoning_level"] = "medium";
        model["supported_reasoning_levels"] = new JsonArray();
        model["input_modalities"] = new JsonArray("text", "image");
        model["upgrade"] = null;
        model["auto_review_model_override"] = null;
        return model;
    }
    internal static void ApplyReasoning(JsonObject body, JsonObject metadata)
    {
        var effort = metadata["default_reasoning_level"]?.GetValue<string>();
        body["reasoning"] = effort != null && metadata["supported_reasoning_levels"] is JsonArray levels &&
            levels.Any(level => level?["effort"]?.GetValue<string>() == effort)
            ? new JsonObject { ["effort"] = effort } : null;
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
    }
}
