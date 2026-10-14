using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class RefreshCoordinator
{
    private sealed class Entry : IDisposable
    {
        public readonly string Key;
        public readonly CancellationTokenSource Cancellation;
        public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lifecycle = new();
        private bool _disposed;
        public Entry(string key, CancellationToken token) { Key = key; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); }
        public void Cancel() { lock (_lifecycle) { if (!_disposed) Cancellation.Cancel(); } }
        public void Dispose() { lock (_lifecycle) { _disposed = true; Cancellation.Dispose(); } }
    }
    private readonly object _state = new();
    private readonly Dictionary<string, Entry> _running = new();
    private readonly Func<string, CancellationToken, Task> _refresh;
    public event Action? Changed;
    public RefreshCoordinator(Func<string, CancellationToken, Task> refresh) { _refresh = refresh; }
    public bool IsRunning(string key) { lock (_state) return _running.ContainsKey(key); }
    public int RunningCount { get { lock (_state) return _running.Count; } }
    public Task Schedule(string key, CancellationToken cancellation, bool supersede = false)
    {
        Entry? previous;
        Entry entry;
        lock (_state)
        {
            _running.TryGetValue(key, out previous);
            if (previous != null && !supersede) return previous.Completion.Task.WaitAsync(cancellation);
            entry = new Entry(key, cancellation); _running[key] = entry;
        }
        previous?.Cancel(); Changed?.Invoke();
        _ = ExecuteAsync(entry, previous);
        return entry.Completion.Task;
    }
    private async Task ExecuteAsync(Entry entry, Entry? previous)
    {
        Exception? failure = null;
        bool cancelled = false;
        try
        {
            if (previous != null)
            {
                try { await previous.Completion.Task.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            await Task.Run(() => _refresh(entry.Key, entry.Cancellation.Token), entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception error) { failure = error; }
        finally
        {
            lock (_state) { if (_running.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _running.Remove(entry.Key); }
            entry.Dispose(); Changed?.Invoke();
        }
        if (cancelled) entry.Completion.TrySetCanceled();
        else if (failure != null) entry.Completion.TrySetException(failure);
        else entry.Completion.TrySetResult(true);
    }
    public async Task CancelAndDrainAsync(IEnumerable<string>? keys = null)
    {
        Entry[] entries;
        lock (_state) entries = _running.Values.Where(e => keys == null || keys.Contains(e.Key)).ToArray();
        foreach (var entry in entries) entry.Cancel();
        foreach (var entry in entries)
        {
            try { await entry.Completion.Task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }
}
