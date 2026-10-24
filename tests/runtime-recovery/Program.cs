using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

// The same disposable executable serves JSON-RPC without network or real credentials.
if (args.FirstOrDefault() == "app-server")
{
    while (Console.ReadLine() is { } line)
    {
        using var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.TryGetProperty("id", out var id)) continue;
        var method = doc.RootElement.GetProperty("method").GetString();
        object result = method switch
        {
            "account/read" => new { account = new { type = "chatgpt", email = "fixture@example.test", planType = "team" } },
            "account/rateLimits/read" => new { rateLimits = new { primary = new { usedPercent = 12, windowDurationMins = 300, resetsAt = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds() } } },
            _ => new { }
        };
        Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt32(), result }));
    }
    return;
}

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var root = Path.Combine(Path.GetTempPath(), "switch-runtime-recovery-" + Guid.NewGuid().ToString("N"));
var binaries = Path.Combine(AppContext.BaseDirectory, "runtime-recovery-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var originalCli = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI");
var originalDesktop = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP");
var assembly = Assembly.Load("CodexSwitch.Application");
var serviceType = assembly.GetType("CodexAccountDesk.AccountService", true)!;
var vaultType = assembly.GetType("CodexAccountDesk.AccountVault", true)!;
object? service = null;
var checks = new List<string>();
void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks.Add(message); }
Task Call(string name, params object?[] parameters) => (Task)serviceType.GetMethod(name)!.Invoke(service, parameters)!;
object Account() => ((Array)serviceType.GetProperty("Accounts")!.GetValue(service)!).GetValue(0)!;
object? Value(object account, string name) => account.GetType().GetProperty(name)!.GetValue(account);
string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
try
{
    var currentExecutable = Environment.ProcessPath!;
    var oldDirectory = Path.Combine(binaries, "old");
    var newDirectory = Path.Combine(binaries, "new");
    foreach (var directory in new[] { oldDirectory, newDirectory })
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
    }
    var oldCli = Path.Combine(oldDirectory, Path.GetFileName(currentExecutable));
    var newCli = Path.Combine(newDirectory, Path.GetFileName(currentExecutable));
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI", oldCli);
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", currentExecutable);
    var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
    var token = Encode("{\"alg\":\"none\"}") + "." + Encode("{\"sub\":\"fixture\",\"email\":\"fixture@example.test\",\"exp\":2050000000,\"https://api.openai.com/auth\":{\"chatgpt_plan_type\":\"team\"}}") + ".test";
    var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { id_token = token, access_token = token, refresh_token = "fictional", account_id = "fixture-workspace" } });
    var authPath = Path.Combine(home, "auth.json"); File.WriteAllText(authPath, auth);
    var vault = Activator.CreateInstance(vaultType, Path.Combine(root, "vault"))!;
    service = Activator.CreateInstance(serviceType, new object?[] { vault, home, null, null, null })!;
    await Call("InitializeAsync", CancellationToken.None);
    if (args.FirstOrDefault() == "--evidence")
    {
        await WindowEvidence.RunAsync(service, root, oldCli, newCli, Path.GetFullPath(args[1]), args.Contains("--baseline"));
        return;
    }
    await Call("RefreshAllAsync", null, CancellationToken.None);
    Assert(Value(Account(), "Error") == null && Value(Account(), "Usage") != null, "Initial isolated usage read succeeds");
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", Path.Combine(root, "unavailable-discovery.exe"));
    await Call("RefreshAllAsync", null, CancellationToken.None);
    Assert(Value(Account(), "Error") == null, "Valid cached executables remain usable without repeated installation discovery");
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", currentExecutable);
    var usage = Value(Account(), "Usage");
    File.Delete(oldCli);
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI", newCli);
    await Call("RefreshAllAsync", null, CancellationToken.None);
    Assert(Value(Account(), "Error") == null, "Same service recovers after the old executable is removed without restart");
    Assert(!ReferenceEquals(usage, Value(Account(), "Usage")), "Recovery produces a new usage observation rather than retaining cached success");
    var installation = serviceType.GetProperty("Installation")!.GetValue(service)!;
    Assert((string)installation.GetType().GetProperty("CliPath")!.GetValue(installation)! == newCli, "Recovered service uses the new executable");
    var replacementBytes = File.ReadAllBytes(newCli);
    File.Delete(newCli);
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", Path.Combine(root, "temporarily-unavailable.exe"));
    var beforeFailure = Value(Account(), "Usage");
    await Call("RefreshAllAsync", null, CancellationToken.None);
    Assert(Value(Account(), "Error") != null && ReferenceEquals(beforeFailure, Value(Account(), "Usage")), "Temporary update gap remains an explicit error with previous usage preserved");
    File.WriteAllBytes(newCli, replacementBytes);
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", currentExecutable);
    await Call("RefreshAllAsync", null, CancellationToken.None);
    Assert(Value(Account(), "Error") == null, "Next refresh recovers after installation becomes available again");
    Assert(File.ReadAllText(authPath) == auth, "Canonical fixture credentials remain unchanged");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var cancellationObserved = false;
    try { await Call("RefreshAllAsync", null, cancelled.Token); } catch (OperationCanceledException) { cancellationObserved = true; }
    Assert(cancellationObserved, "Cancellation is preserved");
    Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "One live AccountService across disposable CLI removal/replacement and update gap; no personal accounts, network, desktop restart or account switch." }));
}
finally
{
    if (service != null) await Call("StopAsync");
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI", originalCli);
    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", originalDesktop);
    if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) ||
        !Path.GetFileName(root).StartsWith("switch-runtime-recovery-", StringComparison.Ordinal)) throw new IOException("Unexpected cleanup path");
    Directory.Delete(root, true);
    if (Path.GetDirectoryName(Path.GetFullPath(binaries)) != AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) ||
        !Path.GetFileName(binaries).StartsWith("runtime-recovery-", StringComparison.Ordinal)) throw new IOException("Unexpected binary cleanup path");
    if (Directory.Exists(binaries)) Directory.Delete(binaries, true);
}
