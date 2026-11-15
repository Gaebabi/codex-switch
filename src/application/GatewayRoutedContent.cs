using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

// Only small routing options are mutable. Conversation/image values are slices
// of the validated original body, never a second JSON tree or serialized copy.
internal sealed class GatewayRoutedContent : HttpContent
{
    private readonly List<ReadOnlyMemory<byte>> _parts = new();
    private readonly long _length;
    internal readonly record struct Field(string Name, ReadOnlyMemory<byte> Value);
    internal static List<Field> Fields(ReadOnlyMemory<byte> json)
    {
        var reader = new Utf8JsonReader(json.Span); reader.Read();
        var fields = new List<Field>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!; reader.Read();
            var start = checked((int)reader.TokenStartIndex); reader.Skip();
            fields.Add(new Field(name, json.Slice(start, checked((int)reader.BytesConsumed) - start)));
        }
        return fields;
    }
    internal static IReadOnlySet<int> ApplyConfiguration(ReadOnlyMemory<byte> original, JsonObject options)
    {
        var consumed = new HashSet<int>();
        var input = Fields(original).FirstOrDefault(field => field.Name == "input").Value;
        if (input.IsEmpty || input.Span[0] != (byte)'[') return consumed;
        var index = 0;
        foreach (var item in Items(input))
        {
            var current = index++;
            if (item.Span[0] != (byte)'{') continue;
            var fields = Fields(item);
            if (GatewayJsonRequest.String(fields.FirstOrDefault(field => field.Name == "type").Value) != "configuration_update") continue;
            if (item.Length > 65536 || fields.Any(field => field.Name is not ("type" or "reasoning")) ||
                fields.FirstOrDefault(field => field.Name == "reasoning").Value.IsEmpty)
                throw new InvalidDataException("The native configuration update is not supported by this custom model connection.");
            var update = JsonNode.Parse(item.Span)!["reasoning"];
            if (update is not JsonObject reasoning)
                throw new InvalidDataException("The native reasoning update must be an object.");
            if (options["reasoning"] is not JsonObject effective) options["reasoning"] = effective = new JsonObject();
            foreach (var setting in reasoning) effective[setting.Key] = setting.Value?.DeepClone();
            consumed.Add(current);
        }
        return consumed;
    }
    internal GatewayRoutedContent(ReadOnlyMemory<byte> original, JsonObject options, bool custom, bool generic, bool removeReasoning, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? history = null, IReadOnlySet<int>? configurationUpdates = null, IReadOnlySet<int>? providerReasoning = null, IReadOnlySet<int>? providerItemIds = null)
    {
        var fields = Fields(original);
        var declarations = custom && configurationUpdates != null ? new HashSet<int>(configurationUpdates) : new HashSet<int>();
        if (providerReasoning != null) declarations.UnionWith(providerReasoning);
        var tools = fields.FirstOrDefault(field => field.Name == "tools").Value;
        var input = fields.FirstOrDefault(field => field.Name == "input").Value;
        if (custom && !input.IsEmpty && input.Span[0] == (byte)'[')
        {
            var index = 0;
            foreach (var item in Items(input))
            {
                var current = index++;
                if (item.Span[0] != (byte)'{') continue;
                var itemFields = Fields(item);
                if (GatewayJsonRequest.String(itemFields.FirstOrDefault(field => field.Name == "type").Value) != "additional_tools") continue;
                var additional = itemFields.FirstOrDefault(field => field.Name == "tools").Value;
                if (additional.IsEmpty || additional.Span[0] != (byte)'[')
                    throw new InvalidDataException("The native additional-tools declaration is invalid.");
                // Standard Responses carries the complete current catalog. Lite history
                // contains partial updates, including tools that are no longer available.
                // Replaying those updates into the current catalog revives stale tools.
                if (tools.IsEmpty || tools.Span[0] != (byte)'[')
                    throw new InvalidDataException("Historical tool updates require the current standard Responses tool catalog.");
                foreach (var tool in Items(additional))
                {
                    if (tool.Span[0] != (byte)'{' || GatewayJsonRequest.String(Fields(tool).FirstOrDefault(field => field.Name == "type").Value) == null)
                        throw new InvalidDataException("The native additional-tool definition is invalid.");
                }
                declarations.Add(current);
            }
        }
        Add("{"); var comma = false;
        foreach (var field in fields)
        {
            if (custom && (field.Name == "prompt_cache_key" || generic && field.Name == "service_tier")) continue;
            if (custom && field.Name == "reasoning" && options["reasoning"] == null) continue;
            if (comma) Add(","); comma = true;
            _parts.Add(JsonSerializer.SerializeToUtf8Bytes(field.Name)); Add(":");
            if (custom && (field.Name is "model" or "reasoning" || generic && field.Name == "text"))
                _parts.Add(JsonSerializer.SerializeToUtf8Bytes(options[field.Name]));
            else if (field.Name == "input" && (removeReasoning || history?.Count > 0 || declarations.Count > 0 || providerItemIds?.Count > 0)) AddInput(field.Value, removeReasoning, history, declarations, providerItemIds);
            else _parts.Add(field.Value);
        }
        if (custom && !fields.Any(field => field.Name == "reasoning") && options["reasoning"] != null)
        {
            if (comma) Add(",");
            Add("\"reasoning\":"); _parts.Add(JsonSerializer.SerializeToUtf8Bytes(options["reasoning"]));
        }
        Add("}"); _length = _parts.Sum(part => (long)part.Length);
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    }
    private void Add(string value) => _parts.Add(Encoding.UTF8.GetBytes(value));
    internal static List<ReadOnlyMemory<byte>> Items(ReadOnlyMemory<byte> json)
    {
        var items = new List<ReadOnlyMemory<byte>>();
        var reader = new Utf8JsonReader(json.Span); reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var start = checked((int)reader.TokenStartIndex); reader.Skip();
            items.Add(json.Slice(start, checked((int)reader.BytesConsumed) - start));
        }
        return items;
    }
    private void AddInput(ReadOnlyMemory<byte> json, bool removeReasoning, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? history, HashSet<int> declarations, IReadOnlySet<int>? providerItemIds)
    {
        Add("["); var comma = false; var index = 0;
        foreach (var value in Items(json))
        {
            var current = index++;
            if (declarations.Contains(current)) continue;
            if (history != null && history.TryGetValue(current, out var replacement))
            {
                if (comma) Add(","); comma = true; _parts.Add(replacement); continue;
            }
            if (removeReasoning && value.Span[0] == (byte)'{')
            {
                var fields = Fields(value);
                var type = fields.FirstOrDefault(f => f.Name == "type").Value;
                var encrypted = fields.FirstOrDefault(f => f.Name == "encrypted_content").Value;
                if (GatewayJsonRequest.String(type) == "reasoning" && !encrypted.IsEmpty && !encrypted.Span.SequenceEqual("null"u8)) continue;
            }
            if (comma) Add(","); comma = true;
            if (providerItemIds?.Contains(current) == true)
            {
                Add("{"); var fieldComma = false;
                foreach (var field in Fields(value).Where(field => field.Name != "id"))
                {
                    if (fieldComma) Add(","); fieldComma = true;
                    _parts.Add(JsonSerializer.SerializeToUtf8Bytes(field.Name)); Add(":"); _parts.Add(field.Value);
                }
                Add("}");
            }
            else _parts.Add(value);
        }
        Add("]");
    }
    protected override bool TryComputeLength(out long length) { length = _length; return true; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        foreach (var part in _parts) await stream.WriteAsync(part, cancellationToken).ConfigureAwait(false);
    }
    protected override void Dispose(bool disposing) { if (disposing) _parts.Clear(); base.Dispose(disposing); }
}
