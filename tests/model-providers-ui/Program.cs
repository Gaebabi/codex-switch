using CodexAccountDesk;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ProviderUiEvidence
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Provide an absolute evidence directory.");
        Directory.CreateDirectory(args[0]);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "CodexSwitchProviderUi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var fixtureProvider = new ModelProvider { Name = "Fictional GPT API", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-ui-key", Models = new() { new ProviderModel { UpstreamId = "gpt-fixture", GenericMetadata = true, Metadata = ProviderCatalog.Generic("gpt-fixture") } } };
        new ModelProviderStore(Path.Combine(temporary, "data")).Update(settings => settings.Providers.Add(fixtureProvider));
        var service = new AccountService(new AccountVault(Path.Combine(temporary, "data")), Path.Combine(temporary, "absent-home"));
        var checks = new List<string>();
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { true, false })
            {
                Loc.SetLanguage(language);
                var owner = new Window { Left = -12000, Top = -12000 };
                string? copiedPrompt = null;
                var window = new ModelProvidersWindow(owner, new Burn0Theme(dark), service, text => copiedPrompt = text);
                var surface = (ScrollViewer)window.Content;
                surface.Background = new Burn0Theme(dark).Color("canvas");
                surface.Width = 540; surface.Height = 700;
                surface.Measure(new Size(540, 700)); surface.Arrange(new Rect(0, 0, 540, 700)); surface.UpdateLayout();
                var key = (PasswordBox)typeof(ModelProvidersWindow).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                if (key.Password.Length != 0 || key.ActualWidth <= 100 || key.ActualHeight < 25) throw new Exception("Blank protected key field must be visible.");
                var form = (StackPanel)surface.Content;
                var buttons = form.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Button>()).ToArray();
                if (buttons.Length != 4 || buttons.Any(b => b.ActualWidth < 30 || b.ActualHeight < 20)) throw new Exception("Provider actions must be reachable.");
                var bitmap = new RenderTargetBitmap(540, 700, 96, 96, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                {
                    context.DrawRectangle(new Burn0Theme(dark).Color("canvas"), null, new Rect(0, 0, 540, 700));
                }
                bitmap.Render(drawing);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(args[0], "providers-" + language + "-" + (dark ? "dark" : "light") + ".png"))) encoder.Save(file);
                checks.Add(language + "/" + (dark ? "dark" : "light") + ": actual offscreen WPF provider editor, empty key and four reachable actions");
                ((ListBox)typeof(ModelProvidersWindow).GetField("_providers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!).SelectedIndex = 1;
                var reasoning = form.Children.OfType<Expander>().Single(control => control.Name == "ReasoningSettings");
                reasoning.IsExpanded = true; surface.UpdateLayout(); reasoning.BringIntoView(); surface.UpdateLayout();
                var reasoningPanel = (StackPanel)reasoning.Content;
                var overrideCheck = reasoningPanel.Children.OfType<CheckBox>().Single();
                overrideCheck.IsChecked = true;
                surface.UpdateLayout();
                var effortChecks = reasoningPanel.Children.OfType<WrapPanel>().Single().Children.OfType<CheckBox>().ToArray();
                if (effortChecks.Length != ProviderReasoning.Efforts.Length || effortChecks.Any(check => check.ActualWidth < 20 || check.ActualHeight < 20 || !check.IsEnabled))
                    throw new Exception("All configurable reasoning levels must be laid out and enabled by the override: " + JsonSerializer.Serialize(effortChecks.Select(check => new { check.Content, check.ActualWidth, check.ActualHeight, check.IsEnabled })));
                bitmap = new RenderTargetBitmap(540, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(args[0], "reasoning-" + language + "-" + (dark ? "dark" : "light") + ".png"))) encoder.Save(file);
                checks.Add(language + "/" + (dark ? "dark" : "light") + ": model reasoning override, level choices and default laid out offscreen; no save or account action");
                reasoningPanel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = service.ModelProviders.Settings.Providers.Single().Models.Single().ReasoningOverride;
                if (saved?.Default != "medium" || !saved.Levels.SequenceEqual(new[] { "low", "medium", "high" })) throw new Exception("Editor action must save the selected model's levels and default.");
                checks.Add(language + "/" + (dark ? "dark" : "light") + ": production reasoning save action persists only fictional per-model settings");
                reasoning.IsExpanded = false;
                var handoff = form.Children.OfType<Expander>().Single(control => control.Name == "PortableHistoryHelp");
                handoff.IsExpanded = true;
                surface.UpdateLayout(); surface.ScrollToBottom(); surface.UpdateLayout();
                var copy = ((StackPanel)handoff.Content).Children.OfType<Button>().Single();
                var dataBeforeCopy = Directory.Exists(Path.Combine(temporary, "data"));
                key.Password = "fictional-unsaved-input";
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (copiedPrompt != ModelHistoryHandoff.Prompt || key.Password != "fictional-unsaved-input" || service.ModelProviders.Running || Directory.Exists(Path.Combine(temporary, "data")) != dataBeforeCopy)
                    throw new Exception("Copy handoff must copy only the fixed prompt, preserve unsaved input and perform no provider I/O.");
                key.Clear();
                if (copy.ActualWidth < 30 || copy.ActualWidth > 500 || copy.ActualHeight < 20) throw new Exception("Handoff action must fit the scrollable form.");
                bitmap = new RenderTargetBitmap(540, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(args[0], "handoff-" + language + "-" + (dark ? "dark" : "light") + ".png"))) encoder.Save(file);
                checks.Add(language + "/" + (dark ? "dark" : "light") + ": readable recovery steps and fixed-prompt copy without clipboard, provider I/O or input loss");
                // No window is shown or activated, and no account/client initialization is invoked.
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
