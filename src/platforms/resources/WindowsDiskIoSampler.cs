using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal sealed class WindowsDiskIoSampler : IDisposable
{
    private IntPtr _query;
    private string[] _roots = Array.Empty<string>();
    private readonly Dictionary<string, IntPtr[]> _counters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiskIoHistory> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiskIoProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _profileRefresh;
    private int _collections;
    private uint _status;
    public IReadOnlyDictionary<string, DiskIoReading> Sample(string[] roots)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows disk I/O requires Windows.");
        if (roots.Any(root => root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1..] != ":\\")) throw new ArgumentException("Invalid volume root.");
        var names = roots.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!_roots.SequenceEqual(names, StringComparer.OrdinalIgnoreCase)) Configure(names);
        var at = DateTimeOffset.UtcNow; var result = new Dictionary<string, DiskIoReading>(StringComparer.OrdinalIgnoreCase);
        if (names.Length == 0) return result;
        var collected = _status == 0 ? PdhCollectQueryData(_query) : _status;
        if (collected == 0) _collections++; else _collections = 0;
        if (at >= _profileRefresh)
        { foreach (var root in names) _profiles[root] = WindowsDiskCapabilities.Read(root); _profileRefresh = at.AddSeconds(60); }
        foreach (var root in names)
        {
            var profile = _profiles.GetValueOrDefault(root);
            if (collected != 0 || !_counters.TryGetValue(root, out var counters))
            { result[root] = _history[root].Observe(DiskIoReading.Missing(collected == 5 ? DiskIoState.AccessDenied : DiskIoState.Unavailable, profile)); continue; }
            if (_collections < 2) { result[root] = _history[root].Observe(DiskIoReading.Missing(DiskIoState.Pending, profile)); continue; }
            var values = counters.Select(Read).ToArray();
            if (values[0] == null || values[1] == null) { result[root] = _history[root].Observe(DiskIoReading.Missing(DiskIoState.Unavailable, profile)); continue; }
            var idle = values[4];
            result[root] = _history[root].Observe(new(at, DiskIoState.Available, profile,
                new(values[0], values[2] * 1000), new(values[1], values[3] * 1000), idle is >= 0 and <= 100 ? 100 - idle : null));
        }
        return result;
    }
    private void Configure(string[] roots)
    {
        Dispose(); _roots = roots; _collections = 0; _counters.Clear(); _history.Clear(); _profiles.Clear(); _profileRefresh = default;
        _status = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        foreach (var root in roots)
        {
            _history[root] = new(); if (_status != 0) continue;
            var fields = new[] { "Disk Read Bytes/sec", "Disk Write Bytes/sec", "Avg. Disk sec/Read", "Avg. Disk sec/Write", "% Idle Time" };
            _counters[root] = fields.Select(field => PdhAddEnglishCounterW(_query, @"\LogicalDisk(" + root[..2] + @")\" + field, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero).ToArray();
        }
    }
    private static double? Read(IntPtr counter)
    {
        if (counter == IntPtr.Zero || PdhGetFormattedCounterValue(counter, 0x200, out _, out var value) != 0 || value.Status > 1 || !double.IsFinite(value.Value) || value.Value < 0) return null;
        return value.Value;
    }
    public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
