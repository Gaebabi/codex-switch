using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexAccountDesk;

internal sealed class LinuxX11Close : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Iterator { public IntPtr Data; public int Remaining, Index; }
    private IntPtr _connection;
    private readonly CancellationToken _cancellation;
    private static IOException Unavailable() => new("Normal Linux desktop close requires the original client's accessible X11/XWayland windows. No process was forcibly terminated.");
    private IntPtr Wait(uint sequence)
    {
        if (xcb_flush(_connection) <= 0) throw Unavailable();
        while (true)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (xcb_connection_has_error(_connection) != 0) throw Unavailable();
            if (xcb_poll_for_reply(_connection, sequence, out var reply, out var error) != 0)
            {
                if (error != IntPtr.Zero) { free(error); if (reply != IntPtr.Zero) free(reply); throw Unavailable(); }
                return reply != IntPtr.Zero ? reply : throw Unavailable();
            }
            Thread.Sleep(10);
        }
    }
    private void Checked(uint sequence)
    {
        var barrier = Wait(xcb_get_input_focus(_connection)); free(barrier);
        var error = xcb_request_check(_connection, sequence);
        if (error != IntPtr.Zero) { free(error); throw Unavailable(); }
        if (xcb_connection_has_error(_connection) != 0) throw Unavailable();
    }
    public LinuxX11Close(string display, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(display)) throw Unavailable();
        _cancellation = cancellation; _connection = xcb_connect(display, out _);
        if (_connection == IntPtr.Zero || xcb_connection_has_error(_connection) != 0) { Dispose(); throw Unavailable(); }
    }
    private uint Atom(string name)
    {
        var reply = Wait(xcb_intern_atom(_connection, 0, checked((ushort)name.Length), name));
        try { return unchecked((uint)Marshal.ReadInt32(reply, 8)); } finally { free(reply); }
    }
    private uint[] Property(uint window, uint name)
    {
        var reply = Wait(xcb_get_property(_connection, 0, window, name, 0, 0, 256));
        try
        {
            var count = unchecked((uint)Marshal.ReadInt32(reply, 16));
            if (count == 0) return Array.Empty<uint>();
            if (Marshal.ReadByte(reply, 1) != 32 || count > 256 || Marshal.ReadInt32(reply, 12) != 0) throw Unavailable();
            var values = new uint[count]; for (var i = 0; i < values.Length; i++) values[i] = unchecked((uint)Marshal.ReadInt32(reply, 32 + i * 4)); return values;
        }
        finally { free(reply); }
    }
    public void Request(int processId, Action verifyOriginal)
    {
        var pid = Atom("_NET_WM_PID"); var protocols = Atom("WM_PROTOCOLS"); var delete = Atom("WM_DELETE_WINDOW");
        var setup = xcb_get_setup(_connection); if (setup == IntPtr.Zero) throw Unavailable();
        var screens = xcb_setup_roots_iterator(setup); if (screens.Remaining < 1 || screens.Remaining > 32) throw Unavailable();
        Checked(xcb_grab_server_checked(_connection));
        try
        {
            var queue = new Queue<uint>();
            while (screens.Remaining > 0) { queue.Enqueue(unchecked((uint)Marshal.ReadInt32(screens.Data))); xcb_screen_next(ref screens); }
            var seen = new HashSet<uint>(); var targets = new List<uint>();
            while (queue.Count > 0)
            {
                _cancellation.ThrowIfCancellationRequested(); var window = queue.Dequeue();
                if (!seen.Add(window) || seen.Count > 8192) throw Unavailable();
                var owner = Property(window, pid);
                if (owner.Length == 1 && owner[0] == processId && Array.IndexOf(Property(window, protocols), delete) >= 0) { targets.Add(window); continue; }
                var reply = Wait(xcb_query_tree(_connection, window));
                try
                {
                    var count = (ushort)Marshal.ReadInt16(reply, 16);
                    if (count + seen.Count + queue.Count > 8192) throw Unavailable();
                    for (var i = 0; i < count; i++) queue.Enqueue(unchecked((uint)Marshal.ReadInt32(reply, 32 + i * 4)));
                }
                finally { free(reply); }
            }
            if (targets.Count == 0) throw Unavailable();
            foreach (var window in targets)
            {
                verifyOriginal(); var owner = Property(window, pid);
                if (owner.Length != 1 || owner[0] != processId) throw Unavailable();
                var message = new byte[32]; message[0] = 33; message[1] = 32;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), window);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), protocols);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(12), delete);
                Checked(xcb_send_event_checked(_connection, 0, window, 0, message));
            }
        }
        finally { xcb_ungrab_server(_connection); xcb_flush(_connection); }
    }
    public void Dispose() { var connection = Interlocked.Exchange(ref _connection, IntPtr.Zero); if (connection != IntPtr.Zero) xcb_disconnect(connection); }
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_connect([MarshalAs(UnmanagedType.LPUTF8Str)] string display, out int screen);
    [DllImport("libxcb.so.1")] private static extern void xcb_disconnect(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern int xcb_connection_has_error(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern int xcb_flush(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern int xcb_poll_for_reply(IntPtr connection, uint request, out IntPtr reply, out IntPtr error);
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_request_check(IntPtr connection, uint request);
    [DllImport("libxcb.so.1")] private static extern uint xcb_get_input_focus(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_get_setup(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern Iterator xcb_setup_roots_iterator(IntPtr setup);
    [DllImport("libxcb.so.1")] private static extern void xcb_screen_next(ref Iterator iterator);
    [DllImport("libxcb.so.1")] private static extern uint xcb_intern_atom(IntPtr connection, byte onlyIfExists, ushort length, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libxcb.so.1")] private static extern uint xcb_get_property(IntPtr connection, byte delete, uint window, uint property, uint type, uint offset, uint length);
    [DllImport("libxcb.so.1")] private static extern uint xcb_query_tree(IntPtr connection, uint window);
    [DllImport("libxcb.so.1")] private static extern uint xcb_grab_server_checked(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern uint xcb_ungrab_server(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern uint xcb_send_event_checked(IntPtr connection, byte propagate, uint destination, uint eventMask, byte[] message);
    [DllImport("libc")] private static extern void free(IntPtr value);
}
