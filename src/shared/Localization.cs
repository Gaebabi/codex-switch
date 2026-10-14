using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CodexAccountDesk;

internal static class Loc
{
    private sealed record State(string Language, CultureInfo Culture);
    private static State _state = new("en", CultureInfo.GetCultureInfo("en-US"));
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Catalogs =
        new Dictionary<string, Dictionary<string, string>> { ["ko"] = ReadCatalog("ko"), ["en"] = ReadCatalog("en") };
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } = new[] { ("en", "English"), ("ko", "한국어") };
    public static string Language => Volatile.Read(ref _state).Language;
    public static CultureInfo Culture => Volatile.Read(ref _state).Culture;
    public static string LanguageName => Languages.Single(l => l.Code == Language).Name;
    public static void SetLanguage(string language)
    {
        if (!Catalogs.ContainsKey(language)) throw new ArgumentException("Unsupported application language: " + language);
        var culture = CultureInfo.GetCultureInfo(language == "ko" ? "ko-KR" : "en-US");
        Volatile.Write(ref _state, new State(language, culture));
        CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture; CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    public static string T(string source)
    {
        if (!Catalogs[Language].TryGetValue(source, out var translated)) throw new InvalidDataException("Missing application translation: " + source);
        return translated;
    }
    public static string F(string source, params object?[] arguments) => string.Format(Culture, T(source), arguments);
    public static string Error(string original) => Catalogs[Language].TryGetValue(original, out var translated) ? translated : original;
    public static void ValidateCatalogs()
    {
        var keys = Catalogs["ko"].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        foreach (var catalog in Catalogs.Values)
        {
            if (!keys.SequenceEqual(catalog.Keys.OrderBy(k => k, StringComparer.Ordinal))) throw new InvalidDataException("Language catalogs have different keys.");
            if (catalog.Values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("A translation is empty.");
        }
    }
    private static Dictionary<string, string> ReadCatalog(string language)
    {
        var assembly = typeof(Loc).Assembly;
        using var stream = assembly.GetManifestResourceStream("CodexAccountDesk.Strings." + language + ".json") ?? throw new InvalidDataException("Application language resources are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Application language resources are empty.");
    }
}
