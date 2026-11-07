using CodexAccountDesk;
using System.Text.Json;

internal static class RestartContracts
{
    internal static async Task Run(Action<string, bool> check)
    {
        DesktopShutdown.ValidateAffectedForTest(new[] { (123, 456L) }, new[] { (123, 456L) });
        check("Shutdown accepts only the registered desktop identity", true);
        foreach (var affected in new[] { Array.Empty<(int, long)>(), new[] { (123, 456L), (456, 789L) }, new[] { (123, 457L) } })
        {
            var rejected = false;
            try { DesktopShutdown.ValidateAffectedForTest(new[] { (123, 456L) }, affected); } catch (IOException) { rejected = true; }
            check("Shutdown refuses missing, unrelated or reused process identities", rejected);
        }
        var completed = new AccountSwitchPlan { Phase = SwitchPhase.Finished, AccountApplied = true, RestartOnly = true,
            Chats = new() { new() { ResumeState = ChatResumeState.Resumed }, new() { ResumeState = ChatResumeState.AlreadyRunning } } };
        var retained = new VaultData { PendingSwitch = completed };
        check("A verified finished receipt is retained but does not block a new switch", retained.ActivePendingSwitch == null && ReferenceEquals(retained.PendingSwitch, completed) && !JsonSerializer.Serialize(retained).Contains("ActivePendingSwitch"));
        foreach (var state in new[] { ChatResumeState.Pending, ChatResumeState.Sending, ChatResumeState.Unconfirmed, ChatResumeState.Failed })
        {
            completed.Chats[0].ResumeState = state;
            check("A finished record with an unresolved chat still blocks: " + state, retained.ActivePendingSwitch == completed);
        }
        completed.Chats[0].ResumeState = ChatResumeState.Resumed;
        completed.DesktopRestartRequired = true;
        check("A finished record requiring restart still blocks", retained.ActivePendingSwitch == completed);
        completed.DesktopRestartRequired = false; completed.AccountApplied = false;
        check("A finished record without confirmed account application still blocks", retained.ActivePendingSwitch == completed);
        completed.AccountApplied = true; completed.CreditProtectionPause = true;
        check("A credit-protection record is never treated as a completed receipt", retained.ActivePendingSwitch == completed);
        var backend = new Backend();
        AccountSwitchPlan? saved = null;
        var coordinator = new AccountSwitchCoordinator(backend, value => saved = value, _ => { }, e => e.Message);
        var plan = await coordinator.PrepareRestartAsync("same-account", DateTimeOffset.UtcNow, CancellationToken.None);
        check("Restart plan keeps the original account and title without credential application", plan.RestartOnly && plan.SourceAccountKey == plan.TargetAccountKey && plan.Chats.Single().BackupTitle == "Original title" && plan.Chats.Single().NameSaved);
        var marker = plan.Chats.Single().ContinueMessageId;
        var restored = JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(plan))!;
        check("Restart intent and Continue identity survive encrypted-plan serialization", restored.RestartOnly && restored.Chats.Single().ContinueMessageId == marker);
        check("Restart completes close, launch and eligible resume", await coordinator.RunAsync(restored, CancellationToken.None));
        check("Restart never renames a chat or applies credentials", backend.Applied == 0 && backend.Renamed == 0 && backend.Closed == 1 && backend.Launched == 1 && backend.Resumed == 1 && saved == null);
        backend = new Backend { FailLaunchOnce = true };
        coordinator = new AccountSwitchCoordinator(backend, value => saved = value, _ => { }, e => e.Message);
        plan = await coordinator.PrepareRestartAsync("same-account", DateTimeOffset.UtcNow, CancellationToken.None);
        check("Restart retries a transient launch without repeating shutdown or applying an account", await coordinator.RunAsync(plan, CancellationToken.None) && backend.Launched == 2 && backend.Closed == 1 && backend.Applied == 0);
        backend = new Backend(); coordinator = new AccountSwitchCoordinator(backend, value => saved = value, _ => { }, e => e.Message);
        plan = await coordinator.PrepareRestartAsync("same-account", DateTimeOffset.UtcNow, CancellationToken.None);
        backend.Account = "another-account";
        var refused = false;
        try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (InvalidOperationException) { refused = true; }
        check("An external login change refuses before restart or continuation", refused && backend.Closed == 0 && backend.Applied == 0 && backend.Resumed == 0);
        backend = new Backend(); coordinator = new AccountSwitchCoordinator(backend, value => saved = value, _ => { }, e => e.Message);
        plan = await coordinator.PrepareRestartAsync("same-account", DateTimeOffset.UtcNow, CancellationToken.None);
        backend.CancelAfterClose = new CancellationTokenSource();
        var cancelled = false;
        try { await coordinator.RunAsync(plan, backend.CancelAfterClose.Token); } catch (OperationCanceledException) { cancelled = true; }
        check("Cancellation after shutdown safely reopens without sending Continue", cancelled && backend.Launched == 1 && backend.Resumed == 0 && backend.Applied == 0);
    }
    private sealed class Backend : IAccountSwitchBackend
    {
        public string Account = "same-account";
        public int Applied, Renamed, Closed, Launched, Resumed;
        public bool FailLaunchOnce;
        public CancellationTokenSource? CancelAfterClose;
        public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => Task.FromResult<string?>(Account);
        public Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation) => Task.FromResult(new[] { new SwitchChat { Id = "fixture-chat", OriginalTitle = "Original title", OriginalTurnId = "fixture-turn", OriginalGoal = "active" } });
        public Task ValidateChatsForTargetAsync(SwitchChat[] chats, string target, CancellationToken cancellation) => Task.CompletedTask;
        public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation) { Renamed++; return Task.CompletedTask; }
        public Task CloseDesktopAsync(CancellationToken cancellation) { Closed++; CancelAfterClose?.Cancel(); return Task.CompletedTask; }
        public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation) { Applied++; throw new Exception("Restart must not apply an account"); }
        public Task LaunchDesktopAsync(CancellationToken cancellation) { Launched++; if (FailLaunchOnce && Launched == 1) throw new IOException("Synthetic transient failure"); return Task.CompletedTask; }
        public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation) { sending(); Resumed++; return Task.FromResult(ChatResumeState.Resumed); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
