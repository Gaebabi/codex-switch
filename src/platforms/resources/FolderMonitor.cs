using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly HashSet<string> _refresh = new(DriveMonitor.RootComparer);
    private readonly HashSet<string> _checkRoots = new(DriveMonitor.RootComparer);
    private readonly HashSet<string> _changedItems = new(DriveMonitor.RootComparer);
    private readonly FolderScanBudget _budget = new();
    private readonly string? _indexDirectory;
    private readonly Func<string, FolderJournalStatus> _probeJournal;
    private FolderUsage.Cache _cache;
    private Task? _run;
    private long _journalProbes;
    private int _version;
    private bool _active = true, _needsMeasurement = true;
    private string? _measuringRoot;
    internal FolderMonitor(string? indexDirectory = null, Func<string, FolderJournalStatus>? probeJournal = null)
    { _indexDirectory = indexDirectory; _probeJournal = probeJournal ?? WindowsFolderJournal.Probe; _cache = new(_budget); }
    internal (long Roots, long Directories) QueryCounts { get { var cache = Volatile.Read(ref _cache); return (cache.RootReads, cache.DirectoryReads); } }
    internal long JournalProbes => Interlocked.Read(ref _journalProbes);
    internal bool IsMeasuring { get { lock (_sync) return _scan != null; } }
    internal bool IsMeasuringRoot(string root) { lock (_sync) return _active && DriveMonitor.RootComparer.Equals(root, _measuringRoot); }
    internal FolderIndexStatus? IndexStatus(string root) { lock (_sync) return _status.GetValueOrDefault(root); }
    public event Action? Changed;
    public FolderReading[] Read(string root) { lock (_sync) return _current.GetValueOrDefault(root) ?? Array.Empty<FolderReading>(); }
    public void Configure(DrivePreference[] preferences)
    {
        lock (_sync)
        {
            _scan?.Cancel(); _configuration = preferences; _version++; _dirty.Clear(); _reset.Clear(); _refresh.Clear(); _rebuild.Clear(); _checkRoots.Clear(); _changedItems.Clear(); _needsMeasurement = true;
            var previous = _current;
            _current = preferences.ToDictionary(p => p.Root, p => (p.Folders ?? Array.Empty<FolderPreference>()).Select(folder =>
            {
                var saved = previous.GetValueOrDefault(p.Root)?.FirstOrDefault(row => DriveMonitor.RootComparer.Equals(row.Path, folder.Path));
                return saved?.Bytes != null ? saved with { Path = folder.Path, Color = folder.Color, State = FolderState.Scanning }
                    : new FolderReading(folder.Path, folder.Color, null, FolderState.Scanning);
            }).ToArray(), DriveMonitor.RootComparer);
            Signal();
        }
    }
    internal void RefreshIndex(string root, bool rebuild)
    {
        lock (_sync) { (rebuild ? _rebuild : _refresh).Add(root); Signal(); }
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
    private void Invalidate(int version, string path, FolderChange change)
    {
        lock (_sync)
        {
            if (version != _version || _stop.IsCancellationRequested) return;
            var pending = change switch
            {
                FolderChange.ItemChanged => _changedItems,
                FolderChange.RootMetadata => _checkRoots,
                FolderChange.Reset => _reset,
                _ => _dirty
            };
            pending.Add(path); if (_active) Signal();
        }
    }
    private async Task RunAsync()
    {
        var watches = new List<FolderChanges>(); FolderIndexStore? store = null;
        var journal = new Dictionary<string, FolderJournalStatus>(DriveMonitor.RootComparer);
        var lastCheckpoint = Stopwatch.GetTimestamp();
        var loadManaged = true;
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
                        foreach (var watch in watches) watch.Dispose(); watches.Clear(); cache.Flush(); journal.Clear();
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
                        cache.Configure(preferences); Volatile.Write(ref _cache, cache); cachedVersion = version; loadManaged = true;
                        foreach (var folder in preferences.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()))
                            watches.Add(new(folder.Path, (path, change) => Invalidate(version, path, change), cache.Owns));
                        lock (_sync)
                            if (version == _version)
                                _current = preferences.ToDictionary(p => p.Root, p => cache.Saved(p.Root).Select(r => store != null && r.Bytes.HasValue ? r with { State = FolderState.Cached } : r).ToArray(), DriveMonitor.RootComparer);
                        Changed?.Invoke();
                    }
                    if (!active) { await _changed.WaitAsync(_stop.Token); continue; }
                    string[] dirty, reset, rebuild, refresh, checkRoots, changedItems; bool pending; CancellationTokenSource scan;
                    lock (_sync)
                    {
                        if (!_active || version != _version) continue;
                        _changed.Wait(0); pending = _needsMeasurement; _needsMeasurement = false;
                        dirty = _dirty.ToArray(); reset = _reset.ToArray(); rebuild = _rebuild.ToArray(); refresh = _refresh.ToArray();
                        checkRoots = _checkRoots.ToArray(); _checkRoots.Clear();
                        changedItems = _changedItems.ToArray(); _changedItems.Clear();
                        _dirty.Clear(); _reset.Clear(); _rebuild.Clear(); _refresh.Clear();
                        _scan = scan = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    }
                    var continueWork = false;
                    var appliedChanges = false;
                    try
                    {
                        var recheck = reset.Select(path => Path.GetPathRoot(path)!).Concat(rebuild).Concat(refresh).ToHashSet(DriveMonitor.RootComparer);
                        var changedDirectories = dirty.ToHashSet(DriveMonitor.RootComparer);
                        // Windows can emit Changed for directory metadata during enumeration. Only structural
                        // directory events and changes to non-directory entries invalidate a listing.
                        foreach (var item in changedItems)
                            if (!cache.KnownDirectory(item) && Path.GetDirectoryName(item) is string directory) changedDirectories.Add(directory);
                        var changed = changedDirectories.Select(path => Path.GetPathRoot(path)!).ToHashSet(DriveMonitor.RootComparer);
                        var affected = changed.Concat(recheck).Concat(checkRoots.Select(path => Path.GetPathRoot(path)!)).ToHashSet(DriveMonitor.RootComparer);
                        foreach (var watch in watches)
                        {
                            scan.Token.ThrowIfCancellationRequested();
                            if (!watch.Available && (loadManaged || pending || retry || reset.Contains(watch.Path, DriveMonitor.RootComparer)
                                || refresh.Contains(Path.GetPathRoot(watch.Path)!, DriveMonitor.RootComparer))) _budget.Query(watch.Connect, scan.Token);
                        }
                        if (loadManaged) { cache.RefreshManaged(scan.Token); loadManaged = false; }
                        // Establish the watchers before reading the cursor so work arriving after it remains observable.
                        if (store != null)
                            foreach (var drive in preferences.Where(p => p.Folders?.Length > 0))
                            {
                                if (journal.TryGetValue(drive.Root, out var known) && !recheck.Contains(drive.Root) &&
                                    !(known.Access == FolderIndexAccess.Ready && changed.Contains(drive.Root)) &&
                                    !(retry && known.Access == FolderIndexAccess.Unavailable)) continue;
                                var status = _budget.Query(() =>
                                {
                                    Interlocked.Increment(ref _journalProbes);
                                    return _probeJournal(drive.Root);
                                }, scan.Token);
                                var progress = cache.Progress(drive.Root);
                                lock (_sync) if (version == _version) _status[drive.Root] = new(status.Access, progress.Directories, progress.Files,
                                    status.Error > 0 ? status.Error.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                                Changed?.Invoke();
                                if (status.Access == FolderIndexAccess.Ready)
                                {
                                    if (cache.Cursors.TryGetValue(drive.Root, out var saved) && WindowsFolderJournal.Covers(status, saved))
                                        cache.SaveCursor(drive.Root, WindowsFolderJournal.Replay(drive.Root, status, saved, _budget, change => cache.Invalidate(drive.Root, change), scan.Token));
                                    else
                                    {
                                        foreach (var folder in drive.Folders!) if (cache.HasSavedIndex(folder.Path)) cache.Reset(folder.Path);
                                        cache.SaveCursor(drive.Root, new(status.JournalId, status.NextUsn));
                                    }
                                }
                                journal[drive.Root] = status;
                            }
                        foreach (var path in changedDirectories)
                            if (store == null || !journal.TryGetValue(Path.GetPathRoot(path)!, out var source) || source.Access != FolderIndexAccess.Ready) cache.Invalidate(path);
                        foreach (var path in reset)
                            if (watches.Where(w => DriveMonitor.RootComparer.Equals(w.Path, path)).Any(w => w.TakeIdentityChange()) ||
                                store == null || !journal.TryGetValue(Path.GetPathRoot(path)!, out var source) || source.Access != FolderIndexAccess.Ready) cache.Reset(path);
                        foreach (var root in rebuild)
                            foreach (var folder in preferences.Where(p => DriveMonitor.RootComparer.Equals(p.Root, root)).SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>())) cache.Reset(folder.Path);
                        foreach (var path in checkRoots) cache.CheckRootOnRead(path);
                        appliedChanges = true;
                        if (retry) cache.RetryPartial();
                        var allowed = preferences.Where(p => store == null || !journal.TryGetValue(p.Root, out var access) || access.Access == FolderIndexAccess.Ready ||
                            access.Access == FolderIndexAccess.Unsupported && (affected.Contains(p.Root) ||
                                p.Folders!.Any(f => !cache.HasSavedIndex(f.Path)) || cache.HasWorkFor(p.Folders!.Select(f => f.Path)))).ToArray();
                        var allowedRoots = allowed.Select(p => p.Root).ToHashSet(DriveMonitor.RootComparer);
                        var paths = allowed.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()).Select(p => p.Path).ToArray();
                        var measure = pending || affected.Count > 0 || cache.HasWorkFor(paths) || retry && allowed.Any(p => cache.Saved(p.Root).Any(r => r.State == FolderState.Partial));
                        if (measure && paths.Length > 0)
                        {
                            FolderUsage.Scan(allowed, scan.Token, measured: (root, reading) =>
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
                        }
                        continueWork = cache.HasWorkFor(paths) || allowed.Any(p => cache.Saved(p.Root).Any(r => r.State == FolderState.Scanning));
                        if (store?.HasChanges == true && (!continueWork || Stopwatch.GetElapsedTime(lastCheckpoint) >= TimeSpan.FromSeconds(1)))
                        {
                            cache.Flush(); cache.RefreshManaged(scan.Token); lastCheckpoint = Stopwatch.GetTimestamp();
                        }
                        lock (_sync)
                        {
                            if (version == _version)
                                foreach (var preference in preferences)
                                {
                                    var source = journal.GetValueOrDefault(preference.Root);
                                    var saved = cache.Saved(preference.Root);
                                    if (store != null && source?.Access != FolderIndexAccess.Ready)
                                        _current[preference.Root] = saved.Select(row => row.State == FolderState.Scanning && allowedRoots.Contains(preference.Root) ? row
                                            : row with { State = row.Bytes.HasValue ? FolderState.Cached : row.State == FolderState.Scanning ? FolderState.Unavailable : row.State }).ToArray();
                                    else if (measure) _current[preference.Root] = saved.Select(row =>
                                        row.State == FolderState.Available && watches.Any(w => DriveMonitor.RootComparer.Equals(w.Path, row.Path) && !w.Available)
                                            ? row with { State = FolderState.Partial, Skipped = row.Skipped + 1 } : row).ToArray();
                                    if (store != null)
                                    {
                                        var progress = cache.Progress(preference.Root);
                                        _status[preference.Root] = new(source?.Access ?? FolderIndexAccess.Ready, progress.Directories, progress.Files,
                                            source?.Error is > 0 ? source.Error.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                                    }
                                }
                        }
                        Changed?.Invoke();
                    }
                    catch (OperationCanceledException) when (scan.IsCancellationRequested)
                    {
                        lock (_sync)
                        {
                            _needsMeasurement = true;
                            if (!appliedChanges && version == _version)
                            {
                                _dirty.UnionWith(dirty); _reset.UnionWith(reset); _rebuild.UnionWith(rebuild); _refresh.UnionWith(refresh);
                                _checkRoots.UnionWith(checkRoots); _changedItems.UnionWith(changedItems);
                            }
                        }
                        cache.Flush();
                    }
                    finally
                    {
                        lock (_sync) { _measuringRoot = null; _scan = null; scan.Dispose(); }
                        Changed?.Invoke();
                    }
                    var signaled = await _changed.WaitAsync(continueWork ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(60), _stop.Token);
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
