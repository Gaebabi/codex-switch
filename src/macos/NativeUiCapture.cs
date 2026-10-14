using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CodexAccountDesk;

internal static class NativeUiCapture
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint WindowNumber(IntPtr window, IntPtr selector);
    public static async Task SaveAsync(Window window, string path)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "NSWindow" || !window.IsVisible) throw new InvalidOperationException("A visible app-owned native Mac window is required");
        var id = WindowNumber(handle.Handle, Selector("windowNumber"));
        if (id <= 0) throw new InvalidOperationException("Native Mac window identity unavailable");
        var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var value in new[] { "-x", "-o", "-l", id.ToString(System.Globalization.CultureInfo.InvariantCulture), path }) start.ArgumentList.Add(value);
        using var capture = Process.Start(start) ?? throw new IOException("Native Mac capture did not start");
        var errors = capture.StandardError.ReadToEndAsync(); var output = capture.StandardOutput.ReadToEndAsync();
        try { await capture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException) { if (!capture.HasExited) capture.Kill(); await capture.WaitForExitAsync(); throw; }
        await errors; await output;
        if (capture.ExitCode != 0 || !File.Exists(path)) throw new IOException("Native Mac window capture failed");
    }
}
