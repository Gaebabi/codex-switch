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
        File.WriteAllText(System.IO.Path.Combine(Path, "owner.json"), JsonSerializer.Serialize(new { pid = owner.Id, startedAt = owner.StartTime.ToUniversalTime().Ticks }));
    }
    public string ReadAuth()
    {
        var file = System.IO.Path.Combine(Path, "auth.json");
        return OperatingSystem.IsLinux() ? new System.Text.UTF8Encoding(false, true).GetString(SecureFiles.ReadPrivateBytes(file)) : File.ReadAllText(file);
    }
    public ValueTask DisposeAsync() { SecureFiles.DeleteOwnedRuntime(_root, Path); return ValueTask.CompletedTask; }
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
