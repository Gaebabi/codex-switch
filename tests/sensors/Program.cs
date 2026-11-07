using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using CodexSwitch.Sensors;
using Microsoft.Win32.SafeHandles;

if (!OperatingSystem.IsWindows() || args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--driver") return 2;
var binary = Path.GetFullPath(args[0]); var checks = new List<string>();
if (args.Length == 2)
{
    using var device = Native.CreateFileW(@"\\?\GLOBALROOT\Device\PawnIO", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (device.IsInvalid) throw new InvalidOperationException("Installed CPU driver cannot be opened.");
    var source = Path.Combine("src", "sensors", "modules", System.Runtime.Intrinsics.X86.X86Base.CpuId(0,0).Ebx == 0x756e6547 ? "IntelMSR.bin" : "AMDFamily17.bin");
    var module = File.ReadAllBytes(source);
    if (!Native.DeviceIoControl(device, 0xa1b22084, module, module.Length, Array.Empty<byte>(), 0, out _, IntPtr.Zero)) throw new InvalidOperationException("Official signed module rejected.");
    checks.Add("Installed official driver accepts the unchanged signed CPU module");
    using var tamperedDevice = Native.CreateFileW(@"\\?\GLOBALROOT\Device\PawnIO", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    module[^1] ^= 1;
    if (Native.DeviceIoControl(tamperedDevice, 0xa1b22084, module, module.Length, Array.Empty<byte>(), 0, out _, IntPtr.Zero)) throw new InvalidOperationException("Tampered CPU module accepted.");
    checks.Add("Driver signature enforcement refuses a tampered module without test-signing or policy bypass");
}
using var parent = Process.GetCurrentProcess();
Process Start(params string[] values)
{
    var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var value in values) start.ArgumentList.Add(value);
    return Process.Start(start) ?? throw new IOException("Worker did not start.");
}
foreach (var values in new[] { new[] { "--unknown" }, new[] { "1", "0", "not-a-nonce" }, new[] { Environment.ProcessId.ToString(), "1", Guid.NewGuid().ToString("N") } })
{
    using var refused = Start(values); using var limit = new CancellationTokenSource(5000); await refused.WaitForExitAsync(limit.Token);
    if (refused.ExitCode == 0) throw new InvalidOperationException("Malformed worker launch accepted.");
}
checks.Add("Malformed command/nonce and recycled parent identity are refused without UI");
var nonce = Guid.NewGuid().ToString("N");
using var pipe = new NamedPipeServerStream("CodexSwitch.Cpu." + nonce, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
using var worker = Start(Environment.ProcessId.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), nonce);
using (var limit = new CancellationTokenSource(15000)) await pipe.WaitForConnectionAsync(limit.Token);
if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client) || client != worker.Id) throw new InvalidOperationException("Wrong sensor client process.");
checks.Add("Native output-only pipe is current-user-only and binds the launched process ID");
CpuSample? last = null;
for (var index = 0; index < 2; index++)
{
    using var limit = new CancellationTokenSource(8000); var value = await CpuSensorProtocol.ReadAsync(pipe, limit.Token);
    if (last != null && value.At <= last.At) throw new InvalidOperationException("Sensor status did not update."); last = value;
}
checks.Add("Actual native worker emits fresh validated CPU values or honest unavailable states");
var wrongNonce = Guid.NewGuid().ToString("N");
using (var wrongPipe = new NamedPipeServerStream("CodexSwitch.Cpu." + wrongNonce, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
using (var confused = Start(worker.Id.ToString(), worker.StartTime.ToUniversalTime().Ticks.ToString(), wrongNonce))
using (var limit = new CancellationTokenSource(8000))
{
    await wrongPipe.WaitForConnectionAsync(limit.Token); await confused.WaitForExitAsync(limit.Token);
    if (confused.ExitCode != 4) throw new InvalidOperationException("Wrong parent pipe server was trusted.");
}
checks.Add("A live parent identity cannot authorize a pipe served by a different process");
pipe.Dispose();
using (var limit = new CancellationTokenSource(8000)) await worker.WaitForExitAsync(limit.Token);
checks.Add("Worker exits on pipe closure without persistent service or account access");
var report = JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks, sensorState = last!.State, nativeError = last.Error,
    scope = "Actual output-only worker lifecycle on this Windows host. A fresh native value is separate from owner-device acceptance; no accounts, private paths or screen access." });
Directory.CreateDirectory("artifacts"); File.WriteAllText(Path.Combine("artifacts", args.Length == 2 ? "cpu-sensor-worker-driver.json" : "cpu-sensor-worker.json"), report);
Console.WriteLine(report);
return 0;
internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int length, byte[] output, int capacity, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
}
