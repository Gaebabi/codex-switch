using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class WindowEvidence
{
    public static Task RunAsync(object service, string root, string oldCli, string newCli, string output, bool baseline)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("RUNNER_OS") != "Windows")
            throw new InvalidOperationException("Native recovery capture requires the disposable hosted Windows workflow.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { Run(service, root, oldCli, newCli, output, baseline); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Run(object service, string root, string oldCli, string newCli, string output, bool baseline)
    {
        Directory.CreateDirectory(output);
        var product = Assembly.Load("CodexSwitch");
        var windowType = product.GetType("CodexAccountDesk.MainWindow", true)!;
        var serviceType = service.GetType();
        var authPath = Path.Combine(root, "home", "auth.json");
        var originalAuth = File.ReadAllText(authPath);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(Window window, string name) => windowType.GetField(name, flags)!.GetValue(window)!;
        Task Call(Window window, string name, params object[] values) => (Task)windowType.GetMethod(name, flags | BindingFlags.Public)!.Invoke(window, values)!;
        object Account() => ((Array)serviceType.GetProperty("Accounts")!.GetValue(service)!).GetValue(0)!;
        bool Successful() => Account().GetType().GetProperty("Error")!.GetValue(Account()) == null;
        async Task Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Native refresh did not recover"); await Task.Delay(25); }
        }
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Exception? failure = null;
        var checks = new List<string>();
        void Check(string name, bool valid) { if (!valid) throw new InvalidOperationException(name); checks.Add(name); }
        app.Startup += async (_, _) =>
        {
            Window? window = null;
            try
            {
                var options = Activator.CreateInstance(product.GetType("CodexAccountDesk.Program+Options", true)!)!;
                options.GetType().GetProperty("DataDirectory")!.SetValue(options, Path.Combine(root, "vault"));
                options.GetType().GetProperty("CodexHome")!.SetValue(options, Path.Combine(root, "home"));
                options.GetType().GetProperty("UiEvidenceDirectory")!.SetValue(options, output);
                window = (Window)Activator.CreateInstance(windowType, service, options)!;
                window.ShowInTaskbar = false; window.Width = 600; window.Height = 440;
                window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = 20; window.Top = 20;
                window.Show(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                var handle = new WindowInteropHelper(window).Handle;
                void Capture(string name)
                {
                    window.UpdateLayout();
                    product.GetType("CodexAccountDesk.NativeUiEvidence", true)!.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(Window), typeof(string) }, null)!.Invoke(null, new object[] { window, Path.Combine(output, name + ".png") });
                }
                await Call(window, "EnsureInitializedAsync", CancellationToken.None);
                await Call(window, "RefreshAsync");
                Check("Native ordinary refresh reads the fictional account", Successful());
                await Task.Delay(200); Capture("initial");
                File.Delete(oldCli); Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI", newCli);
                // Invoke the actual button, including its async handler and operation tracking.
                ((Button)Field(window, "_refresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(() => !((bool)serviceType.GetProperty("IsBusy")!.GetValue(service)!) &&
                    (int)Field(window, "_operations").GetType().GetProperty("Count")!.GetValue(Field(window, "_operations"))! == 0);
                await Task.Delay(200);
                Check(baseline ? "Baseline retains a refresh error after executable removal" : "Actual Refresh button recovers after executable removal", Successful() != baseline);
                Capture(baseline ? "before" : "after");
                if (!baseline)
                {
                    var replacementBytes = File.ReadAllBytes(newCli);
                    File.Delete(newCli);
                    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", Path.Combine(root, "unavailable-during-update.exe"));
                    await Call(window, "RefreshAsync");
                    Check("Temporary installation gap remains visible as a refresh failure", !Successful());
                    Capture("update-gap");
                    File.WriteAllBytes(newCli, replacementBytes);
                    Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", Environment.ProcessPath);
                    var timer = (DispatcherTimer)Field(window, "_refreshTimer");
                    timer.Interval = TimeSpan.FromMilliseconds(100); timer.Start();
                    await Wait(Successful); timer.Stop();
                    await Wait(() => !((bool)serviceType.GetProperty("IsBusy")!.GetValue(service)!));
                    Check("Production timer callback recovers without another button click", Successful());
                    await Task.Delay(200); Capture("automatic-recovery");
                }
                Check("The same native window and service remain alive", window.IsVisible && new WindowInteropHelper(window).Handle == handle && ReferenceEquals(Field(window, "_service"), service));
                Check("Canonical fixture credentials remain unchanged", File.ReadAllText(authPath) == originalAuth);
                File.WriteAllText(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, baseline,
                    scope = "Actual Windows WPF window/button/timer and AccountService with fictional authentication and disposable offline JSON-RPC; no personal account, installed client shutdown or live update acceptance." }));
            }
            catch (Exception error) { failure = error; }
            finally { if (window != null) await Call(window, "ExitAsync"); app.Shutdown(); }
        };
        app.Run();
        if (failure != null) throw new InvalidOperationException("Native runtime-recovery verification failed", failure);
    }
}
