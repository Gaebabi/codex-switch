using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

internal static class IncrementalFolderContracts
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string target, IntPtr security);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Incremental folder monitoring contract failed."); }
    private static async Task Wait(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until) await Task.Delay(50);
        Assert(ready());
    }
    public static async Task Run(Action<string, Action> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "codex-incremental-test-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(directory, "first"); var second = Path.Combine(directory, "second");
        var leaf = Path.Combine(first, "leaf"); var quiet = Path.Combine(first, "quiet");
        Directory.CreateDirectory(leaf); Directory.CreateDirectory(quiet); Directory.CreateDirectory(second);
        var payload = Path.Combine(leaf, "data.bin"); File.WriteAllBytes(payload, new byte[65536]);
        File.WriteAllBytes(Path.Combine(quiet, "quiet.bin"), new byte[32768]);
        var root = Path.GetPathRoot(directory)!;
        var preferences = new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = first }, new FolderPreference { Path = second, Color = 1 } } } };
        var cache = new FolderUsage.Cache();
        long Total() => FolderUsage.Scan(preferences, CancellationToken.None, cache: cache)[root].Sum(r => r.Bytes!.Value);
        try
        {
            var time = TimeSpan.Zero;
            var budget = new FolderScanBudget(() => time, (delay, _) => time += delay);
            var limited = new FolderUsage.Cache(budget);
            FolderUsage.Scan(preferences, CancellationToken.None, cache: limited);
            var queries = budget.Queries; var spent = time;
            FolderUsage.Scan(preferences, CancellationToken.None, cache: limited);
            check("Production directory/root queries use the budget while unchanged cache hits use no query or wait", () =>
                Assert(queries > 0 && budget.Queries == queries && spent == time && spent >= TimeSpan.FromSeconds((queries - 1.0) / FolderScanBudget.QueriesPerSecond)));
            var before = Total(); var reads = cache.DirectoryReads; var roots = cache.RootReads;
            check("Unchanged folder snapshots perform no repeated directory enumeration", () =>
            { Assert(Total() == before && cache.DirectoryReads == reads && cache.RootReads == roots); });
            File.WriteAllBytes(payload, new byte[131072]); cache.Invalidate(leaf);
            check("A changed leaf rereads only that directory and preserves unchanged subtrees", () =>
            { Assert(Total() == before + 65536 && cache.DirectoryReads == reads + 1 && cache.RootReads == roots + 1); });
            if (!CreateHardLinkW(Path.Combine(second, "shared.bin"), payload, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            cache.Invalidate(second); before = Total();
            File.WriteAllBytes(Path.Combine(second, "shared.bin"), new byte[196608]); cache.Invalidate(second);
            check("A changed hard-link alias updates the drive allocation once across cached folders", () => Assert(Total() == before + 65536));
            var renamed = Path.Combine(first, "renamed"); Directory.Move(leaf, renamed); Directory.CreateDirectory(leaf);
            File.WriteAllBytes(Path.Combine(leaf, "replacement.bin"), new byte[8192]); cache.Invalidate(first);
            check("Directory rename and replacement discard old path snapshots without losing the moved tree", () =>
            {
                var actual = Total(); var expected = FolderUsage.Scan(preferences, CancellationToken.None)[root].Sum(r => r.Bytes!.Value);
                if (actual != expected) throw new InvalidOperationException($"Rename allocation: cached={actual}, fresh={expected}");
            });
            Directory.Delete(renamed, true); cache.Invalidate(first);
            check("Deleted subtrees and surviving hard links match a fresh allocation scan", () =>
            { Assert(Total() == FolderUsage.Scan(preferences, CancellationToken.None)[root].Sum(r => r.Bytes!.Value)); });
            File.WriteAllBytes(Path.Combine(quiet, "unreported.bin"), new byte[4096]); cache.Reset(first);
            check("Lost-notification recovery rebuilds the affected root instead of retaining stale allocation", () =>
            { Assert(Total() == FolderUsage.Scan(preferences, CancellationToken.None)[root].Sum(r => r.Bytes!.Value)); });
            await using (var paused = new FolderMonitor())
            {
                paused.SetActive(false); paused.Configure(preferences); paused.Start();
                await Task.Delay(2000);
                check("A hidden monitor performs no initial root queries or directory enumeration", () => Assert(paused.QueryCounts == (0L, 0L)));
                paused.SetActive(true);
                await Wait(() => paused.Read(root).All(r => r.State == FolderState.Available) && !paused.IsMeasuring);
                paused.SetActive(false); await Task.Delay(2500);
                var counts = paused.QueryCounts; var bytes = paused.Read(root)[0].Bytes;
                File.WriteAllBytes(Path.Combine(quiet, "paused.bin"), new byte[8192]);
                await Task.Delay(2500);
                Assert(paused.QueryCounts == counts && paused.Read(root)[0].Bytes == bytes);
                paused.SetActive(true);
                await Wait(() => paused.Read(root)[0].Bytes == bytes + 8192);
                check("A hidden monitor retains changes without reads and resumes the affected folder", () => Assert(paused.QueryCounts.Roots == counts.Roots + 1));
            }
            await using (var canceled = new FolderMonitor())
            {
                var hidden = false;
                canceled.Configure(preferences);
                canceled.Changed += () =>
                {
                    var rows = canceled.Read(root);
                    if (!hidden && rows.Length == 2 && rows[0].Bytes.HasValue && rows[1].State == FolderState.Scanning)
                    { hidden = true; canceled.SetActive(false); }
                };
                canceled.Start();
                await Wait(() => hidden && !canceled.IsMeasuring);
                var counts = canceled.QueryCounts;
                await Task.Delay(1500);
                Assert(canceled.QueryCounts == counts && canceled.Read(root)[1].State == FolderState.Scanning);
                canceled.SetActive(true);
                await Wait(() => canceled.Read(root).All(r => r.State == FolderState.Available));
                check("Hiding during traversal cancels pending folders and reopening completes measurement", () => Assert(hidden));
            }
            await using var monitor = new FolderMonitor(); var sawActivity = false; var wrongDriveActivity = false;
            monitor.Changed += () => { sawActivity |= monitor.IsMeasuringRoot(root); wrongDriveActivity |= monitor.IsMeasuringRoot(@"Z:\"); };
            monitor.Configure(preferences); monitor.Start();
            await Wait(() => monitor.Read(root).All(r => r.State == FolderState.Available) && !monitor.IsMeasuring);
            check("Scan activity identifies only the measured drive and clears after completion", () =>
                Assert(sawActivity && !wrongDriveActivity && !monitor.IsMeasuringRoot(root)));
            var idle = monitor.Read(root).Select(r => r.CheckedAt).ToArray();
            await Task.Delay(TimeSpan.FromSeconds(65));
            check("An idle live monitor does not repeat the old sixty-second full scan", () => Assert(monitor.Read(root).Select(r => r.CheckedAt).SequenceEqual(idle)));
            var live = Path.Combine(quiet, "live.bin"); before = monitor.Read(root).Sum(r => r.Bytes!.Value);
            File.WriteAllBytes(live, new byte[65536]);
            await Wait(() => monitor.Read(root).Sum(r => r.Bytes ?? 0) == before + 65536);
            File.WriteAllBytes(live, new byte[131072]);
            await Wait(() => monitor.Read(root).Sum(r => r.Bytes ?? 0) == before + 131072);
            File.Move(live, Path.Combine(second, "moved.bin"));
            await Wait(() => monitor.Read(root)[1].Bytes == FolderUsage.Allocation(Path.Combine(second, "shared.bin")).Bytes + 131072);
            File.Delete(Path.Combine(second, "moved.bin"));
            await Wait(() => monitor.Read(root).Sum(r => r.Bytes ?? 0) == before);
            check("Real filesystem notifications update create, growth, cross-folder rename and deletion", () => Assert(monitor.Read(root).All(r => r.State == FolderState.Available)));
            Directory.Delete(second, true);
            await Wait(() => monitor.Read(root)[1].State == FolderState.Missing);
            Directory.CreateDirectory(second); File.WriteAllBytes(Path.Combine(second, "restored.bin"), new byte[4096]);
            await Wait(() => monitor.Read(root)[1].Bytes == 4096 && monitor.Read(root)[1].State == FolderState.Available);
            check("A deleted tracked root is observed again after recreation without restarting", () => Assert(monitor.Read(root)[1].Bytes == 4096));
            monitor.Configure(Array.Empty<DrivePreference>());
            check("Removed watcher configurations cannot republish obsolete folder readings", () => Assert(monitor.Read(root).Length == 0));
        }
        finally { Directory.Delete(directory, true); }
    }
}
