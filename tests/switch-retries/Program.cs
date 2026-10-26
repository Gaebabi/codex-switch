using CodexAccountDesk;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("RUNNER_OS") != "Windows")
    throw new InvalidOperationException("Native capture requires disposable hosted Windows CI.");
var output = Path.GetFullPath(args[0]);
var baseline = args.Contains("--baseline");
Directory.CreateDirectory(output);
var fixture = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "switch-retry-evidence-" + Guid.NewGuid().ToString("N"));
var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var thread = new Thread(() =>
{
    var product = Assembly.Load("CodexSwitch");
    var type = product.GetType("CodexAccountDesk.MainWindow", true)!;
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    object? Call(Window window, string method, params object?[] values) => type.GetMethod(method, flags)!.Invoke(window, values);
    void Set(Window window, string field, object? value) => type.GetField(field, flags)!.SetValue(window, value);
    IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    bool HasAction(Window window, string text, bool enabled)
    {
        // RenderAccounts replaces controls; apply/arrange their actual templates before inspection.
        window.UpdateLayout();
        return Visuals(window).OfType<Button>().Any(button =>
            button.IsVisible && button.IsEnabled == enabled && Visuals(button).OfType<TextBlock>().Any(t => t.Text == Loc.T(text)));
    }
    var checks = new List<string>();
    void Check(string name, bool valid) { if (!valid) throw new InvalidOperationException(name); checks.Add(name); }
    AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    Exception? failure = null;
    Window? ownedWindow = null;
    app.Startup += async (_, _) =>
    {
        try
        {
            var language = args.Contains("--ko") ? "ko" : "en";
            var persistent = args.Contains("--persistent");
            {
                var vault = new AccountVault(Path.Combine(fixture, language + persistent, "data"));
                foreach (var profile in SyntheticDashboard.Accounts())
                {
                    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = profile.Key, email = profile.Email }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    profile.AuthJson = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = profile.Key, id_token = "e30." + token + ".fictional", access_token = "fictional-access", refresh_token = "fictional-refresh" } });
                    var identity = AuthDocument.Parse(profile.AuthJson);
                    profile.Key = identity.Key; profile.AccountId = identity.AccountId; vault.Data.Accounts.Add(profile);
                }
                vault.Save();
                var home = Path.Combine(fixture, language + persistent, "absent-home");
                var service = new AccountService(vault, home);
                var source = vault.Data.Accounts[0].Key; var target = vault.Data.Accounts[1].Key;
                typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, source);
                var options = Activator.CreateInstance(product.GetType("CodexAccountDesk.Program+Options", true)!)!;
                options.GetType().GetProperty("DataDirectory")!.SetValue(options, vault.DirectoryPath);
                options.GetType().GetProperty("CodexHome")!.SetValue(options, home);
                options.GetType().GetProperty("UiEvidenceDirectory")!.SetValue(options, output);
                var window = (Window)Activator.CreateInstance(type, service, options)!;
                ownedWindow = window;
                {
                    Call(window, "PrepareUiEvidence", language, language == "en");
                    window.ShowInTaskbar = false; window.Width = 600; window.Height = 440;
                    window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = 20; window.Top = 20;
                    window.Show(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    var handle = new WindowInteropHelper(window).Handle;
                    Set(window, "_ready", true); Set(window, "_switching", true); Set(window, "_queuedKey", target);
                    async Task Capture(string name)
                    {
                        window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(200);
                        product.GetType("CodexAccountDesk.NativeUiEvidence", true)!.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static,
                            null, new[] { typeof(Window), typeof(string) }, null)!.Invoke(null, new object[] { window, Path.Combine(output, language + "-" + name + ".png") });
                    }
                    var backend = new RetryBackend(source, target, persistent, () =>
                    {
                        foreach (var account in service.Accounts) account.IsCurrent = account.Key == target;
                        typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, target);
                    });
                    var coordinator = new AccountSwitchCoordinator(backend, p => Call(window, "SaveSwitch", p), p => Call(window, "SwitchChanged", p), error => error.Message);
                    var plan = await coordinator.PrepareAsync(target, DateTimeOffset.Parse("2026-10-27T03:00:00+09:00"), CancellationToken.None);
                    var marker = plan.Chats[0].ContinueMessageId;
                    backend.BeforeLaunch = async () =>
                    {
                        if (baseline || backend.Launches <= 1 || persistent) return;
                        Check(language + ": retry keeps busy controls and hides Recover", HasAction(window, "전환 중", false) && !HasAction(window, "switch.recover", true));
                        Check(language + ": retry reservation was persisted", new AccountVault(vault.DirectoryPath).Data.PendingSwitch?.AutomaticRecoveryAttempts == backend.Launches - 1);
                        await Capture("retry-" + (backend.Launches - 1));
                    };
                    var completed = false;
                    try { completed = await coordinator.RunAsync(plan, CancellationToken.None); }
                    catch (IOException) { Check(language + ": only expected persistent or baseline launch failure", persistent || baseline); }
                    Set(window, "_switching", false); Set(window, "_queuedKey", null);
                    Call(window, "UpdateControls"); Call(window, "RenderAccounts");
                    Check(language + ": backup/shutdown/apply performed once", backend.Names == 1 && backend.Closes == 1 && backend.Applies == 1);
                    Check(language + ": own native window remains alive", window.IsVisible && new WindowInteropHelper(window).Handle == handle);
                    Check(language + ": no actual home or client was created", !Directory.Exists(home));
                    if (baseline || persistent)
                    {
                        Check(language + ": failure retains manual Recover", !completed && HasAction(window, "switch.recover", true) && backend.Sends == 0);
                        Check(language + ": bounded launch count", backend.Launches == (baseline ? 2 : 3));
                        await Capture(baseline ? "before" : "exhausted");
                    }
                    else
                    {
                        Check(language + ": two retries finish in original invocation", completed && backend.Launches == 3 && plan.AutomaticRecoveryAttempts == 2);
                        Check(language + ": one Continue identity and cleared checkpoint", backend.Sends == 1 && backend.Marker == marker && vault.Data.PendingSwitch == null && !HasAction(window, "switch.recover", true));
                        await Capture("after");
                    }
                    File.WriteAllText(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new { baseline, passed = checks.Count, checks,
                        scope = "Actual WPF own-HWND windows, production coordinator and native progress/save callbacks with fictional accounts and controlled offline backend. Fixture sets busy context; no real Switch click, client launch, authenticated account change or Continue is claimed." }));
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { if (ownedWindow != null) await (Task)Call(ownedWindow, "ExitAsync")!; }
            catch (Exception error) { failure ??= error; }
            finally { app.Shutdown(); }
        }
    };
    app.Run();
    if (failure == null) completion.TrySetResult(); else completion.TrySetException(failure);
});
thread.SetApartmentState(ApartmentState.STA); thread.Start();
try { await completion.Task; }
finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }

sealed class RetryBackend(string source, string target, bool persistent, Action applied) : IAccountSwitchBackend
{
    public int Names, Closes, Applies, Launches, Sends;
    public string Current = source;
    public string? Marker;
    public Func<Task>? BeforeLaunch;
    public Task<string?> CurrentAccountAsync(CancellationToken token) => Task.FromResult<string?>(Current);
    public Task<SwitchChat[]> CaptureAsync(CancellationToken token) => Task.FromResult(new[] { new SwitchChat { Id = "fictional-chat", OriginalTitle = "Fictional work" } });
    public Task BackupNameAsync(SwitchChat chat, DateTimeOffset at, CancellationToken token) { Names++; return Task.CompletedTask; }
    public Task CloseDesktopAsync(CancellationToken token) { Closes++; return Task.CompletedTask; }
    public Task ApplyAccountAsync(string key, string? previous, CancellationToken token) { Applies++; Current = target; applied(); return Task.CompletedTask; }
    public async Task LaunchDesktopAsync(CancellationToken token)
    {
        Launches++; if (BeforeLaunch != null) await BeforeLaunch();
        if (persistent || Launches <= 2) throw new IOException("Fictional desktop relaunch temporarily unavailable");
    }
    public Task<ChatResumeState> ResumeAsync(SwitchChat chat, Action sending, CancellationToken token)
    { sending(); Sends++; Marker = chat.ContinueMessageId; return Task.FromResult(ChatResumeState.Resumed); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
