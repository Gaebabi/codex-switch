using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace CodexAccountDesk;

internal sealed class ProviderReasoningOptions
{
    public List<string> Levels { get; set; } = new();
    public string Default { get; set; } = "medium";
}

internal static class ProviderReasoning
{
    internal static readonly string[] Efforts = { "none", "minimal", "low", "medium", "high", "xhigh", "max" };

    internal static void Validate(ProviderReasoningOptions? options)
    {
        if (options == null) return;
        if (options.Levels == null || options.Levels.Count == 0 || options.Levels.Count > Efforts.Length ||
            options.Levels.Distinct(StringComparer.Ordinal).Count() != options.Levels.Count ||
            options.Levels.Any(level => !Efforts.Contains(level, StringComparer.Ordinal)) || !options.Levels.Contains(options.Default))
            throw new InvalidDataException("Choose supported reasoning levels and a default from those levels.");
    }

    internal static JsonObject Metadata(ProviderModel model, JsonObject? native)
    {
        var result = (JsonObject)model.Metadata.DeepClone();
        if (model.ReasoningOverride is { } options)
        {
            Validate(options);
            result["default_reasoning_level"] = options.Default;
            result["supported_reasoning_levels"] = new JsonArray(Efforts.Where(options.Levels.Contains).Select(level => (JsonNode)new JsonObject { ["effort"] = level, ["description"] = level }).ToArray());
        }
        else if (model.GenericMetadata && native != null)
        {
            foreach (var key in new[] { "default_reasoning_level", "supported_reasoning_levels", "supports_reasoning_summary_parameter" })
                if (native.TryGetPropertyValue(key, out var value)) result[key] = value?.DeepClone();
        }
        return result;
    }

    // A generic Responses route supports the standard effort field. Other optional
    // reasoning fields still require catalog support; never discard a chosen effort.
    internal static string? PrepareRequest(ProviderModel model, JsonObject metadata, JsonObject body, bool nativeMatch)
    {
        if (!model.GenericMetadata && model.ReasoningOverride == null) return null;
        if (body["reasoning"] == null) return null;
        if (body["reasoning"] is not JsonObject reasoning) return "Reasoning must contain a supported effort. Refresh the model list or configure this model's reasoning levels in Switch > Models.";
        if (reasoning["effort"] != null)
        {
            if (reasoning["effort"] is not System.Text.Json.Nodes.JsonValue value || !value.TryGetValue<string>(out var effort) ||
                metadata["supported_reasoning_levels"] is not JsonArray levels || !levels.Any(level => level?["effort"]?.GetValue<string>() == effort))
                return "The selected reasoning effort is not configured for this model. Refresh the model list or configure its supported levels in Switch > Models.";
        }
        if (model.GenericMetadata)
        {
            if (metadata["supports_reasoning_summary_parameter"]?.GetValue<bool>() != true)
            { reasoning.Remove("summary"); reasoning.Remove("generate_summary"); }
            if (model.ReasoningOverride == null && !nativeMatch)
                body.Remove("reasoning");
            else if (reasoning.Count == 0) body.Remove("reasoning");
        }
        return null;
    }
}
