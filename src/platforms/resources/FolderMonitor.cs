using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record FolderIndexStatus(FolderIndexAccess Access, long Directories = 0, long Files = 0, string? Problem = null);

internal sealed class FolderMonitor : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private CancellationTokenSource? _scan;
    private DrivePreference[] _configuration = Array.Empty<DrivePreference>();
    private Dictionary<string, FolderReading[]> _current = new(DriveMonitor.RootComparer);
    private readonly Dictionary<string, FolderIndexStatus> _status = new(DriveMonitor.RootComparer);
    private readonly HashSet<string> _dirty = new(DriveMonitor.RootComparer), _reset = new(DriveMonitor.RootComparer), _rebuild = new(DriveMonitor.RootComparer);
    private readonly FolderScanBudget _budget = new();
    private readonly string? _indexDirectory;
    private FolderUsage.Cache _cache;
    private Task? _run;
    private int _version;
    private bool _active = true, _needsMeasurement = true;
    private string? _measuringRoot;
    internal FolderMonitor(string? indexDirectory = null) { _indexDirectory = indexDirectory; _cache = new(_budget); }
    internal (long Roots, long Directories) QueryCounts { get { var cache = Volatile.Read(ref _cache); return (cache.RootReads, cache.DirectoryReads); } }
    internal bool IsMeasuring { get { lock (_sync) return _scan != null; } }
    internal bool IsMeasuringRoot(string root) { lock (_sync) return _active && DriveMonitor.RootComparer.Equals(root, _measuringRoot); }
    internal FolderIndexStatus? IndexStatus(string root) { lock (_sync) return _status.GetValueOrDefault(root); }
    public event Action? Changed;
    public FolderReading[] Read(string root) { lock (_sync) return _current.GetValueOrDefault(root) ?? Array.Empty<FolderReading>(); }
    public void Configure(DrivePreference[] preferences)
    {
        lock (_sync)
        {
            _scan?.Cancel(); _configuration = preferences; _version++; _dirty.Clear(); _reset.Clear(); _needsMeasurement = true;
            _current = preferences.ToDictionary(p => p.Root, p => (p.Folders ?? Array.Empty<FolderPreference>()).Select(f => new FolderReading(f.Path, f.Color, null, FolderState.Scanning)).ToArray(), DriveMonitor.RootComparer);
            Signal();
        }
    }
    internal void RefreshIndex(string root, bool rebuild)
    {
        lock (_sync) { if (rebuild) _rebuild.Add(root); _needsMeasurement = true; Signal(); }
    }
    public void Start() { _run ??= Task.Run(RunAsync); }
    public void SetActive(bool active)
    {
        lock (_sync)
        {
            if (_active == active || _stop.IsCancellationRequested) return;
            _active = active; _needsMeasurement = true; if (!active) _scan?.Cancel(); Signal();
        }
    }
    private void Signal() { if (_changed.CurrentCount == 0) _changed.Release(); }
    private void Invalidate(int version, string path, bool reset)
    {
        lock (_sync)
        {
            if (version != _version || _stop.IsCancellationRequested) return;
            (reset ? _reset : _dirty).Add(path); if (_active) Signal();
        }
    }
    private async Task RunAsync()
    {
        var watches = new List<FolderChanges>(); FolderIndexStore? store = null;
        var cache = _cache; var cachedVersion = -1; var retry = false; var indexFaulted = false;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                DrivePreference[] preferences; int version; bool active;
                lock (_sync) { preferences = _configuration; version = _version; active = _active; }
                try
                {
                    bool recover; lock (_sync) recover = indexFaulted && _rebuild.Count != 0;
                    if (indexFaulted && !recover) { await _changed.WaitAsync(_stop.Token); continue; }
                    if (recover && _indexDirectory != null)
                    {
                        foreach (var watch in watches) watch.Dispose(); watches.Clear();
                        store?.Discard(); store = null;
                        FolderIndexStore.Quarantine(_indexDirectory); cachedVersion = -1; cache = new(_budget); indexFaulted = false;
                    }
                    if (cachedVersion != version)
                    {
                        foreach (var watch in watches) watch.Dispose(); watches.Clear(); cache.Flush();
                        if (_indexDirectory != null && preferences.Any(p => p.Folders?.Length > 0)) store ??= new(_indexDirectory);
                        if (store != null)
                        {
                            var saved = store.Read<FolderReading>(FolderIndexStore.ReadingKind).ToDictionary(p => p.Key, p => p.Value, DriveMonitor.RootComparer);
                            lock (_sync)
                                if (version == _version)
                                    foreach (var preference in preferences)
                                        _current[preference.Root] = (preference.Folders ?? Array.Empty<FolderPreference>()).Select(folder => saved.TryGetValue(folder.Path, out var row)
                                            ? row with { Path = folder.Path, Color = folder.Color, State = FolderState.Cached } : new(folder.Path, folder.Color, null, FolderState.Scanning)).ToArray();
                            Changed?.Invoke();
                        }
                        cache = new(_budget, store) { DirectoriesPerSlice = store == null ? int.MaxValue : 8 };
                        cache.Configure(preferences); cache.RefreshManaged(_stop.Token); Volatile.Write(ref _cache, cache); cachedVersion = version;
                        foreach (var folder in preferences.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()))
                            watches.Add(new(folder.Path, (path, full) => Invalidate(version, path, full), cache.Owns));
                        lock (_sync)
                            if (version == _version)
                                _current = preferences.ToDictionary(p => p.Root, p => cache.Saved(p.Root).Select(r => store != null && r.Bytes.HasValue ? r with { State = FolderState.Cached } : r).ToArray(), DriveMonitor.RootComparer);
                        Changed?.Invoke();
                    }
                    if (!active) { await _changed.WaitAsync(_stop.Token); continue; }
                    string[] dirty, reset, rebuild; bool pending; CancellationTokenSource scan;
                    lock (_sync)
                    {
                        if (!_active || version != _version) continue;
                        _changed.Wait(0); pending = _needsMeasurement; _needsMeasurement = false;
                        dirty = _dirty.ToArray(); reset = _reset.ToArray(); rebuild = _rebuild.ToArray(); _dirty.Clear(); _reset.Clear(); _rebuild.Clear();
                        _scan = scan = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    }
                    var continueWork = false;
                    try
                    {
                        var journal = new Dictionary<string, FolderJournalStatus>(DriveMonitor.RootComparer);
                        void Synchronize()
                        {
                            if (store == null) return;
                            foreach (var drive in preferences.Where(p => p.Folders?.Length > 0))
                            {
                                var status = _budget.Query(() => WindowsFolderJournal.Probe(drive.Root), scan.Token); journal[drive.Root] = status;
                                var progress = cache.Progress(drive.Root);
                                lock (_sync) if (version == _version) _status[drive.Root] = new(status.Access, progress.Directories, progress.Files,
                                    status.Error > 0 ? status.Error.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                            }
                            Changed?.Invoke();
                            foreach (var drive in preferences.Where(p => p.Folders?.Length > 0))
                            {
                                var status = journal[drive.Root];
                                if (status.Access != FolderIndexAccess.Ready) continue;
                                if (cache.Cursors.TryGetValue(drive.Root, out var saved) && WindowsFolderJournal.Covers(status, saved))
                                    cache.SaveCursor(drive.Root, WindowsFolderJournal.Replay(drive.Root, status, saved, _budget, change => cache.Invalidate(drive.Root, change), scan.Token));
                                else
                                {
                                    foreach (var folder in drive.Folders!) if (cache.HasSavedIndex(folder.Path)) cache.Reset(folder.Path);
                                    cache.SaveCursor(drive.Root, new(status.JournalId, status.NextUsn));
                                }
                            }
                        }
                        cache.Checkpoint = Synchronize;
                        Synchronize();
                        foreach (var watch in watches)
                        {
                            scan.Token.ThrowIfCancellationRequested();
                            if (!watch.Available) _budget.Query(watch.Connect, scan.Token);
                        }
                        // The watcher covers changes while the journal checkpoint is being read.
                        Synchronize();
                        foreach (var path in dirty)
                            if (store == null || !journal.TryGetValue(Path.GetPathRoot(path)!, out var source) || source.Access != FolderIndexAccess.Ready) cache.Invalidate(path);
                        foreach (var path in reset)
                            if (watches.Where(w => DriveMonitor.RootComparer.Equals(w.Path, path)).Any(w => w.TakeIdentityChange()) ||
                                store == null || !journal.TryGetValue(Path.GetPathRoot(path)!, out var source) || source.Access != FolderIndexAccess.Ready) cache.Reset(path);
                        foreach (var root in rebuild)
                            foreach (var folder in preferences.Where(p => DriveMonitor.RootComparer.Equals(p.Root, root)).SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>())) cache.Reset(folder.Path);
                        if (retry) cache.RetryPartial();
                        var allowed = preferences.Where(p => store == null || !journal.TryGetValue(p.Root, out var access) || access.Access == FolderIndexAccess.Ready ||
                            access.Access == FolderIndexAccess.Unsupported && (rebuild.Contains(p.Root, DriveMonitor.RootComparer) || p.Folders?.All(f => !cache.HasSavedIndex(f.Path)) == true || dirty.Concat(reset).Any(d => DriveMonitor.RootComparer.Equals(Path.GetPathRoot(d), p.Root)))) .ToArray();
                        var paths = allowed.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()).Select(p => p.Path).ToArray();
                        var measure = pending || dirty.Length > 0 || reset.Length > 0 || rebuild.Length > 0 || cache.HasWorkFor(paths) || retry && allowed.Any(p => cache.Saved(p.Root).Any(r => r.State == FolderState.Partial));
                        if (measure && paths.Length > 0)
                        {
                            var result = FolderUsage.Scan(allowed, scan.Token, measured: (root, reading) =>
                            {
                                var progress = cache.Progress(root);
                                lock (_sync)
                                    if (version == _version)
                                    {
                                        _current[root] = _current[root].Select(row => DriveMonitor.RootComparer.Equals(row.Path, reading.Path) ? reading : row).ToArray();
                                        if (_status.TryGetValue(root, out var previous)) _status[root] = previous with { Directories = progress.Directories, Files = progress.Files };
                                    }
                                Changed?.Invoke();
                            }, cache: cache, activity: (root, measuring) =>
                            {
                                lock (_sync) _measuringRoot = measuring ? root : null;
                                Changed?.Invoke();
                            });
                            cache.Configure(preferences);
                            if (OperatingSystem.IsWindows())
                                foreach (var root in result.Keys.ToArray())
                                    result[root] = result[root].Select(row => row.State == FolderState.Available && watches.Any(w => DriveMonitor.RootComparer.Equals(w.Path, row.Path) && !w.Available)
                                        ? row with { State = FolderState.Partial, Skipped = row.Skipped + 1 } : row).ToArray();
                            lock (_sync) if (version == _version) foreach (var pair in result) _current[pair.Key] = pair.Value;
                        }
                        cache.Flush(); cache.RefreshManaged(scan.Token);
                        lock (_sync)
                        {
                            if (version == _version)
                                foreach (var preference in preferences)
                                {
                                    var source = journal.GetValueOrDefault(preference.Root);
                                    var saved = cache.Saved(preference.Root);
                                    if (store != null && source?.Access != FolderIndexAccess.Ready)
                                        _current[preference.Root] = saved.Select(row => row with { State = row.Bytes.HasValue ? FolderState.Cached : FolderState.Unavailable }).ToArray();
                                    else if (measure) _current[preference.Root] = saved;
                                    if (store != null)
                                    {
                                        var progress = cache.Progress(preference.Root);
                                        _status[preference.Root] = new(source?.Access ?? FolderIndexAccess.Ready, progress.Directories, progress.Files,
                                            source?.Error is > 0 ? source.Error.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                                    }
                                }
                            continueWork = cache.HasWorkFor(paths) || allowed.Any(p => cache.Saved(p.Root).Any(r => r.State == FolderState.Scanning));
                        }
                        Changed?.Invoke();
                    }
                    catch (OperationCanceledException) when (scan.IsCancellationRequested) { lock (_sync) _needsMeasurement = true; cache.Flush(); }
                    finally
                    {
                        cache.Checkpoint = null;
                        lock (_sync) { _measuringRoot = null; _scan = null; scan.Dispose(); }
                        Changed?.Invoke();
                    }
                    var signaled = await _changed.WaitAsync(TimeSpan.FromSeconds(continueWork ? 1 : 60), _stop.Token);
                    retry = !signaled && !continueWork;
                    if (signaled) await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
                {
                    indexFaulted = true;
                    foreach (var watch in watches) watch.Dispose(); watches.Clear();
                    PublishFailure(preferences, error);
                    await _changed.WaitAsync(_stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            foreach (var watch in watches) watch.Dispose();
            if (indexFaulted) store?.Discard();
            else
            {
                try { cache.Flush(true); store?.Dispose(); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { PublishFailure(_configuration, error); store?.Discard(); }
            }
        }
    }
    private void PublishFailure(DrivePreference[] preferences, Exception error)
    {
        lock (_sync)
        {
            _measuringRoot = null;
            foreach (var preference in preferences)
            {
                _status[preference.Root] = new(FolderIndexAccess.Unavailable, Problem: error.Message);
                _current[preference.Root] = _current.GetValueOrDefault(preference.Root)?.Select(r => r with { State = r.Bytes.HasValue ? FolderState.Cached : FolderState.Unavailable }).ToArray() ?? Array.Empty<FolderReading>();
            }
        }
        Changed?.Invoke();
    }
    public async ValueTask DisposeAsync()
    { _stop.Cancel(); if (_run != null) await _run; _changed.Dispose(); _stop.Dispose(); }
}
