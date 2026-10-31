using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class ModelGateway : IAsyncDisposable
{
    private readonly ModelProviderStore _store;
    private readonly Func<string?, string?, GatewayIdentity?> _authenticate;
    private readonly HttpClient _http;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Task> _requests = new();
    private readonly SemaphoreSlim _slots = new(16, 16);
    private Task? _accept;
    private int _nextRequest;
    private sealed record NativeCatalog(Dictionary<string, JsonObject> Models, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, NativeCatalog> _nativeCatalogs = new();
    public int Port { get; private set; }
    public bool Running => _listener.IsListening;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastRequestAt { get; private set; }
    public string BaseUrl => "http://127.0.0.1:" + Port + "/backend-api/codex";
    internal ModelGateway(ModelProviderStore store, Func<string?, string?, GatewayIdentity?> authenticate, HttpClient http)
    { _store = store; _authenticate = authenticate; _http = http; }
    public void Start()
    {
        if (_accept != null) return;
        Port = _store.Read().Port;
        if (Port == 0)
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            try { reservation.Start(); Port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
            finally { reservation.Stop(); }
        }
        _listener.Prefixes.Add(BaseUrl + "/");
        // A failed bind stays visible; never request URL ACL/elevation changes or bind a wildcard.
        _listener.Start();
        _store.Update(settings => settings.Port = Port);
        _accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_stop.Token).ConfigureAwait(false);
                if (!_slots.Wait(0)) { await ErrorAsync(context.Response, 503, "The local model gateway is busy.", _stop.Token).ConfigureAwait(false); continue; }
                var id = Interlocked.Increment(ref _nextRequest);
                var task = ServeAsync(context); _requests[id] = task;
                _ = task.ContinueWith(_ => { _requests.TryRemove(id, out var removed); _slots.Release(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync(HttpListenerContext context)
    {
        var started = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(TimeSpan.FromMinutes(30));
        var cancellation = deadline.Token;
        try
        {
            var request = context.Request; var response = context.Response;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) ||
                request.UserHostName != "127.0.0.1:" + Port || request.Headers["Origin"] != null || request.Headers["Sec-Fetch-Site"] != null)
            { await ErrorAsync(response, 403, "Only authenticated local Codex requests are accepted.", cancellation).ConfigureAwait(false); return; }
            var identity = _authenticate(request.Headers["Authorization"], request.Headers["ChatGPT-Account-ID"]);
            if (identity == null) { await ErrorAsync(response, 401, "The current OpenAI sign-in is required.", cancellation).ConfigureAwait(false); return; }
            LastRequestAt = DateTimeOffset.UtcNow;
            var path = request.Url?.AbsolutePath;
            if (path == "/backend-api/codex/models" && request.HttpMethod == "GET")
            {
                var catalog = await CatalogAsync(identity, request, cancellation).ConfigureAwait(false);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog);
                if (bytes.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The combined model catalog exceeds the client limit.");
                response.ContentType = "application/json"; response.Headers["Cache-Control"] = "no-store"; response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); return;
            }
            if (request.HttpMethod != "POST" || path is not ("/backend-api/codex/responses" or "/backend-api/codex/responses/compact"))
            { await ErrorAsync(response, 404, "This model gateway endpoint is not supported.", cancellation).ConfigureAwait(false); return; }
            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true || request.ContentLength64 > 16 * 1024 * 1024)
            { await ErrorAsync(response, 400, "A bounded JSON Responses request is required.", cancellation).ConfigureAwait(false); return; }
            if (!string.IsNullOrEmpty(request.Headers["Content-Encoding"]) && request.Headers["Content-Encoding"] != "identity")
            { await ErrorAsync(response, 415, "Disable Codex request compression in the connection settings before using the local gateway.", cancellation).ConfigureAwait(false); return; }
            using var input = new StreamContent(request.InputStream);
            var bytesIn = await ProviderCatalog.ReadLimitedAsync(input, 16 * 1024 * 1024, cancellation).ConfigureAwait(false);
            var body = JsonNode.Parse(bytesIn) as JsonObject ?? throw new InvalidDataException("The Responses request is invalid.");
            var model = body["model"]?.GetValue<string>();
            if (string.IsNullOrEmpty(model)) throw new InvalidDataException("A model must be selected.");
            // Snapshot a route and its key before the first upstream byte. Settings edits never retarget an in-flight request.
            var providers = _store.Read().Providers;
            var route = providers.SelectMany(p => p.Models.Select(m => (Provider: p, Model: m))).FirstOrDefault(pair => pair.Model.Alias(pair.Provider.Id) == model);
            if (model.StartsWith("cs_", StringComparison.Ordinal) && route.Provider == null)
            { await ErrorAsync(response, 404, "The selected custom model is no longer registered. Select another model.", cancellation).ConfigureAwait(false); return; }
            if (route.Provider == null && !await KnownOpenAiModelAsync(model, identity, request, cancellation).ConfigureAwait(false))
            { await ErrorAsync(response, 404, "The selected model is not in this account's OpenAI catalog or registered custom routes. Select a model from the combined list.", cancellation).ConfigureAwait(false); return; }
            // Incremental provider state and opaque reasoning cannot be transferred between upstreams.
            // HTTP full-input replay is required for every routed request, including after gateway restart.
            if (body["previous_response_id"] != null || body["conversation"] != null)
            { await ErrorAsync(response, 400, "Use full-input HTTP Responses replay; provider-specific response references cannot be routed safely.", cancellation).ConfigureAwait(false); return; }
            var changedBody = false;
            var routeIdentity = route.Provider == null ? "openai" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(route.Provider.Id + "\n" + route.Provider.BaseUrl + "\n" + route.Provider.ApiKey)));
            var portableHistory = _store.RequiresPortableHistory(request.Headers["session_id"] ?? request.Headers["conversation_id"], identity, routeIdentity);
            if (portableHistory && body["input"] is JsonArray items)
            {
                if (items.Any(item => item is JsonObject obj && obj["type"]?.GetValue<string>() == "compaction" && obj["encrypted_content"] != null))
                { await ErrorAsync(response, 400, ModelHistoryHandoff.Refusal, cancellation).ConfigureAwait(false); return; }
                for (var i = items.Count - 1; i >= 0; i--)
                    if (items[i] is JsonObject obj && obj["type"]?.GetValue<string>() == "reasoning" && obj["encrypted_content"] != null) { items.RemoveAt(i); changedBody = true; }
            }
            Uri destination;
            if (route.Provider != null)
            {
                changedBody = true;
                body["model"] = route.Model.UpstreamId;
                body.Remove("prompt_cache_key");
                JsonObject? native = null;
                if (route.Model.GenericMetadata && route.Model.ReasoningOverride == null && body["reasoning"] != null)
                    native = await NativeModelAsync(route.Model.UpstreamId, identity, request, cancellation).ConfigureAwait(false);
                var reasoningError = ProviderReasoning.PrepareRequest(route.Model, ProviderReasoning.Metadata(route.Model, native), body, native != null);
                if (reasoningError != null) { await ErrorAsync(response, 400, reasoningError, cancellation).ConfigureAwait(false); return; }
                if (route.Model.GenericMetadata)
                {
                    body.Remove("service_tier");
                    if (body["text"] is JsonObject text) text.Remove("verbosity");
                }
                destination = new Uri(ProviderCatalog.Endpoint(route.Provider.BaseUrl), path.EndsWith("/compact", StringComparison.Ordinal) ? "responses/compact" : "responses");
            }
            else destination = new Uri((identity.ApiKey ? "https://api.openai.com/v1/" : "https://chatgpt.com/backend-api/codex/") + (path.EndsWith("/compact", StringComparison.Ordinal) ? "responses/compact" : "responses"));
            using var upstream = new HttpRequestMessage(HttpMethod.Post, destination);
            if (route.Provider == null) CopyOpenAiHeaders(request, upstream, identity);
            else upstream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.Provider.ApiKey);
            upstream.Content = new ByteArrayContent(changedBody ? JsonSerializer.SerializeToUtf8Bytes(body) : bytesIn); upstream.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var result = await _http.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            { await ErrorAsync(response, (int)result.StatusCode is >= 300 and < 400 ? 502 : (int)result.StatusCode, "The selected upstream returned HTTP " + (int)result.StatusCode + ". No request was retried.", cancellation).ConfigureAwait(false); return; }
            _store.RecordSuccessfulRoute(request.Headers["session_id"] ?? request.Headers["conversation_id"], identity, routeIdentity);
            response.StatusCode = (int)result.StatusCode; response.ContentType = result.Content.Headers.ContentType?.ToString() ?? "application/json";
            response.Headers["Cache-Control"] = "no-store"; response.SendChunked = true; started = true;
            await using var stream = await result.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            var buffer = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            { await response.OutputStream.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false); await response.OutputStream.FlushAsync(cancellation).ConfigureAwait(false); }
            LastError = null;
        }
        catch (OperationCanceledException) { LastError = "The model request was cancelled or timed out."; context.Response.Abort(); }
        catch (Exception error)
        {
            LastError = "The model request or upstream connection failed (" + error.GetType().Name + ").";
            if (started) context.Response.Abort();
            else
            {
                try { await ErrorAsync(context.Response, error is InvalidDataException or JsonException or ArgumentException ? 400 : 502, LastError, cancellation).ConfigureAwait(false); }
                catch (Exception transport) when (transport is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { context.Response.Abort(); }
            }
        }
        finally { context.Response.Close(); }
    }
    private async Task<JsonObject> CatalogAsync(GatewayIdentity identity, HttpListenerRequest incoming, CancellationToken cancellation, string? clientVersion = null)
    {
        var version = clientVersion ?? incoming.QueryString["client_version"] ?? "";
        if (version.Length is < 1 or > 80 || version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')))
            throw new InvalidDataException("The model catalog request has no valid client version.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/codex/models?client_version=" + Uri.EscapeDataString(version));
        CopyOpenAiHeaders(incoming, request, identity);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("The current account's OpenAI model catalog is unavailable.");
        var result = JsonNode.Parse(await ProviderCatalog.ReadLimitedAsync(response.Content, ProviderCatalog.MaximumCatalogBytes, cancellation).ConfigureAwait(false)) as JsonObject;
        if (result?["models"] is not JsonArray models || models.Count == 0) throw new InvalidDataException("OpenAI did not return a usable Codex model catalog.");
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (model is not JsonObject metadata || metadata["slug"]?.GetValue<string>() is not string slug || !slugs.Add(slug)) throw new InvalidDataException("The OpenAI catalog has invalid model IDs.");
        }
        if (_nativeCatalogs.Count >= 64) _nativeCatalogs.Clear();
        var nativeModels = models.Cast<JsonObject>().ToDictionary(m => m["slug"]!.GetValue<string>(), m => (JsonObject)m.DeepClone(), StringComparer.Ordinal);
        _nativeCatalogs[IdentityKey(identity)] = new NativeCatalog(nativeModels, DateTimeOffset.UtcNow.AddMinutes(5));
        if (_store.Read().LastClientVersion != version) _store.Update(settings => settings.LastClientVersion = version);
        foreach (var provider in _store.Read().Providers)
        foreach (var model in provider.Models)
        {
            var metadata = ProviderReasoning.Metadata(model, nativeModels.GetValueOrDefault(model.UpstreamId)); var alias = model.Alias(provider.Id);
            if (!slugs.Add(alias)) throw new InvalidDataException("Model route names collide.");
            metadata["slug"] = alias; metadata["display_name"] = (metadata["display_name"]?.GetValue<string>() ?? model.UpstreamId) + " (" + provider.Name + ")";
            metadata["upgrade"] = null; metadata["auto_review_model_override"] = null;
            models.Add(metadata);
        }
        return result!;
    }
    private async Task<bool> KnownOpenAiModelAsync(string model, GatewayIdentity identity, HttpListenerRequest incoming, CancellationToken cancellation)
        => await NativeModelAsync(model, identity, incoming, cancellation).ConfigureAwait(false) != null;
    private async Task<JsonObject?> NativeModelAsync(string model, GatewayIdentity identity, HttpListenerRequest incoming, CancellationToken cancellation)
    {
        var key = IdentityKey(identity);
        if (_nativeCatalogs.TryGetValue(key, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Models.GetValueOrDefault(model);
        var version = ClientVersion(incoming.Headers["version"]) ?? ClientVersion(incoming.UserAgent) ?? _store.Read().LastClientVersion;
        if (version == null) throw new InvalidDataException("Refresh the model list before using this connection.");
        await CatalogAsync(identity, incoming, cancellation, version).ConfigureAwait(false);
        return _nativeCatalogs.TryGetValue(key, out cached) ? cached.Models.GetValueOrDefault(model) : null;
    }
    private static string IdentityKey(GatewayIdentity identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Token + "\n" + identity.AccountId)));
    internal static string? ClientVersion(string? value)
    {
        if (value == null) return null;
        var direct = Regex.Match(value, @"^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$");
        if (direct.Success) return direct.Value.Split(new[] { '-', '+' })[0];
        var agent = Regex.Match(value, @"(?:Codex Desktop|codex_cli_rs|Codex CLI|codex)/(\d+\.\d+\.\d+)");
        return agent.Success ? agent.Groups[1].Value : null;
    }
    private static void CopyOpenAiHeaders(HttpListenerRequest incoming, HttpRequestMessage outgoing, GatewayIdentity identity)
    {
        foreach (var name in incoming.Headers.AllKeys)
        {
            if (name == null || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("ChatGPT-Account-ID", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase) || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) || name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) || name.Equals("Expect", StringComparison.OrdinalIgnoreCase) || name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            outgoing.Headers.TryAddWithoutValidation(name, incoming.Headers[name]);
        }
        outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        if (identity.AccountId != null) outgoing.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", identity.AccountId);
    }
    private static async Task ErrorAsync(HttpListenerResponse response, int status, string message, CancellationToken cancellation)
    {
        response.StatusCode = status; response.ContentType = "application/json"; response.Headers["Cache-Control"] = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { error = new { message, type = "model_gateway_error" } }); response.ContentLength64 = bytes.Length;
        try { await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); }
        finally { response.Close(); }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Close();
        if (_accept != null) await _accept.ConfigureAwait(false);
        await Task.WhenAll(_requests.Values).ConfigureAwait(false);
        _stop.Dispose(); _slots.Dispose();
    }
}
