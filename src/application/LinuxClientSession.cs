using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal enum LinuxAccountPolicyFailure { CredentialsUnavailable, WorkspaceMismatch, AsyncPreflightRequired }
internal sealed class LinuxAccountPolicyException : InvalidOperationException
{
    public LinuxAccountPolicyFailure Failure { get; }
    public LinuxAccountPolicyException(LinuxAccountPolicyFailure failure) : base(failure switch
    {
        LinuxAccountPolicyFailure.WorkspaceMismatch => "The selected account does not match this Codex context's required workspace.",
        LinuxAccountPolicyFailure.AsyncPreflightRequired => "Linux account operations require a fresh asynchronous client policy check.",
        _ => "This Codex context does not allow ChatGPT file credentials. Its configuration has not been changed."
    }) { Failure = failure; }
}

internal sealed class LinuxClientSession : IAsyncDisposable
{
    private readonly LinuxClientInstallation _installation;
    private readonly CodexRpc _rpc;
    public string Home { get; }
    public string Context { get; }
    public string CliVersion { get; }
    public CodexInstallation Installation => new(_installation.Cli.Path, _installation.Desktop?.Path ?? "");
    public string Provenance => _installation.Provenance;
    public string? PackageVersion => _installation.PackageVersion;
    public bool DesktopBinaryApproved => _installation.Desktop != null;
    private LinuxClientSession(LinuxClientInstallation installation, CodexRpc rpc, string home, string context, string version)
    { _installation = installation; _rpc = rpc; Home = home; Context = context; CliVersion = version; }
    public static string DirectoryPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full == Path.GetPathRoot(full)) throw new ArgumentException("The filesystem root cannot be a client account context.");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Client account checks require an existing explicitly selected directory.");
        return full;
    }
    public static async Task<LinuxClientSession> OpenAsync(string home, string? context, LinuxClientSelection? selection, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux client sessions require Linux.");
        home = DirectoryPath(home); context = DirectoryPath(context ?? home);
        LinuxPrivateFiles.CreateDirectory(home);
        _ = LinuxPrivateFiles.Exists(home, "auth.json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var installation = await Task.Run(() => LinuxClientDiscovery.Open(selection, deadline.Token), deadline.Token).ConfigureAwait(false);
        CodexRpc? rpc = null;
        try
        {
            rpc = CodexRpc.InspectLinuxConfiguration(installation.Cli, home);
            await rpc.InitializeAsync(deadline.Token).ConfigureAwait(false);
            var version = ValidateInitialization(rpc, home);
            return new(installation, rpc, home, context, version);
        }
        catch
        {
            try { if (rpc != null) await rpc.DisposeAsync().ConfigureAwait(false); }
            finally { installation.Dispose(); }
            throw;
        }
    }
    internal static string ValidateInitialization(CodexRpc rpc, string home)
    {
        var hello = rpc.Initialization ?? throw Unsupported();
        if (hello.ValueKind != JsonValueKind.Object || Text(hello, "platformOs") != "linux" || Text(hello, "platformFamily") != "unix" || Text(hello, "codexHome") != home) throw Unsupported();
        const string prefix = "codex_account_desk/"; var agent = Text(hello, "userAgent");
        if (!agent.StartsWith(prefix, StringComparison.Ordinal)) throw Unsupported();
        var end = agent.IndexOf(' ', prefix.Length); if (end < 0) throw Unsupported();
        var version = agent[prefix.Length..end];
        if (version is not ("0.162.0-alpha.17.2" or "0.162.1")) throw Unsupported();
        return version;
    }
    private static string Text(JsonElement source, string name) => source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Unsupported();
    private static InvalidDataException Unsupported() => new("The Linux client version or configuration protocol is not verified.");
    internal static async Task<ClientCredentialPolicy> ReadPolicyAsync(CodexRpc rpc, string context, CancellationToken cancellation)
    {
        var configuration = await rpc.RequestAsync("config/read", new { includeLayers = true, cwd = context }, cancellation).ConfigureAwait(false);
        var requirements = await rpc.RequestAsync("configRequirements/read", null, cancellation).ConfigureAwait(false);
        return ClientCredentialPolicy.Parse(configuration, requirements);
    }
    public Task<ClientCredentialPolicy> ReadPolicyAsync(CancellationToken cancellation) => ReadPolicyAsync(_rpc, Context, cancellation);
    internal static void RequireFile(ClientCredentialPolicy policy, string? workspace = null)
    {
        if (!policy.FileCredentialsAllowed) throw new LinuxAccountPolicyException(LinuxAccountPolicyFailure.CredentialsUnavailable);
        if (workspace != null && !policy.AllowsAccount(workspace)) throw new LinuxAccountPolicyException(LinuxAccountPolicyFailure.WorkspaceMismatch);
    }
    public async Task<ClientCredentialPolicy> RequireFileAsync(CancellationToken cancellation, string? workspace = null)
    {
        var policy = await ReadPolicyAsync(cancellation).ConfigureAwait(false); RequireFile(policy, workspace); return policy;
    }
    internal async Task<JsonElement> MetadataAsync(string method, object parameters, CancellationToken cancellation)
    {
        if (method is not ("thread/read" or "thread/name/set")) throw new ArgumentException("Unsupported metadata operation.");
        await RequireFileAsync(cancellation).ConfigureAwait(false);
        return await _rpc.RequestAsync(method, parameters, cancellation).ConfigureAwait(false);
    }
    public async Task<CodexRpc> OpenIsolatedRpcAsync(string home, string? workspace, CancellationToken cancellation)
    {
        await RequireFileAsync(cancellation, workspace).ConfigureAwait(false);
        home = DirectoryPath(home); var rpc = CodexRpc.InspectLinuxConfiguration(_installation.Cli, home);
        try
        {
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false); ValidateInitialization(rpc, home);
            RequireFile(await ReadPolicyAsync(rpc, home, cancellation).ConfigureAwait(false), workspace);
            return rpc;
        }
        catch { await rpc.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async ValueTask DisposeAsync()
    { try { await _rpc.DisposeAsync().ConfigureAwait(false); } finally { _installation.Dispose(); } }
}
