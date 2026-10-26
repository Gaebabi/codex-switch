using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal enum SwitchPhase { BackingUp, Closing, Applying, Launching, Resuming, Finished }
internal enum ChatResumeState { Pending, Sending, Resumed, AlreadyRunning, Completed, Unconfirmed, Failed }

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
    public bool Automatic { get; set; }
    public int AutomaticRecoveryAttempts { get; set; }
    public bool DesktopRestartRequired { get; set; }
    public List<SwitchChat> Chats { get; set; } = new();
    public string? Error { get; set; }
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
    Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation);
    Task CloseDesktopAsync(CancellationToken cancellation);
    Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation);
    Task LaunchDesktopAsync(CancellationToken cancellation);
    Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation);
}

internal sealed class AccountSwitchCoordinator
{
    public const int MaximumRecoveryAttempts = 2;
    private readonly IAccountSwitchBackend _backend;
    private readonly Action<AccountSwitchPlan?> _save;
    private readonly Action<AccountSwitchPlan> _changed;
    private readonly Func<Exception, string> _describeError;
    public AccountSwitchCoordinator(IAccountSwitchBackend backend, Action<AccountSwitchPlan?> save, Action<AccountSwitchPlan> changed, Func<Exception, string> describeError)
    { _backend = backend; _save = save; _changed = changed; _describeError = describeError; }
    public async Task<AccountSwitchPlan> PrepareAsync(string target, DateTimeOffset at, CancellationToken cancellation)
    {
        var source = await _backend.CurrentAccountAsync(cancellation);
        var chats = await _backend.CaptureAsync(cancellation);
        foreach (var chat in chats) chat.BackupTitle = SwitchTitle.Stamp(chat.OriginalTitle, at);
        var plan = new AccountSwitchPlan { SourceAccountKey = source, TargetAccountKey = target, RequestedAt = at, Chats = chats.ToList() };
        Checkpoint(plan, SwitchPhase.BackingUp); return plan;
    }
    public async Task<bool> RunAsync(AccountSwitchPlan plan, CancellationToken cancellation)
    {
        plan.AutomaticRecoveryAttempts = Math.Clamp(plan.AutomaticRecoveryAttempts, 0, MaximumRecoveryAttempts);
        var shutdownConfirmed = plan.AccountApplied || plan.Phase is SwitchPhase.Applying or SwitchPhase.Launching or SwitchPhase.Resuming;
        var launchAttempted = false;
        try
        {
            var current = await _backend.CurrentAccountAsync(cancellation);
            if (!plan.AccountApplied && current == plan.TargetAccountKey && plan.Phase == SwitchPhase.Applying)
            { plan.AccountApplied = true; Checkpoint(plan, SwitchPhase.Launching); }
            if (!plan.AccountApplied)
            {
                if (current != plan.SourceAccountKey) throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                Checkpoint(plan, SwitchPhase.BackingUp);
                foreach (var chat in plan.Chats.Where(chat => !chat.NameSaved))
                {
                    cancellation.ThrowIfCancellationRequested();
                    await RetryStepAsync(plan, async () =>
                    {
                        if (await _backend.CurrentAccountAsync(cancellation) != plan.SourceAccountKey)
                            throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                        await _backend.BackupNameAsync(chat, plan.RequestedAt, cancellation);
                    }, cancellation);
                    chat.NameSaved = true; Checkpoint(plan, SwitchPhase.BackingUp);
                }
                if (await _backend.CurrentAccountAsync(cancellation) != plan.SourceAccountKey)
                    throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                cancellation.ThrowIfCancellationRequested();
                plan.DesktopRestartRequired = true;
                Checkpoint(plan, SwitchPhase.Closing);
                await RetryStepAsync(plan, async () =>
                {
                    if (await _backend.CurrentAccountAsync(cancellation) != plan.SourceAccountKey)
                        throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                    await _backend.CloseDesktopAsync(cancellation);
                }, cancellation);
                shutdownConfirmed = true;
                cancellation.ThrowIfCancellationRequested();
                if (await _backend.CurrentAccountAsync(cancellation) != plan.SourceAccountKey)
                    throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                Checkpoint(plan, SwitchPhase.Applying);
                await RetryStepAsync(plan, async () =>
                {
                    var account = await _backend.CurrentAccountAsync(cancellation);
                    if (account == plan.TargetAccountKey) return;
                    if (account != plan.SourceAccountKey)
                        throw new InvalidOperationException("로그인 계정이 변경돼 전환을 중단했습니다.");
                    await _backend.ApplyAccountAsync(plan.TargetAccountKey, plan.SourceAccountKey, cancellation);
                }, cancellation);
                plan.AccountApplied = true; Checkpoint(plan, SwitchPhase.Launching);
            }
            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (await _backend.CurrentAccountAsync(cancellation) != plan.TargetAccountKey)
                        throw new InvalidOperationException("선택한 계정과 현재 로그인이 달라 채팅 재개를 중단했습니다.");
                    plan.DesktopRestartRequired = true;
                    Checkpoint(plan, SwitchPhase.Launching); launchAttempted = true;
                    await _backend.LaunchDesktopAsync(cancellation);
                    plan.DesktopRestartRequired = false;
                    Checkpoint(plan, SwitchPhase.Resuming);
                    var retryable = false;
                    foreach (var chat in plan.Chats.Where(chat => chat.ResumeState is not (ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed)))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (await _backend.CurrentAccountAsync(cancellation) != plan.TargetAccountKey)
                            throw new InvalidOperationException("로그인 계정이 변경돼 채팅 재개를 중단했습니다.");
                        try
                        {
                            chat.Error = null;
                            chat.ResumeState = await _backend.ResumeAsync(chat, () => { chat.ResumeState = ChatResumeState.Sending; Checkpoint(plan, SwitchPhase.Resuming); }, cancellation);
                            retryable |= chat.ResumeState is ChatResumeState.Pending or ChatResumeState.Sending or ChatResumeState.Unconfirmed;
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            chat.ResumeState = chat.ResumeState is ChatResumeState.Sending or ChatResumeState.Unconfirmed ? ChatResumeState.Unconfirmed : ChatResumeState.Failed;
                            chat.Error = _describeError(error);
                            retryable |= IsRetryable(error);
                        }
                        Checkpoint(plan, SwitchPhase.Resuming);
                    }
                    var complete = plan.Chats.All(chat => chat.ResumeState is ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed);
                    if (complete) { Checkpoint(plan, SwitchPhase.Finished); _save(null); return true; }
                    if (!retryable || plan.AutomaticRecoveryAttempts >= MaximumRecoveryAttempts) return false;
                }
                catch (Exception error) when (!cancellation.IsCancellationRequested && IsRetryable(error) && plan.AutomaticRecoveryAttempts < MaximumRecoveryAttempts)
                {
                    plan.Error = _describeError(error);
                }
                // Keep the caller's switching state until both retries are spent. Persist the
                // reservation before waiting so a utility restart cannot reset the retry budget.
                await ReserveRetryAsync(plan, cancellation);
            }
        }
        catch (Exception error)
        {
            plan.Error = error is OperationCanceledException ? "전환 작업을 취소했습니다." : _describeError(error);
            if (error is OperationCanceledException && cancellation.IsCancellationRequested && plan.Automatic)
                plan.AutomaticRecoveryAttempts = Math.Max(MaximumRecoveryAttempts, plan.AutomaticRecoveryAttempts);
            // Reopen after cancellation or a failed apply, but do not add a fourth launch after
            // exhausting the retries. Never overwrite credentials or repeat Continue here.
            if (plan.DesktopRestartRequired && shutdownConfirmed && (!launchAttempted || cancellation.IsCancellationRequested))
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                try
                {
                    var current = await _backend.CurrentAccountAsync(recovery.Token);
                    if (current == plan.SourceAccountKey || current == plan.TargetAccountKey)
                    {
                        await _backend.LaunchDesktopAsync(recovery.Token);
                        plan.DesktopRestartRequired = false;
                    }
                }
                catch (Exception restartError) { plan.Error += "\n" + Loc.F("switch.restart_failed", _describeError(restartError)); }
            }
            _save(plan); _changed(plan); throw;
        }
    }
    private async Task RetryStepAsync(AccountSwitchPlan plan, Func<Task> action, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { await action(); return; }
            catch (Exception error) when (!cancellation.IsCancellationRequested && IsRetryable(error) && plan.AutomaticRecoveryAttempts < MaximumRecoveryAttempts)
            { plan.Error = _describeError(error); }
            await ReserveRetryAsync(plan, cancellation);
        }
    }
    private Task ReserveRetryAsync(AccountSwitchPlan plan, CancellationToken cancellation)
    {
        plan.AutomaticRecoveryAttempts++;
        _save(plan); _changed(plan);
        return Task.Delay(TimeSpan.FromSeconds(1), cancellation);
    }
    private static bool IsRetryable(Exception error) => error is IOException or TimeoutException ||
        error is System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 or 32 or 33 or 170 };
    private void Checkpoint(AccountSwitchPlan plan, SwitchPhase phase)
    { plan.Phase = phase; plan.Error = null; _save(plan); _changed(plan); }
}
