using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CodexSwitch.Sensors;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal sealed class WindowsCpuTemperature : IAsyncDisposable
{
    private CancellationTokenSource? _stop;
    private Task? _run;
    private Process? _worker;
    private TaskCompletionSource? _started;
    private bool _disposed;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private TemperatureReading _current = TemperatureReading.Missing(TemperatureState.Pending, CpuSensorProtocol.Source);
    public TemperatureReading Current => Volatile.Read(ref _current);
    public static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "sensors", "CodexSwitch.Sensors.exe");
    public void Start() { ObjectDisposedException.ThrowIf(_disposed, this); if (_run == null) StartWorker(false); }
    public async Task EnableAsync(CancellationToken cancellation)
        => await RestartAsync(true, cancellation);
    public async Task<TemperatureReading> ProbeAsync(CancellationToken cancellation)
    {
        await RestartAsync(false, cancellation); return Current;
    }
    private async Task RestartAsync(bool elevate, CancellationToken cancellation)
    {
        await _transition.WaitAsync(cancellation);
        try { ObjectDisposedException.ThrowIf(_disposed, this); await StopAsync(); cancellation.ThrowIfCancellationRequested(); StartWorker(elevate); await _started!.Task.WaitAsync(cancellation); }
        finally { _transition.Release(); }
    }
    private void StartWorker(bool elevate)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows CPU collection requires Windows.");
        _stop = new(); _started = new(TaskCreationOptions.RunContinuationsAsynchronously); Publish(TemperatureState.Pending);
        var token = _stop.Token; _run = Task.Run(() => RunAsync(elevate, token));
    }
    private async Task RunAsync(bool elevate, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(WorkerPath)) { Publish(TemperatureState.ProviderMissing, "thermal.cpu_bundle_missing"); return; }
            var digest = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "CodexSwitch.CpuSensorSha256")?.Value;
            if (string.IsNullOrEmpty(digest)) { Publish(TemperatureState.ProviderMissing, "thermal.cpu_bundle_missing"); return; }
            using var verified = new FileStream(WorkerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifyWorker(verified, digest);
            cancellation.ThrowIfCancellationRequested();
            var nonce = Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream("CodexSwitch.Cpu." + nonce, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var parent = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(WorkerPath) { UseShellExecute = elevate, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (elevate) start.Verb = "runas";
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add(nonce);
            _worker = Process.Start(start) ?? throw new IOException("CPU worker did not start.");
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(15)); await pipe.WaitForConnectionAsync(connect.Token);
            }
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client) || client != _worker.Id) throw new InvalidDataException("Unexpected CPU sensor process.");
            while (!cancellation.IsCancellationRequested)
            {
                using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellation); read.CancelAfter(TimeSpan.FromSeconds(8));
                var sample = await CpuSensorProtocol.ReadAsync(pipe, read.Token);
                var state = Enum.Parse<TemperatureState>(sample.State);
                var at = DateTimeOffset.FromUnixTimeMilliseconds(sample.At);
                var reading = state == TemperatureState.Available ? TemperatureReading.From(sample.Celsius!.Value, CpuSensorProtocol.Source, sample.Sensor, at) :
                    TemperatureReading.Missing(state, CpuSensorProtocol.Source, code: state switch { TemperatureState.ProviderMissing => "thermal.cpu_setup_required", TemperatureState.AccessDenied => "thermal.cpu_enable_required", _ => sample.Error.HasValue ? "Win32 " + sample.Error.Value : null }, at: at);
                Volatile.Write(ref _current, reading);
                _started!.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (OperationCanceledException) { Publish(TemperatureState.ReadFailed); }
        catch (Win32Exception error) { Publish(error.NativeErrorCode is 5 or 1223 ? TemperatureState.AccessDenied : TemperatureState.ReadFailed); }
        catch (UnauthorizedAccessException) { Publish(TemperatureState.AccessDenied); }
        catch (IOException) { Publish(TemperatureState.ReadFailed); }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or ArgumentException) { Publish(TemperatureState.InvalidData); }
        finally { _started!.TrySetResult(); }
    }
    internal static void VerifyWorker(Stream binary, string expected)
    {
        if (expected.Length != 64 || !expected.All(char.IsAsciiHexDigit) || !Convert.ToHexString(SHA256.HashData(binary)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CPU worker does not match this application build.");
    }
    private void Publish(TemperatureState state, string? code = null) => Volatile.Write(ref _current, TemperatureReading.Missing(state, CpuSensorProtocol.Source, code: code));
    private async Task StopAsync()
    {
        _stop?.Cancel(); if (_run != null) await _run;
        if (_worker != null)
        {
            if (!_worker.HasExited) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CpuSensorProtocol.ShutdownTimeoutSeconds)); await _worker.WaitForExitAsync(timeout.Token); }
            _worker.Dispose(); _worker = null;
        }
        _stop?.Dispose(); _stop = null; _run = null;
    }
    public async ValueTask DisposeAsync() { await _transition.WaitAsync(); try { _disposed = true; await StopAsync(); } finally { _transition.Release(); } }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
}
