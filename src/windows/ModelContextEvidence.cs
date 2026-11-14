using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CodexAccountDesk;

internal static class ModelContextEvidence
{
    internal static async Task CaptureAsync(MainWindow owner, AccountService dashboard, string directory)
    {
        var root = Path.GetDirectoryName(dashboard.Vault.DirectoryPath)!;
        if (!Path.GetFileName(root).StartsWith("codex-ui-evidence-", StringComparison.Ordinal))
            throw new InvalidOperationException("Context evidence requires the isolated fictional native UI fixture.");
        var data = Path.Combine(root, "context-data");
        var provider = new ModelProvider { Name = "Fictional API", BaseUrl = "https://fixture.example.invalid/v1", ApiKey = "fictional-context-key",
            Models = new() { new ProviderModel { UpstreamId = "demo-model", Metadata = ProviderCatalog.Generic("demo-model") } } };
        new ModelProviderStore(data).Update(settings => settings.Providers.Add(provider));
        var service = new AccountService(new AccountVault(data), Path.Combine(root, "context-home"));
        static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        try
        {
            foreach (var dark in new[] { true, false })
            foreach (var language in new[] { "en", "ko" })
            {
                owner.PrepareUiEvidence(language, dark);
                service.ModelProviders.RememberEditorDraft(provider.Id, new ProviderEditorDraft { Name = provider.Name, BaseUrl = provider.BaseUrl, Step = 1 });
                var window = new ModelProvidersWindow(owner, new Burn0Theme(dark), service) { ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); window.UpdateLayout();
                    var panel = Field<StackPanel>(window, "_contextPanel");
                    var expander = ((StackPanel)Field<StackPanel>(window, "_ready").Parent).Children.OfType<Expander>().Single(item => item.Name == "ContextSettings");
                    expander.IsExpanded = true;
                    var target = Field<Button>(window, "_contextModel");
                    void PickContext(int index)
                    {
                        target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        target.ContextMenu.Items.OfType<MenuItem>().ElementAt(index).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    }
                    var size = Field<TextBox>(window, "_contextWindow");
                    var compact = Field<TextBox>(window, "_contextCompact");
                    var save = panel.Children.OfType<Button>().Single();
                    PickContext(0); size.Text = "128000"; compact.Text = "100000";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (service.ModelProviders.Settings.NativeContext != new ModelContextOptions(128000, 100000))
                        throw new InvalidOperationException("The native OpenAI context controls did not save.");
                    PickContext(1); size.Text = "64000"; compact.Text = "48000";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (service.ModelProviders.Settings.Providers.Single().Models.Single().ContextOverride != new ModelContextOptions(64000, 48000))
                        throw new InvalidOperationException("The native custom-model context controls did not save.");
                    window.UpdateLayout(); Field<ScrollViewer>(window, "_scroll").ScrollToEnd();
                    await Task.Delay(150); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    var bounds = save.TransformToAncestor(window).TransformBounds(new Rect(save.RenderSize));
                    if (!save.IsVisible || bounds.Top < 0 || bounds.Bottom > window.ActualHeight || bounds.Right > window.ActualWidth)
                        throw new InvalidOperationException("Context Save must remain visible inside the native window.");
                    typeof(NativeUiEvidence).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Window), typeof(string) }, null)!
                        .Invoke(null, new object[] { window, Path.Combine(directory, language + "-" + (dark ? "dark" : "light") + "-context.png") });
                }
                finally { window.Close(); }
            }
        }
        finally { await service.StopAsync(); }
    }
}
