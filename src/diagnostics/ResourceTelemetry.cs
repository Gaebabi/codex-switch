using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexSwitch.Diagnostics;

internal sealed class ResourceTelemetry(Process process, Action<object> emit, string processRole = "native_engine") : IAsyncDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessIoCounters(IntPtr handle, out IoCounters counters);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    private readonly CancellationTokenSource _stop = new(); private Task? _task;
    internal void Start() => _task = Sample();
    private async Task Sample()
    {
        if (!OperatingSystem.IsWindows()) { emit(new { kind = "resource_unavailable", reason = "windows_counters_required" }); return; }
        IntPtr query = IntPtr.Zero;
        try
        {
            var counters = new Dictionary<string, IntPtr>();
            var opened = PdhOpenQuery(null, IntPtr.Zero, out query);
            var paths = new Dictionary<string, string> { ["readBytesPerSecond"] = "Disk Read Bytes/sec", ["writeBytesPerSecond"] = "Disk Write Bytes/sec", ["queueLength"] = "Current Disk Queue Length", ["readLatencySeconds"] = "Avg. Disk sec/Read", ["writeLatencySeconds"] = "Avg. Disk sec/Write" };
            if (opened == 0)
                foreach (var path in paths) if (PdhAddEnglishCounter(query, "\\PhysicalDisk(_Total)\\" + path.Value, IntPtr.Zero, out var counter) == 0) counters[path.Key] = counter;
            emit(new { kind = "resource_scope", pid = process.Id, processRole, samplingMs = 250, processIoScope = "all_process_io_including_pipes_not_physical_disk", diskScope = "whole_machine_all_processes", availableDiskCounters = counters.Keys.ToArray(), diskOpenStatus = opened,
                exactProcessDiskWaitAvailable = false, reason = "Per-process physical disk waits require a separate kernel trace; counters cannot establish causation." });
            IoCounters? previous = null; TimeSpan? cpu = null; var last = Stopwatch.GetTimestamp();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                process.Refresh(); if (process.HasExited) return;
                var now = Stopwatch.GetTimestamp(); var interval = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds; last = now;
                var ioValid = GetProcessIoCounters(process.Handle, out var io); var cpuNow = process.TotalProcessorTime;
                var disk = new Dictionary<string, double?>();
                var collected = query != IntPtr.Zero ? PdhCollectQueryData(query) : uint.MaxValue;
                foreach (var counter in counters)
                    disk[counter.Key] = collected == 0 && PdhGetFormattedCounterValue(counter.Value, 0x200, out _, out var value) == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) ? value.Value : null;
                emit(new { kind = "resource_sample", pid = process.Id, intervalMs = interval, cpuMs = cpu.HasValue ? (double?)(cpuNow - cpu.Value).TotalMilliseconds : null,
                    workingSetBytes = process.WorkingSet64, processIoAvailable = ioValid,
                    readBytes = ioValid && previous.HasValue ? (ulong?)(io.ReadBytes - previous.Value.ReadBytes) : null,
                    writeBytes = ioValid && previous.HasValue ? (ulong?)(io.WriteBytes - previous.Value.WriteBytes) : null,
                    readOperations = ioValid && previous.HasValue ? (ulong?)(io.ReadOperations - previous.Value.ReadOperations) : null,
                    writeOperations = ioValid && previous.HasValue ? (ulong?)(io.WriteOperations - previous.Value.WriteOperations) : null, disk });
                previous = ioValid ? io : null; cpu = cpuNow;
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { emit(new { kind = "resource_unavailable", reason = error.GetType().Name }); }
        finally { if (query != IntPtr.Zero) PdhCloseQuery(query); }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); if (_task != null) await _task; _stop.Dispose(); }
}
