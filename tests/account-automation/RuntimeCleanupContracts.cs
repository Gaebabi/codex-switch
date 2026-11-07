using CodexAccountDesk;
using System.Diagnostics;

internal static class RuntimeCleanupContracts
{
    public static async Task Run(Action<string, bool> check)
    {
        var parent = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") : Path.GetTempPath();
        var fixture = Path.Combine(parent, "codex-runtime-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var home = new RuntimeHome(fixture, null);
            var path = home.Path;
            await home.DisposeAsync(); await home.DisposeAsync();
            check("Completed runtime cleanup remains immediate and repeatable", !Directory.Exists(path));
            if (!OperatingSystem.IsWindows()) return; // Unix unlink does not model Windows sharing rules.

            home = new RuntimeHome(fixture, null); path = home.Path;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var held = new FileStream(Path.Combine(path, "goals_1.sqlite-shm"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                var pending = CancelAndClean(home, cancelled.Token);
                check("A short native sharing violation waits asynchronously instead of replacing cancellation", !pending.IsCompleted);
                held.Dispose();
                var originalCancellation = false;
                try { await pending; }
                catch (OperationCanceledException error) when (error.CancellationToken == cancelled.Token) { originalCancellation = true; }
                check("Released SQLite-style locks preserve the original cancellation and remove only its runtime", originalCancellation && !Directory.Exists(path));
            }
            finally { held.Dispose(); await home.DisposeAsync(); }

            home = new RuntimeHome(fixture, null); path = home.Path;
            held = new FileStream(Path.Combine(path, "goals_1.sqlite-shm"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                var clock = Stopwatch.StartNew(); var refused = false;
                try { await home.DisposeAsync(); }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { refused = true; }
                check("A persistent sharing lock surfaces its original error after a bounded wait", refused && clock.Elapsed >= TimeSpan.FromSeconds(1.5) && clock.Elapsed < TimeSpan.FromSeconds(8) && Directory.Exists(path));
            }
            finally { held.Dispose(); await home.DisposeAsync(); }

            home = new RuntimeHome(fixture, null); path = home.Path;
            var denied = Path.Combine(path, "readonly-fixture.bin"); File.WriteAllText(denied, "Synthetic private cleanup fixture");
            File.SetAttributes(denied, FileAttributes.ReadOnly);
            try
            {
                var permissionRefused = false;
                try { await home.DisposeAsync(); } catch (UnauthorizedAccessException) { permissionRefused = true; }
                check("Nonsharing permission failures are never swallowed or converted to success", permissionRefused && File.Exists(denied));
            }
            finally { File.SetAttributes(denied, FileAttributes.Normal); await home.DisposeAsync(); }
        }
        finally
        {
            var target = Path.GetFullPath(fixture);
            if (!target.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(target).StartsWith("codex-runtime-cleanup-", StringComparison.Ordinal)) throw new IOException("Unexpected fixture cleanup target");
            Directory.Delete(target, true);
        }
    }

    private static async Task CancelAndClean(RuntimeHome home, CancellationToken token)
    {
        await using (home) { token.ThrowIfCancellationRequested(); }
    }
}
