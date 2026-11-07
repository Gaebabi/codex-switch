using CodexAccountDesk;
using System.Text.Json;

internal static class CreditProtectionContracts
{
    public static async Task Run(Action<string, bool> check)
    {
        var at = DateTimeOffset.UtcNow;
        var backend = new FakeBackend(); AccountSwitchPlan? saved = null;
        var coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan == null ? null : JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(plan)), _ => { }, e => e.Message);
        var pause = await coordinator.PrepareAsync("source", at, default, creditPause: true);
        var marker = pause.Chats[0].ContinueMessageId;
        check("Credit pause is durable before any normal shutdown", saved?.CreditProtectionPause == true && backend.Closes == 0);
        check("Credit pause stops after confirmed shutdown without applying credentials, relaunching or sending Continue",
            !await coordinator.RunAsync(pause, default) && pause.Phase == SwitchPhase.CreditPaused && backend.Closes == 1 && backend.Applies == 0 && backend.Launches == 0 && backend.Resumes == 0);
        var replay = JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(saved))!;
        check("Persisted credit pause never blindly reopens on coordinator replay", !await coordinator.RunAsync(replay, default) && backend.Launches == 0 && backend.Closes == 1);
        await coordinator.RetargetCreditPauseAsync(replay, "source", default);
        check("Explicit same-account recovery validates saved chat contexts and retains original Continue identity", backend.TargetValidations == 1 && replay.Chats[0].ContinueMessageId == marker && !replay.CreditProtectionPause);
        check("Same-account credit recovery resumes without a credential write or another shutdown", await coordinator.RunAsync(replay, default) && backend.Applies == 0 && backend.Closes == 1 && backend.Resumes == 1 && backend.LastMarker == marker);
        backend = new(); coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); await coordinator.RunAsync(pause, default); marker = pause.Chats[0].ContinueMessageId;
        await coordinator.RetargetCreditPauseAsync(pause, "target", default);
        check("Credit pause can switch to a newly usable account only after target-context validation", backend.TargetValidations == 1 && !pause.AccountApplied && pause.TargetAccountKey == "target");
        check("Retargeted recovery preserves chats and uses the ordinary handoff path", await coordinator.RunAsync(pause, default) && backend.Applies == 1 && backend.Current == "target" && backend.LastMarker == marker);
        backend = new(); coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); await coordinator.RunAsync(pause, default);
        marker = pause.Chats[0].ContinueMessageId;
        await coordinator.RetargetCreditPauseAsync(pause, "target", default); backend.Failure = "apply";
        try { await coordinator.RunAsync(pause, default); throw new Exception("Apply failure ignored"); } catch (IOException) { }
        check("A failed protected handoff never falls back to relaunching the depleted source", backend.Current == "source" && backend.Launches == 0 && pause.PreventDepletedSourceRelaunch && pause.Error != null);
        backend.Failure = "";
        check("Failed protected apply retains the original resumable chat and Continue marker", await coordinator.RunAsync(pause, default) && backend.Current == "target" && backend.LastMarker == marker);
        backend = new() { Failure = "target-refused" }; coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); await coordinator.RunAsync(pause, default);
        var before = JsonSerializer.Serialize(pause);
        try { await coordinator.RetargetCreditPauseAsync(pause, "target", default); throw new Exception("Target refusal bypassed"); }
        catch (InvalidDataException) { }
        check("A target workspace refusal preserves the credit checkpoint and credentials", JsonSerializer.Serialize(pause) == before && backend.Applies == 0 && backend.Launches == 0);
        backend = new(); coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); await coordinator.RunAsync(pause, default); marker = pause.Chats[0].ContinueMessageId;
        await coordinator.RearmCreditPauseAsync(pause, default); await coordinator.RunAsync(pause, default);
        check("Reopened depleted clients can be normally paused again without duplicate backups or Continue", backend.Closes == 2 && backend.Names == 1 && backend.Resumes == 0 && pause.Chats[0].ContinueMessageId == marker);
        using var cancel = new CancellationTokenSource();
        backend = new() { Failure = "cancel-after-close", Cancel = cancel }; coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true);
        try { await coordinator.RunAsync(pause, cancel.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        check("Cancellation after credit shutdown never reopens into paid usage", backend.Closes == 1 && backend.Launches == 0 && pause.CreditProtectionPause);
        backend = new() { CloseFailures = 1 }; coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); await coordinator.RunAsync(pause, default);
        check("Credit shutdown uses the existing saved bounded retry budget", backend.Closes == 2 && pause.AutomaticRecoveryAttempts == 1 && pause.Phase == SwitchPhase.CreditPaused);
        backend = new() { Failure = "close" }; coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true);
        try { await coordinator.RunAsync(pause, default); throw new Exception("Shutdown failure ignored"); } catch (IOException) { }
        check("Exhausted shutdown retries preserve recovery without claiming a pause or relaunching", backend.Closes == 3 && pause.Phase == SwitchPhase.Closing && pause.Error != null && backend.Launches == 0 && backend.Applies == 0);
        backend = new(); coordinator = new(backend, _ => { }, _ => { }, e => e.Message);
        pause = await coordinator.PrepareAsync("source", at, default, true); backend.Current = "changed";
        try { await coordinator.RunAsync(pause, default); throw new Exception("Identity change ignored"); } catch (InvalidOperationException) { }
        check("Changed accounts cannot be closed by an old credit-protection plan", backend.Closes == 0 && backend.Applies == 0 && backend.Launches == 0);
    }

    public static void Service(Action<string, bool> check, AccountService service, AccountProfile source, AccountProfile target, DateTimeOffset now)
    {
        var data = service.Vault.Data;
        check("New and legacy preferences allow credit use by default", new VaultData().AllowCreditUse && JsonSerializer.Deserialize<VaultData>("{}")!.AllowCreditUse);
        UsageSnapshot Quota(double remaining) => new() { CheckedAt = now, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 100 - remaining, Minutes = 10080, ResetsAt = now.AddDays(1).ToUnixTimeSeconds() } } } };
        source.Usage = Quota(0); target.Usage = Quota(0); data.PendingSwitch = null; data.LastAutomaticSwitchAttempt = null;
        data.AllowCreditUse = true; data.AutomaticSwitchEnabled = true;
        check("Default credit permission never requests a stop at zero quota", AccountAutomation.CreditStopTarget(service, false, now) == null);
        data.AllowCreditUse = false;
        check("Opted-in zero quota with no eligible account requests the current-account pause", AccountAutomation.CreditStopTarget(service, false, now)?.Key == source.Key);
        target.Usage = Quota(80);
        check("A feasible automatic target takes precedence over credit shutdown", AccountAutomation.CreditStopTarget(service, false, now) == null && AccountAutomation.Reserve(service, false, now)?.Key == target.Key);
        check("Zero quota is protected if switching is presently prevented by cooldown", AccountAutomation.CreditStopTarget(service, false, now)?.Key == source.Key);
        data.LastAutomaticSwitchAttempt = null; data.AutomaticSwitchEnabled = false;
        check("Credit protection remains effective when automatic switching is disabled", AccountAutomation.CreditStopTarget(service, false, now)?.Key == source.Key);
        check("Busy operations cannot stop a client", AccountAutomation.CreditStopTarget(service, true, now) == null);
        source.Usage.CheckedAt = now.AddMinutes(-3);
        check("Stale quota cannot trigger credit shutdown", AccountAutomation.CreditStopTarget(service, false, now) == null);
        source.Usage = Quota(0); source.Usage.Buckets.Add(source.Usage.Buckets[0]);
        check("Ambiguous bucket identities cannot trigger credit shutdown", AccountAutomation.CreditStopTarget(service, false, now) == null);
        source.Usage = Quota(0);
        data.PendingSwitch = new() { SourceAccountKey = source.Key, TargetAccountKey = source.Key, AccountApplied = true, CreditProtectionPause = true, Phase = SwitchPhase.CreditPaused };
        data.AutomaticSwitchEnabled = true;
        check("A paused depleted account may automatically recover to a newly eligible target", AccountAutomation.Reserve(service, false, now)?.Key == target.Key);
        data.LastAutomaticSwitchAttempt = null; source.Usage = Quota(50);
        check("A paused account may automatically resume once fresh ordinary quota returns", AccountAutomation.Reserve(service, false, now)?.Key == source.Key);
        data.LastAutomaticSwitchAttempt = null; source.Usage = Quota(1);
        check("A nearly empty paused account still respects the configured automatic threshold", AccountAutomation.Next(service, now)?.Key == target.Key && AccountAutomation.Reserve(service, false, now)?.Key == target.Key);
        data.LastAutomaticSwitchAttempt = null; data.PendingSwitch.Phase = SwitchPhase.Closing;
        check("An incomplete credit shutdown never receives unbounded automatic retries", AccountAutomation.Reserve(service, false, now) == null && AccountAutomation.CreditStopTarget(service, false, now) == null);
        data.PendingSwitch = null; data.AllowCreditUse = false; service.Vault.Save();
        check("Opt-in credit protection survives encrypted preference reload", !new AccountVault(service.Vault.DirectoryPath).Data.AllowCreditUse);
        check("Credit recovery accepts fresh positive ordinary quota but not zero or unknown", AccountAutomation.CanUse(service, service.Accounts.Single(p => p.Key == source.Key), now));
        var staged = service.Accounts.Single(p => p.Key == source.Key); staged.RequiresApply = true;
        check("Explicit login application can use verified fresh ordinary quota while automatic selection still refuses staged credentials", AccountAutomation.CanUse(service, staged, now) && !AutomaticSwitchPolicy.OrdinaryAvailable(staged, now));
        source.Usage = Quota(0);
        check("Disabled credit use refuses manual recovery into exhausted quota", !AccountAutomation.CanUse(service, service.Accounts.Single(p => p.Key == source.Key), now));
        data.AllowCreditUse = true;
        check("Explicit credit permission permits manual recovery without inventing quota", AccountAutomation.CanUse(service, service.Accounts.Single(p => p.Key == source.Key), now));
    }
}
