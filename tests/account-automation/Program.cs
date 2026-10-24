using CodexAccountDesk;
using System.Text;
using System.Text.Json;

var checks = new List<string>();
void Check(string name, bool condition) { if (!condition) throw new Exception(name); checks.Add(name); }
var now = DateTimeOffset.UtcNow;
AccountProfile Profile(string key, double remaining, bool current = false) => new() {
    Key = key, IsCurrent = current, Usage = new() { CheckedAt = now, Buckets = new() {
        new() { Id = "codex", Primary = new() { UsedPercent = 100 - remaining, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } } };
AccountProfile? Choose(AccountProfile source, AccountProfile target) => AutomaticSwitchPolicy.Select(new[] { source, target }, 10, now);
var source = Profile("source", 9, true); var target = Profile("target", 80);
Check("Below threshold chooses a fresh eligible account", Choose(source, target) == target);
var exhausted = Profile("source", 0, true); exhausted.Usage!.OrdinaryUsageAllowed = false; exhausted.Usage.Buckets[0].LimitState = "reached";
Check("An exhausted source can switch away despite its limit flag", Choose(exhausted, target) == target);
source.Usage!.Buckets[0].Primary!.UsedPercent = 90;
Check("Exactly threshold does not switch", Choose(source, target) == null);
source.Usage.Buckets[0].Primary!.UsedPercent = 91;
foreach (var change in new Action<AccountProfile>[] {
    p => p.Error = "offline", p => p.IsRefreshing = true, p => p.RequiresApply = true,
    p => p.Usage!.CheckedAt = now.AddMinutes(-3), p => p.Usage!.CheckedAt = now.AddMinutes(1),
    p => p.Usage!.OrdinaryUsageAllowed = false, p => p.Usage!.Buckets[0].SpendControlReached = true,
    p => p.Usage!.Buckets[0].LimitState = "blocked", p => p.Usage!.Buckets[0].Primary = null,
    p => p.Usage!.Buckets[0].Primary!.UsedPercent = double.NaN,
    p => p.Usage!.Buckets[0].Primary!.ResetsAt = now.ToUnixTimeSeconds(),
    p => p.Usage!.Buckets[0].Id = "another-model", p => p.Usage!.Buckets[0].Primary!.Minutes = 300,
    p => p.Usage!.Buckets[0].Primary!.UsedPercent = 86 })
{
    var bad = Profile("target", 80); change(bad); Check("Invalid candidate rejected " + checks.Count, Choose(source, bad) == null);
}
var staleSource = Profile("source", 5, true); staleSource.Error = "offline";
Check("Cached low usage never initiates a switch", Choose(staleSource, target) == null);
foreach (var duration in new long?[] { null, 0, -1 })
{
    var unknownSource = Profile("source", 5, true); var unknownTarget = Profile("target", 80);
    unknownSource.Usage!.Buckets[0].Primary!.Minutes = duration; unknownTarget.Usage!.Buckets[0].Primary!.Minutes = duration;
    Check("Matching unknown or invalid durations never qualify: " + (duration?.ToString() ?? "missing"), Choose(unknownSource, unknownTarget) == null);
}
var partialTarget = Profile("target", 80);
partialTarget.Usage!.Buckets[0].Secondary = new() { UsedPercent = 0, Minutes = null };
Check("An extra candidate window with unknown duration blocks automatic switching", Choose(source, partialTarget) == null);
Check("Missing source never starts Codex", AutomaticSwitchPolicy.Select(new[] { target }, 10, now) == null);
Check("Ambiguous current identities fail closed", AutomaticSwitchPolicy.Select(new[] { source, Profile("second-current", 5, true), target }, 10, now) == null);
Check("Invalid persisted threshold fails closed", AutomaticSwitchPolicy.Select(new[] { source, target }, 99, now) == null);
Check("Highest minimum remaining wins deterministically", AutomaticSwitchPolicy.Select(new[] { source, target, Profile("better", 90) }, 10, now)?.Key == "better");
Check("User ranking takes precedence over remaining amount among eligible accounts", AutomaticSwitchPolicy.Select(new[] { source, target, Profile("better", 90) }, 10, now, new[] { "target", "better" })?.Key == "target");
var preferredStale = Profile("preferred", 90); preferredStale.Error = "expired login";
Check("User ranking skips an unavailable higher priority", AutomaticSwitchPolicy.Select(new[] { source, target, preferredStale }, 10, now, new[] { "preferred", "target" })?.Key == "target");
Check("User ranking skips the current account", AutomaticSwitchPolicy.Select(new[] { source, target }, 10, now, new[] { "source", "target" })?.Key == "target");
var limited = Profile("limited", 90); limited.Usage!.Buckets[0].Secondary = new() { UsedPercent = 99 };
Check("Another exhausted window excludes a candidate", Choose(source, limited) == null);
Check("Official Pro 100 preserves the prolite tier", SubscriptionPresentation.Plan("pro_lite") == "Pro 100" && SubscriptionPresentation.Plan("prolite") == "Pro 100");
Check("Official Pro 200 preserves the pro tier", SubscriptionPresentation.Plan("pro") == "Pro 200" && SubscriptionPresentation.Plan("pro_200") == "Pro 200");
Check("Official Pro 500 preserves the promax tier", SubscriptionPresentation.Plan("promax") == "Pro 500" && SubscriptionPresentation.Plan("pro_max") == "Pro 500" && SubscriptionPresentation.Plan("pro_500") == "Pro 500");
Check("Numbered Pro identifiers do not collapse into one label", SubscriptionPresentation.Plan("pro_100") == "Pro 100" && new[] { "prolite", "pro", "promax" }.Select(SubscriptionPresentation.Plan).Distinct().Count() == 3);
Check("Plan normalization retains the correct tier", SubscriptionPresentation.Plan(" PROLITE ") == "Pro 100" && SubscriptionPresentation.Plan("ProMax") == "Pro 500");
Check("Official Business family replaces Team", SubscriptionPresentation.Plan("team") == "Business");
Check("Unknown entitlement is preserved without inventing a plan", SubscriptionPresentation.Plan("future_tier") == "future_tier");
Check("Missing subscription days stay unavailable", SubscriptionPresentation.Days(null, now) == "—");
Check("Subscription days use Korean calendar", SubscriptionPresentation.Days(DateTimeOffset.Parse("2026-10-27T00:00:00+09:00"), DateTimeOffset.Parse("2026-10-24T23:59:00+09:00")) == "3");

foreach (var failure in new[] { "launch-once", "launch-always", "apply", "cancel-after-close" })
{
    using var cancel = new CancellationTokenSource();
    var backend = new FakeBackend { Failure = failure, Cancel = cancel };
    AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p == null ? null : JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(p)), _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, cancel.Token);
    var marker = plan.Chats[0].ContinueMessageId;
    try { await coordinator.RunAsync(plan, cancel.Token); throw new Exception("Expected synthetic failure"); }
    catch (Exception e) when (e.Message != "Expected synthetic failure") { }
    Check(failure + ": checkpoint retained", saved != null);
    Check(failure + ": original or selected desktop relaunch attempted after close", backend.Launches >= 1);
    Check(failure + ": no Continue during restart recovery", backend.Resumes == 0);
    Check(failure + ": failed restart remains retryable", saved!.DesktopRestartRequired == (failure == "launch-always"));
    backend.Failure = ""; backend.Cancel = null;
    Check(failure + ": serialized checkpoint resumes", await coordinator.RunAsync(saved, CancellationToken.None));
    Check(failure + ": same Continue identity survives retry", backend.LastMarker == marker && backend.Resumes == 1);
    Check(failure + ": completed plan cleared", saved == null);
}
{
    var backend = new FakeBackend(); AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    backend.Current = "unrelated";
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (InvalidOperationException) { }
    Check("Unrelated login prevents shutdown/apply/restart", backend.Launches == 0 && backend.Applies == 0 && saved != null);
}
{
    var backend = new FakeBackend { Current = "target", Failure = "launch-once" };
    AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = new AccountSwitchPlan { SourceAccountKey = "source", TargetAccountKey = "target", AccountApplied = true, Phase = SwitchPhase.Resuming };
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (IOException) { }
    Check("A later recovery launch failure also attempts bounded reopening", backend.Launches == 2 && backend.Applies == 0 && saved != null && !saved.DesktopRestartRequired);
}
// User-local temp retains native ACL ownership; a machine-wide redirected temp can forbid WRITE_OWNER.
var fixture = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "codex-account-automation-" + Guid.NewGuid().ToString("N"));
try
{
    var vault = new AccountVault(Path.Combine(fixture, "data"));
    Check("Automation defaults Off at 10 percent", !vault.Data.AutomaticSwitchEnabled && vault.Data.AutomaticSwitchThreshold == 10);
    string Auth(string id, string suffix) => JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new {
        account_id = id, id_token = "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = id, email = id + "@example.invalid" }))).TrimEnd('=').Replace('+','-').Replace('/','_') + ".fictional",
        access_token = "fictional-access-" + suffix, refresh_token = "fictional-refresh-" + suffix } });
    var original = vault.Upsert(Auth("one", "old")); original.Alias = "Preserved alias"; vault.Save();
    var service = new AccountService(vault, Path.Combine(fixture, "absent-home"), (_, _) => Task.CompletedTask);
    var bytes = File.ReadAllBytes(vault.FilePath);
    try { service.CompleteRegistration(Auth("two", "new"), original.Key); throw new Exception("Wrong identity accepted"); }
    catch (InvalidOperationException) { }
    Check("Wrong browser identity preserves vault bytes and account count", bytes.SequenceEqual(File.ReadAllBytes(vault.FilePath)) && vault.Data.Accounts.Count == 1);
    var registration = service.CompleteRegistration(Auth("one", "new"), original.Key); await registration.ImmediateRefresh;
    Check("Same identity refresh preserves key alias and count", registration.Profile.Key == original.Key && original.Alias == "Preserved alias" && vault.Data.Accounts.Count == 1);
    Check("Browser login never overwrites desktop credentials", !Directory.Exists(service.CodexHome));
    var second = vault.Upsert(Auth("two", "test"));
    AccountAutomation.SetPriority(service, second.Key, 1);
    var ranked = new AccountVault(vault.DirectoryPath).Data;
    Check("User priority survives encrypted save and selects custom mode", ranked.AutomaticSwitchUsePriority && ranked.AutomaticSwitchPriority[0] == second.Key);
    AccountAutomation.SetPriority(service, original.Key, 1);
    Check("Moving an account preserves a complete unique order", vault.Data.AutomaticSwitchPriority.SequenceEqual(new[] { original.Key, second.Key }));
    original.Usage = Profile("unused", 5).Usage; second.Usage = Profile("unused", 70).Usage;
    typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, original.Key);
    Check("Disabled automation never reserves", AccountAutomation.Reserve(service, false, now) == null);
    vault.Data.AutomaticSwitchEnabled = true;
    Check("Busy UI never reserves", AccountAutomation.Reserve(service, true, now) == null);
    Check("Successful reservation respects ranking and saves the cooldown", AccountAutomation.Reserve(service, false, now)?.Key == second.Key && new AccountVault(vault.DirectoryPath).Data.LastAutomaticSwitchAttempt == now);
    Check("Timer re-entry cannot repeat a reservation", AccountAutomation.Reserve(service, false, now.AddSeconds(1)) == null);
    vault.Data.AutomaticSwitchEnabled = true; vault.Data.AutomaticSwitchThreshold = 15; vault.Data.LastAutomaticSwitchAttempt = now;
    vault.Save(); var reopened = new AccountVault(vault.DirectoryPath);
    Check("Encrypted preferences and cooldown survive restart", reopened.Data.AutomaticSwitchEnabled && reopened.Data.AutomaticSwitchThreshold == 15 && reopened.Data.LastAutomaticSwitchAttempt == now);
    AccountAutomation.Pause(service);
    Check("Failure pause persists without losing accounts", !new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchEnabled && vault.Data.Accounts.Count == 2);
}
finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, scope = "Fictional account files and backend only; no real Codex process, browser, login or chat touched." }, new JsonSerializerOptions { WriteIndented = true }));

sealed class FakeBackend : IAccountSwitchBackend
{
    public string Current = "source", Failure = "";
    public CancellationTokenSource? Cancel;
    public int Launches, Applies, Resumes;
    public string? LastMarker;
    public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => Task.FromResult<string?>(Current);
    public Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation) => Task.FromResult(new[] { new SwitchChat { Id = "chat", OriginalTitle = "Fictional work" } });
    public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation) => Task.CompletedTask;
    public Task CloseDesktopAsync(CancellationToken cancellation) { if (Failure == "cancel-after-close") Cancel!.Cancel(); return Task.CompletedTask; }
    public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation) { if (Failure == "apply") throw new IOException("Synthetic apply failure"); Current = key; Applies++; return Task.CompletedTask; }
    public Task LaunchDesktopAsync(CancellationToken cancellation) { Launches++; if (Failure == "launch-always" || Failure == "launch-once" && Launches == 1) throw new IOException("Synthetic launch failure"); return Task.CompletedTask; }
    public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation) { sending(); Resumes++; LastMarker = chat.ContinueMessageId; return Task.FromResult(ChatResumeState.Resumed); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
