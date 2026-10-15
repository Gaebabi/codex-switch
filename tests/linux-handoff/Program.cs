using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

if (!OperatingSystem.IsLinux() || LinuxPrivateFiles.UserId == 0 || Environment.GetEnvironmentVariable("CODEX_SWITCH_DISPOSABLE_RUNNER") != "true" ||
    args.Length != 1 || !Path.GetFileName(args[0]).StartsWith("codex-linux-handoff-test-", StringComparison.Ordinal))
    throw new InvalidOperationException("Only an owned normal-user disposable Linux handoff sandbox is allowed.");
var root = Path.GetFullPath(args[0]); LinuxPrivateFiles.CreateDirectory(root);
var checks = new List<string>();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180)); var token = deadline.Token;
void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Linux handoff contract failed."); }
async Task Check(string name, Func<Task> action) { await action(); checks.Add(name); }
string Home(string name)
{
    var home = Path.Combine(root, name); LinuxPrivateFiles.CreateDirectory(home);
    SecureFiles.AtomicWriteText(Path.Combine(home, "config.toml"), "cli_auth_credentials_store = \"file\"\nanalytics.enabled = false\n");
    return home;
}
string FictionalAuth(string subject)
{
    var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = subject, email = "demo@example.invalid" }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = "synthetic-workspace", id_token = "e30." + claims + ".signature", access_token = "synthetic-access", refresh_token = "synthetic-refresh" } });
}
using var installed = LinuxClientDiscovery.Open(null, token);
var cliHome = Home("cli-only"); var vault = new AccountVault(Path.Combine(root, "vault"));
var first = vault.Upsert(FictionalAuth("first")); var second = vault.Upsert(FictionalAuth("second")); vault.Save();
var service = new AccountService(vault, cliHome, linuxClient: new(installed.Cli.Path, installed.Cli.Sha256));
await service.InitializeAsync(token);
AccountSwitchCoordinator Coordinator(DesktopSwitchBackend backend, AccountVault store) => new(backend, plan => { store.Data.PendingSwitch = plan; store.Save(); }, _ => { }, UserErrors.Describe);
await Check("CLI-only production coordinator applies fictional local credentials without launching a desktop or sending a turn", async () =>
{
    await using var backend = new DesktopSwitchBackend(service, new DesktopSessionMonitor(cliHome));
    var coordinator = Coordinator(backend, vault); var plan = await coordinator.PrepareAsync(first.Key, DateTimeOffset.UtcNow, token);
    Assert(plan.Chats.Count == 0 && !service.HasLinuxDesktop && await coordinator.RunAsync(plan, token));
    Assert(await service.CurrentAccountKeyAsync(token) == first.Key && vault.Data.PendingSwitch == null && File.ReadAllText(service.AuthPath) == first.AuthJson);
});
await Check("Cancelled production handoff preserves the original auth bytes and encrypted checkpoint", async () =>
{
    await using var backend = new DesktopSwitchBackend(service, new DesktopSessionMonitor(cliHome));
    var coordinator = Coordinator(backend, vault); var plan = await coordinator.PrepareAsync(second.Key, DateTimeOffset.UtcNow, token);
    using var stopped = new CancellationTokenSource(); stopped.Cancel();
    try { await coordinator.RunAsync(plan, stopped.Token); throw new InvalidOperationException("Cancellation was ignored."); }
    catch (OperationCanceledException) { }
    Assert(File.ReadAllText(service.AuthPath) == first.AuthJson && new AccountVault(vault.DirectoryPath).Data.PendingSwitch?.Id == plan.Id);
});
await Check("Restarted production coordinator recovers the encrypted handoff and preserves the previous login", async () =>
{
    await service.StopAsync();
    var store = new AccountVault(vault.DirectoryPath); var replacement = new AccountService(store, cliHome, linuxClient: new(installed.Cli.Path, installed.Cli.Sha256));
    await replacement.InitializeAsync(token);
    await using var backend = new DesktopSwitchBackend(replacement, new DesktopSessionMonitor(cliHome));
    Assert(await Coordinator(backend, store).RunAsync(store.Data.PendingSwitch!, token));
    Assert(await replacement.CurrentAccountKeyAsync(token) == second.Key && store.Data.PreviousDesktopAuthJson == first.AuthJson && store.Data.PendingSwitch == null);
    await replacement.StopAsync();
});
var desktopHome = Home("desktop"); var desktopVault = new AccountVault(Path.Combine(root, "desktop-vault"));
var desktopService = new AccountService(desktopVault, desktopHome); await desktopService.InitializeAsync(token);
await using var sessions = new DesktopSessionMonitor(desktopHome);
await using var desktopBackend = new DesktopSwitchBackend(desktopService, sessions);
await Check("Ordinary account service starts the actual pinned sandboxed desktop and its authenticated IPC discovery", async () =>
{
    await desktopService.StartDesktopAsync(token); sessions.Start(); await sessions.WaitForDiscoveryAsync(token);
    Assert(sessions.Current.Available && desktopService.LinuxDesktop!.RequireOwnedOrStopped(token) && (await desktopBackend.CaptureAsync(token)).Length == 0);
});
await Check("Another ordinary service cannot adopt or stop the existing managed desktop", async () =>
{
    using var other = new LinuxDesktopRuntime(desktopHome);
    try { await other.CloseAsync(token); throw new InvalidOperationException("An unowned client was adopted."); }
    catch (LinuxDesktopOwnershipException) { }
    Assert(desktopService.LinuxDesktop!.RequireOwnedOrStopped(token));
});
await Check("Production Linux metadata names and verifies a real locally persisted offline thread", async () =>
{
    await using var rpc = CodexRpc.InspectLinuxConfiguration(installed.Cli, desktopHome); await rpc.InitializeAsync(token);
    var created = await rpc.RequestAsync("thread/start", new { cwd = desktopHome }, token);
    var id = created.GetProperty("thread").GetProperty("id").GetString()!;
    await rpc.RequestAsync("thread/name/set", new { threadId = id, name = "Owned offline metadata verification" }, token);
    var chat = new SwitchChat { Id = id, OriginalTitle = "Owned offline metadata verification" };
    await desktopBackend.BackupNameAsync(chat, DateTimeOffset.UtcNow, token);
    var read = await rpc.RequestAsync("thread/read", new { threadId = id, includeTurns = false }, token);
    Assert(JsonValue.String(read.GetProperty("thread"), "name") == chat.BackupTitle);
    await desktopService.LinuxDesktop!.OpenThreadAsync(id, token);
    Assert(desktopService.LinuxDesktop.RequireOwnedOrStopped(token));
});
await Check("Production backend normally closes its original desktop before returning and relaunches in the same home", async () =>
{
    await desktopBackend.CloseDesktopAsync(token); Assert(LinuxDesktopRuntime.Running(token).Length == 0);
    await desktopBackend.LaunchDesktopAsync(token); Assert(desktopService.LinuxDesktop!.RequireOwnedOrStopped(token) && sessions.Current.Available);
    await desktopBackend.CloseDesktopAsync(token); Assert(LinuxDesktopRuntime.Running(token).Length == 0 && !File.Exists(Path.Combine(desktopHome, "auth.json")));
});
await desktopService.StopAsync(); desktopService.ReleaseDesktop();
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    scope = "Production integration with fictional local auth and actual offline native CLI/desktop/metadata. No authenticated accounts, inference, active-chat continuation or full desktop support acceptance." }));
