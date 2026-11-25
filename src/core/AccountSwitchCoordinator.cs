using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal enum SwitchPhase { BackingUp, Closing, Applying, Launching, Resuming, Finished, CreditPaused }
internal enum ChatResumeState { Pending, Sending, Resumed, AlreadyRunning, Completed, Unconfirmed, Failed }
internal enum CodexModelProfile { Native, Switch }
internal enum SwitchStage { Discovering, BackingUp, Closing, Applying, ModelProfile, Launching, Connecting, Resuming, Reopening, Retrying, AccountWindow }

internal sealed record SwitchProgress(SwitchStage Stage, TimeSpan Budget, int Completed = 0, int Total = 0)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    public int SecondsLeft => Math.Max(0, (int)Math.Ceiling((Budget - Stopwatch.GetElapsedTime(_started)).TotalSeconds));
    public bool CanCancel => Stage is SwitchStage.Discovering or SwitchStage.BackingUp or SwitchStage.Connecting or SwitchStage.Resuming or SwitchStage.Retrying or SwitchStage.AccountWindow;
    public string Label => Stage switch
    {
        SwitchStage.Discovering => Loc.T("switch.discovering"),
        SwitchStage.AccountWindow => Loc.T("switch.find_window"),
        SwitchStage.BackingUp => Loc.F("채팅 이름 백업 {0}/{1}", Completed, Total),
        SwitchStage.Closing => Loc.T("Codex 정상 종료 중…"),
        SwitchStage.Applying => Loc.T("계정 적용 중…"),
        SwitchStage.ModelProfile => Loc.T("switch.model_profile"),
        SwitchStage.Launching or SwitchStage.Reopening => Loc.T("Codex 여는 중…"),
        SwitchStage.Connecting => Loc.T("switch.connecting"),
        SwitchStage.Resuming => Loc.F("채팅 재개 {0}/{1}", Completed, Total),
        SwitchStage.Retrying => Loc.F("switch.retrying", Completed, Total),
        _ => throw new ArgumentOutOfRangeException(nameof(Stage))
    };
}

internal sealed class SwitchDeadlineException(SwitchProgress progress, Exception cause) :
    TimeoutException(Loc.F("switch.timeout", progress.Label, (int)Math.Ceiling(progress.Budget.TotalSeconds)), cause)
{
    public SwitchStage Stage => progress.Stage;
}

internal sealed class SwitchChat
{
    public string Id { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string BackupTitle { get; set; } = "";
    public string? OriginalTurnId { get; set; }
    public string? OriginalGoal { get; set; }
    public bool NameSaved { get; set; }
    public string ContinueMessageId { get; set; } = Guid.NewGuid().ToString();
    public ChatResumeState ResumeState { get; set; }
    public string? Error { get; set; }
}

internal sealed class AccountSwitchPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? SourceAccountKey { get; set; }
    public string TargetAccountKey { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public SwitchPhase Phase { get; set; }
    public bool AccountApplied { get; set; }
    public bool RestartOnly { get; set; }
    public CodexModelProfile? ModelProfile { get; set; }
    public bool ModelProfileApplied { get; set; }
    public bool Automatic { get; set; }
    public int AutomaticRecoveryAttempts { get; set; }
    public bool DesktopRestartRequired { get; set; }
    public bool CreditProtectionPause { get; set; }
    public bool PreventDepletedSourceRelaunch { get; set; }
    public List<SwitchChat> Chats { get; set; } = new();
    public string? Error { get; set; }
}

internal static class SwitchRecovery
{
    public static bool CanReplaceEmptyRestart(AccountSwitchPlan? plan, string? current) =>
        current != null && plan is { RestartOnly: true, Automatic: false, AccountApplied: false,
            CreditProtectionPause: false, PreventDepletedSourceRelaunch: false, ModelProfile: null, ModelProfileApplied: false,
            Phase: SwitchPhase.BackingUp or SwitchPhase.Closing, Chats.Count: 0 } &&
        plan.SourceAccountKey == current && plan.TargetAccountKey == current;

    public static bool AllowsAccount(AccountSwitchPlan? plan, string target, string? current, bool manualSwitch) =>
        plan == null || plan.CreditProtectionPause || plan.TargetAccountKey == target ||
        manualSwitch && CanReplaceEmptyRestart(plan, current);

    public static AccountSwitchPlan? Active(AccountSwitchPlan? plan) =>
        plan is { Phase: SwitchPhase.Finished, AccountApplied: true, DesktopRestartRequired: false, CreditProtectionPause: false } &&
        (plan.ModelProfile == null || plan.ModelProfileApplied) &&
        plan.Chats != null && plan.Chats.All(chat => chat != null && chat.ResumeState is ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed)
            ? null : plan;
}

internal static class SwitchTitle
{
    public static string Stamp(string title, DateTimeOffset requestedAt)
    {
        var name = Regex.Replace(title, @"^\s*(?:\[\d{6}\]\s*)+", "", RegexOptions.CultureInvariant).TrimStart();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("채팅 이름을 확인하지 못했습니다.");
        return "[" + requestedAt.ToOffset(TimeSpan.FromHours(9)).ToString("yyMMdd", CultureInfo.InvariantCulture) + "] " + name;
    }
}

internal interface IAccountSwitchBackend : IAsyncDisposable
{
    Task<string?> CurrentAccountAsync(CancellationToken cancellation);
    Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation);
    Task ValidateChatsForTargetAsync(SwitchChat[] chats, string target, CancellationToken cancellation);
    Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation);
    Task CloseDesktopAsync(CancellationToken cancellation);
    Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation);
    Task LaunchDesktopAsync(CancellationToken cancellation);
    Task WaitForDesktopConnectionAsync(CancellationToken cancellation) => Task.CompletedTask;
    Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation);
}

internal static class ActiveChatTitleUpdate
{
    public static async Task<int> RunAsync(IAccountSwitchBackend backend, DateTimeOffset at, Action<int> progress, CancellationToken cancellation)
    {
        var chats = await backend.CaptureAsync(cancellation);
        var saved = 0;
        foreach (var chat in chats)
        {
            await backend.BackupNameAsync(chat, at, cancellation);
            progress(++saved);
        }
        return saved;
    }
}

internal sealed class AccountSwitchCoordinator
{
    public const int MaximumRecoveryAttempts = 2;
    private readonly IAccountSwitchBackend _backend;
    private readonly Action<AccountSwitchPlan?> _save;
    private readonly Action<AccountSwitchPlan> _changed;
    private readonly Func<Exception, string> _describeError;
    private readonly Func<CodexModelProfile, CancellationToken, Task>? _applyModelProfile;
    private readonly Func<CodexModelProfile, CancellationToken, Task>? _validateModelProfile;
    private readonly Action<SwitchProgress>? _progress;
    private readonly Func<SwitchStage, TimeSpan> _budget;
    public AccountSwitchCoordinator(IAccountSwitchBackend backend, Action<AccountSwitchPlan?> save, Action<AccountSwitchPlan> changed, Func<Exception, string> describeError,
        Func<CodexModelProfile, CancellationToken, Task>? applyModelProfile = null,
        Func<CodexModelProfile, CancellationToken, Task>? validateModelProfile = null, Action<SwitchProgress>? progress = null,
        Func<SwitchStage, TimeSpan>? budget = null)
    {
        _backend = backend; _save = save; _changed = changed; _describeError = describeError;
        _applyModelProfile = applyModelProfile; _validateModelProfile = validateModelProfile;
        _progress = progress; _budget = budget ?? DefaultBudget;
    }
    public async Task<AccountSwitchPlan> PrepareAsync(string target, DateTimeOffset at, CancellationToken cancellation, bool creditPause = false,
        string? expectedSource = null)
    {
        var (source, chats) = await InStageAsync(SwitchStage.Discovering, async token =>
        {
            var account = await _backend.CurrentAccountAsync(token);
            if (creditPause && account != target || expectedSource != null && account != expectedSource)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            var captured = await _backend.CaptureAsync(token);
            if (expectedSource != null && await _backend.CurrentAccountAsync(token) != expectedSource)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            return (account, captured);
        }, cancellation);
        cancellation.ThrowIfCancellationRequested();
        foreach (var chat in chats) chat.BackupTitle = SwitchTitle.Stamp(chat.OriginalTitle, at);
        var plan = new AccountSwitchPlan { SourceAccountKey = source, TargetAccountKey = target, RequestedAt = at, Chats = chats.ToList(), CreditProtectionPause = creditPause, PreventDepletedSourceRelaunch = creditPause };
        Checkpoint(plan, SwitchPhase.BackingUp); return plan;
    }
    public async Task<AccountSwitchPlan> PrepareRestartAsync(string account, DateTimeOffset at, CancellationToken cancellation, CodexModelProfile? modelProfile = null)
    {
        var chats = await InStageAsync(SwitchStage.Discovering, async token =>
        {
            if (await _backend.CurrentAccountAsync(token) != account) throw new InvalidOperationException(Loc.T("restart.login_changed"));
            var captured = await _backend.CaptureAsync(token);
            if (await _backend.CurrentAccountAsync(token) != account) throw new InvalidOperationException(Loc.T("restart.login_changed"));
            return captured;
        }, cancellation);
        cancellation.ThrowIfCancellationRequested();
        foreach (var chat in chats) { chat.BackupTitle = chat.OriginalTitle; chat.NameSaved = true; }
        var plan = new AccountSwitchPlan { SourceAccountKey = account, TargetAccountKey = account, RequestedAt = at, Chats = chats.ToList(), RestartOnly = true, ModelProfile = modelProfile };
        Checkpoint(plan, SwitchPhase.BackingUp); return plan;
    }
    public async Task RetargetCreditPauseAsync(AccountSwitchPlan plan, string target, CancellationToken cancellation)
    {
        if (!plan.CreditProtectionPause || plan.SourceAccountKey == null || plan.TargetAccountKey != plan.SourceAccountKey)
            throw new InvalidOperationException("남은 채팅 재개 작업을 먼저 처리해 주세요.");
        var chats = await InStageAsync(SwitchStage.Discovering, async token =>
        {
            if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            var captured = ProposedChats(plan, await _backend.CaptureAsync(token));
            await _backend.ValidateChatsForTargetAsync(captured, target, token);
            if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            return captured;
        }, cancellation);
        cancellation.ThrowIfCancellationRequested();
        plan.Chats = chats.ToList();
        plan.CreditProtectionPause = false; plan.TargetAccountKey = target; plan.AccountApplied = target == plan.SourceAccountKey;
        plan.Error = null; plan.AutomaticRecoveryAttempts = 0;
        Checkpoint(plan, plan.AccountApplied ? SwitchPhase.Launching : SwitchPhase.BackingUp);
    }
    public async Task RearmCreditPauseAsync(AccountSwitchPlan plan, CancellationToken cancellation)
    {
        if (!plan.CreditProtectionPause || plan.Phase != SwitchPhase.CreditPaused || plan.SourceAccountKey == null || plan.SourceAccountKey != plan.TargetAccountKey)
            throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
        var chats = await InStageAsync(SwitchStage.Discovering, async token =>
        {
            if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            var captured = ProposedChats(plan, await _backend.CaptureAsync(token));
            if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            return captured;
        }, cancellation);
        cancellation.ThrowIfCancellationRequested();
        plan.Chats = chats.ToList();
        plan.AccountApplied = false; plan.AutomaticRecoveryAttempts = 0; plan.Error = null;
        Checkpoint(plan, SwitchPhase.BackingUp);
    }
    private static SwitchChat[] ProposedChats(AccountSwitchPlan plan, SwitchChat[] captured)
    {
        var result = plan.Chats.ToList();
        var seen = new HashSet<string>(result.Select(chat => chat.Id), StringComparer.Ordinal);
        var capturedIds = new HashSet<string>(StringComparer.Ordinal);
        if (seen.Count != result.Count) throw new InvalidDataException(Loc.T("credits.invalid_chats"));
        foreach (var chat in captured)
        {
            if (string.IsNullOrWhiteSpace(chat.Id) || !capturedIds.Add(chat.Id)) throw new InvalidDataException(Loc.T("credits.invalid_chats"));
            if (seen.Add(chat.Id)) { chat.BackupTitle = SwitchTitle.Stamp(chat.OriginalTitle, plan.RequestedAt); result.Add(chat); }
        }
        return result.ToArray();
    }
    public async Task<bool> RunAsync(AccountSwitchPlan plan, CancellationToken cancellation)
    {
        plan.AutomaticRecoveryAttempts = Math.Clamp(plan.AutomaticRecoveryAttempts, 0, MaximumRecoveryAttempts);
        var shutdownConfirmed = plan.AccountApplied || plan.Phase is SwitchPhase.Applying or SwitchPhase.Launching or SwitchPhase.Resuming;
        var launchAttempted = false;
        var closedThisRun = false;
        try
        {
            if (plan.ModelProfile != null && (!plan.RestartOnly || plan.CreditProtectionPause || _applyModelProfile == null || !Enum.IsDefined(plan.ModelProfile.Value)))
                throw new InvalidOperationException("The saved Codex model profile restart cannot be applied by this client.");
            var current = await InStageAsync(SwitchStage.Discovering, _backend.CurrentAccountAsync, cancellation);
            if (plan.RestartOnly && (plan.SourceAccountKey == null || plan.SourceAccountKey != plan.TargetAccountKey || current != plan.SourceAccountKey))
                throw new InvalidOperationException(Loc.T("restart.login_changed"));
            if (plan.ModelProfile is { } requestedProfile && !plan.ModelProfileApplied && _validateModelProfile != null)
                await InStageAsync(SwitchStage.ModelProfile, token => _validateModelProfile(requestedProfile, token), cancellation);
            if (plan.CreditProtectionPause && (plan.SourceAccountKey == null || plan.SourceAccountKey != plan.TargetAccountKey || current != plan.SourceAccountKey))
                throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
            if (plan.CreditProtectionPause && plan.Phase == SwitchPhase.CreditPaused) return false;
            if (!plan.AccountApplied && current == plan.TargetAccountKey && plan.Phase == SwitchPhase.Applying)
            { plan.AccountApplied = true; Checkpoint(plan, SwitchPhase.Launching); }
            if (!plan.AccountApplied)
            {
                if (current != plan.SourceAccountKey) throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                Checkpoint(plan, SwitchPhase.BackingUp);
                var saved = plan.Chats.Count(chat => chat.NameSaved);
                foreach (var chat in plan.Chats.Where(chat => !chat.NameSaved))
                {
                    await RetryStepAsync(plan, SwitchStage.BackingUp, async token =>
                    {
                        if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                            throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                        await _backend.BackupNameAsync(chat, plan.RequestedAt, token);
                    }, cancellation, saved, plan.Chats.Count);
                    chat.NameSaved = true; saved++; Checkpoint(plan, SwitchPhase.BackingUp);
                }
                cancellation.ThrowIfCancellationRequested();
                plan.DesktopRestartRequired = true;
                Checkpoint(plan, SwitchPhase.Closing);
                await RetryStepAsync(plan, SwitchStage.Closing, async token =>
                {
                    if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                        throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                    await _backend.CloseDesktopAsync(token);
                }, cancellation);
                shutdownConfirmed = true;
                closedThisRun = true;
                cancellation.ThrowIfCancellationRequested();
                if (plan.CreditProtectionPause)
                {
                    if (await InStageAsync(SwitchStage.Discovering, _backend.CurrentAccountAsync, cancellation) != plan.SourceAccountKey)
                        throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                    plan.AccountApplied = true;
                    Checkpoint(plan, SwitchPhase.CreditPaused);
                    return false;
                }
                if (!plan.RestartOnly)
                {
                    Checkpoint(plan, SwitchPhase.Applying);
                    await RetryStepAsync(plan, SwitchStage.Applying, async token =>
                    {
                        var account = await _backend.CurrentAccountAsync(token);
                        if (account == plan.TargetAccountKey) return;
                        if (account != plan.SourceAccountKey)
                            throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                        await _backend.ApplyAccountAsync(plan.TargetAccountKey, plan.SourceAccountKey, token);
                    }, cancellation);
                }
                plan.AccountApplied = true; Checkpoint(plan, SwitchPhase.Launching);
            }
            if (plan.ModelProfile is { } modelProfile && !plan.ModelProfileApplied)
            {
                if (!closedThisRun)
                {
                    plan.DesktopRestartRequired = true; Checkpoint(plan, SwitchPhase.Closing);
                    await InStageAsync(SwitchStage.Closing, async token =>
                    {
                        if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                            throw new InvalidOperationException(Loc.T("restart.login_changed"));
                        await _backend.CloseDesktopAsync(token);
                    }, cancellation);
                    shutdownConfirmed = true;
                }
                cancellation.ThrowIfCancellationRequested();
                Checkpoint(plan, SwitchPhase.Applying);
                await InStageAsync(SwitchStage.ModelProfile, async token =>
                {
                    if (await _backend.CurrentAccountAsync(token) != plan.SourceAccountKey)
                        throw new InvalidOperationException(Loc.T("restart.login_changed"));
                    await _applyModelProfile!(modelProfile, token);
                }, cancellation);
                plan.ModelProfileApplied = true; Checkpoint(plan, SwitchPhase.Launching);
            }
            await RetryStepAsync(plan, SwitchStage.Launching, async token =>
            {
                if (await _backend.CurrentAccountAsync(token) != plan.TargetAccountKey)
                    throw new InvalidOperationException("선택한 계정과 현재 로그인이 달라 채팅 재개를 중단했습니다.");
                plan.DesktopRestartRequired = true;
                Checkpoint(plan, SwitchPhase.Launching); launchAttempted = true;
                await _backend.LaunchDesktopAsync(token);
            }, cancellation);
            plan.DesktopRestartRequired = false;
            Checkpoint(plan, SwitchPhase.Resuming);
            var remaining = plan.Chats.Where(chat => !IsResumed(chat)).ToList();
            while (remaining.Count != 0)
            {
                await RetryStepAsync(plan, SwitchStage.Connecting, _backend.WaitForDesktopConnectionAsync, cancellation);
                var retry = new List<SwitchChat>();
                foreach (var chat in remaining)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var verifiedAccount = false;
                    try
                    {
                        chat.Error = null;
                        chat.ResumeState = await InStageAsync(SwitchStage.Resuming, async token =>
                        {
                            if (await _backend.CurrentAccountAsync(token) != plan.TargetAccountKey)
                                throw new InvalidOperationException("로그인 계정이 변경돼 채팅 재개를 중단했습니다.");
                            verifiedAccount = true;
                            return await _backend.ResumeAsync(chat, () =>
                            {
                                token.ThrowIfCancellationRequested();
                                chat.ResumeState = ChatResumeState.Sending; Checkpoint(plan, SwitchPhase.Resuming);
                            }, token);
                        }, cancellation, plan.Chats.Count(IsResumed), plan.Chats.Count);
                        if (chat.ResumeState is ChatResumeState.Pending or ChatResumeState.Sending or ChatResumeState.Unconfirmed) retry.Add(chat);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                    catch (Exception error) when (verifiedAccount)
                    {
                        chat.ResumeState = chat.ResumeState is ChatResumeState.Sending or ChatResumeState.Unconfirmed ? ChatResumeState.Unconfirmed : ChatResumeState.Failed;
                        chat.Error = _describeError(error);
                        if (IsRetryable(error)) retry.Add(chat);
                    }
                    Checkpoint(plan, SwitchPhase.Resuming);
                }
                if (retry.Count == 0 || plan.AutomaticRecoveryAttempts >= MaximumRecoveryAttempts) break;
                await ReserveRetryAsync(plan, cancellation);
                remaining = retry;
            }
            if (!plan.Chats.All(IsResumed)) return false;
            Checkpoint(plan, SwitchPhase.Finished); _save(null); return true;
        }
        catch (Exception error)
        {
            plan.Error = error is OperationCanceledException ? "전환 작업을 취소했습니다." : _describeError(error);
            if (error is OperationCanceledException && cancellation.IsCancellationRequested && plan.Automatic)
                plan.AutomaticRecoveryAttempts = Math.Max(MaximumRecoveryAttempts, plan.AutomaticRecoveryAttempts);
            // Reopen after cancellation or a failed apply, but do not add a fourth launch after
            // exhausting the retries. Never overwrite credentials or repeat Continue here.
            if (!plan.CreditProtectionPause && plan.DesktopRestartRequired && shutdownConfirmed && !launchAttempted)
            {
                try
                {
                    await InStageAsync(SwitchStage.Reopening, async token =>
                    {
                        var current = await _backend.CurrentAccountAsync(token);
                        if ((current == plan.SourceAccountKey || current == plan.TargetAccountKey) &&
                            (!plan.PreventDepletedSourceRelaunch || current == plan.TargetAccountKey))
                        {
                            await _backend.LaunchDesktopAsync(token);
                            plan.DesktopRestartRequired = false;
                        }
                    }, CancellationToken.None);
                }
                catch (Exception restartError) { plan.Error += "\n" + Loc.F("switch.restart_failed", _describeError(restartError)); }
            }
            _save(plan); _changed(plan); throw;
        }
    }
    private async Task RetryStepAsync(AccountSwitchPlan plan, SwitchStage stage, Func<CancellationToken, Task> action,
        CancellationToken cancellation, int completed = 0, int total = 0)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { await InStageAsync(stage, action, cancellation, completed, total); return; }
            catch (Exception error) when (!cancellation.IsCancellationRequested && IsRetryable(error) && plan.AutomaticRecoveryAttempts < MaximumRecoveryAttempts)
            { plan.Error = _describeError(error); }
            await ReserveRetryAsync(plan, cancellation);
        }
    }
    private Task ReserveRetryAsync(AccountSwitchPlan plan, CancellationToken cancellation)
    {
        plan.AutomaticRecoveryAttempts++;
        _save(plan); _changed(plan);
        var delay = TimeSpan.FromSeconds(1);
        _progress?.Invoke(new(SwitchStage.Retrying, delay, plan.AutomaticRecoveryAttempts, MaximumRecoveryAttempts));
        return Task.Delay(delay, cancellation);
    }
    private static bool IsResumed(SwitchChat chat) =>
        chat.ResumeState is ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed;

    private Task InStageAsync(SwitchStage stage, Func<CancellationToken, Task> action, CancellationToken cancellation, int completed = 0, int total = 0) =>
        InStageAsync(stage, async token => { await action(token); return true; }, cancellation, completed, total);

    private async Task<T> InStageAsync<T>(SwitchStage stage, Func<CancellationToken, Task<T>> action, CancellationToken cancellation, int completed = 0, int total = 0)
    {
        cancellation.ThrowIfCancellationRequested();
        var progress = new SwitchProgress(stage, _budget(stage), completed, total);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(progress.Budget);
        _progress?.Invoke(progress);
        try { return await action(deadline.Token); }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !cancellation.IsCancellationRequested)
        { throw new SwitchDeadlineException(progress, error); }
    }

    private static TimeSpan DefaultBudget(SwitchStage stage) => TimeSpan.FromSeconds(stage switch
    {
        SwitchStage.Discovering => 45,
        SwitchStage.Closing => 45,
        SwitchStage.Launching => 40,
        SwitchStage.Connecting => 30,
        SwitchStage.Resuming => 90,
        SwitchStage.Reopening => 35,
        SwitchStage.ModelProfile => 180,
        _ => 60
    });

    private static bool IsRetryable(Exception error) => error switch
    {
        SwitchDeadlineException deadline => deadline.Stage is SwitchStage.BackingUp or SwitchStage.Connecting or SwitchStage.Resuming,
        IOException or TimeoutException or System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 or 32 or 33 or 170 } => true,
        _ => false
    };
    private void Checkpoint(AccountSwitchPlan plan, SwitchPhase phase)
    { plan.Phase = phase; plan.Error = null; _save(plan); _changed(plan); }
}
