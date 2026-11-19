using System.Text.Json;
using CodexAccountDesk;

internal static class StatisticsContracts
{
    internal static DiagnosticStatistics Fixture()
    {
        var statistics = new DiagnosticStatistics(); var epoch = DateTimeOffset.UtcNow.AddSeconds(-20);
        void Emit(double time, object data)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { at = epoch.AddMilliseconds(time), monotonicTicks = (long)(time * 1000), frequency = 1000000, data }));
            statistics.Apply("fixture", document.RootElement);
        }
        void Native(double time, string? chat, string? turn, string method, string? item = null, string? type = null, string? rpc = null, double? rpcMs = null, string? status = null) =>
            Emit(time, new { kind = "native_protocol", thread = chat, turn, method, itemId = item, itemType = type, rpcId = rpc, rpcMs, status });
        Native(-500, "chat-eof", "turn-eof", "turn/started");
        Emit(-490, new { kind = "request", id = "request-eof", clientRequestId = "chat-eof", transport = "http", method = "POST" });
        Emit(-300, new { kind = "headers", id = "request-eof", status = 200 });
        Emit(-240, new { kind = "response_metadata", id = "request-eof", type = "response.output_text.delta", nonEmptyDelta = true, receivedTicks = -240000L });
        Emit(-100, new { kind = "response_metadata", id = "request-eof", type = "response.completed", receivedTicks = -100000L });
        Emit(-90, new { kind = "transport_failure", id = "request-eof", error = "TaskCanceledException" });
        Emit(-85, new { kind = "request", id = "followup-eof", clientRequestId = "chat-eof", transport = "http", method = "POST" });
        Emit(-80, new { kind = "headers", id = "followup-eof", status = 200 });
        Emit(-70, new { kind = "response_metadata", id = "followup-eof", type = "response.output_text.delta", nonEmptyDelta = true, receivedTicks = -70000L });
        Emit(-40, new { kind = "response_metadata", id = "followup-eof", type = "response.completed", receivedTicks = -40000L });
        Native(-30, "chat-eof", "turn-eof", "turn/completed", status: "completed");
        Native(-24, "chat-idle", "turn-idle", "turn/started");
        Native(-23, "chat-idle", "turn-idle", "item/completed", "last", "agentMessage");
        Native(-10, "chat-idle", null, "thread/status/changed", status: "idle");
        Emit(-10, new { kind = "native_protocol", thread = "chat-a", method = "turn/start", direction = "client_to_native", rpcId = "fixture-start", effort = "high" });
        Native(0, "chat-a", "turn-a", "turn/started");
        Emit(5, new { kind = "native_protocol", turn = "turn-a", method = "turn/start", direction = "native_to_client", rpcId = "fixture-start" });
        Native(100, "chat-a", "history-only", "thread/items/list");
        Emit(500, new { kind = "request", id = "request-a", clientRequestId = "chat-a", transport = "websocket", method = "GET" });
        Emit(600, new { kind = "headers", id = "request-a", status = 101 });
        Emit(520, new { kind = "request", id = "fallback-a", clientRequestId = "chat-a", transport = "websocket", method = "GET" });
        Emit(545, new { kind = "headers", id = "fallback-a", status = 426, protocolFallback = true });
        Emit(800, new { kind = "request_metadata", id = "request-a", type = "response.create", model = "gpt-6.1-sol", serviceTier = "priority", receivedTicks = 800000L, sendStartedTicks = 700000L });
        Emit(1000, new { kind = "response_metadata", id = "request-a", type = "response.output_text.delta", responseId = "response-a", nonEmptyDelta = true, receivedTicks = 950000L });
        Emit(1800, new { kind = "response_metadata", id = "request-a", type = "response.output_text.delta", responseId = "response-a", nonEmptyDelta = true, receivedTicks = 1800000L });
        Native(2000, "chat-a", "turn-a", "item/started", "tool-a", "commandExecution");
        Native(4000, "chat-a", "turn-a", "item/started", "tool-b", "mcpToolCall");
        Native(4500, "chat-a", "turn-a", "item/commandExecution/requestApproval", rpc: "approval-a");
        Native(5000, "chat-a", "turn-a", "item/completed", "tool-a", "commandExecution");
        Native(5500, null, null, "item/commandExecution/requestApproval", rpc: "approval-a", rpcMs: 1000);
        Native(7000, "chat-a", "turn-a", "item/completed", "tool-b", "mcpToolCall");
        Emit(7900, new { kind = "response_metadata", id = "request-a", type = "response.output_text.delta", responseId = "response-a", nonEmptyDelta = true, receivedTicks = 7900000L });
        Emit(8000, new { kind = "response_metadata", id = "request-a", type = "response.completed", responseId = "response-a",
            model = "gpt-6.1-sol", serviceTier = "default", receivedTicks = 8000000L });
        var usage = new { kind = "native_token_usage", thread = "chat-a", turn = "turn-a", last = new { inputTokens = 100, outputTokens = 20, cachedInputTokens = 80, reasoningOutputTokens = 0 }, total = new { totalTokens = 120 } };
        Emit(8001, usage); Emit(8002, usage);
        Native(10000, "chat-a", "turn-a", "turn/completed", status: "completed");
        Native(11000, "chat-b", "turn-b", "turn/started");
        Emit(11100, new { kind = "request", id = "request-b", clientRequestId = "chat-b", transport = "websocket", method = "GET" });
        Emit(11200, new { kind = "headers", id = "request-b", status = 101 });
        Emit(11300, new { kind = "partial_metadata", id = "request-b", type = "response.create", model = "gpt-6-astra", serviceTier = "ultrafast" });
        Native(12000, "chat-a", "turn-retry", "turn/started");
        Emit(12100, new { kind = "request", id = "request-retry", clientRequestId = "chat-a", transport = "websocket", method = "GET" });
        Emit(12500, new { kind = "headers", id = "request-retry", status = 429 });
        Native(18000, "chat-a", "turn-retry", "turn/completed", status: "failed");
        Emit(19000, new { kind = "resource_sample" });
        return statistics;
    }

    internal static int Run()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var statistics = Fixture(); var chats = statistics.Snapshot(DateTimeOffset.UtcNow);
        var chat = chats.Single(c => c.Id == "chat-a"); var turn = chat.Turns.Single(t => t.Id == "turn-a");
        Check(turn.Effort == "high" && turn.Tier == "priority" && turn.ReturnedTier == "default" &&
            turn.Model == "gpt-6.1-sol" && turn.ReturnedModel == "gpt-6.1-sol", "Turn metadata follows the exact RPC and preserves both request and response tiers.");
        Check(Math.Abs(turn.ElapsedMs - 10000) < .1 && Math.Abs(turn.Shares.Total - 10000) < .1, "Partition covers the observed turn exactly.");
        Check(Math.Abs(turn.Shares.Tools - 4000) < .1 && Math.Abs(turn.Shares.Approval - 1000) < .1, "Overlapping tools and approval are not double counted.");
        Check(Math.Abs(turn.Shares.Model - 2400) < .1 && Math.Abs(turn.Shares.Other - 2600) < .1, "Request/other shares use dispatch spans and exclusive priorities.");
        Check(turn.Input == 100 && turn.Output == 20 && turn.Cached == 80, "Repeated cumulative token notifications do not duplicate tokens.");
        Check(Math.Abs(turn.Llm.Single().TtftMs!.Value - 250) < .1 && turn.Llm.Single().StreamTpsApprox.HasValue, "Arrival timing excludes downstream forwarding delay and a unique native usage receipt completes the call metric.");
        Check(turn.FirstTextMs is double firstText && Math.Abs(firstText - 950) < .1, "The turn TTFT rail starts at the turn, not the request.");
        Check(turn.Timeline.Select(p => p.Phase).SequenceEqual(new[]
        {
            DiagnosticPhase.Other, DiagnosticPhase.Model, DiagnosticPhase.Other, DiagnosticPhase.Model,
            DiagnosticPhase.Tool, DiagnosticPhase.Approval, DiagnosticPhase.Tool, DiagnosticPhase.Model, DiagnosticPhase.Other
        }), "Visible stripe segments follow event time, including repeated phases.");
        Check(turn.Tools.Select(t => (t.Name, t.Completed)).SequenceEqual(new[] { ("commandExecution", true), ("mcpToolCall", true) })
            && turn.Tools.All(t => Math.Abs(t.EndMs - t.StartMs - 3000) < .1), "Two overlapping tools retain their own measured elapsed times.");
        Check(chat.Turns.Single(t => t.Id == "turn-retry").Errors == 1, "429 is attributed only to the matching active chat.");
        Check(chat.Turns.All(t => t.Id != "history-only"), "Reading old metadata cannot create a running turn.");
        Check(turn.Errors == 0 && chats.Single(c => c.Id == "chat-eof").Turns.Single().Errors == 0,
            "Expected gateway negotiation and HTTP cancellation after response completion are not inference errors.");
        var calls = chats.Single(c => c.Id == "chat-eof").Turns.Single();
        Check(calls.FirstTextWindows.Any(w => Math.Abs(w.StartMs - 415) < .01 && Math.Abs(w.EndMs - 430) < .01),
            "A later model request has its own dispatch-to-first-text interval.");
        var idle = chats.Single(c => c.Id == "chat-idle").Turns.Single();
        Check(!idle.Running && Math.Abs(idle.ElapsedMs - 1) < .01, "An idle thread freezes at its last measured event.");
        Check(chat.Shares.Total >= 15999 && chat.Shares.Total <= 16001, "Chat totals sum turn time, not idle gaps.");
        Check(chats.Single(c => c.Id == "chat-b").Turns.Single().Running, "Active captures update live without manufacturing completion.");
        var stopped = statistics.Snapshot(DateTimeOffset.UtcNow.AddMinutes(1)).Single(c => c.Id == "chat-b").Turns.Single();
        Check(!stopped.Running && stopped.Partial && stopped.Status == "captureEnded", "An ended capture cannot accumulate idle time forever.");
        var clipped = DiagnosticStatistics.Partition(0, 100, new[] { (-30d, 200d, DiagnosticPhase.Model), (50d, 80d, DiagnosticPhase.Approval) }).Shares;
        Check(clipped.Total == 100 && clipped.Model == 70 && clipped.Approval == 30, "Out-of-turn spans are clipped.");
        Check(DiagnosticStatistics.Partition(2, 2, Array.Empty<(double, double, DiagnosticPhase)>()).Timeline.Count == 0, "A zero-length turn has no fabricated segment.");
        Check(stopped.FirstTextMs == null, "A legacy or partial capture has no fabricated TTFT rail.");
        var llm = new DiagnosticLlmTiming("call", 1000, true) { End = 6000, OutputTokens = 120, ReasoningTokens = 20 };
        llm.Delta(3000, true); llm.Delta(3500, true); llm.Delta(5000, true);
        Check(llm.TtftMs == 2000 && Math.Abs(llm.StreamTpsApprox!.Value - 100d / 3) < .001 && llm.EndToEndTps == 20, "TTFT and delivery TPS use their explicit timing windows and exclude reasoning tokens.");
        Check(llm.MaxGapMs == 1500 && llm.P95GapMs == 1500, "Inter-chunk waits are measured separately from token counts.");
        var single = new DiagnosticLlmTiming("single", 0, true) { End = 2001, OutputTokens = 10, ReasoningTokens = 0 }; single.Delta(2000, true);
        Check(single.StreamTpsApprox == null && single.EndToEndTps.HasValue, "A single token batch cannot produce a fake high stream TPS.");
        var frozen = llm.Snapshot(); llm.Ambiguous = true;
        Check(llm.TtftMs == null && llm.StreamTpsApprox == null && frozen.TtftMs == 2000, "Ambiguous timing remains unavailable and UI snapshots cannot race the collector.");
        return count;
    }
}
