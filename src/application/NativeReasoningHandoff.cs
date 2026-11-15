using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

// Reasoning state belongs to its originating model. Only proven custom state is
// omitted from the temporary native compaction request; saved history is untouched.
internal static class NativeReasoningHandoff
{
    internal static bool HasOutputId(JsonElement item) => !string.IsNullOrEmpty(JsonValue.String(item, "id")) &&
        JsonValue.String(item, "type") is "function_call" or "custom_tool_call";

    internal static string OutputKey(JsonElement item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var name in new[] { "type", "id", "call_id", "name", "arguments", "input" })
                if (item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                { writer.WritePropertyName(name); Canonical(writer, value); }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Canonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var child in value.EnumerateArray()) Canonical(writer, child);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    internal static bool HasState(JsonElement item) => JsonValue.String(item, "type") == "reasoning" &&
        (!string.IsNullOrEmpty(JsonValue.String(item, "encrypted_content")) ||
         item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() != 0);

    internal static string Key(JsonElement item)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        Append(JsonValue.String(item, "id"));
        Append(JsonValue.String(item, "encrypted_content"));
        foreach (var name in new[] { "summary", "content" })
        {
            Append(name);
            if (!item.TryGetProperty(name, out var parts) || parts.ValueKind == JsonValueKind.Null) { Append("0"); continue; }
            if (parts.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The provider reasoning state is malformed.");
            Append(parts.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || JsonValue.String(part, "type") is not string type || JsonValue.String(part, "text") is not string text)
                    throw new InvalidDataException("The provider reasoning state cannot be identified.");
                Append(type); Append(text);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static IReadOnlySet<int> Match(GatewayHistoryInput input, string[] keys, bool outputIds = false)
    {
        var expected = new HashSet<string>(keys, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var indices = new HashSet<int>();
        foreach (var item in input.Items)
        {
            using var document = JsonDocument.Parse(item.Bytes);
            if (outputIds ? !HasOutputId(document.RootElement) : item.Type != "reasoning") continue;
            var key = outputIds ? OutputKey(document.RootElement) : Key(document.RootElement);
            if (!expected.Contains(key)) continue;
            found.Add(key); indices.Add(item.Index);
        }
        if (!found.SetEquals(expected))
            throw new InvalidDataException("The provider output changed after native handoff preflight.");
        return indices;
    }
}
