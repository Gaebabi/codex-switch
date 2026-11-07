using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class RuntimeHome : IAsyncDisposable
{
    public string Path { get; }
    private readonly string _root;
    public RuntimeHome(string dataDirectory, string? authJson)
    {
        _root = System.IO.Path.Combine(dataDirectory, "runtime");
        Directory.CreateDirectory(_root);
        Path = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
        SecureFiles.CreatePrivateDirectory(Path);
        if (authJson != null) SecureFiles.AtomicWriteText(System.IO.Path.Combine(Path, "auth.json"), authJson);
        using var owner = Process.GetCurrentProcess();
        SecureFiles.AtomicWriteText(System.IO.Path.Combine(Path, "owner.json"), JsonSerializer.Serialize(new { pid = owner.Id, startedAt = owner.StartTime.ToUniversalTime().Ticks }));
    }
    public string ReadAuth()
    {
        var file = System.IO.Path.Combine(Path, "auth.json");
        return OperatingSystem.IsLinux() ? new System.Text.UTF8Encoding(false, true).GetString(SecureFiles.ReadPrivateBytes(file)) : File.ReadAllText(file);
    }
    public async ValueTask DisposeAsync()
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try { SecureFiles.DeleteOwnedRuntime(_root, Path); return; }
            // Windows can retain SQLite handles briefly after the owned query
            // process exits. Revalidate the same private path on every attempt;
            // permissions, link refusals and persistent failures still surface.
            catch (IOException error) when (OperatingSystem.IsWindows() && ((error.HResult & 0xffff) is 32 or 33) &&
                Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
            { await Task.Delay(50).ConfigureAwait(false); }
        }
    }
    public static void CleanupStale(string dataDirectory)
    {
        var root = System.IO.Path.Combine(dataDirectory, "runtime");
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.GetDirectories(root))
        {
            if (!Guid.TryParseExact(System.IO.Path.GetFileName(path), "N", out _)) continue;
            var ownerFile = System.IO.Path.Combine(path, "owner.json");
            if (!File.Exists(ownerFile)) continue;
            using var owner = JsonDocument.Parse(File.ReadAllText(ownerFile));
            var pid = JsonValue.Int(owner.RootElement, "pid");
            var startTicks = JsonValue.Long(owner.RootElement, "startedAt");
            if (pid == null || startTicks == null) continue;
            var alive = false;
            try { using var process = Process.GetProcessById(pid.Value); alive = process.StartTime.ToUniversalTime().Ticks == startTicks; }
            catch (ArgumentException) { }
            if (!alive) SecureFiles.DeleteOwnedRuntime(root, path);
        }
    }
}
