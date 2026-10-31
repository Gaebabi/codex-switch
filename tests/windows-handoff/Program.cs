using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows handoff checks require Windows");
        var executable = Path.GetFullPath(args.Single());
        if (Path.GetFileName(executable) != "HandoffFixture.exe" || !File.Exists(executable) || RuntimeLocator.IsDesktopRunning(executable))
            throw new InvalidOperationException("A fresh disposable fixture executable is required");
        var directory = Path.Combine(Path.GetTempPath(), "codex-handoff-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var passed = new List<string>();
        try
        {
            var watch = Stopwatch.StartNew(); await DesktopShutdown.CloseAsync(executable, CancellationToken.None);
            Assert(watch.Elapsed < TimeSpan.FromSeconds(2), "An absent desktop incurred a shutdown delay"); passed.Add("Absent desktop needs no retry or fixed delay");
            using (var sentinel = await StartSentinelAsync(executable, directory))
            using (var fixture = await StartAsync(executable, directory, 1800))
            {
                var backend = new Backend(executable, fixture); var phases = new List<SwitchPhase>();
                var coordinator = new AccountSwitchCoordinator(backend, _ => { }, plan => phases.Add(plan.Phase), UserErrors.Describe);
                var plan = await coordinator.PrepareAsync("target", DateTimeOffset.UtcNow, CancellationToken.None);
                Assert(await coordinator.RunAsync(plan, CancellationToken.None), "One invocation did not complete the handoff");
                Assert(fixture.Parent.HasExited && fixture.Child.HasExited, "Credentials changed before actual process exit");
                Assert(!sentinel.Process.HasExited, "A separate same-name process was terminated by the handoff");
                Assert(backend.Calls.SequenceEqual(new[] { "name", "close", "apply", "launch", "resume" }) && plan.Phase == SwitchPhase.Finished,
                    "Handoff needed a second invocation, repeated work or reordered shutdown/application");
                passed.Add("Real delayed child exits and an unrelated same-name app survives one apply/launch/resume invocation");
            }
            using (var fixture = await StartAsync(executable, directory, 1800))
            {
                var backend = new Backend(executable, fixture); AccountSwitchPlan? saved = null;
                var coordinator = new AccountSwitchCoordinator(backend, plan => saved = plan == null ? null : JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(plan)), _ => { }, UserErrors.Describe);
                var plan = await coordinator.PrepareAsync("source", DateTimeOffset.UtcNow, CancellationToken.None, creditPause: true);
                var marker = plan.Chats.Single().ContinueMessageId;
                Assert(!await coordinator.RunAsync(plan, CancellationToken.None) && fixture.Parent.HasExited && fixture.Child.HasExited,
                    "Credit protection did not wait for actual normal parent/child exit");
                Assert(saved is { CreditProtectionPause: true, Phase: SwitchPhase.CreditPaused } && saved.Chats.Single().ContinueMessageId == marker &&
                    backend.Calls.SequenceEqual(new[] { "name", "close" }), "Credit pause rewrote credentials, launched, sent Continue or lost its checkpoint");
                passed.Add("Credit protection normally closes the actual disposable parent/child and preserves recovery without apply/launch/Continue");
            }
            using (var fixture = await StartAsync(executable, directory, 1800))
            using (var cancellation = new CancellationTokenSource())
            {
                var backend = new Backend(executable, fixture); var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
                var plan = await coordinator.PrepareAsync("target", DateTimeOffset.UtcNow, CancellationToken.None);
                var cancel = Task.Run(async () => { await fixture.Parent.WaitForExitAsync(); cancellation.Cancel(); });
                var cancelled = false;
                try { await coordinator.RunAsync(plan, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                await cancel;
                Assert(cancelled && fixture.Parent.HasExited && !plan.AccountApplied && !backend.Calls.Contains("apply"), "Cancellation was suppressed or credentials were applied");
                passed.Add("Cancellation during child exit leaves credentials and continuation untouched");
            }
            using (var fixture = await StartAsync(executable, directory, 33000))
            {
                var backend = new Backend(executable, fixture); var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, UserErrors.Describe);
                var plan = await coordinator.PrepareAsync("target", DateTimeOffset.UtcNow, CancellationToken.None);
                watch.Restart(); var timedOut = false;
                try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (IOException) { timedOut = true; }
                Assert(timedOut && watch.Elapsed >= TimeSpan.FromSeconds(29) && watch.Elapsed < TimeSpan.FromSeconds(40) &&
                    !fixture.Child.HasExited && !plan.AccountApplied && !backend.Calls.Contains("launch"), "Deadline, no-force shutdown or apply/launch boundary failed");
                passed.Add("30-second deadline stops handoff without force-killing a slow child or applying credentials");
            }
            using (var fixture = await StartAsync(executable, directory, 0, true))
            {
                var denied = false;
                try { await DesktopShutdown.CloseAsync(executable, CancellationToken.None); }
                catch (IOException error) when (error.InnerException is System.ComponentModel.Win32Exception native && native.NativeErrorCode == 351) { denied = true; }
                Assert(denied && !fixture.Parent.HasExited, "Unsupported native shutdown was hidden or force-killed");
                passed.Add("Restart Manager unsupported headless shutdown is preserved and does not force-close the process");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = passed.Count, failed = 0, checks = passed,
                scope = "Real Windows Restart Manager and production shutdown/coordinator; only disposable parent/child processes. Synthetic backend credential/launch/resume endpoints; no live Codex/account/chat acceptance." }));
            return 0;
        }
        finally
        {
            var target = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(target) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(target).StartsWith("codex-handoff-", StringComparison.Ordinal)) throw new IOException("Unexpected cleanup target");
            Directory.Delete(target, true);
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<OwnedSentinel> StartSentinelAsync(string executable, string directory)
    {
        var other = Path.Combine(directory, "other"); Directory.CreateDirectory(other);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(executable)!)) File.Copy(file, Path.Combine(other, Path.GetFileName(file)));
        var copy = Path.Combine(other, Path.GetFileName(executable));
        var ready = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(copy) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { "sentinel", ready, "0" }) start.ArgumentList.Add(value);
        var process = Process.Start(start) ?? throw new IOException("Sentinel did not start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { while (!File.Exists(ready)) await Task.Delay(25, timeout.Token); return new OwnedSentinel(process); }
        catch { if (!process.HasExited) process.Kill(); process.Dispose(); throw; }
    }
    private sealed class OwnedSentinel(Process process) : IDisposable
    {
        public Process Process => process;
        public void Dispose() { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
    }
    private static async Task<Fixture> StartAsync(string executable, string directory, int delay, bool headless = false)
    {
        var ready = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { headless ? "headless" : "parent", ready, delay.ToString() }) start.ArgumentList.Add(value);
        var parent = Process.Start(start) ?? throw new IOException("Fixture did not start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!File.Exists(ready)) await Task.Delay(25, timeout.Token);
            var child = Process.GetProcessById(int.Parse(File.ReadAllText(ready)));
            _ = child.SafeHandle;
            return new Fixture(parent, child);
        }
        catch { if (!parent.HasExited) parent.Kill(true); parent.Dispose(); throw; }
    }
    private sealed record Fixture(Process Parent, Process Child) : IDisposable
    {
        public void Dispose()
        {
            if (!Parent.HasExited) { Parent.Kill(); Parent.WaitForExit(); }
            if (!Child.HasExited) { Child.Kill(); Child.WaitForExit(); }
            Parent.Dispose(); Child.Dispose();
        }
    }
    private sealed class Backend(string executable, Fixture fixture) : IAccountSwitchBackend
    {
        public List<string> Calls = new();
        private string _account = "source";
        public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => Task.FromResult<string?>(_account);
        public Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation) => Task.FromResult(new[] { new SwitchChat { Id = "fictional-chat", OriginalTitle = "Fictional handoff" } });
        public Task ValidateChatsForTargetAsync(SwitchChat[] chats, string target, CancellationToken cancellation) => Task.CompletedTask;
        public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation) { Calls.Add("name"); return Task.CompletedTask; }
        public async Task CloseDesktopAsync(CancellationToken cancellation) { Calls.Add("close"); await DesktopShutdown.CloseAsync(executable, cancellation); }
        public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation)
        { Assert(fixture.Parent.HasExited && fixture.Child.HasExited && _account == source, "Applying before actual exit or to a changed account"); Calls.Add("apply"); _account = key; return Task.CompletedTask; }
        public Task LaunchDesktopAsync(CancellationToken cancellation) { Calls.Add("launch"); return Task.CompletedTask; }
        public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation)
        { Calls.Add("resume"); sending(); return Task.FromResult(ChatResumeState.Resumed); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
