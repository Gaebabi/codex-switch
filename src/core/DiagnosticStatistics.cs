using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record DiagnosticShares(double Model, double Tools, double Approval, double Other)
{
    internal double Total => Model + Tools + Approval + Other;
    internal static DiagnosticShares Sum(IEnumerable<DiagnosticShares> values) => values.Aggregate(new DiagnosticShares(0, 0, 0, 0), (a, b) => new(a.Model + b.Model, a.Tools + b.Tools, a.Approval + b.Approval, a.Other + b.Other));
}
internal enum DiagnosticPhase { Other, Model, Tool, Approval, FirstText }
internal sealed record DiagnosticInterval(double StartMs, double EndMs, DiagnosticPhase Phase);
internal sealed record DiagnosticTool(double StartMs, double EndMs, string Name, bool Completed);
internal sealed record DiagnosticTurn(string Key, string Thread, string Id, DateTimeOffset Started, double ElapsedMs, DiagnosticShares Shares,
    IReadOnlyList<DiagnosticInterval> Timeline, IReadOnlyList<DiagnosticTool> Tools, double? FirstTextMs, IReadOnlyList<DiagnosticInterval> FirstTextWindows,
    bool Running, bool Partial, string Status, string? Model, string? Tier, string? Effort, string? ReturnedModel, string? ReturnedTier,
    int Calls, int Errors, long Input, long Output, long Cached, IReadOnlyList<DiagnosticLlmTiming> Llm)
{
    internal double LiveRemainingMs { get; init; }
}
internal sealed record DiagnosticChat(string Id, IReadOnlyList<DiagnosticTurn> Turns, DiagnosticShares Shares);

internal sealed class DiagnosticStatistics
{
    private sealed class Turn(string key, string source, string thread, string id, double start)
    {
        internal string Key = key, Thread = thread, Id = id, Status = "inProgress";
        internal readonly string Source = source;
        internal double Start = start, Last = start; internal double? End;
        internal bool SawStart, Partial;
        internal string? Model, Tier, Effort, ReturnedModel, ReturnedTier;
        internal int Calls, Errors;
        internal long Input, Output, Cached;
        internal readonly List<Span> Spans = new();
        internal readonly Dictionary<string, Tool> Tools = new();
        internal readonly List<DiagnosticLlmTiming> Llm = new();
    }
    private sealed record Span(double Start, double End, DiagnosticPhase Phase);
    private sealed record Tool(double Start, string Name, double? End = null);
    private sealed class Request(string source, string? candidate, double connected, bool http)
    {
        internal string Source = source; internal string? Candidate = candidate, TurnKey;
        internal double? Start; internal int Active;
        internal double Connected = connected; internal bool Failed, CompletedResponse;
        internal double? Dispatched;
        internal bool Http = http, HttpStarted, Counted;
        internal DiagnosticLlmTiming? Timing;
    }
    private readonly Dictionary<string, Turn> _turns = new();
    private readonly Dictionary<string, string> _active = new();
    private readonly Dictionary<string, Request> _requests = new();
    private readonly Dictionary<string, (string Turn, double Start)> _approvals = new();
    private readonly Dictionary<string, (string Thread, string? Model, string? Effort, string? Tier)> _turnStarts = new();
    private readonly Dictionary<string, long> _tokenTotals = new();
    private readonly Dictionary<string, (double Mono, double Utc)> _clocks = new();
    private readonly HashSet<string> _knownThreads = new();
    private readonly Dictionary<string, double> _lastSeen = new();
    internal DateTimeOffset? LastHeartbeat { get; private set; }
    internal long Dropped { get; private set; }

    private static string? Text(JsonElement v, string key) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static double? Number(JsonElement v, string key) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    private static JsonElement Object(JsonElement v, string key) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
    private static double UtcMs(DateTimeOffset time) => (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10000d;
    private string ActiveKey(string source, string thread) => source + ":" + thread;
    private Turn Get(string source, string thread, string id, double time)
    {
        var key = source + ":" + thread + ":" + id;
        if (!_turns.TryGetValue(key, out var turn)) _turns[key] = turn = new(key, source, thread, id, time);
        turn.Last = Math.Max(turn.Last, time); _knownThreads.Add(thread); return turn;
    }

    internal void Apply(string source, JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("at", out var date) || date.ValueKind != JsonValueKind.String || !date.TryGetDateTimeOffset(out var at)) return;
        var time = UtcMs(at); var ticks = Number(envelope, "monotonicTicks"); var frequency = Number(envelope, "frequency");
        if (ticks.HasValue && frequency is > 0)
        {
            var mono = ticks.Value / frequency.Value * 1000;
            if (!_clocks.TryGetValue(source, out var clock)) _clocks[source] = clock = (mono, time);
            time = clock.Utc + mono - clock.Mono;
        }
        Dropped = Math.Max(Dropped, (long)(Number(envelope, "dropped") ?? 0));
        _lastSeen[source] = Math.Max(_lastSeen.GetValueOrDefault(source), time);
        var data = Object(envelope, "data"); var kind = Text(data, "kind");
        double SampleTime(string name) => Number(data, name) is double sample && frequency is > 0 && _clocks.TryGetValue(source, out var origin) ? origin.Utc + sample / frequency.Value * 1000 - origin.Mono : time;
        if (kind == "resource_sample") { if (!LastHeartbeat.HasValue || at > LastHeartbeat) LastHeartbeat = at; return; }
        var thread = Text(data, "thread"); var id = Text(data, "turn");
        if (thread != null) _knownThreads.Add(thread);
        if (kind == "native_protocol" && Text(data, "method") == "turn/start" && Text(data, "rpcId") is string startId)
        {
            var key = source + ":" + startId;
            if (Text(data, "direction") == "client_to_native" && thread != null)
            {
                if (_turnStarts.Count >= 256) _turnStarts.Remove(_turnStarts.Keys.First());
                _turnStarts[key] = (thread, Text(data, "model"), Text(data, "effort"), Text(data, "serviceTier"));
            }
            else if (Text(data, "direction") == "native_to_client" && _turnStarts.Remove(key, out var start) && id != null)
            {
                var turn = Get(source, start.Thread, id, time);
                turn.Model ??= start.Model; turn.Effort ??= start.Effort; turn.Tier ??= start.Tier;
            }
        }
        var recordedMethod = Text(data, "method");
        if (kind == "native_protocol" && thread != null &&
            (recordedMethod == "thread/closed" || recordedMethod == "thread/status/changed" && Text(data, "status") is ("idle" or "systemError" or "notLoaded")) &&
            _active.Remove(ActiveKey(source, thread), out var closing) && _turns[closing].End == null)
        {
            var ended = _turns[closing];
            ended.End = ended.Last; ended.Status = "captureEnded";
        }
        if (kind == "request" && Text(data, "id") is string requestId)
        {
            var request = new Request(source, thread ?? Text(data, "clientRequestId"), time, Text(data, "transport") == "http" && Text(data, "method") == "POST");
            if (request.Candidate != null && _active.TryGetValue(ActiveKey(source, request.Candidate), out var currentKey)) request.TurnKey = currentKey;
            _requests[source + ":" + requestId] = request; return;
        }
        if (kind == "request_dispatched" && Text(data, "id") is string dispatched && _requests.TryGetValue(source + ":" + dispatched, out var sent)) { sent.Dispatched = time; return; }
        if (kind is "request_metadata" or "partial_metadata" or "response_metadata" or "headers" or "transport_failure" or "completed")
        {
            if (Text(data, "id") is not string request || !_requests.TryGetValue(source + ":" + request, out var state)) return;
            var type = Text(data, "type");
            if (state.TurnKey == null && state.Candidate != null && _knownThreads.Contains(state.Candidate) && _active.TryGetValue(ActiveKey(source, state.Candidate), out var initialTurn)) state.TurnKey = initialTurn;
            if (kind == "headers" && state.TurnKey != null && _turns.TryGetValue(state.TurnKey, out var setup)) setup.Spans.Add(new(state.Connected, time, DiagnosticPhase.Model));
            if (state.Http && !state.HttpStarted && state.TurnKey != null)
            {
                state.HttpStarted = true; state.Start = state.Connected; state.Active = 1; _turns[state.TurnKey].Calls++; state.Counted = true;
                state.Timing = new(request + ":http", state.Dispatched ?? state.Connected, true); _turns[state.TurnKey].Llm.Add(state.Timing);
            }
            if (type == "response.create" && kind is "request_metadata" or "partial_metadata")
            {
                if (state.Candidate == null || !_knownThreads.Contains(state.Candidate) || !_active.TryGetValue(ActiveKey(source, state.Candidate), out var active)) return;
                var turn = _turns[active]; turn.Calls++; state.Counted = true;
                if (state.Active == 0) { state.Start = SampleTime("sendStartedTicks"); state.TurnKey = active; }
                else if (state.TurnKey != active) { turn.Partial = true; return; }
                state.Active++; state.Failed = false;
                var timing = new DiagnosticLlmTiming(request + ":" + turn.Calls, SampleTime("sendStartedTicks"), Number(data, "receivedTicks") != null);
                if (state.Active > 1) { timing.Ambiguous = true; if (state.Timing != null) state.Timing.Ambiguous = true; }
                state.Timing = timing; turn.Llm.Add(timing);
            }
            if (state.TurnKey != null && _turns.TryGetValue(state.TurnKey, out var owner))
            {
                if (kind == "request_metadata" || kind == "partial_metadata" && Text(data, "direction") == "request_metadata")
                {
                    owner.Model = Text(data, "model") ?? owner.Model;
                    owner.Tier = Text(data, "serviceTier") ?? owner.Tier;
                    owner.Effort = Text(data, "effort") ?? owner.Effort;
                }
                if (type == "response.completed" && kind is ("response_metadata" or "partial_metadata"))
                {
                    owner.ReturnedModel = Text(data, "model") ?? owner.ReturnedModel;
                    owner.ReturnedTier = Text(data, "serviceTier") ?? owner.ReturnedTier;
                }
                if (state.Timing is { } timing && (kind == "response_metadata" || kind == "partial_metadata" && Text(data, "direction") == "response_metadata"))
                {
                    var received = SampleTime("receivedTicks");
                    timing.Precise &= Number(data, "receivedTicks") != null;
                    var responseId = Text(data, "responseId");
                    if (timing.Response != null && responseId != null && timing.Response != responseId) timing.Ambiguous = true;
                    timing.Response ??= responseId;
                    if (kind == "partial_metadata" && (type == null || type.EndsWith(".delta", StringComparison.Ordinal))) timing.Ambiguous = true;
                    if (data.TryGetProperty("nonEmptyDelta", out var nonempty) && nonempty.ValueKind == JsonValueKind.True && type is "response.output_text.delta" or "response.refusal.delta" or "response.function_call_arguments.delta" or "response.custom_tool_call_input.delta")
                        timing.Delta(received, type is "response.output_text.delta" or "response.refusal.delta");
                    if (type is "response.completed" or "response.failed" or "response.incomplete" or "error") timing.End = received;
                    var usage = Object(data, "usage");
                    timing.OutputTokens = (long?)Number(usage, "outputTokens") ?? timing.OutputTokens;
                    timing.ReasoningTokens = (long?)Number(usage, "reasoningOutputTokens") ?? timing.ReasoningTokens;
                }
                if (type == "response.completed") state.CompletedResponse = true;
                if (type is "response.completed" or "response.failed" or "error") CloseRequest(state, owner, time, false);
                if (kind == "completed" && state.Http) CloseRequest(state, owner, time, true);
                var expectedFallback = kind == "headers" && data.TryGetProperty("protocolFallback", out var fallback) && fallback.ValueKind == JsonValueKind.True;
                var readCanceled = kind == "transport_failure" && state.Http && state.CompletedResponse &&
                    Text(data, "error") is "TaskCanceledException" or "OperationCanceledException";
                if (!state.Failed && !expectedFallback && !readCanceled && (type is "response.failed" or "error" || (Number(data, "status") is >= 400) || kind == "transport_failure"))
                { owner.Errors++; state.Failed = true; if (!state.Counted) { owner.Calls++; state.Counted = true; } }
            }
            return;
        }
        if (thread == null || id == null)
        {
            var replyMethod = Text(data, "method");
            if (kind == "native_protocol" && replyMethod != null && (replyMethod.Contains("requestApproval", StringComparison.Ordinal) || replyMethod.Contains("requestUserInput", StringComparison.Ordinal)) && Text(data, "rpcId") is string rpc && Number(data, "rpcMs") != null &&
                _approvals.Remove(source + ":" + rpc, out var wait) && _turns.TryGetValue(wait.Turn, out var waiting))
                waiting.Spans.Add(new(wait.Start, time, DiagnosticPhase.Approval));
            return;
        }
        if (kind is not ("native_protocol" or "turn_summary" or "native_token_usage")) return;
        var method = Text(data, "method");
        if (kind == "native_protocol" && method is not ("turn/started" or "turn/completed" or "item/started" or "item/completed" or "error") &&
            !(method?.Contains("requestApproval", StringComparison.Ordinal) == true || method?.Contains("requestUserInput", StringComparison.Ordinal) == true))
            return;
        var current = Get(source, thread, id, time);
        if (kind == "native_token_usage")
        {
            var total = Number(Object(data, "total"), "totalTokens"); var tokenKey = ActiveKey(source, thread);
            if (total.HasValue && _tokenTotals.TryGetValue(tokenKey, out var previous) && previous == (long)total.Value) return;
            if (total.HasValue) _tokenTotals[tokenKey] = (long)total.Value;
            var last = Object(data, "last"); current.Input += (long)(Number(last, "inputTokens") ?? 0);
            current.Output += (long)(Number(last, "outputTokens") ?? 0); current.Cached += (long)(Number(last, "cachedInputTokens") ?? 0);
            var missing = current.Llm.Where(c => c.End.HasValue && !c.OutputTokens.HasValue && !c.Ambiguous).ToArray();
            if (missing.Length == 1) { missing[0].OutputTokens = (long?)Number(last, "outputTokens"); missing[0].ReasoningTokens = (long?)Number(last, "reasoningOutputTokens"); }
            foreach (var request in _requests.Values.Where(r => r.TurnKey == current.Key && r.Active > 0)) CloseRequest(request, current, time, true);
            return;
        }
        if (kind == "turn_summary" && Number(data, "elapsedMs") is double elapsed)
        {
            if (!current.SawStart) { current.Start = time - elapsed; current.Partial = true; }
            current.End = time; return;
        }
        var item = Text(data, "itemId");
        if (method == "turn/started")
        {
            if (_active.TryGetValue(ActiveKey(source, thread), out var previousTurn) && previousTurn != current.Key && _turns[previousTurn].End == null)
            { _turns[previousTurn].End = time; _turns[previousTurn].Partial = true; _turns[previousTurn].Status = "interrupted"; }
            current.Start = time; current.SawStart = true; _active[ActiveKey(source, thread)] = current.Key;
        }
        current.Model = Text(data, "model") ?? current.Model;
        current.Effort = Text(data, "effort") ?? current.Effort;
        var requested = Text(data, "serviceTier"); if (requested != null && requested != "auto") current.Tier = requested;
        var itemType = Text(data, "itemType");
        var tool = itemType is "commandExecution" or "mcpToolCall" or "dynamicToolCall" or "webSearch" or "fileChange" or "imageView" or "imageGeneration" or "collabAgentToolCall";
        if (item != null && method == "item/started" && tool)
            current.Tools.TryAdd(item, new(time, Text(data, "tool") ?? itemType!));
        if (item != null && method == "item/completed")
        {
            var name = Text(data, "tool");
            if (current.Tools.TryGetValue(item, out var observed))
            {
                if (observed.End == null) current.Tools[item] = observed with { End = time, Name = name ?? observed.Name };
            }
            else if (tool && Number(data, "itemMs") is double duration && duration > 0)
                current.Tools[item] = new(time - duration, name ?? itemType!, time);
        }
        if (method != null && (method.Contains("requestApproval", StringComparison.Ordinal) || method.Contains("requestUserInput", StringComparison.Ordinal)) && Text(data, "rpcId") is string approval && Number(data, "rpcMs") == null)
            _approvals[source + ":" + approval] = (current.Key, time);
        if (method == "turn/completed")
        {
            current.End = time; current.Status = Text(data, "status") ?? "completed";
            if (_active.GetValueOrDefault(ActiveKey(source, thread)) == current.Key) _active.Remove(ActiveKey(source, thread));
            foreach (var request in _requests.Values.Where(r => r.TurnKey == current.Key && r.Active > 0)) { current.Partial = true; request.Active = 0; request.Start = null; }
        }
    }

    private static void CloseRequest(Request request, Turn turn, double time, bool finishAll)
    {
        if (request.Active <= 0) return;
        request.Active = finishAll ? 0 : request.Active - 1;
        if (request.Active == 0 && request.Start.HasValue) { turn.Spans.Add(new(request.Start.Value, time, DiagnosticPhase.Model)); request.Start = null; }
    }

    // Sweep endpoints once: approval > tool > model/request; every remaining interval is unassigned.
    internal static (DiagnosticShares Shares, IReadOnlyList<DiagnosticInterval> Timeline) Partition(double start, double end, IEnumerable<(double Start, double End, DiagnosticPhase Phase)> spans)
    {
        if (end <= start) return (new(0, 0, 0, 0), Array.Empty<DiagnosticInterval>());
        var points = new List<(double At, DiagnosticPhase Phase, int Delta)> { (start, DiagnosticPhase.Other, 0), (end, DiagnosticPhase.Other, 0) };
        foreach (var span in spans)
        {
            var a = Math.Max(start, span.Start); var b = Math.Min(end, span.End);
            if (a < b) { points.Add((a, span.Phase, 1)); points.Add((b, span.Phase, -1)); }
        }
        points.Sort((a, b) => a.At.CompareTo(b.At)); var active = new int[4]; var totals = new double[4]; var previous = start;
        var timeline = new List<DiagnosticInterval>();
        foreach (var group in points.GroupBy(p => p.At))
        {
            var category = active[3] > 0 ? 3 : active[2] > 0 ? 2 : active[1] > 0 ? 1 : 0;
            if (group.Key > previous)
            {
                var phase = (DiagnosticPhase)category;
                var offset = group.Key - start;
                if (timeline.Count > 0 && timeline[^1].Phase == phase)
                    timeline[^1] = timeline[^1] with { EndMs = offset };
                else
                    timeline.Add(new(previous - start, offset, phase));
                totals[category] += group.Key - previous;
            }
            foreach (var point in group) active[(int)point.Phase] += point.Delta;
            previous = group.Key;
        }
        return (new(totals[1], totals[2], totals[3], totals[0]), timeline);
    }

    internal IReadOnlyList<DiagnosticChat> Snapshot(DateTimeOffset now, IReadOnlySet<string>? readySources = null)
    {
        var result = new List<DiagnosticTurn>();
        var activeRequests = _requests.Values.Where(r => r.TurnKey != null && r.Active > 0 && r.Start.HasValue).ToLookup(r => r.TurnKey!);
        var approvals = _approvals.Values.ToLookup(w => w.Turn);
        foreach (var turn in _turns.Values)
        {
            if (readySources != null && !readySources.Contains(turn.Source)) continue;
            var sourceSeen = _lastSeen.GetValueOrDefault(turn.Source, turn.Last);
            var live = turn.End == null && turn.SawStart && _active.GetValueOrDefault(ActiveKey(turn.Source, turn.Thread)) == turn.Key &&
                UtcMs(now) - sourceSeen <= 5000;
            var end = turn.End ?? Math.Max(turn.Last, live ? UtcMs(now) : turn.Last);
            var spans = turn.Spans.Select(s => (s.Start, s.End, s.Phase)).ToList();
            spans.AddRange(turn.Tools.Values.Select(s => (s.Start, s.End ?? end, DiagnosticPhase.Tool)));
            spans.AddRange(activeRequests[turn.Key].Select(r => (r.Start!.Value, end, DiagnosticPhase.Model)));
            spans.AddRange(approvals[turn.Key].Select(w => (w.Start, end, DiagnosticPhase.Approval)));
            var (shares, timeline) = Partition(turn.Start, end, spans);
            var tools = turn.Tools.Values
                .Select(s => new DiagnosticTool(Math.Max(s.Start, turn.Start) - turn.Start, Math.Min(s.End ?? end, end) - turn.Start, s.Name,
                    s.End.HasValue && s.Start >= turn.Start && s.End <= end))
                .Where(s => s.EndMs > s.StartMs).OrderBy(s => s.StartMs).ToArray();
            var firstText = turn.SawStart
                ? turn.Llm.Where(c => c.TtftMs.HasValue && c.FirstText >= turn.Start && c.FirstText <= end).Select(c => c.FirstText - turn.Start).Min()
                : null;
            var windows = turn.Llm.Where(c => c.TtftMs.HasValue)
                .Select(c => new DiagnosticInterval(Math.Max(c.Start, turn.Start) - turn.Start,
                    Math.Min(c.FirstText!.Value, end) - turn.Start, DiagnosticPhase.FirstText)).ToList();
            if (firstText is double first) windows.Add(new(0, first, DiagnosticPhase.FirstText));
            if (live)
            {
                windows.AddRange(activeRequests[turn.Key]
                    .Where(r => r.Timing is { Precise: true, Ambiguous: false, FirstText: null })
                    .Select(r => new DiagnosticInterval(Math.Max(r.Timing!.Start, turn.Start) - turn.Start, end - turn.Start, DiagnosticPhase.FirstText)));
            }
            result.Add(new(turn.Key, turn.Thread, turn.Id, DateTimeOffset.UnixEpoch.AddMilliseconds(turn.Start), Math.Max(0, end - turn.Start), shares, timeline, tools, firstText,
                windows.Where(window => window.EndMs > window.StartMs).OrderBy(window => window.StartMs).ToArray(),
                live, turn.Partial || !turn.SawStart || turn.Status == "captureEnded" || turn.End == null && !live,
                turn.End == null && !live ? "captureEnded" : turn.Status,
                turn.Model, turn.Tier, turn.Effort, turn.ReturnedModel, turn.ReturnedTier, turn.Calls, turn.Errors, turn.Input, turn.Output, turn.Cached, turn.Llm.Select(c => c.Snapshot()).ToArray())
                { LiveRemainingMs = live ? Math.Max(0, 5000 - (UtcMs(now) - sourceSeen)) : 0 });
        }
        return result.GroupBy(t => t.Thread).Select(group => new DiagnosticChat(group.Key, group.OrderByDescending(t => t.Started).ToArray(), DiagnosticShares.Sum(group.Select(t => t.Shares)))).OrderByDescending(c => c.Turns[0].Started).ToArray();
    }
}
