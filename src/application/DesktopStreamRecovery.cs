using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record StreamRecoveryObservation(string Thread, string Owner, string Turn, string Status, string? Error,
    string Model, string Provider, string Identity, string Route, DateTimeOffset At, bool Ready);
internal sealed class StreamRecoveryReceipt
{
    public string Thread { get; set; } = "";
    public string Failure { get; set; } = "";
    public string? SubmittedTurn { get; set; }
    public string Message { get; set; } = "";
    public string Model { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Route { get; set; } = "";
    public int Attempts { get; set; }
    public bool Sending { get; set; }
    public bool Closed { get; set; }
}
internal static class StreamRecoveryPolicy
{
    internal const int MaximumAttempts = 3;
    // Eligibility comes from the persisted terminal status, never error prose.
    // Even an absent message or a message mentioning cancellation can describe
    // a failed request. Actual user interruptions remain a separate turn state.
    internal static bool Failed(string status) => status == "failed";
    internal static bool RegisteredModel(string model, ModelProviderSettings providers) =>
        !model.StartsWith("cs_", StringComparison.Ordinal) || providers.Providers.Any(provider =>
            provider.Models.Any(item => item.Alias(provider.Id) == model));
    internal static bool Same(StreamRecoveryObservation a, StreamRecoveryObservation b) => b.Ready &&
        a.Thread == b.Thread && a.Owner == b.Owner && a.Turn == b.Turn && Failed(b.Status) &&
        a.Model == b.Model && a.Provider == b.Provider && a.Identity == b.Identity && a.Route == b.Route;
}
internal sealed record StreamRecoveryDecision(string Reason, int Attempts = 0);
internal sealed class StreamRecoveryCoordinator
{
    private readonly Dictionary<string, StreamRecoveryReceipt> _receipts;
    private readonly Action _save;
    private readonly Func<int, CancellationToken, Task> _delay;
    internal StreamRecoveryCoordinator(Dictionary<string, StreamRecoveryReceipt> receipts, Action save, Func<int, CancellationToken, Task>? delay = null)
    { _receipts = receipts; _save = save; _delay = delay ?? ((attempt, token) => Task.Delay(TimeSpan.FromSeconds(attempt switch { 1 => 3, 2 => 10, _ => 30 }), token)); }
    internal async Task<StreamRecoveryDecision> CheckAsync(StreamRecoveryObservation observed, Func<CancellationToken, Task<StreamRecoveryObservation?>> refresh,
        Func<string, CancellationToken, Task<string>> send, CancellationToken cancellation)
    {
        _receipts.TryGetValue(observed.Thread, out var receipt);
        if (observed.Status == "completed")
        {
            if (receipt != null && !receipt.Closed && !receipt.Sending && receipt.Identity == observed.Identity && receipt.Route == observed.Route)
            { receipt.Closed = true; _save(); }
            return new("completed", receipt?.Attempts ?? 0);
        }
        if (receipt?.Closed == true && receipt.Failure == observed.Turn) return new("already-handled", receipt.Attempts);
        if (!observed.Ready || !StreamRecoveryPolicy.Failed(observed.Status)) return new("not-failed-or-not-ready");
        if (DateTimeOffset.UtcNow - observed.At > TimeSpan.FromDays(1) || observed.At > DateTimeOffset.UtcNow.AddMinutes(1)) return new("outside-recovery-window");
        if (receipt != null && !receipt.Closed)
        {
            if (receipt.Sending) return new("unconfirmed-send", receipt.Attempts);
            if (receipt.Attempts >= StreamRecoveryPolicy.MaximumAttempts) return new("attempt-limit", receipt.Attempts);
            if (receipt.Failure == observed.Turn) return new("already-handled", receipt.Attempts);
            if (receipt.SubmittedTurn != observed.Turn) return new("different-turn", receipt.Attempts);
            if (receipt.Model != observed.Model || receipt.Provider != observed.Provider ||
                receipt.Identity != observed.Identity || receipt.Route != observed.Route) return new("settings-changed", receipt.Attempts);
        }
        else
        {
            if (_receipts.Count >= 10000 && receipt == null) return new("receipt-limit");
            receipt = new() { Thread = observed.Thread, Model = observed.Model, Provider = observed.Provider, Identity = observed.Identity, Route = observed.Route };
        }
        await _delay(receipt.Attempts + 1, cancellation).ConfigureAwait(false);
        var current = await refresh(cancellation).ConfigureAwait(false);
        if (current == null || !StreamRecoveryPolicy.Same(observed, current)) return new("state-changed", receipt.Attempts);
        cancellation.ThrowIfCancellationRequested();
        // Save before IPC. An interrupted/ambiguous send is never resent on restart.
        receipt.Attempts++; receipt.Failure = observed.Turn; receipt.Message = Guid.NewGuid().ToString(); receipt.Sending = true;
        _receipts[observed.Thread] = receipt; _save();
        var submitted = await send(receipt.Message, cancellation).ConfigureAwait(false);
        if (!Guid.TryParse(submitted, out _)) throw new InvalidDataException("Automatic continuation returned an invalid turn identity.");
        receipt.SubmittedTurn = submitted; receipt.Sending = false; _save();
        return new("continued", receipt.Attempts);
    }
}
internal sealed class StreamRecoveryStore
{
    private readonly string _directory, _path;
    internal Dictionary<string, StreamRecoveryReceipt> Receipts { get; }
    internal StreamRecoveryStore(string directory)
    {
        _directory = directory; _path = Path.Combine(directory, "stream-recovery.vault");
        if (!File.Exists(_path)) { Receipts = new(); return; }
        var plain = VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(_path), directory);
        try { Receipts = JsonSerializer.Deserialize<Dictionary<string, StreamRecoveryReceipt>>(plain) ?? throw new InvalidDataException("Recovery receipt is empty."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        if (Receipts.Count > 10000 || Receipts.Any(pair => !Guid.TryParse(pair.Key, out _) || pair.Key != pair.Value.Thread || pair.Value.Attempts is < 0 or > 3))
            throw new InvalidDataException("Recovery receipt is invalid.");
    }
    internal void Save()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Receipts);
        try { SecureFiles.CreatePrivateDirectory(_directory); SecureFiles.AtomicWrite(_path, VaultProtection.Protect(bytes, _directory)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
