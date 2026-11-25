using CodexAccountDesk;

internal static class SwitchLifecycleContracts
{
    public static async Task Run(Action<string, bool> check)
    {
        var now = DateTimeOffset.UtcNow;
        var abandoned = new AccountSwitchPlan
        {
            SourceAccountKey = "source", TargetAccountKey = "source", RequestedAt = now.AddDays(-4),
            RestartOnly = true, Phase = SwitchPhase.Closing, DesktopRestartRequired = true
        };
        check("Only an explicit switch from the recorded login may replace an empty pre-apply restart",
            SwitchRecovery.AllowsAccount(abandoned, "target", "source", manualSwitch: true) &&
            !SwitchRecovery.AllowsAccount(abandoned, "target", "source", manualSwitch: false) &&
            !SwitchRecovery.AllowsAccount(abandoned, "target", "different", manualSwitch: true));
        abandoned.Chats.Add(new SwitchChat());
        check("A restart with a chat checkpoint holds other targets while allowing its own recovery",
            !SwitchRecovery.AllowsAccount(abandoned, "target", "source", true) &&
            SwitchRecovery.AllowsAccount(abandoned, "source", "source", true));
        abandoned.Chats.Clear();
        abandoned.ModelProfile = CodexModelProfile.Native;
        check("A profile restoration cannot be replaced", !SwitchRecovery.AllowsAccount(abandoned, "target", "source", true));
        abandoned.ModelProfile = null;
        abandoned.AccountApplied = true;
        check("An applied restart cannot be replaced", !SwitchRecovery.AllowsAccount(abandoned, "target", "source", true));
        abandoned.AccountApplied = false;

        AccountSwitchPlan? saved = abandoned;
        var backend = new FakeBackend { CaptureOverride = async token => { await Task.Delay(Timeout.Infinite, token); return []; } };
        var coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, error => error.Message,
            budget: _ => TimeSpan.FromMilliseconds(80));
        var discoveryTimedOut = false;
        try { await coordinator.PrepareAsync("target", now, default, expectedSource: "source"); }
        catch (SwitchDeadlineException error) { discoveryTimedOut = error.Stage == SwitchStage.Discovering; }
        check("A preparation deadline preserves the existing checkpoint and touches neither account nor desktop",
            discoveryTimedOut && ReferenceEquals(saved, abandoned) && backend.Closes == 0 && backend.Applies == 0);
        backend.CaptureOverride = _ => { backend.Current = "changed"; return Task.FromResult(Array.Empty<SwitchChat>()); };
        try { await coordinator.PrepareAsync("target", now, default, expectedSource: "source"); }
        catch (InvalidOperationException) { }
        check("A login change during fresh capture preserves the original restart", ReferenceEquals(saved, abandoned));

        foreach (var restart in new[] { false, true })
        {
            backend = new FakeBackend { CapturedChats = [], ConnectionFailures = 20 };
            var progress = new List<SwitchStage>();
            coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, error => error.Message,
                progress: item => progress.Add(item.Stage));
            var plan = restart ? await coordinator.PrepareRestartAsync("source", now, default) :
                await coordinator.PrepareAsync("target", now, default, expectedSource: "source");
            var expected = restart
                ? new[] { SwitchStage.Discovering, SwitchStage.Discovering, SwitchStage.Closing, SwitchStage.Launching }
                : new[] { SwitchStage.Discovering, SwitchStage.Discovering, SwitchStage.Closing, SwitchStage.Applying, SwitchStage.Launching };
            check($"Zero-chat {(restart ? "restart" : "switch")} completes after one launch without waiting for IPC",
                await coordinator.RunAsync(plan, default) && saved == null && backend.Connections == 0 && backend.Launches == 1 && progress.SequenceEqual(expected));
        }

        foreach (var stage in new[] { SwitchStage.Closing, SwitchStage.Launching })
        {
            var settled = false;
            async Task WaitForDeadline(CancellationToken token)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { await Task.Delay(10); settled = true; }
            }
            backend = new FakeBackend();
            if (stage == SwitchStage.Closing) backend.CloseOverride = WaitForDeadline;
            else backend.LaunchOverride = WaitForDeadline;
            coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, error => error.Message,
                budget: step => step == stage ? TimeSpan.FromMilliseconds(80) : TimeSpan.FromSeconds(2));
            var plan = await coordinator.PrepareAsync("target", now, default);
            var timedOut = false;
            try { await coordinator.RunAsync(plan, default); }
            catch (SwitchDeadlineException error) { timedOut = error.Stage == stage; }
            check($"{stage} deadline waits for the operation to settle and preserves recovery without repeating the request",
                timedOut && settled && saved?.DesktopRestartRequired == true && saved.AutomaticRecoveryAttempts == 0 &&
                backend.Closes == 1 && backend.Launches == (stage == SwitchStage.Closing ? 0 : 1) &&
                backend.Applies == (stage == SwitchStage.Closing ? 0 : 1) && backend.Resumes == 0);
        }

        backend = new FakeBackend { ConnectionFailures = 3 };
        coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, error => error.Message);
        var disconnected = await coordinator.PrepareAsync("target", now, default);
        try { await coordinator.RunAsync(disconnected, default); }
        catch (TimeoutException) { }
        check("A disconnected launched app retains the unfinished chat recovery and never relaunches",
            saved is { Phase: SwitchPhase.Resuming, AccountApplied: true, DesktopRestartRequired: false, AutomaticRecoveryAttempts: 2 } &&
            backend.Connections == 3 && backend.Launches == 1 && backend.Resumes == 0);

        backend = new FakeBackend { AfterSending = token => Task.Delay(Timeout.Infinite, token) };
        coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, error => error.Message,
            budget: step => step == SwitchStage.Resuming ? TimeSpan.FromMilliseconds(80) : TimeSpan.FromSeconds(2));
        var unconfirmed = await coordinator.PrepareAsync("target", now, default);
        var marker = unconfirmed.Chats.Single().ContinueMessageId;
        check("A Continue deadline returns to observation and keeps the same single dispatch",
            !await coordinator.RunAsync(unconfirmed, default) && backend.Launches == 1 && backend.Resumes == 1 &&
            backend.ResumeCalls == 3 && saved!.Chats.Single().ResumeState == ChatResumeState.Unconfirmed &&
            saved.Chats.Single().ContinueMessageId == marker);
    }
}
