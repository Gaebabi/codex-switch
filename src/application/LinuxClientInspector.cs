using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record LinuxClientInspection(string CliVersion, string Provenance, string? PackageVersion, bool DesktopBinaryApproved, ClientCredentialPolicy Policy);

internal static class LinuxClientInspector
{
    public static async Task<LinuxClientInspection> ReadAsync(string home, LinuxClientSelection? selection, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux client inspection requires Linux.");
        home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        if (!Directory.Exists(home)) throw new DirectoryNotFoundException("Client inspection requires an existing explicitly selected home.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var installation = await Task.Run(() => LinuxClientDiscovery.Open(selection, deadline.Token), deadline.Token).ConfigureAwait(false);
        await using var rpc = CodexRpc.InspectLinuxConfiguration(installation.Cli, home);
        await rpc.InitializeAsync(deadline.Token).ConfigureAwait(false);
        var hello = rpc.Initialization ?? throw Unsupported();
        if (hello.ValueKind != JsonValueKind.Object || Text(hello, "platformOs") != "linux" || Text(hello, "platformFamily") != "unix" || Text(hello, "codexHome") != home)
            throw Unsupported();
        const string prefix = "codex_account_desk/"; var userAgent = Text(hello, "userAgent");
        if (!userAgent.StartsWith(prefix, StringComparison.Ordinal)) throw Unsupported();
        var end = userAgent.IndexOf(' ', prefix.Length); if (end < 0) throw Unsupported();
        var version = userAgent[prefix.Length..end];
        if (version is not ("0.162.0-alpha.17.2" or "0.162.1")) throw Unsupported();
        var configuration = await rpc.RequestAsync("config/read", new { includeLayers = true, cwd = home }, deadline.Token).ConfigureAwait(false);
        var requirements = await rpc.RequestAsync("configRequirements/read", null, deadline.Token).ConfigureAwait(false);
        var policy = ClientCredentialPolicy.Parse(configuration, requirements);
        return new(version, installation.Provenance, installation.PackageVersion, installation.Desktop != null, policy);
    }
    private static string Text(JsonElement source, string name) => source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Unsupported();
    private static InvalidDataException Unsupported() => new("The Linux client version or configuration protocol is not verified.");
}

internal static class LinuxClientDiagnostics
{
    public static async Task<int> VerifyAsync(string home, string reportPath, LinuxClientSelection? selection)
    {
        home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)); reportPath = Path.GetFullPath(reportPath);
        if (reportPath == home || reportPath.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Client inspection reports must be outside the selected Codex home.");
        object report; var result = 0;
        try
        {
            var inspection = await LinuxClientInspector.ReadAsync(home, selection, CancellationToken.None).ConfigureAwait(false);
            report = new { schema = 1, inspectionAvailable = true, inspection.CliVersion, inspection.Provenance, inspection.PackageVersion,
                inspection.DesktopBinaryApproved, credentialStore = inspection.Policy.Store, fileCredentialsAllowed = inspection.Policy.FileCredentialsAllowed,
                workspaceRestricted = inspection.Policy.ForcedWorkspaceId != null, requiresApiLogin = inspection.Policy.ForcedLoginMethod == "api",
                configurationScope = "SelectedHomeContext",
                desktopHandoffAvailable = false,
                scope = "Native executable identity and effective config/requirements inspection only; no login, inference, switching or desktop/IPC/resumption acceptance. Paths, workspace IDs and full config are not exported." };
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or OperationCanceledException or TimeoutException or
            System.ComponentModel.Win32Exception or PlatformNotSupportedException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or CodexRequestException)
        {
            result = 1;
            report = new { schema = 1, inspectionAvailable = false, reason = error is OperationCanceledException or TimeoutException ? "TimedOut" : "UnverifiedClientOrPolicy",
                fileCredentialsAllowed = false, desktopHandoffAvailable = false,
                scope = "Client inspection refused; no fallback, personal diagnostics or credential operation is exported." };
        }
        LinuxPrivateFiles.WriteNew(reportPath, JsonSerializer.SerializeToUtf8Bytes(report, report.GetType(), new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
}
