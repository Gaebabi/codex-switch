using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodexSwitch.Sensors;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 1 && arguments[0] == "--probe")
            {
                var sample = ReadOnce(); Console.WriteLine(JsonSerializer.Serialize(sample, CpuJson.Default.CpuSample)); return 0;
            }
            if (arguments.Length != 3 || !int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) || parentId <= 0 ||
                !long.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(arguments[2], "N", out var nonce)) return 2;
            using var parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != ticks) return 3;
            using var pipe = new NamedPipeClientStream(".", "CodexSwitch.Cpu." + nonce.ToString("N"), PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CpuSensorProtocol.ConnectTimeoutSeconds)); await pipe.ConnectAsync(timeout.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId) || serverId != parentId) return 4;
            using var reader = CreateReader(out var failure);
            while (!parent.HasExited)
            {
                using var send = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                CpuSample value;
                try { value = failure != null ? failure with { At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } : reader!.Read(); }
                catch (Win32Exception error) { value = Failure(error); }
                catch (InvalidDataException) { value = CpuSensorProtocol.Missing("ReadFailed"); }
                await CpuSensorProtocol.WriteAsync(pipe, value, send.Token);
                await Task.Delay(2000);
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ArgumentException or InvalidOperationException or Win32Exception) { return 1; }
    }
    private static PawnCpuReader? CreateReader(out CpuSample? failure)
    {
        failure = null;
        try { return new PawnCpuReader(); }
        catch (UnauthorizedAccessException) { failure = CpuSensorProtocol.Missing("AccessDenied"); return null; }
        catch (Win32Exception error) { failure = Failure(error); return null; }
        catch (InvalidDataException) { failure = CpuSensorProtocol.Missing("ReadFailed"); return null; }
    }
    private static CpuSample ReadOnce()
    {
        using var reader = CreateReader(out var failure);
        if (failure != null) return failure;
        try { return reader!.Read(); }
        catch (Win32Exception error) { return Failure(error); }
        catch (InvalidDataException) { return CpuSensorProtocol.Missing("ReadFailed"); }
    }
    private static CpuSample Failure(Win32Exception error) => CpuSensorProtocol.Missing(error.NativeErrorCode switch
    { 5 => "AccessDenied", 1 or 50 => "PlatformUnsupported", _ => "ReadFailed" }) with { Error = error.NativeErrorCode };
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
}
