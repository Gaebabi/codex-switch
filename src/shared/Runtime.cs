using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record CodexInstallation(string CliPath, string DesktopPath);

internal static class RuntimeLocator
{
    public static async Task<CodexInstallation> FindAsync(CancellationToken cancellation)
    {
        if (OperatingSystem.IsMacOS()) return await MacDesktop.FindAsync(cancellation);
        var desktop = await FindDesktopAsync(cancellation);
        var candidates = new List<string>();
        var overrideCli = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI");
        if (!string.IsNullOrWhiteSpace(overrideCli)) candidates.Add(overrideCli);
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (entry.Trim(' ', '"').Length > 0) candidates.Add(Path.Combine(entry.Trim(' ', '"'), "codex.exe"));
        var installedBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(installedBin)) candidates.AddRange(Directory.GetDirectories(installedBin).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d => Path.Combine(d, "codex.exe")));
        if (desktop.Length > 0) candidates.Add(Path.Combine(Path.GetDirectoryName(desktop)!, "resources", "codex.exe"));
        var cli = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Codex가 설치되지 않았습니다. Codex 데스크톱 앱 또는 CLI를 먼저 설치해 주세요.");
        return new CodexInstallation(Path.GetFullPath(cli), desktop);
    }
    private static async Task<string> FindDesktopAsync(CancellationToken cancellation)
    {
        var overrideDesktop = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP");
        if (!string.IsNullOrWhiteSpace(overrideDesktop))
        {
            if (!File.Exists(overrideDesktop)) throw new FileNotFoundException("지정한 Codex 앱 경로가 없습니다.");
            return Path.GetFullPath(overrideDesktop);
        }
        foreach (var process in Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("Codex")))
        {
            using (process)
            {
                string? path;
                try { path = process.MainModule?.FileName; }
                catch (System.ComponentModel.Win32Exception) { continue; }
                catch (InvalidOperationException) { continue; }
                if (path != null && (path.Contains("WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("OpenAI\\Codex", StringComparison.OrdinalIgnoreCase) && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))) return path;
            }
        }
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Get-AppxPackage OpenAI.Codex | Select-Object -ExpandProperty InstallLocation" }) start.ArgumentList.Add(argument);
        using var lookup = Process.Start(start) ?? throw new IOException("Codex 설치 경로를 조회할 수 없습니다.");
        var output = lookup.StandardOutput.ReadToEndAsync(cancellation);
        var errors = lookup.StandardError.ReadToEndAsync(cancellation);
        await lookup.WaitForExitAsync(cancellation);
        await errors;
        var packagePath = (await output).Trim();
        if (lookup.ExitCode != 0) throw new IOException("Windows에서 Codex 설치 정보를 읽을 수 없습니다.");
        foreach (var name in new[] { "ChatGPT.exe", "Codex.exe" })
        {
            var candidate = Path.Combine(packagePath, "app", name);
            if (packagePath.Length > 0 && File.Exists(candidate)) return candidate;
        }
        return "";
    }
    public static bool IsDesktopRunning(string executable)
    {
        if (OperatingSystem.IsMacOS()) return MacDesktop.IsRunning(executable);
        if (executable.Length == 0) return false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { throw new IOException("Codex의 실행 상태를 확인할 수 없어 계정 전환을 중단했습니다."); }
            }
        }
        return false;
    }
    public static void LaunchDesktop(string executable)
    {
        if (OperatingSystem.IsMacOS()) { MacDesktop.Launch(executable); return; }
        if (executable.Length == 0 || !File.Exists(executable)) throw new FileNotFoundException("Codex 데스크톱 앱을 찾을 수 없습니다. 계정은 적용됐으므로 Codex를 직접 실행해 주세요.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        Process.Start(start)?.Dispose();
    }
    public static void OpenDesktopThread(string executable, string id)
    {
        if (!Guid.TryParse(id, out _)) throw new InvalidDataException("채팅 ID가 올바르지 않습니다.");
        if (OperatingSystem.IsMacOS()) { MacDesktop.Launch(executable, "codex://threads/" + id); return; }
        if (!File.Exists(executable)) throw new FileNotFoundException("Codex 데스크톱 앱을 찾을 수 없습니다.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("codex://threads/" + id);
        Process.Start(start)?.Dispose();
    }
}

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
    public string ReadAuth() => File.ReadAllText(System.IO.Path.Combine(Path, "auth.json"));
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
