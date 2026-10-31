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
            service.Vault.Data.Accounts.Add(new AccountProfile { Key = "fictional", Alias = "Demo account", IsCurrent = true });
            typeof(AccountService).GetProperty("CurrentKey")!.SetValue(service, "fictional");
            foreach (var language in new[] { "en", "ko" })
            {
                var main = new MainWindow(service, new Program.Options { DataDirectory = Path.Combine(temporary, "data"), CodexHome = Path.Combine(temporary, "absent-home"), UiEvidenceDirectory = args[0] });
                typeof(MainWindow).GetField("_ready", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                main.PrepareUiEvidence(language, false);
                var root = (FrameworkElement)main.Content; root.Width = 500; root.Height = 320;
                ((Panel)root).Background = main.Background;
                root.Measure(new Size(500, 320)); root.Arrange(new Rect(0, 0, 500, 320)); root.UpdateLayout();
                var restart = (Button)typeof(MainWindow).GetField("_restartCodex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                var models = (Button)typeof(MainWindow).GetField("_providers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                var count = (FrameworkElement)typeof(MainWindow).GetField("_sessionCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                var x = restart.TranslatePoint(new Point(), root).X; var end = x + restart.ActualWidth;
                if (!restart.IsEnabled || restart.ActualWidth < 65 || x < models.TranslatePoint(new Point(), root).X + models.ActualWidth || end > count.TranslatePoint(new Point(), root).X)
                    throw new Exception("Restart geometry: " + JsonSerializer.Serialize(new { enabled = restart.IsEnabled, width = restart.ActualWidth, x, end, modelEnd = models.TranslatePoint(new Point(), root).X + models.ActualWidth, counterX = count.TranslatePoint(new Point(), root).X }));
                var bitmap = new RenderTargetBitmap(500, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(args[0], "main-restart-" + language + ".png"))) encoder.Save(file);
                checks.Add(language + ": enabled Restart Codex in minimum-width main layout, separate from Models and counters; action not invoked");
                typeof(MainWindow).GetField("_switching", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                typeof(MainWindow).GetMethod("UpdateControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                if (restart.IsEnabled) throw new Exception("Restart must be disabled while a handoff is in progress.");
                checks.Add(language + ": duplicate restart action disabled while busy");
            }
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
