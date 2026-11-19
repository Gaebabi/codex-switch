using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexSwitch.Diagnostics;

// Observes original byte streams; never parses and reserializes the forwarded protocol.
internal sealed class ProtocolTelemetry(Action<object> emit)
{
    private readonly ConcurrentDictionary<string, (string Method, long Start)> _rpc = new();
    private readonly ConcurrentDictionary<string, long> _items = new(), _turns = new();
    private readonly ConcurrentDictionary<string, ToolClock> _toolClocks = new();
    private sealed class ToolClock
    {
        private readonly object _gate = new(); private int _active; private long _start; private double _total;
        internal void Begin(long now) { lock (_gate) if (_active++ == 0) _start = now; }
        internal void End(long now) { lock (_gate) if (_active > 0 && --_active == 0) _total += Stopwatch.GetElapsedTime(_start, now).TotalMilliseconds; }
        internal double Total(long now) { lock (_gate) return _total + (_active > 0 ? Stopwatch.GetElapsedTime(_start, now).TotalMilliseconds : 0); }
    }
    private readonly ConcurrentDictionary<string, string> _toolItems = new();
    private long _sequence;
    internal void Observe(string direction, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            static string? Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: <= 160 } text && text.All(c => char.IsAsciiLetterOrDigit(c) || "_./:-".Contains(c)) ? text : null;
            static JsonElement Object(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            var method = Text(root, "method");
            string? rpcId = null;
            if (root.TryGetProperty("id", out var id)) rpcId = id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.ValueKind == JsonValueKind.String ? Text(root, "id") : null;
            var now = Stopwatch.GetTimestamp(); double? rpcMs = null; var resolvedMethod = method;
            if (rpcId != null)
            {
                if (method != null) _rpc[direction + ":" + rpcId] = (method, now);
                else if (_rpc.TryRemove((direction == "client_to_native" ? "native_to_client" : "client_to_native") + ":" + rpcId, out var pending))
                { resolvedMethod = pending.Method; rpcMs = Stopwatch.GetElapsedTime(pending.Start, now).TotalMilliseconds; }
            }
            var parameters = Object(root, "params"); var result = Object(root, "result");
            var thread = Text(parameters, "threadId") ?? Text(Object(result, "thread"), "id");
            var turn = Text(parameters, "turnId") ?? Text(Object(parameters, "turn"), "id") ?? Text(Object(result, "turn"), "id");
            var item = Object(parameters, "item"); var itemId = Text(item, "id") ?? Text(parameters, "itemId");
            var nativeUsage = Object(parameters, "tokenUsage");
            object? Usage(JsonElement u)
            {
                if (u.ValueKind != JsonValueKind.Object) return null;
                long? Count(string key) => u.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var count) ? count : null;
                return new { inputTokens = Count("inputTokens"), cachedInputTokens = Count("cachedInputTokens"), outputTokens = Count("outputTokens"), reasoningOutputTokens = Count("reasoningOutputTokens"), totalTokens = Count("totalTokens") };
            }
            if (nativeUsage.ValueKind == JsonValueKind.Object)
                emit(new { kind = "native_token_usage", thread, turn, last = Usage(Object(nativeUsage, "last")), total = Usage(Object(nativeUsage, "total")) });
            var kind = Text(item, "type");
            var status = Text(item, "status") ?? Text(Object(parameters, "turn"), "status")
                ?? (method == "thread/status/changed" ? Text(Object(parameters, "status"), "type") : null);
            double? itemMs = null, turnMs = null;
            var itemKey = thread + ":" + turn + ":" + itemId;
            if (method == "item/started" && itemId != null) _items[itemKey] = now;
            if (method == "item/completed" && itemId != null && _items.TryRemove(itemKey, out var itemStart)) itemMs = Stopwatch.GetElapsedTime(itemStart, now).TotalMilliseconds;
            var turnKey = thread + ":" + turn;
            if (method == "item/started" && itemId != null && kind is "commandExecution" or "mcpToolCall" or "dynamicToolCall" or "webSearch" or "fileChange" or "imageView" or "imageGeneration" or "collabAgentToolCall")
            { _toolItems[itemKey] = turnKey; _toolClocks.GetOrAdd(turnKey, _ => new ToolClock()).Begin(now); }
            if (method == "item/completed" && _toolItems.TryRemove(itemKey, out var toolTurn) && _toolClocks.TryGetValue(toolTurn, out var clock)) clock.End(now);
            if (method == "turn/started" && turn != null) _turns[turnKey] = now;
            if (method == "turn/completed" && turn != null && _turns.TryRemove(turnKey, out var turnStart))
            {
                turnMs = Stopwatch.GetElapsedTime(turnStart, now).TotalMilliseconds;
                var toolMs = _toolClocks.TryRemove(turnKey, out var toolClock) ? toolClock.Total(now) : 0;
                emit(new { kind = "turn_summary", thread, turn, elapsedMs = turnMs, toolActiveUnionMs = toolMs,
                    outsideToolIntervalsMs = Math.Max(0, turnMs.Value - toolMs), residualMeaning = "Includes model, network, approval and unattributed local waits; not a measured idle or disk wait." });
                foreach (var key in _toolItems.Where(pair => pair.Value == turnKey).Select(pair => pair.Key)) { _toolItems.TryRemove(key, out _); _items.TryRemove(key, out _); }
            }
            // Suppress output deltas here: their content and high volume are irrelevant to RPC timings.
            if (method?.EndsWith("/delta", StringComparison.Ordinal) == true) return;
            emit(new { kind = "native_protocol", sequence = Interlocked.Increment(ref _sequence), direction, method = resolvedMethod, rpcId, thread, turn, itemId,
                itemType = kind, tool = Text(item, "tool") ?? Text(item, "name"), status,
                model = Text(parameters, "model") ?? Text(result, "model"), effort = Text(parameters, "effort") ?? Text(parameters, "reasoningEffort") ?? Text(Object(parameters, "config"), "model_reasoning_effort"),
                serviceTier = Text(parameters, "serviceTier") ?? Text(Object(parameters, "config"), "service_tier"),
                rpcMs, itemMs, turnMs, failed = root.TryGetProperty("error", out _) });
        }
        catch (JsonException) { emit(new { kind = "protocol_metadata_unavailable", direction, reason = "not_json" }); }
    }

    internal async Task Copy(Stream source, Stream destination, string direction, CancellationToken cancellation = default)
    {
        var buffer = new byte[16384]; using var line = new MemoryStream(); var oversized = false; int n;
        while ((n = await source.ReadAsync(buffer, cancellation)) != 0)
        {
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] == 10)
                {
                    if (!oversized && line.Length != 0) Observe(direction, line.ToArray());
                    else if (oversized) emit(new { kind = "protocol_metadata_unavailable", direction, reason = "line_over_512KiB" });
                    line.SetLength(0); oversized = false;
                }
                else if (!oversized && line.Length < 512 * 1024) line.WriteByte(buffer[i]); else oversized = true;
            }
            // Register a dispatch before publishing its final bytes: a local native reply may be immediate.
            var writeStart = Stopwatch.GetTimestamp();
            await destination.WriteAsync(buffer.AsMemory(0, n), cancellation); await destination.FlushAsync(cancellation);
            emit(new { kind = "protocol_pipe_write", direction, bytes = n, writeWaitMs = Stopwatch.GetElapsedTime(writeStart).TotalMilliseconds });
        }
    }
}
