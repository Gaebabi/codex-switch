using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NodeValue = System.Text.Json.Nodes.JsonValue;

namespace CodexAccountDesk;

internal sealed class ModelProviderManager : IAsyncDisposable
{
    private readonly ModelProviderStore _store;
    private readonly HttpClient _http;
    private readonly Func<string?, string?, GatewayIdentity?> _authenticate;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly object _startGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;
    private ModelGateway? _gateway;
    public ModelProviderSettings Settings => _store.Read();
    public bool Running => _gateway?.Running == true;
    public string? LastError => _gateway?.LastError;
    public DateTimeOffset? LastRequestAt => _gateway?.LastRequestAt;
    public string? BaseUrl => _gateway?.BaseUrl;
    internal ModelProviderManager(string directory, Func<string?, string?, GatewayIdentity?> authenticate, HttpClient? http = null)
    { _store = new ModelProviderStore(directory); _authenticate = authenticate; _http = http ?? ProviderCatalog.CreateHttpClient(); }
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
            _store.Update(next => { var index = next.Providers.FindIndex(p => p.Id == provider.Id); if (index < 0) next.Providers.Add(provider); else next.Providers[index] = provider; });
            return provider;
        }
        finally { _changes.Release(); }
    }
    public async Task RemoveAsync(string id, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try { _store.Update(next => next.Providers.RemoveAll(p => p.Id == id)); }
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
        var gateway = new ModelGateway(_store, _authenticate, _http);
        try { gateway.Start(); _gateway = gateway; }
        catch { gateway.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        }
    }
    public async Task ConnectAsync(string executable, string home, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_store.Read().Connected) throw new InvalidOperationException("Codex connection settings are already prepared. Restart Codex once when ready.");
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
            if (ValueAt(snapshot.Config, "model_providers.codex_switch_router") != null)
                throw new InvalidOperationException("The provider ID codex_switch_router is already configured. Review it before connecting.");
            var edits = new Dictionary<string, JsonNode?>
            {
                ["openai_base_url"] = NodeValue.Create(_gateway!.BaseUrl), ["model_provider"] = NodeValue.Create("codex_switch_router"),
                ["model_catalog_json"] = null, ["features.responses_websockets_v2"] = NodeValue.Create(false),
                ["features.enable_request_compression"] = NodeValue.Create(false),
                ["model_providers.codex_switch_router"] = new JsonObject
                {
                    ["name"] = "Codex Switch", ["base_url"] = _gateway.BaseUrl, ["wire_api"] = "responses",
                    ["requires_openai_auth"] = true, ["supports_websockets"] = false,
                    ["model_catalog_url"] = _gateway.BaseUrl + "/models",
                    ["request_max_retries"] = 0, ["stream_max_retries"] = 0,
                    ["capabilities"] = new JsonObject { ["remote_compaction"] = "unsupported" }
                }
            };
            var original = edits.Keys.Append("model").ToDictionary(key => key, key => ValueAt(snapshot.Config, key)?.DeepClone());
            // Persist recovery before the atomic config write. A crash can only leave an extra listener,
            // never a client configured for a listener that will not be started on the next Switch launch.
            _store.Update(next => { next.OriginalConfig = original; next.AppliedConfig = edits; next.Connected = true; next.ConnectionWriteConfirmed = false; });
            // Keep recovery information if the RPC commits before its reply is lost.
            // Disconnect re-reads the file and refuses to overwrite divergent values.
            await WriteConfigAsync(rpc, home, snapshot.Version, edits, cancellation).ConfigureAwait(false);
            _store.Update(next => next.ConnectionWriteConfirmed = true);
        }
        finally { _changes.Release(); }
    }
    public async Task DisconnectAsync(string executable, string home, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); cancellation = lifetime.Token;
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var settings = _store.Read();
            if (settings.OriginalConfig == null || settings.AppliedConfig == null) return;
            await using var rpc = new CodexRpc(executable, home, preserveProvider: true);
            await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
            var snapshot = await ReadUserConfigAsync(rpc, home, cancellation).ConfigureAwait(false);
            var restore = new Dictionary<string, JsonNode?>();
            foreach (var (key, applied) in settings.AppliedConfig)
            {
                var current = ValueAt(snapshot.Config, key);
                if (!JsonNode.DeepEquals(current, applied) && !JsonNode.DeepEquals(current, settings.OriginalConfig[key]))
                    throw new InvalidOperationException("Codex connection settings changed outside Switch. Review them before disconnecting.");
                restore[key] = settings.OriginalConfig[key]?.DeepClone();
            }
            if (ValueAt(snapshot.Config, "model") is NodeValue model && model.TryGetValue<string>(out var selected) && selected.StartsWith("cs_", StringComparison.Ordinal))
                restore["model"] = settings.OriginalConfig["model"]?.DeepClone();
            await WriteConfigAsync(rpc, home, snapshot.Version, restore, cancellation).ConfigureAwait(false);
            _store.Update(next => { next.Connected = false; next.ConnectionWriteConfirmed = false; next.OriginalConfig = null; next.AppliedConfig = null; });
            // Keep the current listener alive until Switch exits: already-open Codex sessions may
            // still use it until the owner's one-time restart after disconnecting.
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
