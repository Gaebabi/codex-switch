using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class ModelProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<ProviderModel> Models { get; set; } = new();
    public List<string>? IncludedModelIds { get; set; }
    public override string ToString() => Name;
}

internal sealed class ProviderModel
{
    public string UpstreamId { get; set; } = "";
    public bool GenericMetadata { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProviderReasoningOptions? ReasoningOverride { get; set; }
    public JsonObject Metadata { get; set; } = new();
    public string Alias(string providerId) => "cs_" + providerId + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UpstreamId)))[..16].ToLowerInvariant();
}

internal sealed class ModelProviderSettings
{
    public int Version { get; set; } = 1;
    public int Port { get; set; }
    public string? LastClientVersion { get; set; }
    public bool Connected { get; set; }
    public bool ConnectionWriteConfirmed { get; set; }
    public List<ModelProvider> Providers { get; set; } = new();
    public Dictionary<string, JsonNode?>? OriginalConfig { get; set; }
    public Dictionary<string, JsonNode?>? AppliedConfig { get; set; }
    public Dictionary<string, GatewayConversationRoute> ConversationRoutes { get; set; } = new();
}

internal sealed class GatewayConversationRoute
{
    public string Route { get; set; } = "";
    public bool Mixed { get; set; }
}

internal sealed class ModelProviderStore
{
    private readonly string _directory;
    private readonly string _path;
    private ModelProviderSettings _settings;
    private readonly object _gate = new();
    public ModelProviderStore(string directory)
    {
        _directory = Path.GetFullPath(directory); _path = Path.Combine(_directory, "model-providers.vault");
        _settings = new();
        if (!File.Exists(_path)) return;
        var plaintext = VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(_path), _directory);
        try { _settings = JsonSerializer.Deserialize<ModelProviderSettings>(plaintext) ?? throw new InvalidDataException("The model provider store is empty."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (_settings.Version != 1 || _settings.Port is < 0 or > 65535 || _settings.Providers.Count > 32 ||
            _settings.Providers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != _settings.Providers.Count)
            throw new InvalidDataException("The model provider store is invalid.");
        foreach (var provider in _settings.Providers) ProviderCatalog.Validate(provider);
    }
    public ModelProviderSettings Read() { lock (_gate) return Clone(_settings); }
    internal bool RequiresPortableHistory(string? session, GatewayIdentity identity, string route)
    {
        // Refused requests must not alter the source route: the owner still needs it
        // to produce a readable handoff using the original provider and account.
        var key = ConversationKey(session, identity);
        if (key == null) return true;
        lock (_gate)
        {
            _settings.ConversationRoutes.TryGetValue(key, out var previous);
            if (previous == null && _settings.ConversationRoutes.Count >= 10000) return true;
            return IsMixed(previous, route);
        }
    }
    internal void RecordSuccessfulRoute(string? session, GatewayIdentity identity, string route)
    {
        var key = ConversationKey(session, identity);
        if (key == null) return;
        lock (_gate)
        {
            _settings.ConversationRoutes.TryGetValue(key, out var previous);
            if (previous == null && _settings.ConversationRoutes.Count >= 10000) return;
            var mixed = IsMixed(previous, route);
            if (previous?.Route == route && previous.Mixed == mixed) return;
            Update(next => next.ConversationRoutes[key] = new GatewayConversationRoute { Route = route, Mixed = mixed });
        }
    }
    private static bool IsMixed(GatewayConversationRoute? previous, string route) =>
        previous?.Mixed == true || (previous != null ? previous.Route != route : route != "openai");
    private static string? ConversationKey(string? session, GatewayIdentity identity) =>
        string.IsNullOrEmpty(session) || session.Length > 256 ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((identity.AccountId ?? identity.Token) + "\n" + session)));
    public void Update(Action<ModelProviderSettings> update)
    {
        lock (_gate)
        {
            var next = Clone(_settings); update(next);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(next);
            try { SecureFiles.CreatePrivateDirectory(_directory); SecureFiles.AtomicWrite(_path, VaultProtection.Protect(plaintext, _directory)); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            _settings = next;
        }
    }
    private static ModelProviderSettings Clone(ModelProviderSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings);
        try { return JsonSerializer.Deserialize<ModelProviderSettings>(bytes)!; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

internal static class ProviderCatalog
{
    internal const int MaximumCatalogBytes = 1024 * 1024;
    private static readonly Lazy<string> FallbackInstructions = new(() =>
    {
        using var source = typeof(ProviderCatalog).Assembly.GetManifestResourceStream("CodexAccountDesk.CodexFallbackInstructions.md")!;
        using var reader = new StreamReader(source, Encoding.UTF8); return reader.ReadToEnd();
    });
    public static Uri Endpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTPS API base URL, or HTTP on loopback, without credentials, a query or a fragment.");
        var builder = new UriBuilder(uri); var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/responses/compact", "/responses", "/models" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path[..^suffix.Length]; break; }
        builder.Path = path.TrimEnd('/') + "/"; return builder.Uri;
    }
    public static void Validate(ModelProvider provider, bool validateMetadata = true)
    {
        if (!Guid.TryParseExact(provider.Id, "N", out _) || string.IsNullOrWhiteSpace(provider.Name) || provider.Name.Length > 80 || provider.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(provider.ApiKey) || provider.ApiKey.Length > 16384 || provider.ApiKey.Any(char.IsControl))
            throw new ArgumentException("A provider needs a valid name and API key.");
        Endpoint(provider.BaseUrl);
        if (provider.Name.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a distinct provider name; OpenAI identifies the existing OpenAI route.");
        if (provider.Models.Count is < 1 or > 128 || provider.Models.Any(m => string.IsNullOrWhiteSpace(m.UpstreamId) || m.UpstreamId.Length > 256 || m.UpstreamId.Any(char.IsControl)) ||
            provider.Models.Select(m => m.UpstreamId).Distinct(StringComparer.Ordinal).Count() != provider.Models.Count)
            throw new InvalidDataException("The provider must return between 1 and 128 distinct model IDs.");
        foreach (var model in provider.Models)
        {
            ProviderReasoning.Validate(model.ReasoningOverride);
            var metadata = model.Metadata;
            if (!validateMetadata) continue;
            foreach (var key in new[] { "slug", "display_name", "shell_type", "visibility" })
                if (metadata[key] is not System.Text.Json.Nodes.JsonValue value || !value.TryGetValue<string>(out _)) throw new InvalidDataException("The provider's Codex catalog has an invalid required field.");
            if (metadata["supported_reasoning_levels"] is not JsonArray || metadata["experimental_supported_tools"] is not JsonArray || metadata["truncation_policy"] is not JsonObject ||
                (metadata["model_messages"]?["instructions_template"] == null && metadata["base_instructions"] == null))
                throw new InvalidDataException("The provider's Codex catalog is missing required metadata or model instructions.");
        }
    }
    public static async Task<ModelProvider> DiscoverAsync(HttpClient http, string? id, string name, string baseUrl, string key, CancellationToken cancellation, IReadOnlyCollection<string>? includedIds = null)
    {
        var provider = new ModelProvider { Id = id ?? Guid.NewGuid().ToString("N"), Name = name.Trim(), BaseUrl = Endpoint(baseUrl).AbsoluteUri.TrimEnd('/'), ApiKey = key.Trim() };
        var filter = includedIds?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        if (filter?.Count == 0) filter = null;
        if (filter?.Count > 128) throw new ArgumentException("Select at most 128 model IDs.");
        provider.IncludedModelIds = filter?.ToList();
        // Validate all user-controlled header and destination values before making a request.
        provider.Models.Add(new ProviderModel { UpstreamId = "validation" }); Validate(provider, validateMetadata: false); provider.Models.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint(baseUrl), "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("Model discovery failed (HTTP " + (int)response.StatusCode + "). Check the base URL and API key.");
        var body = await ReadLimitedAsync(response.Content, 4 * MaximumCatalogBytes, cancellation).ConfigureAwait(false);
        JsonObject catalog;
        try { catalog = JsonNode.Parse(body) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidDataException("The provider returned an invalid model list."); }
        if (catalog["models"] is JsonArray models)
        {
            foreach (var model in models)
            {
                if (model is not JsonObject metadata || metadata["slug"]?.GetValue<string>() is not string slug) throw new InvalidDataException("A Codex model catalog entry has no slug.");
                if (filter != null && !filter.Contains(slug)) continue;
                if (provider.Models.Count >= 128) throw new InvalidDataException("This provider has many models. Enter the model IDs to include.");
                provider.Models.Add(new ProviderModel { UpstreamId = slug, Metadata = (JsonObject)metadata.DeepClone() });
            }
        }
        else if (catalog["data"] is JsonArray data)
        {
            foreach (var model in data)
            {
                if (model?["id"]?.GetValue<string>() is not string slug) throw new InvalidDataException("A provider model entry has no ID.");
                if (filter != null && !filter.Contains(slug)) continue;
                if (provider.Models.Count >= 128) throw new InvalidDataException("This provider has many models. Enter the model IDs to include.");
                provider.Models.Add(new ProviderModel { UpstreamId = slug, GenericMetadata = true, Metadata = Generic(slug) });
            }
        }
        else throw new InvalidDataException("The endpoint returned neither a Codex catalog nor an OpenAI-compatible model list.");
        if (filter != null && filter.Any(id => !provider.Models.Any(model => model.UpstreamId == id)))
            throw new InvalidDataException("Some selected model IDs were not returned by this provider.");
        Validate(provider); return provider;
    }
    // Generic client settings do not assert a context limit, image, reasoning or hosted-tool capability.
    // Providers may supply a native Codex catalog instead to retain their reviewed model settings.
    internal static JsonObject Generic(string slug) => new()
    {
        ["slug"] = slug, ["display_name"] = slug, ["description"] = "Responses model with generic client settings; provider-specific capabilities are not supplied.",
        ["default_reasoning_level"] = "none", ["supported_reasoning_levels"] = new JsonArray(new JsonObject { ["effort"] = "none", ["description"] = "Provider default" }),
        ["shell_type"] = "shell_command", ["visibility"] = "list", ["supported_in_api"] = true, ["priority"] = 1000,
        ["availability_nux"] = null, ["upgrade"] = null, ["support_verbosity"] = false, ["default_verbosity"] = null,
        ["supports_reasoning_summary_parameter"] = false, ["apply_patch_tool_type"] = null,
        ["truncation_policy"] = new JsonObject { ["mode"] = "tokens", ["limit"] = 10000 },
        ["context_window"] = null, ["experimental_supported_tools"] = new JsonArray(), ["input_modalities"] = new JsonArray("text"),
        ["model_messages"] = new JsonObject { ["instructions_template"] = FallbackInstructions.Value }, ["service_tiers"] = new JsonArray(), ["additional_speed_tiers"] = new JsonArray()
    };
    internal static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximum, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("The provider response is too large.");
        await using var source = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var target = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (target.Length + count > maximum) throw new InvalidDataException("The provider response is too large.");
            target.Write(buffer, 0, count);
        }
        return target.ToArray();
    }
    internal static HttpClient CreateHttpClient() => new(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
}

internal sealed class GatewayIdentity
{
    public string Token { get; }
    public string? AccountId { get; }
    public bool ApiKey { get; }
    internal GatewayIdentity(string token, string? accountId, bool apiKey) { Token = token; AccountId = accountId; ApiKey = apiKey; }
    public override string ToString() => "Authenticated OpenAI request";
    internal static GatewayIdentity? Authenticate(string? authorization, string? account, Func<string?> readAuth)
    {
        if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 32768) return null;
        var auth = readAuth(); if (auth == null) return null;
        using var document = JsonDocument.Parse(auth); var root = document.RootElement;
        var apiKey = root.TryGetProperty("OPENAI_API_KEY", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = null;
        string? token = apiKey, id = null;
        if (token == null && root.TryGetProperty("tokens", out var tokens))
        {
            token = tokens.TryGetProperty("access_token", out var access) && access.ValueKind == JsonValueKind.String ? access.GetString() : null;
            id = tokens.TryGetProperty("account_id", out var accountId) && accountId.ValueKind == JsonValueKind.String ? accountId.GetString() : null;
        }
        if (string.IsNullOrWhiteSpace(id)) id = null;
        if (string.IsNullOrWhiteSpace(token) || (id != null && account != null && id != account)) return null;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])); var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actual, expected) ? new GatewayIdentity(token, id, apiKey != null) : null;
    }
}
