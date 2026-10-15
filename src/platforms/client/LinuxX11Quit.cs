using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexAccountDesk;

internal sealed class LinuxX11Quit : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Iterator { public IntPtr Data; public int Remaining, Index; }
    private IntPtr _connection;
    private readonly CancellationToken _cancellation;
    private static IOException Unavailable() => new("Normal Linux application Quit requires the original client's mapped X11 window, focus and default Quit accelerator. No process was forcibly terminated.");
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
    public LinuxX11Quit(string display, CancellationToken cancellation)
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
    private bool Mapped(uint window)
    {
        var reply = Wait(xcb_get_window_attributes(_connection, window));
        try { return Marshal.ReadByte(reply, 26) == 2; } finally { free(reply); }
    }
    private (byte Quit, byte Control) Keys(IntPtr setup)
    {
        var first = Marshal.ReadByte(setup, 34); var last = Marshal.ReadByte(setup, 35); var count = last - first + 1;
        if (first < 8 || last < first || count > 255) throw Unavailable();
        var reply = Wait(xcb_get_keyboard_mapping(_connection, first, checked((byte)count)));
        try
        {
            var width = Marshal.ReadByte(reply, 1);
            if (width == 0 || width > 32 || unchecked((uint)Marshal.ReadInt32(reply, 4)) != count * width) throw Unavailable();
            byte quit = 0, control = 0;
            for (var index = 0; index < count; index++)
            {
                var symbol = unchecked((uint)Marshal.ReadInt32(reply, 32 + index * width * 4));
                if (quit == 0 && symbol is 0x71 or 0x51) quit = checked((byte)(first + index));
                if (control == 0 && symbol is 0xffe3 or 0xffe4) control = checked((byte)(first + index));
            }
            return quit != 0 && control != 0 ? (quit, control) : throw Unavailable();
        }
        finally { free(reply); }
    }
    private bool FocusWithin(uint window)
    {
        var reply = Wait(xcb_get_input_focus(_connection)); uint focus;
        try { focus = unchecked((uint)Marshal.ReadInt32(reply, 8)); } finally { free(reply); }
        for (var depth = 0; depth < 64 && focus > 1; depth++)
        {
            if (focus == window) return true;
            reply = Wait(xcb_query_tree(_connection, focus));
            try { focus = unchecked((uint)Marshal.ReadInt32(reply, 12)); } finally { free(reply); }
        }
        return false;
    }
    public void Request(int processId, Action verifyOriginal)
    {
        var pid = Atom("_NET_WM_PID"); var protocols = Atom("WM_PROTOCOLS"); var delete = Atom("WM_DELETE_WINDOW");
        var setup = xcb_get_setup(_connection); if (setup == IntPtr.Zero) throw Unavailable();
        var screens = xcb_setup_roots_iterator(setup); if (screens.Remaining < 1 || screens.Remaining > 32) throw Unavailable();
        var keys = Keys(setup); (uint Window, uint Root) target = default;
        Checked(xcb_grab_server_checked(_connection));
        try
        {
            var queue = new Queue<(uint Window, uint Root)>();
            while (screens.Remaining > 0) { var root = unchecked((uint)Marshal.ReadInt32(screens.Data)); queue.Enqueue((root, root)); xcb_screen_next(ref screens); }
            var seen = new HashSet<uint>();
            while (queue.Count > 0)
            {
                _cancellation.ThrowIfCancellationRequested(); var (window, root) = queue.Dequeue();
                if (!seen.Add(window) || seen.Count > 8192) throw Unavailable();
                var owner = Property(window, pid);
                if (owner.Length == 1 && owner[0] == processId && Array.IndexOf(Property(window, protocols), delete) >= 0 && Mapped(window)) { target = (window, root); break; }
                var reply = Wait(xcb_query_tree(_connection, window));
                try
                {
                    var count = (ushort)Marshal.ReadInt16(reply, 16);
                    if (count + seen.Count + queue.Count > 8192) throw Unavailable();
                    for (var i = 0; i < count; i++) queue.Enqueue((unchecked((uint)Marshal.ReadInt32(reply, 32 + i * 4)), root));
                }
                finally { free(reply); }
            }
            if (target.Window == 0) throw Unavailable();
            verifyOriginal();
            if (Property(target.Root, Atom("_NET_SUPPORTING_WM_CHECK")).Length == 0)
                Checked(xcb_set_input_focus_checked(_connection, 1, target.Window, 0));
            else
            {
                var message = new byte[32]; message[0] = 33; message[1] = 32;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), target.Window);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), Atom("_NET_ACTIVE_WINDOW"));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(12), 2);
                Checked(xcb_send_event_checked(_connection, 0, target.Root, (1u << 19) | (1u << 20), message));
            }
        }
        finally { xcb_ungrab_server(_connection); xcb_flush(_connection); }
        while (!FocusWithin(target.Window)) { _cancellation.ThrowIfCancellationRequested(); verifyOriginal(); Thread.Sleep(10); }
        Checked(xcb_grab_server_checked(_connection));
        try
        {
            verifyOriginal(); var owner = Property(target.Window, pid);
            if (owner.Length != 1 || owner[0] != processId || !Mapped(target.Window) || !FocusWithin(target.Window)) throw Unavailable();
            foreach (var (type, code, state) in new[] { (2, keys.Control, 0), (2, keys.Quit, 4), (3, keys.Quit, 4), (3, keys.Control, 4) })
            {
                var message = new byte[32]; message[0] = (byte)type; message[1] = code;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), target.Root);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(12), target.Window);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(28), (ushort)state); message[30] = 1;
                Checked(xcb_send_event_checked(_connection, 0, target.Window, type == 2 ? 1u : 2u, message));
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
    [DllImport("libxcb.so.1")] private static extern uint xcb_get_window_attributes(IntPtr connection, uint window);
    [DllImport("libxcb.so.1")] private static extern uint xcb_get_keyboard_mapping(IntPtr connection, byte first, byte count);
    [DllImport("libxcb.so.1")] private static extern uint xcb_set_input_focus_checked(IntPtr connection, byte revert, uint focus, uint time);
    [DllImport("libxcb.so.1")] private static extern uint xcb_grab_server_checked(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern uint xcb_ungrab_server(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern uint xcb_send_event_checked(IntPtr connection, byte propagate, uint destination, uint eventMask, byte[] message);
    [DllImport("libc")] private static extern void free(IntPtr value);
}
