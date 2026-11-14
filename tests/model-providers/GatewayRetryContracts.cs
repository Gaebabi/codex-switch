using CodexAccountDesk;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GatewayRetryContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        check("507 recovery has exactly three increasing cancellable delays", GatewayUpstreamRetry.MaximumRetries == 3 &&
            Enumerable.Range(1, 3).Select(GatewayUpstreamRetry.Delay).SequenceEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }));
        foreach (var failures in new[] { 0, 1, 2, 3, 4 })
        {
            using var handler = new RetryUpstream { Failures = failures };
            using var http = new HttpClient(handler); var delays = new List<int>();
            using var result = await GatewayUpstreamRetry.SendAsync(http, () => RetryUpstream.Request("https://chatgpt.com/backend-api/codex/responses"), true,
                (retry, delay, token) => { delays.Add(retry); return Task.CompletedTask; }, CancellationToken.None);
            check("Explicit 507 sequence is bounded and stops on success: " + failures, handler.Requests.Count == Math.Min(failures + 1, 4) &&
                delays.SequenceEqual(Enumerable.Range(1, Math.Min(failures, 3))) && (int)result.StatusCode == (failures < 4 ? 200 : 507));
            check("Rejected responses are disposed before another attempt: " + failures, handler.Errors.All(content => content.Disposed) || failures == 4 && handler.Errors.Count(content => content.Disposed) == 3);
        }
        foreach (var status in new[] { 400, 401, 403, 413, 429, 500, 502, 503 })
        {
            using var handler = new RetryUpstream { OtherStatus = status };
            using var http = new HttpClient(handler);
            using var result = await GatewayUpstreamRetry.SendAsync(http, () => RetryUpstream.Request("https://chatgpt.com/backend-api/codex/responses"), true,
                (_, _, _) => throw new Exception("Unexpected retry"), CancellationToken.None);
            check("507 recovery never expands to another error code: " + status, (int)result.StatusCode == status && handler.Requests.Count == 1);
        }
        using (var handler = new RetryUpstream { ThrowTransport = true })
        using (var http = new HttpClient(handler))
        {
            var failed = false;
            try { using var result = await GatewayUpstreamRetry.SendAsync(http, () => RetryUpstream.Request("https://chatgpt.com/backend-api/codex/responses"), true,
                (_, _, _) => throw new Exception("Unexpected retry"), CancellationToken.None); }
            catch (HttpRequestException) { failed = true; }
            check("An ambiguous transport failure is never retried", failed && handler.Requests.Count == 1);
        }
        using (var handler = new RetryUpstream { Failures = 4 })
        using (var http = new HttpClient(handler))
        using (var stop = new CancellationTokenSource())
        {
            var cancelled = false;
            try { using var result = await GatewayUpstreamRetry.SendAsync(http, () => RetryUpstream.Request("https://chatgpt.com/backend-api/codex/responses"), true,
                async (_, _, token) => { stop.Cancel(); await Task.Delay(TimeSpan.FromHours(1), token); }, stop.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check("Cancellation during backoff cannot submit another inference", cancelled && handler.Requests.Count == 1);
        }

        // Full production listener: retain all bytes, selected destination and original
        // auth; comments are keep-alives rather than assistant or tool events.
        foreach (var custom in new[] { false, true })
        {
            using var handler = new RetryUpstream { Failures = 3 };
            await using var fixture = new Fixture(root, handler, custom);
            var beforeRoutes = JsonSerializer.Serialize(fixture.Store.Read().ConversationRoutes);
            using var response = await fixture.Send();
            var body = await response.Content.ReadAsStringAsync();
            var requests = handler.Requests.ToArray();
            check("Fourth attempt completes through native gateway after three 507s: " + custom, response.IsSuccessStatusCode && body.Contains("response.completed") && !body.Contains("response.failed") && requests.Length == 4);
            check("Every retry preserves the complete body and history: " + custom, requests.Select(item => item.Body).Distinct().Count() == 1 && requests.All(item => item.Body.Contains("fixture-call") && item.Body.Contains("original-tool-result") && item.Body.Contains("unchanged image")));
            check("Every retry preserves original account, model and destination: " + custom, requests.All(item => item.Host == (custom ? "retry.example.invalid" : "chatgpt.com") && item.Token == (custom ? "fictional-custom-retry-key" : "fictional-openai-retry-token") && item.Account == (custom ? null : "fictional-retry-account") && item.Session == (custom ? null : "retry-conversation")));
            check("HTTP 507 retries expose no upstream error body or extra response events: " + custom, !body.Contains("fictional-secret-in-error") && body.Split("response.completed").Length == 2 && body.Contains("recovery 3/3"));
            check("Only the recovered success commits conversation provenance: " + custom, JsonSerializer.Serialize(fixture.Store.Read().ConversationRoutes) != beforeRoutes && fixture.Gateway.LastError == null);
        }
        using (var handler = new RetryUpstream { Failures = 10 })
        {
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send();
            var body = await response.Content.ReadAsStringAsync();
            check("Persistent HTTP 507 stops after the original plus three requests", handler.Requests.Count == 4 && body.Contains("response.failed") && body.Contains("3 of 3 retries") && !body.Contains("response.completed"));
            check("Exhaustion never commits a route or leaks an upstream error", fixture.Store.Read().ConversationRoutes.Count == 0 && !body.Contains("fictional-secret-in-error") && fixture.Gateway.LastError!.Contains("507"));
        }
        using (var handler = new RetryUpstream { Failures = 1, OtherAfterFailures = 429 })
        {
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send(); var body = await response.Content.ReadAsStringAsync();
            check("A different failure after recovery is terminal without extra attempts", handler.Requests.Count == 2 && body.Contains("response.failed") && body.Contains("HTTP 429") && !body.Contains("fictional-secret-in-error"));
        }
        foreach (var path in new[] { "/images/generations", "/images/edits", "/responses?nonstream" })
        {
            using var handler = new RetryUpstream { Failures = 10 };
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send(path);
            check("Compaction, image and nonstream requests retain single-send policy: " + path, (int)response.StatusCode == 507 && handler.Requests.Count == 1);
        }
        using (var handler = new RetryUpstream { Failures = 10 })
        {
            await using var fixture = new Fixture(root, handler);
            // A raw reset avoids HttpClient's background response draining.
            var endpoint = new Uri(fixture.Gateway.BaseUrl);
            using var caller = new TcpClient(); await caller.ConnectAsync(endpoint.Host, endpoint.Port);
            caller.Client.LingerState = new LingerOption(true, 0);
            var input = "{\"model\":\"fixture-openai\",\"stream\":true,\"input\":\"disconnect fixture\"}";
            var bytes = Encoding.UTF8.GetBytes(input);
            var headers = Encoding.ASCII.GetBytes("POST " + endpoint.AbsolutePath + "/responses HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nAuthorization: Bearer fictional-openai-retry-token\r\nsession_id: retry-conversation\r\nversion: 0.163.0\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\n\r\n");
            await caller.GetStream().WriteAsync(headers); await caller.GetStream().WriteAsync(bytes);
            using var read = new StreamReader(caller.GetStream(), Encoding.UTF8, leaveOpen: true);
            string? line;
            do { line = await read.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)); } while (line != null && !line.Contains("Switch HTTP 507"));
            check("The first 507 starts only a comment keep-alive", line != null && handler.Requests.Count == 1);
            caller.Dispose();
            await Task.Delay(3000);
            check("A disconnected caller cannot trigger another upstream attempt", handler.Requests.Count == 1);
        }
        using (var handler = new RetryUpstream { Failures = 10 })
        {
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send();
            fixture.SignedIn = false;
            try { await response.Content.ReadAsStringAsync(); }
            catch (HttpRequestException) { }
            catch (IOException) { }
            check("A sign-in change during backoff stops recovery on the original request", handler.Requests.Count == 1 && fixture.Store.Read().ConversationRoutes.Count == 0);
        }
        using (var handler = new RetryUpstream { BrokenStream = true })
        {
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send();
            var interrupted = false;
            try { var body = await response.Content.ReadAsStringAsync(); interrupted = !body.Contains("response.completed"); }
            catch (HttpRequestException) { interrupted = true; }
            catch (IOException) { interrupted = true; }
            check("Failure after successful response headers is never replayed", interrupted && handler.Requests.Count == 1);
        }
        using (var handler = new RetryUpstream { Failures = 10 })
        {
            await using var fixture = new Fixture(root, handler);
            using var response = await fixture.Send();
            fixture.GatewayDisposed = true;
            await fixture.Gateway.DisposeAsync();
            await Task.Delay(250);
            check("Gateway shutdown cancels pending backoff before another attempt", handler.Requests.Count == 1);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly ModelProviderStore Store;
        internal readonly ModelGateway Gateway;
        private readonly HttpClient _upstream;
        private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(50) };
        private readonly string _model;
        internal bool SignedIn = true;
        internal bool GatewayDisposed;
        internal Fixture(string root, RetryUpstream handler, bool custom = false)
        {
            var directory = Path.Combine(root, "retry-" + Guid.NewGuid().ToString("N"));
            Store = new ModelProviderStore(directory);
            var model = new ProviderModel { UpstreamId = "fixture-openai", Metadata = ProviderCatalog.Generic("fixture-openai") };
            var provider = new ModelProvider { Name = "Retry fixture", BaseUrl = "https://retry.example.invalid/v1", ApiKey = "fictional-custom-retry-key", Models = new() { model } };
            if (custom) Store.Update(next => next.Providers.Add(provider));
            _model = custom ? model.Alias(provider.Id) : model.UpstreamId;
            _upstream = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            Gateway = new ModelGateway(Store, (_, _) => SignedIn ? new GatewayIdentity("fictional-openai-retry-token", "fictional-retry-account", false) : null, _upstream, directory, () => null);
            Gateway.Start();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-openai-retry-token");
            _client.DefaultRequestHeaders.Add("session_id", "retry-conversation");
            _client.DefaultRequestHeaders.Add("version", "0.163.0");
        }
        internal async Task<HttpResponseMessage> Send(string path = "/responses")
        {
            var nonstream = path.EndsWith("?nonstream", StringComparison.Ordinal);
            var body = JsonSerializer.Serialize(new { model = _model, stream = !nonstream, input = new object[] {
                new { type = "function_call", call_id = "fixture-call", name = "fixture_tool", arguments = "{}" },
                new { type = "function_call_output", call_id = "fixture-call", output = "original-tool-result" },
                new { role = "user", content = "unchanged image / Korean 원문" } } });
            using var request = new HttpRequestMessage(HttpMethod.Post, Gateway.BaseUrl + path.Split('?')[0]) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        public async ValueTask DisposeAsync() { _client.Dispose(); if (!GatewayDisposed) await Gateway.DisposeAsync(); _upstream.Dispose(); }
    }
    private sealed record Captured(string Body, string Host, string? Token, string? Account, string? Session);
    private sealed class RetryUpstream : HttpMessageHandler
    {
        internal int Failures;
        internal int? OtherStatus;
        internal int? OtherAfterFailures;
        internal bool ThrowTransport;
        internal bool BrokenStream;
        internal readonly ConcurrentQueue<Captured> Requests = new();
        internal readonly ConcurrentQueue<DisposedContent> Errors = new();
        internal static HttpRequestMessage Request(string url) => new(HttpMethod.Post, url) { Content = new StringContent("{\"model\":\"fixture-openai\",\"stream\":true}") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("fixture-openai")) }.ToJsonString(), Encoding.UTF8, "application/json") };
            var body = await request.Content!.ReadAsStringAsync(cancellation);
            Requests.Enqueue(new(body, request.RequestUri!.Host, request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("ChatGPT-Account-ID", out var account) ? account.Single() : null,
                request.Headers.TryGetValues("session_id", out var session) ? session.Single() : null));
            if (ThrowTransport) throw new HttpRequestException("Owned ambiguous transport fixture");
            if (BrokenStream) return new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenResponse()) { Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") } } };
            var code = OtherStatus ?? (Requests.Count <= Failures ? 507 : OtherAfterFailures ?? 200);
            if (code != 200)
            {
                var content = new DisposedContent(); Errors.Enqueue(content);
                return new((HttpStatusCode)code) { Content = content };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"recovered-fixture\",\"status\":\"completed\",\"output\":[]}}\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
    private sealed class DisposedContent : StringContent
    {
        internal bool Disposed;
        internal DisposedContent() : base("fictional-secret-in-error") { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class BrokenResponse : Stream
    {
        private bool _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) throw new IOException("Owned partial-stream interruption");
            _read = true;
            var bytes = Encoding.UTF8.GetBytes("data: {\"type\":\"response.created\"}\n\n"); bytes.CopyTo(buffer); return ValueTask.FromResult(bytes.Length);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
