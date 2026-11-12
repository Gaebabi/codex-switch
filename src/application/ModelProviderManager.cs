using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NodeValue = System.Text.Json.Nodes.JsonValue;

namespace CodexAccountDesk;

internal sealed partial class ModelProviderManager : IAsyncDisposable
{
    private readonly ModelProviderStore _store;
    private readonly HttpClient _http;
    private readonly Func<string?, string?, GatewayIdentity?> _authenticate;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly object _startGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;
    private ModelGateway? _gateway;
    private readonly string _catalogPath;
    private string? _home;
    private string? _catalogIdentity;
    private DateTimeOffset _catalogUpdated;
    public ModelProviderSettings Settings => _store.Read();
    public bool Running => _gateway?.Running == true;
    public string? LastError => _gateway?.LastError;
    public DateTimeOffset? LastRequestAt => _gateway?.LastRequestAt;
    public string? BaseUrl => _gateway?.BaseUrl;
    internal string GetCustomTerminalCommand(string executable, string home, bool windows)
    {
        var settings = Settings;
        var path = Path.GetFullPath(Path.Combine(home, "codex-switch.config.toml"));
        VerifyProfile(settings, path);
        if (!settings.IsolatedProfile || !settings.Connected || !settings.ConnectionWriteConfirmed || !Running || !File.Exists(path))
            throw new InvalidOperationException("Prepare the custom profile and keep Switch running before opening its terminal.");
        if (!ModelRouting.Registered(settings, settings.SelectedRoute)) throw new InvalidOperationException("Select an available custom model in Codex Switch before opening its terminal.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new InvalidOperationException("The reviewed Codex CLI is unavailable. Refresh the Codex installation first.");
        if (!string.Equals(Path.GetFullPath(executable), settings.CustomExecutablePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            ExecutableHash(executable) != settings.CustomExecutableSha256)
            throw new InvalidOperationException("The Codex CLI changed since profile preparation. Prepare the custom profile again before launching.");
        static string PowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";
        static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
        var credentialVariables = new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL", "OPENAI_IDENTITY_TOKEN_FILE", "OPENAI_IDENTITY_TOKEN_AUDIENCE" };
        return windows
            ? "& { $p = New-Object System.Diagnostics.ProcessStartInfo; $p.FileName = " + PowerShellQuote(executable) +
                "; $p.Arguments = '--no-daemon --profile codex-switch'; $p.UseShellExecute = $false; $p.EnvironmentVariables['CODEX_HOME'] = " + PowerShellQuote(Path.GetFullPath(home)) +
                "; foreach ($name in @(" + string.Join(",", credentialVariables.Select(PowerShellQuote)) + ")) { $p.EnvironmentVariables.Remove($name) }; [System.Diagnostics.Process]::Start($p).WaitForExit() }"
            : "env " + string.Join(" ", credentialVariables.Select(name => "-u " + name)) + " CODEX_HOME=" + ShellQuote(Path.GetFullPath(home)) + " " + ShellQuote(executable) + " --no-daemon --profile codex-switch";
    }
    private static string ExecutableHash(string executable)
    {
        using var file = File.OpenRead(executable);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    internal ModelProviderManager(string directory, Func<string?, string?, GatewayIdentity?> authenticate, HttpClient? http = null, string? codexHome = null)
    {
        _store = new ModelProviderStore(directory); _authenticate = authenticate; _http = http ?? ProviderCatalog.CreateHttpClient();
        _catalogPath = Path.GetFullPath(Path.Combine(directory, "model-providers-catalog.json")); _home = codexHome;
    }
    public async Task<ModelProvider> SaveAsync(string? id, string name, string url, string key, CancellationToken cancellation, IReadOnlyCollection<string>? includedIds = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var settings = _store.Read(); var previous = id == null ? null : settings.Providers.Single(p => p.Id == id);
            if (settings.Providers.Any(p => p.Id != id && p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Choose a distinct provider display name.");
            if (previous == null && settings.Providers.Count >= 32) throw new InvalidOperationException("At most 32 model providers are supported.");
            if (string.IsNullOrWhiteSpace(key))
            {
                if (previous == null) throw new ArgumentException("Enter an API key.");
                if (ProviderCatalog.Endpoint(previous.BaseUrl) != ProviderCatalog.Endpoint(url)) throw new ArgumentException("Enter a replacement API key when changing the destination URL.");
                key = previous.ApiKey;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var provider = await ProviderCatalog.DiscoverAsync(_http, id, name, url, key, deadline.Token, includedIds).ConfigureAwait(false);
            if (previous != null && ProviderCatalog.Endpoint(previous.BaseUrl) == ProviderCatalog.Endpoint(provider.BaseUrl))
                foreach (var model in provider.Models)
                    model.ReasoningOverride = previous.Models.FirstOrDefault(old => old.UpstreamId == model.UpstreamId)?.ReasoningOverride;
            cancellation.ThrowIfCancellationRequested();
            _store.Update(next => { var index = next.Providers.FindIndex(p => p.Id == provider.Id); if (index < 0) next.Providers.Add(provider); else next.Providers[index] = provider; RememberSavedEditor(next, provider, id); });
            if (!Settings.RouterEnabled) await RefreshCatalogCoreAsync(cancellation, true).ConfigureAwait(false);
            return provider;
        }
        finally { _changes.Release(); }
    }
    public async Task RemoveAsync(string id, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            _store.Update(next => { next.Providers.RemoveAll(p => p.Id == id); next.EditorDrafts.Remove(id); if (next.EditorSelection == id) next.EditorSelection = null; });
            if (!Settings.RouterEnabled) await RefreshCatalogCoreAsync(cancellation, true).ConfigureAwait(false);
        }
        finally { _changes.Release(); }
    }
    internal async Task SaveReasoningAsync(string providerId, string modelId, ProviderReasoningOptions? options, CancellationToken cancellation)
    {
        ProviderReasoning.Validate(options);
        // Snapshot caller-owned input before awaiting or persisting it.
        options = options == null ? null : new ProviderReasoningOptions { Levels = options.Levels.ToList(), Default = options.Default };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _changes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            _store.Update(next => next.Providers.Single(p => p.Id == providerId).Models.Single(m => m.UpstreamId == modelId).ReasoningOverride = options);
            if (!Settings.RouterEnabled) await RefreshCatalogCoreAsync(lifetime.Token, true).ConfigureAwait(false);
        }
        finally { _changes.Release(); }
    }
    internal void StartConfigured()
    {
        if (_store.Read().Connected) Start();
    }
    internal void Start()
    {
        lock (_startGate)
        {
        if (_disposal != null) throw new ObjectDisposedException(nameof(ModelProviderManager));
        if (_gateway != null) return;
        var gateway = new ModelGateway(_store, _authenticate, _http, Path.GetDirectoryName(_catalogPath)!, () => _home);
        try { gateway.Start(); _gateway = gateway; }
        catch { gateway.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        }
    }
    public async Task ConnectAsync(string executable, string home, CancellationToken cancellation, bool router = true)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var settings = _store.Read();
            if (!router) throw new InvalidOperationException("New connections require the isolated custom-model profile.");
            if (settings.DesktopProfile) throw new InvalidOperationException("Restore the Default Codex profile before preparing an isolated terminal profile.");
            if (!ModelRouting.Registered(settings, settings.SelectedRoute)) throw new InvalidOperationException("Select an available custom model in Codex Switch before connecting.");
            var profilePath = Path.GetFullPath(Path.Combine(home, "codex-switch.config.toml"));
            VerifyProfile(settings, profilePath);
            var executablePath = Path.GetFullPath(executable);
            var executableHash = ExecutableHash(executablePath);
            Start();
            await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
            var requirements = await rpc.RequestAsync("configRequirements/read", new { }, cancellation).ConfigureAwait(false);
            if (requirements.TryGetProperty("requirements", out var required) && required.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "modelProvider", "model_provider", "modelCatalogJson", "model_catalog_json" })
                    if (required.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null)
                        throw new InvalidOperationException("Managed policy controls the model connection. Ask its administrator to configure an approved gateway.");
            }
            var version = rpc.Initialization is JsonElement hello ? ModelGateway.ClientVersion(JsonValue.String(hello, "userAgent")) : null;
            if (version != null) _store.Update(next => next.LastClientVersion = version);
            var snapshot = await ReadUserConfigAsync(rpc, home, cancellation).ConfigureAwait(false);
            if (version == null || !Version.TryParse(version.Split('-')[0], out var parsedVersion) || parsedVersion < new Version(0, 167, 0))
                throw new InvalidOperationException("The isolated profile requires a reviewed Codex CLI version 0.167.0 or newer with V2 profile support.");
            _home = Path.GetFullPath(home);
            if (ExecutableHash(executablePath) != executableHash) throw new InvalidOperationException("The Codex CLI changed during profile preparation. No profile was written.");
            // Only migrate recorded app-owned root values. Never install a new root override.
            await RestoreRootAsync(rpc, home, snapshot, settings, cancellation).ConfigureAwait(false);
            VerifyProfile(settings, profilePath);
            var content = "# Codex Switch custom-only process. Native Codex uses config.toml unchanged.\n" +
                "model_provider = \"openai\"\nmodel = \"codex-switch\"\ncli_auth_credentials_store = \"file\"\nopenai_base_url = " + JsonSerializer.Serialize(_gateway!.BaseUrl) +
                "\nmodel_catalog_json = " + JsonSerializer.Serialize(_catalogPath) +
                "\n[features]\nresponses_websockets_v2 = false\nenable_request_compression = false\n";
            // Record ownership before writing, so uncertain completion remains recoverable.
            _store.Update(next => { next.IsolatedProfile = true; next.RootMigrationCompleted = true; next.CustomProfilePath = profilePath; next.CustomProfileContent = content; next.CustomExecutablePath = executablePath; next.CustomExecutableSha256 = executableHash; next.RouterEnabled = true; next.Connected = true; next.ConnectionWriteConfirmed = false; });
            await WriteCatalogAsync(version, cancellation).ConfigureAwait(false);
            SecureFiles.AtomicWrite(profilePath, Encoding.UTF8.GetBytes(content));
            _store.Update(next => next.ConnectionWriteConfirmed = true);
        }
        finally { _changes.Release(); }
    }
    private static void VerifyProfile(ModelProviderSettings settings, string path)
    {
        if (settings.CustomProfilePath != null && !string.Equals(settings.CustomProfilePath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The custom profile belongs to another Codex home.");
        if (File.Exists(path) && (settings.CustomProfileContent == null || File.ReadAllText(path) != settings.CustomProfileContent))
            throw new InvalidOperationException("The custom profile changed outside Switch. Review it before connecting or disconnecting.");
    }
    private async Task RestoreRootAsync(CodexRpc rpc, string home, (JsonObject Config, string Version) snapshot, ModelProviderSettings settings, CancellationToken cancellation)
    {
        if (settings.RootMigrationCompleted) return;
        if (settings.AppliedConfig == null)
        {
            if (settings.Connected && !settings.IsolatedProfile) throw new InvalidOperationException("The legacy connection recovery is missing. Restore the native configuration before preparing a custom profile.");
            return;
        }
        if (settings.OriginalConfig == null) throw new InvalidOperationException("The original connection recovery is incomplete.");
        var restore = new Dictionary<string, JsonNode?>();
        foreach (var (key, applied) in settings.AppliedConfig)
        {
            if (!settings.OriginalConfig.TryGetValue(key, out var original)) throw new InvalidOperationException("The original connection recovery is incomplete.");
            var current = ValueAt(snapshot.Config, key);
            if (JsonNode.DeepEquals(current, original)) continue;
            if (!JsonNode.DeepEquals(current, applied)) throw new InvalidOperationException("Codex connection settings changed outside Switch. Review them before migration; no root settings were overwritten.");
            restore[key] = original?.DeepClone();
        }
        if (restore.Count > 0) await WriteConfigAsync(rpc, home, snapshot.Version, restore, cancellation).ConfigureAwait(false);
        // Retain the original/applied recovery receipt for old provider-pinned histories.
    }
    internal async Task RefreshCatalogAsync(CancellationToken cancellation, bool force = false)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _changes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try { await RefreshCatalogCoreAsync(lifetime.Token, force).ConfigureAwait(false); }
        finally { _changes.Release(); }
    }
    private Task RefreshCatalogCoreAsync(CancellationToken cancellation, bool force)
    {
        var settings = _store.Read();
        if (settings.DesktopProfile && settings.Connected) return WriteCatalogAsync(settings.LastClientVersion ?? "unknown", cancellation, force, true, true);
        if (settings.IsolatedProfile && settings.Connected) return WriteCatalogAsync(settings.LastClientVersion ?? "unknown", cancellation, force);
        if (!settings.Connected || settings.AppliedConfig == null ||
            !settings.AppliedConfig.TryGetValue("model_catalog_json", out var path) || path?.GetValue<string>() != _catalogPath)
            return Task.CompletedTask;
        return WriteCatalogAsync(settings.LastClientVersion ?? throw new InvalidDataException("The connected Codex version is unavailable."), cancellation, force);
    }
    private GatewayIdentity ReadIdentity()
    {
        if (_home == null) throw new InvalidOperationException("The connected Codex home is unavailable.");
        var bytes = SecureFiles.ReadPrivateBytes(Path.Combine(_home, "auth.json"));
        try
        {
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            var token = JsonValue.String(root, "OPENAI_API_KEY"); string? account = null;
            if (string.IsNullOrWhiteSpace(token) && root.TryGetProperty("tokens", out var tokens))
            { token = JsonValue.String(tokens, "access_token"); account = JsonValue.String(tokens, "account_id"); }
            return _authenticate("Bearer " + token, account) ?? throw new InvalidOperationException("The current OpenAI sign-in is required to refresh the model catalog.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private async Task WriteCatalogAsync(string version, CancellationToken cancellation, bool force = true, bool? router = null, bool desktop = false)
    {
        if (Settings.IsolatedProfile && !Settings.DesktopProfile && !desktop)
        {
            cancellation.ThrowIfCancellationRequested();
            SecureFiles.AtomicWrite(_catalogPath, JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["models"] = new JsonArray(ModelRouting.Metadata()) }));
            return;
        }
        var identity = ReadIdentity();
        var identityKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Token + "\n" + identity.AccountId)));
        var catalogPath = desktop || Settings.DesktopProfile ? DesktopCatalogPath : _catalogPath;
        if (!force && _catalogIdentity == identityKey && DateTimeOffset.UtcNow - _catalogUpdated < TimeSpan.FromMinutes(5) && File.Exists(catalogPath)) return;
        Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var catalog = await _gateway!.ReadCatalogAsync(identity, version, deadline.Token, router, desktop).ConfigureAwait(false);
        var current = ReadIdentity();
        if (current.Token != identity.Token || current.AccountId != identity.AccountId || current.ApiKey != identity.ApiKey)
            throw new InvalidOperationException("The OpenAI sign-in changed while refreshing the model catalog.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog);
        if (bytes.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The combined model catalog exceeds the client limit.");
        SecureFiles.AtomicWrite(catalogPath, bytes);
        _catalogIdentity = identityKey; _catalogUpdated = DateTimeOffset.UtcNow;
    }
    public async Task DisconnectAsync(string executable, string home, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var settings = _store.Read();
            if (settings.DesktopProfile) throw new InvalidOperationException("Use Codex profile > Default — restore and restart before disconnecting the desktop gateway.");
            if (settings.IsolatedProfile)
            {
                var path = Path.GetFullPath(Path.Combine(home, "codex-switch.config.toml"));
                VerifyProfile(settings, path);
                if (File.Exists(path)) File.Delete(path);
                _store.Update(next => { next.Connected = false; next.ConnectionWriteConfirmed = false; });
                if (_gateway != null) { await _gateway.DisposeAsync().ConfigureAwait(false); _gateway = null; }
                return;
            }
            if (settings.OriginalConfig == null || settings.AppliedConfig == null) return;
            await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
            var snapshot = await ReadUserConfigAsync(rpc, home, cancellation).ConfigureAwait(false);
            await RestoreRootAsync(rpc, home, snapshot, settings, cancellation).ConfigureAwait(false);
            _store.Update(next => { next.Connected = false; next.ConnectionWriteConfirmed = false; next.RootMigrationCompleted = true; });
            if (_gateway != null) { await _gateway.DisposeAsync().ConfigureAwait(false); _gateway = null; }
        }
        finally { _changes.Release(); }
    }
    private static async Task<(JsonObject Config, string Version)> ReadUserConfigAsync(CodexRpc rpc, string home, CancellationToken cancellation)
    {
        JsonElement read;
        try { read = await rpc.RequestAsync("config/read", new { includeLayers = true, cwd = (string?)null }, cancellation).ConfigureAwait(false); }
        catch (CodexRequestException) { throw new InvalidOperationException("Codex configuration could not be read. Review the local configuration and managed policy."); }
        var expected = Path.GetFullPath(Path.Combine(home, "config.toml"));
        foreach (var layer in read.GetProperty("layers").EnumerateArray())
        {
            var name = layer.GetProperty("name");
            if (JsonValue.String(name, "type") != "user" || JsonValue.String(name, "profile") != null || JsonValue.String(name, "file") is not string file ||
                !string.Equals(Path.GetFullPath(file), expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            return (JsonNode.Parse(layer.GetProperty("config").GetRawText())!.AsObject(), layer.GetProperty("version").GetString()!);
        }
        throw new InvalidOperationException("The writable user configuration is unavailable. Create config.toml before connecting.");
    }
    private static async Task WriteConfigAsync(CodexRpc rpc, string home, string version, Dictionary<string, JsonNode?> changes, CancellationToken cancellation)
    {
        JsonElement result;
        try { result = await rpc.RequestAsync("config/batchWrite", new
        {
            filePath = Path.GetFullPath(Path.Combine(home, "config.toml")), expectedVersion = version, reloadUserConfig = false,
            edits = changes.Select(pair => new { keyPath = pair.Key, value = pair.Value, mergeStrategy = "replace" }).ToArray()
        }, cancellation).ConfigureAwait(false); }
        catch (CodexRequestException) { throw new InvalidOperationException("Codex configuration could not be updated. Review concurrent edits, file access and managed policy; no automatic retry was attempted."); }
        if (JsonValue.String(result, "status") != "ok") throw new InvalidOperationException("A higher-priority Codex policy overrides the connection settings. Review the configuration before using the gateway.");
    }
    private static JsonNode? ValueAt(JsonObject config, string path)
    {
        JsonNode? node = config;
        foreach (var part in path.Split('.')) { if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node)) return null; }
        return node;
    }
    public ValueTask DisposeAsync()
    {
        lock (_startGate) return new ValueTask(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        _lifetime.Cancel();
        await _changes.WaitAsync().ConfigureAwait(false);
        try { if (_gateway != null) await _gateway.DisposeAsync().ConfigureAwait(false); _http.Dispose(); }
        finally { _changes.Release(); _changes.Dispose(); _lifetime.Dispose(); }
    }
}
