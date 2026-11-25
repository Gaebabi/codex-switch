using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CodexAccountDesk;

internal static partial class FolderUsage
{
    internal sealed class Cache
    {
        internal sealed record RootIdentity(uint Volume, ulong Id);
        internal sealed record AllocationValue(string Drive, uint Volume, ulong Id, long Bytes, string Directory);
        private readonly record struct FileKey(string Drive, uint Volume, ulong Id);
        private sealed class IndexedFile
        {
            public long Bytes; public string Directory = "";
            public string? Owner; public int Count; public Dictionary<string, int>? Others;
            public string? FirstDirectory; public HashSet<string>? OtherDirectories;
        }
        internal sealed class Work
        {
            internal readonly Stack<string> Pending = new();
            internal readonly HashSet<string> Seen = new(DriveMonitor.RootComparer);
            internal readonly uint Volume;
            internal int Skipped; internal bool Partial, Invalidated;
            internal Work(string path, uint volume) { Pending.Push(path); Volume = volume; }
        }
        private readonly FolderScanBudget? _budget;
        private readonly FolderIndexStore? _store;
        private bool _loaded, _loading;
        private int _writes;
        private DrivePreference[] _preferences = Array.Empty<DrivePreference>();
        private readonly Dictionary<string, int> _ranks = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, FolderEntry[]> _directories = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, string> _directoryNames = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, RootIdentity> _roots = new(DriveMonitor.RootComparer);
        private readonly Dictionary<FileKey, IndexedFile> _files = new();
        private readonly HashSet<FileKey> _managedFiles = new();
        private readonly Dictionary<FileKey, string> _directoryIds = new();
        private readonly HashSet<string> _knownDirectories = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, HashSet<string>> _byFolder = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, long> _bytes = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, long> _fileCounts = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, Work> _work = new(DriveMonitor.RootComparer);
        private readonly HashSet<string> _dirty = new(DriveMonitor.RootComparer), _reset = new(DriveMonitor.RootComparer), _failed = new(DriveMonitor.RootComparer), _trusted = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, FolderReading> _readings = new(DriveMonitor.RootComparer);
        internal readonly Dictionary<string, FolderJournalCursor> Cursors = new(DriveMonitor.RootComparer);
        private readonly Dictionary<string, FolderJournalCursor> _persistedCursors = new(DriveMonitor.RootComparer);
        internal int DirectoriesPerSlice { get; set; } = int.MaxValue;
        internal long DirectoryReads { get; private set; }
        internal long RootReads { get; private set; }
        internal Cache(FolderScanBudget? budget = null, FolderIndexStore? store = null) { _budget = budget; _store = store; }
        internal T Query<T>(Func<T> query, CancellationToken cancellation)
        { if (_budget != null) return _budget.Query(query, cancellation); cancellation.ThrowIfCancellationRequested(); return query(); }
        internal void Configure(DrivePreference[] preferences)
        {
            _preferences = preferences;
            foreach (var folder in preferences.SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()))
                if (!_ranks.ContainsKey(folder.Path)) { _ranks.Add(folder.Path, _ranks.Count); _byFolder[folder.Path] = new(DriveMonitor.RootComparer); _bytes[folder.Path] = 0; _fileCounts[folder.Path] = 0; }
            if (_loaded || _store == null) return;
            _loaded = _loading = true;
            try
            {
                foreach (var row in _store.Read<RootIdentity>(FolderIndexStore.RootKind))
                    if (_ranks.ContainsKey(row.Key)) Root(row.Key, row.Value.Volume, row.Value.Id);
                foreach (var row in _store.Read<FolderEntry[]>(FolderIndexStore.DirectoryKind))
                    if (Folder(row.Key) != null) SetDirectory(row.Key, row.Value);
                foreach (var row in _store.Read<AllocationValue>(FolderIndexStore.AllocationKind))
                {
                    var v = row.Value; var key = new FileKey(v.Drive, v.Volume, v.Id);
                    if (_files.ContainsKey(key)) Allocation(key, v.Bytes, v.Directory);
                }
                foreach (var row in _store.Read<FolderReading>(FolderIndexStore.ReadingKind))
                    if (_ranks.ContainsKey(row.Key)) _readings[row.Key] = row.Value;
                foreach (var row in _store.Read<bool>(FolderIndexStore.DirtyKind)) if (Folder(row.Key) != null) _dirty.Add(row.Key);
                foreach (var row in _store.Read<bool>(FolderIndexStore.ResetKind)) if (Folder(row.Key) != null) _reset.Add(row.Key);
                foreach (var row in _store.Read<FolderJournalCursor>(FolderIndexStore.JournalKind)) Cursors[row.Key] = _persistedCursors[row.Key] = row.Value;
            }
            finally { _loading = false; }
        }
        private string? Folder(string path) => _ranks.Keys.FirstOrDefault(root => DriveMonitor.RootComparer.Equals(path, root) || Inside(root, path));
        internal bool Owns(string path) => _store?.Owns(path) == true;
        internal string? ManagedDirectory => _store?.DirectoryPath;
        private void Put<T>(int kind, string key, T value)
        { if (!_loading) _store?.Put(kind, key, value); }
        private void Delete(int kind, string key) { if (!_loading) _store?.Delete(kind, key); }
        internal FolderReading[] Saved(string root) => (_preferences.FirstOrDefault(p => DriveMonitor.RootComparer.Equals(root, p.Root))?.Folders ?? Array.Empty<FolderPreference>())
            .Select(f => _readings.TryGetValue(f.Path, out var row) ? row with { Color = f.Color } : new(f.Path, f.Color, null, FolderState.Scanning)).ToArray();
        internal (long Directories, long Files) Progress(string root)
        {
            var folders = _preferences.Where(p => DriveMonitor.RootComparer.Equals(p.Root, root)).SelectMany(p => p.Folders ?? Array.Empty<FolderPreference>()).Select(p => p.Path).ToHashSet(DriveMonitor.RootComparer);
            return (_byFolder.Where(p => folders.Contains(p.Key)).Sum(p => (long)p.Value.Count), _fileCounts.Where(p => folders.Contains(p.Key)).Sum(p => p.Value));
        }
        internal bool HasWork => _work.Count != 0 || _dirty.Count != 0 || _reset.Count != 0;
        internal bool HasSavedIndex(string folder) => _roots.ContainsKey(folder) || _readings.ContainsKey(folder);
        internal bool HasWorkFor(IEnumerable<string> folders) => folders.Any(folder => _work.ContainsKey(folder) || _dirty.Concat(_reset).Any(path => DriveMonitor.RootComparer.Equals(folder, path) || Inside(folder, path) || Inside(path, folder)));
        internal void RefreshManaged(CancellationToken cancellation)
        {
            if (_store == null || Folder(_store.DirectoryPath) == null) return;
            var path = _store.DirectoryPath;
            var folder = Folder(path)!;
            if (!_roots.TryGetValue(folder, out var identity)) return;
            var information = Query(() =>
            {
                using var handle = CreateFileW(NativePath(path), 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var value) || (value.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The folder-index directory identity cannot be read.");
                return value;
            }, cancellation);
            _directoryIds[new(Path.GetPathRoot(path)!.ToUpperInvariant(), information.Volume, ((ulong)information.IdHigh << 32) | information.IdLow)] = path;
            _knownDirectories.Add(path); _managedFiles.Clear();
            var seen = new HashSet<(uint Volume, ulong Id)>();
            var fresh = FolderUsage.Entries(new DirectoryInfo(path), identity.Volume, cancellation, this).Select(entry =>
            {
                if (_store.Owns(entry.Path)) _managedFiles.Add(Key(path, entry));
                return Resolve(entry, path, seen, cancellation);
            }).ToArray();
            SetDirectory(path, fresh);
            foreach (var row in _readings.Values.Where(r => r.Bytes.HasValue).ToArray()) _readings[row.Path] = row with { Bytes = Bytes(row.Path) };
        }
        internal void BeginScan() { }
        internal bool TryReuse(string folder, out FolderReading reading)
        {
            return _readings.TryGetValue(folder, out reading!) && reading.State != FolderState.Scanning && !_work.ContainsKey(folder) &&
                _trusted.Contains(folder) && !_dirty.Concat(_reset).Any(path => DriveMonitor.RootComparer.Equals(path, folder) || Inside(folder, path) || Inside(path, folder));
        }
        internal void Begin(string folder) { RootReads++; }
        internal Work? Pending(string folder) => _work.GetValueOrDefault(folder);
        internal bool NeedsRead(string path) => !_directories.ContainsKey(path) || _dirty.Contains(path);
        internal Work Start(string folder, uint volume) { var value = new Work(folder, volume); _work[folder] = value; return value; }
        internal long Bytes(string folder) => _bytes.GetValueOrDefault(folder);
        internal void Trust(string folder) => _trusted.Add(folder);
    internal bool NeedsRootCheck(string folder) => !_trusted.Contains(folder);
    internal void CheckRootOnRead(string folder) => _trusted.Remove(folder);
        internal string[] UntrustedRoots => _roots.Keys.Where(p => !_trusted.Contains(p)).ToArray();
        internal void Finish(string folder)
        {
            _work.Remove(folder); _trusted.Add(folder);
            foreach (var path in _reset.Where(p => DriveMonitor.RootComparer.Equals(p, folder) || Inside(folder, p)).ToArray())
            { _reset.Remove(path); Delete(FolderIndexStore.ResetKind, path); }
        }
        internal void Invalidate(string directory)
        {
            var path = Path.TrimEndingDirectorySeparator(directory);
            var folder = Folder(path);
            if (folder == null)
            {
                foreach (var root in _ranks.Keys.Where(p => Inside(path, p)).ToArray()) Reset(root);
                return;
            }
            while (!_directories.ContainsKey(path) && !DriveMonitor.RootComparer.Equals(path, folder)) path = Path.GetDirectoryName(path)!;
            _dirty.Add(path); Put(FolderIndexStore.DirtyKind, path, true);
        }
        internal void Invalidate(string drive, FolderJournalChange change)
        {
            drive = drive.ToUpperInvariant();
            if (_managedFiles.Contains(new(drive, change.Volume, change.FileId))) return;
            if (_directoryIds.TryGetValue(new(drive, change.Volume, change.ParentId), out var parent))
            {
                if (_store != null && DriveMonitor.RootComparer.Equals(parent, _store.DirectoryPath) &&
                    change.Name is "allocation.sqlite3" or "allocation.sqlite3-wal" or "allocation.sqlite3-shm") return;
                Invalidate(parent);
            }
            if (_directoryIds.TryGetValue(new(drive, change.Volume, change.FileId), out var directory))
            {
                if (_store != null && DriveMonitor.RootComparer.Equals(directory, _store.DirectoryPath) && (change.Reason & ~0x80008900u) == 0) return;
                if ((change.Attributes & FileAttributes.Directory) != 0 && (change.Reason & (0x200u | 0x800u | 0x1000u | 0x2000u | 0x100000u)) != 0) Reset(directory);
            }
            if (_files.TryGetValue(new(drive, change.Volume, change.FileId), out var file))
            {
                if (file.FirstDirectory != null) Invalidate(file.FirstDirectory);
                if (file.OtherDirectories != null) foreach (var alias in file.OtherDirectories) Invalidate(alias);
            }
        }
        internal void Reset(string path) => Reset(path, true);
        private void Reset(string path, bool invalidateWork)
        {
            _reset.Add(path); Put(FolderIndexStore.ResetKind, path, true);
            foreach (var directory in _directories.Keys.Where(p => DriveMonitor.RootComparer.Equals(p, path) || Inside(path, p)).ToArray()) RemoveDirectory(directory);
            foreach (var pair in _work.Where(p => DriveMonitor.RootComparer.Equals(p.Key, path) || Inside(p.Key, path) || Inside(path, p.Key)).ToArray())
                if (invalidateWork) { pair.Value.Invalidated = true; _work.Remove(pair.Key); }
                else pair.Value.Seen.RemoveWhere(p => DriveMonitor.RootComparer.Equals(path, p) || Inside(path, p));
        }
        private void RemoveDirectory(string path)
        {
            if (!_directories.Remove(path, out var entries)) return;
            RemoveReferences(path, entries); var folder = Folder(path); if (folder != null) _byFolder[folder].Remove(path);
            _dirty.Remove(path); _failed.Remove(path); Delete(FolderIndexStore.DirectoryKind, path); Delete(FolderIndexStore.DirtyKind, path);
            foreach (var id in _directoryIds.Where(p => DriveMonitor.RootComparer.Equals(p.Value, path) || Inside(path, p.Value)).Select(p => p.Key).ToArray())
            { _knownDirectories.Remove(_directoryIds[id]); _directoryIds.Remove(id); }
            Prune(entries, path);
        }
        internal void Root(string path, uint volume, ulong id)
        {
            if (_roots.TryGetValue(path, out var previous) && previous != new RootIdentity(volume, id)) Reset(path);
            _roots[path] = new(volume, id); _directoryIds[new(Path.GetPathRoot(path)!.ToUpperInvariant(), volume, id)] = path; _knownDirectories.Add(path);
            if (previous != _roots[path]) Put(FolderIndexStore.RootKind, path, _roots[path]);
        }
        internal void Visit(string folder, string directory) { }
        internal bool KnownDirectory(string path) => _knownDirectories.Contains(path);
        private FileKey Key(string directory, FolderEntry entry) => new(Path.GetPathRoot(directory)!.ToUpperInvariant(), entry.Volume, entry.Id);
        private void Allocation(FileKey key, long bytes, string directory)
        {
            if (bytes < 0) throw new InvalidDataException("Invalid folder-index allocation.");
            if (!_directoryNames.TryGetValue(directory, out var canonical)) _directoryNames[directory] = canonical = directory;
            directory = canonical;
            if (!_files.TryGetValue(key, out var file)) _files[key] = file = new();
            if (file.Owner != null) _bytes[file.Owner] = checked(_bytes[file.Owner] + (bytes - file.Bytes));
            file.Bytes = bytes; file.Directory = directory;
            if (!Owns(directory)) Put(FolderIndexStore.AllocationKind, key.Drive + key.Volume.ToString("X8") + key.Id.ToString("X16"), new AllocationValue(key.Drive, key.Volume, key.Id, bytes, directory));
        }
        private void Reference(IndexedFile file, string folder, int delta)
        {
            if (file.Owner != null) { _bytes[file.Owner] -= file.Bytes; _fileCounts[file.Owner]--; }
            if (file.Owner == null) { if (delta < 0) throw new InvalidDataException("Invalid folder-index reference."); file.Owner = folder; file.Count = delta; }
            else if (DriveMonitor.RootComparer.Equals(file.Owner, folder)) file.Count += delta;
            else { file.Others ??= new(DriveMonitor.RootComparer); var count = file.Others.GetValueOrDefault(folder) + delta; if (count == 0) file.Others.Remove(folder); else file.Others[folder] = count; }
            if (file.Count == 0) file.Owner = null;
            if (file.Others?.Count > 0)
            {
                var next = file.Others.Keys.MinBy(p => _ranks[p])!;
                if (file.Owner == null || _ranks[next] < _ranks[file.Owner])
                {
                    if (file.Owner != null) file.Others[file.Owner] = file.Count;
                    file.Owner = next; file.Count = file.Others[next]; file.Others.Remove(next);
                }
            }
            if (file.Owner != null) { _bytes[file.Owner] = checked(_bytes[file.Owner] + file.Bytes); _fileCounts[file.Owner]++; }
        }
        private void RemoveReferences(string directory, FolderEntry[] entries)
        {
            var folder = Folder(directory); if (folder == null) return;
            foreach (var entry in entries.Where(e => e.Resolved && !e.Failed)) Reference(_files[Key(directory, entry)], folder, -1);
            foreach (var key in entries.Where(e => e.Resolved && !e.Failed).Select(e => Key(directory, e)).Distinct())
            {
                var file = _files[key];
                if (DriveMonitor.RootComparer.Equals(file.FirstDirectory, directory))
                { file.FirstDirectory = file.OtherDirectories?.FirstOrDefault(); if (file.FirstDirectory != null) file.OtherDirectories!.Remove(file.FirstDirectory); }
                else file.OtherDirectories?.Remove(directory);
            }
        }
        private void SetDirectory(string directory, FolderEntry[] entries)
        {
            if (!Path.IsPathFullyQualified(directory) || !DriveMonitor.RootComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), directory))
                throw new InvalidDataException("Invalid folder-index directory path.");
            foreach (var entry in entries)
                if (entry.Resolved && (entry.Bytes is not >= 0 || entry.Id == 0) ||
                    (entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 &&
                    (!Path.IsPathFullyQualified(entry.Path) || !DriveMonitor.RootComparer.Equals(Path.GetDirectoryName(entry.Path), directory)))
                    throw new InvalidDataException("Invalid folder-index directory entry.");
            var folder = Folder(directory); if (folder == null) return;
            if (!_directoryNames.TryGetValue(directory, out var canonical)) _directoryNames[directory] = canonical = directory;
            directory = canonical;
            if (_directories.TryGetValue(directory, out var old)) RemoveReferences(directory, old);
            _directories[directory] = entries; _byFolder[folder].Add(directory);
            var failed = false;
            foreach (var entry in entries)
            {
                failed |= entry.Failed;
                if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory && entry.Id != 0)
                { _directoryIds[new(Path.GetPathRoot(directory)!.ToUpperInvariant(), entry.Volume, entry.Id)] = entry.Path; _knownDirectories.Add(entry.Path); }
                if (!entry.Resolved || entry.Failed) continue;
                var key = Key(directory, entry);
                if (!_files.TryGetValue(key, out var file)) _files[key] = file = new() { Bytes = entry.Bytes!.Value, Directory = directory };
                Reference(file, folder, 1);
                if (file.FirstDirectory == null) file.FirstDirectory = directory;
                else if (!DriveMonitor.RootComparer.Equals(file.FirstDirectory, directory)) (file.OtherDirectories ??= new(DriveMonitor.RootComparer)).Add(directory);
            }
            if (_dirty.Remove(directory)) Delete(FolderIndexStore.DirtyKind, directory);
            if (failed) _failed.Add(directory); else _failed.Remove(directory);
            if (!Owns(directory)) Put(FolderIndexStore.DirectoryKind, directory, entries);
            if (old != null) Prune(old, directory);
        }
        private void Prune(FolderEntry[] entries, string directory)
        {
            foreach (var key in entries.Where(e => e.Resolved && !e.Failed).Select(e => Key(directory, e)).Distinct())
                if (_files.TryGetValue(key, out var file) && file.Owner == null)
                { _files.Remove(key); Delete(FolderIndexStore.AllocationKind, key.Drive + key.Volume.ToString("X8") + key.Id.ToString("X16")); }
        }
        private FolderEntry Resolve(FolderEntry entry, string directory, HashSet<(uint Volume, ulong Id)> seen, CancellationToken cancellation)
        {
            if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return entry;
            try
            {
                var value = entry.Bytes.HasValue && (entry.Attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) == 0
                    ? (Bytes: entry.Bytes.Value, Volume: entry.Volume, Id: entry.Id, Links: 1u) : Query(() => FolderUsage.Allocation(entry.Path), cancellation);
                var key = new FileKey(Path.GetPathRoot(directory)!.ToUpperInvariant(), value.Volume, value.Id);
                if (!seen.Add((value.Volume, value.Id)) || _files.TryGetValue(key, out var previous) && !DriveMonitor.RootComparer.Equals(previous.Directory, directory))
                    value = Query(() => FolderUsage.Allocation(entry.Path), cancellation);
                Allocation(key, value.Bytes, directory);
                return entry with { Path = "", Bytes = value.Bytes, Volume = value.Volume, Id = value.Id, Resolved = true };
            }
            catch (IOException) { return entry with { Path = "", Failed = true }; }
            catch (UnauthorizedAccessException) { return entry with { Path = "", Failed = true }; }
        }
        internal IEnumerable<FolderEntry> Entries(DirectoryInfo directory, uint volume, CancellationToken cancellation)
        {
            var path = directory.FullName;
            if (_directories.TryGetValue(path, out var saved) && !_dirty.Contains(path)) return saved;
            DirectoryReads++;
            var seen = new HashSet<(uint Volume, ulong Id)>();
            var fresh = FolderUsage.Entries(directory, volume, cancellation, this).Select(entry => Resolve(entry, path, seen, cancellation)).ToArray();
            if (saved != null)
            {
                var children = fresh.Where(e => (e.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory).ToDictionary(e => e.Path, e => e.Id, DriveMonitor.RootComparer);
                foreach (var child in saved.Where(e => (e.Attributes & FileAttributes.Directory) != 0))
                    if (!children.TryGetValue(child.Path, out var id) || id == 0 || id != child.Id) Reset(child.Path, false);
            }
            SetDirectory(path, fresh);
            if (++_writes % 64 == 0) Flush();
            return fresh;
        }
        internal void FailDirectory(string path) { foreach (var child in _directories.Keys.Where(p => DriveMonitor.RootComparer.Equals(path, p) || Inside(path, p)).ToArray()) RemoveDirectory(child); _dirty.Remove(path); _failed.Add(path); }
        internal void RetryPartial()
        {
            foreach (var directory in _failed.ToArray()) Invalidate(directory);
            foreach (var row in _readings.Values.Where(r => r.State == FolderState.Partial))
                foreach (var path in _directoryIds.Values.Where(p => (DriveMonitor.RootComparer.Equals(p, row.Path) || Inside(row.Path, p)) && !_directories.ContainsKey(p)).ToArray()) Invalidate(path);
        }
        internal void Complete(DrivePreference[] preferences, Dictionary<string, FolderReading[]> readings, CancellationToken cancellation)
        {
            foreach (var root in readings.Keys.ToArray()) readings[root] = readings[root].Select(row =>
            {
                cancellation.ThrowIfCancellationRequested();
                var value = row.Bytes.HasValue ? row with { Bytes = Bytes(row.Path) } : row;
                if (!_readings.TryGetValue(value.Path, out var old) || value != old) Put(FolderIndexStore.ReadingKind, value.Path, value);
                _readings[value.Path] = value; return value;
            }).ToArray();
        }
        internal void SaveCursor(string root, FolderJournalCursor cursor)
        { Cursors[root] = cursor; }
        internal void Flush(bool includeCursor = false)
        {
            if (includeCursor && _store != null)
                foreach (var pair in Cursors)
                    if (!_persistedCursors.TryGetValue(pair.Key, out var saved) || saved != pair.Value) Put(FolderIndexStore.JournalKind, pair.Key, pair.Value);
            if (_store?.HasChanges != true) return;
            foreach (var pair in Cursors) Put(FolderIndexStore.JournalKind, pair.Key, pair.Value);
            _store.Flush();
            foreach (var pair in Cursors) _persistedCursors[pair.Key] = pair.Value;
        }
    }
}
