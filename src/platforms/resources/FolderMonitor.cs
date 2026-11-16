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
    private readonly HashSet<string> _dirty = new(DriveMonitor.RootComparer);
    private readonly HashSet<string> _reset = new(DriveMonitor.RootComparer);
    private FolderUsage.Cache _cache = new();
    private bool _active = true, _needsMeasurement = true;
    internal (long Roots, long Directories) QueryCounts
    { get { var cache = Volatile.Read(ref _cache); return (cache.RootReads, cache.DirectoryReads); } }
    internal bool IsMeasuring { get { lock (_sync) return _scan != null; } }
    public event Action? Changed;
    public FolderReading[] Read(string root)
    { lock (_sync) return _current.TryGetValue(root, out var rows) ? rows : Array.Empty<FolderReading>(); }
    public void Configure(DrivePreference[] preferences)
    {
        lock (_sync)
        {
            _scan?.Cancel(); _configuration = preferences; _version++; _dirty.Clear(); _reset.Clear(); _needsMeasurement = true;
            _current = preferences.ToDictionary(p => p.Root, p => (p.Folders ?? Array.Empty<FolderPreference>()).Select(f => new FolderReading(f.Path, f.Color, null, FolderState.Scanning)).ToArray(), DriveMonitor.RootComparer);
            if (_changed.CurrentCount == 0) _changed.Release();
        }
    }
    public void Start() { _run ??= Task.Run(RunAsync); }
    public void SetActive(bool active)
    {
        lock (_sync)
        {
            if (_active == active || _stop.IsCancellationRequested) return;
            _active = active; _needsMeasurement = true;
            if (!active) _scan?.Cancel();
            if (_changed.CurrentCount == 0) _changed.Release();
        }
    }
    private void Invalidate(int version, string path, bool reset)
    {
        lock (_sync)
        {
            if (version != _version || _stop.IsCancellationRequested) return;
            (reset ? _reset : _dirty).Add(path);
            if (_active && _changed.CurrentCount == 0) _changed.Release();
        }
    }
    private async Task RunAsync()
    {
        var watches = new List<FolderChanges>(); var cache = _cache; var cachedVersion = -1; var retry = false;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                bool active; lock (_sync) active = _active;
                if (!active) { await _changed.WaitAsync(_stop.Token); continue; }
                DrivePreference[] preferences; int version; CancellationTokenSource scan; string[] dirty, reset; bool pending;
                lock (_sync)
                {
                    if (!_active) continue;
                    _changed.Wait(0); preferences = _configuration; version = _version;
                    pending = _needsMeasurement; _needsMeasurement = false;
                    dirty = _dirty.ToArray(); reset = _reset.ToArray(); _dirty.Clear(); _reset.Clear();
                    _scan = scan = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                }
                try
                {
                    var measure = pending || cachedVersion != version || dirty.Length != 0 || reset.Length != 0;
                    if (cachedVersion != version)
                    {
                        foreach (var watch in watches) watch.Dispose(); watches.Clear(); _cache = cache = new(); cachedVersion = version;
                        foreach (var folder in preferences.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()))
                            watches.Add(new(folder.Path, (path, full) => Invalidate(version, path, full)));
                    }
                    foreach (var path in dirty) cache.Invalidate(path);
                    foreach (var path in reset) cache.Reset(path);
                    foreach (var watch in watches)
                    {
                        scan.Token.ThrowIfCancellationRequested();
                        if (watch.Connect()) { cache.Reset(watch.Path); measure = true; }
                    }
                    if (retry)
                    {
                        lock (_sync) measure |= _current.Values.SelectMany(r => r).Any(r => r.State == FolderState.Partial);
                        cache.RetryPartial();
                    }
                    if (measure)
                    {
                        var result = FolderUsage.Scan(preferences, scan.Token, (root, reading) =>
                        {
                            lock (_sync)
                            {
                                if (version != _version) return;
                                _current[root] = _current[root].Select(f => DriveMonitor.RootComparer.Equals(f.Path, reading.Path) ? reading : f).ToArray();
                            }
                            Changed?.Invoke();
                        }, cache);
                        if (OperatingSystem.IsWindows())
                            foreach (var root in result.Keys.ToArray())
                                result[root] = result[root].Select(row => row.State == FolderState.Available && watches.Any(w => DriveMonitor.RootComparer.Equals(w.Path, row.Path) && !w.Available)
                                    ? row with { State = FolderState.Partial, Skipped = row.Skipped + 1 } : row).ToArray();
                        lock (_sync) { if (version == _version) _current = result; }
                        Changed?.Invoke();
                    }
                }
                catch (OperationCanceledException) when (scan.IsCancellationRequested) { lock (_sync) _needsMeasurement = true; }
                finally { lock (_sync) { _scan = null; scan.Dispose(); } }
                retry = !await _changed.WaitAsync(TimeSpan.FromSeconds(60), _stop.Token);
                if (!retry) await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { foreach (var watch in watches) watch.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); if (_run != null) await _run; _changed.Dispose(); _stop.Dispose(); }
}
