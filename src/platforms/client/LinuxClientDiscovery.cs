using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace CodexAccountDesk;

internal sealed record LinuxClientSelection(string CliPath, string ExpectedSha256);

internal sealed class LinuxClientInstallation : IDisposable
{
    public LinuxExecutable Cli { get; }
    public LinuxExecutable? Desktop { get; }
    public string Provenance { get; }
    public string? PackageVersion { get; }
    internal LinuxClientInstallation(LinuxExecutable cli, LinuxExecutable? desktop, string provenance, string? packageVersion)
    { Cli = cli; Desktop = desktop; Provenance = provenance; PackageVersion = packageVersion; }
    public void Dispose() { Desktop?.Dispose(); Cli.Dispose(); }
}

internal static class LinuxClientDiscovery
{
    internal const string CliPath = "/usr/lib/chatgpt/resources/codex", DesktopPath = "/usr/lib/chatgpt/ChatGPT";
    public static LinuxClientInstallation Open(LinuxClientSelection? selection = null, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Linux client discovery requires native Linux x64 or ARM64.");
        cancellation.ThrowIfCancellationRequested();
        if (selection != null) return new(LinuxExecutable.Verify(selection.CliPath, selection.ExpectedSha256, false, cancellation), null, "ExplicitChecksum", null);
        using var manifest = ApprovedManifest(); var root = manifest.RootElement;
        var architecture = root.GetProperty("architectures").GetProperty(RuntimeInformation.ProcessArchitecture.ToString());
        VerifyPackageStatus(LinuxExecutable.ReadSystemText("/var/lib/dpkg/status", cancellation), root.GetProperty("packageVersion").GetString()!, architecture.GetProperty("debianArchitecture").GetString()!);
        var cli = LinuxExecutable.Verify(CliPath, architecture.GetProperty("cliSha256").GetString()!, true, cancellation);
        try
        {
            var desktop = LinuxExecutable.Verify(DesktopPath, architecture.GetProperty("desktopSha256").GetString()!, true, cancellation);
            return new(cli, desktop, "ApprovedSystemPackage", root.GetProperty("packageVersion").GetString());
        }
        catch { cli.Dispose(); throw; }
    }
    internal static JsonDocument ApprovedManifest()
    {
        using var stream = typeof(LinuxClientDiscovery).Assembly.GetManifestResourceStream("CodexAccountDesk.LinuxClients.json") ?? throw new InvalidDataException("Approved Linux client metadata is unavailable.");
        return JsonDocument.Parse(stream);
    }
    internal static void VerifyPackageStatus(string text, string version, string architecture)
    {
        var packages = new List<Dictionary<string, string>>(); var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (text.Replace("\r\n", "\n") + "\n\n").Split('\n'))
        {
            if (line.Length == 0)
            {
                if (fields.GetValueOrDefault("Package") == "chatgpt") packages.Add(fields);
                fields = new(StringComparer.Ordinal); continue;
            }
            if (char.IsWhiteSpace(line[0])) continue;
            var colon = line.IndexOf(':'); if (colon <= 0 || !fields.TryAdd(line[..colon], line[(colon + 1)..].Trim()))
                throw new InvalidDataException("Linux package installation metadata is invalid.");
        }
        if (packages.Count != 1 || packages[0].GetValueOrDefault("Status") != "install ok installed" ||
            packages[0].GetValueOrDefault("Version") != version || packages[0].GetValueOrDefault("Architecture") != architecture)
            throw new IOException("The Linux client package is not an approved installed version. Client-dependent actions remain unavailable.");
    }
}
