using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexAccountDesk;

// JsonDocument sizes its pooled metadata from payload bytes, even when nearly
// all bytes belong to a few image strings. Validate tokens without that allocation.
internal sealed class GatewayJsonRequest
{
    private readonly List<GatewayRoutedContent.Field> _fields;
    internal GatewayJsonRequest(ReadOnlyMemory<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes.Span);
        var stack = new Stack<HashSet<string>?>();
        var tokens = 0; var propertyBytes = 0;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("The Responses request is invalid.");
        do
        {
            if (++tokens > 1000000) throw new InvalidDataException("The model request has too many JSON tokens.");
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: stack.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                case JsonTokenType.StartArray: stack.Push(null); break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray: stack.Pop(); break;
                case JsonTokenType.PropertyName:
                    if (reader.ValueSpan.Length > 1024 || (propertyBytes += reader.ValueSpan.Length) > 8 * 1024 * 1024)
                        throw new InvalidDataException("The model request's JSON property names are too large.");
                    if (!stack.Peek()!.Add(reader.GetString()!)) throw new JsonException("Duplicate JSON properties are not accepted.");
                    break;
            }
        } while (reader.Read());
        if (stack.Count != 0) throw new JsonException("The model request is incomplete.");
        _fields = GatewayRoutedContent.Fields(bytes);
    }
    internal ReadOnlyMemory<byte> Get(string name) => _fields.FirstOrDefault(field => field.Name == name).Value;
    internal bool Present(string name)
    { var value = Get(name); return !value.IsEmpty && !value.Span.SequenceEqual("null"u8); }
    internal string? Model => String(Get("model"));
    internal static string? String(ReadOnlyMemory<byte> value)
    {
        if (value.IsEmpty || value.Length > 1024) return null;
        var reader = new Utf8JsonReader(value.Span);
        return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
    }
    internal bool HasEncrypted(string type)
    {
        var input = Get("input");
        if (input.IsEmpty || input.Span[0] != (byte)'[') return false;
        foreach (var item in GatewayRoutedContent.Items(input))
        {
            if (item.Span[0] != (byte)'{') continue;
            var fields = GatewayRoutedContent.Fields(item);
            var encrypted = fields.FirstOrDefault(field => field.Name == "encrypted_content").Value;
            if (String(fields.FirstOrDefault(field => field.Name == "type").Value) == type && !encrypted.IsEmpty && !encrypted.Span.SequenceEqual("null"u8)) return true;
        }
        return false;
    }
    internal void Release() => _fields.Clear();
}
