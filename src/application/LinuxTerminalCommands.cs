using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class LinuxTerminalCommands
{
    internal sealed record Options(string Command, string? Target, string Data, string Home, string Context, LinuxClientSelection? Client,
        string? Name, string? Resume, bool Continue, bool Exec, string? Cursor);

    internal static Options Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Choose list, import, switch, sessions or resume. Use cli --help.");
        var command = args[0];
        if (command is not ("list" or "import" or "switch" or "sessions" or "resume")) throw new ArgumentException("Unknown terminal command. Use cli --help.");
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountDesk");
        var home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var context = Environment.CurrentDirectory; string? target = null, cli = null, sha = null, name = null, resume = null, cursor = null;
        var follow = false; var exec = false; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--continue") { if (follow) throw new ArgumentException("Duplicate --continue."); follow = true; continue; }
            if (args[i] == "--exec") { if (exec) throw new ArgumentException("Duplicate --exec."); exec = true; continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            { if (target != null) throw new ArgumentException("Only one account or session target is allowed."); target = args[i]; continue; }
            var option = args[i];
            if (!seen.Add(option)) throw new ArgumentException("Duplicate terminal option.");
            if (++i >= args.Length) throw new ArgumentException("A terminal option needs a value.");
            var value = args[i];
            switch (option)
            {
                case "--data-dir": data = value; break;
                case "--codex-home": home = value; break;
                case "--client-context": context = value; break;
                case "--codex-cli": if (cli != null) throw new ArgumentException("Duplicate CLI path."); cli = value; break;
                case "--codex-cli-sha256": if (sha != null) throw new ArgumentException("Duplicate CLI checksum."); sha = value; break;
                case "--name": if (name != null) throw new ArgumentException("Duplicate account name."); name = value; break;
                case "--resume": if (resume != null) throw new ArgumentException("Duplicate session target."); resume = value; break;
                case "--cursor": cursor = value; break;
                default: throw new ArgumentException("Unknown terminal option.");
            }
        }
        if ((cli == null) != (sha == null)) throw new ArgumentException("Native CLI path and SHA256 must be supplied together.");
        if ((command is "switch" or "resume") != (target != null)) throw new ArgumentException("Only switch/resume require one explicit target.");
        if (command == "resume") { if (resume != null) throw new ArgumentException("Specify the session only once."); resume = target; }
        if (resume != null && (command is not ("switch" or "resume") || !Guid.TryParseExact(resume, "D", out _)))
            throw new ArgumentException("Resume requires an exact saved session UUID.");
        if (follow && resume == null || exec && (!follow || resume == null)) throw new ArgumentException("--continue requires a session; --exec also requires --continue.");
        if (name != null && (command != "import" || name.Length is 0 or > 80 || name.Any(char.IsControl))) throw new ArgumentException("Only import accepts a nonempty account name of at most80 printable characters.");
        if (cursor != null && (command != "sessions" || cursor.Length is 0 or > 4096 || cursor.Any(char.IsControl))) throw new ArgumentException("Only sessions accepts an opaque pagination cursor.");
        return new(command, target, Path.GetFullPath(data), Path.GetFullPath(home), Path.GetFullPath(context), cli == null ? null : new(Path.GetFullPath(cli), sha!), name, resume, follow, exec, cursor);
    }

    internal const string Help = """
        CodexSwitch cli list|import|sessions [options]
        CodexSwitch cli switch <account ID or unique name> [--resume <session UUID>] [--continue] [--exec] [options]
        CodexSwitch cli resume <session UUID> [--continue] [--exec] [options]

        Options: --codex-cli <native executable> --codex-cli-sha256 <SHA256>
                 --codex-home <existing private Codex home> --data-dir <existing vault directory>
                 --client-context <project> --name <alias> (import only) --cursor <nextCursor> (sessions only)
        Authenticate with the official Codex CLI, then import to save that login in the existing encrypted vault.
        No DISPLAY is required. An unlocked supported Secret Service is required; no plaintext fallback.
        Quit existing CLI/IDE/desktop clients normally before changing the shared login.
        Interactive resume requires a terminal. --exec --continue resumes non-interactively.
        --continue explicitly submits one follow-up. No automatic account rotation or permission overrides.
        Short command output is JSON and contains no tokens/emails.
        """;

    public static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && args[0] is "--help" or "help") { output.WriteLine(Help); return 0; }
        try
        {
            if (!OperatingSystem.IsLinux() || LinuxPrivateFiles.UserId == 0) throw new InvalidOperationException("Terminal account commands require a normal Linux user.");
            var options = Parse(args);
            using var instance = new Mutex(true, "CodexAccountDesk-" + SecureFiles.Hash(Environment.UserName + "\n" + options.Data)[..24], out var first);
            if (!first) throw new InvalidOperationException("Close the other Codex Switch instance normally before using the same vault.");
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try { return RunAsync(options, output, error, cancellation.Token).GetAwaiter().GetResult(); }
            finally { Console.CancelKeyPress -= cancel; instance.ReleaseMutex(); }
        }
        catch (OperationCanceledException) { error.WriteLine("Terminal operation cancelled; no automatic retry."); return 130; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or CodexRequestException or System.ComponentModel.Win32Exception)
        { error.WriteLine("Terminal operation failed: " + (failure is CodexRequestException ? "The requested Codex operation failed. Check the saved session, client configuration and login; no automatic retry was made." : UserErrors.Describe(failure))); return 1; }
    }

    private static void Write(TextWriter output, object value) => output.WriteLine(JsonSerializer.Serialize(value));
    private static void RequireQuiescent(LinuxExecutable cli, CancellationToken cancellation)
    {
        if (LinuxDesktopRuntime.Running(cancellation).Length != 0 || LinuxTerminalProcesses.AnyRunning(null, cli.Path, cancellation))
            throw new InvalidOperationException("Quit existing Codex CLI/IDE/desktop clients normally before switching or resuming. No process was stopped.");
    }
    internal static AccountProfile Select(AccountProfile[] accounts, string target)
    {
        var found = accounts.Where(a => a.Key == target || a.Alias != null && a.Alias.Equals(target, StringComparison.Ordinal)).ToArray();
        return found.Length == 1 ? found[0] : throw new ArgumentException("Account ID/name is missing or ambiguous. Use cli list and the full account ID.");
    }
    private static async Task<int> RunAsync(Options options, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        LinuxClientSession.DirectoryPath(options.Home);
        if (options.Resume != null && !options.Exec && !LinuxTerminal.IsInteractive)
            throw new InvalidOperationException("Interactive resume requires a terminal. Use --exec --continue for an explicitly requested noninteractive follow-up.");
        using var installed = LinuxClientDiscovery.Open(options.Client, cancellation);
        var selected = new LinuxClientSelection(installed.Cli.Path, installed.Cli.Sha256);
        using var homeLock = LinuxPrivateFiles.Lock(options.Home, ".codex-switch-terminal.lock");
        var vault = new AccountVault(options.Data);
        var service = new AccountService(vault, options.Home, linuxClient: selected, contextDirectory: options.Context);
        try
        {
            if (options.Command == "list")
            {
                var current = await service.CurrentAccountKeyAsync(cancellation).ConfigureAwait(false);
                Write(output, new { accounts = service.Accounts.Select(a => new { id = a.Key, name = a.Alias ?? "Account " + a.Key[..8], current = a.Key == current, requiresApply = a.RequiresApply }) });
                return 0;
            }
            if (options.Command == "sessions")
            {
                await using var metadata = await LinuxClientSession.OpenAsync(options.Home, options.Context, selected, cancellation).ConfigureAwait(false);
                var result = await metadata.MetadataAsync("thread/list", new { limit = 100, archived = false, sourceKinds = new[] { "cli", "exec" }, cursor = options.Cursor }, cancellation).ConfigureAwait(false);
                Write(output, new { sessions = result.GetProperty("data").EnumerateArray().Select(t => new { id = t.GetProperty("id").GetString(), cwd = t.GetProperty("cwd").GetString() }), nextCursor = JsonValue.String(result, "nextCursor") });
                return 0;
            }
            if (vault.Data.ActivePendingSwitch != null) throw new InvalidOperationException("Complete the existing desktop recovery before terminal account changes or resume.");
            RequireQuiescent(installed.Cli, cancellation);
            if (options.Command == "import")
            {
                if (await service.CurrentAccountKeyAsync(cancellation).ConfigureAwait(false) == null) throw new InvalidOperationException("Sign in with the official Codex CLI before importing.");
                await service.InitializeAsync(cancellation).ConfigureAwait(false);
                var current = service.CurrentKey ?? throw new InvalidOperationException("Sign in with the official Codex CLI before importing.");
                if (options.Name != null) service.Rename(current, options.Name);
                Write(output, new { imported = current, name = service.Accounts.Single(a => a.Key == current).Alias });
                return 0;
            }
            var target = options.Command == "switch" ? Select(service.Accounts, options.Target!) : null;
            var currentKey = await service.CurrentAccountKeyAsync(cancellation).ConfigureAwait(false);
            if (options.Command == "resume" && (currentKey == null || !service.Accounts.Any(a => a.Key == currentKey)))
                throw new InvalidOperationException("Save the current official CLI login with cli import before resuming.");
            string? cwd = null;
            if (options.Resume != null)
            {
                await using var metadata = await LinuxClientSession.OpenAsync(options.Home, options.Context, selected, cancellation).ConfigureAwait(false);
                var thread = (await metadata.MetadataAsync("thread/read", new { threadId = options.Resume, includeTurns = false }, cancellation).ConfigureAwait(false)).GetProperty("thread");
                if (JsonValue.String(thread, "id") != options.Resume) throw new InvalidDataException("Saved session identity does not match the explicit resume target.");
                if (JsonValue.String(thread, "source") is not ("cli" or "exec")) throw new InvalidDataException("Resume requires an actual saved CLI/exec session. Use cli sessions; desktop and metadata-only threads are not terminal sessions.");
                cwd = LinuxClientSession.DirectoryPath(JsonValue.String(thread, "cwd") ?? throw new InvalidDataException("The saved session has no working directory."));
                await service.CheckThreadContextAsync(thread, target?.Key ?? currentKey, cancellation).ConfigureAwait(false);
            }
            if (target != null && (target.Key != currentKey || target.RequiresApply))
            {
                await service.ApplySwitchAsync(target.Key, null, false, cancellation, currentKey, true, requireCliQuiescence: true).ConfigureAwait(false);
                if (await service.CurrentAccountKeyAsync(cancellation).ConfigureAwait(false) != target.Key) throw new InvalidDataException("Applied account identity could not be confirmed.");
            }
            if (options.Resume == null) { Write(output, new { account = target!.Key, applied = true, alreadyCurrent = currentKey == target.Key && !target.RequiresApply }); return 0; }
            if (await service.CurrentAccountKeyAsync(cancellation).ConfigureAwait(false) != (target?.Key ?? currentKey) || currentKey == null && target == null)
                throw new InvalidDataException("A verified ChatGPT login is required to resume the saved session.");
            RequireQuiescent(installed.Cli, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var start = CreateResumeStart(installed.Cli, options.Home, cwd!, options.Resume, options.Continue, options.Exec);
            Write(error, new { accountApplied = target != null, session = options.Resume, resuming = true, followUpRequested = options.Continue, mode = options.Exec ? "exec" : "interactive" });
            using var process = Process.Start(start) ?? throw new IOException("Account applied, but Codex resume did not start. No automatic retry was made.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        finally { await service.StopAsync().ConfigureAwait(false); service.ReleaseDesktop(); }
    }

    internal static ProcessStartInfo CreateResumeStart(LinuxExecutable executable, string home, string cwd, string session, bool followUp, bool exec)
    {
        if (!Guid.TryParseExact(session, "D", out _) || exec && !followUp) throw new ArgumentException("An exact session UUID and explicit exec follow-up are required.");
        var start = new ProcessStartInfo(executable.ExecutionPath) { UseShellExecute = false, WorkingDirectory = cwd };
        if (exec) start.ArgumentList.Add("exec");
        start.ArgumentList.Add("resume");
        start.ArgumentList.Add(exec ? "--json" : "--no-daemon");
        start.ArgumentList.Add(session);
        if (followUp) start.ArgumentList.Add("Continue");
        start.Environment["CODEX_HOME"] = home;
        foreach (var name in new[] { "CODEX_ACCESS_TOKEN", "OPENAI_API_KEY", "OPENAI_IDENTITY_TOKEN_FILE", "OPENAI_IDENTITY_TOKEN_AUDIENCE", "LD_PRELOAD", "LD_LIBRARY_PATH", "LD_AUDIT", "LD_DEBUG", "GLIBC_TUNABLES", "GCONV_PATH" }) start.Environment.Remove(name);
        return start;
    }
}
