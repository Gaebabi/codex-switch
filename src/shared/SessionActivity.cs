using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record SessionActivity(bool Available, int Running, int Waiting, int Observed, string? Problem = null,
    int? Queued = null, int? Goals = null, int PausedQueued = 0, int PausedGoals = 0, string? QueueProblem = null);

internal sealed record DesktopThreadState(string Id, string Owner, string Status, bool Waiting, string? Goal, string? Title,
    string? ResumeState, string? LastTurnId, string? LastTurnStatus, string? LastMessageId);

internal sealed class SessionActivityLedger
{
    private sealed record Entry(string Owner, long Revision = -1, string? Status = null, bool Waiting = false, bool Child = false,
        bool GoalKnown = false, string? Goal = null, string? Title = null, string? ResumeState = null,
        string? LastTurnId = null, string? LastTurnStatus = null, string? LastMessageId = null, int LastTurnIndex = -1);
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _sync = new();
    private bool _discovered;
    private DesktopQueueSnapshot? _queues;
    private readonly Dictionary<string, (long Sequence, DesktopQueueMessage[] Messages)> _queueOverrides = new();
    private long _queueSequence;
    private string? _queueProblem;
    public void Discover(string id, string owner) { lock (_sync) { _entries[id] = new(owner); _queueOverrides.Remove(id); } }
    public void Remove(string id) { lock (_sync) { _entries.Remove(id); _queueOverrides.Remove(id); } }
    public void CompleteDiscovery() { lock (_sync) _discovered = true; }
    public bool OwnsClient(string? owner) { lock (_sync) return _entries.Values.Any(e => e.Owner == owner); }
    public void Reset() { lock (_sync) { _entries.Clear(); _discovered = false; _queues = null; _queueProblem = null; _queueOverrides.Clear(); _queueSequence++; } }
    public long BeginQueueRead() { lock (_sync) return _queueSequence; }
    public void SetQueues(DesktopQueueSnapshot? queues, long readSequence, string? problem = null)
    {
        lock (_sync)
        {
            _queues = queues; _queueProblem = problem;
            if (queues == null) return;
            foreach (var pair in _queueOverrides.ToArray())
                if (pair.Value.Sequence <= readSequence && DesktopQueueState.Same(pair.Value.Messages, queues.Saved.GetValueOrDefault(pair.Key) ?? Array.Empty<DesktopQueueMessage>()))
                    _queueOverrides.Remove(pair.Key);
        }
    }
    public void ApplyQueue(string id, string owner, JsonElement messages)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(id, out var entry) && entry.Owner == owner)
                _queueOverrides[id] = (++_queueSequence, DesktopQueueState.Messages(messages));
        }
    }
    public DesktopThreadState[] Threads()
    {
        lock (_sync) return _entries.Where(pair => pair.Value.Status != null && !pair.Value.Child).Select(pair =>
        {
            var e = pair.Value;
            return new DesktopThreadState(pair.Key, e.Owner, e.Status!, e.Waiting, e.Goal, e.Title, e.ResumeState, e.LastTurnId, e.LastTurnStatus, e.LastMessageId);
        }).ToArray();
    }
    public SessionActivity Read()
    {
        lock (_sync)
        {
            var observed = _entries.Values.Where(e => e.Status != null).ToArray();
            var roots = observed.Where(e => !e.Child).ToArray();
            var queues = _queues == null ? null : _queues.Saved.Keys.Concat(_queues.Server.Keys).Concat(_queueOverrides.Keys).Distinct()
                .Where(id => !_entries.TryGetValue(id, out var e) || !e.Child).Select(id => DesktopQueueState.Count(
                    _queueOverrides.TryGetValue(id, out var update) ? update.Messages : _queues.Saved.GetValueOrDefault(id) ?? Array.Empty<DesktopQueueMessage>(),
                    _queues.Server.GetValueOrDefault(id) ?? Array.Empty<DesktopQueueMessage>(), _entries.GetValueOrDefault(id)?.LastTurnStatus == "interrupted")).ToArray();
            var available = _discovered && observed.Length == _entries.Count;
            return new(_discovered && observed.Length == _entries.Count, roots.Count(e => e.Status == "active" && !e.Waiting),
                roots.Count(e => e.Status == "active" && e.Waiting), roots.Length, Queued: queues?.Sum(q => q.Total),
                Goals: available && roots.All(e => e.GoalKnown) ? roots.Count(e => e.Goal == "active") : null,
                PausedQueued: queues?.Sum(q => q.Paused) ?? 0,
                PausedGoals: roots.Count(e => e.Goal is "paused" or "blocked" or "usageLimited" or "budgetLimited"), QueueProblem: _queueProblem);
        }
    }
    public bool Apply(string id, string owner, JsonElement change)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out var entry) || entry.Owner != owner) return false;
            var revision = JsonValue.Long(change, "revision") ?? throw new System.IO.InvalidDataException("Session revision is missing.");
            var type = JsonValue.String(change, "type");
            if (type == "snapshot")
            {
                if (revision < entry.Revision) return false;
                var state = change.GetProperty("conversationState");
                var status = state.GetProperty("threadRuntimeStatus");
                var hasGoal = state.TryGetProperty("threadGoal", out var goal);
                var goalStatus = hasGoal ? ReadGoal(goal) : null;
                var last = state.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array ? turns.EnumerateArray().LastOrDefault() : default;
                _entries[id] = new(owner, revision, ReadStatus(status), IsWaiting(status),
                    JsonValue.String(state, "parentThreadId") != null || JsonValue.Bool(state, "sideConversation") == true,
                    hasGoal && (goal.ValueKind == JsonValueKind.Null || goalStatus != null), goalStatus,
                    JsonValue.String(state, "title") ?? JsonValue.String(state, "fallbackSearchTitle"), JsonValue.String(state, "resumeState"),
                    last.ValueKind == JsonValueKind.Object ? JsonValue.String(last, "turnId") : null,
                    last.ValueKind == JsonValueKind.Object ? JsonValue.String(last, "status") : null, JsonValue.String(state, "latestTurnStartMessageId"),
                    turns.ValueKind == JsonValueKind.Array ? turns.GetArrayLength() - 1 : -1);
                return false;
            }
            if (type != "patches") throw new System.IO.InvalidDataException("Unsupported session change.");
            if (revision <= entry.Revision) return false;
            if (entry.Status == null || JsonValue.Long(change, "baseRevision") != entry.Revision)
            {
                _entries[id] = entry with { Revision = -1, Status = null };
                return true;
            }
            foreach (var patch in change.GetProperty("patches").EnumerateArray())
            {
                var path = patch.GetProperty("path").EnumerateArray().ToArray();
                if (path.Length == 0) { _entries[id] = entry with { Status = null }; return true; }
                if (path[0].ValueKind != JsonValueKind.String) continue;
                var key = path[0].GetString();
                if (key is "title" or "fallbackSearchTitle" or "resumeState" or "latestTurnStartMessageId")
                {
                    var metadataValue = JsonValue.String(patch, "op") == "remove" ? null : JsonValue.String(patch, "value");
                    entry = key switch { "title" => entry with { Title = metadataValue }, "fallbackSearchTitle" => entry with { Title = entry.Title ?? metadataValue },
                        "resumeState" => entry with { ResumeState = metadataValue }, _ => entry with { LastMessageId = metadataValue } };
                    continue;
                }
                if (key == "turns")
                {
                    if (path.Length == 1 || path[1].ValueKind != JsonValueKind.Number)
                    { _entries[id] = entry with { Status = null }; return true; }
                    var turnIndex = path[1].GetInt32();
                    if (turnIndex < entry.LastTurnIndex) continue;
                    if (path.Length == 2 && JsonValue.String(patch, "op") == "remove")
                    { _entries[id] = entry with { Status = null }; return true; }
                    if (path.Length == 2)
                    {
                        var turn = patch.GetProperty("value");
                        if (turn.ValueKind == JsonValueKind.Object)
                            entry = entry with { LastTurnId = JsonValue.String(turn, "turnId"), LastTurnStatus = JsonValue.String(turn, "status"), LastTurnIndex = turnIndex };
                    }
                    else if (path.Length == 3 && JsonValue.String(patch, "op") != "remove" && path[2].ValueKind == JsonValueKind.String)
                    {
                        var field = path[2].GetString(); var turnValue = JsonValue.String(patch, "value");
                        if (field == "turnId") entry = entry with { LastTurnId = turnValue, LastTurnIndex = turnIndex };
                        else if (field == "status") entry = entry with { LastTurnStatus = turnValue, LastTurnIndex = turnIndex };
                    }
                    continue;
                }
                if (key is not ("threadRuntimeStatus" or "parentThreadId" or "sideConversation" or "threadGoal")) continue;
                if (key == "threadGoal")
                {
                    var removed = JsonValue.String(patch, "op") == "remove";
                    if (path.Length == 1)
                    {
                        var goal = removed ? default : patch.GetProperty("value");
                        var goalStatus = removed ? null : ReadGoal(goal);
                        entry = entry with { GoalKnown = removed || goal.ValueKind == JsonValueKind.Null || goalStatus != null, Goal = goalStatus };
                    }
                    else if (path.Length == 2 && path[1].GetString() == "status")
                    {
                        var goalStatus = removed ? null : ReadGoalType(patch.GetProperty("value").GetString());
                        entry = entry with { GoalKnown = goalStatus != null, Goal = goalStatus };
                    }
                    continue;
                }
                if (JsonValue.String(patch, "op") == "remove") { _entries[id] = entry with { Status = null }; return true; }
                var value = patch.GetProperty("value");
                if (key == "threadRuntimeStatus")
                {
                    if (path.Length == 1) entry = entry with { Status = ReadStatus(value), Waiting = IsWaiting(value) };
                    else if (path.Length == 2 && path[1].GetString() == "type") entry = entry with { Status = ReadStatusType(value.GetString()) };
                    else if (path.Length == 2 && path[1].GetString() == "activeFlags") entry = entry with { Waiting = IsWaitingFlags(value) };
                    else { _entries[id] = entry with { Status = null }; return true; }
                }
                else { _entries[id] = entry with { Status = null }; return true; }
            }
            _entries[id] = entry with { Revision = revision };
            return false;
        }
    }
    private static string ReadStatus(JsonElement status) => ReadStatusType(JsonValue.String(status, "type"));
    private static string ReadStatusType(string? type) => type is "active" or "idle" or "notLoaded" or "systemError" ? type :
        throw new System.IO.InvalidDataException("Unsupported session status.");
    private static bool IsWaiting(JsonElement status) => status.TryGetProperty("activeFlags", out var flags) && IsWaitingFlags(flags);
    private static bool IsWaitingFlags(JsonElement flags) => flags.EnumerateArray().Any(f => f.GetString() is "waitingOnApproval" or "waitingOnUserInput");
    private static string? ReadGoal(JsonElement goal) => goal.ValueKind == JsonValueKind.Null ? null : ReadGoalType(JsonValue.String(goal, "status"));
    private static string? ReadGoalType(string? status) => status is "active" or "paused" or "blocked" or "complete" or "budgetLimited" or "usageLimited" ? status : null;
}
