using CodexSwitch.Diagnostics;
using CodexAccountDesk;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "switch-relay-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = new List<string>();
void Check(string name, bool value) { if (!value) throw new Exception(name); checks.Add(name); }
try
{
    string? current = "previous-Switch-adapter";
    var native = Path.Combine(root, OperatingSystem.IsWindows() ? "codex.exe" : "codex"); File.WriteAllText(native, "fictional fixture");
    var settings = Path.Combine(root, "config.toml"); File.WriteAllText(settings, "model = \"owner-model\"\n"); var before = File.ReadAllBytes(settings);
    var state = new RelayInstallation(Path.Combine(root, "state"), Path.Combine(root, "relay.exe"), () => current, v => current = v);
    state.Enable(native); Check("ON selects only the separate relay executable", state.Enabled);
    state.Enable(native); Check("Repeated ON retains the original restoration receipt", state.Read().PreviousOverride == "previous-Switch-adapter");
    state.Disable(); Check("OFF removes the launch override for original bundled native discovery", current == null && !state.Enabled);
    state.Disable(); Check("Repeated OFF is idempotent", current == null);
    state.Enable(native); File.Delete(native); state.Disable();
    Check("OFF still restores native discovery after an update removes the old executable", current == null && state.RequestedMode == "off_requested");
    File.WriteAllText(native, "fictional fixture");
    state.Enable(native); current = "another-owner-override";
    var refused = false; try { state.Disable(); } catch (IOException) { refused = true; }
    Check("OFF refuses to overwrite a foreign launch change", refused && current == "another-owner-override");
    Check("ON and OFF leave native config bytes unchanged", before.SequenceEqual(File.ReadAllBytes(settings)));
    current = null; state.Enable(native);
    var upgradedExecutable = Path.Combine(root, "upgraded-relay.exe"); File.WriteAllText(upgradedExecutable, "fixture");
    var upgraded = new RelayInstallation(Path.Combine(root, "state"), upgradedExecutable, () => current, v => current = v);
    var previousReceipt = state.Read(); upgraded.Upgrade();
    Check("Upgrade preserves ON, native home and original launch recovery", upgraded.Enabled && current == upgradedExecutable && upgraded.Read() == (previousReceipt with { RelayExecutable = upgradedExecutable }));
    upgraded.Disable();
    var disabledExecutable = Path.Combine(root, "disabled-relay.exe"); File.WriteAllText(disabledExecutable, "fixture");
    var disabledUpgrade = new RelayInstallation(Path.Combine(root, "state"), disabledExecutable, () => current, v => current = v);
    disabledUpgrade.Upgrade();
    Check("Upgrade preserves native OFF without enabling a launch override", current == null && disabledUpgrade.RequestedMode == "off_requested");
    var pairedRoot = Path.Combine(root, "paired-package"); Directory.CreateDirectory(Path.Combine(pairedRoot, "backend")); Directory.CreateDirectory(Path.Combine(pairedRoot, "diagnostics"));
    var authoritative = Path.Combine(root, "authoritative-switch-data");
    File.WriteAllText(Path.Combine(pairedRoot, "backend", "CodexSwitch.Backend.json"), JsonSerializer.Serialize(new { Directory = authoritative }));
    var pairedExe = Path.Combine(pairedRoot, "diagnostics", "CodexSwitch.Diagnostics.exe"); File.WriteAllText(pairedExe, "fixture");
    string? pairedLaunch = null;
    var pairedStore = new RelayInstallation(executable: pairedExe, get: () => pairedLaunch, set: value => pairedLaunch = value); pairedStore.Enable(native);
    var freshStore = new RelayInstallation(executable: pairedExe, get: () => pairedLaunch, set: value => pairedLaunch = value);
    var storageStatus = JsonSerializer.SerializeToElement(freshStore.Status());
    Check("A fresh launch resolves the paired explicit state directory rather than caller LocalAppData", freshStore.Enabled && storageStatus.GetProperty("stateDirectory").GetString() == Path.Combine(authoritative, "diagnostic-relay"));
    Check("The executable stores a stable explicit diagnostic bootstrap", File.Exists(Path.Combine(pairedRoot, "diagnostics", "CodexSwitch.Diagnostics.json")) && freshStore.Read() == pairedStore.Read());
    var combinedRoot = Path.Combine(root, "versioned");
    var previousBackend = Path.Combine(combinedRoot, "old", "backend", "CodexSwitch.Backend.exe");
    var combinedRelay = Path.Combine(combinedRoot, "new", "diagnostics", "CodexSwitch.Diagnostics.exe");
    var combinedBackend = Path.Combine(combinedRoot, "new", "backend", "CodexSwitch.Backend.exe");
    var combinedState = Path.Combine(root, "combined-data", "diagnostic-relay");
    foreach (var file in new[] { previousBackend, combinedRelay, combinedBackend })
    { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
    var combinedHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    File.WriteAllText(Path.ChangeExtension(combinedBackend, ".json"), JsonSerializer.Serialize(new
    { NativeExecutable = native, Home = combinedHome, Directory = Path.GetDirectoryName(combinedState) }));
    string? combinedLaunch = previousBackend;
    var combined = new RelayInstallation(combinedState, combinedRelay, () => combinedLaunch, value => combinedLaunch = value);
    combined.Enable(native);
    Check("ON selects the paired backend for previously enabled custom models", combined.Enabled && combined.PairedBackend(combined.Read()) == combinedBackend);
    combinedLaunch = previousBackend;
    Check("A restored Switch catalog is distinct from a foreign launcher", combined.RequestedMode == "custom_backend");
    combined.Disable();
    Check("OFF still removes all owned adapters", combinedLaunch == null && combined.RequestedMode == "off_requested");
    var direct = NativeRelayProcess.StartInfo(native, ["app-server", "--analytics-default-enabled"], null);
    Check("OFF launch preserves exact native arguments with no relay or adapter environment", direct.FileName == native && direct.ArgumentList.SequenceEqual(new[] { "app-server", "--analytics-default-enabled" }) && !direct.Environment.ContainsKey("CODEX_CLI_PATH"));
    var diagnostic = NativeRelayProcess.StartInfo(native, ["app-server"], "http://127.0.0.1:1234/fixture");
    Check("ON injects only the process-scoped endpoint", diagnostic.ArgumentList.Count == 3 && diagnostic.ArgumentList[0] == "-c" && diagnostic.ArgumentList[2] == "app-server");
    var protocolRecords = new List<object>(); var protocolObserver = new ProtocolTelemetry(protocolRecords.Add);
    var protocolInput = Encoding.UTF8.GetBytes("{\"id\":42,\"method\":\"turn/start\",\"params\":{\"threadId\":\"fixture-thread\",\"input\":\"secret protocol 한글\"}}\n");
    using (var source = new MemoryStream(protocolInput)) using (var destination = new MemoryStream())
    { await protocolObserver.Copy(source, destination, "client_to_native"); Check("Native JSON-RPC bytes including Unicode are relayed verbatim", destination.ToArray().SequenceEqual(protocolInput)); }
    void Protocol(string json) => protocolObserver.Observe("native_to_client", Encoding.UTF8.GetBytes(json));
    Protocol("{\"method\":\"turn/started\",\"params\":{\"threadId\":\"fixture-thread\",\"turn\":{\"id\":\"fixture-turn\"}}}");
    Protocol("{\"method\":\"item/started\",\"params\":{\"threadId\":\"fixture-thread\",\"turnId\":\"fixture-turn\",\"item\":{\"id\":\"tool-a\",\"type\":\"commandExecution\",\"command\":\"secret command\"}}}");
    await Task.Delay(5);
    Protocol("{\"method\":\"item/completed\",\"params\":{\"threadId\":\"fixture-thread\",\"turnId\":\"fixture-turn\",\"item\":{\"id\":\"tool-a\",\"type\":\"commandExecution\",\"output\":\"secret tool output\"}}}");
    Protocol("{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"fixture-thread\",\"turn\":{\"id\":\"fixture-turn\"}}}");
    Protocol("{\"id\":42,\"result\":{\"turn\":{\"id\":\"fixture-turn\"}}}");
    Protocol("{\"method\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"fixture-thread\",\"turnId\":\"fixture-turn\",\"tokenUsage\":{\"last\":{\"inputTokens\":100,\"cachedInputTokens\":80,\"outputTokens\":15,\"reasoningOutputTokens\":3,\"totalTokens\":115},\"total\":{\"totalTokens\":230}}}}");
    var protocolLog = JsonSerializer.Serialize(protocolRecords);
    Check("Protocol telemetry measures tool, turn, RPC and native token usage without content", protocolLog.Contains("turn_summary") && protocolLog.Contains("toolActiveUnionMs") && protocolLog.Contains("rpcMs") && protocolLog.Contains("native_token_usage") && protocolLog.Contains("\"reasoningOutputTokens\":3") && !protocolLog.Contains("secret"));
    var immediateRecords = new List<object>(); var immediate = new ProtocolTelemetry(immediateRecords.Add);
    using (var source = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":7,\"method\":\"config/read\",\"params\":{}}\n")))
    using (var destination = new ImmediateReplyStream(() => immediate.Observe("native_to_client", Encoding.UTF8.GetBytes("{\"id\":7,\"result\":{}}"))))
        await immediate.Copy(source, destination, "client_to_native");
    using (var records = JsonDocument.Parse(JsonSerializer.Serialize(immediateRecords)))
        Check("An immediate native reply cannot race ahead of its RPC timing registration", records.RootElement.EnumerateArray().Any(r => r.TryGetProperty("rpcMs", out var duration) && duration.ValueKind == JsonValueKind.Number));
    var resourceRecords = new System.Collections.Concurrent.ConcurrentQueue<object>();
    await using (var resource = new ResourceTelemetry(System.Diagnostics.Process.GetCurrentProcess(), resourceRecords.Enqueue)) { resource.Start(); await Task.Delay(600); }
    var resourceLog = JsonSerializer.Serialize(resourceRecords);
    Check("Physical disk counters and process pipe-inclusive I/O have explicit separate scopes", resourceLog.Contains("whole_machine_all_processes") && resourceLog.Contains("including_pipes_not_physical_disk") && resourceLog.Contains("resource_sample"));

    var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
    await using var upstream = builder.Build(); upstream.UseWebSockets();
    var requests = 0; byte[]? actualBody = null; string? actualAuth = null;
    var expectedError = "{\"error\":{\"code\":\"rate_limit_exceeded\",\"type\":\"rate_limit_error\",\"message\":\"secret error text\"}}";
    upstream.Run(async c =>
    {
        Interlocked.Increment(ref requests); actualAuth = c.Request.Headers.Authorization;
        if (c.WebSockets.IsWebSocketRequest && c.Request.Path != "/responses/ws-limited")
        {
            using var socket = await c.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[16384]; using var body = new MemoryStream(); ValueWebSocketReceiveResult received;
            do { received = await socket.ReceiveAsync(buffer.AsMemory(), c.RequestAborted); body.Write(buffer, 0, received.Count); } while (!received.EndOfMessage);
            actualBody = body.ToArray();
            var response = Encoding.UTF8.GetBytes("{\"type\":\"response.output_text.delta\",\"delta\":\"secret output 한글\"}");
            await socket.SendAsync(response.AsMemory(0, 17), WebSocketMessageType.Text, false, c.RequestAborted);
            await socket.SendAsync(response.AsMemory(17), WebSocketMessageType.Text, true, c.RequestAborted);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", c.RequestAborted); return;
        }
        using var input = new MemoryStream(); await c.Request.Body.CopyToAsync(input); actualBody = input.ToArray();
        if (c.Request.Path == "/responses/limited" || c.Request.Path == "/responses/ws-limited")
        { c.Response.StatusCode = 429; c.Response.Headers.RetryAfter = "31"; c.Response.Headers["x-request-id"] = "req-fixture"; c.Response.Headers["x-ratelimit-remaining-requests"] = "0"; c.Response.Headers["x-ratelimit-reset-requests"] = "31s"; await c.Response.WriteAsync(expectedError); }
        else
        {
            c.Response.ContentType = "text/event-stream";
            await c.Response.WriteAsync("data: {\"type\":\"response.output_text.delta\",\"delta\":\"secret output 한글\"}\n\n"); await c.Response.Body.FlushAsync();
            await Task.Delay(20);
            await c.Response.WriteAsync("data: {\"type\":\"response.completed\",\"response\":{\"model\":\"gpt-6-astra\",\"service_tier\":\"ultrafast\",\"usage\":{\"input_tokens\":100,\"input_tokens_details\":{\"cached_tokens\":80},\"output_tokens\":15,\"output_tokens_details\":{\"reasoning_tokens\":3}}}}\n\n");
        }
    });
    await upstream.StartAsync();
    var address = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var events = Path.Combine(root, "events", "events.jsonl");
    await using (var relay = await DiagnosticRelay.Start(events, new Uri(address + "/")))
    {
        using var http = new HttpClient(); http.DefaultRequestHeaders.Authorization = new("Bearer", "secret-auth-token");
        var payload = Encoding.UTF8.GetBytes("{\"model\":\"gpt-6-astra\",\"service_tier\":\"ultrafast\",\"input\":\"secret prompt 한글\"}");
        using var limited = await http.PostAsync(relay.BaseUrl + "/responses/limited", new ByteArrayContent(payload));
        Check("429 status, Retry-After and original error bytes are preserved", (int)limited.StatusCode == 429 && limited.Headers.RetryAfter?.Delta?.TotalSeconds == 31 && await limited.Content.ReadAsStringAsync() == expectedError);
        Check("The relay never retries a rejected inference", requests == 1);
        Check("HTTP request bytes and authorization reach only the upstream", actualBody!.SequenceEqual(payload) && actualAuth == "Bearer secret-auth-token");
        using (var handshake = new HttpRequestMessage(HttpMethod.Get, relay.BaseUrl + "/responses/ws-limited"))
        {
            handshake.Headers.TryAddWithoutValidation("Connection", "Upgrade"); handshake.Headers.TryAddWithoutValidation("Upgrade", "websocket");
            handshake.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13"); handshake.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
            using var failure = await http.SendAsync(handshake);
            Check("A refused WebSocket handshake preserves the 429 body and headers without fallback", (int)failure.StatusCode == 429 && failure.Headers.RetryAfter?.Delta?.TotalSeconds == 31 && await failure.Content.ReadAsStringAsync() == expectedError && requests == 2);
        }
        using var streamed = await http.PostAsync(relay.BaseUrl + "/responses", new ByteArrayContent(payload));
        var text = await streamed.Content.ReadAsStringAsync();
        Check("SSE content and Unicode survive unchanged", text.Contains("secret output 한글") && text.Contains("response.completed"));
        var countBefore = requests;
        var customUrl = DiagnosticGatewayEndpoint.ForPort(relay.BaseUrl, relay.GatewayKey, new Uri(address).Port);
        using var custom = await http.PostAsync(customUrl + "/responses", new ByteArrayContent(payload));
        Check("An authenticated paired gateway route preserves the full custom SSE stream",
            (await custom.Content.ReadAsStringAsync()).Contains("response.completed") && actualBody!.SequenceEqual(payload));
        using var unauthorized = await http.PostAsync(customUrl[..^1] + (customUrl[^1] == 'A' ? "B" : "A") + "/responses", new ByteArrayContent(payload));
        Check("Changing the gateway authorization never reaches a loopback target", unauthorized.StatusCode == HttpStatusCode.Forbidden && requests == countBefore + 1);
        countBefore = requests;
        using var wrong = await http.PostAsync(relay.BaseUrl.Replace(relay.BaseUrl[(relay.BaseUrl.LastIndexOf('/') + 1)..], "wrong") + "/responses", new ByteArrayContent(payload));
        Check("A wrong capability path does not reach OpenAI", (int)wrong.StatusCode == 403 && requests == countBefore);
        using var ws = new ClientWebSocket(); ws.Options.SetRequestHeader("Authorization", "Bearer secret-auth-token");
        await ws.ConnectAsync(new Uri(relay.BaseUrl.Replace("http:", "ws:") + "/responses"), CancellationToken.None);
        await ws.SendAsync(payload.AsMemory(0, 10), WebSocketMessageType.Text, false, CancellationToken.None);
        await ws.SendAsync(payload.AsMemory(10), WebSocketMessageType.Text, true, CancellationToken.None);
        using var output = new MemoryStream(); var wsbuffer = new byte[16384]; ValueWebSocketReceiveResult reply;
        do { reply = await ws.ReceiveAsync(wsbuffer.AsMemory(), CancellationToken.None); output.Write(wsbuffer, 0, reply.Count); } while (!reply.EndOfMessage);
        Check("WebSocket fragmented input bytes and text output survive", actualBody!.SequenceEqual(payload) && Encoding.UTF8.GetString(output.ToArray()).Contains("secret output 한글"));
        // A large message must be forwarded even when metadata sampling is bounded.
        using var largeWs = new ClientWebSocket(); largeWs.Options.SetRequestHeader("Authorization", "Bearer secret-auth-token");
        await largeWs.ConnectAsync(new Uri(relay.BaseUrl.Replace("http:", "ws:") + "/responses"), CancellationToken.None);
        var large = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { input = new string('x', 100000) }));
        await largeWs.SendAsync(large.AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
        await largeWs.ReceiveAsync(wsbuffer.AsMemory(), CancellationToken.None);
        Check("Large WebSocket payload is not truncated by diagnostics", actualBody!.SequenceEqual(large));
    }
    var log = await File.ReadAllTextAsync(events);
    Check("Diagnostics exclude auth, prompts, output and raw error messages", !new[] { "secret-auth-token", "secret prompt", "secret output", "secret error text" }.Any(log.Contains));
    Check("Diagnostics retain quota resets, 429, retry delay, tier and cached/reasoning token counts", log.Contains("rate_limit_exceeded") && log.Contains("\"retryAfter\":\"31\"") && log.Contains("ultrafast") && log.Contains("\"outputTokens\":15") && log.Contains("\"cachedInputTokens\":80") && log.Contains("\"reasoningOutputTokens\":3") && log.Contains("31s"));
    Check("Oversized metadata sampling is explicitly reported", log.Contains("message_over_64KiB"));
    var timingRows = File.ReadAllLines(events).Select(line => JsonDocument.Parse(line)).ToArray();
    try
    {
        var deltaRecord = timingRows.Select(d => d.RootElement.GetProperty("data")).First(d => d.TryGetProperty("kind", out var k) && k.GetString() == "response_metadata" && d.TryGetProperty("type", out var t) && t.GetString() == "response.output_text.delta" && d.GetProperty("messageId").ValueKind == JsonValueKind.String);
        var messageId = deltaRecord.GetProperty("messageId").GetString();
        var transfer = timingRows.Select(d => d.RootElement.GetProperty("data")).First(d => d.TryGetProperty("kind", out var k) && k.GetString() == "websocket_message" && d.GetProperty("messageId").GetString() == messageId);
        Check("Upstream delta arrival precedes forwarding completion and is not a local-send timestamp", deltaRecord.GetProperty("receivedTicks").GetInt64() == transfer.GetProperty("receivedTicks").GetInt64() && deltaRecord.GetProperty("receivedTicks").GetInt64() <= transfer.GetProperty("sentTicks").GetInt64() && deltaRecord.GetProperty("nonEmptyDelta").GetBoolean());
    }
    finally { foreach (var row in timingRows) row.Dispose(); }
    var fileA = state.NewEvents(); var fileB = state.NewEvents();
    await using (var firstRelay = await DiagnosticRelay.Start(fileA, new Uri(address + "/")))
    await using (var secondRelay = await DiagnosticRelay.Start(fileB, new Uri(address + "/")))
    {
        using var simultaneous = new HttpClient(); simultaneous.DefaultRequestHeaders.Authorization = new("Bearer", "fixture-multiple-processes");
        var replies = await Task.WhenAll(simultaneous.PostAsync(firstRelay.BaseUrl + "/responses/limited", new StringContent("{}")), simultaneous.PostAsync(secondRelay.BaseUrl + "/responses/limited", new StringContent("{}")));
        foreach (var reply in replies) reply.Dispose();
    }
    int RequestCount(string file) => File.ReadAllLines(file).Count(line => { using var doc = JsonDocument.Parse(line); return doc.RootElement.GetProperty("data").TryGetProperty("kind", out var kind) && kind.GetString() == "request"; });
    Check("Concurrent native relay instances retain separate complete event files", fileA != fileB && RequestCount(fileA) == 1 && RequestCount(fileB) == 1);
    await upstream.StopAsync();
    await NativeContracts.Run(root, Check);
    Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks }));
}
finally { Directory.Delete(root, true); }

internal sealed class ImmediateReplyStream(Action reply) : MemoryStream
{
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    { reply(); return base.WriteAsync(bytes, cancellationToken); }
}
