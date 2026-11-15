using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NodeValue = System.Text.Json.Nodes.JsonValue;

namespace CodexAccountDesk;

internal static class DesktopModelDefaults
{
    internal static void Validate(JsonObject? defaults)
    {
        if (defaults == null) return;
        if (defaults.Count is < 1 or > 2 || defaults.Any(pair => pair.Key is not ("model" or "model_reasoning_effort")) ||
            defaults["model"] is not NodeValue model || !model.TryGetValue<string>(out var id) || !ModelRouting.Owned(id) ||
            id.Length > 256 || id.Any(char.IsControl) ||
            defaults["model_reasoning_effort"] is JsonNode effort &&
                (effort is not NodeValue value || !value.TryGetValue<string>(out var text) ||
                 text.Length is < 1 or > 32 || text.Any(char.IsControl)))
            throw new InvalidDataException("The desktop model defaults are invalid.");
    }

    internal static bool IsUserFile(JsonObject parameters, string home) => parameters["filePath"] == null ||
        string.Equals(Path.GetFullPath(parameters["filePath"]!.GetValue<string>()), Path.Combine(Path.GetFullPath(home), "config.toml"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static JsonArray Edits(string method, JsonObject parameters) => method == "config/value/write"
        ? new JsonArray(new JsonObject
        {
            ["keyPath"] = parameters["keyPath"]?.DeepClone(), ["value"] = parameters["value"]?.DeepClone(),
            ["mergeStrategy"] = parameters["mergeStrategy"]?.DeepClone()
        }) : parameters["edits"]!.DeepClone().AsArray();

    internal static (JsonObject? Defaults, JsonArray NativeEdits) Partition(JsonObject? current, JsonArray edits)
    {
        var modelEdit = edits.LastOrDefault(edit => edit?["keyPath"]?.GetValue<string>() == "model");
        var model = modelEdit != null ? modelEdit["value"] : current?["model"];
        if (model is not NodeValue value || !value.TryGetValue<string>(out var id) || !ModelRouting.Owned(id))
            return (null, edits);
        var defaults = current?.DeepClone().AsObject() ?? new JsonObject();
        var native = new JsonArray();
        foreach (var edit in edits)
        {
            var key = edit!["keyPath"]!.GetValue<string>();
            if (key is "model" or "model_reasoning_effort")
            {
                if (edit["mergeStrategy"]?.GetValue<string>() is not ("replace" or "upsert"))
                    throw new InvalidDataException("The desktop model edit strategy is invalid.");
                defaults[key] = edit["value"]?.DeepClone();
            }
            else native.Add(edit.DeepClone());
        }
        Validate(defaults);
        return (defaults, native);
    }
}
