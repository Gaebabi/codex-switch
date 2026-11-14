using CodexAccountDesk;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

internal static class StartupProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] is not ("visible" or "tray")) throw new ArgumentException("mode package CLI evidence-root");
        foreach (var name in new[] { "CodexSwitch.Core", "CodexSwitch.Sqlite", "CodexSwitch.PlatformSecurity", "CodexSwitch.DesktopClient", "CodexSwitch.Protocol", "CodexSwitch.Hardware", "CodexSwitch.Application", "CodexSwitch" })
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(args[1], name + ".dll"));
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexSwitchStartup", Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data"); var home = Path.Combine(root, "deliberately-absent-home");
        var evidence = Path.GetFullPath(args[3]); Directory.CreateDirectory(evidence);
        var provider = new ModelProvider { Name = "Startup fixture", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-only", Models = new() { new ProviderModel { UpstreamId = "fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("fixture") } } };
        new ModelProviderStore(data).Update(settings => { settings.Providers.Add(provider); settings.Connected = true; settings.RouterEnabled = true; settings.SelectedRoute = "gpt-6.1-sol"; });
        Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI", Path.GetFullPath(args[2]));
        Environment.SetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP", Environment.ProcessPath);
        var service = new AccountService(new AccountVault(data), home);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var options = new CodexAccountDesk.Program.Options { DataDirectory = data, CodexHome = home, StartHidden = args[0] == "tray" };
        var window = new MainWindow(service, options) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000 };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        Exception? failure = null;
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args[0] == "tray") _ = window.StartInTrayAsync();
                var until = DateTime.UtcNow.AddSeconds(25);
                while (!Field<bool>("_ready") || !service.ModelProviders.Running)
                {
                    if (DateTime.UtcNow > until) throw new TimeoutException("Actual startup did not initialize the fixture gateway; status=" + Field<System.Windows.Controls.TextBlock>("_status").Text);
                    await Task.Delay(50);
                }
                var picker = Field<ModelRoutePicker>("_routePicker");
                if (picker.Visibility != Visibility.Visible || picker.CreateMenu().Items.Count != 1 || service.ModelProviders.Settings.SelectedRoute != "gpt-6.1-sol") throw new Exception("Legacy selection or visible provider picker was lost.");
                if (Directory.Exists(home)) throw new Exception("Absent fixture home unexpectedly created.");
                var assembly = typeof(MainWindow).Assembly;
                if (!string.Equals(Path.GetDirectoryName(assembly.Location), Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase)) throw new Exception("Did not load the published package.");
                File.WriteAllText(Path.Combine(evidence, args[0] + ".json"), JsonSerializer.Serialize(new { mode = args[0], started = Field<bool>("_started"), ready = Field<bool>("_ready"), gatewayRunning = service.ModelProviders.Running, pickerVisible = picker.Visibility == Visibility.Visible, choices = picker.CreateMenu().Items.Count, selection = service.ModelProviders.Settings.SelectedRoute, assembly = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))), receiptExists = File.Exists(Path.Combine(data, "running-instance.json")), ownerHomeAbsent = !Directory.Exists(home) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { failure = error; }
            finally
            {
                await (Task)typeof(MainWindow).GetMethod("ExitAsync", flags)!.Invoke(window, null)!;
                app.Shutdown();
            }
        };
        if (args[0] == "tray") { app.MainWindow = window; app.Run(); } else app.Run(window);
        if (failure != null) throw failure;
        Console.WriteLine("Published package " + args[0] + " startup initialized its disposable provider store and gateway.");
        return 0;
    }
}
