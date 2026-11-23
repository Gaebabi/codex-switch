using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CodexAccountDesk;

internal enum FolderChange { Entries, ItemChanged, RootMetadata, Reset }

internal sealed class FolderChanges : IDisposable
{
    private readonly Action<string, FolderChange> _changed;
    private readonly Func<string, bool> _ignore;
    private readonly List<FileSystemWatcher> _watchers = new();
    private volatile bool _retry = true;
    private volatile bool _watchingFolder;
    private int _identityChanged;
    internal bool TakeIdentityChange() => Interlocked.Exchange(ref _identityChanged, 0) != 0;
    internal string Path { get; }
    internal bool Available => !_retry;
    internal FolderChanges(string path, Action<string, FolderChange> changed, Func<string, bool> ignore)
    { Path = path; _changed = changed; _ignore = ignore; }
    internal bool Connect()
    {
        if (!_retry || !OperatingSystem.IsWindows()) return false;
        Dispose();
        _retry = false;
        try
        {
            var candidate = new DirectoryInfo(Path);
            for (var ancestor = candidate; ancestor != null; ancestor = ancestor.Parent)
                if (!ancestor.Exists || (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) candidate = ancestor.Parent;
            if (candidate == null) { _retry = true; return false; }
            var recursive = DriveMonitor.RootComparer.Equals(candidate.FullName, Path);
            _watchingFolder = recursive;
            const NotifyFilters rootChanges = NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.Security;
            if (recursive)
            {
                Add(candidate.FullName, true, NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite);
                Add(candidate.FullName, true, NotifyFilters.Attributes | NotifyFilters.Security, attributesOnly: true);
                if (candidate.Parent != null) Add(candidate.Parent.FullName, false, rootChanges);
            }
            else Add(candidate.FullName, false, rootChanges);
            return true;
        }
        catch (IOException) { _retry = true; Dispose(); return false; }
        catch (UnauthorizedAccessException) { _retry = true; Dispose(); return false; }
        catch (ArgumentException) { _retry = true; Dispose(); return false; }
    }
    private void Add(string scope, bool recursive, NotifyFilters filters, bool attributesOnly = false)
    {
        var watcher = new FileSystemWatcher(scope)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = filters,
            InternalBufferSize = 65536,
        };
        watcher.Changed += (_, e) => Notify(e.FullPath, recursive, changed: true, attributesOnly: attributesOnly);
        watcher.Created += (_, e) => Notify(e.FullPath, recursive);
        watcher.Deleted += (_, e) => Notify(e.FullPath, recursive);
        watcher.Renamed += (_, e) => { Notify(e.OldFullPath, recursive); Notify(e.FullPath, recursive); };
        watcher.Error += (_, _) => { _retry = true; _changed(Path, FolderChange.Reset); };
        _watchers.Add(watcher); watcher.EnableRaisingEvents = true;
    }
    private void Notify(string path, bool recursive, bool changed = false, bool attributesOnly = false)
    {
        if (_ignore(path)) return;
        if (!recursive)
        {
            var prefix = System.IO.Path.TrimEndingDirectorySeparator(path) + System.IO.Path.DirectorySeparatorChar;
            if (DriveMonitor.RootComparer.Equals(path, Path) || Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (changed && _watchingFolder) _changed(Path, FolderChange.RootMetadata);
                else { _retry = true; Interlocked.Exchange(ref _identityChanged, 1); _changed(Path, FolderChange.Reset); }
            }
            return;
        }
        if (changed && !attributesOnly) _changed(path, FolderChange.ItemChanged);
        else _changed(System.IO.Path.GetDirectoryName(path) ?? Path, FolderChange.Entries);
    }
    public void Dispose()
    {
        _watchingFolder = false;
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
