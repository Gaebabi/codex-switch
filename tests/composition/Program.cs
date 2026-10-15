using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexAccountDesk;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var checks = new List<string>();
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop composition contract failed."); }
        void Check(string name, Action action) { action(); checks.Add(name); }
        bool ImportsNative(Assembly assembly) => assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Any(m => m.IsDefined(typeof(DllImportAttribute), false));
        Check("Account integration has one UI-independent application assembly", () =>
        {
            var assembly = typeof(AccountService).Assembly;
            Assert(assembly.GetName().Name == "CodexSwitch.Application" && typeof(AccountVault).Assembly == assembly && typeof(DesktopSwitchBackend).Assembly == assembly &&
                typeof(RuntimeHome).Assembly == assembly && typeof(PlatformLanguagePackWriter).Assembly == assembly);
            Assert(!ImportsNative(assembly) && !assembly.GetReferencedAssemblies().Any(a => a.Name is "CodexSwitch" or "CodexSwitch.Desktop" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
        });
        Check("Shared views/themes have no native imports or concrete Mac host types", () =>
        {
            var assembly = typeof(DeskWindow).Assembly;
            Assert(assembly.GetName().Name == "CodexSwitch.Desktop" && typeof(DeskTheme).Assembly == assembly && typeof(MacAccountCard).Assembly == assembly && typeof(HistoryWindow).Assembly == assembly);
            Assert(!ImportsNative(assembly) && !assembly.GetTypes().Any(t => t.Name is "Program" or "MacMenuBar" or "MacAppearance" or "MacDiagnostics" or "NativeUiCapture"));
        });
        Check("Theme/mark/icon resources follow their shared UI owner", () =>
        {
            var assembly = typeof(DeskTheme).Assembly;
            Assert(new[] { "CodexAccountDesk.Burn0Tokens.css", "CodexAccountDesk.Burn0Icons.json", "CodexAccountDesk.Mark.path", "!AvaloniaResources" }.All(name => assembly.GetManifestResourceNames().Contains(name)));
        });
        void CheckAppearance() => Check("Portable theme creation preserves palette and defers native appearance callbacks", () =>
        {
            var called = false; var appearance = new DesktopAppearance("sans-serif", () => { called = true; return true; });
            var dark = new DeskTheme(true, appearance); var light = new DeskTheme(false, appearance);
            Assert(!called && dark.Appearance == appearance && light.Appearance == appearance && dark.Brush("canvas").Color != light.Brush("canvas").Color);
        });
        if (args.Length > 0)
        {
            if (args.Length != 1 || args[0] != "--native-ui" || !OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) throw new ArgumentException("Native composition mode requires a declared Windows or Mac runner.");
            var absent = Path.Combine(Path.GetTempPath(), "codex-composition-absent-" + Guid.NewGuid().ToString("N"));
            var resources = new PendingResources(); Exception? failure = null;
            var host = new DesktopHost(absent, absent, "Codex Switch — Composition probe", resources, null, KeyModifiers.Control, "/",
                new DesktopAppearance(OperatingSystem.IsMacOS() ? "Helvetica Neue" : "Segoe UI", () => false), () => 0);
            var builder = AppBuilder.Configure(() => new DeskApp(host, SyntheticDashboard.Accounts(), async window =>
            {
                try
                {
                    CheckAppearance();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check("Actual shared native window remains reachable without a tray", () => Assert(window.IsVisible && window.MenuBarProbe == null && !window.Exiting));
                    window.Close();
                    Check("Ordinary native close exits and disposes trayless resources", () => Assert(window.Exiting && resources.Disposed && !window.IsVisible));
                    Check("Synthetic native UI never creates a data or Codex home", () => Assert(!Directory.Exists(absent) && !File.Exists(absent) && resources.Starts == 0));
                }
                catch (Exception error) { failure = error; }
                finally { if (!window.Exiting) await window.ExitAsync(); }
            })).UsePlatformDetect();
            if (OperatingSystem.IsMacOS()) builder.With(new AvaloniaNativePlatformOptions { RenderingMode = new[] { AvaloniaNativeRenderingMode.Software } });
            else builder.With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } });
            var exit = builder.StartWithClassicDesktopLifetime(Array.Empty<string>(), ShutdownMode.OnExplicitShutdown);
            if (failure != null) throw new InvalidOperationException("Native trayless composition failed.", failure);
            Assert(exit == 0 && checks.Count == 7);
        }
        else CheckAppearance();
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks,
            scope = "Production assembly/resource/appearance boundaries; optional native Windows/Mac mode hosts the actual shared UI with synthetic accounts and pending resources, then closes normally. No real data, account/client/sensor actions or Linux support claim." }));
        return 0;
    }
}
internal sealed class PendingResources : IResourceMonitor
{
    public HardwareUsage Current => HardwareUsage.Pending;
    public event Action? Changed { add { } remove { } }
    public bool Disposed { get; private set; }
    public int Starts { get; private set; }
    public void Start() => Starts++;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
