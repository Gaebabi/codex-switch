using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal static class NativeUiCapture
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint WindowNumber(IntPtr window, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern byte NativeVisible(IntPtr window, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void Display(IntPtr window, IntPtr selector);
    public static async Task SaveAsync(TopLevel window, string path)
    {
        Dispatcher.UIThread.VerifyAccess();
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "NSWindow" || !window.IsVisible) throw new InvalidOperationException("A visible app-owned native Mac window is required");
        var id = WindowNumber(handle.Handle, Selector("windowNumber"));
        if (id <= 0) throw new InvalidOperationException("Native Mac window identity unavailable");
        var visibleSelector = Selector("isVisible");
        var wasVisible = NativeVisible(handle.Handle, visibleSelector) != 0;
        if (window is Window ownedWindow) ownedWindow.Activate();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var ready = Stopwatch.StartNew();
        while (NativeVisible(handle.Handle, visibleSelector) == 0 && ready.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(16);
        if (NativeVisible(handle.Handle, visibleSelector) == 0) throw new IOException("Owned NSWindow did not become visible before capture");
        Display(handle.Handle, Selector("displayIfNeeded"));
        var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var value in new[] { "-x", "-o", "-l", id.ToString(System.Globalization.CultureInfo.InvariantCulture), path }) start.ArgumentList.Add(value);
        using var capture = Process.Start(start) ?? throw new IOException("Native Mac capture did not start");
        var errors = capture.StandardError.ReadToEndAsync(); var output = capture.StandardOutput.ReadToEndAsync();
        try { await capture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException) { if (!capture.HasExited) capture.Kill(); await capture.WaitForExitAsync(); throw; }
        var errorText = await errors; await output;
        if (capture.ExitCode != 0 || !File.Exists(path))
        {
            File.WriteAllText(path + ".capture-error.txt", errorText);
            var reason = errorText.Contains("image from window", StringComparison.OrdinalIgnoreCase) ? "WindowServer image unavailable" :
                errorText.Contains("permission", StringComparison.OrdinalIgnoreCase) || errorText.Contains("not permitted", StringComparison.OrdinalIgnoreCase) ? "OS capture permission denied" :
                errorText.Contains("display", StringComparison.OrdinalIgnoreCase) ? "OS display unavailable" : "OS capture error";
            throw new IOException($"Native Mac window capture failed: {Path.GetFileName(path)}; exit={capture.ExitCode}; {reason}; nativeVisibleBefore={wasVisible}; " +
                $"nativeVisibleNow={NativeVisible(handle.Handle, visibleSelector) != 0}; occlusion={WindowNumber(handle.Handle, Selector("occlusionState"))}; sharing={WindowNumber(handle.Handle, Selector("sharingType"))}.");
        }
    }
}
