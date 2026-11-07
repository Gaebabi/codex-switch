using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodexAccountDesk;

var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Protocol boundary contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Rejected<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected protocol/storage rejection was not observed."); }
Check("RPC/desktop state/queue algorithms share one UI-independent protocol assembly", () =>
{
    var assembly = typeof(DesktopSessionMonitor).Assembly;
    Assert(assembly.GetName().Name == "CodexSwitch.Protocol" && typeof(CodexRpc).Assembly == assembly &&
        typeof(SessionActivity).Assembly == assembly && typeof(DesktopQueueState).Assembly == assembly && typeof(ServerQueueStore).Assembly == assembly);
    Assert(!assembly.GetReferencedAssemblies().Any(a => a.Name is "CodexSwitch" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
});
Check("Native SQLite is isolated from UI/domain/protocol projects", () =>
{
    var assembly = typeof(ReadOnlySqlite).Assembly;
    Assert(assembly.GetName().Name == "CodexSwitch.Sqlite" && !assembly.GetReferencedAssemblies().Any(a =>
        a.Name is "CodexSwitch" or "CodexSwitch.Core" or "CodexSwitch.Protocol" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
});
JsonElement Session(string status, string? turn = null, bool child = false, long revision = 1) => JsonSerializer.SerializeToElement(new {
    type = "snapshot", revision, conversationState = new { parentThreadId = child ? "main" : null, sideConversation = false,
        threadRuntimeStatus = new { type = status, activeFlags = Array.Empty<string>() }, threadGoal = (object?)null,
        turns = turn == null ? Array.Empty<object>() : new object[] { new { turnId = "fixture-turn", status = turn } } } });
Check("Error count stays unknown until all discovered chats are observed", () =>
{
    var ledger = new SessionActivityLedger(); Assert(ledger.Read().Errors == null);
    ledger.Discover("unread", "owner"); ledger.CompleteDiscovery(); Assert(ledger.Read().Errors == null);
    ledger.Apply("unread", "owner", Session("idle")); Assert(ledger.Read().Errors == 0);
    ledger.Reset(); Assert(ledger.Read().Errors == null);
    ledger.CompleteDiscovery(); Assert(ledger.Read().Errors == 0);
});
Check("Only currently failed main chats count, without duplicates or interruptions", () =>
{
    var ledger = new SessionActivityLedger();
    foreach (var id in new[] { "system", "failed", "active", "cancelled", "completed", "child", "unloaded" }) ledger.Discover(id, "owner");
    ledger.Apply("system", "owner", Session("systemError", "failed"));
    ledger.Apply("system", "owner", Session("systemError", "failed"));
    ledger.Apply("failed", "owner", Session("idle", "failed"));
    ledger.Apply("active", "owner", Session("active", "failed"));
    ledger.Apply("cancelled", "owner", Session("idle", "interrupted"));
    ledger.Apply("completed", "owner", Session("idle", "completed"));
    ledger.Apply("child", "owner", Session("systemError", "failed", true));
    ledger.Apply("unloaded", "owner", Session("notLoaded", "failed"));
    ledger.CompleteDiscovery(); Assert(ledger.Read().Errors == 2);
    ledger.Apply("failed", "other-owner", Session("idle", "completed", revision: 2)); Assert(ledger.Read().Errors == 2);
    ledger.Apply("system", "owner", Session("active", "inProgress", revision: 2)); Assert(ledger.Read().Errors == 1);
    ledger.Apply("failed", "owner", Session("idle", "completed", revision: 2)); Assert(ledger.Read().Errors == 0);
});
Check("Latest failure and recovery patches update the count without rescanning history", () =>
{
    var ledger = new SessionActivityLedger(); ledger.Discover("main", "owner"); ledger.Apply("main", "owner", Session("active", "inProgress")); ledger.CompleteDiscovery();
    ledger.Apply("main", "owner", JsonSerializer.SerializeToElement(new { type = "patches", baseRevision = 1, revision = 2, patches = new object[] {
        new { op = "replace", path = new object[] { "threadRuntimeStatus", "type" }, value = "idle" },
        new { op = "replace", path = new object[] { "turns", 0, "status" }, value = "failed" } } }));
    Assert(ledger.Read().Errors == 1);
    ledger.Apply("main", "owner", JsonSerializer.SerializeToElement(new { type = "patches", baseRevision = 2, revision = 3, patches = new object[] {
        new { op = "replace", path = new object[] { "threadRuntimeStatus", "type" }, value = "active" } } }));
    Assert(ledger.Read().Errors == 0);
    ledger.Remove("main"); Assert(ledger.Read().Errors == 0);
});
var absent = Path.Combine(Path.GetTempPath(), "codex-protocol-contract-" + Guid.NewGuid().ToString("N"));
var monitor = new DesktopSessionMonitor(absent);
try
{
    Check("Unstarted observation stays unavailable without inspecting a home", () => Assert(!monitor.Current.Available));
    if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
    {
        Check("Unsupported desktop IPC refuses before unauthenticated Unix connection", () => Rejected<PlatformNotSupportedException>(() => monitor.Start()));
    }
    if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
    {
        Check("Unsupported SQLite refuses before Windows library fallback", () => Rejected<PlatformNotSupportedException>(() => new ReadOnlySqlite(absent)));
    }
    else
    {
        Check("Read-only open refuses an absent database without creating a file", () =>
        { Rejected<IOException>(() => { using var database = new ReadOnlySqlite(absent); }); Assert(!File.Exists(absent)); });
        var corrupt = Path.Combine(Path.GetTempPath(), "codex-corrupt-fixture-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var bytes = Encoding.UTF8.GetBytes("Synthetic malformed SQLite header"); File.WriteAllBytes(corrupt, bytes);
        try
        {
            Check("Corrupt read-only metadata is rejected and its bytes remain intact", () =>
            {
                Rejected<IOException>(() => { using var database = new ReadOnlySqlite(corrupt); database.Read("SELECT name FROM sqlite_master"); });
                Assert(File.ReadAllBytes(corrupt).SequenceEqual(bytes));
            });
        }
        finally { File.Delete(corrupt); }
        Check("Metadata reader binds Unicode parameters and rejects a prepared write", () =>
        {
            using var database = new ReadOnlySqlite(":memory:");
            Assert(database.Read("SELECT ?", "Synthetic 한글 日本語").Single().Single() == "Synthetic 한글 日本語");
            Rejected<InvalidOperationException>(() => database.Read("CREATE TABLE forbidden (id INTEGER)"));
            Assert(database.Read("SELECT name FROM sqlite_master").Count == 0);
        });
    }
}
finally { await monitor.DisposeAsync(); }
ClientCredentialPolicy Policy(string store, string? forced = null, string? workspace = null, string requirements = "null")
{
    using var config = JsonDocument.Parse(JsonSerializer.Serialize(new { config = new { cli_auth_credentials_store = store, forced_login_method = forced, forced_chatgpt_workspace_id = workspace } }));
    using var required = JsonDocument.Parse("{\"requirements\":" + requirements + "}");
    return ClientCredentialPolicy.Parse(config.RootElement, required.RootElement);
}
Check("Only the observed explicit file backend can admit file credential operations", () =>
{
    Assert(Policy("file").FileCredentialsAllowed);
    foreach (var store in new[] { "keyring", "auto", "ephemeral" }) Assert(!Policy(store).FileCredentialsAllowed);
});
Check("Login/workspace and managed login restrictions prevent incompatible account application", () =>
{
    Assert(!Policy("file", "api").FileCredentialsAllowed);
    var restricted = Policy("file", "chatgpt", "synthetic-workspace"); Assert(restricted.AllowsAccount("synthetic-workspace") && !restricted.AllowsAccount("different-workspace"));
    Assert(!Policy("file", requirements: "{\"cliAuthCredentialsStore\":\"file\",\"allowedLoginMethods\":[\"api\"]}").FileCredentialsAllowed);
});
Check("Unknown backends and inconsistent or missing managed policy contracts refuse", () =>
{
    Rejected<InvalidDataException>(() => Policy("unknown"));
    Rejected<InvalidDataException>(() => Policy("file", requirements: "{\"cliAuthCredentialsStore\":\"keyring\",\"allowedLoginMethods\":[\"chatgpt\"]}"));
    Rejected<InvalidDataException>(() => Policy("file", requirements: "{}"));
    Rejected<InvalidDataException>(() => Policy("file", requirements: "{\"cliAuthCredentialsStore\":null,\"allowedLoginMethods\":[\"unknown\"]}"));
});
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks,
    scope = "Production protocol/database assembly and refusal/unknown contracts with synthetic corrupt bytes only; no personal home, installed IPC/client, authentication or native switch acceptance." }));
