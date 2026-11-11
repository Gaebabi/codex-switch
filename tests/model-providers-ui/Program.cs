using CodexAccountDesk;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ProviderUiEvidence
{
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static FrameworkElement Surface(Window window)
    {
        var content = (FrameworkElement)window.Content; window.Content = null;
        var viewport = new Grid { Background = window.Background };
        TextElement.SetForeground(viewport, window.Foreground); TextElement.SetFontFamily(viewport, window.FontFamily); TextElement.SetFontSize(viewport, window.FontSize);
        viewport.Children.Add(content); return viewport;
    }
    private static void Layout(FrameworkElement root)
    { root.Width = 500; root.Height = 510; root.Measure(new Size(500, 510)); root.Arrange(new Rect(0, 0, 500, 510)); root.UpdateLayout(); }
    private static void Capture(FrameworkElement root, string path, Brush background)
    {
        Layout(root);
        var frame = new System.Windows.Threading.DispatcherFrame();
        root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        var image = new RenderTargetBitmap(500, 510, 96, 96, PixelFormats.Pbgra32);
        var canvas = new DrawingVisual(); using (var context = canvas.RenderOpen()) context.DrawRectangle(background, null, new Rect(0, 0, 500, 510));
        image.Render(canvas); image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Provide an absolute evidence directory.");
        Directory.CreateDirectory(args[0]);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexSwitchProviderUi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var fixtureProvider = new ModelProvider { Name = "Fictional GPT API", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-ui-key", Models = new() { new ProviderModel { UpstreamId = "gpt-fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("gpt-fixture") } } };
        var data = Path.Combine(temporary, "data");
        new ModelProviderStore(data).Update(settings => settings.Providers.Add(fixtureProvider));
        var service = new AccountService(new AccountVault(data), Path.Combine(temporary, "absent-home"));
        var manager = new ModelProviderManager(data, (_, _) => null, new System.Net.Http.HttpClient(new ProviderUiHttp()));
        typeof(AccountService).GetField("_modelProviders", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, manager);
        var checks = new List<string>();
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { true, false })
            {
                Loc.SetLanguage(language);
                var saved = manager.Settings.Providers.Single(); manager.RememberEditorDraft(saved.Id, ProviderEditorDraft.From(saved));
                var owner = new Window { Left = -12000, Top = -12000 }; var theme = new Burn0Theme(dark);
                string? copied = null;
                var window = new ModelProvidersWindow(owner, theme, service, value => copied = value);
                var root = Surface(window); Layout(root);
                var name = Field<TextBox>(window, "_name"); var url = Field<TextBox>(window, "_url");
                var key = Field<PasswordBox>(window, "_key"); var plainKey = Field<TextBox>(window, "_plainKey");
                var primary = Field<Button>(window, "_primary"); var entry = Field<StackPanel>(window, "_entry"); var ready = Field<StackPanel>(window, "_ready");
                var editor = Field<ProviderEditorSession>(window, "_editor");
                if (name.Text != saved.Name || url.Text != saved.BaseUrl || key.Password != saved.ApiKey || plainKey.Visibility != Visibility.Collapsed || entry.Visibility != Visibility.Visible || ready.Visibility != Visibility.Collapsed)
                    throw new Exception("Opening a saved provider must load its values into the masked first step.");
                if (primary.ActualWidth < 80 || primary.TranslatePoint(new Point(), root).X + primary.ActualWidth > 500 || Field<Button>(window, "_copyKey").ActualWidth < 32)
                    throw new Exception("The primary and key actions must fit the narrow layout.");
                if (Field<Button>(window, "_copyKey").TranslatePoint(new Point(), root).X + Field<Button>(window, "_copyKey").ActualWidth > 500)
                    throw new Exception("The key copy control must remain inside the client viewport.");
                Capture(root, Path.Combine(args[0], "providers-" + language + "-" + (dark ? "dark" : "light") + ".png"), theme.Color("canvas"));
                checks.Add(language + "/" + dark + ": saved values load in a compact two-step form with a masked key");
                Field<Button>(window, "_reveal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (plainKey.Visibility != Visibility.Visible || plainKey.Text != saved.ApiKey) throw new Exception("Eye action must reveal the saved key without clearing it.");
                plainKey.Text = "fictional-edited-key";
                Field<Button>(window, "_copyKey").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (copied != "fictional-edited-key" || key.Password != copied) throw new Exception("Copy must use the entered key and synchronize masked editing.");
                Field<Button>(window, "_reveal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (plainKey.Visibility != Visibility.Collapsed || key.Password != copied) throw new Exception("Hiding a key must preserve its exact value.");
                checks.Add(language + "/" + dark + ": eye, edit, copy and re-mask preserve the exact fictional key");
                url.Text = "invalid endpoint"; primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (url.Text != "invalid endpoint" || key.Password != copied || editor.Draft.Step != 0 || !Field<TextBlock>(window, "_status").Text.Contains(Loc.T("providers.failed")))
                    throw new Exception("Failed save must preserve all fields on the first step.");
                url.Text = saved.BaseUrl; primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(root);
                if (editor.Draft.Step != 1 || ready.Visibility != Visibility.Visible || entry.Visibility != Visibility.Collapsed || (string)primary.Content != Loc.T("providers.connect") || key.Password != copied)
                    throw new Exception("The real Save action must retain fields and advance to Connect.");
                Capture(root, Path.Combine(args[0], "saved-connection-" + language + "-" + (dark ? "dark" : "light") + ".png"), theme.Color("canvas"));
                checks.Add(language + "/" + dark + ": failed and successful saves preserve input and advance only after persistence");
                Field<Button>(window, "_back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                name.Text = "Unfinished provider edit"; window.Close();
                var reopened = new ModelProvidersWindow(owner, theme, service, value => copied = value);
                var reopenedRoot = Surface(reopened);
                if (Field<TextBox>(reopened, "_name").Text != "Unfinished provider edit" || Field<PasswordBox>(reopened, "_key").Password != "fictional-edited-key")
                    throw new Exception("Closing and reopening must retain the selected provider draft.");
                reopened.CreateMoreMenu().Items.OfType<MenuItem>().Single(item => (string)item.Header == Loc.T("providers.reset")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (Field<TextBox>(reopened, "_name").Text != saved.Name || Field<PasswordBox>(reopened, "_key").Password != "fictional-edited-key")
                    throw new Exception("Explicit Load must restore the persisted provider values.");
                checks.Add(language + "/" + dark + ": close/reopen preserves drafts and explicit Load restores saved values");
                Field<Button>(reopened, "_primary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var advanced = Field<StackPanel>(reopened, "_ready").Children.OfType<Expander>().Single(); advanced.IsExpanded = true;
                var advancedPanel = (StackPanel)advanced.Content;
                var reasoning = advancedPanel.Children.OfType<Expander>().Single(control => control.Name == "ReasoningSettings"); reasoning.IsExpanded = true;
                var panel = (StackPanel)reasoning.Content; var overrideCheck = panel.Children.OfType<CheckBox>().Single(); overrideCheck.IsChecked = true;
                var efforts = panel.Children.OfType<WrapPanel>().Single().Children.OfType<CheckBox>().ToArray();
                if (efforts.Length != ProviderReasoning.Efforts.Length || efforts.Any(check => !check.IsEnabled)) throw new Exception("Advanced reasoning controls must remain available.");
                panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (manager.Settings.Providers.Single().Models.Single().ReasoningOverride?.Default != "medium") throw new Exception("Advanced reasoning save must persist.");
                Capture(reopenedRoot, Path.Combine(args[0], "reasoning-" + language + "-" + (dark ? "dark" : "light") + ".png"), theme.Color("canvas"));
                var handoff = advancedPanel.Children.OfType<Expander>().Single(control => control.Name == "PortableHistoryHelp"); handoff.IsExpanded = true;
                ((StackPanel)handoff.Content).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (copied != ModelHistoryHandoff.Prompt || Field<PasswordBox>(reopened, "_key").Password != "fictional-edited-key") throw new Exception("Handoff copy must preserve provider fields.");
                Capture(reopenedRoot, Path.Combine(args[0], "handoff-" + language + "-" + (dark ? "dark" : "light") + ".png"), theme.Color("canvas"));
                checks.Add(language + "/" + dark + ": advanced reasoning and handoff actions retain saved provider fields");
                reopened.CreateMoreMenu().Items.OfType<MenuItem>().Single(item => (string)item.Header == Loc.T("providers.reuse")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (Field<TextBox>(reopened, "_url").Text != saved.BaseUrl || Field<PasswordBox>(reopened, "_key").Password != "fictional-edited-key" || Field<ProviderEditorSession>(reopened, "_editor").SelectedId != null)
                    throw new Exception("Reuse must create a new draft without losing connection values.");
                reopened.Close();
                checks.Add(language + "/" + dark + ": explicit reuse carries saved connection values into a new draft");
            }
            var fixtureClaims = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"sub\":\"home-ui-fixture\",\"email\":\"home@example.invalid\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var fixtureAccount = service.Vault.Upsert(JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { account_id = "home-ui-fixture", id_token = "e30." + fixtureClaims + ".fictional", access_token = "fictional-home-access", refresh_token = "fictional-home-refresh" } }));
            fixtureAccount.Alias = "Demo account"; fixtureAccount.IsCurrent = true;
            typeof(AccountService).GetProperty("CurrentKey")!.SetValue(service, fixtureAccount.Key);
            foreach (var language in new[] { "en", "ko" })
            {
                var main = new MainWindow(service, new Program.Options { DataDirectory = Path.Combine(temporary, "data"), CodexHome = Path.Combine(temporary, "absent-home"), UiEvidenceDirectory = args[0] });
                typeof(MainWindow).GetField("_ready", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                main.PrepareUiEvidence(language, false);
                var root = (FrameworkElement)main.Content; root.Width = 500; root.Height = 320;
                ((Panel)root).Background = main.Background;
                root.Measure(new Size(500, 320)); root.Arrange(new Rect(0, 0, 500, 320)); root.UpdateLayout();
                var restart = (MenuItem)typeof(MainWindow).GetField("_restartCodex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                var models = (MenuItem)typeof(MainWindow).GetField("_providers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                var settings = main.HomeActionsMenu();
                var gear = (Button)typeof(MainWindow).GetField("_homeActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                if (!restart.IsEnabled || !models.IsEnabled || !settings.Items.Contains(restart) || !settings.Items.Contains(models) || gear.ActualWidth < 28 || gear.TranslatePoint(new Point(), root).X + gear.ActualWidth > 500)
                    throw new Exception("Settings gear must expose enabled model and restart actions at the minimum width.");
                var titleActions = ((Grid)((Grid)root).Children[0]).Children.OfType<StackPanel>().Single(panel => panel.Name == "TitleBarActions");
                if (titleActions.Children.OfType<Button>().Count() != 6 || titleActions.Children.OfType<Button>().Any(button => button.Name is "LanguageButton" or "ThemeButton" or "ModelProvidersButton" or "RestartCodexButton"))
                    throw new Exception("Titlebar must contain only add/refresh/settings and the three native caption controls.");
                var automatic = settings.Items.OfType<MenuItem>().Single(item => item.Name == "AutomaticSwitchMenuItem");
                var languageSettings = settings.Items.OfType<MenuItem>().Single(item => item.Name == "LanguageSettingsMenuItem");
                var themeSettings = settings.Items.OfType<MenuItem>().Single(item => item.Name == "ThemeSettingsMenuItem");
                if (!automatic.Items.OfType<MenuItem>().Any(item => item.Name == "AllowCreditUse") || languageSettings.Items.OfType<MenuItem>().Count() < Loc.Languages.Count() || themeSettings.Items.Count != 2)
                    throw new Exception("Gear settings must retain automation/credits, all language choices and both themes.");
                settings.PlacementTarget = gear;
                settings.ApplyTemplate(); settings.Measure(new Size(320, 520)); settings.Arrange(new Rect(0, 0, settings.DesiredSize.Width, settings.DesiredSize.Height)); settings.UpdateLayout();
                var settingsBitmap = new RenderTargetBitmap((int)Math.Ceiling(settings.ActualWidth), (int)Math.Ceiling(settings.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                settingsBitmap.Render(settings);
                var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                using (var file = File.Create(Path.Combine(args[0], "home-settings-" + language + ".png"))) settingsEncoder.Save(file);
                var bitmap = new RenderTargetBitmap(500, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(args[0], "main-restart-" + language + ".png"))) encoder.Save(file);
                checks.Add(language + ": settings gear exposes enabled Models and Restart at minimum width; action not invoked");
                var originalLanguage = Loc.Language;
                var targetLanguage = language == "ko" ? "en" : "ko";
                languageSettings.Items.OfType<MenuItem>().Single(item => item.Header as string == Loc.Languages.Single(info => info.Code == targetLanguage).NativeName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (Loc.Language != targetLanguage || service.Vault.Data.Language != targetLanguage) throw new Exception("Settings language action must apply and persist its selection.");
                main.ChangeLanguage(originalLanguage);
                var themeMenu = main.HomeActionsMenu().Items.OfType<MenuItem>().Single(item => item.Name == "ThemeSettingsMenuItem");
                themeMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == Loc.T("다크")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (service.Vault.Data.Theme != "dark") throw new Exception("Settings theme action must persist the selected appearance.");
                var originalAutomatic = service.Vault.Data.AutomaticSwitchEnabled;
                var toggle = (MenuItem)main.HomeActionsMenu().Items.OfType<MenuItem>().Single(item => item.Name == "AutomaticSwitchMenuItem").Items[0];
                toggle.IsChecked = !originalAutomatic; toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (new AccountVault(service.Vault.DirectoryPath).Data.AutomaticSwitchEnabled != !originalAutomatic) throw new Exception("Settings automatic toggle must persist through the production action.");
                toggle.IsChecked = originalAutomatic; toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                checks.Add(language + ": gear language, theme and automatic-toggle actions preserve production persistence paths");
                typeof(MainWindow).GetField("_switching", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                typeof(MainWindow).GetMethod("UpdateControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                if (restart.IsEnabled) throw new Exception("Restart must be disabled while a handoff is in progress.");
                checks.Add(language + ": duplicate restart action disabled while busy");
            }
            HomeLayoutContracts.Verify(service, args[0], checks);
            SessionCounterContracts.Verify(args[0], checks);
            File.WriteAllText(Path.Combine(args[0], "ui-verification.json"), JsonSerializer.Serialize(new
            {
                checks,
                scope = "Actual production WPF component rendered offscreen with empty disposable storage. No native window, owner account or Codex picker acceptance."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Provider editor offscreen UI checks passed: " + checks.Count); return 0;
        }
        finally
        {
            service.StopAsync().GetAwaiter().GetResult();
            if (Guid.TryParseExact(Path.GetFileName(temporary), "N", out _)) Directory.Delete(temporary, true);
        }
    }
}

internal sealed class ProviderUiHttp : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new System.Net.Http.StringContent("{\"data\":[{\"id\":\"gpt-fixture\"}]}", System.Text.Encoding.UTF8, "application/json") });
}
