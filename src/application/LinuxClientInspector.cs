using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record LinuxClientInspection(string CliVersion, string Provenance, string? PackageVersion, bool DesktopBinaryApproved, ClientCredentialPolicy Policy);

internal static class LinuxClientInspector
{
    public static async Task<LinuxClientInspection> ReadAsync(string home, LinuxClientSelection? selection, CancellationToken cancellation, string? context = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await using var session = await LinuxClientSession.OpenAsync(home, context, selection, deadline.Token).ConfigureAwait(false);
        var policy = await session.ReadPolicyAsync(deadline.Token).ConfigureAwait(false);
        return new(session.CliVersion, session.Provenance, session.PackageVersion, session.DesktopBinaryApproved, policy);
    }
}

internal static class LinuxClientDiagnostics
{
    public static async Task<int> VerifyAsync(string home, string reportPath, LinuxClientSelection? selection, string? context = null)
    {
        home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)); reportPath = Path.GetFullPath(reportPath);
        if (home == Path.GetPathRoot(home) || reportPath == home || reportPath.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Client inspection reports must be outside the selected Codex home.");
        object report; var result = 0;
        try
        {
            var inspection = await LinuxClientInspector.ReadAsync(home, selection, CancellationToken.None, context).ConfigureAwait(false);
            report = new { schema = 1, inspectionAvailable = true, inspection.CliVersion, inspection.Provenance, inspection.PackageVersion,
                inspection.DesktopBinaryApproved, credentialStore = inspection.Policy.Store, fileCredentialsAllowed = inspection.Policy.FileCredentialsAllowed,
                workspaceRestricted = inspection.Policy.ForcedWorkspaceId != null, requiresApiLogin = inspection.Policy.ForcedLoginMethod == "api",
                configurationScope = context == null ? "SelectedHomeContext" : "SelectedDirectoryContext",
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
