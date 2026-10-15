using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal static class LinuxUiEvidence
{
    private static async Task Capture(Window window, string path)
    {
        Dispatcher.UIThread.VerifyAccess(); var handle = window.TryGetPlatformHandle();
        if (!window.IsVisible || handle?.HandleDescriptor != "XID" || handle.Handle == IntPtr.Zero) throw new InvalidOperationException("An app-owned visible X11 window is required.");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var native = path + ".xwd";
        foreach (var (command, arguments) in new[] {
            ("/usr/bin/xwd", new[] { "-silent", "-id", handle.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture), "-out", native }),
            ("/usr/bin/convert", new[] { native, path }) })
        {
            var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var owned = Process.Start(start) ?? throw new IOException("Owned native capture process did not start.");
            var output = owned.StandardOutput.ReadToEndAsync(); var error = owned.StandardError.ReadToEndAsync();
            try { await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { if (!owned.HasExited) owned.Kill(); await owned.WaitForExitAsync(); throw; }
            await output; await error;
            if (owned.ExitCode != 0) throw new IOException("Native own-window capture failed: " + Path.GetFileName(path));
        }
        if (!File.Exists(path)) throw new IOException("Native capture output is missing.");
        File.Delete(native);
    }
    public static async Task RenderAsync(DeskWindow window, string directory)
    {
        Directory.CreateDirectory(directory); var checks = new List<string>(); var failures = 0;
        try
        {
            foreach (var language in new[] { "en", "ko" }) foreach (var dark in new[] { true, false }) foreach (var size in new[] { (500, 360), (600, 420), (800, 600) })
            {
                window.Preview(language, dark, SyntheticDashboard.Accounts()); window.Width = size.Item1; window.Height = size.Item2;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(120);
                if (!window.IsVisible || window.Cards.Count != 4 || window.MenuBarProbe != null || Math.Abs(window.Bounds.Width - size.Item1) > 1 || Math.Abs(window.Bounds.Height - size.Item2) > 1 ||
                    window.Cards.Any(card => card.Bounds.Width <= 0 || card.Bounds.Height < 126))
                    throw new InvalidOperationException("Shared Linux window account geometry/tray capability did not match.");
                var name = language + "-" + (dark ? "dark" : "light") + "-" + size.Item1 + "x" + size.Item2 + "-native.png";
                await Capture(window, Path.Combine(directory, name)); checks.Add(name);
            }
        }
        catch { failures++; throw; }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "render-report.json"), JsonSerializer.Serialize(new { schema = 1, os = "Linux",
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                version = typeof(Program).Assembly.GetName().Version!.ToString(3), nativeWindowCapture = true, rendering = "Software (X11 native window)",
                display = "X11/Xvfb", failures, checks, scope = "Published Linux source preview, actual app-owned XID capture with synthetic accounts; account/client/resources/handoff are unavailable. No personal display/home/keyring/Codex access." }));
            Program.DiagnosticExitCode = failures == 0 ? 0 : 1; await window.ExitAsync();
        }
    }
}
