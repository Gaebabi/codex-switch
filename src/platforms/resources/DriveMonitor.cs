using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record DriveReading(string Root, string Label, long? Free, long? Total, TemperatureReading? Temperature = null)
{
    public double? FreePercent => Free.HasValue && Total > 0 ? 100.0 * Free.Value / Total.Value : null;
}
internal sealed class DriveMonitor : IAsyncDisposable
{
    private readonly Func<string, TemperatureReading> _temperature;
    public DriveMonitor(Func<string, TemperatureReading>? temperature = null) => _temperature = temperature ?? (_ => TemperatureReading.Missing(TemperatureState.PlatformUnsupported));
    private DrivePreference[] _configuration = Array.Empty<DrivePreference>();
    private DriveReading[] _current = Array.Empty<DriveReading>();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private Task? _run;
    public event Action? Changed;
    public DriveReading[] Current => Volatile.Read(ref _current);
    public void Configure(DrivePreference[] preferences)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (preferences.Any(p => OperatingSystem.IsWindows() ? p.Root.Length != 3 || !char.IsAsciiLetter(p.Root[0]) || p.Root[1..] != ":\\" :
            !Path.IsPathFullyQualified(p.Root) || Path.GetFullPath(p.Root) != p.Root) || preferences.Select(p => p.Root).Distinct(comparison).Count() != preferences.Length)
            throw new InvalidDataException("드라이브 설정이 올바르지 않습니다.");
        Volatile.Write(ref _configuration, preferences.Select(p => new DrivePreference { Root = OperatingSystem.IsWindows() ? p.Root.ToUpperInvariant() : p.Root, Label = p.Label }).ToArray());
        if (_changed.CurrentCount == 0) _changed.Release();
    }
    public void Start() { _run ??= Task.Run(RunAsync); }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var readings = Volatile.Read(ref _configuration).Select(Read).ToArray();
                Volatile.Write(ref _current, readings); Changed?.Invoke();
                await _changed.WaitAsync(TimeSpan.FromSeconds(15), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private DriveReading Read(DrivePreference preference)
    {
        try
        {
            var drive = new DriveInfo(preference.Root);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) return new(preference.Root, preference.Label, null, null);
            return new(preference.Root, preference.Label, drive.AvailableFreeSpace, drive.TotalSize, _temperature(preference.Root));
        }
        catch (IOException) { return new(preference.Root, preference.Label, null, null); }
        catch (UnauthorizedAccessException) { return new(preference.Root, preference.Label, null, null); }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); if (_run != null) await _run; _changed.Dispose(); _stop.Dispose(); }
}
