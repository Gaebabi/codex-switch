using CodexAccountDesk;
using System.Text;
using System.Text.Json;

internal static class StreamRecoveryContracts
{
    internal static async Task Run(Action<string,bool> check)
    {
        const string failure = "stream disconnected before completion: An error occurred while processing your request. Please include the request ID fixture.";
        foreach (var status in new[] { "active", "idle", "completed", "interrupted", "inProgress", "unknown" })
            check("Only a persisted failed terminal status permits recovery: " + status, !StreamRecoveryPolicy.Failed(status));
        var routes = new ModelProviderSettings();
        check("First-request failure does not require fabricated successful-route provenance", StreamRecoveryPolicy.RegisteredModel("fixture-openai", routes) && routes.ConversationRoutes.Count == 0);
        check("A removed custom model cannot be continued to a fallback provider", !StreamRecoveryPolicy.RegisteredModel("cs_removed_model", routes));
        var id = Guid.NewGuid().ToString();
        string Complete(string? error) => JsonSerializer.Serialize(new { type = "event_msg", timestamp = DateTimeOffset.UtcNow,
            payload = new { type = "task_complete", turn_id = id, error = error == null ? null : new { message = error } } }) + "\n";
        var tail = DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Complete(failure)), false, "fixture-model", "codex_switch_router");
        check("Actual rollout completion parser retains failed turn identity and model", tail?.Id == id && tail.Status == "failed" && tail.Error == failure && tail.Model == "fixture-model");
        check("A persisted successful completion has no stream error", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Complete(null)), false, "m", "p")?.Status == "completed");
        string Terminal(string status, object? error) => JsonSerializer.Serialize(new { type = "event_msg", timestamp = DateTimeOffset.UtcNow,
            payload = new { type = "task_complete", turn_id = id, status, error } }) + "\n";
        check("Failure without error prose remains failed", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Terminal("failed", null)), false, "m", "p")?.Status == "failed");
        check("An error object without a message remains failed", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Terminal("failed", new { code = "server_error" })), false, "m", "p")?.Status == "failed");
        check("Actual interrupted status wins over attached error prose", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Terminal("interrupted", new { message = failure })), false, "m", "p")?.Status == "interrupted");
        check("Unknown terminal status cannot imply completion", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Terminal("unknown", null)), false, "m", "p") == null);
        var next = JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "task_started", turn_id = Guid.NewGuid().ToString() } }) + "\n";
        check("A newer started turn invalidates historical failure before any continuation", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Complete(failure) + next), false, "m", "p") == null);
        check("A newer user abort invalidates historical failure", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes(Complete(failure) + "{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}\n"), false, "m", "p") == null);
        check("Incomplete appended JSON is not interpreted as another failure", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes("{\"type\":\"event_msg\""), false, "m", "p") == null);
        check("Tail starts in the middle of an image/tool line without parsing that item", DesktopTurnOutcomes.ParseTail(Encoding.UTF8.GetBytes("partial-non-json\n" + Complete(failure)), true, "m", "p")?.Id == id);

        StreamRecoveryObservation Observation(string? turn=null) => new(Guid.NewGuid().ToString(), "owner", turn ?? Guid.NewGuid().ToString(), "failed", failure,
            "fixture-model", "codex_switch_router", "identity-hash", "route-hash", DateTimeOffset.UtcNow, true);
        foreach (var error in new string?[] { failure, "unexpected status 401 Unauthorized", "HTTP 403", "HTTP 413 Payload Too Large",
            "HTTP 429", "insufficient quota", "context limit", "provider-encrypted compaction", "HTTP 507", "switch_upstream_recovery_stopped",
            "stream disconnected before completion: The selected upstream did not return the requested event stream after HTTP 507 recovery.",
            "A bounded JSON Responses request is required.", "upstream interrupted its response", "connection closed", null })
        {
            var candidate = Observation() with { Error = error }; var data = new Dictionary<string,StreamRecoveryReceipt>(); var sent = 0;
            var recovery = new StreamRecoveryCoordinator(data, () => {}, (_,_) => Task.CompletedTask);
            var decision = await recovery.CheckAsync(candidate, _ => Task.FromResult<StreamRecoveryObservation?>(candidate), (_,_) => { sent++; return Task.FromResult(Guid.NewGuid().ToString()); }, CancellationToken.None);
            check("Every confirmed error family gets one identified continuation: " + (error ?? "missing error message"), sent == 1 && decision.Reason == "continued" && decision.Attempts == 1);
        }
        var receipts = new Dictionary<string,StreamRecoveryReceipt>(); var saved = 0; var sends = 0; var delays = new List<int>();
        var coordinator = new StreamRecoveryCoordinator(receipts, () => saved++, (attempt, _) => { delays.Add(attempt); return Task.CompletedTask; });
        var observed = Observation();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var oldSends = sends; var nextTurn = Guid.NewGuid().ToString();
            await coordinator.CheckAsync(observed, _ => Task.FromResult<StreamRecoveryObservation?>(observed), (message, _) =>
            {
                check("Receipt persists before send with a stable message identity: " + attempt, saved > 0 && receipts[observed.Thread].Sending && receipts[observed.Thread].Message == message && receipts[observed.Thread].Attempts == attempt);
                sends++; return Task.FromResult(nextTurn);
            }, CancellationToken.None);
            check("Consecutive failed continuations share one three-attempt budget: " + attempt, sends == oldSends + (attempt <= 3 ? 1 : 0));
            if (attempt <= 3) observed = observed with { Turn = nextTurn };
        }
        check("Automatic continuation budget is exactly three and backoff is bounded", sends == 3 && delays.SequenceEqual(new[] {1,2,3}));
        var reloaded = JsonSerializer.Deserialize<Dictionary<string,StreamRecoveryReceipt>>(JsonSerializer.Serialize(receipts))!;
        coordinator = new StreamRecoveryCoordinator(reloaded, () => saved++, (_,_) => Task.CompletedTask);
        await coordinator.CheckAsync(observed, _ => Task.FromResult<StreamRecoveryObservation?>(observed), (_,_) => { sends++; return Task.FromResult(Guid.NewGuid().ToString()); }, CancellationToken.None);
        check("Restart cannot reset an exhausted stream-recovery budget", sends == 3);
        await coordinator.CheckAsync(observed with { Status = "completed", Error = null }, _ => throw new Exception(), (_,_) => throw new Exception(), CancellationToken.None);
        observed = observed with { Turn = Guid.NewGuid().ToString() };
        await coordinator.CheckAsync(observed, _ => Task.FromResult<StreamRecoveryObservation?>(observed), (_,_) => { sends++; return Task.FromResult(Guid.NewGuid().ToString()); }, CancellationToken.None);
        check("Confirmed success permits a new independent failure budget", sends == 4 && reloaded[observed.Thread].Attempts == 1);
        foreach (var change in new Func<StreamRecoveryObservation,StreamRecoveryObservation>[] {
            o=>o with { Turn = Guid.NewGuid().ToString() }, o=>o with { Status="active" }, o=>o with { Ready=false },
            o=>o with { Owner="another" }, o=>o with { Model="another" }, o=>o with { Provider="another" },
            o=>o with { Identity="another" }, o=>o with { Route="another" }, o=>o with { Status="interrupted" }, o=>o with { Status="completed" } })
        {
            var data = new Dictionary<string,StreamRecoveryReceipt>(); var candidate = Observation(); var called = false;
            var recovery = new StreamRecoveryCoordinator(data, ()=>{}, (_,_)=>Task.CompletedTask);
            await recovery.CheckAsync(candidate, _=>Task.FromResult<StreamRecoveryObservation?>(change(candidate)), (_,_)=>{called=true;return Task.FromResult(Guid.NewGuid().ToString());},CancellationToken.None);
            check("Changed owner/turn/model/account/queue/runtime state prevents automatic send", !called && data.Count == 0);
        }
        {
            var data = new Dictionary<string,StreamRecoveryReceipt>(); var candidate = Observation(); var count = 0;
            var recovery = new StreamRecoveryCoordinator(data, ()=>{}, (_,_)=>Task.CompletedTask);
            try { await recovery.CheckAsync(candidate,_=>Task.FromResult<StreamRecoveryObservation?>(candidate),(_,_)=>{count++;throw new IOException("Lost IPC response");},CancellationToken.None); }
            catch(IOException) { }
            var loaded = JsonSerializer.Deserialize<Dictionary<string,StreamRecoveryReceipt>>(JsonSerializer.Serialize(data))!;
            recovery = new StreamRecoveryCoordinator(loaded,()=>{},(_,_)=>Task.CompletedTask);
            await recovery.CheckAsync(candidate,_=>Task.FromResult<StreamRecoveryObservation?>(candidate),(_,_)=>{count++;return Task.FromResult(Guid.NewGuid().ToString());},CancellationToken.None);
            check("Ambiguous continuation is persisted and never resent on restart", count==1 && loaded[candidate.Thread].Sending);
        }
        {
            var data = new Dictionary<string,StreamRecoveryReceipt>(); var candidate=Observation();var called=false;
            var recovery = new StreamRecoveryCoordinator(data,()=>throw new IOException("Disk write failed"),(_,_)=>Task.CompletedTask);
            try { await recovery.CheckAsync(candidate,_=>Task.FromResult<StreamRecoveryObservation?>(candidate),(_,_)=>{called=true;return Task.FromResult(Guid.NewGuid().ToString());},CancellationToken.None); } catch(IOException) { }
            check("Persistence failure prevents continuation submission",!called);
            using var stop=new CancellationTokenSource();stop.Cancel();
            recovery=new StreamRecoveryCoordinator(new(),()=>{},(_,token)=>Task.Delay(100,token));
            try { await recovery.CheckAsync(candidate,_=>Task.FromResult<StreamRecoveryObservation?>(candidate),(_,_)=>{called=true;return Task.FromResult(Guid.NewGuid().ToString());},stop.Token); }catch(OperationCanceledException) { }
            check("Cancellation during backoff never submits a continuation",!called);
        }
        var parameters=JsonSerializer.SerializeToElement(DesktopStreamRecoveryMonitor.StartParameters("fixture-thread","fixture-message"));
        check("Automatic continuation inherits existing settings and uses one identified Continue",parameters.GetProperty("turnStart").GetProperty("context").GetProperty("inheritThreadSettings").GetBoolean() &&
            parameters.GetProperty("turnStart").GetProperty("request").GetProperty("clientUserMessageId").GetString()=="fixture-message" &&
            parameters.GetProperty("turnStart").GetProperty("request").GetProperty("input")[0].GetProperty("text").GetString()=="Continue");
    }
}
