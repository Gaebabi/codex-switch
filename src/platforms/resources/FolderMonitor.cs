using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class FolderMonitor : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private CancellationTokenSource? _scan;
    private DrivePreference[] _configuration = Array.Empty<DrivePreference>();
    private Dictionary<string, FolderReading[]> _current = new(DriveMonitor.RootComparer);
    private Task? _run;
    private int _version;
    public event Action? Changed;
    public FolderReading[] Read(string root)
    { lock (_sync) return _current.TryGetValue(root, out var rows) ? rows : Array.Empty<FolderReading>(); }
    public void Configure(DrivePreference[] preferences)
    {
        lock (_sync)
        {
            _scan?.Cancel(); _configuration = preferences; _version++;
            _current = preferences.ToDictionary(p => p.Root, p => (p.Folders ?? Array.Empty<FolderPreference>()).Select(f => new FolderReading(f.Path, f.Color, null, FolderState.Scanning)).ToArray(), DriveMonitor.RootComparer);
            if (_changed.CurrentCount == 0) _changed.Release();
        }
    }
    public void Start() { _run ??= Task.Run(RunAsync); }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                DrivePreference[] preferences; int version; CancellationTokenSource scan;
                lock (_sync)
                {
                    _changed.Wait(0); preferences = _configuration; version = _version;
                    _scan = scan = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                }
                try
                {
                    var result = FolderUsage.Scan(preferences, scan.Token, (root, reading) =>
                    {
                        lock (_sync)
                        {
                            if (version != _version) return;
                            _current[root] = _current[root].Select(f => DriveMonitor.RootComparer.Equals(f.Path, reading.Path) ? reading : f).ToArray();
                        }
                        Changed?.Invoke();
                    });
                    lock (_sync) { if (version == _version) _current = result; }
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
                finally { lock (_sync) { _scan = null; scan.Dispose(); } }
                await _changed.WaitAsync(TimeSpan.FromSeconds(60), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); if (_run != null) await _run; _changed.Dispose(); _stop.Dispose(); }
}
