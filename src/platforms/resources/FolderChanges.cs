using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CodexAccountDesk;

internal sealed class FolderChanges : IDisposable
{
    private readonly Action<string, bool> _changed;
    private readonly Func<string, bool> _ignore;
    private readonly List<FileSystemWatcher> _watchers = new();
    private volatile bool _retry = true;
    private int _identityChanged;
    internal bool TakeIdentityChange() => Interlocked.Exchange(ref _identityChanged, 0) != 0;
    internal string Path { get; }
    internal bool Available => !_retry;
    internal FolderChanges(string path, Action<string, bool> changed, Func<string, bool>? ignore = null) { Path = path; _changed = changed; _ignore = ignore ?? (_ => false); }
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
            Add(candidate.FullName, recursive);
            if (recursive && candidate.Parent != null) Add(candidate.Parent.FullName, false);
            return true;
        }
        catch (IOException) { _retry = true; Dispose(); return false; }
        catch (UnauthorizedAccessException) { _retry = true; Dispose(); return false; }
        catch (ArgumentException) { _retry = true; Dispose(); return false; }
    }
    private void Add(string scope, bool recursive)
    {
        var watcher = new FileSystemWatcher(scope)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = recursive
                ? NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes | NotifyFilters.Security
                : NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.Security,
            InternalBufferSize = 65536,
        };
        watcher.Changed += (_, e) => Notify(e.FullPath, recursive);
        watcher.Created += (_, e) => Notify(e.FullPath, recursive);
        watcher.Deleted += (_, e) => Notify(e.FullPath, recursive);
        watcher.Renamed += (_, e) => { Notify(e.OldFullPath, recursive); Notify(e.FullPath, recursive); };
        watcher.Error += (_, _) => { _retry = true; _changed(Path, true); };
        _watchers.Add(watcher); watcher.EnableRaisingEvents = true;
    }
    private void Notify(string path, bool recursive)
    {
        if (_ignore(path)) return;
        if (!recursive)
        {
            var prefix = System.IO.Path.TrimEndingDirectorySeparator(path) + System.IO.Path.DirectorySeparatorChar;
            if (DriveMonitor.RootComparer.Equals(path, Path) || Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { _retry = true; Interlocked.Exchange(ref _identityChanged, 1); _changed(Path, true); }
            return;
        }
        var directory = System.IO.Path.GetDirectoryName(path);
        _changed(directory ?? Path, false);
    }
    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
