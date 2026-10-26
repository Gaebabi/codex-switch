using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class SwitchDiagnostics
{
    private sealed class Backend : IAccountSwitchBackend
    {
        public string? Account = "source";
        public SwitchChat[] Chats = new[] { new SwitchChat { Id = "a", OriginalTitle = "테스트 채팅", OriginalTurnId = "old" }, new SwitchChat { Id = "b", OriginalTitle = "[261011] Another chat" } };
        public List<string> Calls = new();
        public Action<SwitchChat>? Naming;
        public Action? Applying;
        public Func<SwitchChat, Action, ChatResumeState>? Resuming;
        public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => Task.FromResult(Account);
        public Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation) { Calls.Add("capture"); return Task.FromResult(Chats); }
        public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation) { Calls.Add("name:" + chat.Id); Naming?.Invoke(chat); return Task.CompletedTask; }
        public Task CloseDesktopAsync(CancellationToken cancellation) { Calls.Add("close"); return Task.CompletedTask; }
        public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation) { Calls.Add("apply"); if (Account != source) throw new InvalidOperationException("source changed"); Account = key; Applying?.Invoke(); return Task.CompletedTask; }
        public Task LaunchDesktopAsync(CancellationToken cancellation) { Calls.Add("launch"); return Task.CompletedTask; }
        public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation)
        { Calls.Add("resume:" + chat.Id); if (Resuming != null) return Task.FromResult(Resuming(chat, sending)); sending(); return Task.FromResult(ChatResumeState.Resumed); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    public static void AddTests(Action<string, Action> test, Action<bool, string> assert, string directory)
    {
        var at = DateTimeOffset.Parse("2026-10-11T15:01:00Z");
        test("Date backup adds or updates one prefix using the Korean calendar day", () =>
        {
            assert(SwitchTitle.Stamp("작업 이름", at) == "[261012] 작업 이름", "New date prefix or Korean day failed");
            assert(SwitchTitle.Stamp("[261011] 작업 이름", at) == "[261012] 작업 이름", "Existing date accumulated");
            assert(SwitchTitle.Stamp("[261010] [261011] 작업 이름", at) == "[261012] 작업 이름", "Repeated date prefixes accumulated");
            assert(SwitchTitle.Stamp(SwitchTitle.Stamp("작업 이름", at), at) == "[261012] 작업 이름", "Repeating backup changed the name");
        });
        test("Date backup preserves non-date tags and Unicode names", () =>
        {
            assert(SwitchTitle.Stamp("[진행] café 작업", at) == "[261012] [진행] café 작업", "Non-date tag or encoding was lost");
            var failed = false; try { SwitchTitle.Stamp("[261011] ", at); } catch (InvalidOperationException) { failed = true; }
            assert(failed, "An unknown name was fabricated");
        });
        test("All names are confirmed before shutdown, account apply, launch, and resume", () =>
        {
            var backend = new Backend(); AccountSwitchPlan? saved = null;
            var coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, CancellationToken.None).GetAwaiter().GetResult();
            backend.Resuming = (chat, sending) => { sending(); assert(saved?.Phase == SwitchPhase.Resuming && chat.ResumeState == ChatResumeState.Sending, "Send preceded its durable checkpoint"); return ChatResumeState.Resumed; };
            assert(coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(), "Transition did not finish");
            assert(backend.Calls.IndexOf("close") > backend.Calls.IndexOf("name:b") && backend.Calls.IndexOf("apply") > backend.Calls.IndexOf("close") &&
                backend.Calls.IndexOf("resume:a") > backend.Calls.IndexOf("launch"), "Transition reordered destructive steps");
            assert(saved == null && plan.AccountApplied && plan.Chats.All(chat => chat.NameSaved), "Completed plan or name progress did not persist");
        });
        test("A name failure prevents shutdown/apply and retry preserves completed backups", () =>
        {
            var backend = new Backend { Naming = chat => { if (chat.Id == "b") throw new IOException("synthetic name failure"); } };
            var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, CancellationToken.None).GetAwaiter().GetResult();
            var failed = false; try { coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { failed = true; }
            assert(failed && !backend.Calls.Contains("close") && !backend.Calls.Contains("apply") && plan.Chats[0].NameSaved && !plan.Chats[1].NameSaved, "Partial backup closed the desktop");
            backend.Naming = null; assert(coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(), "Name retry failed");
            assert(backend.Calls.Count(call => call == "name:a") == 1, "Saved backup repeated during retry");
        });
        test("Cancellation during backup preserves names and leaves the account unchanged", () =>
        {
            using var cancellation = new CancellationTokenSource(); var backend = new Backend { Naming = _ => cancellation.Cancel() };
            var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, cancellation.Token).GetAwaiter().GetResult();
            var failed = false; try { coordinator.RunAsync(plan, cancellation.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { failed = true; }
            assert(failed && backend.Account == "source" && !backend.Calls.Contains("close") && plan.Chats[0].NameSaved, "Cancellation applied credentials or lost saved names");
        });
        test("An external login change during backup prevents shutdown and account overwrite", () =>
        {
            var backend = new Backend(); backend.Naming = _ => backend.Account = "other";
            var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, CancellationToken.None).GetAwaiter().GetResult();
            var failed = false; try { coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { failed = true; }
            assert(failed && !backend.Calls.Contains("close") && !backend.Calls.Contains("apply"), "Changed login was overwritten");
        });
        test("Recovery after credential apply does not apply credentials or rename chats twice", () =>
        {
            var backend = new Backend { Applying = () => throw new IOException("synthetic checkpoint failure after apply") };
            var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, CancellationToken.None).GetAwaiter().GetResult();
            assert(coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(), "Applied account did not recover in the original invocation");
            assert(plan.Phase == SwitchPhase.Finished && backend.Account == "target" && plan.AccountApplied && plan.AutomaticRecoveryAttempts == 1, "Automatic reconciliation lost its checkpoint or retry budget");
            assert(backend.Calls.Count(call => call == "apply") == 1 && backend.Calls.Count(call => call == "name:a") == 1, "Recovery repeated apply/name writes");
        });
        test("Resume failure preserves an unconfirmed send, resumes other chats, and skips finished work on retry", () =>
        {
            var backend = new Backend { Resuming = (chat, sending) => { sending(); if (chat.Id == "a") throw new IOException("synthetic delivery loss"); return ChatResumeState.Resumed; } };
            AccountSwitchPlan? saved = null; var coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan, _ => { }, UserErrors.Describe);
            var plan = coordinator.PrepareAsync("target", at, CancellationToken.None).GetAwaiter().GetResult();
            assert(!coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult(), "Unconfirmed delivery was declared complete");
            assert(saved != null && plan.Chats[0].ResumeState == ChatResumeState.Unconfirmed && plan.Chats[1].ResumeState == ChatResumeState.Resumed, "Resume failure lost partial progress");
            var marker = plan.Chats[0].ContinueMessageId; backend.Resuming = (chat, _) => ChatResumeState.Unconfirmed;
            assert(!coordinator.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult() && plan.Chats[0].ContinueMessageId == marker, "Retry replaced the submission identity");
            assert(backend.Calls.Count(call => call == "resume:b") == 1 && backend.Calls.Count(call => call == "apply") == 1, "Retry repeated completed work or account apply");
        });
        test("Continuation skips active/completed work and never automatically repeats ambiguous delivery", () =>
        {
            var chat = new SwitchChat { Id = "a", OriginalTurnId = "old" };
            var thread = new DesktopThreadState("a", "owner", "idle", false, null, "name", "resumed", "old", "interrupted", null);
            assert(SwitchContinuation.Decide(chat, thread) == null, "Interrupted idle work did not need continuation");
            assert(SwitchContinuation.Decide(chat, thread with { Status = "active" }) == ChatResumeState.AlreadyRunning, "Active work was sent another message");
            assert(SwitchContinuation.Decide(chat, thread with { LastTurnStatus = "completed" }) == ChatResumeState.Completed, "Completed source turn restarted");
            assert(SwitchContinuation.Decide(chat, thread with { LastTurnId = "new" }) == ChatResumeState.Completed, "Newer work restarted");
            chat.ResumeState = ChatResumeState.Unconfirmed;
            assert(SwitchContinuation.Decide(chat, thread) == ChatResumeState.Unconfirmed, "Unknown send was repeated");
            assert(SwitchContinuation.Decide(chat, thread with { LastTurnId = "new", LastTurnStatus = "completed", LastMessageId = chat.ContinueMessageId }) == ChatResumeState.Completed, "Completed identified send was lost");
        });
        test("Pending switch names, target IDs, and submission identities survive encrypted vault reload", () =>
        {
            var path = Path.Combine(directory, "switch-vault"); var vault = new AccountVault(path);
            var chat = new SwitchChat { Id = "a", OriginalTitle = "private-switch-title", BackupTitle = "[261012] private-switch-title", NameSaved = true, ResumeState = ChatResumeState.Unconfirmed };
            vault.Data.PendingSwitch = new() { SourceAccountKey = "source", TargetAccountKey = "target", AccountApplied = true, Phase = SwitchPhase.Resuming, Chats = new() { chat } }; vault.Save();
            var loaded = new AccountVault(path).Data.PendingSwitch!;
            assert(loaded.TargetAccountKey == "target" && loaded.Chats[0].NameSaved && loaded.Chats[0].ContinueMessageId == chat.ContinueMessageId && loaded.Chats[0].ResumeState == ChatResumeState.Unconfirmed, "Switch progress did not survive reload");
            assert(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(vault.FilePath)).Contains("private-switch-title", StringComparison.Ordinal), "Chat title was stored in plaintext");
        });
        test("Continue targets the original chat once and inherits model/workspace/permission settings", () =>
        {
            var chat = new SwitchChat { Id = "original-chat" };
            var parameters = JsonSerializer.SerializeToElement(SwitchContinuation.StartParameters(chat));
            var start = parameters.GetProperty("turnStart"); var request = start.GetProperty("request");
            assert(JsonValue.String(parameters, "conversationId") == chat.Id && JsonValue.String(request, "threadId") == chat.Id &&
                JsonValue.String(request, "clientUserMessageId") == chat.ContinueMessageId && request.GetProperty("input")[0].GetProperty("text").GetString() == "Continue", "Wrong chat, message, or submission identity");
            assert(JsonValue.Bool(start.GetProperty("context"), "inheritThreadSettings") == true && !request.TryGetProperty("model", out _) &&
                !request.TryGetProperty("sandboxPolicy", out _) && !request.TryGetProperty("cwd", out _), "Continuation replaced existing settings");
        });
    }
}
