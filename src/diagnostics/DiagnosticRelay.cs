using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using CodexAccountDesk;

namespace CodexSwitch.Diagnostics;

internal sealed class DiagnosticRelay : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _local = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal byte[] GatewayKey { get; } = RandomNumberGenerator.GetBytes(32);
    private readonly Uri _upstream;
    private readonly string _path = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly Channel<object> _events = Channel.CreateBounded<object>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Task _writer;
    private NetworkTelemetry? _network;
    private long _dropped;
    internal string BaseUrl { get; private set; } = "";
    private static readonly HashSet<string> Hop = new(StringComparer.OrdinalIgnoreCase) { "Host", "Connection", "Upgrade", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Sec-WebSocket-Extensions", "Sec-WebSocket-Accept" };

    private DiagnosticRelay(string events, Uri upstream)
    {
        _upstream = upstream;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(DiagnosticRelay).Assembly.GetName().Name });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => { server.Listen(IPAddress.Loopback, 0); server.Limits.MaxRequestBodySize = null; });
        _app = builder.Build();
        _app.UseWebSockets();
        _app.Run(Forward);
        RelayInstallation.PrivateDirectory(Path.GetDirectoryName(events)!);
        _writer = WriteEvents(events);
    }

    internal static async Task<DiagnosticRelay> Start(string events, Uri? testUpstream = null)
    {
        var relay = new DiagnosticRelay(events, testUpstream ?? new Uri("https://chatgpt.com/backend-api/codex/"));
        try
        {
            await relay._app.StartAsync();
            var address = relay._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            relay.BaseUrl = address + relay._path;
            relay._network = new NetworkTelemetry(relay.Emit);
            relay.Emit(new { kind = "relay_started", pid = Environment.ProcessId });
            return relay;
        }
        catch { await relay.DisposeAsync(); throw; }
    }

    internal void Emit(object value)
    {
        var item = new { at = DateTimeOffset.UtcNow, processId = Environment.ProcessId, monotonicTicks = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency, dropped = Interlocked.Read(ref _dropped), data = value };
        if (!_events.Writer.TryWrite(item)) Interlocked.Increment(ref _dropped);
    }
    private async Task WriteEvents(string path)
    {
        StreamWriter? writer = null;
        try
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Event file must not be a link.");
            StreamWriter Open() => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536, true), new UTF8Encoding(false), 65536);
            writer = Open(); var flushed = Stopwatch.GetTimestamp();
            var readiness = _events.Reader.WaitToReadAsync().AsTask();
            while (true)
            {
                await Task.WhenAny(readiness, Task.Delay(Math.Max(1, 250 - (int)Stopwatch.GetElapsedTime(flushed).TotalMilliseconds)));
                var count = 0;
                while (count++ < 1024 && _events.Reader.TryRead(out var value)) await writer.WriteLineAsync(JsonSerializer.Serialize(value));
                if (Stopwatch.GetElapsedTime(flushed).TotalMilliseconds >= 250)
                {
                    await writer.FlushAsync(); flushed = Stopwatch.GetTimestamp();
                    if (new FileInfo(path).Length > 64 * 1024 * 1024)
                    { await writer.DisposeAsync(); File.Move(path, path + ".previous", true); writer = Open(); }
                }
                if (readiness.IsCompleted)
                {
                    if (!await readiness) break;
                    readiness = _events.Reader.WaitToReadAsync().AsTask();
                }
            }
            await writer.DisposeAsync(); writer = null;
        }
        catch (Exception error)
        {
            _events.Writer.TryComplete();
            // Never put exception messages, URLs, prompts or credentials in diagnostics.
            await Console.Error.WriteLineAsync("Diagnostic Relay metadata writer failed: " + error.GetType().Name);
        }
        finally { if (writer != null) writer.Dispose(); }
    }

    private async Task Forward(HttpContext context)
    {
        var watch = Stopwatch.StartNew(); var id = Guid.NewGuid().ToString("N");
        using var correlation = NetworkTelemetry.Correlate(id);
        var request = context.Request; var cancellation = context.RequestAborted;
        if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) || request.Headers.ContainsKey("Origin") ||
            !request.Path.StartsWithSegments(_path, out var remainder) || !request.Headers.ContainsKey("Authorization"))
        { context.Response.StatusCode = 403; return; }
        var local = remainder.StartsWithSegments("/gateway", out var gatewayPath);
        Uri target;
        if (local)
        {
            if (!DiagnosticGatewayEndpoint.TryResolve(GatewayKey, gatewayPath.Value, out var gateway))
            { context.Response.StatusCode = 403; return; }
            target = new Uri(gateway!.AbsoluteUri + request.QueryString.ToUriComponent());
        }
        else target = new Uri(_upstream.AbsoluteUri.TrimEnd('/') + remainder.ToUriComponent() + request.QueryString.ToUriComponent());
        var websocket = context.WebSockets.IsWebSocketRequest;
        static string? GuidHeader(string? value) => Guid.TryParse(value, out var guid) ? guid.ToString() : null;
        Emit(new { kind = "request", id, transport = websocket ? "websocket" : "http", method = request.Method,
            thread = GuidHeader(request.Headers["session_id"]) ?? GuidHeader(request.Headers["conversation_id"]), clientRequestId = GuidHeader(request.Headers["x-client-request-id"]) });
        try
        {
            using var outbound = new HttpRequestMessage(new HttpMethod(request.Method), target) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            ObservedStream? upload = null;
            var connectionTokens = request.Headers.Connection.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!websocket && (request.ContentLength.HasValue || request.Headers.ContainsKey("Content-Type") || request.Headers.ContainsKey("Transfer-Encoding")))
                outbound.Content = new StreamContent(upload = new ObservedStream(request.Body, sample => Observe(id, "request_metadata", sample, watch.Elapsed.TotalMilliseconds)));
            foreach (var header in request.Headers)
            {
                if (Hop.Contains(header.Key) || connectionTokens.Contains(header.Key, StringComparer.OrdinalIgnoreCase) || websocket && header.Key.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase)) continue;
                if (!outbound.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray())) outbound.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            if (websocket)
            {
                outbound.Headers.TryAddWithoutValidation("Connection", "Upgrade"); outbound.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                outbound.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
                outbound.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", request.Headers["Sec-WebSocket-Key"].ToString());
                if (request.Headers.TryGetValue("Sec-WebSocket-Protocol", out var protocols)) outbound.Headers.TryAddWithoutValidation("Sec-WebSocket-Protocol", protocols.ToArray());
            }
            Emit(new { kind = "request_dispatched", id, elapsedMs = watch.Elapsed.TotalMilliseconds });
            using var result = await (local ? _local : _http).SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (upload != null) Emit(new { kind = "upload", id, bytes = upload.BytesRead, localBodyReadWaitMs = upload.ReadWaitMs });
            string? Header(string key) => result.Headers.TryGetValues(key, out var values) ? string.Join(",", values) : null;
            var limits = new Dictionary<string, string?>();
            foreach (var name in new[] { "x-ratelimit-limit-requests", "x-ratelimit-limit-tokens", "x-ratelimit-remaining-requests", "x-ratelimit-remaining-tokens", "x-ratelimit-reset-requests", "x-ratelimit-reset-tokens", "x-ratelimit-limit-project-tokens", "x-ratelimit-remaining-project-tokens", "x-ratelimit-reset-project-tokens", "retry-after-ms", "openai-processing-ms" })
                limits[name] = Safe(Header(name));
            Emit(new { kind = "headers", id, status = (int)result.StatusCode, elapsedMs = watch.Elapsed.TotalMilliseconds,
                protocolFallback = local && websocket && (int)result.StatusCode == 426,
                requestId = Safe(Header("x-request-id")), retryAfter = Safe(Header("Retry-After")),
                remainingRequests = Safe(Header("x-ratelimit-remaining-requests")), remainingTokens = Safe(Header("x-ratelimit-remaining-tokens")), limits });
            if (websocket && result.StatusCode == HttpStatusCode.SwitchingProtocols)
            {
                var expected = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(request.Headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                if (Header("Sec-WebSocket-Accept") != expected) throw new IOException("Invalid upstream WebSocket handshake.");
                var protocol = Header("Sec-WebSocket-Protocol");
                await using var stream = await result.Content.ReadAsStreamAsync(cancellation);
                using var upstream = WebSocket.CreateFromStream(stream, false, protocol, Timeout.InfiniteTimeSpan);
                using var client = await context.WebSockets.AcceptWebSocketAsync(protocol);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                var send = Pump(client, upstream, id, "request_metadata", watch, stop.Token);
                var receive = Pump(upstream, client, id, "response_metadata", watch, stop.Token);
                await Task.WhenAny(send, receive); stop.Cancel();
                try { await Task.WhenAll(send, receive); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
            else
            {
                context.Response.StatusCode = (int)result.StatusCode;
                var responseTokens = (Header("Connection") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (var header in result.Headers.Concat(result.Content.Headers))
                    if (!Hop.Contains(header.Key) && !responseTokens.Contains(header.Key, StringComparer.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
                await using var stream = await result.Content.ReadAsStreamAsync(cancellation);
                using var observer = new EventObserver((sample, received) => Observe(id, "response_metadata", sample, watch.Elapsed.TotalMilliseconds, received));
                var buffer = new byte[16384]; int count; var first = true; long downloaded = 0; double readWaitMs = 0, writeWaitMs = 0;
                while (true)
                {
                    var readStart = Stopwatch.GetTimestamp(); count = await stream.ReadAsync(buffer, cancellation); var received = Stopwatch.GetTimestamp(); readWaitMs += Stopwatch.GetElapsedTime(readStart, received).TotalMilliseconds;
                    if (count == 0) break;
                    if (first) { first = false; Emit(new { kind = "first_body_byte", id, elapsedMs = watch.Elapsed.TotalMilliseconds }); }
                    observer.Append(buffer.AsSpan(0, count), received);
                    var writeStart = Stopwatch.GetTimestamp();
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    await context.Response.Body.FlushAsync(cancellation);
                    writeWaitMs += Stopwatch.GetElapsedTime(writeStart).TotalMilliseconds; downloaded += count;
                }
                Emit(new { kind = "download", id, bytes = downloaded, upstreamReadWaitMs = readWaitMs, downstreamWriteWaitMs = writeWaitMs });
            }
            Emit(new { kind = "completed", id, elapsedMs = watch.Elapsed.TotalMilliseconds });
        }
        catch (Exception error) when (error is HttpRequestException or IOException or WebSocketException or OperationCanceledException)
        {
            Emit(new { kind = "transport_failure", id, error = error.GetType().Name, elapsedMs = watch.Elapsed.TotalMilliseconds });
            // No retry and no alternate route: the native client owns its original recovery policy.
            context.Abort();
        }
    }

    private async Task Pump(WebSocket source, WebSocket destination, string id, string direction, Stopwatch watch, CancellationToken cancellation)
    {
        var buffer = new byte[16384];
        using var sample = new MemoryStream(); var oversized = false; long bytes = 0, firstReceived = 0, firstSend = 0; double receiveWaitMs = 0, sendWaitMs = 0;
        var messageId = Guid.NewGuid().ToString("N");
        while (!cancellation.IsCancellationRequested)
        {
            var readStart = Stopwatch.GetTimestamp(); var chunk = await source.ReceiveAsync(buffer.AsMemory(), cancellation); var received = Stopwatch.GetTimestamp();
            receiveWaitMs += Stopwatch.GetElapsedTime(readStart, received).TotalMilliseconds;
            if (chunk.MessageType == WebSocketMessageType.Close)
            {
                await destination.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure, source.CloseStatusDescription, cancellation); return;
            }
            if (firstReceived == 0) firstReceived = received;
            if (firstSend == 0) firstSend = Stopwatch.GetTimestamp();
            if (!oversized && sample.Length + chunk.Count <= 65536) sample.Write(buffer, 0, chunk.Count); else oversized = true;
            if (chunk.EndOfMessage)
            {
                if (!oversized && chunk.MessageType == WebSocketMessageType.Text) Observe(id, direction, sample.ToArray(), watch.Elapsed.TotalMilliseconds, received, firstSend, messageId);
                else
                {
                    if (oversized && chunk.MessageType == WebSocketMessageType.Text) ObservePrefix(id, direction, sample.ToArray(), watch.Elapsed.TotalMilliseconds, received, firstSend, messageId);
                    Emit(new { kind = "metadata_unavailable", id, direction, reason = oversized ? "message_over_64KiB" : "binary_message" });
                }
            }
            var writeStart = Stopwatch.GetTimestamp(); await destination.SendAsync(buffer.AsMemory(0, chunk.Count), chunk.MessageType, chunk.EndOfMessage, cancellation); var sent = Stopwatch.GetTimestamp();
            sendWaitMs += Stopwatch.GetElapsedTime(writeStart, sent).TotalMilliseconds; bytes += chunk.Count;
            if (chunk.EndOfMessage)
            {
                Emit(new { kind = "websocket_message", id, direction, messageId, bytes, receiveWaitMs, sendWaitMs, firstReceivedTicks = firstReceived, receivedTicks = received, sendStartedTicks = firstSend, sentTicks = sent, elapsedMs = watch.Elapsed.TotalMilliseconds });
                sample.SetLength(0); oversized = false;
                bytes = 0; firstReceived = 0; firstSend = 0; receiveWaitMs = 0; sendWaitMs = 0; messageId = Guid.NewGuid().ToString("N");
            }
        }
    }

    private static string? Safe(string? value) => value is { Length: > 0 and <= 160 } && value.All(c => char.IsAsciiLetterOrDigit(c) || "_-.:/ ,".Contains(c)) ? value : null;
    private void Observe(string id, string direction, ReadOnlyMemory<byte> bytes, double elapsed, long? receivedTicks = null, long? sendStartedTicks = null, string? messageId = null)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            string? Field(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Safe(value.GetString()) : null;
            var type = Field(root, "type");
            var data = root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object ? response : root;
            var error = root.TryGetProperty("error", out var detail) ? detail : data.TryGetProperty("error", out var nestedError) ? nestedError : default;
            var item = root.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
            var effort = direction == "request_metadata" && data.TryGetProperty("reasoning", out var reasoningConfig)
                ? Field(reasoningConfig, "effort") : null;
            var nonEmptyDelta = root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String && !delta.ValueEquals("");
            object? usage = null;
            if (data.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                long? Number(JsonElement node, string name) => node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var number) ? number : null;
                var cached = u.TryGetProperty("input_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object ? Number(inputDetails, "cached_tokens") : null;
                var reasoning = u.TryGetProperty("output_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object ? Number(outputDetails, "reasoning_tokens") : null;
                usage = new { inputTokens = Number(u, "input_tokens"), cachedInputTokens = cached, outputTokens = Number(u, "output_tokens"), reasoningOutputTokens = reasoning, totalTokens = Number(u, "total_tokens") };
            }
            // Never serialize an upstream object or text delta: only these explicit metadata fields.
            Emit(new { kind = direction, id, type, receivedTicks, sendStartedTicks, messageId, timingVersion = receivedTicks.HasValue ? 2 : 1,
                responseId = Field(root, "response_id") ?? (response.ValueKind == JsonValueKind.Object ? Field(response, "id") : null), itemId = Field(root, "item_id") ?? Field(item, "id"), itemType = Field(item, "type"), nonEmptyDelta,
                model = Field(data, "model"), serviceTier = Field(data, "service_tier"), effort,
                errorCode = Field(error, "code"), errorType = Field(error, "type"), usage, elapsedMs = elapsed,
                textDelta = type == "response.output_text.delta" });
        }
        catch (JsonException) { ObservePrefix(id, direction, bytes, elapsed, receivedTicks, sendStartedTicks, messageId); }
    }

    private void ObservePrefix(string id, string direction, ReadOnlyMemory<byte> bytes, double elapsed, long? receivedTicks = null, long? sendStartedTicks = null, string? messageId = null)
    {
        string? model = null, tier = null, type = null, responseId = null; var parseStatus = "partial"; var responseDepth = -1;
        try
        {
            var reader = new Utf8JsonReader(bytes.Span, false, new JsonReaderState());
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == responseDepth) responseDepth = -1;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var depth = reader.CurrentDepth;
                var field = reader.GetString();
                if (depth == 1 && field == "response") { if (reader.Read() && reader.TokenType == JsonTokenType.StartObject) responseDepth = reader.CurrentDepth; continue; }
                if (responseDepth >= 0 && depth == responseDepth + 1 && field == "id") { if (reader.Read() && reader.TokenType == JsonTokenType.String) responseId = Safe(reader.GetString()); continue; }
                if (depth != 1) continue;
                if (field is not ("model" or "service_tier" or "type")) continue;
                if (!reader.Read()) break;
                if (reader.TokenType != JsonTokenType.String) continue;
                var value = Safe(reader.GetString());
                if (field == "model") model = value; else if (field == "service_tier") tier = value; else type = value;
            }
        }
        catch (JsonException) { parseStatus = "incomplete_or_invalid_prefix"; }
        Emit(new { kind = "partial_metadata", id, direction, receivedTicks, sendStartedTicks, messageId, timingVersion = receivedTicks.HasValue ? 2 : 1, responseId, model, serviceTier = tier, type, parseStatus, elapsedMs = elapsed, remainingMetadataAvailable = false });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(); await _app.DisposeAsync(); _http.Dispose(); _local.Dispose(); _network?.Dispose();
        _events.Writer.TryComplete(); await _writer;
    }
}

internal sealed class ObservedStream(Stream inner, Action<ReadOnlyMemory<byte>> observe) : Stream
{
    private readonly MemoryStream _sample = new(); private bool _oversized, _observed;
    internal long BytesRead { get; private set; }
    internal double ReadWaitMs { get; private set; }
    public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) { var start = Stopwatch.GetTimestamp(); var n = inner.Read(buffer, offset, count); ReadWaitMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds; BytesRead += n; Sample(buffer.AsSpan(offset, n)); return n; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var start = Stopwatch.GetTimestamp(); var n = await inner.ReadAsync(buffer, cancellationToken); ReadWaitMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds; BytesRead += n; Sample(buffer.Span[..n]); return n; }
    private void Sample(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) { if (!_observed) observe(_sample.ToArray()); _observed = true; return; }
        if (!_oversized && _sample.Length + bytes.Length <= 65536) _sample.Write(bytes); else _oversized = true;
    }
    protected override void Dispose(bool disposing) { if (disposing) _sample.Dispose(); base.Dispose(disposing); }
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class EventObserver(Action<ReadOnlyMemory<byte>, long> observe) : IDisposable
{
    private readonly MemoryStream _line = new(); private bool _oversized;
    private long _received;
    internal void Append(ReadOnlySpan<byte> bytes, long received)
    {
        _received = received;
        foreach (var b in bytes)
        {
            if (b == 10)
            {
                if (_line.Length > 0)
                {
                    var line = _line.ToArray().AsMemory();
                    if (line.Span.StartsWith("data: "u8)) line = line[6..];
                    if (!line.Span.StartsWith(":"u8) && !line.Span.StartsWith("event:"u8) && !line.Span.StartsWith("[DONE]"u8)) observe(line, received);
                }
                _line.SetLength(0); _oversized = false;
            }
            else if (!_oversized && _line.Length < 65536) _line.WriteByte(b); else _oversized = true;
        }
    }
    public void Dispose() { if (_line.Length > 0) observe(_line.ToArray(), _received); _line.Dispose(); }
}
