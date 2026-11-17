using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

internal static class PersistentFolderContracts
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateHardLinkW(string link, string target, IntPtr security);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Persistent folder-index contract failed."); }
    private static async Task Wait(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(40);
        while (!ready() && DateTime.UtcNow < end) await Task.Delay(40);
        Assert(ready());
    }
    public static async Task Run(Action<string, Action> check)
    {
        check("Journal identity and retained cursor bounds reject missing, replaced and future history", () =>
        {
            var status = new FolderJournalStatus(FolderIndexAccess.Ready, 1, 42, 100, 200);
            Assert(WindowsFolderJournal.Covers(status, new(42, 100)) && WindowsFolderJournal.Covers(status, new(42, 200)));
            Assert(!WindowsFolderJournal.Covers(status, new(41, 100)) && !WindowsFolderJournal.Covers(status, new(42, 99)) && !WindowsFolderJournal.Covers(status, new(42, 201)));
            Assert(WindowsFolderJournal.Enable(@"C:\not-a-volume") == 87);
        });
        check("Native journal records preserve identities and reject malformed boundaries or future schemas", () =>
        {
            var bytes = new byte[80]; BinaryPrimitives.WriteInt32LittleEndian(bytes, 80); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 7); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), 9);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 123); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 2);
            var name = Encoding.Unicode.GetBytes("data.bin"); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), (ushort)name.Length); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 60); name.CopyTo(bytes, 60);
            var row = WindowsFolderJournal.Parse(bytes, 11).Single(); Assert(row.FileId == 7 && row.ParentId == 9 && row.Volume == 11 && row.Name == "data.bin" && row.Usn == 123);
            foreach (var offset in new[] { 0, 4, 56, 58 })
            {
                var invalid = (byte[])bytes.Clone(); invalid[offset] = 255;
                try { WindowsFolderJournal.Parse(invalid, 11); throw new Exception("Invalid USN record accepted."); }
                catch (InvalidDataException) { }
            }
        });
        if (!OperatingSystem.IsWindows()) return;
        var temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexFolderIndexContracts", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(temporary, "data", "first"); var second = Path.Combine(temporary, "data", "second");
        var leaf = Path.Combine(first, "leaf"); var quiet = Path.Combine(first, "quiet");
        Directory.CreateDirectory(leaf); Directory.CreateDirectory(quiet); Directory.CreateDirectory(second);
        var payload = Path.Combine(leaf, "data.bin"); var quietFile = Path.Combine(quiet, "quiet.bin");
        File.WriteAllBytes(payload, new byte[65536]); File.WriteAllBytes(quietFile, new byte[32768]);
        var root = Path.GetPathRoot(temporary)!;
        var preferences = new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = first }, new FolderPreference { Path = second, Color = 1 } } } };
        var index = Path.Combine(temporary, "index");
        var clock = TimeSpan.Zero; var budget = new FolderScanBudget(() => clock, (delay, _) => clock += delay);
        var status = WindowsFolderJournal.Probe(root);
        try
        {
            check("The ordinary FILE_TRAVERSE journal reader is available on the native Windows system volume", () => Assert(status.Access == FolderIndexAccess.Ready));
            void Sync(FolderUsage.Cache cache)
            {
                var current = WindowsFolderJournal.Probe(root); Assert(current.Access == FolderIndexAccess.Ready);
                if (cache.Cursors.TryGetValue(root, out var cursor))
                    cache.SaveCursor(root, WindowsFolderJournal.Replay(root, current, cursor, budget, change => cache.Invalidate(root, change), CancellationToken.None));
                else cache.SaveCursor(root, new(current.JournalId, current.NextUsn));
            }
            long Total(FolderUsage.Cache cache) => FolderUsage.Scan(preferences, CancellationToken.None, cache: cache)[root].Sum(r => r.Bytes!.Value);
            long Expected() => Directory.EnumerateFiles(Path.Combine(temporary, "data"), "*", SearchOption.AllDirectories)
                .Select(FolderUsage.Allocation).GroupBy(f => (f.Volume, f.Id)).Sum(g => g.First().Bytes);
            using (var store = new FolderIndexStore(index))
            {
                var cache = new FolderUsage.Cache(budget, store); cache.Configure(preferences); Sync(cache);
                var actual = Total(cache); cache.Flush();
                check("Persistent directory and allocation records equal independent native file allocations", () => Assert(actual == Expected() && cache.DirectoryReads >= 4));
            }
            using (var store = new FolderIndexStore(index))
            {
                var cache = new FolderUsage.Cache(budget, store); cache.Configure(preferences); Sync(cache); var total = Total(cache);
                check("A new cache instance reuses a verified unchanged index without directory enumeration", () => Assert(total == Expected() && cache.DirectoryReads == 0));
                File.WriteAllBytes(payload, new byte[131072]); Sync(cache); var before = cache.DirectoryReads; total = Total(cache); cache.Flush();
                check("Offline journal replay refreshes the changed leaf without enumerating its quiet sibling", () => Assert(total == Expected() && cache.DirectoryReads == before + 1));
                Directory.Move(quiet, Path.Combine(first, "moved")); File.Delete(payload); File.WriteAllBytes(Path.Combine(leaf, "new.bin"), new byte[8192]);
                Sync(cache); total = Total(cache); cache.Flush();
                check("Offline create, delete and directory rename preserve current paths and allocated totals", () => Assert(total == Expected()));
                var target = Path.Combine(leaf, "new.bin"); var alias = Path.Combine(second, "shared.bin");
                if (!CreateHardLinkW(alias, target, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Sync(cache); total = Total(cache); cache.Flush();
                check("A journal-discovered hard link remains allocated only once across tracked folders", () => Assert(total == Expected()));
            }
            using (var store = new FolderIndexStore(index))
            {
                var cache = new FolderUsage.Cache(budget, store); cache.Configure(preferences);
                File.WriteAllBytes(Path.Combine(second, "shared.bin"), new byte[196608]); Sync(cache); var total = Total(cache); cache.Flush();
                check("Restarted alias indexes invalidate all affected directories after an offline hard-link write", () => Assert(total == Expected() && cache.DirectoryReads == 2));
            }
            var partialIndex = Path.Combine(temporary, "partial-index");
            long partialDirectories;
            using (var store = new FolderIndexStore(partialIndex))
            {
                var cache = new FolderUsage.Cache(budget, store) { DirectoriesPerSlice = 1 }; cache.Configure(preferences); Sync(cache);
                var rows = FolderUsage.Scan(preferences, CancellationToken.None, cache: cache)[root]; partialDirectories = cache.DirectoryReads; cache.Flush();
                Assert(rows.Any(r => r.State == FolderState.Scanning));
            }
            using (var store = new FolderIndexStore(partialIndex))
            {
                var cache = new FolderUsage.Cache(budget, store); cache.Configure(preferences); Sync(cache); var total = Total(cache); cache.Flush();
                check("A partially completed index resumes saved directories instead of rereading them", () => Assert(total == Expected() && cache.DirectoryReads + partialDirectories == 4));
            }
            var hidden = new FolderMonitor(index); hidden.SetActive(false); hidden.Configure(preferences); hidden.Start();
            await Wait(() => hidden.Read(root).Length == 2 && hidden.Read(root).All(r => r.Bytes.HasValue));
            check("Hidden startup restores saved values without querying tracked roots or directories", () => Assert(hidden.QueryCounts == (0, 0) && !hidden.IsMeasuring && hidden.Read(root).All(r => r.State == FolderState.Cached)));
            await hidden.DisposeAsync();
            var owned = Path.Combine(temporary, "owned"); Directory.CreateDirectory(owned); File.WriteAllBytes(Path.Combine(owned, "payload.bin"), new byte[8192]);
            await using (var monitor = new FolderMonitor(Path.Combine(owned, "index")))
            {
                monitor.Configure(new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = owned } } } }); monitor.Start();
                await Wait(() => monitor.Read(root).Single().State == FolderState.Available && !monitor.IsMeasuring);
                var queries = monitor.QueryCounts; await Task.Delay(3500);
                check("Index-owned writes inside a tracked tree reach idle and retain their physical allocation", () =>
                    Assert(!monitor.IsMeasuringRoot(root) && monitor.QueryCounts == queries && monitor.Read(root).Single().Bytes >= 8192));
            }
            var foreign = Path.Combine(temporary, "foreign"); Directory.CreateDirectory(foreign);
            var foreignFile = Path.Combine(foreign, "allocation.sqlite3"); var original = Encoding.UTF8.GetBytes("not-an-owned-index"); File.WriteAllBytes(foreignFile, original);
            check("An unrelated or corrupt database is refused without overwriting its bytes", () =>
            {
                try { using var invalid = new FolderIndexStore(foreign); throw new Exception("A foreign index was accepted."); }
                catch (Exception error) when (error is IOException or InvalidDataException) { Assert(File.ReadAllBytes(foreignFile).SequenceEqual(original)); }
            });
            await using (var recovery = new FolderMonitor(foreign))
            {
                recovery.Configure(preferences); recovery.Start();
                await Wait(() => recovery.IndexStatus(root)?.Access == FolderIndexAccess.Unavailable);
                Assert(!recovery.IsMeasuringRoot(root));
                recovery.RefreshIndex(root, true);
                await Wait(() => recovery.Read(root).All(r => r.State == FolderState.Available) && !recovery.IsMeasuring);
                check("Index failure clears activity and an explicit rebuild preserves the rejected file while recovering", () =>
                    Assert(recovery.Read(root).Sum(r => r.Bytes!.Value) == Expected() && Directory.EnumerateFiles(foreign, "allocation.sqlite3.invalid-*").Any(p => File.ReadAllBytes(p).SequenceEqual(original))));
            }
        }
        finally
        {
            var expectedParent = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexFolderIndexContracts"));
            if (Directory.GetParent(temporary)!.FullName == expectedParent && Guid.TryParseExact(Path.GetFileName(temporary), "N", out _)) Directory.Delete(temporary, true);
        }
    }
}
