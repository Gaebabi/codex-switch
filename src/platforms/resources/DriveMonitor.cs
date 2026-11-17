using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record DriveReading(string Root, string Label, long? Free, long? Total, TemperatureReading? Temperature = null, DiskIoReading? Io = null, FolderReading[]? Folders = null, bool MeasuringFolders = false, FolderIndexStatus? Index = null)
{
    public double? FreePercent => Free.HasValue && Total > 0 ? 100.0 * Free.Value / Total.Value : null;
}
internal sealed class DriveMonitor : IAsyncDisposable
{
    internal static StringComparer RootComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Func<string, TemperatureReading> _temperature;
    private readonly FolderMonitor _folders;
    public DriveMonitor(Func<string, TemperatureReading>? temperature = null, string? indexDirectory = null)
    {
        _folders = new(indexDirectory);
        _temperature = temperature ?? (_ => TemperatureReading.Missing(TemperatureState.PlatformUnsupported));
        _folders.Changed += () => Changed?.Invoke();
    }
    private DrivePreference[] _configuration = Array.Empty<DrivePreference>();
    private DriveReading[] _current = Array.Empty<DriveReading>();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private Task? _run;
    public event Action? Changed;
    public DriveReading[] Current => Volatile.Read(ref _current).Select(r => r with { Folders = _folders.Read(r.Root), MeasuringFolders = _folders.IsMeasuringRoot(r.Root), Index = _folders.IndexStatus(r.Root) }).ToArray();
    public void Configure(DrivePreference[] preferences)
    {
        var comparison = RootComparer;
        if (preferences.Any(p => OperatingSystem.IsWindows() ? p.Root.Length != 3 || !char.IsAsciiLetter(p.Root[0]) || p.Root[1..] != ":\\" :
            !Path.IsPathFullyQualified(p.Root) || Path.GetFullPath(p.Root) != p.Root) || preferences.Select(p => p.Root).Distinct(comparison).Count() != preferences.Length)
            throw new InvalidDataException("드라이브 설정이 올바르지 않습니다.");
        var configuration = preferences.Select(p => new DrivePreference { Root = OperatingSystem.IsWindows() ? p.Root.ToUpperInvariant() : p.Root, Label = p.Label, Folders = FolderUsage.Validate(p.Root, p.Folders) }).ToArray();
        Volatile.Write(ref _configuration, configuration); _folders.Configure(configuration);
        if (_changed.CurrentCount == 0) _changed.Release();
    }
    public void Start() { _folders.Start(); _run ??= Task.Run(RunAsync); }
    public void SetFolderMeasurementActive(bool active) => _folders.SetActive(active);
    internal void RefreshFolderIndex(string root, bool rebuild = false)
    {
        if (!_configuration.Any(p => RootComparer.Equals(p.Root, root))) throw new ArgumentException("Unknown folder-index drive.");
        _folders.RefreshIndex(root, rebuild);
    }
    private async Task RunAsync()
    {
        using var io = OperatingSystem.IsWindows() ? new WindowsDiskIoSampler() : null;
        var cache = new Dictionary<string, (DriveReading Reading, DateTimeOffset Next)>(RootComparer);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var configuration = Volatile.Read(ref _configuration); var now = DateTimeOffset.UtcNow;
                foreach (var removed in cache.Keys.Except(configuration.Select(p => p.Root), RootComparer).ToArray()) cache.Remove(removed);
                var throughput = io?.Sample(configuration.Select(p => p.Root).ToArray());
                var readings = configuration.Select(preference =>
                {
                    if (!cache.TryGetValue(preference.Root, out var cached) || now >= cached.Next)
                    { cached = (Read(preference), now.AddSeconds(15)); cache[preference.Root] = cached; }
                    return cached.Reading with { Label = preference.Label, Io = throughput != null && throughput.TryGetValue(preference.Root, out var value) ? value : DiskIoReading.Missing(DiskIoState.PlatformUnsupported) };
                }).ToArray();
                Volatile.Write(ref _current, readings); Changed?.Invoke();
                await _changed.WaitAsync(TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 2 : 15), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private DriveReading Read(DrivePreference preference)
    {
        try
        {
            var drive = new DriveInfo(preference.Root);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network) || !drive.IsReady) return new(preference.Root, preference.Label, null, null);
            return new(preference.Root, preference.Label, drive.AvailableFreeSpace, drive.TotalSize, _temperature(preference.Root));
        }
        catch (IOException) { return new(preference.Root, preference.Label, null, null); }
        catch (UnauthorizedAccessException) { return new(preference.Root, preference.Label, null, null); }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); await _folders.DisposeAsync(); if (_run != null) await _run; _changed.Dispose(); _stop.Dispose(); }
}
