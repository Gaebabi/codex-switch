using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record AccountRegistration(AccountProfile Profile, Task ImmediateRefresh);

internal sealed class AccountService
{
    public AccountVault Vault { get; }
    public string CodexHome { get; }
    public string AuthPath => Path.Combine(CodexHome, "auth.json");
    public CodexInstallation Installation { get; private set; } = new("", "");
    public string? CurrentKey { get; private set; }
    public bool IsBusy => RefreshingCount > 0 || _loginGate.CurrentCount == 0 || _switchGate.CurrentCount == 0;
    public int RefreshingCount => _queries.RunningCount;
    public event Action? Changed;
    private readonly RefreshCoordinator _queries;
    private readonly SemaphoreSlim _querySlots = new(3, 3);
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly LinuxClientSelection? _linuxClient;
    private readonly string _contextDirectory;
    private HashSet<string> _switchTargets = new();
    private TaskCompletionSource<bool>? _switchFinished;
    public AccountService(AccountVault vault, string codexHome, Func<string, CancellationToken, Task>? refreshOperation = null,
        LinuxClientSelection? linuxClient = null, string? contextDirectory = null)
    {
        Vault = vault; CodexHome = Path.GetFullPath(codexHome);
        _linuxClient = linuxClient; _contextDirectory = Path.GetFullPath(contextDirectory ?? codexHome);
        _queries = new RefreshCoordinator(refreshOperation ?? RefreshCoreAsync);
        _queries.Changed += () => Changed?.Invoke();
    }
    public AccountProfile[] Accounts
    {
        get
        {
            lock (Vault.SyncRoot) return Vault.Data.Accounts.Select(p => new AccountProfile
            {
                Key = p.Key, Alias = p.Alias, Email = p.Email, AccountId = p.AccountId, Plan = p.Plan,
                SubscriptionActiveUntil = p.SubscriptionActiveUntil, SubscriptionLastChecked = p.SubscriptionLastChecked,
                IsCurrent = p.Key == CurrentKey, RequiresApply = p.RequiresApply, Usage = p.Usage, Error = p.Error,
                IsRefreshing = _queries.IsRunning(p.Key), History = p.History.Select(t => t.Copy()).ToList()
            }).ToArray();
        }
    }
    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var client = await OpenLinuxAsync(cancellation).ConfigureAwait(false);
        var policy = client == null ? null : await client.RequireFileAsync(cancellation).ConfigureAwait(false);
        var installation = client?.Installation ?? await RuntimeLocator.FindAsync(cancellation).ConfigureAwait(false);
        if (policy != null && CanonicalAuthExists()) LinuxClientSession.RequireFile(policy, AuthDocument.Parse(ReadCanonicalAuth()).AccountId);
        await Task.Run(() => RuntimeHome.CleanupStale(Vault.DirectoryPath), cancellation).ConfigureAwait(false);
        lock (Vault.SyncRoot) { Installation = installation; SynchronizeCurrentCore(policy); Vault.Save(); }
        Changed?.Invoke();
    }
    public AccountProfile? SynchronizeCurrent()
    {
        RequireLegacySync(); return SynchronizeCurrentCore(null);
    }
    public async Task<AccountProfile?> SynchronizeCurrentAsync(CancellationToken cancellation)
    {
        await using var client = await OpenLinuxAsync(cancellation).ConfigureAwait(false);
        var policy = client == null ? null : await client.RequireFileAsync(cancellation).ConfigureAwait(false);
        return SynchronizeCurrentCore(policy);
    }
    private AccountProfile? SynchronizeCurrentCore(ClientCredentialPolicy? policy)
    {
        lock (Vault.SyncRoot)
        {
            if (!CanonicalAuthExists()) { CurrentKey = null; UpdateCurrentFlags(); return null; }
            var json = ReadCanonicalAuth();
            var identity = AuthDocument.Parse(json);
            if (policy != null) LinuxClientSession.RequireFile(policy, identity.AccountId);
            var pending = Vault.Data.Accounts.Find(a => a.Key == identity.Key && a.RequiresApply);
            var profile = pending ?? Vault.Upsert(json);
            if (profile.AuthJson == json) profile.RequiresApply = false;
            CurrentKey = profile.Key; UpdateCurrentFlags(); return profile;
        }
    }
    private void UpdateCurrentFlags() { foreach (var profile in Vault.Data.Accounts) profile.IsCurrent = profile.Key == CurrentKey; }
    public async Task RefreshAllAsync(Action<AccountProfile>? updated, CancellationToken cancellation)
    {
        await using var client = await OpenLinuxAsync(cancellation).ConfigureAwait(false);
        var policy = client == null ? null : await client.RequireFileAsync(cancellation).ConfigureAwait(false);
        lock (Vault.SyncRoot) { SynchronizeCurrentCore(policy); Vault.Save(); }
        var accounts = Accounts.OrderByDescending(a => a.IsCurrent).ToArray();
        await Task.WhenAll(accounts.Select(async account =>
        {
            await RefreshAccountAsync(account.Key, cancellation).ConfigureAwait(false);
            var result = Accounts.FirstOrDefault(a => a.Key == account.Key);
            if (result != null) updated?.Invoke(result);
        })).ConfigureAwait(false);
    }
    public async Task RefreshAccountAsync(string key, CancellationToken cancellation, bool supersede = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        linked.Token.ThrowIfCancellationRequested();
        Task? pendingSwitch;
        lock (Vault.SyncRoot) pendingSwitch = _switchTargets.Contains(key) ? _switchFinished?.Task : null;
        if (pendingSwitch != null) await pendingSwitch.WaitAsync(linked.Token).ConfigureAwait(false);
        await _queries.Schedule(key, linked.Token, supersede).ConfigureAwait(false);
    }
    private async Task RefreshCoreAsync(string key, CancellationToken cancellation)
    {
        await _querySlots.WaitAsync(cancellation).ConfigureAwait(false);
        AccountProfile? profile = null;
        long revision = -1;
        try
        {
            string? workspace;
            lock (Vault.SyncRoot)
            {
                profile = Vault.Data.Accounts.Find(a => a.Key == key); workspace = profile?.AccountId;
                if (OperatingSystem.IsLinux() && profile != null) revision = profile.CredentialRevision;
            }
            if (profile == null) return;
            await using var client = await OpenLinuxAsync(cancellation, workspace).ConfigureAwait(false);
            string authJson;
            lock (Vault.SyncRoot)
            {
                profile = Vault.Data.Accounts.Find(a => a.Key == key);
                if (profile == null) return;
                var canonical = CanonicalAuthExists() ? ReadCanonicalAuth() : null;
                if (canonical != null && !profile.RequiresApply && AuthDocument.Parse(canonical).Key == key) Vault.Upsert(canonical);
                authJson = profile.AuthJson; revision = profile.CredentialRevision;
            }
            await using var home = new RuntimeHome(Vault.DirectoryPath, authJson);
            UsageSnapshot? usage = null;
            string? email = null, plan = null;
            try
            {
                await using var rpc = await OpenAccountRpcAsync(home.Path, client, workspace, cancellation).ConfigureAwait(false);
                var accountResult = await rpc.RequestAsync("account/read", new { refreshToken = false }, cancellation).ConfigureAwait(false);
                if (!accountResult.TryGetProperty("account", out var info) || info.ValueKind != JsonValueKind.Object || JsonValue.String(info, "type") != "chatgpt")
                    throw new InvalidOperationException("로그인이 만료됐습니다. 계정 추가로 다시 로그인해 주세요.");
                var response = await rpc.RequestAsync("account/rateLimits/read", null, cancellation).ConfigureAwait(false);
                usage = UsageSnapshot.Parse(response, profile.AccountId);
                email = JsonValue.String(info, "email"); plan = JsonValue.String(info, "planType");
            }
            finally
            {
                if (File.Exists(Path.Combine(home.Path, "auth.json")))
                {
                    var refreshed = home.ReadAuth();
                    if (AuthDocument.Parse(refreshed).Key != key) throw new InvalidOperationException("조회 중 로그인 계정이 변경됐습니다.");
                    if (client != null) await client.RequireFileAsync(cancellation, workspace).ConfigureAwait(false);
                    lock (Vault.SyncRoot)
                    {
                        if (IsCurrentRevision(profile, revision))
                        {
                            if (refreshed != authJson)
                            {
                                var canonical = CanonicalAuthExists() ? ReadCanonicalAuth() : null;
                                if (!profile.RequiresApply && canonical == authJson) SecureFiles.AtomicWriteText(AuthPath, refreshed);
                                else if (!profile.RequiresApply && canonical != null && AuthDocument.Parse(canonical).Key == key) refreshed = canonical;
                            }
                            if (profile.AuthJson != refreshed) { Vault.Upsert(refreshed); Vault.Save(); }
                            revision = profile.CredentialRevision;
                        }
                    }
                }
            }
            lock (Vault.SyncRoot)
            {
                if (!IsCurrentRevision(profile, revision)) return;
                profile.Usage = usage; profile.Error = null;
                if (usage != null) UsageHistory.Record(profile, usage);
                if (!string.IsNullOrEmpty(email)) profile.Email = email;
                if (plan != null) profile.Plan = plan;
                Vault.Save();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            lock (Vault.SyncRoot) { if (profile != null && IsCurrentRevision(profile, revision)) profile.Error = UserErrors.Describe(error); }
        }
        finally { _querySlots.Release(); Changed?.Invoke(); }
    }
    private bool IsCurrentRevision(AccountProfile profile, long revision) => Vault.Data.Accounts.Contains(profile) && profile.CredentialRevision == revision;
    public async Task<AccountRegistration> AddAccountAsync(Action<Uri> openBrowser, CancellationToken cancellation)
    {
        if (!await _loginGate.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new InvalidOperationException("이미 로그인 창이 열려 있습니다.");
        try
        {
            await using var client = await OpenLinuxAsync(cancellation).ConfigureAwait(false);
            await using var home = new RuntimeHome(Vault.DirectoryPath, null);
            await using (var rpc = await OpenAccountRpcAsync(home.Path, client, null, cancellation).ConfigureAwait(false))
            {
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                string? loginId = null;
                var received = new List<JsonElement>();
                var notificationGate = new object();
                void Finish(JsonElement parameters)
                {
                    if (JsonValue.String(parameters, "loginId") != loginId) return;
                    if (JsonValue.Bool(parameters, "success") == true) completed.TrySetResult(true);
                    else completed.TrySetException(new InvalidOperationException("로그인을 완료하지 못했습니다. 브라우저에서 계정을 확인하고 다시 시도해 주세요."));
                }
                void Observe(string method, JsonElement parameters)
                {
                    if (method != "account/login/completed") return;
                    lock (notificationGate) { if (loginId == null) received.Add(parameters); else Finish(parameters); }
                }
                rpc.Notification += Observe;
                try
                {
                    var start = await rpc.RequestAsync("account/login/start", new { type = "chatgpt" }, cancellation).ConfigureAwait(false);
                    lock (notificationGate)
                    {
                        loginId = JsonValue.String(start, "loginId") ?? throw new InvalidDataException("로그인 요청 식별자가 없습니다.");
                        foreach (var notification in received) Finish(notification);
                    }
                    var uri = new Uri(JsonValue.String(start, "authUrl") ?? throw new InvalidDataException("로그인 주소가 없습니다."));
                    if (uri.Scheme != "https" || (uri.Host != "auth.openai.com" && uri.Host != "chatgpt.com" && uri.Host != "auth.chatgpt.com")) throw new InvalidDataException("공식 OpenAI 로그인 주소가 아닙니다.");
                    openBrowser(uri);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    deadline.CancelAfter(TimeSpan.FromMinutes(10));
                    try { await completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("로그인 시간이 만료됐습니다. 계정 추가를 다시 선택해 주세요."); }
                }
                finally
                {
                    rpc.Notification -= Observe;
                    if (!completed.Task.IsCompletedSuccessfully && loginId != null)
                    {
                        using var cancelDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        try { await rpc.RequestAsync("account/login/cancel", new { loginId }, cancelDeadline.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        catch (IOException) { }
                        catch (CodexRequestException) { }
                    }
                }
            }
            var auth = home.ReadAuth();
            if (client != null) await client.RequireFileAsync(cancellation, AuthDocument.Parse(auth).AccountId).ConfigureAwait(false);
            return CompleteRegistrationCore(auth);
        }
        finally { _loginGate.Release(); Changed?.Invoke(); }
    }
    internal AccountRegistration CompleteRegistration(string authJson)
    {
        RequireLegacySync(); return CompleteRegistrationCore(authJson);
    }
    private AccountRegistration CompleteRegistrationCore(string authJson)
    {
        string key;
        lock (Vault.SyncRoot)
        {
            var profile = Vault.Upsert(authJson);
            var active = CanonicalAuthExists() ? ReadCanonicalAuth() : null;
            profile.RequiresApply = active != null && AuthDocument.Parse(active).Key == profile.Key && active != profile.AuthJson;
            profile.Error = null; key = profile.Key; Vault.Save();
        }
        Changed?.Invoke();
        var refresh = RefreshAccountAsync(key, CancellationToken.None, true);
        return new AccountRegistration(Accounts.Single(a => a.Key == key), refresh);
    }
    public async Task ApplySwitchAsync(string key, Func<bool>? desktopRunning, bool launch, CancellationToken cancellation,
        string? expectedSource = null, bool verifySource = false)
    {
        await _switchGate.WaitAsync(cancellation).ConfigureAwait(false);
        TaskCompletionSource<bool>? finished = null;
        try
        {
            string? workspace;
            lock (Vault.SyncRoot) workspace = Vault.Data.Accounts.Find(a => a.Key == key)?.AccountId;
            await using var client = await OpenLinuxAsync(cancellation, workspace).ConfigureAwait(false);
            var installation = client?.Installation ?? await RuntimeLocator.FindAsync(cancellation).ConfigureAwait(false);
            bool Running() => desktopRunning?.Invoke() ?? RuntimeLocator.IsDesktopRunning(installation.DesktopPath);
            if (Running()) throw new InvalidOperationException("Codex가 아직 실행 중입니다. 앱 메뉴에서 완전히 종료해 주세요.");
            lock (Vault.SyncRoot)
            {
                _switchTargets = new HashSet<string>(new[] { key, CurrentKey ?? "" });
                finished = _switchFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await _queries.CancelAndDrainAsync(_switchTargets).ConfigureAwait(false);
            var policy = client == null ? null : await client.RequireFileAsync(cancellation, workspace).ConfigureAwait(false);
            lock (Vault.SyncRoot)
            {
                Installation = installation;
                var selected = Vault.Data.Accounts.Find(a => a.Key == key) ?? throw new InvalidOperationException("선택한 계정이 삭제됐습니다.");
                if (policy != null) LinuxClientSession.RequireFile(policy, selected.AccountId); else CheckCredentialBackend(selected);
                if (AuthDocument.Parse(selected.AuthJson).Key != key) throw new InvalidDataException("선택한 로그인 정보와 계정이 일치하지 않습니다.");
                var selectedAuth = selected.AuthJson;
                var before = CanonicalAuthExists() ? ReadCanonicalAuth() : null;
                if (verifySource && (before == null ? null : AuthDocument.Parse(before).Key) != expectedSource)
                    throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                if (before != null) { Vault.Data.PreviousDesktopAuthJson = before; if (AuthDocument.Parse(before).Key != key) Vault.Upsert(before); }
                Vault.Save();
                if (Running()) throw new InvalidOperationException("Codex가 다시 실행돼 전환을 중단했습니다.");
                var now = CanonicalAuthExists() ? ReadCanonicalAuth() : null;
                if (before != now) throw new InvalidOperationException("다른 프로그램이 로그인 정보를 갱신해 전환을 중단했습니다. 다시 시도해 주세요.");
                cancellation.ThrowIfCancellationRequested();
                SecureFiles.AtomicWriteText(AuthPath, selectedAuth); selected.RequiresApply = false;
                SynchronizeCurrentCore(policy); Vault.Save();
            }
            if (launch) RuntimeLocator.LaunchDesktop(installation.DesktopPath);
        }
        finally
        {
            lock (Vault.SyncRoot) { _switchTargets.Clear(); _switchFinished = null; }
            finished?.TrySetResult(true); _switchGate.Release(); Changed?.Invoke();
        }
    }
    public void Rename(string key, string alias)
    {
        lock (Vault.SyncRoot) { var profile = Vault.Data.Accounts.Find(a => a.Key == key); if (profile != null) { profile.Alias = alias.Trim(); Vault.Save(); } }
        Changed?.Invoke();
    }
    public async Task RemoveAsync(string key)
    {
        await _queries.CancelAndDrainAsync(new[] { key }).ConfigureAwait(false);
        lock (Vault.SyncRoot)
        {
            if (key == CurrentKey) throw new InvalidOperationException("현재 로그인 계정은 제거할 수 없습니다.");
            Vault.Data.Accounts.RemoveAll(a => a.Key == key); Vault.Save();
        }
        Changed?.Invoke();
    }
    public Task StopAsync() { _shutdown.Cancel(); return _queries.CancelAndDrainAsync(); }
    public void CheckCredentialBackend(AccountProfile? target = null)
    {
        RequireLegacySync();
        var config = Path.Combine(CodexHome, "config.toml");
        if (!File.Exists(config)) return;
        var rootConfig = Regex.Split(File.ReadAllText(config), @"(?m)^\s*\[")[0];
        var match = Regex.Match(rootConfig, "(?m)^\\s*cli_auth_credentials_store\\s*=\\s*['\"]([^'\"]+)['\"]");
        if (match.Success && match.Groups[1].Value != "file") throw new InvalidOperationException("이 버전의 계정 전환은 Codex의 file 로그인 저장 방식에서 사용할 수 있습니다.");
        if (target == null) return;
        var method = Regex.Match(rootConfig, "(?m)^\\s*forced_login_method\\s*=\\s*['\"]([^'\"]+)['\"]");
        if (method.Success && method.Groups[1].Value == "api") throw new InvalidOperationException("현재 Codex 설정은 API 키 로그인을 요구하므로 ChatGPT 계정으로 전환할 수 없습니다.");
        var workspace = Regex.Match(rootConfig, "(?m)^\\s*forced_chatgpt_workspace_id\\s*=\\s*['\"]([^'\"]+)['\"]");
        if (workspace.Success && workspace.Groups[1].Value != target.AccountId) throw new InvalidOperationException("선택한 계정은 현재 Codex 설정에서 요구하는 워크스페이스와 일치하지 않습니다.");
    }
    public async Task CheckCredentialBackendAsync(AccountProfile? target, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) { CheckCredentialBackend(target); return; }
        await using var client = await OpenLinuxAsync(cancellation, target?.AccountId).ConfigureAwait(false);
    }
    internal async Task<string?> CurrentAccountKeyAsync(CancellationToken cancellation)
    {
        await using var client = await OpenLinuxAsync(cancellation).ConfigureAwait(false);
        var policy = client == null ? null : await client.RequireFileAsync(cancellation).ConfigureAwait(false);
        if (!CanonicalAuthExists()) return null;
        var profile = AuthDocument.Parse(ReadCanonicalAuth());
        if (policy != null) LinuxClientSession.RequireFile(policy, profile.AccountId);
        return profile.Key;
    }
    private static void RequireLegacySync()
    {
        if (OperatingSystem.IsLinux()) throw new LinuxAccountPolicyException(LinuxAccountPolicyFailure.AsyncPreflightRequired);
    }
    private bool CanonicalAuthExists() => OperatingSystem.IsLinux() ? LinuxPrivateFiles.Exists(CodexHome, "auth.json") : File.Exists(AuthPath);
    private string ReadCanonicalAuth() => OperatingSystem.IsLinux() ? new UTF8Encoding(false, true).GetString(SecureFiles.ReadPrivateBytes(AuthPath)) : File.ReadAllText(AuthPath);
    private async Task<LinuxClientSession?> OpenLinuxAsync(CancellationToken cancellation, string? workspace = null)
    {
        if (!OperatingSystem.IsLinux()) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var client = await LinuxClientSession.OpenAsync(CodexHome, _contextDirectory, _linuxClient, deadline.Token).ConfigureAwait(false);
        try { await client.RequireFileAsync(deadline.Token, workspace).ConfigureAwait(false); return client; }
        catch { await client.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task<CodexRpc> OpenAccountRpcAsync(string home, LinuxClientSession? client, string? workspace, CancellationToken cancellation)
    {
        if (client != null) return await client.OpenIsolatedRpcAsync(home, workspace, cancellation).ConfigureAwait(false);
        var rpc = new CodexRpc(Installation.CliPath, home);
        try { await rpc.InitializeAsync(cancellation).ConfigureAwait(false); return rpc; }
        catch { await rpc.DisposeAsync().ConfigureAwait(false); throw; }
    }
}

internal static class UserErrors
{
    public static string Describe(Exception error)
    {
        if (error is LinuxAccountPolicyException policy) return Loc.T(policy.Failure switch
        {
            LinuxAccountPolicyFailure.WorkspaceMismatch => "msg.linux_account_workspace_policy",
            LinuxAccountPolicyFailure.AsyncPreflightRequired => "msg.linux_account_async_policy",
            _ => "msg.linux_account_file_policy"
        });
        if (OperatingSystem.IsLinux() && error is UnauthorizedAccessException) return Loc.T("msg.linux_storage_access_denied");
        if (OperatingSystem.IsLinux() && error is System.ComponentModel.Win32Exception) return Loc.T("msg.linux_process_start_failed");
        if (error is UnauthorizedAccessException) return OperatingSystem.IsWindows() ? "로그인 정보나 저장 폴더에 접근할 수 없습니다. Windows 권한을 확인해 주세요." : "로그인 정보나 저장 폴더에 접근할 수 없습니다. macOS 권한을 확인해 주세요.";
        if (error is JsonException or FormatException) return "로그인 정보 또는 Codex 응답의 형식이 올바르지 않습니다. 다시 로그인해 주세요.";
        if (error is System.ComponentModel.Win32Exception) return OperatingSystem.IsWindows() ? "Windows에서 프로그램 실행 또는 암호화 저장소를 사용할 수 없습니다." : "macOS에서 프로그램을 실행할 수 없습니다.";
        if (error is CodexRequestException request)
        {
            if (request.Operation?.StartsWith("thread", StringComparison.Ordinal) == true) return request.Message;
            if (error.Message.Contains("401") || error.Message.Contains("refresh", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("auth", StringComparison.OrdinalIgnoreCase)) return "로그인이 만료되거나 취소됐습니다. 계정 추가로 다시 로그인해 주세요.";
            return "Codex 서버에서 사용량을 가져오지 못했습니다. 연결을 확인하고 새로 고침해 주세요.";
        }
        return error.Message;
    }
}
