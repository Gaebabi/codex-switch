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
    internal GatewayRoutedContent(ReadOnlyMemory<byte> original, JsonObject options, bool custom, bool generic, bool removeReasoning, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? history = null)
    {
        var fields = Fields(original);
        var declarations = new HashSet<int>();
        JsonArray? tools = null;
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
                if (additional.IsEmpty || JsonNode.Parse(additional.Span) is not JsonArray added)
                    throw new InvalidDataException("The native additional-tools declaration is invalid.");
                if (tools == null)
                {
                    var existing = fields.FirstOrDefault(field => field.Name == "tools").Value;
                    tools = existing.IsEmpty ? new JsonArray() : JsonNode.Parse(existing.Span) as JsonArray ??
                        throw new InvalidDataException("The active tool declarations are invalid.");
                }
                foreach (var tool in added)
                {
                    if (tool is not JsonObject definition || definition["type"] is not System.Text.Json.Nodes.JsonValue type || !type.TryGetValue<string>(out _))
                        throw new InvalidDataException("The native additional-tool definition is invalid.");
                    var matches = tools.OfType<JsonObject>().Where(candidate => JsonNode.DeepEquals(candidate["type"], definition["type"]) &&
                        JsonNode.DeepEquals(candidate["name"], definition["name"])).ToArray();
                    if (matches.Any(candidate => !JsonNode.DeepEquals(candidate, definition)))
                        throw new InvalidDataException("Conflicting native tool definitions cannot be transferred to the selected model.");
                    if (matches.Length == 0) tools.Add(definition.DeepClone());
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
            else if (field.Name == "tools" && tools != null) _parts.Add(JsonSerializer.SerializeToUtf8Bytes(tools));
            else if (field.Name == "input" && (removeReasoning || history?.Count > 0 || declarations.Count > 0)) AddInput(field.Value, removeReasoning, history, declarations);
            else _parts.Add(field.Value);
        }
        if (tools != null && !fields.Any(field => field.Name == "tools"))
        {
            if (comma) Add(","); comma = true;
            Add("\"tools\":"); _parts.Add(JsonSerializer.SerializeToUtf8Bytes(tools));
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
    private void AddInput(ReadOnlyMemory<byte> json, bool removeReasoning, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? history, HashSet<int> declarations)
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
            if (comma) Add(","); comma = true; _parts.Add(value);
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
