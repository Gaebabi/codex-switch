using CodexAccountDesk;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GatewayLoadContracts
{
    internal static async Task Run(string root, Action<string, bool> check, bool stress)
    {
        var budget = new GatewayBodyBudget();
        check("Aggregate buffer reservations preserve small-request headroom", budget.TryReserve(512 * 1024 * 1024, true) && !budget.TryReserve(512 * 1024 * 1024, true) && budget.TryReserve(16 * 1024 * 1024, false));
        budget.Release(528 * 1024 * 1024);
        check("Completed reservations return the entire aggregate buffer budget", budget.Bytes == 0);
        check("An occupied aggregate buffer budget can reserve small-request capacity", budget.TryReserve(960 * 1024 * 1024, true) && budget.TryReserve(64 * 1024 * 1024, false));
        using (var source = new MemoryStream(new byte[1]))
        {
            var refused = false;
            try { using var body = await GatewayRequestBody.ReadAsync(source, 1, 1, budget, true, CancellationToken.None); }
            catch (GatewayBusyException) { refused = true; }
            check("Aggregate saturation refuses before reading or allocating the next body", refused && source.Position == 0);
        }
        budget.Release(1024 * 1024 * 1024);
        using (var cancelled = new CancellationTokenSource())
        using (var source = new MemoryStream(new byte[64]))
        {
            cancelled.Cancel();
            try { using var body = await GatewayRequestBody.ReadAsync(source, 64, 64, budget, false, cancelled.Token); }
            catch (OperationCanceledException) { }
            check("Cancelled readers release their buffer reservation", budget.Bytes == 0);
        }
        foreach (var bad in new[] { "{\"model\":\"a\",\"mo\\u0064el\":\"b\"}", "{\"input\":[{\"x\":1,\"x\":2}]}", "{\"x\":[1,]}", "{}{}" })
        {
            var refused = false;
            try { _ = new GatewayJsonRequest(Encoding.UTF8.GetBytes(bad)); }
            catch (JsonException) { refused = true; }
            check("Token validation retains strict nested and escaped-name JSON refusal: " + bad, refused);
        }
        var exact = Encoding.UTF8.GetBytes("{\"model\":\"alias\",\"input\":[{\"type\":\"\\u0072easoning\",\"encrypted_content\":\"opaque\"},{\"role\":\"user\",\"content\":\"한글\\u0020image-data\"}],\"reasoning\":{\"effort\":\"high\"},\"prompt_cache_key\":\"remove\",\"extra\":{ \"kept\" : true }}");
        using (var routed = new GatewayRoutedContent(exact, new JsonObject { ["model"] = "load-model", ["reasoning"] = new JsonObject { ["effort"] = "high" } }, true, true, true))
        {
            var text = await routed.ReadAsStringAsync();
            var parsed = JsonNode.Parse(text)!;
            check("Sliced custom rewrites retain exact history bytes and remove escaped opaque reasoning", !text.Contains("opaque") && text.Contains("한글\\u0020image-data") && text.Contains("{ \"kept\" : true }") && parsed["model"]!.GetValue<string>() == "load-model" && parsed["reasoning"]!["effort"]!.GetValue<string>() == "high" && parsed["prompt_cache_key"] == null);
        }
        var tool = new JsonObject { ["type"] = "namespace", ["name"] = "fixture", ["tools"] = new JsonArray(new JsonObject { ["type"] = "function", ["name"] = "lookup", ["parameters"] = new JsonObject { ["type"] = "object" } }) };
        var message = new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = "Keep the original conversation" };
        var toolCall = new JsonObject { ["type"] = "function_call", ["name"] = "lookup", ["call_id"] = "fixture-call", ["arguments"] = "{}" };
        var declaration = new JsonObject { ["type"] = "additional_tools", ["role"] = "developer", ["tools"] = new JsonArray(tool.DeepClone()) };
        var input = new JsonObject { ["model"] = "alias", ["tools"] = new JsonArray(tool.DeepClone()),
            ["input"] = new JsonArray(message.DeepClone(), declaration, toolCall.DeepClone(), declaration.DeepClone()) };
        var inputBytes = JsonSerializer.SerializeToUtf8Bytes(input);
        using (var routed = new GatewayRoutedContent(inputBytes, new JsonObject { ["model"] = "load-model" }, true, true, false))
        {
            var parsed = JsonNode.Parse(await routed.ReadAsStringAsync())!;
            check("Native additional-tools events become standard declarations without duplicating tools or losing messages and calls",
                parsed["tools"]!.AsArray().Count == 1 && JsonNode.DeepEquals(parsed["tools"]![0], tool) && parsed["input"]!.AsArray().Count == 2 &&
                JsonNode.DeepEquals(parsed["input"]![0], message) && JsonNode.DeepEquals(parsed["input"]![1], toolCall));
        }
        input.Remove("tools");
        using (var routed = new GatewayRoutedContent(JsonSerializer.SerializeToUtf8Bytes(input), new JsonObject { ["model"] = "load-model" }, true, true, false))
            check("Additional tool schemas survive when the native request has no top-level tools array", JsonNode.DeepEquals(JsonNode.Parse(await routed.ReadAsStringAsync())!["tools"]![0], tool));
        using (var native = new GatewayRoutedContent(inputBytes, new JsonObject(), false, false, false))
            check("Native OpenAI transport retains additional-tools events unchanged", JsonNode.DeepEquals(JsonNode.Parse(await native.ReadAsStringAsync()), JsonNode.Parse(inputBytes)));
        input["tools"] = new JsonArray(tool.DeepClone());
        input["tools"]![0]!["description"] = "conflicting schema";
        var conflict = false;
        try { using var content = new GatewayRoutedContent(JsonSerializer.SerializeToUtf8Bytes(input), new JsonObject { ["model"] = "load-model" }, true, true, false); }
        catch (InvalidDataException) { conflict = true; }
        check("Conflicting historical and active tool definitions fail without guessing a schema", conflict);
        var handler = new LoadUpstream();
        var directory = Path.Combine(root, "load-providers");
        var store = new ModelProviderStore(directory);
        var custom = new ModelProvider { Name = "Load fixture", BaseUrl = "https://load.example.invalid/v1", ApiKey = "fictional-load-key" };
        custom.Models.Add(new ProviderModel { UpstreamId = "load-model", GenericMetadata = true, Metadata = ProviderCatalog.Generic("load-model") });
        store.Update(next => next.Providers.Add(custom));
        await using var gateway = new ModelGateway(store, (auth, _) => auth == "Bearer fictional-load-token" ? new GatewayIdentity("fictional-load-token", "fictional-load-account", false) : null, new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, directory, () => null);
        gateway.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-load-token");
        // Establish native-model metadata before concurrency measurements.
        using (var listed = await client.GetAsync(gateway.BaseUrl + "/models?client_version=0.163.0")) listed.EnsureSuccessStatusCode();

        var streams = new List<HttpResponseMessage>();
        try
        {
            handler.HoldResponses = true;
            for (var i = 0; i < 48; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, gateway.BaseUrl + "/responses") { Content = new StringContent("{\"model\":\"load-model\",\"input\":\"fixture\"}", Encoding.UTF8, "application/json") };
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                streams.Add(response); check("Concurrent response stream admitted: " + i, response.IsSuccessStatusCode);
            }
            var before = handler.Requests;
            using (var rejected = await client.PostAsync(gateway.BaseUrl + "/responses", new StringContent("{\"model\":\"load-model\"}", Encoding.UTF8, "application/json")))
                check("The 49th response refuses with retry guidance before inference", rejected.StatusCode == HttpStatusCode.ServiceUnavailable && rejected.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(1) && handler.Requests == before);
            using (var listed = await client.GetAsync(gateway.BaseUrl + "/models?client_version=0.163.0"))
                check("Model discovery remains available while 48 responses stream", listed.IsSuccessStatusCode);
        }
        finally
        {
            handler.HoldResponses = false;
            foreach (var slow in handler.Streams) slow.Finish.TrySetResult();
            foreach (var response in streams) { await response.Content.ReadAsStringAsync(); response.Dispose(); }
        }
        using (var recovered = await client.PostAsync(gateway.BaseUrl + "/responses", new StringContent("{\"model\":\"load-model\"}", Encoding.UTF8, "application/json")))
            check("The listener resumes generation after response overload drains", recovered.IsSuccessStatusCode);

        // Disconnect overload clients while all ordinary upload slots are occupied.
        var uploads = new List<TcpClient>();
        try
        {
            var endpoint = new Uri(gateway.BaseUrl);
            for (var i = 0; i < 16; i++)
            {
                var connection = new TcpClient(); uploads.Add(connection); await connection.ConnectAsync(endpoint.Host, endpoint.Port);
                var length = i < 8 ? "Transfer-Encoding: chunked\r\n" : "Content-Length: 100\r\n";
                await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST " + endpoint.AbsolutePath + "/responses HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nAuthorization: Bearer fictional-load-token\r\nContent-Type: application/json\r\n" + length + "\r\n"));
            }
            using (var busy = await client.GetAsync(gateway.BaseUrl + "/models?client_version=0.163.0"))
                check("Sixteen incomplete uploads refuse extra work with bounded overload handling", busy.StatusCode == HttpStatusCode.ServiceUnavailable);
            for (var i = 0; i < 24; i++)
            {
                using var disconnected = new TcpClient(); await disconnected.ConnectAsync(endpoint.Host, endpoint.Port);
                await disconnected.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET " + endpoint.AbsolutePath + "/models?client_version=0.163.0 HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\n\r\n"));
            }
        }
        finally { foreach (var upload in uploads) upload.Dispose(); }
        var resumed = false;
        for (var attempt = 0; attempt < 60 && !resumed; attempt++)
        {
            using var listed = await client.GetAsync(gateway.BaseUrl + "/models?client_version=0.163.0");
            resumed = listed.IsSuccessStatusCode;
            if (!resumed) await Task.Delay(50);
        }
        check("Disconnected overload clients cannot stop the accept loop or leak upload slots", resumed);

        if (!stress) return;
        var started = Stopwatch.StartNew();
        var initial = GC.GetTotalMemory(true);
        var peak = initial;
        using var monitorStop = new CancellationTokenSource();
        var monitor = Task.Run(async () => { while (!monitorStop.IsCancellationRequested) { peak = Math.Max(peak, GC.GetTotalMemory(false)); await Task.Delay(10); } });
        var measurements = new List<object>();
        long retained = 0;
        try
        {
            foreach (var endpoint in new[] { "/responses", "/responses/compact", "/images/edits" })
            {
                var before = handler.Bytes;
                using var request = new HttpRequestMessage(HttpMethod.Post, gateway.BaseUrl + endpoint) { Content = new GeneratedJsonContent(ModelGateway.MaximumRequestBytes, "load-model", endpoint.EndsWith("compact")) };
                using var response = await client.SendAsync(request); await response.Content.ReadAsStringAsync();
                check("An exact 512 MiB request is fully streamed to the original upstream: " + endpoint, response.IsSuccessStatusCode && handler.Bytes - before == ModelGateway.MaximumRequestBytes);
                measurements.Add(new { endpoint, bytes = ModelGateway.MaximumRequestBytes, status = (int)response.StatusCode });
                GC.Collect(); GC.WaitForPendingFinalizers();
                retained = Math.Max(retained, GC.GetTotalMemory(true));
            }
            var totalBefore = handler.Bytes; var clock = Stopwatch.StartNew();
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, gateway.BaseUrl + "/responses") { Content = new GeneratedJsonContent(65 * 1024 * 1024, custom.Models[0].Alias(custom.Id), false) };
                using var response = await client.SendAsync(request); await response.Content.ReadAsStringAsync(); return response.StatusCode;
            }));
            check("Eight concurrent 65 MiB custom uploads preserve throughput within the byte budget", results.All(status => status == HttpStatusCode.OK) && handler.Bytes - totalBefore > 8L * 64 * 1024 * 1024);
            measurements.Add(new { scenario = "eight-custom-uploads", bytes = handler.Bytes - totalBefore, milliseconds = clock.ElapsedMilliseconds, successes = results.Count(status => status == HttpStatusCode.OK) });
            check("Near-limit JSON parsing does not retain payload-sized pooled metadata", retained - initial < 64L * 1024 * 1024);
        }
        finally { monitorStop.Cancel(); await monitor; }
        Console.WriteLine(JsonSerializer.Serialize(new { gatewayLoad = new { measurements, elapsedMs = started.ElapsedMilliseconds, baselineManagedBytes = initial, peakManagedBytes = peak, retainedManagedBytesAfterLargeRequests = retained, bufferBudgetBytes = GatewayBodyBudget.MaximumBytes, scope = "Generated fictional bytes and streaming fake upstream; excludes external API and owner account acceptance." } }));
    }

    private sealed class LoadUpstream : HttpMessageHandler
    {
        internal bool HoldResponses;
        internal int Requests;
        internal long Bytes;
        internal ConcurrentQueue<GatedStream> Streams { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["models"] = new JsonArray(ProviderCatalog.Generic("load-model")) }.ToJsonString(), Encoding.UTF8, "application/json") };
            Interlocked.Increment(ref Requests);
            using var sink = new CountingSink();
            await request.Content!.CopyToAsync(sink, cancellation);
            Interlocked.Add(ref Bytes, sink.Bytes);
            if (HoldResponses)
            {
                var slow = new GatedStream(); Streams.Enqueue(slow);
                var content = new StreamContent(slow); content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return new(HttpStatusCode.OK) { Content = content };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }

    private sealed class CountingSink : Stream
    {
        internal long Bytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Bytes;
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Bytes += count;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Bytes += buffer.Length; return ValueTask.CompletedTask; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class GeneratedJsonContent : HttpContent
    {
        private readonly int _length;
        private readonly byte[] _prefix;
        private readonly byte[] _suffix = "\"}"u8.ToArray();
        private readonly bool _chunked;
        internal GeneratedJsonContent(int length, string model, bool chunked)
        { _length = length; _chunked = chunked; _prefix = Encoding.UTF8.GetBytes("{\"model\":" + JsonSerializer.Serialize(model) + ",\"input\":\""); Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
        protected override bool TryComputeLength(out long length) { length = _length; return !_chunked; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellation)
        {
            await stream.WriteAsync(_prefix, cancellation);
            var buffer = new byte[64 * 1024]; Array.Fill(buffer, (byte)'x');
            for (var remaining = _length - _prefix.Length - _suffix.Length; remaining > 0;)
            { var count = Math.Min(remaining, buffer.Length); await stream.WriteAsync(buffer.AsMemory(0, count), cancellation); remaining -= count; }
            await stream.WriteAsync(_suffix, cancellation);
        }
    }
}
