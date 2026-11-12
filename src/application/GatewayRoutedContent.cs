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
        Add("{"); var comma = false;
        foreach (var field in Fields(original))
        {
            if (custom && (field.Name == "prompt_cache_key" || generic && field.Name == "service_tier")) continue;
            if (custom && field.Name == "reasoning" && options["reasoning"] == null) continue;
            if (comma) Add(","); comma = true;
            _parts.Add(JsonSerializer.SerializeToUtf8Bytes(field.Name)); Add(":");
            if (custom && (field.Name is "model" or "reasoning" || generic && field.Name == "text"))
                _parts.Add(JsonSerializer.SerializeToUtf8Bytes(options[field.Name]));
            else if (field.Name == "input" && (removeReasoning || history?.Count > 0)) AddInput(field.Value, removeReasoning, history);
            else _parts.Add(field.Value);
        }
        if (custom && !Fields(original).Any(field => field.Name == "reasoning") && options["reasoning"] != null)
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
    private void AddInput(ReadOnlyMemory<byte> json, bool removeReasoning, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? history)
    {
        Add("["); var comma = false; var index = 0;
        foreach (var value in Items(json))
        {
            var current = index++;
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
