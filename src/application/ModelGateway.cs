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

internal sealed partial class ModelGateway : IAsyncDisposable
{
    internal const int MaximumRequestBytes = 512 * 1024 * 1024;
    private const int SmallRequestBytes = 16 * 1024 * 1024;
    private const string RequestTooLarge = "This request exceeds the Switch gateway's 512 MiB limit. The original chat is unchanged; reduce attachments or use a text handoff.";
    private readonly ModelProviderStore _store;
    private readonly Func<string?, string?, GatewayIdentity?> _authenticate;
    private readonly HttpClient _http;
    private readonly GatewayHistoryBridge _history;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Task> _requests = new();
    private readonly SemaphoreSlim _slots = new(16, 16);
    private readonly SemaphoreSlim _largeBodies = new(8, 8);
    private readonly SemaphoreSlim _streamSlots = new(48, 48);
    private readonly SemaphoreSlim _rejections = new(4, 4);
    private readonly GatewayBodyBudget _bodyBudget = new();
    private readonly Dictionary<string, DateTimeOffset> _requestReceipts = new();
    private Task? _accept;
    private int _nextRequest;
    private sealed record NativeCatalog(Dictionary<string, JsonObject> Models, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, NativeCatalog> _nativeCatalogs = new();
    public int Port { get; private set; }
    public bool Running => _listener.IsListening;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastRequestAt { get; private set; }
    public string BaseUrl => "http://127.0.0.1:" + Port + "/backend-api/codex";
    internal ModelGateway(ModelProviderStore store, Func<string?, string?, GatewayIdentity?> authenticate, HttpClient http, string directory, Func<string?> home)
    {
        _store = store; _authenticate = authenticate; _http = http;
        _history = new(directory, store, http, home, identity =>
        {
            var current = authenticate("Bearer " + identity.Token, identity.AccountId);
            return current != null && current.Token == identity.Token && current.AccountId == identity.AccountId && current.ApiKey == identity.ApiKey;
        });
    }
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
                var admitted = _slots.Wait(0);
                if (!admitted && !_rejections.Wait(0)) { context.Response.Abort(); continue; }
                var id = Interlocked.Increment(ref _nextRequest);
                // Parsing/routing an already-buffered large body must not block acceptance.
                var task = Task.Run(() => admitted ? ServeAsync(context) : RejectAsync(context)); _requests[id] = task;
                _ = task.ContinueWith(_ => _requests.TryRemove(id, out var removed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync(HttpListenerContext context)
    {
        var started = false; var largeBody = false; var workSlot = true; var streamSlot = false;
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
            var routingSettings = _store.Read();
            LastRequestAt = DateTimeOffset.UtcNow;
            var path = request.Url?.AbsolutePath;
            if (path == "/backend-api/codex/responses" && request.IsWebSocketRequest)
            { await ErrorAsync(response, 426, "Use HTTP Responses with full input. This gateway does not support WebSocket continuation.", cancellation).ConfigureAwait(false); return; }
            if (path == "/backend-api/codex/models" && request.HttpMethod == "GET")
            {
                var catalog = await CatalogAsync(identity, request, cancellation).ConfigureAwait(false);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog);
                if (bytes.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The combined model catalog exceeds the client limit.");
                response.ContentType = "application/json"; response.Headers["Cache-Control"] = "no-store"; response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); return;
            }
            var imageRequest = path is "/backend-api/codex/images/generations" or "/backend-api/codex/images/edits";
            if (imageRequest && routingSettings.IsolatedProfile && !routingSettings.DesktopProfile)
            { await ErrorAsync(response, 400, "Built-in OpenAI images belong to native Codex. This process supports custom models only.", cancellation).ConfigureAwait(false); return; }
            if (request.HttpMethod != "POST" || path == null || !imageRequest && path is not ("/backend-api/codex/responses" or "/backend-api/codex/responses/compact"))
            { await ErrorAsync(response, 404, "This model gateway endpoint is not supported.", cancellation).ConfigureAwait(false); return; }
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            { await ErrorAsync(response, 415, "Use application/json for model and image requests.", cancellation).ConfigureAwait(false); return; }
            if (request.ContentLength64 > MaximumRequestBytes)
            { await ErrorAsync(response, 413, RequestTooLarge, cancellation).ConfigureAwait(false); return; }
            if (!string.IsNullOrEmpty(request.Headers["Content-Encoding"]) && request.Headers["Content-Encoding"] != "identity")
            { await ErrorAsync(response, 415, "Disable Codex request compression in the connection settings before using the local gateway.", cancellation).ConfigureAwait(false); return; }
            streamSlot = _streamSlots.Wait(0);
            if (!streamSlot) { await ErrorAsync(response, 503, "The local model gateway has reached its concurrent response limit.", cancellation).ConfigureAwait(false); return; }
            // Unknown-length bodies are budgeted incrementally. Keep small-request headroom.
            if (request.ContentLength64 < 0 || request.ContentLength64 > SmallRequestBytes)
            {
                largeBody = _largeBodies.Wait(0);
                if (!largeBody) { await ErrorAsync(response, 503, "The local model gateway is processing other large requests. Try again when they finish.", cancellation).ConfigureAwait(false); return; }
            }
            GatewayRequestBody input;
            using var uploadDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            uploadDeadline.CancelAfter(TimeSpan.FromMinutes(2));
            try { input = await GatewayRequestBody.ReadAsync(request.InputStream, request.ContentLength64, MaximumRequestBytes, _bodyBudget, largeBody, uploadDeadline.Token).ConfigureAwait(false); }
            catch (InvalidDataException)
            { await ErrorAsync(response, 413, RequestTooLarge, cancellation).ConfigureAwait(false); return; }
            using var bufferedInput = input;
            // Parse the owned buffer without ToArray or a mutable JSON tree for unchanged requests.
            var bytesIn = input.Bytes;
            var original = new GatewayJsonRequest(bytesIn);
            JsonObject? body = null;
            GatewayHistoryInput? history = null;
            void ReleaseInput()
            {
                // Keep the budgeted input only while a pre-response 507 retry is possible.
                // Successful response streams never retain request buffers/upload slots.
                original.Release(); history?.Release(); body = null; bytesIn = default;
                input.Dispose();
                if (largeBody) { _largeBodies.Release(); largeBody = false; }
                if (workSlot) { _slots.Release(); workSlot = false; }
            }
            if (imageRequest)
            {
                using var image = new HttpRequestMessage(HttpMethod.Post, new Uri((identity.ApiKey ? "https://api.openai.com/v1/" : "https://chatgpt.com/backend-api/codex/") + path["/backend-api/codex/".Length..]));
                CopyOpenAiHeaders(request, image, identity);
                image.Content = new ReadOnlyMemoryContent(bytesIn); image.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var imageResult = await _http.SendAsync(image, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                image.Content?.Dispose(); image.Content = null; ReleaseInput();
                if (!imageResult.IsSuccessStatusCode)
                { await ErrorAsync(response, (int)imageResult.StatusCode is >= 300 and < 400 ? 502 : (int)imageResult.StatusCode, "The OpenAI image endpoint returned HTTP " + (int)imageResult.StatusCode + ". No request was retried.", cancellation).ConfigureAwait(false); return; }
                response.StatusCode = (int)imageResult.StatusCode; response.ContentType = imageResult.Content.Headers.ContentType?.ToString() ?? "application/json";
                response.Headers["Cache-Control"] = "no-store";
                foreach (var name in new[] { "x-codex-imagegen-request-id", "x-request-id" })
                    if (imageResult.Headers.TryGetValues(name, out var values)) response.Headers[name] = values.FirstOrDefault();
                response.SendChunked = true; started = true;
                await using var imageStream = await imageResult.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await imageStream.CopyToAsync(response.OutputStream, cancellation).ConfigureAwait(false);
                LastError = null; return;
            }
            var model = original.Model;
            if (string.IsNullOrEmpty(model)) throw new InvalidDataException("A model must be selected.");
            // Snapshot a route and its key before the first upstream byte. Settings edits never retarget an in-flight request.
            var settings = routingSettings; var routed = model == ModelRouting.Model;
            var session = ConversationIdentity(request, original);
            if (routed)
            {
                if (!ModelRouting.Registered(settings, settings.SelectedRoute))
                { await ErrorAsync(response, 400, "Select an available model in Codex Switch before sending another request.", cancellation).ConfigureAwait(false); return; }
                model = settings.SelectedRoute!;
            }
            var providers = settings.Providers;
            var route = providers.SelectMany(p => p.Models.Select(m => (Provider: p, Model: m))).FirstOrDefault(pair => pair.Model.Alias(pair.Provider.Id) == model);
            if (model.StartsWith("cs_", StringComparison.Ordinal) && route.Provider == null)
            { await ErrorAsync(response, 404, "The selected custom model is no longer registered. Select another model.", cancellation).ConfigureAwait(false); return; }
            if (settings.IsolatedProfile && !settings.DesktopProfile && route.Provider == null)
            { await ErrorAsync(response, 400, "This is the custom-only Switch process. Use native Codex for OpenAI models.", cancellation).ConfigureAwait(false); return; }
            JsonObject? openAiModel = null;
            if (route.Provider == null && (openAiModel = await NativeModelAsync(model, identity, request, cancellation).ConfigureAwait(false)) == null)
            { await ErrorAsync(response, 404, "The selected model is not in this account's OpenAI catalog or registered custom routes. Select a model from the combined list.", cancellation).ConfigureAwait(false); return; }
            // Incremental provider state and opaque reasoning cannot be transferred between upstreams.
            // HTTP full-input replay is required for every routed request, including after gateway restart.
            if (original.Present("previous_response_id") || original.Present("conversation"))
            { await ErrorAsync(response, 400, "Use full-input HTTP Responses replay; provider-specific response references cannot be routed safely.", cancellation).ConfigureAwait(false); return; }
            var removeReasoning = false;
            var routeIdentity = route.Provider == null ? "openai" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(route.Provider.Id + "\n" + route.Provider.BaseUrl + "\n" + route.Provider.ApiKey)));
            var portableHistory = _store.RequiresPortableHistory(session, identity, routeIdentity);
            var source = route.Provider == null ? new GatewayHistorySource("openai", model) : GatewayHistoryBridge.Source(route.Provider, route.Model);
            history = new GatewayHistoryInput(original);
            var historyOperation = RequestKey(bytesIn, identity, routed ? ModelRouting.Model : routeIdentity, path) ?? Convert.ToHexString(SHA256.HashData(bytesIn.Span));
            if (portableHistory)
            {
                removeReasoning = original.HasEncrypted("reasoning");
            }
            Uri destination;
            var rewrite = routed || route.Provider != null;
            if (rewrite)
            {
                body = new JsonObject { ["model"] = route.Provider == null ? model : route.Model.UpstreamId };
                foreach (var option in GatewayRoutedContent.Fields(bytesIn).Where(field => field.Name is "reasoning" or "text"))
                {
                    if (option.Value.Length > 65536) throw new InvalidDataException("The model routing options are too large.");
                    body[option.Name] = JsonNode.Parse(option.Value.Span);
                }
            }
            if (route.Provider != null)
            {
                JsonObject? native = null;
                if ((!settings.IsolatedProfile || settings.DesktopProfile) && route.Model.GenericMetadata && route.Model.ReasoningOverride == null &&
                    (routed ? settings.OpenAiModels.Any(choice => choice.Id == route.Model.UpstreamId) : body!["reasoning"] != null))
                    native = await NativeModelAsync(route.Model.UpstreamId, identity, request, cancellation).ConfigureAwait(false);
                var metadata = ProviderReasoning.Metadata(route.Model, native);
                if (routed)
                {
                    if (original.HasInputType("input_image") && (metadata["input_modalities"] as JsonArray)?.Any(item => item?.GetValue<string>() == "image") != true)
                    { await ErrorAsync(response, 400, "The model selected in Codex Switch does not declare image input support. Select an image-capable model; no input was removed or sent.", cancellation).ConfigureAwait(false); return; }
                    ModelRouting.ApplyReasoning(body!, metadata);
                }
                var reasoningError = ProviderReasoning.PrepareRequest(route.Model, metadata, body!, native != null);
                if (reasoningError != null) { await ErrorAsync(response, 400, reasoningError, cancellation).ConfigureAwait(false); return; }
                if (route.Model.GenericMetadata)
                {
                    if (body!["text"] is JsonObject text) text.Remove("verbosity");
                }
                destination = new Uri(ProviderCatalog.Endpoint(route.Provider.BaseUrl), path.EndsWith("/compact", StringComparison.Ordinal) ? "responses/compact" : "responses");
            }
            else
            {
                if (routed)
                {
                    if (original.HasInputType("input_image") && (openAiModel!["input_modalities"] as JsonArray)?.Any(item => item?.GetValue<string>() == "image") != true)
                    { await ErrorAsync(response, 400, "The model selected in Codex Switch does not declare image input support. Select an image-capable model; no input was removed or sent.", cancellation).ConfigureAwait(false); return; }
                    ModelRouting.ApplyReasoning(body!, openAiModel!);
                }
                destination = new Uri((identity.ApiKey ? "https://api.openai.com/v1/" : "https://chatgpt.com/backend-api/codex/") + (path.EndsWith("/compact", StringComparison.Ordinal) ? "responses/compact" : "responses"));
            }
            var requestKey = RequestKey(bytesIn, identity, routed ? ModelRouting.Model : routeIdentity, path);
            var compaction = path.EndsWith("/compact", StringComparison.Ordinal) || history.CompactionTrigger;
            // Source-summary reservations are durable per checkpoint. Unknown history
            // cannot reserve or alter the destination's generation receipt.
            await _history.PrepareAsync(history, session, identity, source, settings, request, historyOperation, cancellation).ConfigureAwait(false);
            if (requestKey != null)
            {
                var admission = ReserveRequest(requestKey);
                if (admission != 0)
                {
                    await ErrorAsync(response, admission == 1 ? 400 : 503, admission == 1
                        ? "This Codex turn request has already been sent upstream. Its input was not replayed; start a new turn to retry."
                        : "The local model gateway's request receipt capacity is busy.", cancellation).ConfigureAwait(false);
                    return;
                }
            }
            if (compaction)
            {
                var checkpoint = await _history.CompactAsync(history, session, identity, source, settings, request, historyOperation, cancellation).ConfigureAwait(false);
                ReleaseInput();
                await WritePortableCompactionAsync(response, checkpoint, source.Model, path.EndsWith("/compact", StringComparison.Ordinal), cancellation).ConfigureAwait(false);
                _store.RecordSuccessfulRoute(session, identity, routeIdentity, source.Model, source.ProviderId);
                LastError = null; return;
            }
            var retries = 0;
            HttpRequestMessage CreateUpstream()
            {
                var upstream = new HttpRequestMessage(HttpMethod.Post, destination);
                if (route.Provider == null) CopyOpenAiHeaders(request, upstream, identity);
                else upstream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.Provider.ApiKey);
                upstream.Content = rewrite || removeReasoning || history.Changed ?
                    new GatewayRoutedContent(bytesIn, body ?? new JsonObject(), rewrite, route.Model?.GenericMetadata == true, removeReasoning, history.Replacements) :
                    new ReadOnlyMemoryContent(bytesIn);
                upstream.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return upstream;
            }
            async Task BeforeRetry(int retry, TimeSpan delay, CancellationToken token)
            {
                retries = retry;
                LastError = "Upstream HTTP 507; automatic recovery " + retry + "/" + GatewayUpstreamRetry.MaximumRetries + ".";
                if (!started)
                {
                    response.StatusCode = 200; response.ContentType = "text/event-stream";
                    response.Headers["Cache-Control"] = "no-store"; response.SendChunked = true; started = true;
                }
                // Real writes during backoff detect a cancelled/disconnected caller before
                // another inference request. Comments never create a response or chat turn.
                await RetryCommentAsync(response, ": Switch HTTP 507 recovery " + retry + "/3\n\n", token).ConfigureAwait(false);
                var until = DateTimeOffset.UtcNow + delay;
                while (DateTimeOffset.UtcNow < until)
                {
                    var remaining = until - DateTimeOffset.UtcNow;
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
                    await RetryCommentAsync(response, ": Switch waiting for upstream recovery\n\n", token).ConfigureAwait(false);
                }
                var current = _authenticate(request.Headers["Authorization"], request.Headers["ChatGPT-Account-ID"]);
                if (current == null || current.Token != identity.Token || current.AccountId != identity.AccountId || current.ApiKey != identity.ApiKey)
                    throw new InvalidOperationException("The original sign-in changed before automatic recovery.");
            }
            var streaming = original.Streaming;
            using var result = await GatewayUpstreamRetry.SendAsync(_http, CreateUpstream,
                path == "/backend-api/codex/responses" && streaming, BeforeRetry, cancellation).ConfigureAwait(false);
            ReleaseInput();
            if (!result.IsSuccessStatusCode)
            {
                LastError = "The selected upstream returned HTTP " + (int)result.StatusCode + ". " +
                    (retries == 0 ? "No request was retried." : "Automatic HTTP 507 recovery stopped after " + retries + " of 3 retries.");
                if (started) await RetryFailureAsync(response, LastError, cancellation).ConfigureAwait(false);
                else await ErrorAsync(response, (int)result.StatusCode is >= 300 and < 400 ? 502 : (int)result.StatusCode, LastError, cancellation).ConfigureAwait(false);
                return;
            }
            var responseMediaType = result.Content.Headers.ContentType?.MediaType ?? (streaming ? "text/event-stream" : "application/json");
            if (started && !string.Equals(responseMediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                LastError = "The selected upstream did not return the requested event stream after HTTP 507 recovery.";
                await RetryFailureAsync(response, LastError, cancellation).ConfigureAwait(false); return;
            }
            _store.RecordSuccessfulRoute(session, identity, routeIdentity, source.Model, source.ProviderId);
            if (!started)
            {
                response.StatusCode = (int)result.StatusCode; response.ContentType = result.Content.Headers.ContentType?.ToString() ?? responseMediaType;
                response.Headers["Cache-Control"] = "no-store"; response.SendChunked = true; started = true;
            }
            await using var stream = await result.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var provenance = new GatewayHistoryObserver(_history, session, identity, source, response.ContentType);
            var buffer = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            { provenance.Observe(buffer.AsSpan(0, count)); await response.OutputStream.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false); await response.OutputStream.FlushAsync(cancellation).ConfigureAwait(false); }
            LastError = null;
        }
        catch (GatewayBusyException)
        { if (!started) await TryErrorAsync(context.Response, 503, "The local model gateway's request buffer budget is busy. Try again when uploads finish.", cancellation).ConfigureAwait(false); }
        catch (GatewayHistoryException error)
        { LastError = error.Message; if (!started) await TryErrorAsync(context.Response, 400, error.Message, cancellation).ConfigureAwait(false); else AbortResponse(context.Response); }
        catch (OperationCanceledException) { LastError = "The model request was cancelled or timed out."; AbortResponse(context.Response); }
        catch (Exception error)
        {
            LastError = "The model request or upstream connection failed (" + error.GetType().Name + ").";
            if (started) AbortResponse(context.Response);
            else
            {
                try { await ErrorAsync(context.Response, error is InvalidDataException or JsonException or ArgumentException ? 400 : 502, LastError, cancellation).ConfigureAwait(false); }
                catch (Exception transport) when (transport is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { AbortResponse(context.Response); }
            }
        }
        finally { if (largeBody) _largeBodies.Release(); if (workSlot) _slots.Release(); if (streamSlot) _streamSlots.Release(); context.Response.Close(); }
    }
    internal Task<JsonObject> ReadCatalogAsync(GatewayIdentity identity, string clientVersion, CancellationToken cancellation, bool? router = null, bool desktop = false) =>
        CatalogAsync(identity, null, cancellation, clientVersion, router, desktop);
    private async Task<JsonObject> CatalogAsync(GatewayIdentity identity, HttpListenerRequest? incoming, CancellationToken cancellation, string? clientVersion = null, bool? router = null, bool desktop = false)
    {
        var profile = _store.Read();
        if (profile.IsolatedProfile && !profile.DesktopProfile && !desktop) return new JsonObject { ["models"] = new JsonArray(ModelRouting.Metadata()) };
        var version = clientVersion ?? incoming?.QueryString["client_version"] ?? "";
        if (version.Length is < 1 or > 80 || version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')))
            throw new InvalidDataException("The model catalog request has no valid client version.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/codex/models?client_version=" + Uri.EscapeDataString(version));
        if (incoming != null) CopyOpenAiHeaders(incoming, request, identity);
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
            if (identity.AccountId != null) request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", identity.AccountId);
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("The current account's OpenAI model catalog is unavailable.");
        var result = JsonNode.Parse(await ProviderCatalog.ReadLimitedAsync(response.Content, ProviderCatalog.MaximumCatalogBytes, cancellation).ConfigureAwait(false)) as JsonObject;
        if (result?["models"] is not JsonArray models || models.Count == 0) throw new InvalidDataException("OpenAI did not return a usable Codex model catalog.");
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (model is not JsonObject metadata || metadata["slug"]?.GetValue<string>() is not string slug || !slugs.Add(slug)) throw new InvalidDataException("The OpenAI catalog has invalid model IDs.");
        }
        if (models.Count > 256 || slugs.Contains(ModelRouting.Model) || slugs.Any(slug => slug.StartsWith("cs_", StringComparison.Ordinal)))
            throw new InvalidDataException("The OpenAI model catalog has too many models or reserved route IDs.");
        var choices = models.Cast<JsonObject>().Where(model => model["visibility"]?.GetValue<string>() == "list" &&
            (!identity.ApiKey || model["supported_in_api"]?.GetValue<bool>() == true)).Select(model => new ModelRouteChoice(model["slug"]!.GetValue<string>(),
                (model["display_name"]?.GetValue<string>() ?? model["slug"]!.GetValue<string>()) + " (OpenAI)")).ToList();
        ModelRouting.ValidateChoices(choices);
        if (_nativeCatalogs.Count >= 64) _nativeCatalogs.Clear();
        var nativeModels = models.Cast<JsonObject>().ToDictionary(m => m["slug"]!.GetValue<string>(), m => (JsonObject)m.DeepClone(), StringComparer.Ordinal);
        _nativeCatalogs[IdentityKey(identity)] = new NativeCatalog(nativeModels, DateTimeOffset.UtcNow.AddMinutes(5));
        _store.Update(settings =>
        {
            settings.LastClientVersion = version;
            settings.OpenAiModels = choices;
        });
        if (router ?? _store.Read().RouterEnabled)
        {
            models.Add(ModelRouting.Metadata());
            return result!;
        }
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
    private int ReserveRequest(string key)
    {
        lock (_requestReceipts)
        {
            var now = DateTimeOffset.UtcNow;
            if (_requestReceipts.Count >= 4096)
                foreach (var expired in _requestReceipts.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) _requestReceipts.Remove(expired);
            if (_requestReceipts.TryGetValue(key, out var until) && until > now) return 1;
            if (_requestReceipts.Count >= 4096) return 2;
            _requestReceipts[key] = now.AddMinutes(30); return 0;
        }
    }
    private static string? RequestKey(ReadOnlyMemory<byte> body, GatewayIdentity identity, string route, string path)
    {
        var fields = GatewayRoutedContent.Fields(body);
        var metadata = fields.FirstOrDefault(field => field.Name == "client_metadata").Value;
        if (metadata.IsEmpty || metadata.Length > 65536) return null;
        using var client = JsonDocument.Parse(metadata);
        if (client.RootElement.ValueKind != JsonValueKind.Object ||
            !client.RootElement.TryGetProperty("x-codex-turn-metadata", out var encoded) || encoded.ValueKind != JsonValueKind.String) return null;
        using var turnMetadata = JsonDocument.Parse(encoded.GetString()!);
        if (!Guid.TryParse(JsonValue.String(turnMetadata.RootElement, "turn_id"), out var turn)) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes((identity.AccountId ?? identity.Token) + "\n" + turn.ToString("D") + "\n" + route + "\n" + path));
        foreach (var field in fields.Where(field => field.Name != "client_metadata"))
        {
            hash.AppendData(Encoding.UTF8.GetBytes("\n" + field.Name + "\n"));
            hash.AppendData(field.Value.Span);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string? ConversationIdentity(HttpListenerRequest request, GatewayJsonRequest original)
    {
        var legacy = request.Headers["session_id"] ?? request.Headers["conversation_id"];
        var metadata = original.Get("client_metadata");
        if (metadata.IsEmpty) return legacy;
        if (metadata.Length > 65536) throw new GatewayHistoryException("Conversation metadata exceeds the safe identity bound.");
        using var document = JsonDocument.Parse(metadata);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new GatewayHistoryException("The conversation metadata is invalid.");
        var thread = JsonValue.String(document.RootElement, "thread_id");
        return !string.IsNullOrEmpty(thread) ? thread : legacy ?? JsonValue.String(document.RootElement, "session_id");
    }
    internal static string? ClientVersion(string? value)
    {
        if (value == null) return null;
        var direct = Regex.Match(value, @"^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$");
        if (direct.Success) return direct.Value.Split(new[] { '-', '+' })[0];
        var agent = Regex.Match(value, @"(?:Codex Desktop|codex_cli_rs|Codex CLI|codex_account_desk|codex)/(\d+\.\d+\.\d+)");
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
        if (status == 503) response.Headers["Retry-After"] = "1";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { error = new { message, type = "model_gateway_error" } }); response.ContentLength64 = bytes.Length;
        try { await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); }
        finally { response.Close(); }
    }
    private static async Task RetryCommentAsync(HttpListenerResponse response, string comment, CancellationToken cancellation)
    {
        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(comment), cancellation).ConfigureAwait(false);
        await response.OutputStream.FlushAsync(cancellation).ConfigureAwait(false);
    }
    private static Task RetryFailureAsync(HttpListenerResponse response, string message, CancellationToken cancellation) =>
        RetryCommentAsync(response, "data: " + JsonSerializer.Serialize(new { type = "response.failed", response = new {
            id = "switch_507_" + Guid.NewGuid().ToString("N"), @object = "response", status = "failed", output = Array.Empty<object>(),
            error = new { code = "switch_upstream_recovery_stopped", message } } }) + "\n\n", cancellation);
    private static async Task TryErrorAsync(HttpListenerResponse response, int status, string message, CancellationToken cancellation)
    {
        try { await ErrorAsync(response, status, message, cancellation).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException) { AbortResponse(response); }
    }
    private static void AbortResponse(HttpListenerResponse response)
    {
        try { response.Abort(); }
        // Listener shutdown can dispose HTTP.sys before the cancelled request unwinds.
        catch (ObjectDisposedException) { }
    }
    private async Task RejectAsync(HttpListenerContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { await TryErrorAsync(context.Response, 503, "The local model gateway is busy.", deadline.Token).ConfigureAwait(false); }
        finally { _rejections.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Close();
        if (_accept != null) await _accept.ConfigureAwait(false);
        await Task.WhenAll(_requests.Values).ConfigureAwait(false);
        _stop.Dispose(); _slots.Dispose(); _largeBodies.Dispose(); _streamSlots.Dispose(); _rejections.Dispose();
    }
}
