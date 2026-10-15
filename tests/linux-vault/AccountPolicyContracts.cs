using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

internal static class AccountPolicyContracts
{
    internal static async Task<string[]> RunAsync(string root)
    {
        var checks = new List<string>();
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Native account policy contract failed."); }
        async Task Check(string name, Func<Task> action) { await action(); checks.Add(name); }
        async Task Denied(Func<Task> action, LinuxAccountPolicyFailure expected)
        {
            try { await action(); }
            catch (LinuxAccountPolicyException error) when (error.Failure == expected) { return; }
            throw new InvalidOperationException("Expected native account policy refusal was not observed.");
        }
        string Home(string name, string config)
        {
            var path = Path.Combine(root, "data", "home-" + name); SecureFiles.CreatePrivateDirectory(path);
            SecureFiles.AtomicWriteText(Path.Combine(path, "config.toml"), config); return path;
        }
        AccountVault Vault(string name) => new(Path.Combine(root, "data", "linux-self-test-account-" + name));
        var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"synthetic-subject\",\"email\":\"demo@example.invalid\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = "synthetic-workspace", id_token = "e30." + claims + ".signature", access_token = "synthetic-access", refresh_token = "synthetic-refresh" } });
        const string LocalEndpoint = "chatgpt_base_url = \"http://127.0.0.1:9\"\n";
        await Check("Actual account initialization refuses non-file stores before vault/runtime mutation", async () =>
        {
            foreach (var store in new[] { "keyring", "auto", "ephemeral" })
            {
                var home = Home(store, "cli_auth_credentials_store = \"" + store + "\"\n" + LocalEndpoint);
                var input = Path.Combine(home, "auth.json"); SecureFiles.AtomicWriteText(input, "not-an-auth-document");
                var vault = Vault(store); var service = new AccountService(vault, home);
                await Denied(() => service.InitializeAsync(CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
                Assert(!File.Exists(vault.FilePath) && !Directory.Exists(Path.Combine(vault.DirectoryPath, "runtime")) && File.ReadAllText(input) == "not-an-auth-document");
                await service.StopAsync();
            }
        });
        await Check("Import/login/refresh/apply/current-account APIs refuse before reading incompatible input or opening a browser", async () =>
        {
            var home = Home("blocked-apis", "cli_auth_credentials_store = \"keyring\"\n" + LocalEndpoint);
            var input = Path.Combine(home, "auth.json"); SecureFiles.AtomicWriteText(input, "not-an-auth-document");
            var vault = Vault("blocked-apis"); var profile = vault.Upsert(auth); vault.Save(); var before = File.ReadAllBytes(vault.FilePath);
            var service = new AccountService(vault, home); var opened = false;
            await Denied(() => service.SynchronizeCurrentAsync(CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            await Denied(() => service.CurrentAccountKeyAsync(CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            await Denied(() => service.RefreshAllAsync(null, CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            await Denied(() => service.AddAccountAsync(_ => opened = true, CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            await Denied(() => service.ApplySwitchAsync(profile.Key, null, false, CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            await service.RefreshAccountAsync(profile.Key, CancellationToken.None);
            Assert(profile.Error != null && !opened && before.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && File.ReadAllText(input) == "not-an-auth-document" && !Directory.Exists(Path.Combine(vault.DirectoryPath, "runtime")));
            await service.StopAsync();
        });
        await Check("Synchronous account entry points cannot bypass native asynchronous policy checks", async () =>
        {
            var service = new AccountService(Vault("sync"), Home("sync", "cli_auth_credentials_store = \"file\"\n"));
            await Denied(() => Task.Run(() => service.SynchronizeCurrent()), LinuxAccountPolicyFailure.AsyncPreflightRequired);
            await Denied(() => Task.Run(() => service.CheckCredentialBackend()), LinuxAccountPolicyFailure.AsyncPreflightRequired);
            await Denied(() => Task.Run(() => service.CompleteRegistration(auth)), LinuxAccountPolicyFailure.AsyncPreflightRequired);
            await service.StopAsync();
        });
        await Check("Explicit-file empty initialization uses the actual native encrypted vault without authentication", async () =>
        {
            var home = Home("empty", "cli_auth_credentials_store = \"file\"\n"); var vault = Vault("empty"); var service = new AccountService(vault, home);
            await service.InitializeAsync(CancellationToken.None);
            var reopened = new AccountVault(vault.DirectoryPath);
            Assert(reopened.Data.Accounts.Count == 0 && File.ReadAllBytes(vault.FilePath).AsSpan(0, 5).SequenceEqual(Encoding.ASCII.GetBytes("CADL1")) && !File.Exists(Path.Combine(home, "auth.json")));
            await service.StopAsync();
        });
        await Check("Ordinary async local import preserves synthetic auth bytes and encrypts the imported identity", async () =>
        {
            var home = Home("import", "cli_auth_credentials_store = \"file\"\n" + LocalEndpoint); var input = Path.Combine(home, "auth.json"); SecureFiles.AtomicWriteText(input, auth);
            var vault = Vault("import"); var service = new AccountService(vault, home);
            await service.InitializeAsync(CancellationToken.None); var imported = await service.SynchronizeCurrentAsync(CancellationToken.None);
            Assert(imported?.Key == AuthDocument.Parse(auth).Key && service.CurrentKey == imported.Key && new AccountVault(vault.DirectoryPath).Data.Accounts.Single().AuthJson == auth && File.ReadAllText(input) == auth);
            await service.StopAsync();
        });
        await Check("Workspace and forced API restrictions prevent local identity import before vault mutation", async () =>
        {
            foreach (var kind in new[] { "workspace", "api" })
            {
                var rule = kind == "workspace" ? "forced_chatgpt_workspace_id = \"different-workspace\"\n" : "forced_login_method = \"api\"\n";
                var home = Home("restricted-" + kind, "cli_auth_credentials_store = \"file\"\n" + rule + LocalEndpoint); SecureFiles.AtomicWriteText(Path.Combine(home, "auth.json"), auth);
                var vault = Vault("restricted-" + kind); var service = new AccountService(vault, home);
                await Denied(() => service.InitializeAsync(CancellationToken.None), kind == "workspace" ? LinuxAccountPolicyFailure.WorkspaceMismatch : LinuxAccountPolicyFailure.CredentialsUnavailable);
                Assert(vault.Data.Accounts.Count == 0 && !File.Exists(vault.FilePath) && File.ReadAllText(Path.Combine(home, "auth.json")) == auth);
                await service.StopAsync();
            }
        });
        await Check("A reused actual client session rereads changed policy rather than caching permission", async () =>
        {
            var home = Home("changed", "cli_auth_credentials_store = \"file\"\n");
            await using var client = await LinuxClientSession.OpenAsync(home, null, null, CancellationToken.None);
            await client.RequireFileAsync(CancellationToken.None);
            SecureFiles.AtomicWriteText(Path.Combine(home, "config.toml"), "cli_auth_credentials_store = \"keyring\"\n");
            await Denied(() => client.RequireFileAsync(CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
        });
        await Check("Actual trusted project policy blocks account initialization despite a file-compatible home", async () =>
        {
            var project = Path.Combine(root, "data", "account-project"); SecureFiles.CreatePrivateDirectory(project);
            SecureFiles.CreatePrivateDirectory(Path.Combine(project, ".git")); SecureFiles.CreatePrivateDirectory(Path.Combine(project, ".codex"));
            SecureFiles.AtomicWriteText(Path.Combine(project, ".codex", "config.toml"), "cli_auth_credentials_store = \"keyring\"\nforced_login_method = \"api\"\n");
            var home = Home("project", "cli_auth_credentials_store = \"file\"\n" + LocalEndpoint + "[projects." + JsonSerializer.Serialize(project) + "]\ntrust_level = \"trusted\"\n");
            var input = Path.Combine(home, "auth.json"); SecureFiles.AtomicWriteText(input, "not-an-auth-document");
            var vault = Vault("project"); var service = new AccountService(vault, home, contextDirectory: project);
            await Denied(() => service.InitializeAsync(CancellationToken.None), LinuxAccountPolicyFailure.CredentialsUnavailable);
            Assert(!File.Exists(vault.FilePath) && vault.Data.Accounts.Count == 0 && File.ReadAllText(input) == "not-an-auth-document");
            await service.StopAsync();
        });
        await Check("Isolated account query children use a retained verified client and their actual file policy", async () =>
        {
            var home = Home("query", "cli_auth_credentials_store = \"file\"\n"); var vault = Vault("query");
            await using var client = await LinuxClientSession.OpenAsync(home, null, null, CancellationToken.None);
            await using var runtime = new RuntimeHome(vault.DirectoryPath, null);
            await using var rpc = await client.OpenIsolatedRpcAsync(runtime.Path, null, CancellationToken.None);
            Assert((await LinuxClientSession.ReadPolicyAsync(rpc, runtime.Path, CancellationToken.None)).FileCredentialsAllowed && !File.Exists(Path.Combine(home, "auth.json")));
        });
        await Check("Desktop metadata cannot bypass the still-unverified Linux lifecycle and peer boundary", async () =>
        {
            var home = Home("desktop-boundary", "cli_auth_credentials_store = \"file\"\n");
            var service = new AccountService(Vault("desktop-boundary"), home);
            await using var sessions = new DesktopSessionMonitor(home);
            await using var backend = new DesktopSwitchBackend(service, sessions);
            try { await backend.BackupNameAsync(new SwitchChat { Id = Guid.NewGuid().ToString(), OriginalTitle = "Synthetic" }, DateTimeOffset.UtcNow, CancellationToken.None); }
            catch (PlatformNotSupportedException) { await service.StopAsync(); return; }
            throw new InvalidOperationException("Unverified desktop metadata action was allowed.");
        });
        return checks.ToArray();
    }
}
