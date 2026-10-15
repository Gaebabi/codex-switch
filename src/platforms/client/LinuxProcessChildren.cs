using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal sealed class LinuxProcessChildren : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Poll { public int Descriptor; public short Events, Returned; }
    private readonly List<SafeFileHandle> _processes = new();
    private static bool Exited(SafeFileHandle handle)
    {
        var value = new Poll { Descriptor = handle.DangerousGetHandle().ToInt32(), Events = 1 };
        var result = poll(ref value, 1, 0);
        if (result < 0 || (value.Returned & ~0x11) != 0) throw LinuxPeerProcess.Refused();
        return result == 1 && (value.Returned & 0x11) != 0;
    }
    public static LinuxProcessChildren Capture(int parent, CancellationToken cancellation)
    {
        var result = new LinuxProcessChildren();
        try
        {
            var pending = new Queue<(int Id, SafeFileHandle? Handle)>(); pending.Enqueue((parent, null)); var seen = new HashSet<int> { parent };
            while (pending.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested(); var current = pending.Dequeue();
                if (current.Handle != null && Exited(current.Handle)) continue;
                string[] threads;
                try { threads = Directory.GetDirectories("/proc/" + current.Id + "/task"); }
                catch (IOException) when (current.Handle != null && Exited(current.Handle)) { continue; }
                foreach (var thread in threads)
                {
                    string text;
                    try { text = File.ReadAllText(Path.Combine(thread, "children")); }
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    foreach (var item in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var child) || child < 1 || seen.Count >= 4096) throw LinuxPeerProcess.Refused();
                        if (!seen.Add(child)) continue;
                        var descriptor = pidfd_open(child, 0);
                        if (descriptor < 0)
                        { if (Marshal.GetLastPInvokeError() == 3) continue; throw LinuxPeerProcess.Refused(); }
                        var handle = new SafeFileHandle(new IntPtr(descriptor), true); result._processes.Add(handle);
                        if (!Exited(handle)) pending.Enqueue((child, handle));
                    }
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public async Task WaitAsync(CancellationToken cancellation)
    { while (_processes.Any(handle => !Exited(handle))) await Task.Delay(100, cancellation).ConfigureAwait(false); }
    public void Dispose() { foreach (var process in _processes) process.Dispose(); }
    [DllImport("libc", SetLastError = true)] private static extern int pidfd_open(int processId, uint flags);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref Poll descriptors, nuint count, int timeout);
}
