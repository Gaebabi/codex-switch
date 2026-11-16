using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

internal static class DesktopObservationContracts
{
    private const string Thread = "00000000-0000-4000-8000-000000000013";
    private const string Owner = "fictional-owner";
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop observation contract failed."); }
    private static object Snapshot(string status = "active") => new
    {
        type = "snapshot", revision = 1,
        conversationState = new
        {
            parentThreadId = (string?)null, resumeState = "resumed", threadGoal = (object?)null,
            threadRuntimeStatus = new { type = status, activeFlags = Array.Empty<string>() },
            turns = new[] { new { turnId = "fictional-turn", status = "inProgress" } },
            turnHistory = new { }, threadGoalResumeConfirmation = (object?)null,
            environmentSelectionEvidence = (object?)null
        }
    };
    private static object Stream(int version, object change, string owner = Owner) => new
    {
        type = "broadcast", method = "thread-stream-state-changed", version, sourceClientId = owner,
        @params = new { hostId = "local", conversationId = Thread, change }
    };
    private static object Queue(int version, object[] messages) => new
    {
        type = "broadcast", method = "thread-queued-followups-changed", version, sourceClientId = Owner,
        @params = new { hostId = "local", conversationId = Thread, messages }
    };
    private static async Task ReplayAsync(DesktopSessionMonitor monitor, params object[] frames)
    {
        using var input = new MemoryStream();
        foreach (var value in frames)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            input.Write(header); input.Write(bytes);
        }
        input.Position = 0;
        var reader = typeof(DesktopSessionMonitor).GetMethod("ReadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try { await (Task)reader.Invoke(monitor, new object[] { input, CancellationToken.None })!; }
        catch (EndOfStreamException) { }
    }
    private static SessionActivityLedger Ledger(DesktopSessionMonitor monitor)
    {
        var ledger = (SessionActivityLedger)typeof(DesktopSessionMonitor).GetField("_ledger", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(monitor)!;
        ledger.Discover(Thread, Owner); ledger.CompleteDiscovery();
        ledger.SetQueues(new(new(), new()), ledger.BeginQueueRead());
        return ledger;
    }
    internal static async Task RunAsync(List<string> checks)
    {
        foreach (var version in new[] { 11, 13 })
        {
            await using var monitor = new DesktopSessionMonitor("unopened-fixture-home"); Ledger(monitor);
            await ReplayAsync(monitor, Stream(version, Snapshot()));
            Assert(monitor.Current.Available && monitor.Current.Running == 1 && monitor.Current.Goals == 0 && monitor.Current.Errors == 0);
            await ReplayAsync(monitor, Stream(version, new { type = "patches", baseRevision = 1, revision = 2, patches = new object[]
            {
                new { op = "replace", path = new object[] { "threadRuntimeStatus", "type" }, value = "idle" },
                new { op = "replace", path = new object[] { "turns", 0, "status" }, value = "failed" }
            } }));
            Assert(monitor.Current.Available && monitor.Current.Running == 0 && monitor.Current.Errors == 1);
        }
        checks.Add("Observed desktop stream versions11/13 decode actual framed snapshots and failure/recovery patches");

        foreach (var version in new[] { 12, 14 })
        {
            await using var monitor = new DesktopSessionMonitor("unopened-fixture-home"); Ledger(monitor);
            await RejectedAsync<InvalidDataException>(() => ReplayAsync(monitor, Stream(version, Snapshot())));
            Assert(!monitor.Current.Available);
        }
        await using (var monitor = new DesktopSessionMonitor("unopened-fixture-home"))
        {
            Ledger(monitor);
            await RejectedAsync<InvalidDataException>(() => ReplayAsync(monitor, Stream(13, Snapshot("unknown-status"))));
            await ReplayAsync(monitor, Stream(13, Snapshot(), "foreign-owner"));
            Assert(!monitor.Current.Available);
        }
        checks.Add("Unobserved stream versions, invalid runtime states and foreign owners never become available zero counts");

        foreach (var version in new[] { 2, 3 })
        {
            await using var monitor = new DesktopSessionMonitor("unopened-fixture-home");
            var ledger = Ledger(monitor);
            ledger.SetQueues(new(new(), new() { [Thread] = new[] { new DesktopQueueMessage("server-message", "shared-client-message") } }), ledger.BeginQueueRead());
            object message = version == 2
                ? new { id = "saved-message", clientUserMessageId = "shared-client-message" }
                : new { id = "saved-message", submission = new { hostId = "local" }, submissionOptions = new { executionHostId = "local", clientUserMessageId = "shared-client-message" } };
            await ReplayAsync(monitor, Stream(13, Snapshot()), Queue(version, new[] { message }));
            Assert(monitor.Current.Available && monitor.Current.Queued == 1);
        }
        checks.Add("Queue versions2/3 retain client identity across legacy and nested layouts without double-counting the server queue");

        await using (var monitor = new DesktopSessionMonitor("unopened-fixture-home"))
        {
            Ledger(monitor);
            await ReplayAsync(monitor, Stream(13, Snapshot()), Queue(4, Array.Empty<object>()));
            Assert(monitor.Current.Available && monitor.Current.Running == 1 && monitor.Current.Queued == null);
        }
        checks.Add("An unobserved queue version keeps its count unknown while confirmed session status remains available");

        foreach (var value in new object[]
        {
            new { id = "message", clientUserMessageId = "legacy", submissionOptions = new { clientUserMessageId = "different" } },
            new { id = "message", submissionOptions = new { clientUserMessageId = 42 } },
            new { id = "message", clientUserMessageId = "" }
        })
        {
            using var messages = JsonDocument.Parse(JsonSerializer.Serialize(new[] { value }));
            Rejected<InvalidDataException>(() => DesktopQueueState.Messages(messages.RootElement));
        }
        using (var messages = JsonDocument.Parse("[{\"id\":\"message\",\"submissionOptions\":{\"executionHostId\":\"remote\",\"clientUserMessageId\":\"remote-message\"}}]"))
            Assert(DesktopQueueState.Messages(messages.RootElement).Length == 0);
        checks.Add("Queue identity conflicts and malformed identities are rejected while remote-host queue entries remain excluded");
    }
    private static async Task RejectedAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected desktop protocol rejection was not observed.");
    }
    private static void Rejected<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected queue identity rejection was not observed.");
    }
}
