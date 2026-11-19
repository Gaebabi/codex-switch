using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace CodexSwitch.Diagnostics;

internal sealed record InstallationReceipt(string NativeExecutable, string RelayExecutable, string? PreviousOverride, string? CodexHome = null);
internal sealed record DiagnosticBootstrap(string StateDirectory);

internal sealed class RelayInstallation
{
    private readonly string _directory;
    private readonly Func<string?> _get;
    private readonly Action<string?> _set;
    private readonly string _executable;
    private readonly string? _home;
    private string Receipt => Path.Combine(_directory, "installation.json");
    internal string Events => Path.Combine(_directory, "events.jsonl");
    internal string NewEvents() => Path.Combine(_directory, "events-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
    internal string[] EventFiles() => Directory.Exists(_directory) ? Directory.GetFiles(_directory, "events*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(16).ToArray() : Array.Empty<string>();
    internal bool Enabled => File.Exists(Receipt) && Same(_get(), Read().RelayExecutable);
    internal string RequestedMode
    {
        get
        {
            if (!File.Exists(Receipt)) return "unconfigured";
            var receipt = Read(); var launch = _get();
            return Same(launch, receipt.RelayExecutable) ? "on_requested" : launch == null ? "off_requested"
                : IsCustomLaunch(launch, receipt) ? "custom_backend" : "external_override";
        }
    }

    internal RelayInstallation(string? directory = null, string? executable = null, Func<string?>? get = null, Action<string?>? set = null, string? home = null)
    {
        _executable = executable ?? Environment.ProcessPath!;
        _directory = Path.GetFullPath(directory ?? ResolveDirectory(_executable));
        _home = Path.GetFullPath(home ?? Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        _get = get ?? (() => Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User));
        _set = set ?? (value =>
        {
            Environment.SetEnvironmentVariable("CODEX_CLI_PATH", value, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("CODEX_CLI_PATH", value, EnvironmentVariableTarget.Process);
        });
    }

    private static string ResolveDirectory(string executable)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var own = Path.Combine(folder, "CodexSwitch.Diagnostics.json");
        if (File.Exists(own))
        {
            var bootstrap = JsonSerializer.Deserialize<DiagnosticBootstrap>(File.ReadAllBytes(own)) ?? throw new IOException("Diagnostic storage binding is empty.");
            if (!Path.IsPathFullyQualified(bootstrap.StateDirectory)) throw new IOException("Diagnostic storage binding must be absolute.");
            return bootstrap.StateDirectory;
        }
        var paired = Path.GetFullPath(Path.Combine(folder, "..", "backend", "CodexSwitch.Backend.json"));
        if (File.Exists(paired))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(paired));
            var data = document.RootElement.GetProperty("Directory").GetString();
            if (data == null || !Path.IsPathFullyQualified(data)) throw new IOException("Paired Switch storage binding is invalid.");
            return Path.Combine(data, "diagnostic-relay");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountDesk", "diagnostic-relay");
    }

    private void BindStorage()
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_executable))!, "CodexSwitch.Diagnostics.json");
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<DiagnosticBootstrap>(File.ReadAllBytes(path));
            if (existing == null || !Same(Path.GetFullPath(existing.StateDirectory), _directory)) throw new IOException("The diagnostic executable is already bound to different storage.");
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new DiagnosticBootstrap(_directory))); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void PrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Diagnostic storage must not be a directory link.");
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new IOException("Current user is unavailable.");
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(owner);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal InstallationReceipt Read() => JsonSerializer.Deserialize<InstallationReceipt>(File.ReadAllBytes(Receipt)) ?? throw new IOException("Missing installation receipt.");
    private static bool Same(string? a, string? b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private string PackageRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_executable)!, "..", ".."));
    private bool IsOwnedBackend(string? path)
    {
        if (path == null || !Path.IsPathFullyQualified(path) || !Same(Path.GetFileName(path), "CodexSwitch.Backend.exe")) return false;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Same(Path.GetFileName(folder), "backend") && Same(Path.GetDirectoryName(Path.GetDirectoryName(folder)!), PackageRoot);
    }
    private bool IsCustomLaunch(string? launch, InstallationReceipt receipt) => Same(launch, receipt.PreviousOverride) && IsOwnedBackend(launch);

    internal string? PairedBackend(InstallationReceipt receipt)
    {
        if (!IsOwnedBackend(receipt.PreviousOverride)) return null;
        var backend = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_executable)!, "..", "backend", "CodexSwitch.Backend.exe"));
        var bootstrap = Path.ChangeExtension(backend, ".json");
        if (!File.Exists(backend) || !File.Exists(bootstrap)) throw new IOException("The paired Switch model backend is missing.");
        using var profile = JsonDocument.Parse(File.ReadAllBytes(bootstrap));
        string Value(string key) => profile.RootElement.GetProperty(key).GetString() ?? throw new IOException("The Switch backend binding is incomplete.");
        if (!Same(Path.GetFullPath(Value("NativeExecutable")), receipt.NativeExecutable) ||
            !Same(Path.GetFullPath(Value("Home")), receipt.CodexHome) ||
            !Same(Path.GetFullPath(Path.Combine(Value("Directory"), "diagnostic-relay")), _directory))
            throw new IOException("The paired model backend and diagnostic relay must use the same native executable, home and data directory.");
        return backend;
    }
    internal void Enable(string native)
    {
        native = Path.GetFullPath(native);
        var nativeHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        if (_home != null && !Same(Path.GetFullPath(_home), Path.GetFullPath(nativeHome)))
            throw new IOException("This custom home differs from the native launch environment. Align CODEX_HOME before enabling; native OFF must preserve the same home without an adapter.");
        if (!File.Exists(native) || !Path.GetFileName(native).Equals(OperatingSystem.IsWindows() ? "codex.exe" : "codex", StringComparison.OrdinalIgnoreCase) || Same(native, _executable))
            throw new IOException("Select the original codex executable, not a Switch adapter.");
        PrivateDirectory(_directory);
        using var lease = new FileStream(Path.Combine(_directory, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        BindStorage();
        var previous = _get();
        InstallationReceipt receipt;
        if (File.Exists(Receipt))
        {
            receipt = Read();
            if (!Same(receipt.NativeExecutable, native) || !Same(receipt.RelayExecutable, _executable) || !Same(receipt.CodexHome, _home)) throw new IOException("Installation identity changed; retain the original installation for recovery.");
            if (previous != null && !Same(previous, native) && !Same(previous, receipt.RelayExecutable) && !Same(previous, receipt.PreviousOverride)) throw new IOException("Another program changed CODEX_CLI_PATH; no settings were changed.");
        }
        else
        {
            receipt = new(native, _executable, previous, _home);
            _ = PairedBackend(receipt);
            // Commit recovery information before publishing the override.
            var staging = Receipt + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(staging, JsonSerializer.SerializeToUtf8Bytes(receipt));
                File.Move(staging, Receipt);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        _ = PairedBackend(receipt);
        _set(receipt.RelayExecutable);
        if (!Enabled) throw new IOException("The launch override was not saved.");
    }

    internal void Disable()
    {
        using var lease = new FileStream(Path.Combine(_directory, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var receipt = Read();
        var launch = _get();
        if (launch != null && !Same(launch, receipt.RelayExecutable) && !IsCustomLaunch(launch, receipt)) throw new IOException("Another program changed CODEX_CLI_PATH; refusing to overwrite it.");
        // Restore bundled native discovery, not a pinned executable that a Codex update can remove.
        _set(null);
        if (_get() != null) throw new IOException("Native launch restoration was not saved.");
    }
    internal void Upgrade()
    {
        using var lease = new FileStream(Path.Combine(_directory, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Read(); var launch = _get();
        var enabled = Same(launch, previous.RelayExecutable);
        if (launch != null && !enabled && !IsCustomLaunch(launch, previous)) throw new IOException("Another program changed CODEX_CLI_PATH; no installation settings were changed.");
        BindStorage();
        if (Same(previous.RelayExecutable, _executable)) return;
        if (!File.Exists(_executable)) throw new IOException("The new diagnostic executable is missing.");
        void Write(InstallationReceipt value)
        {
            var staging = Receipt + "." + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(staging, JsonSerializer.SerializeToUtf8Bytes(value)); File.Move(staging, Receipt, true); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        Write(previous with { RelayExecutable = _executable });
        try
        {
            if (enabled) _set(_executable);
            if (!Same(_get(), enabled ? _executable : launch)) throw new IOException("The diagnostic launch override changed during upgrade.");
        }
        catch
        {
            if (Same(_get(), launch)) Write(previous);
            throw;
        }
    }
    internal object Status() => new { mode = RequestedMode, restartRequiredForExistingDesktop = true,
        configuredLauncher = _get(), stateDirectory = _directory, receiptExecutable = File.Exists(Receipt) ? Read().RelayExecutable : null, events = File.Exists(Events) ? Events : null };
}
