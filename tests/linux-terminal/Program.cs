using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

if (!OperatingSystem.IsLinux() || LinuxPrivateFiles.UserId == 0 || Environment.GetEnvironmentVariable("CODEX_SWITCH_DISPOSABLE_RUNNER") != "true" ||
    args.Length != 2 || !Path.GetFileName(args[0]).StartsWith("codex-linux-terminal-test-", StringComparison.Ordinal) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
    throw new InvalidOperationException("Only the owned normal-user headless native terminal sandbox is allowed.");
var root = Path.GetFullPath(args[0]); LinuxPrivateFiles.CreateDirectory(root);
var home = Path.Combine(root, "home"); LinuxPrivateFiles.CreateDirectory(home);
var data = Path.Combine(root, "vault"); var project = Path.Combine(root, "project"); LinuxPrivateFiles.CreateDirectory(project);
SecureFiles.AtomicWriteText(Path.Combine(home, "config.toml"), "cli_auth_credentials_store = \"file\"\nanalytics.enabled = false\n");
var config = File.ReadAllBytes(Path.Combine(home, "config.toml"));
using var client = LinuxClientDiscovery.Open(new(Environment.GetEnvironmentVariable("CODEX_SWITCH_STANDALONE_CLI")!, Environment.GetEnvironmentVariable("CODEX_SWITCH_STANDALONE_CLI_SHA")!), CancellationToken.None);
var selection = new LinuxClientSelection(client.Cli.Path, client.Cli.Sha256);
var vault = new AccountVault(data);
string FictionalAuth(string subject)
{
    var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = subject, email = "demo@example.invalid" }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = "synthetic-workspace", id_token = "e30." + claims + ".signature", access_token = "synthetic-access", refresh_token = "synthetic-refresh" } });
}
var first = vault.Upsert(FictionalAuth("first")); first.Alias = "Demo personal";
var second = vault.Upsert(FictionalAuth("second")); second.Alias = "Demo work";
vault.Data.Theme = "light"; vault.Save();
SecureFiles.AtomicWriteText(Path.Combine(home, "auth.json"), first.AuthJson);
var checks = new List<string>();
void Check(string name, bool passed)
{
    if (!passed) throw new InvalidOperationException("Terminal contract failed: " + name);
    checks.Add(name); Console.Error.WriteLine("Verified disposable terminal contract: " + name);
}
async Task<(int Code, string Output, string Error)> Invoke(params string[] command)
{
    var start = new ProcessStartInfo(Path.GetFullPath(args[1])) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
    foreach (var value in new[] { "cli" }.Concat(command).Concat(new[] { "--data-dir", data, "--codex-home", home, "--client-context", project, "--codex-cli", client.Cli.Path, "--codex-cli-sha256", client.Cli.Sha256 })) start.ArgumentList.Add(value);
    foreach (var variable in new[] { "DISPLAY", "WAYLAND_DISPLAY", "XAUTHORITY" }) start.Environment.Remove(variable);
    using var process = Process.Start(start)!; process.StandardInput.Close();
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
    var result = (process.ExitCode, await output, await error);
    if (result.Item2.Contains("synthetic-access", StringComparison.Ordinal) || result.Item3.Contains("synthetic-access", StringComparison.Ordinal)) throw new InvalidOperationException("Credential output was exposed.");
    if (result.Item1 != 0) Console.Error.WriteLine("Disposable terminal command " + command[0] + " failed: " + result.Item3);
    return result;
}
var originalVault = File.ReadAllBytes(vault.FilePath); var originalAuth = File.ReadAllBytes(Path.Combine(home, "auth.json"));
var listing = await Invoke("list");
using (var parsed = JsonDocument.Parse(listing.Output))
    Check("Published headless command lists two encrypted accounts without token/email output or data writes", listing.Code == 0 && parsed.RootElement.GetProperty("accounts").GetArrayLength() == 2 && !listing.Output.Contains("example.invalid") && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
var imported = await Invoke("import", "--name", "Demo personal");
Check("Actual headless import preserves existing accounts, encrypted format and user preferences", imported.Code == 0 && new AccountVault(data).Data.Accounts.Count == 2 && new AccountVault(data).Data.Theme == "light");
var changed = await Invoke("switch", "Demo work");
var after = new AccountVault(data);
Check("Production headless switch applies the selected fictional login and preserves the exact previous login", changed.Code == 0 && File.ReadAllText(Path.Combine(home, "auth.json")) == second.AuthJson && after.Data.PreviousDesktopAuthJson == first.AuthJson && after.Data.Accounts.Count == 2);
originalVault = File.ReadAllBytes(vault.FilePath); originalAuth = File.ReadAllBytes(Path.Combine(home, "auth.json"));
var noChange = await Invoke("switch", second.Key);
Check("Switching the already-current account does not replace previous-login backup or ciphertext", noChange.Code == 0 && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && new AccountVault(data).Data.PreviousDesktopAuthJson == first.AuthJson);
var unknown = await Invoke("switch", "missing-account");
Check("Unknown account selection fails before login or encrypted-data mutation", unknown.Code != 0 && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
var missingSession = await Invoke("switch", first.Key, "--resume", "00000000-0000-4000-8000-000000000001", "--exec", "--continue");
Check("A nonexistent saved session never changes the selected login or fabricates a new session", missingSession.Code != 0 && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
await using (var running = CodexRpc.InspectLinuxConfiguration(client.Cli, home))
{
    await running.InitializeAsync(CancellationToken.None);
    var conflict = await Invoke("switch", first.Key);
    Check("An actual external native app-server blocks switching without stopping it or altering credentials", conflict.Code != 0 && !Process.GetProcessById(running.OwnedProcessId).HasExited && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
}
string id;
await using (var rpc = CodexRpc.InspectLinuxConfiguration(client.Cli, home))
{
    await rpc.InitializeAsync(CancellationToken.None);
    id = (await rpc.RequestAsync("thread/start", new { cwd = project }, CancellationToken.None)).GetProperty("thread").GetProperty("id").GetString()!;
    await rpc.RequestAsync("thread/name/set", new { threadId = id, name = "Owned offline terminal resume verification" }, CancellationToken.None);
}
var sessions = await Invoke("sessions");
using (var parsed = JsonDocument.Parse(sessions.Output))
    Check("The native CLI session list does not misrepresent an empty app-server metadata thread as saved CLI work", sessions.Code == 0 && parsed.RootElement.GetProperty("sessions").ValueKind == JsonValueKind.Array && !parsed.RootElement.GetProperty("sessions").EnumerateArray().Any(s => s.GetProperty("id").GetString() == id));
var metadataOnly = await Invoke("switch", first.Key, "--resume", id, "--exec", "--continue");
Check("A non-CLI metadata thread is refused before changing the login or encrypted data", metadataOnly.Code != 0 && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
var notTty = await Invoke("switch", first.Key, "--resume", id);
Check("Interactive resume without a TTY fails before changing the login", notTty.Code != 0 && originalVault.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && originalAuth.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "auth.json"))));
var control = LinuxTerminalCommands.CreateResumeStart(client.Cli, home, project, id, true, false);
Check("Resume launch binds the original descriptor, UUID and folder with one explicit follow-up and no permission override", control.FileName == client.Cli.ExecutionPath && control.WorkingDirectory == project && control.ArgumentList.SequenceEqual(new[] { "resume", "--no-daemon", id, "Continue" }) && !control.UseShellExecute && !control.RedirectStandardInput && !control.RedirectStandardOutput);
var batch = LinuxTerminalCommands.CreateResumeStart(client.Cli, home, project, id, true, true);
Check("Noninteractive resume exposes real upstream JSON events without a permission or session-selection override", batch.ArgumentList.SequenceEqual(new[] { "exec", "resume", "--json", id, "Continue" }) && batch.WorkingDirectory == project && batch.Environment["CODEX_HOME"] == home && !batch.UseShellExecute);
foreach (var malformed in new[] { new[] { "switch", first.Key, "--continue" }, new[] { "resume", "--last" }, new[] { "resume", id, "--exec" }, new[] { "list", "--data-dir", data, "--data-dir", root } })
{
    try { LinuxTerminalCommands.Parse(malformed); throw new InvalidOperationException("Unsafe command arguments were accepted."); }
    catch (ArgumentException) { }
}
Check("Ambiguous/implicit session targets and incomplete/duplicate options are refused", true);
after = new AccountVault(data); after.Data.Accounts[0].Alias = "duplicate"; after.Data.Accounts[1].Alias = "duplicate";
try { LinuxTerminalCommands.Select(after.Data.Accounts.ToArray(), "duplicate"); throw new InvalidOperationException("Ambiguous account was selected."); }
catch (ArgumentException) { }
Check("Duplicate account aliases require the explicit stable account ID", true);
Check("Original Codex configuration remains byte-identical", config.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "config.toml")))) ;
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, architecture = RuntimeInformation.ProcessArchitecture.ToString(), sessionId = id,
    scope = "Actual published headless commands, encrypted provider and official native standalone CLI with fictional local credentials; no real authentication, inference or Continue acceptance." }));
