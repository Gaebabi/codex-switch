using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexAccountDesk;

var checks = new List<string>();
void Check(string name, Action action) { action(); checks.Add(name); }
void Assert(bool value) { if (!value) throw new InvalidOperationException("Localization assertion failed."); }
void Rejected(Action action)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException) { return; }
    throw new Exception("Invalid pack was accepted.");
}
byte[] Resource(string name)
{
    using var input = typeof(Loc).Assembly.GetManifestResourceStream("CodexAccountDesk.LanguagePacks." + name)!;
    using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
}
var englishBytes = Resource("en.json");
Check("Both app policies and catalogues live in one UI-neutral assembly", () =>
{
    var core = typeof(Loc).Assembly;
    Assert(core.GetName().Name == "CodexSwitch.Core" && typeof(AccountProfile).Assembly == core &&
        typeof(AccountSwitchCoordinator).Assembly == core && typeof(RefreshCoordinator).Assembly == core);
    Assert(!core.GetReferencedAssemblies().Any(a => a.Name is "Avalonia" or "Avalonia.Controls" or "PresentationFramework" or "System.Management" or "CodexSwitch"));
    Assert(!core.GetTypes().SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)).Any(m => m.IsDefined(typeof(System.Runtime.InteropServices.DllImportAttribute), false)));
});
if (OperatingSystem.IsLinux())
    Check("Linux vault defaults use a POSIX root and unknown package metadata refuses", () =>
    {
        var data = new VaultData(); Assert(data.Drives.Count == 1 && data.Drives[0].Root == "/" && data.Drives[0].Label == "/");
        var rejected = false;
        try { LinuxClientDiscovery.VerifyPackageStatus("", "unverified", "unknown"); }
        catch (IOException) { rejected = true; }
        Assert(rejected);
    });
else if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
    Check("Unsupported vault/discovery cannot take a Windows fallback", () =>
    {
        var vaultRejected = false; var discoveryRejected = false;
        try { _ = new VaultData(); } catch (PlatformNotSupportedException) { vaultRejected = true; }
        try { RuntimeLocator.FindAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult(); }
        catch (PlatformNotSupportedException) { discoveryRejected = true; }
        Assert(vaultRejected && discoveryRejected);
    });
var basis = LanguagePack.Parse(englishBytes, null, false);
byte[] Mutate(Action<JsonObject> action)
{ var json = JsonNode.Parse(englishBytes)!.AsObject(); action(json); return Encoding.UTF8.GetBytes(json.ToJsonString()); }
Check("Nine complete built-in packs preserve English/Korean source aliases", () => { Loc.ValidateCatalogs(); Assert(Loc.Languages.Count == 9); Loc.SetLanguage("ko"); Assert(Loc.T("계정 추가") == "계정 추가"); Loc.SetLanguage("en"); Assert(Loc.T("계정 추가") == "Add account"); });
Check("Linux preview and key failures resolve through every bundled catalogue", () =>
{
    foreach (var info in Loc.Languages)
    {
        Loc.SetLanguage(info.Code);
        Assert(Loc.Error(new LinuxVaultException(LinuxKeyFailure.Locked).Message) == Loc.T("msg.linux_keyring_locked"));
        Assert(Loc.Error(new LinuxVaultException(LinuxKeyFailure.Missing).Message) == Loc.T("msg.linux_keyring_missing"));
        Assert(!Loc.T("msg.linux_preview_accounts_unavailable").StartsWith("msg.", StringComparison.Ordinal));
    }
    Loc.SetLanguage("en");
});
foreach (var info in Loc.Languages)
{
    Check(info.Code + " messages/culture/calendar/formats", () =>
    {
        Loc.SetLanguage(info.Code); Assert(Loc.Culture.Name == info.CultureName && Loc.Culture.IsReadOnly && Loc.Culture.DateTimeFormat.Calendar is GregorianCalendar);
        foreach (var id in basis.Strings.Keys) Assert(!string.IsNullOrWhiteSpace(Loc.T(id)));
        var number = Loc.F("{0:0.##} / {1:0.##} GiB", 1.25, 2.5); Assert(number.Contains(Loc.Culture.NumberFormat.NumberDecimalSeparator));
        Assert(Loc.Count("msg.accounts", 1).Contains("1") && Loc.Count("초기화권 {0}개", 2).Contains("2"));
    });
}
Check("BCP 47 culture aliases and missing saved preference do not overwrite data", () => { Assert(Loc.RestoreLanguage("ko-KR") && Loc.Language == "ko"); Assert(!Loc.RestoreLanguage("zz-missing") && Loc.Language == "en"); });
Check("Manual unsupported selection fails", () => Rejected(() => Loc.SetLanguage("zz-missing")));
Check("Integer plural categories cover shipped locales and Arabic/Russian", () =>
{
    Assert(LanguagePack.Category("en-US",1)=="one" && LanguagePack.Category("en-US",0)=="other");
    Assert(LanguagePack.Category("fr-FR",0)=="one" && LanguagePack.Category("fr-FR",1000000)=="many");
    Assert(LanguagePack.Category("pt-BR",0)=="one" && LanguagePack.Category("pt-PT",0)=="other");
    Assert(LanguagePack.Category("ja-JP",1)=="other" && LanguagePack.Category("zh-TW",1)=="other");
    Assert(LanguagePack.Category("ru-RU",21)=="one" && LanguagePack.Category("ru-RU",12)=="many" && LanguagePack.Category("ru-RU",24)=="few");
    Assert(new[]{0,1,2,3,11,100}.Select(n=>LanguagePack.Category("ar-SA",n)).SequenceEqual(new[]{"zero","one","two","few","many","other"}));
});
Check("Negative count is rejected", () => Rejected(() => LanguagePack.Category("en",-1)));
Check("English and French account singulars are natural", () => { Loc.SetLanguage("en"); Assert(Loc.Count("msg.accounts",1)=="1 account"); Loc.SetLanguage("fr"); Assert(Loc.Count("msg.accounts",1)=="1 compte"); });
Check("Empty and oversized payloads are rejected", () => { Rejected(()=>LanguagePack.Parse(Array.Empty<byte>(),basis,true)); Rejected(()=>LanguagePack.Parse(new byte[LanguagePack.MaxBytes+1],basis,true)); });
Check("Malformed JSON rejected", () => Rejected(()=>LanguagePack.Parse(Encoding.UTF8.GetBytes("{"),basis,true)));
Check("Standard UTF-8 BOM is accepted; UTF-16 is rejected", () => { LanguagePack.Parse(new byte[]{0xef,0xbb,0xbf}.Concat(englishBytes).ToArray(),basis,true); Rejected(()=>LanguagePack.Parse(Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(englishBytes)),basis,true)); });
Check("Duplicate metadata rejected", () => Rejected(()=>LanguagePack.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(englishBytes).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")),basis,true)));
Check("Duplicate messages rejected", () => Rejected(()=>LanguagePack.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(englishBytes).Replace("\"msg.language\": \"Language\"", "\"msg.language\": \"Language\", \"msg.language\": \"Other\"")),basis,true)));
Check("Unknown metadata rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["execute"]="not executable"),basis,true)));
Check("Unsupported schema rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["schemaVersion"]=2),basis,true)));
Check("Non-integer and mistyped versions are rejected by schema validation", () => { Rejected(()=>LanguagePack.Parse(Mutate(j=>j["schemaVersion"]=1.5),basis,true)); Rejected(()=>LanguagePack.Parse(Mutate(j=>j["schemaVersion"]="1"),basis,true)); });
Check("Path traversal language codes rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["code"]="../en"),basis,true)));
Check("Mismatched culture rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["culture"]="de-DE"),basis,true)));
Check("Incorrect text direction rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["direction"]="rtl"),basis,true)));
Check("Unknown message ID rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]!.AsObject()["msg.typo"]="Unexpected"),basis,true)));
Check("Empty translation rejected", () => Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]!["msg.language"]=""),basis,true)));
Check("Removed, duplicated or changed format placeholders rejected", () =>
{
    var id=Loc.Id("크레딧 {0}");
    foreach(var value in new[]{"Credits", "Credits {0} {0}", "Credits {1}", "Credits {0:0.00}"}) Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]![id]=value),basis,true));
});
Check("Reordered placeholders retain meaning", () => { var bytes=Mutate(j=>j["strings"]![Loc.Id("{0} {1:0.#}%")]="{1:0.#}% — {0}"); LanguagePack.Parse(bytes,basis,true); });
Check("Escaped literal braces cannot conceal, add or remove format arguments", () =>
{
    var id=Loc.Id("크레딧 {0}");
    LanguagePack.Parse(Mutate(j=>j["strings"]![id]="Credits {{{0}}}"),basis,true);
    Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]!["msg.language"]="Language {{{0}}}"),basis,true));
    Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]![id]="Credits {{0}}"),basis,true));
    Assert(LanguagePack.Signature("{{x}} {1:0.#} {{{0}}}")==LanguagePack.Signature("{0} {1:0.#}"));
});
Check("Untrusted translations cannot introduce large or altered field alignment", () =>
{
    foreach(var value in new[]{"Credits {0,1000000}", "Credits {0,-1000000}", "Credits {0,10}"})
        Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]![Loc.Id("크레딧 {0}")]=value),basis,true));
    LanguagePack.Parse(Mutate(j=>j["strings"]![Loc.Id("크레딧 {0}")]="Credits {0,0}"),basis,true);
});
Check("Hidden direction overrides and unsafe control text rejected", () => { foreach(var value in new[]{"Text\u202e", "Text\u2066", "Text\0", "Text\ufffd"}) Rejected(()=>LanguagePack.Parse(Mutate(j=>j["strings"]!["msg.language"]=value),basis,true)); });
Check("Plural other and placeholder safety are required", () =>
{
    var id=Loc.Id("msg.accounts");
    Rejected(()=>LanguagePack.Parse(Mutate(j=>j["plurals"]![id]!.AsObject().Remove("other")),basis,true));
    Rejected(()=>LanguagePack.Parse(Mutate(j=>j["plurals"]![id]!["one"]="A count without a placeholder"),basis,true));
});
var directory=Path.Combine(Path.GetTempPath(),"codex-language-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var packPath=Path.Combine(directory,"candidate.json"); var installed=Path.Combine(directory,"languages");
    var partial=new JsonObject { ["schemaVersion"]=1,["code"]="ca",["nativeName"]="Català",["englishName"]="Catalan",["culture"]="ca-ES",["direction"]="ltr",["strings"]=new JsonObject { [Loc.Id("계정 추가")]="Afegeix un compte" } };
    File.WriteAllText(packPath,partial.ToJsonString(),Encoding.UTF8);
    Check("Real install/reload uses data-only JSON and English plural fallback", () =>
    {
        var info=Loc.Install(packPath,installed,PlatformLanguagePackWriter.Instance); Assert(info.IsCustom && info.Translated==1 && info.Total==basis.Strings.Count);
        Loc.SetLanguage("ca"); Assert(Loc.T("계정 추가")=="Afegeix un compte" && Loc.T("새로 고침")=="Refresh" && Loc.Count("msg.accounts",1)=="1 account");
        Assert(File.Exists(Path.Combine(installed,"pack-ca.json"))); Loc.LoadInstalled(installed); Assert(Loc.RestoreLanguage("ca") && Loc.Language=="ca");
    });
    Check("Rejected replacement keeps installed bytes and current selection unchanged", () =>
    {
        var before=File.ReadAllBytes(Path.Combine(installed,"pack-ca.json")); partial["strings"]!["unknown.id"]="Invalid"; File.WriteAllText(packPath,partial.ToJsonString(),Encoding.UTF8);
        Rejected(()=>Loc.Install(packPath,installed,PlatformLanguagePackWriter.Instance)); Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(installed,"pack-ca.json"))) && Loc.Language=="ca"); partial["strings"]!.AsObject().Remove("unknown.id");
    });
    Check("Platform write failure cannot publish an unpersisted language pack", () =>
    {
        var before = File.ReadAllBytes(Path.Combine(installed,"pack-ca.json"));
        var oldText = Loc.T("계정 추가");
        partial["strings"]![Loc.Id("계정 추가")] = "Unpersisted replacement";
        File.WriteAllText(packPath,partial.ToJsonString(),Encoding.UTF8);
        var rejected = false;
        try { Loc.Install(packPath,installed,new RefusingPackWriter()); } catch (IOException) { rejected = true; }
        Assert(rejected && Loc.Language=="ca" && Loc.T("계정 추가")==oldText &&
            before.SequenceEqual(File.ReadAllBytes(Path.Combine(installed,"pack-ca.json"))));
    });
    Check("Valid RTL metadata and Gregorian culture are supported", () =>
    {
        partial["code"]="ar"; partial["nativeName"]="العربية"; partial["englishName"]="Arabic"; partial["culture"]="ar-SA"; partial["direction"]="rtl";
        partial["strings"]![Loc.Id("계정 추가")]="إضافة حساب"; File.WriteAllText(packPath,partial.ToJsonString(),Encoding.UTF8);
        Loc.Install(packPath,installed,PlatformLanguagePackWriter.Instance); Loc.SetLanguage("ar"); Assert(Loc.RightToLeft && Loc.Culture.DateTimeFormat.Calendar is GregorianCalendar && Loc.T("계정 추가")=="إضافة حساب");
    });
    Check("Restoring an external-only pack selects English without other pack loss", () => { Loc.SetLanguage(Loc.RestoreBuiltIn(installed)); Assert(Loc.Language=="en" && !File.Exists(Path.Combine(installed,"pack-ar.json")) && File.Exists(Path.Combine(installed,"pack-ca.json"))); });
    Check("Corrupt installed packs report failures without hiding or losing valid packs", () => { File.WriteAllText(Path.Combine(installed,"broken.json"),"{"); File.WriteAllBytes(Path.Combine(installed,"invalid.json"),Mutate(j=>j["schemaVersion"]=2)); var problems=Loc.LoadInstalled(installed); Assert(problems.Count==2 && Loc.Languages.Any(p=>p.Code=="ca") && Loc.Languages.Any(p=>p.Code=="en")); });
}
finally { Loc.LoadInstalled(Path.Combine(directory,"empty")); Loc.SetLanguage("en"); Directory.Delete(directory,true); }
Console.WriteLine(JsonSerializer.Serialize(new { passed=checks.Count,failed=0,checks,scope="Production language-pack parsing/catalogue/install APIs with disposable text files only; no account/Codex data or process access." },new JsonSerializerOptions { WriteIndented=true }));

internal sealed class RefusingPackWriter : ILanguagePackWriter
{
    public void Write(string directory, string fileName, byte[] bytes) => throw new IOException("Synthetic platform write refusal");
}
