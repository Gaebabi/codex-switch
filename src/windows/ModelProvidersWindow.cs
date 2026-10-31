using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class ModelProvidersWindow : Window
{
    private readonly AccountService _service;
    private readonly ModelProviderManager _manager;
    private readonly Burn0Theme _theme;
    private readonly ListBox _providers = new() { Height = 64, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new(), _url = new();
    private readonly TextBox _included = new() { AcceptsReturn = true, Height = 54, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly PasswordBox _key = new();
    private readonly ListBox _reasoningModel = new() { Height = 64 }, _reasoningDefault = new() { ItemsSource = ProviderReasoning.Efforts, Height = 64, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))) };
    private readonly CheckBox _reasoningOverride = new();
    private readonly Dictionary<string, CheckBox> _reasoningLevels = new();
    private readonly StackPanel _reasoningPanel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _models = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> _actions = new();
    private readonly CancellationTokenSource _stop = new();
    private string? _selected;
    private bool _busy;
    private sealed record Choice(string? Id, string Name) { public override string ToString() => Name; }
    internal ModelProvidersWindow(Window owner, Burn0Theme theme, AccountService service, Action<string>? copyHandoff = null)
    {
        if (PresentationSource.FromVisual(owner) != null) Owner = owner;
        _theme = theme; _service = service; _manager = service.ModelProviders;
        Title = Loc.T("providers.title"); Width = 560; Height = 720; MinWidth = 420; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner.Left < -10000) { WindowStartupLocation = WindowStartupLocation.Manual; Left = owner.Left; Top = owner.Top; ShowInTaskbar = false; }
        Resources = owner.Resources; Background = theme.Color("canvas"); Foreground = theme.Color("text"); FontFamily = theme.Font;
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var form = new StackPanel { Margin = new Thickness(18) };
        void Field(string key, Control control)
        {
            form.Children.Add(new TextBlock { Text = Loc.T(key), Margin = new Thickness(0, 12, 0, 5) });
            control.MinHeight = 30; form.Children.Add(control);
            control.Background = theme.Color("surface"); control.Foreground = theme.Color("text"); control.BorderBrush = theme.Color("divider");
            if (control is TextBox or PasswordBox) control.Padding = new Thickness(6, 4, 6, 4);
        }
        void Hint(string key) => form.Children.Add(new TextBlock { Text = Loc.T(key), TextWrapping = TextWrapping.Wrap, Foreground = theme.Color("muted"), FontSize = 12, Margin = new Thickness(0, 8, 0, 4) });
        Hint("providers.intro"); Field("providers.select", _providers);
        Field("providers.name", _name); Field("providers.url", _url); Field("providers.key", _key); Hint("providers.key_hint");
        Field("providers.include", _included);
        var edit = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
        edit.Children.Add(Action("providers.save", SaveAsync)); edit.Children.Add(Action("providers.remove", RemoveAsync)); form.Children.Add(edit);
        form.Children.Add(_models);
        _reasoningOverride.Content = Loc.T("providers.reasoning_override");
        _reasoningOverride.MinHeight = 28; _reasoningOverride.Foreground = theme.Color("text");
        void ReasoningField(string key, Control control)
        {
            _reasoningPanel.Children.Add(new TextBlock { Text = Loc.T(key), Foreground = theme.Color("text"), Margin = new Thickness(0, 8, 0, 4) });
            control.MinHeight = 28; control.Background = theme.Color("surface"); control.Foreground = theme.Color("text"); control.BorderBrush = theme.Color("divider"); _reasoningPanel.Children.Add(control);
        }
        ReasoningField("providers.reasoning_model", _reasoningModel); _reasoningPanel.Children.Add(_reasoningOverride);
        var levels = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var effort in ProviderReasoning.Efforts)
        {
            var check = new CheckBox { Content = effort, MinHeight = 28, Foreground = theme.Color("text"), Margin = new Thickness(0, 0, 10, 5) };
            _reasoningLevels.Add(effort, check); levels.Children.Add(check);
        }
        _reasoningPanel.Children.Add(levels); ReasoningField("providers.reasoning_default", _reasoningDefault);
        _reasoningPanel.Children.Add(new TextBlock { Text = Loc.T("providers.reasoning_hint"), TextWrapping = TextWrapping.Wrap, Foreground = theme.Color("muted"), FontSize = 12, Margin = new Thickness(0, 8, 0, 8) });
        _reasoningPanel.Children.Add(Action("providers.reasoning_save", SaveReasoningAsync));
        form.Children.Add(new Expander { Name = "ReasoningSettings", Header = new TextBlock { Text = Loc.T("providers.reasoning_title"), Foreground = theme.Color("text") }, Content = _reasoningPanel });
        _reasoningModel.SelectionChanged += (_, _) => SelectReasoning();
        _reasoningOverride.Checked += (_, _) => UpdateReasoningControls(); _reasoningOverride.Unchecked += (_, _) => UpdateReasoningControls();
        var connection = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
        connection.Children.Add(Action("providers.connect", () => _service.ConnectModelProvidersAsync(_stop.Token)));
        connection.Children.Add(Action("providers.disconnect", () => _service.DisconnectModelProvidersAsync(_stop.Token)));
        form.Children.Add(connection);
        var handoff = new StackPanel();
        handoff.Children.Add(new TextBlock { Text = Loc.T("providers.handoff_steps"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = theme.Color("muted"), Margin = new Thickness(0, 8, 0, 8) });
        var copy = new Button { Name = "CopyHandoffPrompt", Content = Loc.T("providers.handoff_copy"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5), Background = theme.Color("surface"), Foreground = theme.Color("text") };
        copy.Click += (_, _) =>
        {
            try { (copyHandoff ?? Clipboard.SetText)(ModelHistoryHandoff.Prompt); _status.Text = Loc.T("providers.handoff_copied"); }
            catch (Exception) { _status.Text = Loc.T("providers.handoff_copy_failed"); }
        };
        _actions.Add(copy); handoff.Children.Add(copy);
        form.Children.Add(new Expander { Name = "PortableHistoryHelp", Header = new TextBlock { Text = Loc.T("providers.handoff_title"), Foreground = theme.Color("text") }, Content = handoff, Margin = new Thickness(0, 6, 0, 6) });
        var details = new StackPanel();
        foreach (var key in new[] { "providers.metadata_hint", "providers.connection_hint", "providers.history_hint" })
            details.Children.Add(new TextBlock { Text = Loc.T(key), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 6), Foreground = theme.Color("muted") });
        form.Children.Add(new Expander { Header = new TextBlock { Text = Loc.T("providers.details"), Foreground = theme.Color("text") }, Content = details, Margin = new Thickness(0, 6, 0, 6) });
        _status.Margin = new Thickness(0, 12, 0, 4); form.Children.Add(_status);
        Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _providers.SelectionChanged += (_, _) => Select();
        Closing += (_, _) => _stop.Cancel();
        RefreshChoices(null);
    }
    private Button Action(string message, Func<Task> action)
    {
        var button = new Button { Content = Loc.T(message), Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 5, 10, 5), Background = _theme.Color("surface"), Foreground = _theme.Color("text") };
        button.Click += async (_, _) => await RunAsync(action, message == "providers.connect" ? "providers.prepared" : message == "providers.disconnect" ? "providers.restored" : "providers.saved"); _actions.Add(button); return button;
    }
    private void RefreshChoices(string? selected)
    {
        var choices = new[] { new Choice(null, Loc.T("providers.new")) }.Concat(_manager.Settings.Providers.Select(p => new Choice(p.Id, p.Name))).ToArray();
        _providers.ItemsSource = choices; _providers.SelectedItem = choices.FirstOrDefault(p => p.Id == selected) ?? choices[0];
        var settings = _manager.Settings;
        _status.Text = Loc.T(settings.Connected ? settings.ConnectionWriteConfirmed ? "providers.prepared" : "providers.incomplete" : "providers.not_connected");
    }
    private void Select()
    {
        if (_providers.SelectedItem is not Choice choice) return;
        _selected = choice.Id; var provider = _manager.Settings.Providers.FirstOrDefault(p => p.Id == _selected);
        _name.Text = provider?.Name ?? ""; _url.Text = provider?.BaseUrl ?? ""; _key.Clear();
        _included.Text = string.Join("\n", provider?.IncludedModelIds ?? new List<string>());
        _models.Text = provider == null ? "" : Loc.F("providers.model_count", provider.Models.Count) + "\n" + string.Join("\n", provider.Models.Take(3).Select(m => m.UpstreamId));
        _reasoningPanel.IsEnabled = provider != null;
        _reasoningModel.ItemsSource = provider?.Models.Select(m => m.UpstreamId).ToArray();
        _reasoningModel.SelectedIndex = provider == null ? -1 : 0; SelectReasoning();
    }
    private void SelectReasoning()
    {
        var options = _manager.Settings.Providers.FirstOrDefault(p => p.Id == _selected)?.Models.FirstOrDefault(m => m.UpstreamId == _reasoningModel.SelectedItem as string)?.ReasoningOverride;
        _reasoningOverride.IsChecked = options != null;
        foreach (var pair in _reasoningLevels) pair.Value.IsChecked = options?.Levels.Contains(pair.Key) ?? pair.Key is "low" or "medium" or "high";
        _reasoningDefault.SelectedItem = options?.Default ?? "medium"; UpdateReasoningControls();
    }
    private void UpdateReasoningControls()
    {
        foreach (var check in _reasoningLevels.Values) check.IsEnabled = _reasoningOverride.IsChecked == true;
        _reasoningDefault.IsEnabled = _reasoningOverride.IsChecked == true;
    }
    private async Task SaveReasoningAsync()
    {
        if (_selected == null || _reasoningModel.SelectedItem is not string model) return;
        await _manager.SaveReasoningAsync(_selected, model, _reasoningOverride.IsChecked != true ? null : new ProviderReasoningOptions
        {
            Levels = _reasoningLevels.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList(), Default = (string?)_reasoningDefault.SelectedItem ?? "medium"
        }, _stop.Token);
    }
    private async Task SaveAsync()
    {
        var provider = await _manager.SaveAsync(_selected, _name.Text, _url.Text, _key.Password, _stop.Token, _included.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        RefreshChoices(provider.Id);
    }
    private async Task RemoveAsync()
    {
        if (_selected == null) return;
        await _manager.RemoveAsync(_selected, _stop.Token); RefreshChoices(null);
    }
    private async Task RunAsync(Func<Task> action, string success)
    {
        if (_busy) return; _busy = true;
        _name.IsEnabled = _url.IsEnabled = _key.IsEnabled = false;
        _included.IsEnabled = false;
        _reasoningPanel.IsEnabled = false;
        foreach (var button in _actions) button.IsEnabled = false; _providers.IsEnabled = false;
        _status.Text = Loc.T("providers.working");
        try { await action(); _status.Text = Loc.T(success); }
        catch (OperationCanceledException) { _status.Text = Loc.T("providers.cancelled"); }
        catch (Exception error) { _status.Text = Loc.T("providers.failed") + " " + error.Message; }
        finally { _key.Clear(); _busy = false; foreach (var button in _actions) button.IsEnabled = true; _providers.IsEnabled = _name.IsEnabled = _url.IsEnabled = _key.IsEnabled = _included.IsEnabled = true; _reasoningPanel.IsEnabled = _selected != null; }
    }
}
