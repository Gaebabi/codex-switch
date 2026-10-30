using CodexAccountDesk;
using System.Text;
using System.Text.Json;

var checks = new List<string>();
void Check(string name, bool condition) { if (!condition) throw new Exception(name); checks.Add(name); }
var now = DateTimeOffset.UtcNow;
CreditHistoryContracts.Run(Check);
await RuntimeCleanupContracts.Run(Check);
await CreditProtectionContracts.Run(Check);
JsonElement Hello(string version, string home = "/fictional/home", string os = "linux", string family = "unix", string prefix = "codex_account_desk/") =>
    JsonSerializer.SerializeToElement(new { codexHome = home, platformOs = os, platformFamily = family, userAgent = prefix + version + " (fixture)" });
foreach (var version in new[] { "0.162.0-alpha.17.2", "0.162.1", "0.163.0", "0.164.0" })
    Check("Exact reviewed Linux initialization version accepted: " + version, LinuxClientSession.ValidateInitialization(Hello(version), "/fictional/home") == version);
foreach (var hello in new[] { Hello("0.163.1"), Hello("0.164.1"), Hello("0.165.0"), Hello("0.164.0", "/other/home"), Hello("0.164.0", os: "windows"), Hello("0.164.0", family: "windows"), Hello("0.164.0", prefix: "unrelated/") })
{
    var refused = false;
    try { LinuxClientSession.ValidateInitialization(hello, "/fictional/home"); } catch (InvalidDataException) { refused = true; }
    Check("Unreviewed version or mismatched initialization refuses before account access: " + checks.Count, refused);
}
AccountProfile Profile(string key, double remaining, bool current = false) => new() {
    Key = key, IsCurrent = current, Usage = new() { CheckedAt = now, Buckets = new() {
        new() { Id = "codex", Primary = new() { UsedPercent = 100 - remaining, Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } } };
AccountProfile? Choose(AccountProfile source, AccountProfile target) => AutomaticSwitchPolicy.Select(new[] { source, target }, 10, now);
var source = Profile("source", 9, true); var target = Profile("target", 80);
UsageSnapshot AmbiguousUsage() => UsageSnapshot.Parse(JsonDocument.Parse("""
    {"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20,"windowDurationMins":10080}},"codex":{"primary":{"usedPercent":30,"windowDurationMins":10080}}}}
    """).RootElement, "fictional");
var duplicate = Profile("duplicate", 80); duplicate.Usage = AmbiguousUsage(); duplicate.Usage.CheckedAt = now;
Check("Ambiguous JSON bucket IDs are retained as observed", duplicate.Usage.Buckets.Count == 2 && duplicate.Usage.Buckets.All(b => b.Id == "codex"));
var observedAmbiguity = JsonSerializer.Serialize(duplicate.Usage);
Check("An ambiguous candidate is skipped without throwing away valid alternatives", AutomaticSwitchPolicy.Next(new[] { source, duplicate, target }, 10, now)?.Key == target.Key);
Check("Ambiguous candidates never initiate a switch", AutomaticSwitchPolicy.Select(new[] { source, duplicate }, 10, now) == null);
Check("Ambiguous candidate selection leaves the observed quota unchanged", JsonSerializer.Serialize(duplicate.Usage) == observedAmbiguity);
duplicate.IsCurrent = true; source.IsCurrent = false;
duplicate.Usage.Buckets[0].Primary!.UsedPercent = 95; // A low source would otherwise trigger a handoff.
Check("An ambiguous source disables the forecast", AutomaticSwitchPolicy.Next(new[] { duplicate, target }, 10, now) == null);
Check("An ambiguous source cannot trigger an automatic handoff", AutomaticSwitchPolicy.Select(new[] { duplicate, target }, 10, now) == null);
duplicate.IsCurrent = false; source.IsCurrent = true;
foreach (var identifier in new[] { "", " " })
{
    var missing = Profile("missing", 80); missing.Usage!.Buckets[0].Id = identifier;
    Check("Empty bucket IDs cannot qualify: " + identifier.Length, Choose(source, missing) == null);
}
var split = Profile("split", 80); split.Usage!.Buckets.Add(new() { Id = "codex", Secondary = new() { UsedPercent = 0, Minutes = 300 } });
Check("Duplicate bucket IDs with different slots are still ambiguous", Choose(source, split) == null);
Check("Rejection preserves the valid candidate and existing trigger behavior", Choose(source, target)?.Key == target.Key);
Check("Below threshold chooses a fresh eligible account", Choose(source, target) == target);
var exhausted = Profile("source", 0, true); exhausted.Usage!.OrdinaryUsageAllowed = false; exhausted.Usage.Buckets[0].LimitState = "reached";
Check("An exhausted source can switch away despite its limit flag", Choose(exhausted, target) == target);
foreach (var seconds in new[] { 0, -1, -300 })
{
    var awaitingReset = Profile("source", 0, true);
    awaitingReset.Usage!.Buckets[0].Primary!.ResetsAt = now.AddSeconds(seconds).ToUnixTimeSeconds();
    var observation = JsonSerializer.Serialize(awaitingReset.Usage);
    Check("Fresh zero can leave a source whose reset timestamp has arrived: " + seconds, Choose(awaitingReset, target) == target);
    Check("Reset-boundary forecast matches the actual eligible target: " + seconds,
        AutomaticSwitchPolicy.Next(new[] { awaitingReset, target }, 10, now) == target);
    Check("An elapsed reset does not invent available ordinary quota: " + seconds,
        AutomaticSwitchPolicy.Exhausted(awaitingReset, now) && !AutomaticSwitchPolicy.OrdinaryAvailable(awaitingReset, now));
    Check("Reset-boundary selection preserves the observed source report: " + seconds, JsonSerializer.Serialize(awaitingReset.Usage) == observation);
    awaitingReset.Usage.CheckedAt = now.AddMinutes(-3);
    Check("An elapsed reset cannot rescue a stale zero report: " + seconds, Choose(awaitingReset, target) == null);
}
var nonzeroReset = Profile("source", 5, true); nonzeroReset.Usage!.Buckets[0].Primary!.ResetsAt = now.ToUnixTimeSeconds();
Check("An elapsed nonzero source window still requires a new report", Choose(nonzeroReset, target) == null);
var pendingTargetReset = Profile("target", 80); pendingTargetReset.Usage!.Buckets[0].Primary!.ResetsAt = now.ToUnixTimeSeconds();
var pendingSourceReset = Profile("source", 0, true); pendingSourceReset.Usage!.Buckets[0].Primary!.ResetsAt = now.ToUnixTimeSeconds();
Check("An elapsed destination reset remains ineligible even when leaving a zero source", Choose(pendingSourceReset, pendingTargetReset) == null);
pendingSourceReset.Usage.Buckets[0].Secondary = new() { UsedPercent = 20, Minutes = 300, ResetsAt = now.ToUnixTimeSeconds() };
Check("Another expired nonzero source window still blocks automatic selection", Choose(pendingSourceReset, target) == null);
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
var sooner = Profile("sooner", 30); sooner.SubscriptionActiveUntil = now.AddHours(12);
var later = Profile("later", 90); later.SubscriptionActiveUntil = now.AddDays(2);
Check("Nearest future expiry wins over more quota", AutomaticSwitchPolicy.Select(new[] { source, later, sooner }, 10, now) == sooner);
Check("Known future expiry precedes an unknown date", AutomaticSwitchPolicy.Select(new[] { source, target, sooner }, 10, now) == sooner);
later.SubscriptionActiveUntil = sooner.SubscriptionActiveUntil;
Check("Equal expiry uses highest minimum remaining", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now) == later);
sooner.SubscriptionActiveUntil = now.AddHours(-1);
sooner.Usage!.OrdinaryUsageAllowed = true;
Check("A past subscription date still usable by the server is consumed before future dates", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now) == sooner);
Check("Past-date forecast agrees with the eligible automatic target", AutomaticSwitchPolicy.Next(new[] { source, sooner, later }, 10, now) == sooner);
sooner.Usage.OrdinaryUsageAllowed = false;
Check("Past dates never override explicit server denial", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now) == later);
sooner.Usage.OrdinaryUsageAllowed = true; sooner.Usage.Buckets[0].SpendControlReached = true;
Check("Past dates never override a spending restriction", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now) == later);
sooner.Usage.Buckets[0].SpendControlReached = false;
Check("Custom ranks still override date preference", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now, new[] { later.Key, sooner.Key }) == later);
sooner.SubscriptionActiveUntil = now.AddHours(1); sooner.Error = "offline";
Check("Expiry preference cannot bypass stale or failed account guards", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now) == later);
sooner.Error = null;
Check("Explicit custom rank is preserved over expiry", AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now, new[] { "later", "sooner" }) == later);
var healthySource = Profile("source", 80, true);
Check("Next-target forecast works before threshold without initiating a handoff", AutomaticSwitchPolicy.Next(new[] { healthySource, sooner, later }, 10, now) == sooner && AutomaticSwitchPolicy.Select(new[] { healthySource, sooner, later }, 10, now) == null);
Check("Forecast matches selection once threshold is crossed", AutomaticSwitchPolicy.Next(new[] { source, sooner, later }, 10, now) == AutomaticSwitchPolicy.Select(new[] { source, sooner, later }, 10, now));
healthySource.Usage!.CheckedAt = now.AddMinutes(-3);
Check("Forecast also refuses stale source observations", AutomaticSwitchPolicy.Next(new[] { healthySource, sooner, later }, 10, now) == null);
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

foreach (var failures in new[] { 1, 2 })
{
    var backend = new FakeBackend { LaunchFailures = failures }; AccountSwitchPlan? saved = null;
    var retryReservations = new List<int>();
    var coordinator = new AccountSwitchCoordinator(backend,
        p => saved = p == null ? null : JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(p)),
        p => { if (p.Error != null) retryReservations.Add(p.AutomaticRecoveryAttempts); }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    var marker = plan.Chats[0].ContinueMessageId;
    Check(failures + " failed launch(es) complete in the original invocation", await coordinator.RunAsync(plan, CancellationToken.None));
    Check(failures + " retries reopen without repeating shutdown, backup or credential application",
        backend.Launches == failures + 1 && backend.Closes == 1 && backend.Names == 1 && backend.Applies == 1);
    Check(failures + " retries retain one Continue and clear Recover", backend.Resumes == 1 && backend.LastMarker == marker && saved == null);
    Check(failures + " retries reserve their persisted budget before continuing", retryReservations.SequenceEqual(Enumerable.Range(1, failures)));
}
foreach (var failure in new[] { "launch-always", "apply", "cancel-after-close" })
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
    if (failure == "launch-always") Check("Persistent launch failure stops after the initial attempt and exactly two retries",
        backend.Launches == 3 && saved.AutomaticRecoveryAttempts == 2 && saved.Error == "Synthetic launch failure");
    backend.Failure = ""; backend.Cancel = null;
    Check(failure + ": serialized checkpoint resumes", await coordinator.RunAsync(saved, CancellationToken.None));
    Check(failure + ": same Continue identity survives retry", backend.LastMarker == marker && backend.Resumes == 1);
    Check(failure + ": completed plan cleared", saved == null);
}
foreach (var failure in new[] { "name", "close", "apply", "apply-after-write" })
{
    var backend = new FakeBackend {
        NameFailures = failure == "name" ? 1 : 0, CloseFailures = failure == "close" ? 1 : 0,
        ApplyFailures = failure == "apply" ? 1 : 0, Failure = failure == "apply-after-write" ? failure : "" };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    Check(failure + ": transient failure finishes without manual Recover", await coordinator.RunAsync(plan, CancellationToken.None) && plan.AutomaticRecoveryAttempts == 1);
    Check(failure + ": retries preserve one credential application and Continue", backend.Applies == 1 && backend.Resumes == 1);
    if (failure == "apply-after-write") Check("An uncertain successful account write is reconciled before retrying", backend.ApplyCalls == 1);
}
{
    var backend = new FakeBackend { CloseFailures = 1, LaunchFailures = 3 };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (IOException) { }
    Check("Shutdown and launch share a total two-retry budget", backend.Closes == 2 && backend.Launches == 2 && plan.AutomaticRecoveryAttempts == 2 && backend.Resumes == 0);
}
{
    var backend = new FakeBackend { Failure = "close" }; AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (IOException) { }
    Check("Unconfirmed shutdown retains recovery after two retries without applying or launching", saved?.DesktopRestartRequired == true && backend.Closes == 3 && backend.Applies == 0 && backend.Launches == 0 && backend.Resumes == 0);
}
{
    using var cancellation = new CancellationTokenSource();
    var backend = new FakeBackend { Failure = "cancel-after-close", Cancel = cancellation }; AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, cancellation.Token); plan.Automatic = true;
    try { await coordinator.RunAsync(plan, cancellation.Token); } catch (OperationCanceledException) { }
    Check("Explicit automatic-switch cancellation reopens the source but prevents a later automatic retry", saved?.AutomaticRecoveryAttempts == AccountSwitchCoordinator.MaximumRecoveryAttempts && backend.Current == "source" && backend.Launches == 1 && backend.Applies == 0 && backend.Resumes == 0);
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
    var backend = new FakeBackend { Current = "target", LaunchFailures = 1 };
    AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = new AccountSwitchPlan { SourceAccountKey = "source", TargetAccountKey = "target", AccountApplied = true, Phase = SwitchPhase.Resuming };
    Check("A later recovery completes its retry without applying credentials again", await coordinator.RunAsync(plan, CancellationToken.None) && backend.Launches == 2 && backend.Applies == 0 && saved == null);
}
{
    var backend = new FakeBackend { LaunchFailures = 1, LaunchError = new TimeoutException("Synthetic discovery timeout") };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    Check("Desktop discovery timeouts are retried in the same switch", await coordinator.RunAsync(plan, CancellationToken.None) && backend.Launches == 2);
}
{
    var backend = new FakeBackend { LaunchFailures = 3, LaunchError = new InvalidDataException("Synthetic trust refusal") };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (InvalidDataException) { }
    Check("Integrity refusals are not retried or followed by an extra launcher call", backend.Launches == 1 && plan.AutomaticRecoveryAttempts == 0 && backend.Resumes == 0);
}
{
    var backend = new FakeBackend { LaunchFailures = 3 }; AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, p => { if (p.AutomaticRecoveryAttempts == 1) backend.Current = "unrelated"; }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (InvalidOperationException) { }
    Check("A changed account blocks the next retry without overwriting credentials", backend.Launches == 1 && backend.Applies == 1 && backend.Current == "unrelated" && saved != null && backend.Resumes == 0);
}
{
    using var cancellation = new CancellationTokenSource();
    var backend = new FakeBackend { LaunchFailures = 3 };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, p => { if (p.AutomaticRecoveryAttempts == 1) cancellation.Cancel(); }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, cancellation.Token); plan.Automatic = true;
    try { await coordinator.RunAsync(plan, cancellation.Token); } catch (OperationCanceledException) { }
    Check("Cancellation during the retry wait prevents another switch attempt and disables restart recovery",
        backend.Launches == 2 && plan.AutomaticRecoveryAttempts == 2 && backend.Resumes == 0);
}
{
    var backend = new FakeBackend { Current = "target", Failure = "launch-always" };
    AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = new AccountSwitchPlan { SourceAccountKey = "source", TargetAccountKey = "target", AccountApplied = true, Phase = SwitchPhase.Launching, AutomaticRecoveryAttempts = 1 };
    try { await coordinator.RunAsync(plan, CancellationToken.None); } catch (IOException) { }
    Check("A serialized retry budget permits only its remaining retry", backend.Launches == 2 && saved?.AutomaticRecoveryAttempts == 2);
}
{
    var backend = new FakeBackend { ResumeFailures = 2 }; AccountSwitchPlan? saved = null;
    var coordinator = new AccountSwitchCoordinator(backend, p => saved = p, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    var done = new SwitchChat { Id = "completed", ResumeState = ChatResumeState.Completed };
    plan.Chats.Add(done);
    Check("A transient chat-open failure also completes without exposing Recover", await coordinator.RunAsync(plan, CancellationToken.None) && saved == null);
    Check("Retrying chat-open never repeats the account switch or an already completed chat", backend.Applies == 1 && backend.Resumes == 1 && backend.ResumeCalls == 3 && done.ResumeState == ChatResumeState.Completed);
}
{
    var backend = new FakeBackend { AmbiguousContinue = true };
    var coordinator = new AccountSwitchCoordinator(backend, _ => { }, _ => { }, e => e.Message);
    var plan = await coordinator.PrepareAsync("target", now, CancellationToken.None);
    var marker = plan.Chats[0].ContinueMessageId;
    Check("An ambiguous Continue remains pending after two observation retries", !await coordinator.RunAsync(plan, CancellationToken.None) && plan.AutomaticRecoveryAttempts == 2);
    Check("Observation retries never resend an ambiguous Continue", backend.ResumeCalls == 3 && backend.Resumes == 1 && plan.Chats[0].ContinueMessageId == marker && plan.Chats[0].ResumeState == ChatResumeState.Unconfirmed);
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
    var forecastBytes = File.ReadAllBytes(vault.FilePath);
    Check("Reading a next target does not reserve or change persisted data", AccountAutomation.Next(service, now)?.Key == second.Key && forecastBytes.SequenceEqual(File.ReadAllBytes(vault.FilePath)));
    vault.Data.PendingSwitch = new() { TargetAccountKey = original.Key };
    Check("A pending recovery remains the displayed next target", AccountAutomation.Next(service, now)?.Key == original.Key);
    vault.Data.PendingSwitch = null;
    Check("Successful reservation respects ranking and saves the cooldown", AccountAutomation.Reserve(service, false, now)?.Key == second.Key && new AccountVault(vault.DirectoryPath).Data.LastAutomaticSwitchAttempt == now);
    Check("Timer re-entry cannot repeat a reservation", AccountAutomation.Reserve(service, false, now.AddSeconds(1)) == null);
    vault.Data.AutomaticSwitchEnabled = true; vault.Data.AutomaticSwitchThreshold = 15; vault.Data.LastAutomaticSwitchAttempt = now;
    vault.Save(); var reopened = new AccountVault(vault.DirectoryPath);
    Check("Encrypted preferences and cooldown survive restart", reopened.Data.AutomaticSwitchEnabled && reopened.Data.AutomaticSwitchThreshold == 15 && reopened.Data.LastAutomaticSwitchAttempt == now);
    AccountAutomation.Pause(service);
    Check("Failure pause persists without losing accounts", !new AccountVault(vault.DirectoryPath).Data.AutomaticSwitchEnabled && vault.Data.Accounts.Count == 2);
    CreditProtectionContracts.Service(Check, service, original, second, now);
}
finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, scope = "Fictional account files and backend only; no real Codex process, browser, login or chat touched." }, new JsonSerializerOptions { WriteIndented = true }));

sealed class FakeBackend : IAccountSwitchBackend
{
    public string Current = "source", Failure = "";
    public CancellationTokenSource? Cancel;
    public int Launches, Applies, Resumes, Closes, Names, LaunchFailures, ResumeFailures, ResumeCalls, CloseFailures, NameFailures, ApplyFailures, ApplyCalls;
    public Exception? LaunchError;
    public bool AmbiguousContinue;
    public string? LastMarker;
    public Task<string?> CurrentAccountAsync(CancellationToken cancellation) => Task.FromResult<string?>(Current);
    public Task<SwitchChat[]> CaptureAsync(CancellationToken cancellation) => Task.FromResult(new[] { new SwitchChat { Id = "chat", OriginalTitle = "Fictional work" } });
    public int TargetValidations;
    public Task ValidateChatsForTargetAsync(SwitchChat[] chats, string target, CancellationToken cancellation) { TargetValidations++; if (Failure == "target-refused") throw new InvalidDataException("Synthetic workspace refusal"); return Task.CompletedTask; }
    public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken cancellation) { Names++; if (Names <= NameFailures) throw new IOException("Synthetic name failure"); return Task.CompletedTask; }
    public Task CloseDesktopAsync(CancellationToken cancellation) { Closes++; if (Failure == "close" || Closes <= CloseFailures) throw new IOException("Synthetic shutdown remains incomplete"); if (Failure == "cancel-after-close") Cancel!.Cancel(); return Task.CompletedTask; }
    public Task ApplyAccountAsync(string key, string? source, CancellationToken cancellation) { ApplyCalls++; if (Failure == "apply" || ApplyCalls <= ApplyFailures) throw new IOException("Synthetic apply failure"); Current = key; Applies++; if (Failure == "apply-after-write" && ApplyCalls == 1) throw new IOException("Synthetic failure after writing credentials"); return Task.CompletedTask; }
    public Task LaunchDesktopAsync(CancellationToken cancellation) { Launches++; if (Failure == "launch-always" || Launches <= LaunchFailures) throw LaunchError ?? new IOException("Synthetic launch failure"); return Task.CompletedTask; }
    public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken cancellation)
    {
        ResumeCalls++;
        if (ResumeCalls <= ResumeFailures) throw new IOException("Synthetic chat-open failure");
        // Match the production backend's observation-only policy after ambiguous delivery.
        if (SwitchContinuation.Decide(chat, new(chat.Id, "owner", "idle", false, null, null, "resumed", null, null, null)) is ChatResumeState decided) return Task.FromResult(decided);
        sending(); Resumes++; LastMarker = chat.ContinueMessageId;
        if (AmbiguousContinue) throw new IOException("Synthetic lost Continue response");
        return Task.FromResult(ChatResumeState.Resumed);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
