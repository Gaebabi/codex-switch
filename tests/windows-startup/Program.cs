using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly Product = Assembly.Load("CodexSwitch");
    private static readonly Type WindowType = Product.GetType("CodexAccountDesk.MainWindow", true)!;
    private static readonly List<string> Checks = new();
    private static object Field(Window window, string name) => WindowType.GetField(name, PrivateInstance)!.GetValue(window)!;
    private static Task Call(Window window, string name, params object[] arguments) =>
        (Task)WindowType.GetMethod(name, PrivateInstance)!.Invoke(window, arguments)!;
    private static void Check(string name, bool value)
    { if (!value) throw new InvalidOperationException(name); Checks.Add(name); }
    private static async Task WaitFor(Func<bool> ready)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!ready())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Ordinary recovery did not complete");
            await Task.Delay(20);
        }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var capture = Product.GetType("CodexAccountDesk.NativeUiEvidence", true)!.GetMethod("Capture",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] {typeof(Window), typeof(string)}, null)!;
        capture.Invoke(null, new object[] {window, path});
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh verification output");
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "codex-startup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP");
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var application = new Application {ShutdownMode = ShutdownMode.OnExplicitShutdown};
        var exit = 0;
        application.Startup += async (_, _) =>
        {
            try
            {
                foreach (var automatic in new[] {false, true})
                {
                    var label = automatic ? "automatic" : "manual";
                    var directory = Path.Combine(root, label); Directory.CreateDirectory(directory);
                    var home = Path.Combine(directory, "home"); Directory.CreateDirectory(home);
                    var absent = Path.Combine(directory, "available-later.exe");
                    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", absent);
                    var integration = Assembly.Load("CodexSwitch.Application");
                    var vault = Activator.CreateInstance(integration.GetType("CodexAccountDesk.AccountVault", true)!, Path.Combine(directory, "data"))!;
                    var service = Activator.CreateInstance(integration.GetType("CodexAccountDesk.AccountService", true)!, new object?[] {vault, home, null, null, null})!;
                    var options = Activator.CreateInstance(Product.GetType("CodexAccountDesk.Program+Options", true)!)!;
                    options.GetType().GetProperty("DataDirectory")!.SetValue(options, Path.Combine(directory, "data"));
                    options.GetType().GetProperty("CodexHome")!.SetValue(options, home);
                    options.GetType().GetProperty("UiEvidenceDirectory")!.SetValue(options, output);
                    var window = (Window)Activator.CreateInstance(WindowType, service, options)!;
                    window.ShowInTaskbar = false; window.Left = 20; window.Top = 20; window.WindowStartupLocation = WindowStartupLocation.Manual;
                    var timer = (DispatcherTimer)Field(window, "_refreshTimer");
                    try
                    {
                        window.Show(); await window.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.Loaded);
                        Func<CancellationToken, Task> initialize = token => Call(window, "EnsureInitializedAsync", token);
                        await Call(window, "RunAsync", "Codex 계정 확인 중…", initialize);
                        Check(label + ": real initialization failed while the configured client path is absent", !(bool)Field(window,"_ready") && ((Task)Field(window,"_initialization")).IsFaulted);
                        Check(label + ": account addition stays disabled before successful initialization", !((Button)Field(window,"_add")).IsEnabled);
                        await Task.Delay(200); Capture(window, Path.Combine(output, label + "-unavailable.png"));
                        Check(label + ": refresh remains reachable after initialization failure", ((Button)Field(window,"_refresh")).IsEnabled);
                        // This owned placeholder is discovered but never executed. Empty
                        // accounts avoid authentication, RPC and personal state entirely.
                        File.WriteAllBytes(absent, Array.Empty<byte>());
                        if (automatic)
                        {
                            timer.Interval = TimeSpan.FromMilliseconds(100); timer.Start();
                        }
                        else ((Button)Field(window,"_refresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await WaitFor(() => (bool)Field(window,"_ready")); timer.Stop();
                        await WaitFor(() => (int)Field(window,"_operations").GetType().GetProperty("Count")!.GetValue(Field(window,"_operations"))! == 0);
                        Check(label + ": ordinary retry initialized without restarting the window", (bool)Field(window,"_ready") && window.IsVisible);
                        Check(label + ": refresh and account addition recover together", ((Button)Field(window,"_refresh")).IsEnabled && ((Button)Field(window,"_add")).IsEnabled);
                        Check(label + ": no canonical credentials were created", !File.Exists(Path.Combine(home,"auth.json")));
                        await Task.Delay(200); Capture(window, Path.Combine(output, label + "-recovered.png"));
                    }
                    finally { timer.Stop(); window.Close(); }
                }
            }
            catch (Exception error) { exit=1; Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); }
            finally
            {
                File.WriteAllText(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(new {schema=1,os="Windows",architecture=RuntimeInformation.ProcessArchitecture.ToString(),runtime=RuntimeInformation.FrameworkDescription,version=Product.GetName().Version!.ToString(3),passed=Checks.Count,failed=exit,checks=Checks,
                    scope="Actual WPF window and production initializer/manual button/DispatcherTimer recovery; owned absent-then-placeholder path and empty encrypted storage only. Placeholder never executed; no network, real accounts, personal Codex or desktop capture."}));
                application.Shutdown();
            }
        };
        try {application.Run();}
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP",previous);
            if (Path.GetDirectoryName(root)!=Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(root).StartsWith("codex-startup-test-",StringComparison.Ordinal)) throw new IOException("Unexpected cleanup target");
            Directory.Delete(root,true);
        }
        return exit;
    }
}
