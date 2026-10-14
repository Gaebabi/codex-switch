using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class Diagnostics
{
    private static string? _originalAuthHash;
    private static bool _desktopWasRunning;
    public static void BeginReadOnlyProbe(AccountService service)
    {
        _originalAuthHash = File.Exists(service.AuthPath) ? SecureFiles.Hash(File.ReadAllText(service.AuthPath)) : null;
        _desktopWasRunning = System.Diagnostics.Process.GetProcessesByName("ChatGPT").Any() || System.Diagnostics.Process.GetProcessesByName("Codex").Any();
    }
    public static void WriteReadOnlyReport(string path, AccountService service, object? interaction = null)
    {
        var accounts = service.Vault.Data.Accounts;
        var currentHash = File.Exists(service.AuthPath) ? SecureFiles.Hash(File.ReadAllText(service.AuthPath)) : null;
        var report = new
        {
            version = typeof(Program).Assembly.GetName().Version!.ToString(3), checkedAtUtc = DateTimeOffset.UtcNow, accountCount = accounts.Count, language = Loc.Language, interaction,
            runtimeLoadedFrom = typeof(object).Assembly.Location,
            currentAccountCount = accounts.Count(a => a.IsCurrent), sourceAuthUnchanged = _originalAuthHash == currentHash,
            desktopWasRunning = _desktopWasRunning, desktopStillRunning = RuntimeLocator.IsDesktopRunning(service.Installation.DesktopPath),
            vaultIsEncrypted = File.Exists(service.Vault.FilePath) && !Encoding.UTF8.GetString(File.ReadAllBytes(service.Vault.FilePath)).Contains("access_token", StringComparison.Ordinal),
            temporaryHomesRemaining = Directory.Exists(Path.Combine(service.Vault.DirectoryPath, "runtime")) ? Directory.GetDirectories(Path.Combine(service.Vault.DirectoryPath, "runtime")).Length : 0,
            accounts = accounts.Select(a => new { a.IsCurrent, a.Plan, hasSubscriptionEnd = a.SubscriptionActiveUntil.HasValue, hasEmail = a.Email.Length > 0, success = a.Usage != null && a.Error == null,
                error = a.Error, checkedAtUtc = a.Usage?.CheckedAt, buckets = a.Usage?.Buckets.Select(b => new { b.Id, b.Primary, b.Secondary }),
                hasCreditBalance = a.Usage?.Buckets.Any(b => b.CreditBalance != null), a.Usage?.AvailableResets,
                history = a.History.Select(t => new { t.BucketId, t.Slot, t.Minutes, samples = t.Points.Count, firstAt = t.Points.FirstOrDefault()?.At,
                    lastAt = t.Points.LastOrDefault()?.At }) })
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
    public static object ProbeWindowFrame(System.Windows.Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var nativeCorners = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        uint corners = 0, visibleBorderThickness = 0;
        if (nativeCorners)
        {
            DwmGetWindowAttribute(handle, 33, out corners, sizeof(uint));
            DwmGetWindowAttribute(handle, 37, out visibleBorderThickness, sizeof(uint));
        }
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            return new { nativeCorners, cornerPreference = corners, visibleBorderThickness, appliedBorderColor = WindowFrame.AppliedBorderColor(window), isActive = window.IsActive,
                hasCustomRegion = GetWindowRgn(handle, region) != 0, windowState = window.WindowState.ToString(),
                allowsTransparency = window.AllowsTransparency,
                glassFrame = System.Windows.Shell.WindowChrome.GetWindowChrome(window).GlassFrameThickness.Left };
        }
        finally { DeleteObject(region); }
    }
    [DllImport("dwmapi.dll", PreserveSig = false)] private static extern void DwmGetWindowAttribute(IntPtr handle, int attribute, out uint value, uint size);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);

    public static int RunSelfTests(string reportPath)
    {
        Loc.SetLanguage("ko");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "self-test-" + Guid.NewGuid().ToString("N"));
        SecureFiles.CreatePrivateDirectory(directory);
        var tests = new List<object>();
        var failureCount = 0;
        void Test(string name, Action test)
        {
            try { test(); tests.Add(new { name, passed = true }); }
            catch (Exception error) { failureCount++; tests.Add(new { name, passed = false, failure = error.Message }); }
        }
        void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        void MustFail(Action operation) { var failed = false; try { operation(); } catch (InvalidOperationException) { failed = true; } if (!failed) throw new InvalidOperationException("Operation unexpectedly succeeded"); }
        var a = FakeAuth("alice", "a@example.test", "workspace");
        var b = FakeAuth("bob", "b@example.test", "workspace");
        var profileA = AuthDocument.Parse(a);
        var profileB = AuthDocument.Parse(b);
        Test("Card credits distinguish zero, missing, unlimited, and compact large balances", () =>
        {
            Assert(AccountTankCard.CreditLabel(new() { CreditBalance = "0" }) == "크레딧 0", "Zero balance was hidden");
            Assert(AccountTankCard.CreditLabel(null) == "크레딧 —" && AccountTankCard.CreditLabel(new() { UnlimitedCredits = true }) == "크레딧 ∞", "Missing or unlimited credit was invented");
            Assert(AccountTankCard.CreditLabel(new() { CreditBalance = "1250.50" }) == "크레딧 1,250.5" && AccountTankCard.CreditLabel(new() { CreditBalance = "12500" }) == "크레딧 12.5K", "Balance formatting lost magnitude");
        });
        Test("Drive selections preserve custom labels and order through encrypted reload", () =>
        {
            var vault = new AccountVault(Path.Combine(directory, "drive-settings"));
            Assert(vault.Data.Drives.Select(d => d.Root).SequenceEqual(new[] { "C:\\", "D:\\" }), "Default drive selection changed");
            vault.Data.Drives = new() { new() { Root = "D:\\", Label = "작업" }, new() { Root = "C:\\", Label = "시스템" } }; vault.Save();
            var reloaded = new AccountVault(vault.DirectoryPath);
            Assert(reloaded.Data.Drives.Select(d => d.Label).SequenceEqual(new[] { "작업", "시스템" }), "Labels or order were lost");
            reloaded.Data.Drives.Clear(); reloaded.Save(); Assert(new AccountVault(vault.DirectoryPath).Data.Drives.Count == 0, "Disabled drives were reset to defaults");
            Assert(new DriveReading("C:\\", "C:", 25, 100).FreePercent == 25 && new DriveReading("C:\\", "C:", null, null).FreePercent == null, "Free percentage or unavailable state changed");
        });
        HardwareUsage HardwareFrame(DateTimeOffset at, double? cpu, double? memory = 20, double? gpu = 30, double? vram = 40) =>
            new(at, new(cpu), new(memory), new(gpu), new(vram), Array.Empty<GpuResource>(), 0);
        Test("Hardware history stores real collection times, deduplicates snapshots, and bounds the recent window", () =>
        {
            var history = new HardwareHistory(); var at = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
            var first = history.Observe(HardwareFrame(at, 0)); HardwareUsage last = first;
            for (var i = 1; i <= 70; i++) last = history.Observe(HardwareFrame(at.AddSeconds(i * 2), i));
            Assert(last.History!.Length == 61 && last.History[0].At == at.AddSeconds(20) && last.History[^1].At == at.AddSeconds(140), "Recent window retained stale or lost actual samples");
            Assert(history.Observe(last).History!.Length == 61 && first.History!.Length == 1, "Duplicate refresh added data or mutated an earlier snapshot");
        });
        Test("Missing hardware samples and collection gaps break trend lines without inventing zero", () =>
        {
            var history = new HardwareHistory(); var at = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
            history.Observe(HardwareFrame(at, 10)); history.Observe(HardwareFrame(at.AddSeconds(2), null));
            history.Observe(HardwareFrame(at.AddSeconds(4), 20)); var last = history.Observe(HardwareFrame(at.AddSeconds(20), 30, 0, double.NaN));
            Assert(last.History![1].Cpu == null && last.History[^1].Gpu == null && last.History[^1].Memory == 0, "Unknown became zero or real idle was discarded");
            var segments = ResourceTrend.Segments(last.History.Select(p => new ResourceTrendPoint(p.At, p.Cpu)));
            Assert(segments.Length == 3 && segments.All(s => s.Length == 1), "Graph bridged missing or unobserved intervals");
        });
        Test("A system-clock rollback starts a new hardware timeline rather than reversing the graph", () =>
        {
            var history = new HardwareHistory(); var at = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
            history.Observe(HardwareFrame(at, 10)); history.Observe(HardwareFrame(at.AddSeconds(2), 20));
            var rolledBack = history.Observe(HardwareFrame(at.AddMinutes(-1), 30));
            Assert(rolledBack.History!.Length == 1 && rolledBack.History[0].Cpu == 30, "Future samples survived a clock rollback");
        });
        Test("Physical memory percentages use 64-bit capacity and keep invalid data unavailable", () =>
        {
            Assert(HardwareMath.Percentage(8589934592, 17179869184) == 50, "Large memory capacity overflowed");
            Assert(HardwareMath.Percentage(0, 100) == 0 && HardwareMath.Percentage(1, 0) == null && HardwareMath.Percentage(101, 100) == null, "Unknown capacity became zero or impossible memory was accepted");
        });
        Test("GPU sums processes on the same engine and reports the busiest engine", () =>
        {
            var usage = HardwareMath.EngineUsage(new[] { ("pid_1_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D", 30.0),
                ("pid_2_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D", 40.0), ("pid_1_luid_0x00000000_0x00001234_phys_0_eng_1_engtype_Copy", 80.0) });
            Assert(usage["00000000:00001234"] == 80, "GPU engines were averaged or summed together");
        });
        Test("GPU physical nodes and adapters do not inflate one another's utilization", () =>
        {
            var usage = HardwareMath.EngineUsage(new[] { ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 60.0),
                ("pid_2_luid_0x0_0x1_phys_1_eng_0_engtype_Compute", 70.0), ("pid_3_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 20.0) });
            Assert(usage.Count == 2 && usage["00000000:00000001"] == 70 && usage["00000000:00000002"] == 20, "Nodes or adapters were combined");
        });
        Test("GPU instance parsing matches DXGI LUIDs and invalid rates stay unavailable", () =>
        {
            Assert(HardwareMath.ParseInstance("luid_0xABCDEF01_0x00001234_phys_0")?.Adapter == "abcdef01:00001234", "LUID casing or padding mismatched capacity");
            var usage = HardwareMath.EngineUsage(new[] { ("unknown", 50.0), ("pid_1_luid_0x0_0x1_phys_0_eng_0", double.NaN), ("pid_2_luid_0x0_0x1_phys_0_eng_0", -1.0) });
            Assert(usage.Count == 0, "Missing/invalid GPU data became a valid zero");
        });
        Test("Resource warnings enter at 70/85 and clear with five-point hysteresis", () =>
        {
            Assert(HardwareMath.Pressure(69.9, ResourcePressure.Normal) == ResourcePressure.Normal && HardwareMath.Pressure(70, ResourcePressure.Normal) == ResourcePressure.Warning, "Warning entry threshold changed");
            Assert(HardwareMath.Pressure(85, ResourcePressure.Warning) == ResourcePressure.Critical && HardwareMath.Pressure(80, ResourcePressure.Critical) == ResourcePressure.Critical, "Critical threshold or hold failed");
            Assert(HardwareMath.Pressure(79.9, ResourcePressure.Critical) == ResourcePressure.Warning && HardwareMath.Pressure(65, ResourcePressure.Warning) == ResourcePressure.Warning && HardwareMath.Pressure(64.9, ResourcePressure.Warning) == ResourcePressure.Normal, "Release thresholds flicker");
        });
        Test("Unknown resource samples clear stale warning state and remain distinct from zero", () =>
        {
            Assert(HardwareMath.Pressure(null, ResourcePressure.Critical) == ResourcePressure.Unknown && HardwareMath.Pressure(double.PositiveInfinity, ResourcePressure.Normal) == ResourcePressure.Unknown, "Invalid value retained a warning");
            Assert(HardwareMath.Pressure(0, ResourcePressure.Unknown) == ResourcePressure.Normal && HardwareMath.Pressure(101, ResourcePressure.Normal) == ResourcePressure.Unknown, "Valid idle or impossible sample was misclassified");
        });
        JsonElement SessionSnapshot(string type, long revision = 1, string[]? flags = null, string? parent = null, bool side = false, string? goal = null) =>
            JsonSerializer.SerializeToElement(new { type = "snapshot", revision, conversationState = new { parentThreadId = parent, sideConversation = side,
                threadRuntimeStatus = new { type, activeFlags = flags ?? Array.Empty<string>() }, threadGoal = goal == null ? null : new { status = goal } } });
        JsonElement SessionPatch(long before, long after, object[] patches) => JsonSerializer.SerializeToElement(new { type = "patches", baseRevision = before, revision = after, patches });
        Test("Desktop counts deduplicate snapshots, ignore other owners, and exclude child chats", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.Discover("child", "owner");
            ledger.Apply("root", "owner", SessionSnapshot("active")); ledger.Apply("root", "owner", SessionSnapshot("active"));
            ledger.Apply("root", "other", SessionSnapshot("idle")); ledger.Apply("child", "owner", SessionSnapshot("active", parent: "root")); ledger.CompleteDiscovery();
            ledger.Apply("root", "owner", SessionSnapshot("idle", 0));
            Assert(ledger.Read() is { Available: true, Running: 1, Observed: 1 } && ledger.OwnsClient("owner") && !ledger.OwnsClient("other"), "Duplicate, foreign owner, or child changed the total");
        });
        Test("Approval and user input waits are separated from actively running chats", () =>
        {
            var ledger = new SessionActivityLedger();
            foreach (var flag in new[] { "waitingOnApproval", "waitingOnUserInput" }) { ledger.Discover(flag, "owner"); ledger.Apply(flag, "owner", SessionSnapshot("active", flags: new[] { flag })); }
            ledger.CompleteDiscovery(); Assert(ledger.Read() is { Available: true, Running: 0, Waiting: 2 }, "Waiting chats were counted as running");
        });
        Test("Missing snapshots and disconnects stay unavailable, while verified empty is zero", () =>
        {
            var ledger = new SessionActivityLedger(); Assert(!ledger.Read().Available, "An unconnected monitor fabricated zero");
            ledger.CompleteDiscovery(); Assert(ledger.Read() is { Available: true, Running: 0 }, "Verified empty was hidden");
            ledger.Discover("root", "owner"); Assert(!ledger.Read().Available, "An incomplete discovery fabricated a count");
            ledger.Apply("root", "owner", SessionSnapshot("active")); Assert(ledger.Read().Running == 1, "Snapshot did not complete discovery");
            ledger.Reset(); Assert(!ledger.Read().Available && ledger.Read().Running == 0, "Disconnect retained an active count");
        });
        Test("Runtime transitions and full active flags update the count without history heuristics", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery(); ledger.Apply("root", "owner", SessionSnapshot("active"));
            ledger.Apply("root", "owner", SessionPatch(1, 2, new object[] { new { op = "replace", path = new[] { "threadRuntimeStatus", "activeFlags" }, value = new[] { "waitingOnApproval" } } }));
            Assert(ledger.Read() is { Running: 0, Waiting: 1 }, "Flag change did not update waiting count");
            ledger.Apply("root", "owner", SessionPatch(2, 3, new object[] { new { op = "replace", path = new[] { "threadRuntimeStatus" }, value = new { type = "idle" } } }));
            Assert(ledger.Read() is { Available: true, Running: 0, Waiting: 0 }, "Completed turn remained active");
            ledger.Remove("root"); Assert(ledger.Read().Observed == 0, "Removed thread remained observed");
        });
        Test("Missing patch revisions invalidate the count until a fresh snapshot arrives", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery(); ledger.Apply("root", "owner", SessionSnapshot("active"));
            Assert(ledger.Apply("root", "owner", SessionPatch(4, 5, Array.Empty<object>())) && !ledger.Read().Available, "Revision gap was ignored");
            ledger.Apply("root", "owner", SessionSnapshot("idle", 5)); Assert(ledger.Read() is { Available: true, Running: 0 }, "Fresh snapshot did not repair revision gap");
        });
        Test("Unrelated chat-content patches only advance revision; side conversations are excluded", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery(); ledger.Apply("root", "owner", SessionSnapshot("active"));
            ledger.Apply("root", "owner", SessionPatch(1, 2, new object[] { new { op = "replace", path = new[] { "title" }, value = "synthetic title" } }));
            Assert(ledger.Read().Running == 1 && !ledger.Apply("root", "owner", SessionPatch(2, 3, Array.Empty<object>())), "Unrelated patch altered status or lost revision");
            ledger.Apply("root", "owner", SessionSnapshot("active", 4, side: true)); Assert(ledger.Read().Running == 0, "Side conversation was included");
        });
        Test("Unsupported runtime statuses fail explicitly instead of becoming zero", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); var failed = false;
            try { ledger.Apply("root", "owner", SessionSnapshot("future-state")); } catch (InvalidDataException) { failed = true; }
            Assert(failed, "Unsupported status was accepted");
        });
        Test("Active goals overlap running chats and exclude child goals", () =>
        {
            var ledger = new SessionActivityLedger();
            ledger.Discover("root", "owner"); ledger.Discover("idle", "owner"); ledger.Discover("child", "owner");
            ledger.Apply("root", "owner", SessionSnapshot("active", goal: "active"));
            ledger.Apply("idle", "owner", SessionSnapshot("idle", goal: "active"));
            ledger.Apply("child", "owner", SessionSnapshot("active", parent: "root", goal: "active")); ledger.CompleteDiscovery();
            Assert(ledger.Read() is { Running: 1, Goals: 2, Observed: 2 }, "Goals were treated as exclusive running states or counted twice");
        });
        Test("Goal patches handle pause, completion, clearing, and duplicate delivery", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery();
            ledger.Apply("root", "owner", SessionSnapshot("active", goal: "active"));
            var pause = SessionPatch(1, 2, new object[] { new { op = "replace", path = new[] { "threadGoal", "status" }, value = "paused" } });
            ledger.Apply("root", "owner", pause); ledger.Apply("root", "owner", pause);
            Assert(ledger.Read() is { Available: true, Running: 1, Goals: 0, PausedGoals: 1 }, "Duplicate or pause changed running status");
            ledger.Apply("root", "owner", SessionPatch(2, 3, new object[] { new { op = "replace", path = new[] { "threadGoal" }, value = new { status = "complete" } } }));
            Assert(ledger.Read() is { Goals: 0, PausedGoals: 0 }, "Completed goal remained counted");
            ledger.Apply("root", "owner", SessionPatch(3, 4, new object[] { new { op = "remove", path = new[] { "threadGoal" } } }));
            Assert(ledger.Read() is { Available: true, Goals: 0 }, "Clearing a goal invalidated runtime status");
        });
        Test("Limited goals remain separate and unsupported goal status stays unknown", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery();
            foreach (var status in new[] { "blocked", "budgetLimited", "usageLimited" })
            {
                ledger.Apply("root", "owner", SessionSnapshot("idle", goal: status));
                Assert(ledger.Read() is { Goals: 0, PausedGoals: 1 }, "Limited goal was active or lost");
            }
            ledger.Apply("root", "owner", SessionSnapshot("idle", goal: "future-goal"));
            Assert(ledger.Read() is { Available: true, Goals: null }, "Unsupported goal status fabricated zero");
        });
        Test("Queue messages are deduplicated, retain paused messages, and exclude explicit remote hosts", () =>
        {
            var messages = JsonSerializer.SerializeToElement(new object[] {
                new { id = "a" }, new { id = "a", pausedReason = "synthetic pause" },
                new { id = "b", submission = new { hostId = "local" } },
                new { id = "c", submission = new { hostId = "remote" } },
                new { id = "d", submissionOptions = new { executionHostId = "remote" } },
                new { id = "e", submission = new { hostId = "local" }, submissionOptions = new { executionHostId = "remote" } } });
            Assert(DesktopQueueState.Count(messages) is { Total: 2, Paused: 1 }, "Queue duplicated IDs, lost pauses, or included remote messages");
        });
        Test("Saved queue parsing distinguishes missing keys, empty queues, and malformed data", () =>
        {
            Assert(DesktopQueueState.Parse(JsonSerializer.SerializeToElement(new { })).Count == 0, "Missing default queue did not match desktop semantics");
            var id = Guid.NewGuid().ToString();
            var state = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["queued-follow-ups"] = new Dictionary<string, object> { [id] = new[] { new { id = "message" } } } });
            Assert(DesktopQueueState.Parse(state)[id].Length == 1, "Saved queue did not load");
            var failed = false;
            try { DesktopQueueState.Parse(JsonSerializer.SerializeToElement(1)); }
            catch (InvalidDataException) { failed = true; }
            Assert(failed, "Invalid state became zero");
            failed = false; try { DesktopQueueState.Count(JsonSerializer.SerializeToElement(new[] { new { text = "synthetic" } })); }
            catch (InvalidDataException) { failed = true; } Assert(failed, "Missing message ID became zero");
            failed = false; try { DesktopQueueState.Count(JsonSerializer.SerializeToElement(new[] { new { id = "a", submission = new { hostId = 7 } } })); }
            catch (InvalidDataException) { failed = true; } Assert(failed, "Invalid host was treated as local");
        });
        Test("Queue updates replace totals, ignore foreign owners, and exclude known children", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.Discover("child", "owner"); ledger.CompleteDiscovery();
            ledger.Apply("root", "owner", SessionSnapshot("active")); ledger.Apply("child", "owner", SessionSnapshot("active", parent: "root"));
            ledger.SetQueues(new(new() { ["root"] = new[] { new DesktopQueueMessage("a", Paused: true), new("b") },
                ["child"] = new[] { new DesktopQueueMessage("a"), new("b"), new("c") }, ["unloaded"] = new[] { new DesktopQueueMessage("a", Paused: true) } }, new()), ledger.BeginQueueRead());
            Assert(ledger.Read() is { Queued: 3, PausedQueued: 2 }, "Child queue counted or saved unloaded queue lost");
            ledger.ApplyQueue("root", "other", JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Assert(ledger.Read().Queued == 3, "Foreign owner replaced queue");
            ledger.ApplyQueue("root", "owner", JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Assert(ledger.Read().Queued == 1, "Cleared queue remained counted");
        });
        Test("Queue failures do not hide running chats, and disconnect clears all totals", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery(); ledger.Apply("root", "owner", SessionSnapshot("active", goal: "active"));
            ledger.SetQueues(null, ledger.BeginQueueRead(), "synthetic queue read failure");
            Assert(ledger.Read() is { Available: true, Running: 1, Queued: null, Goals: 1 }, "Queue failure hid valid runtime state or fabricated zero");
            ledger.SetQueues(new(new(), new()), ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 0, "Verified empty queue did not display zero");
            ledger.Reset(); Assert(ledger.Read() is { Available: false, Queued: null, Goals: null }, "Disconnect left stale totals");
        });
        Test("Server queue merges by submission and client identity without merging different threads", () =>
        {
            var saved = new[] { new DesktopQueueMessage("client-a"), new("server-b"), new("legacy-only", Paused: true) };
            var server = new[] { new DesktopQueueMessage("server-a", "client-a"), new("server-b", "client-b") };
            Assert(DesktopQueueState.Count(saved, server) is { Total: 3, Paused: 1 }, "Cross-source duplicates inflated the queue");
            var ledger = new SessionActivityLedger();
            ledger.SetQueues(new(new() { ["one"] = saved, ["two"] = new[] { new DesktopQueueMessage("client-a") } }, new() { ["one"] = server }), ledger.BeginQueueRead());
            Assert(ledger.Read().Queued == 4, "Same ID on different threads was lost");
            Assert(DesktopQueueState.Count(Array.Empty<DesktopQueueMessage>(), new[] { new DesktopQueueMessage("x", "same"), new("y", "same") }).Total == 2,
                "Distinct server submissions with the same client ID were collapsed");
        });
        Test("Server removals and verified empty snapshots immediately reduce the queue", () =>
        {
            var ledger = new SessionActivityLedger();
            ledger.SetQueues(new(new(), new() { ["root"] = new[] { new DesktopQueueMessage("a"), new("b") } }), ledger.BeginQueueRead());
            Assert(ledger.Read().Queued == 2, "Server queue was omitted");
            ledger.SetQueues(new(new(), new() { ["root"] = new[] { new DesktopQueueMessage("b") } }), ledger.BeginQueueRead());
            Assert(ledger.Read().Queued == 1, "Started or deleted submission remained counted");
            ledger.SetQueues(new(new(), new()), ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 0, "Empty server queue stayed stale");
        });
        Test("Owner queue broadcasts survive stale disk polls until disk catches up", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.Apply("root", "owner", SessionSnapshot("active"));
            var old = new DesktopQueueSnapshot(new(), new()); ledger.SetQueues(old, ledger.BeginQueueRead());
            var started = ledger.BeginQueueRead();
            var added = JsonSerializer.SerializeToElement(new[] { new { id = "a" } }); ledger.ApplyQueue("root", "owner", added);
            ledger.SetQueues(old, started); Assert(ledger.Read().Queued == 1, "In-flight poll erased a new owner broadcast");
            ledger.SetQueues(old, ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 1, "Stale file erased the owner queue");
            var caughtUp = new DesktopQueueSnapshot(new() { ["root"] = DesktopQueueState.Messages(added) }, new());
            ledger.SetQueues(caughtUp, ledger.BeginQueueRead());
            ledger.SetQueues(old, ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 0, "Acknowledged override prevented subsequent disk updates");
        });
        Test("An empty owner broadcast survives delayed removal persistence and owner replacement", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner");
            var old = new DesktopQueueSnapshot(new() { ["root"] = new[] { new DesktopQueueMessage("a") } }, new());
            ledger.SetQueues(old, ledger.BeginQueueRead());
            ledger.ApplyQueue("root", "owner", JsonSerializer.SerializeToElement(Array.Empty<object>()));
            ledger.SetQueues(old, ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 0, "Deleted message reappeared from stale storage");
            ledger.Discover("root", "new-owner"); Assert(ledger.Read().Queued == 1, "Previous owner override survived ownership change");
            ledger.ApplyQueue("root", "owner", JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Assert(ledger.Read().Queued == 1, "Old owner replaced new ownership state");
        });
        Test("Queue source failure preserves runtime and waits for a complete recovery", () =>
        {
            var ledger = new SessionActivityLedger(); ledger.Discover("root", "owner"); ledger.CompleteDiscovery();
            ledger.Apply("root", "owner", SessionSnapshot("active", goal: "active"));
            ledger.ApplyQueue("root", "owner", JsonSerializer.SerializeToElement(new[] { new { id = "a" } }));
            Assert(ledger.Read() is { Available: true, Running: 1, Queued: null }, "Partial queue sources fabricated a complete count");
            ledger.SetQueues(new(new(), new()), ledger.BeginQueueRead()); Assert(ledger.Read().Queued == 1, "Valid broadcast was lost during recovery");
            ledger.Reset(); Assert(ledger.Read().Queued == null, "Disconnect preserved an owner override");
        });
        Test("Subscription countdown uses Korean calendar days across midnight and separates missing dates", () =>
        {
            var now = DateTimeOffset.Parse("2026-10-11T23:59:00+09:00");
            var until = DateTimeOffset.Parse("2026-10-13T00:30:00+09:00");
            Assert(AccountTankCard.SubscriptionCountdown(until, now) == "D-2", "Countdown used the wrong calendar day");
            Assert(AccountTankCard.SubscriptionCountdown(until, now.AddMinutes(2)) == "D-1", "Countdown did not change at Korean midnight");
            Assert(AccountTankCard.SubscriptionCountdown(now.AddSeconds(1), now) == "오늘", "Same-day end was not shown");
            Assert(AccountTankCard.SubscriptionCountdown(now.AddDays(-1), now) == "D+1", "Elapsed date was shown as future");
            Assert(AccountTankCard.SubscriptionCountdown(null, now) == "", "Missing date became an invented countdown");
        });
        Test("Subscription dates come from subscription claims rather than JWT expiration", () =>
        {
            var until = DateTimeOffset.Parse("2026-11-07T00:30:00+09:00");
            var checkedAt = until.AddDays(-20);
            var dated = AuthDocument.Parse(FakeAuth("subscription", "subscription@example.test", "workspace", until, checkedAt));
            Assert(dated.SubscriptionActiveUntil == until && dated.SubscriptionLastChecked == checkedAt, "Subscription dates were lost");
            Assert(dated.SubscriptionActiveUntil is DateTimeOffset actual && UsagePresentation.KoreanTime(actual).Day == 7, "Subscription day used the wrong timezone");
            Assert(profileA.SubscriptionActiveUntil == null && profileA.SubscriptionLastChecked == null, "JWT expiration became a subscription or payment date");
        });
        Test("Saved accounts derive subscription dates on load and update them with refreshed sign-in data", () =>
        {
            var until = DateTimeOffset.Parse("2026-11-07T00:30:00+09:00");
            var subscriptionVault = new AccountVault(Path.Combine(directory, "subscription-vault"));
            var dated = subscriptionVault.Upsert(FakeAuth("subscription", "subscription@example.test", "workspace", until, until.AddDays(-20)));
            subscriptionVault.Save();
            var loaded = new AccountVault(subscriptionVault.DirectoryPath);
            Assert(loaded.Data.Accounts.Single().SubscriptionActiveUntil == until, "Existing account did not load its subscription date");
            loaded.Upsert(FakeAuth("subscription", "subscription@example.test", "workspace", until.AddMonths(1), until.AddDays(1)));
            var view = new AccountService(loaded, directory).Accounts.Single();
            Assert(view.SubscriptionActiveUntil == until.AddMonths(1) && view.SubscriptionLastChecked == until.AddDays(1), "Refreshed subscription date did not reach the UI snapshot");
            loaded.Upsert(FakeAuth("subscription", "subscription@example.test", "workspace"));
            Assert(loaded.Data.Accounts.Single().SubscriptionActiveUntil == null, "Removed subscription date stayed cached");
        });
        Test("Different users in one workspace have separate profile identities", () => Assert(profileA.Key != profileB.Key, "Profile collision"));
        Test("API-key and incomplete login caches are rejected", () =>
        { MustFail(() => AuthDocument.Parse("{\"auth_mode\":\"apikey\"}")); MustFail(() => AuthDocument.Parse("{\"tokens\":{}}")); });
        var vault = new AccountVault(Path.Combine(directory, "vault"));
        Test("Windows DPAPI vault round-trip preserves Korean aliases and secrets stay encrypted", () =>
        {
            vault.Upsert(a).Alias = "개인 계정"; vault.Upsert(b); vault.Save();
            var bytes = File.ReadAllBytes(vault.FilePath);
            Assert(!Encoding.UTF8.GetString(bytes).Contains("test-refresh-alice", StringComparison.Ordinal), "Plaintext credential in vault");
            Assert(!Encoding.UTF8.GetString(bytes).Contains("access_token", StringComparison.Ordinal), "Plaintext JSON in vault");
            var loaded = new AccountVault(vault.DirectoryPath);
            Assert(loaded.Data.Accounts.Count == 2 && loaded.Data.Accounts[0].Alias == "개인 계정", "Vault round-trip failed");
        });
        Test("Missing quota windows remain unavailable instead of zero-percent usage", () =>
        {
            using var data = JsonDocument.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null}} ");
            var snapshot = UsageSnapshot.Parse(data.RootElement, "workspace");
            Assert(snapshot.LeastRemaining == null && snapshot.Buckets[0].Primary == null, "Missing information became a quota");
        });
        Test("Multiple buckets and reset-credit availability are parsed", () =>
        {
            using var data = JsonDocument.Parse("{\"accountId\":\"workspace\",\"rateLimits\":{},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":23,\"windowDurationMins\":300,\"resetsAt\":1792301909}},\"other\":{\"primary\":{\"usedPercent\":98,\"windowDurationMins\":10080}}},\"rateLimitResetCredits\":{\"availableCount\":2}}");
            var snapshot = UsageSnapshot.Parse(data.RootElement, "workspace");
            Assert(snapshot.Buckets.Count == 2 && snapshot.LeastRemaining == 2 && snapshot.AvailableResets == 2, "Wrong multi-bucket usage");
            Assert(snapshot.Buckets[0].Primary!.PeriodLabel == "5시간" && snapshot.Buckets[1].Primary!.PeriodLabel == "주간", "Quota windows were hard-coded");
        });
        Test("Wrong-account usage is rejected", () =>
        { using var data = JsonDocument.Parse("{\"accountId\":\"other\",\"rateLimits\":{}} "); MustFail(() => UsageSnapshot.Parse(data.RootElement, "workspace")); });
        Test("Backend denial survives a zero-percent quota", () =>
        { using var data = JsonDocument.Parse("{\"ordinaryUsageAllowed\":false,\"rateLimits\":{\"primary\":{\"usedPercent\":0}}}"); Assert(UsageSnapshot.Parse(data.RootElement, "workspace").OrdinaryUsageAllowed == false, "Backend denial lost"); });
        var fakeHome = Path.Combine(directory, "fake-codex-home"); Directory.CreateDirectory(fakeHome);
        var fakeAuth = Path.Combine(fakeHome, "auth.json"); SecureFiles.AtomicWriteText(fakeAuth, a);
        var service = new AccountService(vault, fakeHome);
        Test("A running desktop blocks credential replacement", () =>
        {
            MustFail(() => service.ApplySwitchAsync(profileB.Key, () => true, false, CancellationToken.None).GetAwaiter().GetResult());
            Assert(File.ReadAllText(fakeAuth) == a, "Live desktop credentials changed");
        });
        Test("A desktop restart during the switch blocks replacement", () =>
        {
            var calls = 0;
            MustFail(() => service.ApplySwitchAsync(profileB.Key, () => ++calls >= 2, false, CancellationToken.None).GetAwaiter().GetResult());
            Assert(File.ReadAllText(fakeAuth) == a, "Credentials changed during restart");
        });
        Test("A concurrent login-file change blocks replacement", () =>
        {
            var calls = 0; var changed = a + "\n";
            MustFail(() => service.ApplySwitchAsync(profileB.Key, () => { if (++calls == 2) SecureFiles.AtomicWriteText(fakeAuth, changed); return false; }, false, CancellationToken.None).GetAwaiter().GetResult());
            Assert(File.ReadAllText(fakeAuth) == changed, "Concurrent credentials overwritten"); SecureFiles.AtomicWriteText(fakeAuth, a);
        });
        Test("Switching in an isolated test home preserves the previous account and supports switching back", () =>
        {
            service.ApplySwitchAsync(profileB.Key, () => false, false, CancellationToken.None).GetAwaiter().GetResult();
            Assert(AuthDocument.Parse(File.ReadAllText(fakeAuth)).Key == profileB.Key, "Selected account was not installed");
            Assert(vault.Data.Accounts.Find(p => p.Key == profileA.Key)!.AuthJson == a, "Previous account lost");
            service.ApplySwitchAsync(profileA.Key, () => false, false, CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.ReadAllText(fakeAuth) == a && service.CurrentKey == profileA.Key, "Switch-back failed");
        });
        Test("Keyring configuration cannot silently receive a file-only switch", () =>
        {
            File.WriteAllText(Path.Combine(fakeHome, "config.toml"), "cli_auth_credentials_store = \"keyring\"\n");
            MustFail(() => service.CheckCredentialBackend()); File.Delete(Path.Combine(fakeHome, "config.toml"));
        });
        Test("Configured workspace and login-method requirements block incompatible account switches", () =>
        {
            var config = Path.Combine(fakeHome, "config.toml");
            File.WriteAllText(config, "forced_chatgpt_workspace_id = \"different-workspace\"\n");
            MustFail(() => service.ApplySwitchAsync(profileB.Key, () => false, false, CancellationToken.None).GetAwaiter().GetResult());
            Assert(File.ReadAllText(fakeAuth) == a, "Workspace mismatch changed the active cache");
            File.WriteAllText(config, "forced_login_method = \"api\"\n"); MustFail(() => service.CheckCredentialBackend(profileB));
            File.Delete(config);
        });
        Test("Reauthentication of the current account remains pending until explicit application", () =>
        {
            var renewed = a + "\n";
            var profile = vault.Upsert(renewed); profile.RequiresApply = true;
            service.SynchronizeCurrent();
            Assert(profile.AuthJson == renewed && File.ReadAllText(fakeAuth) == a, "New login was overwritten or silently applied");
            service.ApplySwitchAsync(profileA.Key, () => false, false, CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.ReadAllText(fakeAuth) == renewed && !profile.RequiresApply && vault.Data.PreviousDesktopAuthJson == a, "Pending login did not apply safely");
            SecureFiles.AtomicWriteText(fakeAuth, a); service.SynchronizeCurrent();
        });
        Test("Atomic writes preserve UTF-8 and leave no staging files", () =>
        {
            var file = Path.Combine(directory, "한글.txt"); SecureFiles.AtomicWriteText(file, "첫 번째"); SecureFiles.AtomicWriteText(file, "두 번째");
            Assert(File.ReadAllText(file) == "두 번째", "UTF-8 changed"); Assert(!Directory.GetFiles(directory, ".account-desk-*.tmp").Any(), "Staging file left behind");
        });
        Test("Runtime cleanup refuses paths outside the owned directory", () =>
        { var blocked = false; try { SecureFiles.DeleteOwnedRuntime(Path.Combine(directory, "runtime"), directory); } catch (IOException) { blocked = true; } Assert(blocked, "Unsafe recursive cleanup accepted"); });
        Test("Owned child processes terminate when their Windows job closes", () =>
        {
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe"))
            { Arguments = "127.0.0.1 -n 30", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            using (var job = new OwnedProcessJob(child)) { }
            Assert(child.WaitForExit(3000), "Owned child survived job close");
        });
        Test("Managed browser-login cancellation leaves the active cache and vault unchanged", () =>
        {
            var loginVault = new AccountVault(Path.Combine(directory, "login-vault"));
            var loginService = new AccountService(loginVault, fakeHome);
            loginService.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            using var cancel = new CancellationTokenSource(); var opened = false; var cancelled = false;
            try { loginService.AddAccountAsync(uri => { opened = true; cancel.Cancel(); }, cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(opened && cancelled && File.ReadAllText(fakeAuth) == a && loginVault.Data.Accounts.Count == 1 && !loginService.IsBusy, "Cancellation leaked account state");
            Assert(!Directory.GetDirectories(Path.Combine(loginVault.DirectoryPath, "runtime")).Any(), "Temporary login cache survived cancellation");
        });
        Test("Countdown displays days, hours, and minutes without waiting for a server poll", () =>
        {
            var now = DateTimeOffset.Parse("2026-10-11T06:00:00Z");
            var window = new UsageWindow { ResetsAt = (now + new TimeSpan(3, 5, 7, 0)).ToUnixTimeSeconds() };
            Assert(window.TimeUntilReset(now) == "리셋까지 3일 5시간 07분", "Countdown is wrong");
            Assert(window.TimeUntilReset(now.AddMinutes(1)) == "리셋까지 3일 5시간 06분", "Countdown waits for a poll");
        });
        Test("Countdown handles unavailable and elapsed reset times without inferring new quota", () =>
        {
            var now = DateTimeOffset.Parse("2026-10-11T06:00:00Z");
            Assert(new UsageWindow().TimeUntilReset(now) == "남은 시간 정보 없음", "Missing reset was fabricated");
            var window = new UsageWindow { ResetsAt = now.ToUnixTimeSeconds(), UsedPercent = 98 };
            Assert(window.TimeUntilReset(now).Contains("갱신 대기") && window.UsedPercent == 98, "Elapsed reset silently reset quota");
            window.ResetsAt = now.AddSeconds(1).ToUnixTimeSeconds(); Assert(window.TimeUntilReset(now).Contains("1분 미만"), "Sub-minute reset became zero");
            window.ResetsAt = now.AddDays(1).AddSeconds(-1).ToUnixTimeSeconds(); Assert(window.TimeUntilReset(now).Contains("0일 23시간 59분"), "Countdown rounded into the next day");
        });
        Test("Burn0 dark and light tokens translate to the actual palette and control metrics", () =>
        {
            var dark = new Burn0Theme(true); var light = new Burn0Theme(false);
            Assert(dark.Color("canvas").Color == System.Windows.Media.Color.FromRgb(21, 21, 21), "Wrong dark canvas");
            Assert(light.Color("canvas").Color == System.Windows.Media.Colors.White, "Wrong light canvas");
            Assert(dark.Color("accent").Color == (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#80afff"), "Wrong Burn0 accent");
            Assert(dark.Number("control-height") == 32 && dark.Number("meter-thickness") == 4 && dark.Number("radius") == 8, "Wrong Burn0 geometry");
        });
        Test("Different accounts refresh concurrently and repeated requests coalesce", () =>
        {
            RunAsync().GetAwaiter().GetResult();
            async Task RunAsync()
            {
                var started = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var coordinator = new RefreshCoordinator(async (key, token) => { started.AddOrUpdate(key, 1, (_, count) => count + 1); if (started.Count == 2) both.TrySetResult(true); await release.Task.WaitAsync(token); });
                var first = coordinator.Schedule("a", CancellationToken.None); var duplicate = coordinator.Schedule("a", CancellationToken.None); var second = coordinator.Schedule("b", CancellationToken.None);
                await both.Task.WaitAsync(TimeSpan.FromSeconds(4)); Assert(coordinator.RunningCount == 2, "Global lock serialized accounts");
                release.TrySetResult(true); await Task.WhenAll(first, duplicate, second);
                Assert(started["a"] == 1 && started["b"] == 1 && coordinator.RunningCount == 0, "Duplicate request or leaked query");
            }
        });
        Test("Fresh-login refresh supersedes the previous request immediately", () =>
        {
            RunAsync().GetAwaiter().GetResult();
            async Task RunAsync()
            {
                var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls = 0;
                var coordinator = new RefreshCoordinator(async (_, token) => { if (Interlocked.Increment(ref calls) == 1) { firstStarted.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); } else secondStarted.TrySetResult(true); });
                var old = coordinator.Schedule("account", CancellationToken.None); await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var renewed = coordinator.Schedule("account", CancellationToken.None, true); await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(4)); await renewed;
                Assert(old.IsCanceled && calls == 2 && coordinator.RunningCount == 0, "Fresh login joined stale refresh or waited for cooldown");
            }
        });
        Test("Registration schedules only the newly authenticated account without a timer tick", () =>
        {
            RunAsync().GetAwaiter().GetResult();
            async Task RunAsync()
            {
                var localVault = new AccountVault(Path.Combine(directory, "immediate-vault")); localVault.Upsert(a);
                var queried = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var localService = new AccountService(localVault, fakeHome, (key, _) => { queried.TrySetResult(key); return Task.CompletedTask; });
                var registration = localService.CompleteRegistration(b); await registration.ImmediateRefresh.WaitAsync(TimeSpan.FromSeconds(4));
                Assert(await queried.Task == profileB.Key && localVault.Data.Accounts.Count == 2, "Wrong account or delayed login refresh");
            }
        });
        Test("Managed browser login starts while another account refresh remains in flight", () =>
        {
            RunAsync().GetAwaiter().GetResult();
            async Task RunAsync()
            {
                var localVault = new AccountVault(Path.Combine(directory, "parallel-login-vault"));
                var queryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseQuery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var localService = new AccountService(localVault, fakeHome, async (_, token) => { queryStarted.TrySetResult(true); await releaseQuery.Task.WaitAsync(token); });
                await localService.InitializeAsync(CancellationToken.None);
                var query = localService.RefreshAccountAsync(profileA.Key, CancellationToken.None); await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
                using var cancelLogin = new CancellationTokenSource(); var browserOpened = false;
                try { await localService.AddAccountAsync(_ => { browserOpened = true; cancelLogin.Cancel(); }, cancelLogin.Token).WaitAsync(TimeSpan.FromSeconds(6)); }
                catch (OperationCanceledException) when (cancelLogin.IsCancellationRequested) { }
                finally { releaseQuery.TrySetResult(true); await query; }
                Assert(browserOpened && File.ReadAllText(fakeAuth) == a, "Login was blocked by refresh or changed the active cache");
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        Test("Korean and English catalogs have complete nonempty matching keys", () => Loc.ValidateCatalogs());
        Test("English countdowns and quota labels use the selected language", () =>
        {
            Loc.SetLanguage("en");
            try
            {
                var now = DateTimeOffset.Parse("2026-10-11T06:00:00Z");
                var window = new UsageWindow { Minutes = 10080, ResetsAt = now.AddDays(2).AddHours(3).AddMinutes(7).ToUnixTimeSeconds() };
                Assert(window.PeriodLabel == "Weekly" && window.TimeUntilReset(now) == "Resets in 2d 3h 07m", "English time labels are wrong");
                Assert(Loc.F("사용 {0:0.#}%  ·  잔여 {1:0.#}%", 12.5, 87.5) == "Used 12.5%  ·  87.5% remaining", "English number format failed");
                Assert(Loc.Error("현재 로그인 계정은 제거할 수 없습니다.") == "The current login account cannot be removed.", "Existing errors did not translate");
                Assert(new UsageWindow { Minutes = 60 }.PeriodLabel == "1 hour" && Loc.T("초기화권 1개") == "1 reset credit", "Singular labels are wrong");
            }
            finally { Loc.SetLanguage("ko"); }
        });
        Test("New vaults and older vaults without a language default to English", () =>
        {
            Assert(new VaultData().Language == "en", "New vault language is not English");
            Assert(JsonSerializer.Deserialize<VaultData>("{}")!.Language == "en", "Missing language does not default to English");
            Assert(JsonSerializer.Deserialize<VaultData>("{\"Language\":\"ko\"}")!.Language == "ko", "Saved Korean choice was overwritten");
        });
        Test("Language choice persists while account identities and authored aliases stay intact", () =>
        {
            var languageVault = new AccountVault(Path.Combine(directory, "language-vault"));
            languageVault.Upsert(a).Alias = "내 개인 계정"; languageVault.Data.Language = "en"; languageVault.Save();
            var loaded = new AccountVault(languageVault.DirectoryPath);
            Assert(loaded.Data.Language == "en" && loaded.Data.Accounts[0].Key == profileA.Key && loaded.Data.Accounts[0].Alias == "내 개인 계정", "Language setting changed account data");
        });
        Test("Changing language immediately updates formatting without restarting services", () =>
        {
            var now = DateTimeOffset.Parse("2026-10-11T06:00:00Z"); var window = new UsageWindow { ResetsAt = now.AddHours(5).ToUnixTimeSeconds() };
            Loc.SetLanguage("en"); Assert(Loc.T("계정 추가") == "Add account" && window.TimeUntilReset(now) == "Resets in 0d 5h 00m", "English not applied");
            Loc.SetLanguage("ko"); Assert(Loc.T("계정 추가") == "계정 추가" && window.TimeUntilReset(now) == "리셋까지 0일 5시간 00분", "Korean not restored");
        });
        var observationTime = DateTimeOffset.Parse("2026-10-11T06:00:00Z");
        var resetTime = observationTime.AddHours(6).ToUnixTimeSeconds();
        UsageObservation[] LinearHistory(double start, double perHour, int minutes = 60) => Enumerable.Range(0, minutes + 1)
            .Select(i => new UsageObservation(observationTime.AddMinutes(i - minutes), start + perHour * i / 60, resetTime, 0)).ToArray();
        Test("Successful observations are isolated by account, bucket, slot, and quota duration", () =>
        {
            var firstAccount = new AccountProfile(); var secondAccount = new AccountProfile();
            var snapshot = new UsageSnapshot { CheckedAt = observationTime, Buckets = new()
            {
                new() { Id = "codex", Primary = new() { UsedPercent = 20, Minutes = 300, ResetsAt = resetTime }, Secondary = new() { UsedPercent = 40, Minutes = 10080, ResetsAt = resetTime } },
                new() { Id = "other", Primary = new() { UsedPercent = 60, Minutes = 300, ResetsAt = resetTime } }
            } };
            UsageHistory.Record(firstAccount, snapshot); UsageHistory.Record(firstAccount, snapshot);
            Assert(firstAccount.History.Count == 3 && firstAccount.History.All(t => t.Points.Count == 1) && secondAccount.History.Count == 0, "Observation identities collided or duplicated");
            Assert(UsageHistory.ForWindow(firstAccount, "codex", "secondary", snapshot.Buckets[0].Secondary!)[0].UsedPercent == 40, "Quota windows were mixed");
        });
        Test("Earlier versions seed only their last real observation and history survives encrypted reload", () =>
        {
            var migration = new AccountVault(Path.Combine(directory, "history-vault")); var existing = migration.Upsert(a);
            existing.Usage = new() { CheckedAt = observationTime, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 25, Minutes = 300, ResetsAt = resetTime } } } };
            migration.Save(); var loaded = new AccountVault(migration.DirectoryPath);
            Assert(loaded.Data.Accounts[0].History.Single().Points.Single().At == observationTime, "Invented earlier observations");
            var next = loaded.Data.Accounts[0].Usage!; next.CheckedAt = observationTime.AddMinutes(1); next.Buckets[0].Primary!.UsedPercent = 26;
            UsageHistory.Record(loaded.Data.Accounts[0], next); loaded.Save(); var reloaded = new AccountVault(migration.DirectoryPath);
            Assert(reloaded.Data.Accounts[0].History.Single().Points.Count == 2 && reloaded.Data.Accounts[0].Key == profileA.Key, "History was lost or account identity changed");
        });
        Test("History retains 30 days with minute and quarter-hour resolution and preserves cycle boundaries", () =>
        {
            var account = new AccountProfile();
            account.History.Add(new() { BucketId = "codex", Slot = "primary", Minutes = 300, Points = Enumerable.Range(0, 31 * 1440)
                .Select(i => new UsageObservation(observationTime.AddMinutes(i - 31 * 1440), 20, resetTime, 0)).ToList() });
            UsageHistory.Record(account, new() { CheckedAt = observationTime, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 21, Minutes = 300, ResetsAt = resetTime } } } });
            var points = account.History.Single().Points;
            Assert(points.Count < 5600 && points[0].At >= observationTime.AddDays(-30) && points[^1].At == observationTime, "Retention is unbounded or lost the latest reading");
            var changed = new UsageSnapshot { CheckedAt = observationTime.AddSeconds(5), Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 0, Minutes = 300, ResetsAt = resetTime + 3600 } } } };
            UsageHistory.Record(account, changed); changed.CheckedAt = observationTime.AddSeconds(10); changed.Buckets[0].Primary!.UsedPercent = 1; UsageHistory.Record(account, changed);
            Assert(account.History[0].Points[^1].Segment != account.History[0].Points[^2].Segment, "Compaction erased a reset boundary");
        });
        Test("Recent consumption predicts an absolute depletion time and duration before reset", () =>
        {
            var window = new UsageWindow { UsedPercent = 40, Minutes = 300, ResetsAt = resetTime };
            var forecast = UsagePredictor.Evaluate(window, LinearHistory(20, 20), observationTime);
            Assert(forecast.State == ForecastState.DepletesFirst && Math.Abs(forecast.RatePerHour!.Value - 20) < 1e-8 && Math.Abs((forecast.ExhaustsAt!.Value - observationTime.AddHours(3)).TotalMilliseconds) < 1, "Depletion math is incorrect");
            Assert(UsagePresentation.TimeLeft(observationTime.AddHours(3), observationTime) == "약 3시간 0분 후", "Remaining time is wrong");
        });
        Test("A reset before depletion is explicit and estimates use at the reset deadline", () =>
        {
            var resetSoon = observationTime.AddHours(2).ToUnixTimeSeconds();
            var history = LinearHistory(30, 10).Select(p => p with { ResetsAt = resetSoon }).ToArray();
            var forecast = UsagePredictor.Evaluate(new() { UsedPercent = 40, ResetsAt = resetSoon }, history, observationTime);
            Assert(forecast.State == ForecastState.ResetsFirst && Math.Abs((forecast.ExhaustsAt!.Value - observationTime.AddHours(6)).TotalMilliseconds) < 1 && Math.Abs(forecast.UsedAtReset!.Value - 60) < 1e-8, "Forecast ignored the earlier reset");
        });
        Test("Sparse or unchanged observations do not fabricate a depletion time", () =>
        {
            var window = new UsageWindow { UsedPercent = 40, ResetsAt = resetTime };
            Assert(UsagePredictor.Evaluate(window, LinearHistory(40, 0), observationTime).State == ForecastState.NoGrowth, "No-growth became infinite or invented depletion");
            Assert(UsagePredictor.Evaluate(window, LinearHistory(39, 20, 3), observationTime).State == ForecastState.Collecting, "Insufficient history created a forecast");
            Assert(!UsagePredictor.Evaluate(window, Array.Empty<UsageObservation>(), observationTime).ExhaustsAt.HasValue, "Missing history became a forecast");
        });
        Test("Reset and usage decreases exclude the earlier consumption segment", () =>
        {
            var account = new AccountProfile();
            for (var i = 0; i < 12; i++) UsageHistory.Record(account, new() { CheckedAt = observationTime.AddMinutes(i - 10), Buckets = new() { new()
            { Id = "codex", Primary = new() { Minutes = 300, ResetsAt = resetTime, UsedPercent = i <= 10 ? 50 + i : 10 } } } });
            var window = new UsageWindow { UsedPercent = 10, Minutes = 300, ResetsAt = resetTime };
            Assert(UsagePredictor.Evaluate(window, UsageHistory.ForWindow(account, "codex", "primary", window), observationTime.AddMinutes(1)).State == ForecastState.Collecting, "Usage decrease reused old growth");
            var afterReset = DateTimeOffset.FromUnixTimeSeconds(resetTime).AddMinutes(1);
            window.ResetsAt = resetTime + 3600;
            UsageHistory.Record(account, new() { CheckedAt = afterReset, Buckets = new() { new() { Id = "codex", Primary = window } } });
            Assert(UsagePredictor.Evaluate(window, account.History[0].Points, afterReset).State == ForecastState.Collecting, "An observed reset reused old cycle data");
        });
        Test("Reset deadline corrections do not split consumption and legacy false segments are repaired", () =>
        {
            var account = new AccountProfile();
            for (var minute = 0; minute <= 60; minute++)
                UsageHistory.Record(account, new() { CheckedAt = observationTime.AddMinutes(minute - 60), Buckets = new() { new()
                    { Id = "codex", Primary = new() { UsedPercent = minute / 10.0, Minutes = 300, ResetsAt = resetTime + minute % 2 } } } });
            var track = account.History.Single();
            Assert(track.Points.All(p => p.Segment == 0), "Timestamp corrections were treated as quota resets");
            track.Points = track.Points.Select((p, index) => p with { Segment = index }).ToList();
            var final = new UsageSnapshot { CheckedAt = observationTime, Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 6, Minutes = 300, ResetsAt = resetTime } } } };
            UsageHistory.Record(account, final);
            Assert(track.Points.Count == 61 && track.Points.All(p => p.Segment == 0) && Math.Abs(UsagePredictor.Learn(final.Buckets[0].Primary!, track.Points).RatePerHour!.Value - 6) < 1e-8,
                "Legacy deadline splits remained or consumed history was altered");
        });
        Test("Stale readings, offline gaps, and elapsed resets withhold predictions", () =>
        {
            var window = new UsageWindow { UsedPercent = 40, ResetsAt = resetTime }; var points = LinearHistory(20, 20);
            Assert(UsagePredictor.Evaluate(window, points, observationTime.AddMinutes(15)).State == ForecastState.Stale, "Stale reading remained a live prediction");
            Assert(UsagePredictor.Evaluate(window, new[] { points[0], points[^1] }, observationTime).State == ForecastState.Collecting, "Offline gap became a consumption rate");
            Assert(UsagePredictor.Evaluate(window, points, observationTime.AddHours(6)).State == ForecastState.ResetElapsed && window.UsedPercent == 40, "Reset arrival fabricated a new quota");
        });
        Test("Forecast retains recent active blocks and excludes day-old history", () =>
        {
            var points = LinearHistory(0, 10, 180); var window = new UsageWindow { UsedPercent = 30, ResetsAt = resetTime };
            var old = LinearHistory(0, 100).Select(p => p with { At = p.At.AddHours(-25) });
            var forecast = UsagePredictor.Evaluate(window, old.Concat(points).ToArray(), observationTime);
            Assert(forecast.ObservedSpan == TimeSpan.FromHours(3) && Math.Abs(forecast.RatePerHour!.Value - 10) < 1e-8 && Math.Abs((forecast.ExhaustsAt!.Value - observationTime.AddHours(7)).TotalMilliseconds) < 1, "Day-old history biased the recent forecast");
        });
        Test("An idle clock never consumes quota or expires the conditional estimate", () =>
        {
            var window = new UsageWindow { UsedPercent = 99, ResetsAt = resetTime }; var points = LinearHistory(0, 99);
            var elapsed = UsagePredictor.Evaluate(window, points, observationTime.AddMinutes(2));
            Assert(elapsed.State == ForecastState.DepletesFirst && elapsed.ExhaustsAt > observationTime.AddMinutes(2) && window.UsedPercent == 99, "Idle time consumed or exhausted the measured quota");
            window.UsedPercent = 100; Assert(UsagePredictor.Evaluate(window, points, observationTime).State == ForecastState.Exhausted, "Measured exhaustion was ignored");
        });
        Test("Forecast and history labels update in Korean and English", () =>
        {
            Loc.SetLanguage("en");
            try { Assert(UsagePresentation.Outlook(new(ForecastState.ResetsFirst)) == "Reset comes first" && UsagePresentation.TimeLeft(observationTime.AddHours(3), observationTime) == "In about 3h 0m", "Forecast labels were not localized"); }
            finally { Loc.SetLanguage("ko"); }
        });
        UsageObservation[] ReplayRates(params double[] rates)
        {
            var points = new List<UsageObservation>(); var used = 0.0;
            var start = observationTime.AddMinutes(-rates.Length * 10);
            points.Add(new(start, used, resetTime, 0));
            for (var minute = 0; minute < rates.Length * 10; minute++)
            { used += rates[minute / 10] / 60; points.Add(new(start.AddMinutes(minute + 1), used, resetTime, 0)); }
            return points.ToArray();
        }
        Test("Pauses and account-switch observation gaps preserve the learned consumption pace", () =>
        {
            var points = ReplayRates(6, 6, 6, 6, 6, 6); var window = new UsageWindow { UsedPercent = points[^1].UsedPercent, ResetsAt = resetTime };
            var before = UsagePredictor.Evaluate(window, points, observationTime);
            var paused = points.Concat(Enumerable.Range(1, 60).Select(i => points[^1] with { At = observationTime.AddMinutes(i) })).ToArray();
            var after = UsagePredictor.Evaluate(window, paused, observationTime.AddHours(1));
            Assert(Math.Abs(before.RatePerHour!.Value - after.RatePerHour!.Value) < 1e-8 && Math.Abs((after.ExhaustsAt!.Value - observationTime.AddHours(1) - (before.ExhaustsAt!.Value - observationTime)).TotalMilliseconds) < 1, "Pause diluted consumption pace or countdown");
            var switched = points.Append(points[^1] with { At = observationTime.AddHours(1) }).ToArray();
            Assert(Math.Abs(UsagePredictor.Evaluate(window, switched, observationTime.AddHours(1)).RatePerHour!.Value - before.RatePerHour.Value) < 1e-8, "Switch gap changed the learned pace");
        });
        Test("Incomplete bursts do not train the rate and one extreme block has bounded influence", () =>
        {
            var points = ReplayRates(6, 6, 6, 6, 6, 6);
            var burst = points.Concat(Enumerable.Range(1, 3).Select(i => points[^1] with { At = observationTime.AddMinutes(i), UsedPercent = points[^1].UsedPercent + i * 4 })).ToArray();
            var window = new UsageWindow { UsedPercent = burst[^1].UsedPercent, ResetsAt = resetTime };
            Assert(Math.Abs(UsagePredictor.Learn(window, burst).RatePerHour!.Value - 6) < 1e-8, "A three-minute burst instantly trained the rate");
            var spike = ReplayRates(6, 6, 6, 6, 6, 6, 120);
            var model = UsagePredictor.Learn(window, spike);
            Assert(model.RatePerHour >= 6 && model.RatePerHour < 6.48, "One outlier caused an abrupt rate change");
        });
        Test("Persistent real speed changes adapt while quantized usage stays near its trend", () =>
        {
            var changed = ReplayRates(Enumerable.Repeat(6.0, 6).Concat(Enumerable.Repeat(18.0, 12)).ToArray());
            var window = new UsageWindow { ResetsAt = resetTime };
            var rate = UsagePredictor.Learn(window, changed).RatePerHour!.Value;
            Assert(rate > 12 && rate < 18, "Smoothing never adapted to sustained faster usage");
            var quantized = ReplayRates(7, 7, 7, 7, 7, 7).Select(p => p with { UsedPercent = Math.Floor(p.UsedPercent + 1e-8) }).ToArray();
            var roundedRate = UsagePredictor.Learn(window, quantized).RatePerHour!.Value;
            Assert(Math.Abs(roundedRate - 7) < 1.4, "Integer quota steps produced a spike rate");
        });
        Test("Polling cadence does not change a constant underlying consumption trend", () =>
        {
            var points = ReplayRates(9, 9, 9, 9, 9, 9); var window = new UsageWindow { ResetsAt = resetTime };
            var dense = UsagePredictor.Learn(window, points).RatePerHour!.Value;
            var sparse = UsagePredictor.Learn(window, points.Where((p, index) => index % 5 == 0).ToArray()).RatePerHour!.Value;
            Assert(Math.Abs(dense - sparse) < 1e-8 && Math.Abs(dense - 9) < 1e-8, "Polling interval biased the consumption rate");
        });
        Test("Tiny consumption rates never overflow the representable forecast calendar", () =>
        {
            var model = new UsageTrendModel(ForecastState.Projected, observationTime, 1e-9);
            var withReset = UsagePredictor.Evaluate(new() { UsedPercent = 10, ResetsAt = resetTime }, model, observationTime);
            var withoutReset = UsagePredictor.Evaluate(new() { UsedPercent = 10 }, model, observationTime);
            Assert(withReset.State == ForecastState.ResetsFirst && withReset.ExhaustsAt == null, "Tiny rate created an invalid timestamp or missed the reset");
            Assert(withoutReset.State == ForecastState.InsufficientChange && withoutReset.ExhaustsAt == null, "Unrepresentable calendar date was fabricated");
        });
        Test("Inactive water is level and active waves preserve mean level and small bounded motion", () =>
        {
            var y = TankGaugeSurface.LevelY(160, 55);
            foreach (var phase in new[] { 0.0, 1.2, 2.5 })
            {
                var samples = Enumerable.Range(0, 48).Select(i => TankGaugeSurface.SurfaceY(160, 55, i / 48.0, true, phase)).ToArray();
                Assert(Math.Abs(samples.Average() - y) < 1e-8 && samples.All(p => Math.Abs(p - y) <= 3), "Wave changed quota fill or exceeded motion bounds");
                Assert(Enumerable.Range(0, 48).All(i => TankGaugeSurface.SurfaceY(160, 55, i / 48.0, false, phase) == y), "Inactive water was not horizontal");
            }
            Assert(TankGaugeSurface.SurfaceY(160, 55, 0.3, true, 0) != TankGaugeSurface.SurfaceY(160, 55, 0.3, true, 1.2), "Active water did not change with animation time");
            Assert(TankGaugeSurface.SurfaceY(160, 0, 0.3, true, 1.2) == 160 && TankGaugeSurface.SurfaceY(160, 100, 0.3, true, 1.2) == 0, "Empty/full levels changed under animation");
        });
        Test("Tank selection uses the limiting quota, supports a chosen window, and keeps unknown separate from zero", () =>
        {
            var profile = new AccountProfile { Usage = new() { Buckets = new() { new() { Id = "codex", Primary = new() { UsedPercent = 25, Minutes = 300 }, Secondary = new() { UsedPercent = 80, Minutes = 10080 } } } } };
            Assert(AccountTankCard.SelectQuota(profile)!.Slot == "secondary" && AccountTankCard.SelectQuota(profile, "codex:primary")!.Window.Remaining == 75, "Selected the wrong reservoir");
            Assert(AccountTankCard.SelectQuota(new()) == null, "Unknown quota became zero remaining");
            profile.Usage.Buckets[0].Primary!.UsedPercent = 100; Assert(AccountTankCard.SelectQuota(profile)!.Window.Remaining == 0, "Known zero quota was hidden");
        });
        SwitchDiagnostics.AddTests(Test, Assert, directory);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = typeof(Program).Assembly.GetName().Version!.ToString(3), passed = tests.Count - failureCount, failed = failureCount,
            scope = "Local encryption, parsing, and isolated authentication-file switching; no live desktop was closed or switched.", tests }, new JsonSerializerOptions { WriteIndented = true }));
        Directory.Delete(directory, true);
        return failureCount == 0 ? 0 : 1;
    }
    public static async Task<int> VerifySessionsAsync(string home, string reportPath)
    {
        await using var monitor = new DesktopSessionMonitor(home);
        var samples = new List<object>(); monitor.Start();
        var available = false;
        for (var i = 0; i < 36; i++)
        {
            await Task.Delay(500);
            var activity = monitor.Current; available |= activity.Available;
            samples.Add(new { at = DateTimeOffset.UtcNow, activity.Available, activity.Running, activity.Waiting, activity.Observed, activity.Problem,
                activity.Queued, activity.Goals, activity.PausedQueued, activity.PausedGoals, activity.QueueProblem });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        bool ownerRpcVerified = false; string? ownerRpcError = null;
        var owner = monitor.Threads.FirstOrDefault();
        if (owner != null)
        {
            try
            {
                var settings = await monitor.SendToOwnerAsync(owner, "thread-follower-read-model-settings", new { conversationId = owner.Id }, 1, CancellationToken.None);
                ownerRpcVerified = settings.TryGetProperty("settings", out _);
            }
            catch (Exception error) { ownerRpcError = error.Message; }
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = typeof(Program).Assembly.GetName().Version!.ToString(3), available, ownerRpcVerified, ownerRpcError,
            scope = "Read-only local desktop status and owner settings, saved and server follow-up queues; no account access or task mutation.", samples },
            new JsonSerializerOptions { WriteIndented = true }));
        return available ? 0 : 1;
    }
    private static string FakeAuth(string subject, string email, string account, DateTimeOffset? subscriptionUntil = null, DateTimeOffset? subscriptionChecked = null)
    {
        string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = Encode("{\"alg\":\"none\"}") + "." + Encode(JsonSerializer.Serialize(new Dictionary<string, object> { ["sub"] = subject, ["email"] = email, ["exp"] = 2050000000,
            ["https://api.openai.com/auth"] = new { chatgpt_plan_type = "pro", chatgpt_subscription_active_until = subscriptionUntil, chatgpt_subscription_last_checked = subscriptionChecked } })) + ".test";
        return JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { id_token = token, access_token = token, refresh_token = "test-refresh-" + subject, account_id = account } });
    }
}
