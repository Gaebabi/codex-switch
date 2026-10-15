using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

[SupportedOSPlatform("linux")]
internal static class LinuxPackageInstaller
{
    private sealed record Entry(string Path, string Sha256, int Mode);
    private sealed record Package(int Schema, string Version, string Rid, string Revision, Entry[] Files);
    private sealed record State(int Schema, string[] Versions, string DesktopSha256, string IconSha256);
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat
    { [FieldOffset(0)] public uint Mask; [FieldOffset(20)] public uint Uid; [FieldOffset(28)] public ushort Mode; }
    private static IOException Refused() => new("Package installation refused an unmanaged, modified or unsafe path. Existing files and account data are preserved.");
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Sha(string path)
    { VerifyPath(path, false); using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void VerifyPath(string path, bool directory)
    {
        var full = Full(path); if (full == "/") throw Refused();
        var current = "/"; var names = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < names.Length; index++)
        {
            current = Path.Combine(current, names[index]);
            if (statx(-100, current, 0x100, 0xb, out var value) != 0 || (value.Mask & 0xb) != 0xb || value.Uid != 0 && value.Uid != geteuid()) throw Refused();
            var isDirectory = index != names.Length - 1 || directory;
            if ((value.Mode & 0xf000) != (isDirectory ? 0x4000 : 0x8000) ||
                (value.Mode & 0x12) != 0 && !(isDirectory && value.Uid == 0 && (value.Mode & 0x200) != 0) ||
                !isDirectory && (value.Mode & 0xc00) != 0) throw Refused();
        }
    }
    private static void EnsureDirectory(string path)
    {
        path = Full(path);
        if (Directory.Exists(path)) { VerifyPath(path, true); return; }
        var parent = Path.GetDirectoryName(path)!;
        if (parent == path || parent == "/") throw Refused();
        EnsureDirectory(parent); Directory.CreateDirectory(path, (UnixFileMode)0x1c0); VerifyPath(path, true);
    }
    private static bool Exists(string path) => new FileInfo(path).LinkTarget != null || File.Exists(path) || Directory.Exists(path);
    private static string Member(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or "..")) throw Refused();
        var full = Full(Path.Combine(root, relative)); if (!full.StartsWith(Full(root) + "/", StringComparison.Ordinal)) throw Refused(); return full;
    }
    private static bool Version(string version) => System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$");
    private static Package Manifest(string source)
    {
        var path = Path.Combine(source, "package-manifest.json"); VerifyPath(path, false);
        var value = JsonSerializer.Deserialize<Package>(File.ReadAllText(path)) ?? throw Refused();
        if (value.Schema != 1 || string.IsNullOrEmpty(value.Version) || !Version(value.Version) || value.Rid != (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64") ||
            string.IsNullOrEmpty(value.Revision) || !System.Text.RegularExpressions.Regex.IsMatch(value.Revision, "^[0-9a-f]{40}$") || value.Files == null || value.Files.Length == 0 || value.Files.Length > 4096 ||
            value.Files.Any(entry => entry == null || string.IsNullOrEmpty(entry.Path) || string.IsNullOrEmpty(entry.Sha256)) || value.Files.Select(entry => entry.Path).Distinct().Count() != value.Files.Length) throw Refused();
        foreach (var entry in value.Files)
            if (entry.Mode is not (0x1a4 or 0x1ed) || entry.Sha256.Length != 64 || Sha(Member(source, entry.Path)) != entry.Sha256) throw Refused();
        if (!value.Files.Any(entry => entry.Path == "CodexSwitch" && entry.Mode == 0x1ed) || !value.Files.Any(entry => entry.Path == "icons/codex-switch.png")) throw Refused();
        return value;
    }
    private static void Atomic(string path, byte[] bytes)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".codex-switch-" + Guid.NewGuid().ToString("N"));
        using (var file = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = (UnixFileMode)0x180, Options = FileOptions.WriteThrough }))
        { file.Write(bytes); file.Flush(true); }
        File.Move(temporary, path, true);
    }
    private static string Exec(string path)
    {
        if (path.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0) throw Refused();
        var quoted = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return "\"" + quoted.Replace("\\", "\\\\") + "\"";
    }
    public static int Execute(bool uninstall, TextWriter reportWriter)
    {
        if (geteuid() == 0 || getuid() != geteuid()) throw new IOException("Run per-user installation as your normal desktop user, without sudo.");
        var selectedData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (selectedData != null && !Path.IsPathFullyQualified(selectedData)) throw Refused();
        var data = Full(selectedData ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"));
        var prefix = Path.Combine(data, "codex-switch"); var versions = Path.Combine(prefix, "versions");
        var codexHome = Full(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        if (prefix == codexHome || prefix.StartsWith(codexHome + "/", StringComparison.Ordinal) || codexHome.StartsWith(prefix + "/", StringComparison.Ordinal)) throw Refused();
        var statePath = Path.Combine(prefix, ".installation.json"); var current = Path.Combine(prefix, "current");
        var desktop = Path.Combine(data, "applications/codex-switch.desktop"); var icon = Path.Combine(data, "icons/hicolor/1024x1024/apps/codex-switch.png");
        if (uninstall && !Directory.Exists(prefix)) return 0;
        EnsureDirectory(prefix); EnsureDirectory(versions);
        var lockPath = Path.Combine(prefix, ".installation.lock"); if (Exists(lockPath)) VerifyPath(lockPath, false);
        using var installationLock = new FileStream(lockPath, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None, UnixCreateMode = (UnixFileMode)0x180 });
        VerifyPath(lockPath, false);
        State? state = null;
        if (Exists(statePath))
        {
            VerifyPath(statePath, false); state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath)) ?? throw Refused();
            if (state.Schema != 1 || state.Versions == null || state.Versions.Length > 128 || state.Versions.Any(version => string.IsNullOrEmpty(version) || !Version(version)) || state.Versions.Distinct().Count() != state.Versions.Length ||
                state.DesktopSha256?.Length != 64 || state.IconSha256?.Length != 64) throw Refused();
        }
        var link = new FileInfo(current).LinkTarget;
        if (Exists(current) && (state == null || link == null || !state.Versions.Any(version => link == "versions/" + version))) throw Refused();
        if (uninstall)
        {
            if (state == null) throw Refused(); var retained = 0;
            foreach (var process in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(process), out var id) || id == Environment.ProcessId) continue;
                string? executable;
                try { executable = new FileInfo(Path.Combine(process, "exe")).LinkTarget; }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (executable?.StartsWith(versions + "/", StringComparison.Ordinal) == true) throw new IOException("Quit the installed Codex Switch utility before uninstalling its files. Native Codex is not stopped.");
            }
            var packages = new List<(string Directory, Package Manifest)>();
            foreach (var version in state.Versions)
            {
                var directory = Path.Combine(versions, version); if (!Directory.Exists(directory)) continue;
                VerifyPath(directory, true); packages.Add((directory, Manifest(directory)));
            }
            using var resultBytes = new MemoryStream();
            using var result = new Utf8JsonWriter(resultBytes);
            result.WriteStartObject(); result.WriteBoolean("uninstalled", true);
            result.WriteString("scope", "Only recorded package files; account/vault/home/keyring data is never opened or removed.");
            result.WritePropertyName("retainedUnmanagedItems");
            foreach (var (path, expected) in new[] { (desktop, state.DesktopSha256), (icon, state.IconSha256) })
                if (Exists(path)) { if (Sha(path) == expected) File.Delete(path); else retained++; }
            if (link != null) File.Delete(current);
            foreach (var (directory, manifest) in packages)
            {
                var parents = new HashSet<string>();
                foreach (var entry in manifest.Files)
                {
                    var parent = Path.GetDirectoryName(Member(directory, entry.Path));
                    while (parent != null && parent != directory) { parents.Add(parent); parent = Path.GetDirectoryName(parent); }
                }
                foreach (var entry in manifest.Files) File.Delete(Member(directory, entry.Path));
                File.Delete(Path.Combine(directory, "package-manifest.json"));
                foreach (var child in parents.OrderByDescending(path => path.Length))
                { VerifyPath(child, true); if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child); }
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); else retained++;
            }
            File.Delete(statePath);
            result.WriteNumberValue(retained); result.WriteEndObject(); result.Flush();
            reportWriter.WriteLine(Encoding.UTF8.GetString(resultBytes.GetBuffer(), 0, checked((int)resultBytes.Length))); return 0;
        }
        var source = Full(AppContext.BaseDirectory); var package = Manifest(source);
        if (package.Version != typeof(LinuxPackageInstaller).Assembly.GetName().Version!.ToString(3)) throw Refused();
        var desktopBytes = Encoding.UTF8.GetBytes("[Desktop Entry]\nType=Application\nName=Codex Switch\nComment=Linux source preview; account handoff is under native acceptance\nExec=/usr/bin/env -C " + Exec(current) + " ./CodexSwitch\nIcon=codex-switch\nTerminal=false\nCategories=Utility;Development;\nStartupNotify=true\n");
        var iconBytes = File.ReadAllBytes(Member(source, "icons/codex-switch.png"));
        foreach (var (path, expected) in new[] { (desktop, state?.DesktopSha256), (icon, state?.IconSha256) })
            if (Exists(path) && (expected == null || Sha(path) != expected)) throw Refused();
        var target = Path.Combine(versions, package.Version);
        if (Exists(target))
        { if (state == null || !state.Versions.Contains(package.Version) || File.ReadAllText(Path.Combine(target, "package-manifest.json")) != File.ReadAllText(Path.Combine(source, "package-manifest.json"))) throw Refused(); Manifest(target); }
        else
        {
            var staging = Path.Combine(versions, ".install-" + Guid.NewGuid().ToString("N")); EnsureDirectory(staging);
            foreach (var entry in package.Files)
            {
                var output = Member(staging, entry.Path); EnsureDirectory(Path.GetDirectoryName(output)!);
                File.Copy(Member(source, entry.Path), output, false); File.SetUnixFileMode(output, (UnixFileMode)entry.Mode);
            }
            File.Copy(Path.Combine(source, "package-manifest.json"), Path.Combine(staging, "package-manifest.json"));
            Manifest(staging); Directory.Move(staging, target);
        }
        EnsureDirectory(Path.GetDirectoryName(desktop)!); EnsureDirectory(Path.GetDirectoryName(icon)!);
        var next = new State(1, (state?.Versions ?? Array.Empty<string>()).Append(package.Version).Distinct().ToArray(), Digest(desktopBytes), Digest(iconBytes));
        Atomic(statePath, JsonSerializer.SerializeToUtf8Bytes(next)); Atomic(desktop, desktopBytes); Atomic(icon, iconBytes);
        var pending = Path.Combine(prefix, ".current-" + Guid.NewGuid().ToString("N")); File.CreateSymbolicLink(pending, "versions/" + package.Version);
        if (rename(pending, current) != 0) throw new IOException("The package activation link could not be atomically replaced (errno " + Marshal.GetLastPInvokeError() + ").");
        reportWriter.WriteLine(JsonSerializer.Serialize(new { installed = true, version = package.Version, package.Rid, scope = "Per-user package/menu/icon files only; account/vault/home/keyring data is never opened or migrated." })); return 0;
    }
    [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
    [DllImport("libc")] private static extern uint getuid();
    [DllImport("libc")] private static extern uint geteuid();
    [DllImport("libc", SetLastError = true)] private static extern int rename([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);
}
