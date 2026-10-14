using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CodexAccountDesk;

internal static class NativeUiEvidence
{
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rectangle bounds);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codex-ui-evidence-" + Guid.NewGuid().ToString("N")));
        var vault = new AccountVault(Path.Combine(temporary, "data")); vault.Data.Accounts.AddRange(SyntheticDashboard.Accounts());
        var service = new AccountService(vault, Path.Combine(temporary, "home"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(service, new Program.Options { DataDirectory = vault.DirectoryPath, CodexHome = service.CodexHome,
            UiEvidenceDirectory = directory }) { ShowInTaskbar = false, Left = 20, Top = 20, WindowStartupLocation = WindowStartupLocation.Manual };
        var cases = new List<object>(); var exit = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                foreach (var language in new[] { "en", "ko" })
                foreach (var size in new[] { (800, 560), (500, 360) })
                {
                    window.PrepareUiEvidence(language, dark); window.Width = size.Item1; window.Height = size.Item2;
                    window.UpdateLayout(); await Task.Delay(300); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    var name = language + "-" + (dark ? "dark" : "light") + "-" + size.Item1 + "x" + size.Item2;
                    Capture(window, Path.Combine(directory, name + ".png")); cases.Add(new { name, passed = true });
                }
            }
            catch (Exception error) { exit = 1; cases.Add(new { name = "native-window", passed = false, error = error.GetType().Name }); }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(new { schema = 1, os = "Windows",
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                    appVersion = typeof(Program).Assembly.GetName().Version!.ToString(3), failures = exit, cases,
                    scope = "Actual published app/native HWND captures; only own window and synthetic dashboard. No network, credential, Codex home/process or personal screen access." }));
                window.Close(); app.Shutdown();
            }
        };
        app.Run(window);
        var target = Path.GetFullPath(temporary);
        if (Path.GetDirectoryName(target) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
            !Path.GetFileName(target).StartsWith("codex-ui-evidence-", StringComparison.Ordinal)) throw new IOException("Unexpected evidence cleanup target");
        Directory.Delete(target, true); return exit;
    }
    private static void Capture(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        GetWindowThreadProcessId(handle, out var process);
        if (process != Environment.ProcessId || !GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Capture requires this app's own window");
        var width = bounds.Right - bounds.Left; var height = bounds.Bottom - bounds.Top;
        if (width < 400 || height < 280 || width > 4000 || height > 3000) throw new InvalidOperationException("Invalid native window bounds");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Black); var context = graphics.GetHdc();
            try { if (!PrintWindow(handle, context, 2)) throw new IOException("Native window capture failed"); }
            finally { graphics.ReleaseHdc(context); }
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
