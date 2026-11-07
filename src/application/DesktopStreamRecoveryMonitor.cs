using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

// Continue any persisted failed turn through its current desktop owner. The
// original Responses stream is never replayed after tools/output may have run.
internal sealed class DesktopStreamRecoveryMonitor : IAsyncDisposable
{
    private readonly AccountService _service;
    private readonly DesktopSessionMonitor _sessions;
    private readonly Func<bool> _ready;
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;
    private readonly System.Collections.Generic.HashSet<string> _blocked = new(StringComparer.Ordinal);
    private DateTimeOffset _lastStatus;
    private sealed record Diagnostic(DateTimeOffset AtUtc, string Chat, string? Turn, string Reason, int Attempts);
    private readonly Queue<Diagnostic> _decisions = new();
    internal string? LastError { get; private set; }
    internal DesktopStreamRecoveryMonitor(AccountService service, DesktopSessionMonitor sessions, Func<bool> ready)
    { _service = service; _sessions = sessions; _ready = ready; }
    internal void Start() => _run ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        try
        {
            var store = new StreamRecoveryStore(_service.Vault.DirectoryPath);
            var recovery = new StreamRecoveryCoordinator(store.Receipts, store.Save);
            while (!_stop.IsCancellationRequested)
            {
                if (_ready() && _sessions.Current.Available)
                foreach (var thread in _sessions.Threads.Where(Stopped))
                {
                    if (_blocked.Contains(thread.Id)) continue;
                    try
                    {
                        var observed = await ObserveAsync(thread.Id, _stop.Token).ConfigureAwait(false);
                        if (observed == null) continue;
                        var decision = await recovery.CheckAsync(observed, token => ObserveAsync(thread.Id, token), async (message, token) =>
                        {
                            // Re-read immediately before submitting. This never opens or
                            // activates a window, applies credentials or changes models.
                            var current = await ObserveAsync(thread.Id, token).ConfigureAwait(false);
                            if (current == null || !StreamRecoveryPolicy.Same(observed, current)) throw new InvalidOperationException("The chat changed before automatic continuation.");
                            var owner = _sessions.Threads.Single(item => item.Id == observed.Thread);
                            var result = await _sessions.SendToOwnerAsync(owner, "thread-follower-start-turn", StartParameters(observed.Thread, message), 2, token).ConfigureAwait(false);
                            return JsonValue.String(result.GetProperty("result").GetProperty("turn"), "id") ?? throw new InvalidDataException("Automatic continuation has no confirmed turn.");
                        }, _stop.Token).ConfigureAwait(false);
                        Record(thread.Id, observed.Turn, decision.Reason, decision.Attempts);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                    catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
                    {
                        // A failed observation/send never triggers a retry of that IPC.
                        // Isolate this chat; other verified chats can still recover.
                        _blocked.Add(thread.Id);
                        LastError = "Automatic stream recovery stopped for a chat (" + error.GetType().Name + ").";
                        Record(thread.Id, null, "blocked-" + error.GetType().Name);
                    }
                }
                if (DateTimeOffset.UtcNow - _lastStatus > TimeSpan.FromSeconds(10))
                {
                    WriteStatus("monitoring", store.Receipts.Values.Sum(item => item.Attempts), store.Receipts.Values.Count(item => item.Sending));
                    _lastStatus = DateTimeOffset.UtcNow;
                }
                await Task.Delay(3000, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Never resend after an ambiguous IPC or persistence failure. The
            // saved Sending receipt prevents it even after an app restart.
            LastError = "Automatic stream recovery stopped (" + error.GetType().Name + ").";
            // Keep the failure inspectable without writing chats, keys or raw upstream errors.
            try { WriteStatus("stopped", null, null); }
            catch (Exception storage) when (storage is IOException or UnauthorizedAccessException) { LastError += " Status storage unavailable."; }
        }
    }
    private void WriteStatus(string state, int? attempts, int? unconfirmed) => SecureFiles.AtomicWriteText(
        Path.Combine(_service.Vault.DirectoryPath, "stream-recovery-state.json"), JsonSerializer.Serialize(new {
            schema = 1, processId = Environment.ProcessId, atUtc = DateTimeOffset.UtcNow, state, ready = _ready(),
            desktopAvailable = _sessions.Current.Available, observedChats = _sessions.Threads.Length,
            blockedChats = _blocked.Count, attempts, unconfirmed, error = LastError, decisions = _decisions.ToArray() }));
    private void Record(string id, string? turn, string reason, int attempts = 0)
    {
        // Bounded fixed-code diagnostics: no chat text, raw errors, account IDs,
        // provider destinations or credentials. Suppress identical polling entries.
        var chat = SecureFiles.Hash(id);
        var key = turn == null ? null : SecureFiles.Hash(turn);
        var last = _decisions.LastOrDefault(item => item.Chat == chat);
        if (last != null && last.Turn == key && last.Reason == reason && last.Attempts == attempts) return;
        _decisions.Enqueue(new(DateTimeOffset.UtcNow, chat, key, reason, attempts));
        while (_decisions.Count > 128) _decisions.Dequeue();
    }
    private static bool Stopped(DesktopThreadState thread) => !thread.Waiting && thread.ResumeState == "resumed" &&
        thread.Status is "systemError" or "idle" && thread.LastTurnStatus != "interrupted" && thread.Goal is null or "active";
    private async Task<StreamRecoveryObservation?> ObserveAsync(string id, CancellationToken cancellation)
    {
        StreamRecoveryObservation? Skip(string reason, string? turn = null) { Record(id, turn, reason); return null; }
        if (!_ready() || !_sessions.Current.Available) return Skip("not-ready");
        if (_sessions.QueuedFor(id) != 0) return Skip("queued-or-queue-unknown");
        var thread = _sessions.Threads.SingleOrDefault(item => item.Id == id);
        if (thread == null || !Stopped(thread)) return Skip("not-stopped");
        var outcome = await Task.Run(() => DesktopTurnOutcomes.Read(_service.CodexHome, id), cancellation).ConfigureAwait(false);
        if (outcome == null) return Skip("terminal-outcome-unknown");
        if (outcome.Status is not ("failed" or "completed")) return Skip("not-failed", outcome.Id);
        var result = await _sessions.SendToOwnerAsync(thread, "thread-follower-read-model-settings", new { conversationId = id }, 1, cancellation).ConfigureAwait(false);
        var settings = result.GetProperty("settings");
        if (JsonValue.String(settings, "model") != outcome.Model || JsonValue.String(settings, "modelProvider") != outcome.Provider ||
            JsonValue.String(settings, "resumeState") != "resumed" || JsonValue.String(settings, "threadSource") != "user") return Skip("model-or-owner-settings-changed", outcome.Id);
        var auth = _service.AuthPath;
        if (!File.Exists(auth) || (File.GetAttributes(auth) & FileAttributes.ReparsePoint) != 0) return Skip("authentication-unavailable", outcome.Id);
        var signedIn = Encoding.UTF8.GetString(SecureFiles.ReadPrivateBytes(auth));
        var identity = SecureFiles.Hash(signedIn);
        // Pin the actual provider destination/key set across backoff as well as
        // the chat's model-provider selection. Secrets never enter the receipt.
        var providers = _service.ModelProviders.Settings;
        if (!providers.Connected || !_service.ModelProviders.Running || outcome.Provider is not ("openai" or "codex_switch_router")) return Skip("route-unavailable", outcome.Id);
        // A first request may fail before any successful-route receipt exists.
        // Continue through the owner's unchanged current settings; do not invent
        // successful provenance or bypass the gateway's portable-history checks.
        if (!StreamRecoveryPolicy.RegisteredModel(outcome.Model, providers)) return Skip("selected-model-unregistered", outcome.Id);
        var rechecked = await Task.Run(() => DesktopTurnOutcomes.Read(_service.CodexHome, id), cancellation).ConfigureAwait(false);
        if (rechecked == null || rechecked.Id != outcome.Id || rechecked.Status != outcome.Status) return Skip("turn-changed", outcome.Id);
        var route = SecureFiles.Hash(JsonSerializer.Serialize(providers.Providers));
        var current = _sessions.Threads.SingleOrDefault(item => item.Id == id);
        if (current == null || current.Owner != thread.Owner || !Stopped(current) || _sessions.QueuedFor(id) != 0) return Skip("state-changed", outcome.Id);
        return new(id, thread.Owner, outcome.Id, outcome.Status, outcome.Error, outcome.Model, outcome.Provider, identity, route, outcome.At, true);
    }
    internal static object StartParameters(string thread, string message) => new { conversationId = thread,
        turnStart = new { request = new { threadId = thread, clientUserMessageId = message,
            input = new[] { new { type = "text", text = "Continue", text_elements = Array.Empty<object>() } } }, context = new { inheritThreadSettings = true } } };
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); if (_run != null) await _run.ConfigureAwait(false); _stop.Dispose(); }
}
