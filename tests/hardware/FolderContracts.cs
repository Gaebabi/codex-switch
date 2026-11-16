using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;
using Microsoft.Win32.SafeHandles;

internal static class FolderContracts
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string target, IntPtr security);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, int size, IntPtr output, int outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);
    public static async Task Run(Action<string, Action> check)
    {
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Folder monitoring contract failed."); }
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var directory = Path.Combine(Path.GetTempPath(), "codex-folder-test-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(directory, "first"); var second = Path.Combine(directory, "second");
        var settings = new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = first, Color = 0 }, new FolderPreference { Path = second, Color = 2 } } };
        check("Folder colors and paths survive JSON persistence without changing legacy defaults", () =>
        {
            var restored = JsonSerializer.Deserialize<DrivePreference>(JsonSerializer.Serialize(settings))!;
            Assert(restored.Folders!.SequenceEqual(settings.Folders));
            Assert(JsonSerializer.Deserialize<DrivePreference>("{\"Root\":\"/\",\"Label\":\"old\"}")!.Folders == null);
        });
        check("Overlapping, relative and drive-root folder paths are rejected before scanning", () =>
        {
            foreach (var paths in new[] { new[] { first, Path.Combine(first, "child") }, new[] { first, first }, new[] { "relative" }, new[] { root } })
            {
                try { FolderUsage.Validate(root, paths.Select(p => new FolderPreference { Path = p }).ToArray()); }
                catch (ArgumentException) { continue; }
                throw new InvalidOperationException("Invalid tracking path accepted.");
            }
            Assert(FolderUsage.Validate(root, settings.Folders).Length == 2);
        });
        check("Folder gauge partitions capacity exactly and cannot consume free space", () =>
        {
            var reading = new DriveReading(root, "", 30, 100, Folders: new[] { new FolderReading(first, 0, 20, FolderState.Available), new FolderReading(second, 2, 10, FolderState.Partial) });
            var segments = FolderUsage.Segments(reading);
            Assert(segments.Select(s => s.Bytes).SequenceEqual(new long[] { 20, 10, 40, 30 }));
            segments = FolderUsage.Segments(reading with { Folders = new[] { new FolderReading(first, 0, 120, FolderState.Available) } });
            Assert(segments.Sum(s => s.Bytes) == 100 && segments[^1].Bytes == 30 && segments.All(s => s.Bytes >= 0));
            Assert(FolderUsage.Segments(reading with { Total = null }).Length == 0);
        });
        if (!OperatingSystem.IsWindows())
        {
            check("Folder allocation reports unsupported without accessing Windows APIs", () => Assert(FolderUsage.Scan(new[] { settings }, CancellationToken.None)[root].All(r => r.State == FolderState.Unsupported && r.Bytes == null)));
            return;
        }
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        var junction = Path.Combine(first, "linked");
        try
        {
            foreach (var sparse in new[] { true, false })
            {
                var path = Path.Combine(directory, sparse ? "sparse.bin" : "compressed.bin");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (sparse)
                    {
                        if (!DeviceIoControl(stream.SafeFileHandle, 0x900c4, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        stream.SetLength(16 * 1048576); stream.Position = stream.Length - 1; stream.WriteByte(1);
                    }
                    else
                    {
                        stream.Write(new byte[1048576]); stream.Flush(true);
                        if (!DeviceIoControl(stream.SafeFileHandle, 0x9c040, new byte[] { 2, 0 }, 2, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                    stream.Flush(true);
                }
                check(sparse ? "Sparse files use actual allocated storage instead of logical length" : "Compressed files use actual allocated storage instead of logical length", () =>
                {
                    var low = GetCompressedFileSizeW(path, out var high); Assert(low != uint.MaxValue);
                    var actual = ((long)high << 32) | low;
                    var measuredBytes = FolderUsage.Allocation(path).Bytes;
                    if (actual >= new FileInfo(path).Length || measuredBytes != actual)
                        throw new InvalidOperationException($"Allocation mismatch: sparse={sparse}, measured={measuredBytes}, independent={actual}, logical={new FileInfo(path).Length}, attributes={File.GetAttributes(path)}");
                });
            }
            check("Batched directory entries preserve sparse and compressed physical allocation", () =>
            {
                var expected = Directory.GetFiles(directory).Sum(p => { var low = GetCompressedFileSizeW(p, out var high); return ((long)high << 32) | low; });
                var row = FolderUsage.Scan(new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = directory } } } }, CancellationToken.None)[root][0];
                Assert(row.State == FolderState.Available && row.Bytes == expected);
                var cache = new FolderUsage.Cache();
                var configuration = new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = directory } } } };
                Assert(FolderUsage.Scan(configuration, CancellationToken.None, cache: cache)[root][0].Bytes == expected);
                var reads = cache.DirectoryReads;
                Assert(FolderUsage.Scan(configuration, CancellationToken.None, cache: cache)[root][0].Bytes == expected && cache.DirectoryReads == reads);
            });
            check("Large directories match per-file allocation across every metadata page", () =>
            {
                var many = Path.Combine(directory, "many"); Directory.CreateDirectory(many);
                var content = new byte[2048]; Random.Shared.NextBytes(content); long expected = 0;
                for (var i = 0; i < 1500; i++)
                {
                    var path = Path.Combine(many, $"file-{i:D4}-metadata-page-boundary.bin"); File.WriteAllBytes(path, content);
                    expected += FolderUsage.Allocation(path).Bytes;
                }
                var row = FolderUsage.Scan(new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = many } } } }, CancellationToken.None)[root][0];
                if (row.State != FolderState.Available || row.Skipped != 0 || row.Bytes != expected)
                    throw new InvalidOperationException($"Paged allocation: state={row.State}, skipped={row.Skipped}, actual={row.Bytes}, expected={expected}, firstFile={FolderUsage.Allocation(Directory.GetFiles(many)[0]).Bytes}");
            });
            check("Deep build/cache paths retain allocated bytes beyond the Win32 MAX_PATH boundary", () =>
            {
                var longRoot = Path.Combine(directory, "long");
                var nested = Path.Combine(longRoot, new string('a', 90), new string('b', 90), new string('c', 90));
                Directory.CreateDirectory(nested);
                var path = Path.Combine(nested, "payload.bin"); var content = new byte[65536]; Random.Shared.NextBytes(content); File.WriteAllBytes(path, content);
                Assert(path.Length > 260);
                var rows = FolderUsage.Scan(new[] { new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = longRoot } } } }, CancellationToken.None)[root];
                Assert(rows[0].State == FolderState.Available && rows[0].Skipped == 0 && rows[0].Bytes >= content.Length);
            });
            File.WriteAllBytes(Path.Combine(first, "data.bin"), new byte[65536]);
            File.WriteAllBytes(Path.Combine(second, "other.bin"), new byte[131072]);
            var measured = FolderUsage.Scan(new[] { settings }, CancellationToken.None)[root];
            check("Completed folders publish before the remaining scan and cancellation stops later results", () =>
            {
                using var stop = new CancellationTokenSource(); var published = new List<FolderReading>();
                try { FolderUsage.Scan(new[] { settings }, stop.Token, (_, row) => { published.Add(row); stop.Cancel(); }); }
                catch (OperationCanceledException) { Assert(published.Count == 1 && published[0].Path == first && published[0].Bytes > 0); return; }
                throw new InvalidOperationException("Cancellation after a completed folder did not stop the rest of the scan.");
            });
            check("Actual folder allocation includes file storage and keeps folder identities/colors", () =>
            {
                Assert(measured.All(r => r.State == FolderState.Available && r.CheckedAt.HasValue));
                Assert(measured[0].Bytes >= 65536 && measured[0].Bytes < 131072 && measured[1].Bytes >= 131072 && measured[1].Bytes < 196608);
                Assert(measured[1].Path == second && measured[1].Color == 2);
            });
            if (!CreateHardLinkW(Path.Combine(second, "shared.bin"), Path.Combine(first, "data.bin"), IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            check("Hard links across tracked folders are counted only once for the drive", () => Assert(FolderUsage.Scan(new[] { settings }, CancellationToken.None)[root].Sum(r => r.Bytes) == measured.Sum(r => r.Bytes)));
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(junction); start.ArgumentList.Add(second);
            using (var process = Process.Start(start)!) { process.WaitForExit(); Assert(process.ExitCode == 0); }
            check("Directory junctions are excluded instead of double-counting another folder", () =>
            {
                var rows = FolderUsage.Scan(new[] { settings }, CancellationToken.None)[root];
                Assert(rows.Sum(r => r.Bytes) == measured.Sum(r => r.Bytes) && rows[0].Skipped == 1);
                var linked = new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = junction } } };
                Assert(FolderUsage.Scan(new[] { linked }, CancellationToken.None)[root][0].State == FolderState.Unavailable);
            });
            check("Missing folders are unknown rather than falsely reported as empty", () =>
            {
                var missing = new DrivePreference { Root = root, Folders = new[] { new FolderPreference { Path = Path.Combine(directory, "missing") } } };
                var row = FolderUsage.Scan(new[] { missing }, CancellationToken.None)[root][0]; Assert(row.State == FolderState.Missing && row.Bytes == null);
            });
            check("Cancellation interrupts traversal without publishing a completed measurement", () =>
            {
                using var stop = new CancellationTokenSource(); stop.Cancel();
                try { FolderUsage.Scan(new[] { settings }, stop.Token); }
                catch (OperationCanceledException) { return; }
                throw new InvalidOperationException("Canceled scan completed.");
            });
            await using var monitor = new FolderMonitor(); monitor.Configure(new[] { settings }); monitor.Start();
            var until = DateTime.UtcNow.AddSeconds(10);
            while (monitor.Read(root).Any(r => r.State == FolderState.Scanning) && DateTime.UtcNow < until) await Task.Delay(20);
            check("Background measurement publishes actual folder values", () => Assert(monitor.Read(root).All(r => r.Bytes.HasValue)));
            File.WriteAllBytes(Path.Combine(first, "new.bin"), new byte[65536]); monitor.Configure(new[] { settings });
            until = DateTime.UtcNow.AddSeconds(10);
            while (monitor.Read(root).Any(r => r.State == FolderState.Scanning) && DateTime.UtcNow < until) await Task.Delay(20);
            check("Configuration refresh observes file growth without restarting the monitor", () => Assert(monitor.Read(root)[0].Bytes > measured[0].Bytes));
            monitor.Configure(Array.Empty<DrivePreference>());
            check("Removed folders disappear immediately and stale results cannot restore them", () => Assert(monitor.Read(root).Length == 0));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(directory, true);
        }
    }
}
