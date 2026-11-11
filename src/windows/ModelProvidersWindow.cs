using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class ModelProvidersWindow : Window
{
    private readonly AccountService _service;
    private readonly ModelProviderManager _manager;
    private readonly Burn0Theme _theme;
    private readonly Action<string> _copyText;
    private ProviderEditorSession _editor;
    private readonly TextBox _name = new() { MaxLength = 80 }, _url = new() { MaxLength = 32768 };
    private readonly TextBox _included = new() { AcceptsReturn = true, MaxLength = 65536, Height = 76, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly PasswordBox _key = new() { MaxLength = 16384 };
    private readonly TextBox _plainKey = new() { MaxLength = 16384, Visibility = Visibility.Collapsed };
    private readonly StackPanel _entry = new(), _ready = new(), _reasoningPanel = new();
    private readonly TextBlock _stepTitle = new(), _models = new() { TextWrapping = TextWrapping.Wrap }, _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _reasoningModel = new(), _reasoningDefault = new() { ItemsSource = ProviderReasoning.Efforts };
    private readonly CheckBox _reasoningOverride = new();
    private readonly Dictionary<string, CheckBox> _reasoningLevels = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Button _providerPicker, _more, _reveal, _copyKey, _primary, _back;
    private readonly ScrollViewer _scroll;
    private bool _busy, _loading, _syncingKey, _keyVisible, _closed;
    private string? _message;
    internal bool RestartRequested { get; private set; }

    internal ModelProvidersWindow(Window owner, Burn0Theme theme, AccountService service, Action<string>? copyHandoff = null)
    {
        if (PresentationSource.FromVisual(owner) != null) Owner = owner;
        _theme = theme; _service = service; _manager = service.ModelProviders; _editor = new(_manager);
        _copyText = copyHandoff ?? Clipboard.SetText;
        Title = Loc.T("providers.title"); Width = 540; Height = 560; MinWidth = 440; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner.Left < -10000) { WindowStartupLocation = WindowStartupLocation.Manual; Left = owner.Left; Top = owner.Top; ShowInTaskbar = false; }
        Resources = owner.Resources; Background = theme.Color("canvas"); Foreground = theme.Color("text"); FontFamily = theme.Font;
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(Label("providers.select"));
        var picker = new Grid(); picker.ColumnDefinitions.Add(new()); picker.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _providerPicker = Button(""); _providerPicker.HorizontalContentAlignment = HorizontalAlignment.Left; _providerPicker.Click += (_, _) => ShowProviders();
        _more = IconButton("more", "providers.more"); _more.Margin = new Thickness(8, 0, 0, 0); _more.Click += (_, _) => ShowMore();
        picker.Children.Add(_providerPicker); Grid.SetColumn(_more, 1); picker.Children.Add(_more); header.Children.Add(picker); root.Children.Add(header);
        var form = new StackPanel { Margin = new Thickness(0, 20, 0, 8) };
        _stepTitle.FontSize = 21; _stepTitle.FontWeight = FontWeights.SemiBold; _stepTitle.Margin = new Thickness(0, 0, 0, 16); form.Children.Add(_stepTitle);
        Field(_entry, "providers.name", _name); Field(_entry, "providers.url", _url); _entry.Children.Add(Label("providers.key"));
        var keyRow = new Grid(); keyRow.ColumnDefinitions.Add(new()); keyRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); keyRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var keyInput = new Grid(); Input(_key); Input(_plainKey); keyInput.Children.Add(_key); keyInput.Children.Add(_plainKey); keyRow.Children.Add(Frame(keyInput));
        _reveal = IconButton("eye", "providers.key_show"); _reveal.Margin = new Thickness(6, 0, 0, 0); _reveal.Click += (_, _) => { _keyVisible = !_keyVisible; UpdateKeyVisibility(); };
        _copyKey = IconButton("copy", "providers.key_copy"); _copyKey.Margin = new Thickness(4, 0, 0, 0); _copyKey.Click += (_, _) => Copy(_editor.ApiKey, "providers.key_copied");
        Grid.SetColumn(_reveal, 1); keyRow.Children.Add(_reveal); Grid.SetColumn(_copyKey, 2); keyRow.Children.Add(_copyKey); _entry.Children.Add(keyRow);
        _entry.Children.Add(new TextBlock { Text = Loc.T("providers.key_hint"), Foreground = theme.Color("muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        AutomationProperties.SetName(_key, Loc.T("providers.key")); AutomationProperties.SetName(_plainKey, Loc.T("providers.key"));
        var filters = new StackPanel(); Input(_included); filters.Children.Add(Frame(_included));
        _entry.Children.Add(new Expander { Header = Loc.T("providers.include"), Foreground = _theme.Color("muted"), Content = filters, Margin = new Thickness(0, 0, 0, 8) }); form.Children.Add(_entry);
        _models.FontSize = 14; _models.LineHeight = 23; _models.Margin = new Thickness(0, 0, 0, 18); _ready.Children.Add(_models); BuildAdvanced(_ready); form.Children.Add(_ready);
        _scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_scroll, 1); root.Children.Add(_scroll);
        var footer = new StackPanel(); _status.FontSize = 12; _status.Margin = new Thickness(0, 6, 0, 12); footer.Children.Add(_status);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _back = Button(""); _back.HorizontalAlignment = HorizontalAlignment.Left; _back.Click += (_, _) => { if (_editor.Draft.Step == 1) { _editor.Edit(); _message = null; Render(); } else Close(); };
        _primary = Button("", true); _primary.IsDefault = true; _primary.Click += async (_, _) => await PrimaryAsync();
        actions.Children.Add(_back); Grid.SetColumn(_primary, 1); actions.Children.Add(_primary); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _name.TextChanged += (_, _) => FieldsChanged(); _url.TextChanged += (_, _) => FieldsChanged(); _included.TextChanged += (_, _) => FieldsChanged();
        _key.PasswordChanged += (_, _) => KeyChanged(_key.Password); _plainKey.TextChanged += (_, _) => KeyChanged(_plainKey.Text);
        Closing += (_, args) =>
        {
            try { _editor.Persist(); _closed = true; _stop.Cancel(); }
            catch (Exception error) { RestartRequested = false; args.Cancel = true; _message = Loc.T("providers.failed") + " " + error.Message; Render(); }
        };
        LoadFields();
    }
    private TextBlock Label(string key) => new() { Text = Loc.T(key), Foreground = _theme.Color("muted"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
    private void Input(Control control)
    {
        control.MinHeight = 38; control.Padding = new Thickness(10, 8, 10, 8); control.BorderThickness = new Thickness(0);
        control.Background = Brushes.Transparent; control.Foreground = _theme.Color("text"); control.FontSize = 14;
    }
    private Border Frame(UIElement child) => new() { Child = child, Background = _theme.Color("surface"), BorderBrush = _theme.Color("divider"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
    private void Field(Panel panel, string key, Control control)
    { panel.Children.Add(Label(key)); AutomationProperties.SetName(control, Loc.T(key)); Input(control); var frame = Frame(control); frame.Margin = new Thickness(0, 0, 0, 14); panel.Children.Add(frame); }
    private Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, MinHeight = 36, Padding = new Thickness(13, 8, 13, 8), BorderThickness = new Thickness(0),
            Background = _theme.Color(primary ? "accent" : "surface"), Foreground = _theme.Color(primary ? "canvas" : "text"), FontSize = 13 };
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty)); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var style = new Style(typeof(Button)); var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, 0.45)); style.Triggers.Add(disabled); button.Style = style;
        return button;
    }
    private Button IconButton(string icon, string label)
    {
        var button = Button(""); button.Width = 36; button.Padding = new Thickness(8); button.Content = Burn0Theme.Icon(icon, _theme.Color("text"), 18);
        button.ToolTip = Loc.T(label); AutomationProperties.SetName(button, Loc.T(label)); return button;
    }
    private void FieldsChanged()
    {
        if (_loading) return;
        _editor.Draft.Name = _name.Text; _editor.Draft.BaseUrl = _url.Text; _editor.Draft.ModelIds = _included.Text; _message = null; Render();
    }
    private void KeyChanged(string value)
    {
        if (_loading || _syncingKey) return;
        _editor.SetApiKey(value); _syncingKey = true;
        try { if (_key.Password != value) _key.Password = value; if (_plainKey.Text != value) _plainKey.Text = value; }
        finally { _syncingKey = false; }
        _message = null; Render();
    }
    private void LoadFields()
    {
        _loading = true;
        try
        {
            _name.Text = _editor.Draft.Name; _url.Text = _editor.Draft.BaseUrl; _included.Text = _editor.Draft.ModelIds;
            _key.Password = _editor.ApiKey; _plainKey.Text = _editor.ApiKey; _keyVisible = false;
            var provider = _manager.Settings.Providers.FirstOrDefault(item => item.Id == _editor.SelectedId);
            _reasoningModel.ItemsSource = provider?.Models.Select(item => item.UpstreamId).ToArray(); _reasoningModel.SelectedIndex = provider == null ? -1 : 0;
        }
        finally { _loading = false; }
        SelectReasoning(); UpdateKeyVisibility(); Render();
    }
    private void UpdateKeyVisibility()
    {
        _key.Visibility = _keyVisible ? Visibility.Collapsed : Visibility.Visible; _plainKey.Visibility = _keyVisible ? Visibility.Visible : Visibility.Collapsed;
        _reveal.Content = Burn0Theme.Icon(_keyVisible ? "eye-off" : "eye", _theme.Color("text"), 18);
        _reveal.ToolTip = Loc.T(_keyVisible ? "providers.key_hide" : "providers.key_show"); AutomationProperties.SetName(_reveal, (string)_reveal.ToolTip);
    }
    private void Copy(string value, string success)
    {
        try { _copyText(value); _message = Loc.T(success); }
        catch (Exception) { _message = Loc.T("providers.copy_failed"); }
        Render();
    }
    private void ShowProviders()
    { var menu = CreateProviderMenu(); _providerPicker.ContextMenu = menu; menu.IsOpen = true; }
    internal ContextMenu CreateProviderMenu()
    {
        var menu = DropdownTheme.Create(_providerPicker, _theme);
        void Add(string? id, string name)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = id == _editor.SelectedId };
            item.Click += (_, _) => { try { _editor.Select(id); _message = null; LoadFields(); } catch (Exception error) { _message = error.Message; Render(); } }; menu.Items.Add(item);
        }
        Add(null, Loc.T("providers.new")); foreach (var provider in _manager.Settings.Providers) Add(provider.Id, provider.Name);
        return menu;
    }
    private void ShowMore()
    { var menu = CreateMoreMenu(); _more.ContextMenu = menu; menu.IsOpen = true; }
    internal ContextMenu CreateMoreMenu()
    {
        var menu = DropdownTheme.Create(_more, _theme);
        void Add(string label, bool enabled, Func<Task> action)
        {
            var item = new MenuItem { Header = Loc.T(label), IsEnabled = enabled }; item.Click += async (_, _) => await RunAsync(action); menu.Items.Add(item);
        }
        if (_editor.IsSaved)
        {
            Add("providers.reuse", true, () => { _editor.UseForNewProvider(); _message = null; LoadFields(); return Task.CompletedTask; });
            Add("providers.reset", true, () => { _editor.LoadSaved(); _message = null; LoadFields(); return Task.CompletedTask; });
            Add("providers.remove", true, async () => { await _manager.RemoveAsync(_editor.SelectedId!, _stop.Token); _editor = new(_manager); _message = null; LoadFields(); });
        }
        if (_manager.Settings.Connected) Add("providers.disconnect", true, async () => { await _service.DisconnectModelProvidersAsync(_stop.Token); _message = null; });
        return menu;
    }
    private async Task PrimaryAsync()
    {
        if (_busy) return;
        if (_editor.Draft.Step == 0) await RunAsync(async () => { await _editor.SaveAsync(_stop.Token); _message = null; LoadFields(); });
        else if (!_manager.Settings.Connected) await RunAsync(async () => { await _service.ConnectModelProvidersAsync(_stop.Token); _message = null; });
        else if (!_manager.Settings.ConnectionWriteConfirmed) await RunAsync(async () => { await _service.DisconnectModelProvidersAsync(_stop.Token); _message = null; });
        else { _editor.Persist(); RestartRequested = true; Close(); }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; _message = Loc.T("providers.working"); Render();
        try { await action(); }
        catch (OperationCanceledException) { _message = Loc.T("providers.cancelled"); }
        catch (Exception error) { _message = Loc.T("providers.failed") + " " + error.Message; }
        finally { _busy = false; if (!_closed) Render(); }
    }
    private void Render()
    {
        var settings = _manager.Settings; var ready = _editor.Draft.Step == 1;
        var provider = settings.Providers.FirstOrDefault(item => item.Id == _editor.SelectedId);
        _providerPicker.Content = (provider?.Name ?? Loc.T("providers.new")) + "  ▾"; _providerPicker.IsEnabled = _more.IsEnabled = !_busy;
        _more.Visibility = _editor.IsSaved || settings.Connected ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(_providerPicker, Loc.T("providers.select") + ": " + (provider?.Name ?? Loc.T("providers.new")));
        _stepTitle.Text = Loc.T(ready ? "providers.step_connect" : "providers.step_details");
        _entry.Visibility = ready ? Visibility.Collapsed : Visibility.Visible; _ready.Visibility = ready ? Visibility.Visible : Visibility.Collapsed; _entry.IsEnabled = _ready.IsEnabled = !_busy;
        _reveal.IsEnabled = _copyKey.IsEnabled = _editor.ApiKey.Length != 0;
        _models.Text = provider == null ? "" : Loc.F("providers.model_count", provider.Models.Count) + "\n" + string.Join("\n", provider.Models.Take(3).Select(item => item.UpstreamId));
        _back.Content = Loc.T(ready ? "providers.edit" : "providers.close"); _back.IsEnabled = !_busy;
        _primary.Content = Loc.T(!ready ? "providers.save_next" : !settings.Connected ? "providers.connect" : !settings.ConnectionWriteConfirmed ? "providers.disconnect" : "restart.title");
        _primary.IsEnabled = !_busy && (ready || !string.IsNullOrWhiteSpace(_editor.Draft.Name) && !string.IsNullOrWhiteSpace(_editor.Draft.BaseUrl) && _editor.ApiKey.Length != 0);
        _status.Text = _message ?? (ready ? Loc.T(settings.Connected ? settings.ConnectionWriteConfirmed ? "providers.prepared" : "providers.incomplete" : "providers.not_connected") : "");
        _status.Visibility = _status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void BuildAdvanced(Panel parent)
    {
        var advanced = new StackPanel(); Field(_reasoningPanel, "providers.reasoning_model", _reasoningModel);
        _reasoningOverride.Content = Loc.T("providers.reasoning_override"); _reasoningPanel.Children.Add(_reasoningOverride);
        var levels = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var effort in ProviderReasoning.Efforts) { var check = new CheckBox { Content = effort, Margin = new Thickness(0, 0, 12, 6) }; _reasoningLevels[effort] = check; levels.Children.Add(check); }
        _reasoningPanel.Children.Add(levels); Field(_reasoningPanel, "providers.reasoning_default", _reasoningDefault);
        var save = Button(Loc.T("providers.reasoning_save")); save.Name = "SaveReasoning"; save.Click += async (_, _) => await RunAsync(SaveReasoningAsync); _reasoningPanel.Children.Add(save);
        advanced.Children.Add(new Expander { Name = "ReasoningSettings", Header = Loc.T("providers.reasoning_title"), Foreground = _theme.Color("text"), Content = _reasoningPanel });
        var handoff = new StackPanel(); handoff.Children.Add(new TextBlock { Text = Loc.T("providers.handoff_steps"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 8) });
        var copy = Button(Loc.T("providers.handoff_copy")); copy.Name = "CopyHandoffPrompt"; copy.Click += (_, _) => Copy(ModelHistoryHandoff.Prompt, "providers.handoff_copied"); handoff.Children.Add(copy);
        advanced.Children.Add(new Expander { Name = "PortableHistoryHelp", Header = Loc.T("providers.handoff_title"), Foreground = _theme.Color("text"), Content = handoff, Margin = new Thickness(0, 12, 0, 0) });
        parent.Children.Add(new Expander { Name = "AdvancedSettings", Header = Loc.T("providers.advanced"), Foreground = _theme.Color("muted"), Content = advanced });
        _reasoningModel.SelectionChanged += (_, _) => { if (!_loading) SelectReasoning(); };
        _reasoningOverride.Checked += (_, _) => UpdateReasoningControls(); _reasoningOverride.Unchecked += (_, _) => UpdateReasoningControls();
    }
    private void SelectReasoning()
    {
        var options = _manager.Settings.Providers.FirstOrDefault(item => item.Id == _editor.SelectedId)?.Models.FirstOrDefault(item => item.UpstreamId == _reasoningModel.SelectedItem as string)?.ReasoningOverride;
        _reasoningOverride.IsChecked = options != null;
        foreach (var pair in _reasoningLevels) pair.Value.IsChecked = options?.Levels.Contains(pair.Key) ?? pair.Key is "low" or "medium" or "high";
        _reasoningDefault.SelectedItem = options?.Default ?? "medium"; UpdateReasoningControls();
    }
    private void UpdateReasoningControls()
    { foreach (var check in _reasoningLevels.Values) check.IsEnabled = _reasoningOverride.IsChecked == true; _reasoningDefault.IsEnabled = _reasoningOverride.IsChecked == true; }
    private async Task SaveReasoningAsync()
    {
        if (_editor.SelectedId == null || _reasoningModel.SelectedItem is not string model) return;
        await _manager.SaveReasoningAsync(_editor.SelectedId, model, _reasoningOverride.IsChecked != true ? null : new ProviderReasoningOptions
        { Levels = _reasoningLevels.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList(), Default = (string?)_reasoningDefault.SelectedItem ?? "medium" }, _stop.Token);
        _message = Loc.T("providers.saved");
    }
}
