using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexAccountDesk;

internal sealed record LanguageInfo(string Code, string NativeName, string EnglishName, string CultureName,
    bool RightToLeft, int Translated, int Total, bool IsCustom);

internal sealed class LanguagePack
{
    public const int MaxBytes = 1024 * 1024;
    public LanguageInfo Info { get; }
    public FrozenDictionary<string, string> Strings { get; }
    public FrozenDictionary<string, FrozenDictionary<string, string>> Plurals { get; }
    private LanguagePack(LanguageInfo info, Dictionary<string, string> strings, Dictionary<string, FrozenDictionary<string, string>> plurals)
    { Info = info; Strings = strings.ToFrozenDictionary(StringComparer.Ordinal); Plurals = plurals.ToFrozenDictionary(StringComparer.Ordinal); }

    public static LanguagePack Parse(byte[] bytes, LanguagePack? basis, bool custom)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("Language pack size must be between 1 byte and 1 MiB.");
        var json = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsMemory(3) : bytes.AsMemory();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement; CheckObject(root);
        var allowed = new[] { "$schema", "schemaVersion", "code", "nativeName", "englishName", "culture", "direction", "strings", "plurals" };
        if (root.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal))) throw new InvalidDataException("Unknown language pack metadata field.");
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1) throw new InvalidDataException("Unsupported language pack schema version.");
        string Text(string key, int max = 64)
        {
            if (!root.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.String) throw new InvalidDataException("Missing or invalid language pack metadata: " + key);
            var value = field.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("Invalid language pack metadata: " + key);
            CheckText(value); return value;
        }
        var code = Text("code"); var cultureName = Text("culture");
        if (!Regex.IsMatch(code, "^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")) throw new InvalidDataException("Language code must be a portable BCP 47 tag.");
        var culture = CultureInfo.GetCultureInfo(cultureName);
        if (culture.Name.Length == 0 || !string.Equals(code.Split('-')[0], culture.Name.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Language code and culture must refer to the same language.");
        var direction = Text("direction");
        if (direction is not ("ltr" or "rtl") || (direction == "rtl") != culture.TextInfo.IsRightToLeft) throw new InvalidDataException("Language direction does not match its culture.");
        var source = root.GetProperty("strings"); CheckObject(source);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in source.EnumerateObject())
        {
            if (!Regex.IsMatch(property.Name, "^[a-z][a-z0-9_.-]{0,79}$")) throw new InvalidDataException("Invalid message identifier.");
            if (basis != null && !basis.Strings.ContainsKey(property.Name)) throw new InvalidDataException("Unknown message identifier: " + property.Name);
            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) throw new InvalidDataException("Empty or oversized message: " + property.Name);
            CheckText(value); CompositeFormat.Parse(value);
            if (basis != null && Signature(value) != Signature(basis.Strings[property.Name])) throw new InvalidDataException("Translation placeholders do not match: " + property.Name);
            strings.Add(property.Name, value);
        }
        if (strings.Count == 0 || strings.Count > 2048) throw new InvalidDataException("Invalid language pack message count.");
        var plurals = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("plurals", out var pluralRoot))
        {
            CheckObject(pluralRoot);
            foreach (var entry in pluralRoot.EnumerateObject())
            {
                if (!strings.ContainsKey(entry.Name)) throw new InvalidDataException("Plural message must have a translated base: " + entry.Name);
                CheckObject(entry.Value); var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var form in entry.Value.EnumerateObject())
                {
                    if (form.Name is not ("zero" or "one" or "two" or "few" or "many" or "other")) throw new InvalidDataException("Unknown plural category.");
                    var value = form.Value.GetString();
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) throw new InvalidDataException("Empty plural translation.");
                    CheckText(value); CompositeFormat.Parse(value);
                    if (Signature(value) != Signature(strings[entry.Name])) throw new InvalidDataException("Plural placeholders do not match: " + entry.Name);
                    forms.Add(form.Name, value);
                }
                if (!forms.ContainsKey("other")) throw new InvalidDataException("Plural translations require the other category.");
                plurals.Add(entry.Name, forms.ToFrozenDictionary(StringComparer.Ordinal));
            }
        }
        return new(new(code, Text("nativeName"), Text("englishName"), culture.Name, direction == "rtl", strings.Count, basis?.Strings.Count ?? strings.Count, custom), strings, plurals);
    }

    private static void CheckObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Language pack fields must be JSON objects.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate language pack field.");
    }
    private static void CheckText(string value)
    {
        if (value.Contains('\ufffd') || value.Any(c => char.IsControl(c) && c is not ('\n' or '\t') || c is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069'))
            throw new InvalidDataException("Language packs cannot contain hidden direction overrides or unsafe control characters.");
    }
    public static string Signature(string format)
    {
        CompositeFormat.Parse(format);
        var arguments = new List<string>();
        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{') continue;
            if (format[index + 1] == '{') { index++; continue; }
            var end = format.IndexOf('}', index + 1);
            var item = format[(index + 1)..end];
            var colon = item.IndexOf(':');
            var header = colon < 0 ? item : item[..colon];
            var comma = header.IndexOf(',');
            var slot = int.Parse(comma < 0 ? header : header[..comma], CultureInfo.InvariantCulture);
            var alignment = comma < 0 ? 0 : int.Parse(header[(comma + 1)..], CultureInfo.InvariantCulture);
            arguments.Add(slot + "," + alignment + ":" + (colon < 0 ? "" : item[(colon + 1)..]));
            index = end;
        }
        return string.Join("|", arguments.OrderBy(s => s, StringComparer.Ordinal));
    }
    public static string Category(string language, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var primary = language.Split('-')[0];
        return primary switch
        {
            "ko" or "ja" or "zh" => "other",
            "fr" when count is 0 or 1 => "one",
            "pt" when language == "pt-BR" && count is 0 or 1 => "one",
            "fr" or "pt" or "es" or "it" when count != 0 && count % 1000000 == 0 => "many",
            "ru" or "uk" when count % 10 == 1 && count % 100 != 11 => "one",
            "ru" or "uk" when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => "few",
            "ru" or "uk" => "many",
            "ar" when count == 0 => "zero",
            "ar" when count == 1 => "one",
            "ar" when count == 2 => "two",
            "ar" when count % 100 is >= 3 and <= 10 => "few",
            "ar" when count % 100 is >= 11 and <= 99 => "many",
            "ar" => "other",
            _ => count == 1 ? "one" : "other"
        };
    }
}
