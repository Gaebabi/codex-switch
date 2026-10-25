using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexAccountDesk;

internal static class LinuxUiEvidence
{
    private sealed record FrameCapture(string File, string WindowManager, bool OriginalAppPidVerified, bool DirectParentFrameVerified, bool DesktopRootExcluded, int DemoAccounts);
    private static readonly List<FrameCapture> Frames = new();
    private static async Task<string> Command(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var owned = Process.Start(start) ?? throw new IOException("Owned native capture process did not start.");
        var output = owned.StandardOutput.ReadToEndAsync(); var error = owned.StandardError.ReadToEndAsync();
        try { await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { if (!owned.HasExited) owned.Kill(); await owned.WaitForExitAsync(); throw; }
        var text = await output; await error;
        if (owned.ExitCode != 0) throw new IOException("Native capture command failed: " + Path.GetFileName(command));
        return text;
    }
    private static async Task CaptureXid(string xid, string path)
    {
        var native = path + ".xwd";
        await Command("/usr/bin/xwd", "-silent", "-id", xid, "-out", native);
        await Command("/usr/bin/convert", native, "-strip", path);
        if (!File.Exists(path)) throw new IOException("Native capture output is missing.");
        File.Delete(native);
    }
    private static async Task CaptureFrame(string xid, string path)
    {
        if (Environment.GetEnvironmentVariable("CODEX_SWITCH_DISPOSABLE_RUNNER") != "true") throw new InvalidOperationException("Framed evidence requires the declared disposable desktop.");
        var pid = await Command("/usr/bin/xprop", "-id", xid, "_NET_WM_PID");
        var owner = Regex.Match(pid, @"_NET_WM_PID\(CARDINAL\) = ([0-9]+)");
        if (!owner.Success || owner.Groups[1].Value != Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new InvalidOperationException("Frame capture must belong to this app process.");
        var tree = await Command("/usr/bin/xwininfo", "-id", xid, "-tree");
        var parent = Regex.Match(tree, @"Parent window id: (0x[0-9a-f]+)");
        var root = Regex.Match(tree, @"Root window id: (0x[0-9a-f]+)");
        if (!parent.Success || !root.Success || parent.Groups[1].Value == root.Groups[1].Value) throw new InvalidOperationException("The real native window-manager frame is required; desktop capture is refused.");
        var children = await Command("/usr/bin/xwininfo", "-id", parent.Groups[1].Value, "-children");
        var hex = "0x" + long.Parse(xid, System.Globalization.CultureInfo.InvariantCulture).ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        if (!Regex.IsMatch(children, @"^\s+" + Regex.Escape(hex) + @"\s", RegexOptions.Multiline)) throw new InvalidOperationException("WM frame does not contain the original app window.");
        var manager = Regex.Match(await Command("/usr/bin/xprop", "-root", "_NET_SUPPORTING_WM_CHECK"), @"window id # (0x[0-9a-f]+)");
        if (!manager.Success || !(await Command("/usr/bin/xprop", "-id", manager.Groups[1].Value, "_NET_WM_NAME")).TrimEnd().EndsWith("\"Xfwm4\"", StringComparison.Ordinal))
            throw new InvalidOperationException("The verified Xfce window manager is required for this desktop evidence.");
        await CaptureXid(parent.Groups[1].Value, path);
        var after = await Command("/usr/bin/xwininfo", "-id", xid, "-tree");
        if (!after.Contains("Parent window id: " + parent.Groups[1].Value + " ", StringComparison.Ordinal)) throw new InvalidOperationException("App frame changed during capture.");
        Frames.Add(new(Path.GetFileName(path), "Xfwm4", true, true, true, 4));
    }
    private static async Task Capture(Window window, string path)
    {
        Dispatcher.UIThread.VerifyAccess(); var handle = window.TryGetPlatformHandle();
        if (!window.IsVisible || handle?.HandleDescriptor != "XID" || handle.Handle == IntPtr.Zero) throw new InvalidOperationException("An app-owned visible X11 window is required.");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var xid = handle.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
        await CaptureXid(xid, path);
        if (Environment.GetEnvironmentVariable("CODEX_SWITCH_CAPTURE_WM_FRAMES") == "1") await CaptureFrame(xid, Path.ChangeExtension(path, null) + "-wm.png");
    }
    public static async Task RenderAsync(DeskWindow window, string directory)
    {
        Directory.CreateDirectory(directory); Frames.Clear(); var checks = new List<string>(); var failures = 0;
        try
        {
            foreach (var language in new[] { "en", "ko" }) foreach (var dark in new[] { true, false }) foreach (var size in new[] { (500, 360), (600, 420), (800, 600) })
            {
                window.Preview(language, dark, SyntheticDashboard.Accounts()); window.Width = size.Item1; window.Height = size.Item2;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(120);
                if (!window.IsVisible || window.Cards.Count != 4 || window.MenuBarProbe != null || Math.Abs(window.Bounds.Width - size.Item1) > 1 || Math.Abs(window.Bounds.Height - size.Item2) > 1 ||
                    window.Cards.Any(card => card.Bounds.Width <= 0 || card.Bounds.Height + card.Margin.Top + card.Margin.Bottom < 126))
                    throw new InvalidOperationException("Shared Linux window geometry mismatch: requested=" + size.Item1 + "x" + size.Item2 + "; bounds=" + window.Bounds +
                        "; client=" + window.ClientSize + "; visible=" + window.IsVisible + "; tray=" + (window.MenuBarProbe != null) + "; cards=" +
                        string.Join(";", window.Cards.Select(card => card.Bounds.ToString())));
                foreach (var card in window.Cards)
                foreach (var text in card.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (!text.IsVisible) continue;
                    var point = text.TranslatePoint(new Point(), card);
                    if (!point.HasValue || point.Value.Y < -1 || point.Value.Y + text.Bounds.Height > card.Bounds.Height + 1)
                        throw new InvalidOperationException("Linux synthetic card text is clipped: " + text.Text);
                }
                if (window.AccountScroll.Extent.Height > window.AccountScroll.Viewport.Height + 1)
                    throw new InvalidOperationException("Four Linux preview cards unexpectedly require scrolling.");
                var automatic = window.AutomaticSwitchButton;
                if (automatic.Content is not TextBlock caption || caption.Text != Loc.T("auto.off") + " ▾" ||
                    caption.TextLayout.Height > caption.Bounds.Height + 1 || caption.TextLayout.Width > caption.Bounds.Width + 1)
                    throw new InvalidOperationException("The complete automatic-switch caption/state must fit its actual text bounds.");
                var captionPoint = caption.TranslatePoint(new Point(), automatic);
                if (!captionPoint.HasValue || captionPoint.Value.Y < -1 || captionPoint.Value.Y + caption.Bounds.Height > automatic.Bounds.Height + 1)
                    throw new InvalidOperationException("The automatic-switch caption exceeds the visible button.");
                var automaticPoint = automatic.TranslatePoint(new Point(), window) ?? throw new InvalidOperationException("Automatic-switch button is detached.");
                bool Overlaps(Control control)
                {
                    var point = control.TranslatePoint(new Point(), window);
                    return point.HasValue && point.Value.X < automaticPoint.X + automatic.Bounds.Width - 1 &&
                        point.Value.X + control.Bounds.Width > automaticPoint.X + 1 &&
                        point.Value.Y < automaticPoint.Y + automatic.Bounds.Height - 1 && point.Value.Y + control.Bounds.Height > automaticPoint.Y + 1;
                }
                if (automaticPoint.X < 0 || automaticPoint.X + automatic.Bounds.Width > window.ClientSize.Width + 1 ||
                    window.GetVisualDescendants().OfType<Button>().Any(button => button.IsVisible && button != automatic && Overlaps(button)) ||
                    window.SessionValues.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsVisible && Overlaps(text)))
                    throw new InvalidOperationException("Automatic-switch label overlaps another action/counter or leaves the client bounds.");
                var name = language + "-" + (dark ? "dark" : "light") + "-" + size.Item1 + "x" + size.Item2 + "-native.png";
                await Capture(window, Path.Combine(directory, name)); checks.Add(name);
            }
        }
        catch { failures++; throw; }
        finally
        {
            if (Environment.GetEnvironmentVariable("CODEX_SWITCH_CAPTURE_WM_FRAMES") == "1")
                File.WriteAllText(Path.Combine(directory, "wm-report.json"), JsonSerializer.Serialize(new { schema = 1, failures, frames = Frames,
                    scope = "Actual app-owned direct-parent Xfce frames with four fictional demo accounts. No titlebar composition, desktop capture, personal accounts or authenticated login." }));
            File.WriteAllText(Path.Combine(directory, "render-report.json"), JsonSerializer.Serialize(new { schema = 1, os = "Linux",
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                version = typeof(Program).Assembly.GetName().Version!.ToString(3), nativeWindowCapture = true, rendering = "Software (X11 native window)",
                display = "X11/Xvfb", failures, checks, scope = "Published Linux source preview, actual app-owned XID capture with synthetic accounts; account/client/resources/handoff are unavailable. No personal display/home/keyring/Codex access." }));
            Program.DiagnosticExitCode = failures == 0 ? 0 : 1; await window.ExitAsync();
        }
    }
}
