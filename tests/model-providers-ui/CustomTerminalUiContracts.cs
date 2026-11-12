using CodexAccountDesk;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;

internal static class CustomTerminalUiContracts
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    internal static void Verify(string temporary, List<string> checks, Action<Window, string>? capture = null)
    {
        VerifyChildEnvironment(temporary, checks);
        var home = Path.Combine(temporary, "custom terminal's home"); Directory.CreateDirectory(home);
        var executable = Path.Combine(home, "fictional codex.exe"); File.WriteAllText(executable, "Never executed by this test.");
        var profile = Path.Combine(home, "codex-switch.config.toml"); const string content = "# fictional owned profile\n";
        File.WriteAllText(profile, content);
        var data = Path.Combine(home, "store");
        var provider = new ModelProvider { Name = "Terminal fixture", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-only", Models = new() { new ProviderModel { UpstreamId = "fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("fixture") } } };
        new ModelProviderStore(data).Update(settings =>
        {
            settings.Providers.Add(provider); settings.SelectedRoute = provider.Models[0].Alias(provider.Id);
            settings.Connected = settings.RouterEnabled = settings.IsolatedProfile = settings.ConnectionWriteConfirmed = true;
            settings.CustomProfilePath = profile; settings.CustomProfileContent = content;
            settings.CustomExecutablePath = executable;
            settings.CustomExecutableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(executable)));
        });
        var service = new AccountService(new AccountVault(data), home);
        var manager = new ModelProviderManager(data, (_, _) => null, codexHome: home);
        typeof(AccountService).GetField("_modelProviders", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, manager);
        var installation = typeof(AccountService).GetProperty("Installation")!;
        installation.SetValue(service, Activator.CreateInstance(installation.PropertyType, executable, ""));
        manager.RememberEditorDraft(provider.Id, ProviderEditorDraft.From(provider, 1)); manager.Start();
        var launches = new List<string>(); string? copied = null; var fail = false;
        var owner = new Window { Left = -12000, Top = -12000 };
        var window = new ModelProvidersWindow(owner, new Burn0Theme(false), service, value => copied = value, command =>
        {
            if (fail) throw new IOException("Fictional terminal unavailable");
            launches.Add(command);
        });
        try
        {
            var primary = Field<Button>(window, "_primary");
            if ((string)primary.Content != Loc.T("providers.terminal_open") || launches.Count != 0)
                throw new Exception("Preparing/opening the editor must offer explicit custom terminal launch, never launch automatically.");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (launches.Count != 1 || !launches[0].Contains("--no-daemon --profile codex-switch") || !launches[0].Contains("terminal''s home") || File.ReadAllText(profile) != content)
                throw new Exception("An explicit click must pass the safely quoted custom profile command to the launcher without changing the profile.");
            var start = CustomTerminalLauncher.WindowsStartInfo(launches[0]);
            if (!start.UseShellExecute || start.WindowStyle != ProcessWindowStyle.Normal || !Path.IsPathFullyQualified(start.FileName) ||
                start.ArgumentList[1] != "-NoProfile" || start.ArgumentList[2] != "-NoExit" || start.ArgumentList[3] != "-EncodedCommand" ||
                Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[4])) != launches[0])
                throw new Exception("The launcher must use the system PowerShell with a visible terminal and an intact encoded command.");
            fail = true; primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (launches.Count != 1 || !Field<TextBlock>(window, "_status").Text.Contains("Fictional terminal unavailable") || !primary.IsEnabled)
                throw new Exception("Launch failure must remain visible and leave retry/copy available without closing or restarting Codex.");
            window.CreateMoreMenu().Items.OfType<MenuItem>().Single(item => (string)item.Header == Loc.T("providers.terminal_copy")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (copied != launches[0]) throw new Exception("The fallback must copy the same custom-only command.");
            var originalExecutable = File.ReadAllBytes(executable);
            File.WriteAllText(executable, "Replaced fictional executable; must never be launched."); fail = false;
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (launches.Count != 1 || !Field<TextBlock>(window, "_status").Text.Contains(Loc.T("providers.failed")))
                throw new Exception("A replaced executable must be refused before the launcher receives a command.");
            File.WriteAllBytes(executable, originalExecutable);
            File.WriteAllText(profile, "# externally changed\n"); fail = false;
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (launches.Count != 1 || !Field<TextBlock>(window, "_status").Text.Contains(Loc.T("providers.failed")))
                throw new Exception("A changed profile must be refused before the launcher receives a command.");
            File.WriteAllText(profile, content);
            checks.Add("Custom terminal: explicit injected launch, quoted home, system-shell arguments, failure/retry, copy fallback, executable replacement and foreign-profile refusal; no process executed");
            var originalLanguage = Loc.Language;
            try
            {
                foreach (var language in new[] { "en", "ko" })
                foreach (var dark in new[] { true, false })
                {
                    Loc.SetLanguage(language);
                    var confirmed = 0;
                    var confirmation = new ModelProvidersWindow(owner, new Burn0Theme(dark), service, _ => { }, _ => confirmed++);
                    try
                    {
                        var launch = Field<Button>(confirmation, "_primary");
                        if ((string)launch.Content != Loc.T("providers.terminal_open") || confirmed != 0)
                            throw new Exception("The isolated connected fixture must expose explicit terminal launch in each language/theme.");
                        launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (confirmed != 1 || Field<TextBlock>(confirmation, "_status").Text != Loc.T("providers.terminal_opened"))
                            throw new Exception("The injected launch must show its confirmation without starting a process.");
                        capture?.Invoke(confirmation, "custom-terminal-confirmation-" + language + "-" + (dark ? "dark" : "light"));
                        checks.Add(language + "/" + dark + ": isolated custom terminal confirmation rendered with injected launcher; no native terminal or owner process executed");
                    }
                    finally { confirmation.Close(); }
                }
            }
            finally { Loc.SetLanguage(originalLanguage); }
            var store = Field<ModelProviderStore>(manager, "_store");
            foreach (var missing in new string?[] { null, "removed-custom-route", "native-openai" })
            {
                store.Update(settings => settings.SelectedRoute = missing);
                typeof(ModelProvidersWindow).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
                typeof(ModelProvidersWindow).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
                if (primary.IsEnabled || Field<TextBlock>(window, "_status").Text != Loc.T("router.choose"))
                    throw new Exception("Missing, removed and native selections must disable custom terminal launch and explain that selection is required.");
            }
            store.Update(settings => settings.ConnectionWriteConfirmed = false);
            typeof(ModelProvidersWindow).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            if (!primary.IsEnabled || (string)primary.Content != Loc.T("providers.disconnect"))
                throw new Exception("An incomplete custom profile must still allow disconnect when its selection is unavailable.");
            checks.Add("Custom terminal launch disabled for missing/removed/native selections; incomplete profile disconnect remains available");
        }
        finally { window.Close(); owner.Close(); service.StopAsync().GetAwaiter().GetResult(); }
    }

    private static void VerifyChildEnvironment(string temporary, List<string> checks)
    {
        var home = Path.Combine(temporary, "environment fixture's home"); Directory.CreateDirectory(home);
        var executable = Path.ChangeExtension(typeof(ProviderUiEvidence).Assembly.Location, ".exe");
        var profile = Path.Combine(home, "codex-switch.config.toml"); const string content = "# fictional environment fixture\n"; File.WriteAllText(profile, content);
        var data = Path.Combine(home, "store");
        var provider = new ModelProvider { Name = "Environment fixture", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-only", Models = new() { new ProviderModel { UpstreamId = "fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("fixture") } } };
        new ModelProviderStore(data).Update(settings =>
        {
            settings.Providers.Add(provider); settings.SelectedRoute = provider.Models[0].Alias(provider.Id);
            settings.IsolatedProfile = settings.Connected = settings.ConnectionWriteConfirmed = true;
            settings.CustomProfilePath = profile; settings.CustomProfileContent = content; settings.CustomExecutablePath = executable;
            settings.CustomExecutableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(executable)));
        });
        var manager = new ModelProviderManager(data, (_, _) => null, codexHome: home); manager.Start();
        try
        {
            var command = manager.GetCustomTerminalCommand(executable, home, true) + "; if ($env:OPENAI_API_KEY -ne 'fictional-parent-value') { throw 'Parent environment was changed' }";
            var output = Path.Combine(home, "child-environment.json");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(argument);
            foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL", "OPENAI_IDENTITY_TOKEN_FILE", "OPENAI_IDENTITY_TOKEN_AUDIENCE" }) start.Environment[name] = "fictional-parent-value";
            start.Environment["CODEX_SWITCH_TEST_ENV_REPORT"] = output;
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("Owned environment probe did not finish."); }
            if (process.ExitCode != 0 || !File.Exists(output)) throw new Exception("Owned environment probe failed: " + errors.GetAwaiter().GetResult());
            using var result = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
            if (!result.RootElement.GetProperty("clean").GetBoolean() || result.RootElement.GetProperty("home").GetString() != home) throw new Exception("Custom child environment was not isolated.");
            checks.Add("Generated Windows command executes only the owned fixture executable, removes conflicting child credentials and preserves its parent shell environment");
        }
        finally { manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
