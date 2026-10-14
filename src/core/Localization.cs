using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CodexAccountDesk;

internal interface ILanguagePackWriter
{
    void Write(string directory, string fileName, byte[] bytes);
}

internal static class Loc
{
    private sealed record State(LanguagePack Pack, CultureInfo Culture);
    private static readonly object Sync = new();
    private static readonly FrozenDictionary<string, string> Aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(ReadResource("legacy-keys.json"))!.ToFrozenDictionary(StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, LanguagePack> BuiltIns = LoadBuiltIns();
    private static FrozenDictionary<string, LanguagePack> _packs = BuiltIns;
    private static State _state = CreateState(BuiltIns["en"]);
    public static event Action? Changed;
    public static IReadOnlyList<LanguageInfo> Languages => Volatile.Read(ref _packs).Values.Select(p => p.Info).OrderBy(p => p.Code == "en" ? "" : p.EnglishName, StringComparer.Ordinal).ToArray();
    public static string Language => Volatile.Read(ref _state).Pack.Info.Code;
    public static CultureInfo Culture => Volatile.Read(ref _state).Culture;
    public static string LanguageName => Volatile.Read(ref _state).Pack.Info.NativeName;
    public static string LanguageCode => Language.ToUpperInvariant();
    public static bool RightToLeft => Volatile.Read(ref _state).Pack.Info.RightToLeft;
    public static LanguageInfo Current => Volatile.Read(ref _state).Pack.Info;
    public static string Id(string source) => Aliases.TryGetValue(source, out var id) ? id : source;
    public static void SetLanguage(string language)
    {
        if (!Volatile.Read(ref _packs).TryGetValue(language, out var pack)) throw new ArgumentException("Unsupported application language: " + language);
        var state = CreateState(pack); Volatile.Write(ref _state, state);
        CultureInfo.CurrentCulture = state.Culture; CultureInfo.CurrentUICulture = state.Culture;
        CultureInfo.DefaultThreadCurrentCulture = state.Culture; CultureInfo.DefaultThreadCurrentUICulture = state.Culture;
        Changed?.Invoke();
    }
    public static bool RestoreLanguage(string language)
    {
        var packs = Volatile.Read(ref _packs);
        var code = packs.ContainsKey(language) ? language : Languages.FirstOrDefault(p => string.Equals(p.CultureName, language, StringComparison.OrdinalIgnoreCase))?.Code;
        SetLanguage(code ?? "en"); return code != null;
    }
    private static State CreateState(LanguagePack pack)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(pack.Info.CultureName).Clone();
        if (culture.OptionalCalendars.Any(c => c is GregorianCalendar)) culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return new(pack, CultureInfo.ReadOnly(culture));
    }
    private static string Translate(State state, string source)
    {
        var id = Id(source);
        if (state.Pack.Strings.TryGetValue(id, out var translated)) return translated;
        if (BuiltIns["en"].Strings.TryGetValue(id, out var english)) return english;
        throw new InvalidDataException("Missing application message: " + id);
    }
    public static string T(string source) => Translate(Volatile.Read(ref _state), source);
    public static string F(string source, params object?[] arguments)
    { var state = Volatile.Read(ref _state); return string.Format(state.Culture, Translate(state, source), arguments); }
    public static string Count(string source, int count)
    {
        var state = Volatile.Read(ref _state); var id = Id(source);
        var pack = state.Pack.Strings.ContainsKey(id) ? state.Pack : BuiltIns["en"];
        var category = LanguagePack.Category(pack.Info.CultureName, count); var value = Translate(state, source);
        if (pack.Plurals.TryGetValue(id, out var forms)) value = forms.TryGetValue(category, out var form) ? form : forms["other"];
        return string.Format(state.Culture, value, count);
    }
    public static string Error(string original) => BuiltIns["en"].Strings.ContainsKey(Id(original)) ? T(original) : original;

    public static IReadOnlyList<string> LoadInstalled(string directory)
    {
        var problems = new List<string>();
        if (!Directory.Exists(directory)) { lock (Sync) Volatile.Write(ref _packs, BuiltIns); return problems; }
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Language pack directory cannot be a linked path.");
        var files = Directory.GetFiles(directory, "*.json");
        if (files.Length > 64) throw new InvalidDataException("At most 64 local language packs are supported.");
        var candidate = BuiltIns.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installed language pack cannot be a linked path.");
                var pack = LanguagePack.Parse(ReadFile(file), BuiltIns["en"], true);
                if (!string.Equals(Path.GetFileName(file), "pack-" + pack.Info.Code + ".json", StringComparison.Ordinal)) throw new InvalidDataException("Installed pack filename does not match its language code.");
                candidate[pack.Info.Code] = pack;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            { problems.Add(Path.GetFileName(file) + ": " + error.Message); }
        }
        lock (Sync) Volatile.Write(ref _packs, candidate.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        return problems;
    }
    public static LanguageInfo Install(string file, string directory, ILanguagePackWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var bytes = ReadFile(file); var pack = LanguagePack.Parse(bytes, BuiltIns["en"], true);
        lock (Sync)
        {
            var candidate = _packs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (candidate.Values.Count(p => p.Info.IsCustom) >= 64 && !(candidate.TryGetValue(pack.Info.Code, out var existing) && existing.Info.IsCustom)) throw new InvalidDataException("At most 64 local language packs are supported.");
            writer.Write(directory, "pack-" + pack.Info.Code + ".json", bytes);
            candidate[pack.Info.Code] = pack; Volatile.Write(ref _packs, candidate.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        }
        return pack.Info;
    }
    public static string RestoreBuiltIn(string directory)
    {
        lock (Sync)
        {
            var code = Language;
            if (!Current.IsCustom) return code;
            var file = Path.Combine(directory, "pack-" + code + ".json");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Local pack restoration cannot remove linked paths.");
            var candidate = _packs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            File.Delete(file);
            if (BuiltIns.TryGetValue(code, out var original)) candidate[code] = original; else candidate.Remove(code);
            Volatile.Write(ref _packs, candidate.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
            return BuiltIns.ContainsKey(code) ? code : "en";
        }
    }
    private static byte[] ReadFile(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > LanguagePack.MaxBytes) throw new InvalidDataException("Language pack size must be between 1 byte and 1 MiB.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    public static void ValidateCatalogs()
    {
        var basis = BuiltIns["en"];
        foreach (var pack in BuiltIns.Values)
            if (!basis.Strings.Keys.Order().SequenceEqual(pack.Strings.Keys.Order())) throw new InvalidDataException("Built-in language pack is incomplete: " + pack.Info.Code);
        foreach (var id in Aliases.Values) if (!basis.Strings.ContainsKey(id)) throw new InvalidDataException("Unknown legacy message mapping.");
    }
    private static FrozenDictionary<string, LanguagePack> LoadBuiltIns()
    {
        var codes = JsonSerializer.Deserialize<string[]>(ReadResource("index.json")) ?? throw new InvalidDataException("Language pack index is missing.");
        var basis = LanguagePack.Parse(ReadResource("en.json"), null, false);
        var packs = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase) { ["en"] = basis };
        foreach (var code in codes.Where(c => c != "en"))
        {
            var pack = LanguagePack.Parse(ReadResource(code + ".json"), basis, false);
            if (code != pack.Info.Code || !packs.TryAdd(code, pack)) throw new InvalidDataException("Duplicate or mismatched built-in language code.");
        }
        return packs.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("CodexAccountDesk.LanguagePacks." + name) ?? throw new InvalidDataException("Language pack resource is missing: " + name);
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
}
