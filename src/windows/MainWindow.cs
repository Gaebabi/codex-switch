using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexAccountDesk;

internal sealed class MainWindow : Window
{
    private readonly AccountService _service;
    private readonly DesktopSessionMonitor _sessions;
    private readonly HardwareResourceMonitor _hardware = new();
    private readonly HardwareUsagePanel _hardwarePanel = new();
    private readonly DriveMonitor _drives = new(WindowsDiskTemperature.Read);
    private readonly DriveUsagePanel _drivePanel;
    private readonly Program.Options _options;
    private Burn0Theme _theme;
    private readonly System.Windows.Controls.Primitives.UniformGrid _rows = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _status = new(), _loginText = new();
    private readonly SessionUsagePanel _sessionCount = new();
    private readonly Button _refresh = new(), _add = new(), _cancelLogin = new(), _cancelSwitch = new();
    private readonly Button _automaticSwitch = new();
    private MenuItem? _importCurrent;
    private readonly Border _loginPanel = new();
    private readonly List<AccountTankCard> _tankCards = new();
    private readonly Dictionary<string, string> _selectedQuotas = new();
    private readonly Dictionary<string, AccountDetailWindow> _details = new();
    private readonly HashSet<Task> _operations = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loginCancellation;
    private Task? _initialization;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Forms.NotifyIcon? _tray;
    private bool _ready, _loggingIn, _switching, _closing, _exit;
    private bool _started;
    private bool _cpuTemperatureChanging;
    private bool CanRefresh => !_closing && (_ready || _initialization?.IsCompleted != false);
    private string? _queuedKey;
    private SwitchPhase _switchPhase;
    private CancellationTokenSource? _switchCancellation;
    private Button? _maximizeButton;
    private object? _duringRefreshInteraction;
    private string _statusSource = "Codex 계정 확인 중…";
    private object?[] _statusArguments = Array.Empty<object?>();
    private bool _statusWarning;
    internal string? EvidenceReloginAccount { get; private set; }
    private object? _languageInteraction;
    private object? _alternateWindowFrame;
    public MainWindow(AccountService service, Program.Options options)
    {
        _service = service; _options = options;
        _drivePanel = new(OpenDriveSettings);
        _sessions = new DesktopSessionMonitor(options.CodexHome);
        _theme = new Burn0Theme(service.Vault.Data.Theme != "light");
        Title = "Codex Switch"; Icon = AppIdentity.WindowIcon; Width = 600; Height = 400; MinWidth = 500; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (options.ReportPath != null) { ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual; Left = -12000; Top = -12000; }
        WindowStyle = WindowStyle.None;
        WindowFrame.Apply(this);
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        ApplyTheme();
        _scroll.SizeChanged += (_, _) => LayoutCards();
        _scroll.ScrollChanged += (_, args) => { if (args.ViewportWidthChange != 0) LayoutCards(); };
        _refresh.Click += async (_, _) => await RefreshAsync();
        _add.Click += async (_, _) => await AddAsync();
        _cancelLogin.Click += (_, _) => _loginCancellation?.Cancel();
        _cancelSwitch.Click += (_, _) => CancelSwitch();
        _service.Changed += ServiceChanged;
        _sessions.Changed += SessionChanged;
        _hardware.Changed += HardwareChanged;
        _hardwarePanel.CpuTemperatureRequested += async () => await EnableCpuTemperatureAsync();
        _drives.Changed += DrivesChanged;
        StateChanged += (_, _) => { if (_maximizeButton != null) _maximizeButton.Content = Burn0Theme.Icon(WindowState == WindowState.Maximized ? "window-restore" : "window-maximize", Color("muted"), 12, true); };
        _refreshTimer.Tick += async (_, _) => { if (CanRefresh) await RefreshAsync(); };
        _clockTimer.Tick += (_, _) => UpdateCountdowns();
        Loaded += async (_, _) => await StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => { _service.Changed -= ServiceChanged; _sessions.Changed -= SessionChanged; _hardware.Changed -= HardwareChanged; _drives.Changed -= DrivesChanged; _lifetime.Cancel(); _tray?.Dispose(); };
        PreviewKeyDown += async (_, arguments) =>
        {
            if (arguments.Key == System.Windows.Input.Key.F5) { arguments.Handled = true; await RefreshAsync(); }
        };
    }
    private async Task EnableCpuTemperatureAsync()
    {
        if (_options.UiEvidenceDirectory != null || _closing || _cpuTemperatureChanging) return;
        var reading = _hardware.CpuTemperatureStatus;
        if (TemperaturePresentation.Fresh(reading, DateTimeOffset.UtcNow)) return;
        try
        {
            _cpuTemperatureChanging = true; _hardwarePanel.SetCpuTemperatureBusy(true);
            if (reading.Code == "thermal.cpu_setup_required") reading = await _hardware.ProbeCpuTemperatureAsync(_lifetime.Token);
            if (TemperaturePresentation.Fresh(reading, DateTimeOffset.UtcNow)) return;
            if (reading.Code == "thermal.cpu_bundle_missing") { Confirm(Loc.T(reading.Code), "Codex Switch", "확인", false); return; }
            var install = reading.Code == "thermal.cpu_setup_required";
            if (MessageBox.Show(this, Loc.T(install ? "thermal.cpu_setup_confirm" : "thermal.cpu_enable_confirm"), "Codex Switch", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            if (install && await CpuTemperatureSetup.InstallAsync(_lifetime.Token))
            { Confirm(Loc.T("thermal.cpu_restart_required"), "Codex Switch", "확인", false); return; }
            await _hardware.EnableCpuTemperatureAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { }
        catch (Win32Exception error) { Confirm(Loc.F("thermal.cpu_setup_failed", error.NativeErrorCode), "Codex Switch", "확인", false); }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Net.Http.HttpRequestException) { ShowError(error); }
        finally { _cpuTemperatureChanging = false; _hardwarePanel.SetCpuTemperatureBusy(false); }
    }
    private void ApplyTheme()
    {
        foreach (var element in new FrameworkElement[] { _automaticSwitch, _sessionCount, _hardwarePanel, _drivePanel, _status, _loginText, _add, _refresh, _cancelLogin, _cancelSwitch, _loginPanel, _scroll })
        {
            if (element.Parent is Panel panel) panel.Children.Remove(element);
            else if (element.Parent is ContentControl control && ReferenceEquals(control.Content, element)) control.Content = null;
            else if (element.Parent is Decorator decorator && ReferenceEquals(decorator.Child, element)) decorator.Child = null;
        }
        Background = Color("canvas"); Foreground = Color("text"); FontFamily = _theme.Font; FontSize = _theme.Number("ui-size");
        WindowFrame.RefreshBorder(this);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag);
        var direction = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (FlowDirection != direction) FlowDirection = direction;
        SetResources();
        ConfigureTitleAction(_add, "계정 추가", "plus"); ConfigureTitleAction(_refresh, "새로 고침", "refresh");
        var refreshMenu = DropdownTheme.Create(_refresh, _theme);
        _importCurrent = new MenuItem { Header = Loc.T("현재 계정 가져오기") };
        _importCurrent.Click += async (_, _) => await ImportCurrentAsync();
        refreshMenu.Items.Add(_importCurrent); _refresh.ContextMenu = refreshMenu;
        ConfigureButton(_cancelLogin, "로그인 취소", "close"); ConfigureButton(_cancelSwitch, "전환 취소", "close");
        foreach (var text in new[] { _status, _loginText }) { text.Foreground = Color("muted"); text.FontSize = _theme.Number("caption-size"); }
        RenderSessions();
        _hardwarePanel.FlowDirection = FlowDirection.LeftToRight; _drivePanel.FlowDirection = FlowDirection.LeftToRight;
        _hardwarePanel.Refresh(_hardware.Current, _theme);
        _drivePanel.Refresh(_drives.Current, _theme);
        _status.TextWrapping = TextWrapping.NoWrap; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _loginText.Text = Loc.T("브라우저에서 추가할 계정으로 로그인해 주세요.");
        _loginPanel.Background = Color("surface"); _loginPanel.CornerRadius = new CornerRadius(_theme.Number("radius")); _loginPanel.Padding = new Thickness(12);
        Content = BuildRoot(); RenderAccounts(); UpdateControls(); RenderStatus();
    }
    private UIElement BuildRoot()
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(BuildTitleBar());
        var body = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbarRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        toolbarRow.ColumnDefinitions.Add(new ColumnDefinition());
        toolbarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbarRow.ColumnDefinitions.Add(new ColumnDefinition());
        _sessionCount.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_sessionCount, 1);
        toolbarRow.Children.Add(_sessionCount); body.Children.Add(toolbarRow);
        ConfigureButton(_automaticSwitch, "auto.off", null);
        _automaticSwitch.Name = "AutomaticSwitchButton";
        _automaticSwitch.HorizontalAlignment = HorizontalAlignment.Right; _automaticSwitch.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_automaticSwitch, 2);
        _automaticSwitch.FontSize = 10; _automaticSwitch.Padding = new Thickness(6, 2, 6, 2);
        _automaticSwitch.Click -= OpenAutomaticSwitchMenu; _automaticSwitch.Click += OpenAutomaticSwitchMenu;
        toolbarRow.Children.Add(_automaticSwitch);
        var login = new DockPanel(); DockPanel.SetDock(_cancelLogin, Dock.Right); login.Children.Add(_cancelLogin); _loginText.TextWrapping = TextWrapping.Wrap; _loginText.VerticalAlignment = VerticalAlignment.Center; _loginText.Margin = new Thickness(0, 0, 12, 0); login.Children.Add(_loginText);
        _loginPanel.Child = login; _loginPanel.Margin = new Thickness(0, 0, 0, 16); Grid.SetRow(_loginPanel, 1); body.Children.Add(_loginPanel);
        _scroll.Content = _rows; Grid.SetRow(_scroll, 2); body.Children.Add(_scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _hardwarePanel.Margin = new Thickness(0, 0, 0, 4); footer.Children.Add(_hardwarePanel);
        footer.Children.Add(_drivePanel); footer.Children.Add(_status);
        _cancelSwitch.HorizontalAlignment = HorizontalAlignment.Left; _cancelSwitch.Margin = new Thickness(0, 10, 0, 0); footer.Children.Add(_cancelSwitch);
        Grid.SetRow(footer, 3); body.Children.Add(footer); Grid.SetRow(body, 1); root.Children.Add(body); return root;
    }
    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = Color("chrome"), Height = 40 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        identity.Children.Add(AppIdentity.Glyph(Color("text"), 18)); var title = Label("Codex Switch", 12, "text", FontWeights.SemiBold); title.Margin = new Thickness(8, 0, 0, 0); identity.Children.Add(title);
        var version = Label("v" + typeof(MainWindow).Assembly.GetName().Version!.ToString(3), 10, "muted");
        version.Name = "RunningVersionLabel"; version.Margin = new Thickness(6, 0, 0, 0); version.VerticalAlignment = VerticalAlignment.Center; version.FlowDirection = FlowDirection.LeftToRight;
        identity.Children.Add(version); bar.Children.Add(identity);
        var controls = new StackPanel { Name = "TitleBarActions", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(_add); controls.Children.Add(_refresh);
        var language = NewButton(Loc.LanguageCode + " ▾", null, false, true); language.Padding = new Thickness(10, 0, 10, 0); language.Background = Brushes.Transparent; language.ToolTip = Loc.LanguageName + " · " + Loc.T("언어 설정");
        language.Name = "LanguageButton";
        System.Windows.Automation.AutomationProperties.SetName(language, Loc.T("언어 설정") + ": " + Loc.LanguageName);
        if (_options.LanguagePackProblems > 0) language.ToolTip += "\n" + Loc.F("pack.invalid_local_packs", _options.LanguagePackProblems);
        else if (_options.SavedLanguageUnavailable) language.ToolTip += "\n" + Loc.T("Saved language pack unavailable. Showing English.");
        language.Click += (_, _) => OpenLanguageMenu(language); WindowChrome.SetIsHitTestVisibleInChrome(language, true); controls.Children.Add(language);
        var theme = NewButton("", _theme.Dark ? "sun" : "moon", false); theme.Name = "ThemeButton";
        theme.Style = (Style)Resources["TitleActionButton"]; theme.ToolTip = Loc.T(_theme.Dark ? "라이트" : "다크");
        System.Windows.Automation.AutomationProperties.SetName(theme, (string)theme.ToolTip);
        theme.Click += (_, _) => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.Theme = _theme.Dark ? "light" : "dark"; _service.Vault.Save(); } _theme = new Burn0Theme(!_theme.Dark); ApplyTheme(); UpdateTray(); };
        WindowChrome.SetIsHitTestVisibleInChrome(theme, true); controls.Children.Add(theme);
        var minimize = CaptionButton("window-minimize", "최소화", () => SystemCommands.MinimizeWindow(this));
        var maximize = CaptionButton(WindowState == WindowState.Maximized ? "window-restore" : "window-maximize", "최대화 / 복원", () =>
        { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); });
        _maximizeButton = maximize;
        controls.Children.Add(minimize); controls.Children.Add(maximize); controls.Children.Add(CaptionButton("window-close", "트레이로 닫기", Close, true));
        Grid.SetColumn(controls, 2); bar.Children.Add(controls); return bar;
    }
    private Button CaptionButton(string icon, string tooltip, Action action, bool close = false)
    {
        var button = new Button { Width = 46, Height = 40, ToolTip = Loc.T(tooltip), Padding = new Thickness(0), Background = Brushes.Transparent,
            Style = (Style)Resources["CaptionButton"], Content = Burn0Theme.Icon(icon, Color("muted"), 12, true) };
        button.MouseEnter += (_, _) => { button.Background = close ? new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#c42b1c")) : Color("hover"); button.Content = Burn0Theme.Icon(icon, close ? Brushes.White : Color("text"), 12, true); };
        button.MouseLeave += (_, _) => { button.Background = Brushes.Transparent; button.Content = Burn0Theme.Icon(icon is "window-maximize" or "window-restore" ? WindowState == WindowState.Maximized ? "window-restore" : "window-maximize" : icon, Color("muted"), 12, true); };
        button.Click += (_, _) => action(); WindowChrome.SetIsHitTestVisibleInChrome(button, true); return button;
    }
    private void ConfigureTitleAction(Button button, string label, string icon)
    {
        button.Style = (Style)Resources["TitleActionButton"];
        button.Content = Burn0Theme.Icon(icon, Color("text"), 16);
        button.ToolTip = Loc.T(label);
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label));
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
    }
    private void OpenLanguageMenu(Button owner)
    {
        var menu = DropdownTheme.Create(owner, _theme);
        foreach (var option in Loc.Languages)
        {
            var item = new MenuItem { Header = option.NativeName, IsCheckable = true, IsChecked = option.Code == Loc.Language,
                ToolTip = option.IsCustom ? Loc.F("Local pack · {0}/{1} translated", option.Translated, option.Total) + "\n" + Loc.T("English is shown for missing translations.") : Loc.T("Built-in pack") };
            item.Click += (_, _) => ChangeLanguage(option.Code); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var import = new MenuItem { Header = Loc.T("Import language pack…"), ToolTip = Loc.T("Choose a trusted language pack. Imported packs change labels and messages, and contain text only.") };
        import.Click += async (_, _) => await ImportLanguagePackAsync(); menu.Items.Add(import);
        if (Loc.Current.IsCustom)
        {
            var restore = new MenuItem { Header = Loc.T("Restore built-in language pack") };
            restore.Click += (_, _) => { try { ChangeLanguage(Loc.RestoreBuiltIn(Path.Combine(_service.Vault.DirectoryPath, "languages"))); SetStatus("Local translations replaced with the built-in pack."); } catch (Exception error) { Confirm(error.Message, Loc.T("Language packs"), "확인", false); } };
            menu.Items.Add(restore);
        }
        owner.ContextMenu = menu; menu.IsOpen = true;
    }
    private async Task ImportLanguagePackAsync()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Import language pack…"), Filter = Loc.T("Language pack files (*.json)") + "|*.json", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var pack = await Task.Run(() => Loc.Install(picker.FileName, Path.Combine(_service.Vault.DirectoryPath, "languages"), PlatformLanguagePackWriter.Instance));
            ChangeLanguage(pack.Code); SetStatus("Language pack installed. Language changed without restarting.");
        }
        catch (Exception error) { Confirm(error.Message, Loc.T("Language packs"), "확인", false); }
    }
    internal void ChangeLanguage(string language, bool save = true)
    {
        if (!Loc.Languages.Any(p => string.Equals(p.Code, language, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Unsupported application language.");
        if (save) { lock (_service.Vault.SyncRoot) { _service.Vault.Data.Language = language; _service.Vault.Save(); } }
        Loc.SetLanguage(language);
        ApplyTheme(); UpdateTray();
    }
    private bool Confirm(string message, string title, string confirmSource, bool showCancel = true)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 510, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None,
            Background = Color("canvas"), Foreground = Color("text"), FontFamily = FontFamily, Language = Language, FlowDirection = FlowDirection };
        WindowFrame.Apply(dialog, 0);
        var root = new DockPanel(); var heading = new Border { Background = Color("chrome"), Height = 40, Padding = new Thickness(16, 0, 16, 0), Child = new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var content = new StackPanel { Margin = new Thickness(20) }; var body = Label(message, 14, "text"); body.TextWrapping = TextWrapping.Wrap; body.LineHeight = 22; content.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (showCancel) { var cancel = NewButton("취소"); cancel.IsCancel = true; cancel.Click += (_, _) => dialog.DialogResult = false; actions.Children.Add(cancel); }
        var confirm = NewButton(confirmSource, null, true); confirm.IsDefault = true; confirm.Margin = new Thickness(8, 0, 0, 0); confirm.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(confirm);
        content.Children.Add(actions); root.Children.Add(content); dialog.Content = root; return dialog.ShowDialog() == true;
    }
    internal void ShowError(Exception error) => Confirm(Loc.Error(UserErrors.Describe(error)), "Codex Switch", "확인", false);
    internal void ReportBackgroundError(Exception error) => SetStatus(UserErrors.Describe(error), true);
    private void SetResources()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Color("text"))); style.Setters.Add(new Setter(Control.BackgroundProperty, Color("surface")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, _theme.Number("ui-size"))); style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, _theme.Number("control-height"))); style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(_theme.Number("radius"))));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, Color("hover"))); style.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); style.Triggers.Add(disabled);
        var primary = new Style(typeof(Button), style); primary.Setters.Add(new Setter(Control.BackgroundProperty, Color("action"))); primary.Setters.Add(new Setter(Control.ForegroundProperty, Color("on-action")));
        var primaryHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; primaryHover.Setters.Add(new Setter(Control.BackgroundProperty, Color("action"))); primaryHover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88)); primary.Triggers.Add(primaryHover);
        var caption = new Style(typeof(Button), style); caption.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(0)));
        var titleAction = new Style(typeof(Button), style);
        titleAction.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        titleAction.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        titleAction.Setters.Add(new Setter(FrameworkElement.WidthProperty, 32.0)); titleAction.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32.0));
        titleAction.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32.0));
        titleAction.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        DropdownTheme.Install(Resources, _theme);
        ToolTipTheme.Install(Application.Current.Resources, _theme);
        var switchStyle = new Style(typeof(CheckBox)); var switchTemplate = new ControlTemplate(typeof(CheckBox));
        var switchPanel = new FrameworkElementFactory(typeof(StackPanel)); switchPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var switchGrid = new FrameworkElementFactory(typeof(Grid)); switchGrid.SetValue(FrameworkElement.WidthProperty, 44.0); switchGrid.SetValue(FrameworkElement.HeightProperty, 24.0);
        var switchTrack = new FrameworkElementFactory(typeof(Border)); switchTrack.Name = "switchTrack"; switchTrack.SetValue(Border.CornerRadiusProperty, new CornerRadius(12)); switchTrack.SetValue(Border.BackgroundProperty, Color("divider")); switchGrid.AppendChild(switchTrack);
        var switchThumb = new FrameworkElementFactory(typeof(Border)); switchThumb.Name = "switchThumb"; switchThumb.SetValue(FrameworkElement.WidthProperty, 16.0); switchThumb.SetValue(FrameworkElement.HeightProperty, 16.0); switchThumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); switchThumb.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0)); switchThumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); switchThumb.SetValue(Border.BackgroundProperty, Brushes.White); switchGrid.AppendChild(switchThumb); switchPanel.AppendChild(switchGrid);
        var switchContent = new FrameworkElementFactory(typeof(ContentPresenter)); switchContent.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); switchContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); switchPanel.AppendChild(switchContent); switchTemplate.VisualTree = switchPanel;
        var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true }; checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Color("accent"), "switchTrack")); checkedTrigger.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(24, 0, 0, 0), "switchThumb")); switchTemplate.Triggers.Add(checkedTrigger);
        switchStyle.Setters.Add(new Setter(Control.TemplateProperty, switchTemplate)); switchStyle.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        Resources[typeof(Button)] = style;
        Resources["PrimaryButton"] = primary;
        Resources["CaptionButton"] = caption;
        Resources["TitleActionButton"] = titleAction;
        Resources[typeof(CheckBox)] = switchStyle;
    }
    private static ControlTemplate ButtonTemplate(double radius)
    {
        var template = new ControlTemplate(typeof(Button)); var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter); template.VisualTree = border; return template;
    }
    private async Task EnsureInitializedAsync(CancellationToken cancellation)
    {
        if (_ready) return;
        if (_initialization == null || _initialization.IsFaulted || _initialization.IsCanceled) _initialization = _service.InitializeAsync(cancellation);
        UpdateControls(); UpdateTray();
        await _initialization; _ready = true; UpdateControls(); RenderAccounts();
    }
    internal Task StartInTrayAsync() => StartAsync();
    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        if (_options.UiEvidenceDirectory != null) return;
        _sessions.Start();
        _hardware.Start();
        _drives.Configure(_service.Vault.Data.Drives.ToArray()); _drives.Start();
        if (_options.ReportPath == null) { CreateTray(); _refreshTimer.Start(); _clockTimer.Start(); }
        await RunAsync("Codex 계정 확인 중…", async token =>
        {
            await EnsureInitializedAsync(token);
            var refresh = _service.RefreshAllAsync(null, token);
            if (_options.ReportPath != null) _duringRefreshInteraction = CaptureControls();
            await refresh; SetRefreshStatus();
        });
        if (_options.ReportPath == null && _options.UiEvidenceDirectory == null) await RecoverAutomaticSwitchAsync();
        if (_options.SwitchAccount != null && _options.ReportPath == null) await SwitchToAccountAsync(_options.SwitchAccount);
        WriteRunningState("running");
        if (_options.ReportPath != null)
        {
            for (var attempt = 0; attempt < 60 && !_sessions.Current.Available; attempt++) await Task.Delay(250);
            for (var attempt = 0; attempt < 16 && _hardware.Current.Cpu.Percent == null; attempt++) await Task.Delay(250);
            RenderSessions();
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
            if (_options.PreviewPath != null) CapturePreview(_options.PreviewPath);
            if (_options.PreviewPath != null)
            {
                var originalTheme = _theme.Dark;
                _theme = new Burn0Theme(!originalTheme); ApplyTheme(); UpdateLayout();
                _alternateWindowFrame = Diagnostics.ProbeWindowFrame(this);
                CapturePreview(Path.Combine(Path.GetDirectoryName(_options.PreviewPath)!, Path.GetFileNameWithoutExtension(_options.PreviewPath) + (_theme.Dark ? "-dark" : "-light") + ".png"));
                _theme = new Burn0Theme(originalTheme); ApplyTheme();
            }
            if (_options.PreviewPath != null)
            {
                var originalLanguage = Loc.Language;
                var identities = _service.Accounts.Select(a => a.Key + ":" + a.Alias).ToArray();
                ChangeLanguage(originalLanguage == "ko" ? "en" : "ko", false); UpdateLayout();
                CapturePreview(Path.Combine(Path.GetDirectoryName(_options.PreviewPath)!, Path.GetFileNameWithoutExtension(_options.PreviewPath) + "-" + Loc.Language + ".png"));
                _languageInteraction = new { originalLanguage, alternateLanguage = Loc.Language,
                    registrationsPreserved = identities.SequenceEqual(_service.Accounts.Select(a => a.Key + ":" + a.Alias)),
                    manualRefreshAvailable = _ready && !_closing, languageAppliedToStatus = _status.Text == (_statusSource.Length == 0 ? "" : Loc.T(_statusSource)) };
                ChangeLanguage(originalLanguage, false);
            }
            var tankLayout = ProbeTankLayout();
            var smallLayouts = new List<object>();
            if (_options.PreviewPath != null)
            {
                var originalWidth = Width; var originalHeight = Height; var originalLanguage = Loc.Language;
                foreach (var size in new[] { (Width: 600, Height: 400), (Width: 500, Height: 360), (Width: 820, Height: 700) })
                foreach (var language in new[] { originalLanguage, originalLanguage == "ko" ? "en" : "ko" })
                {
                    ChangeLanguage(language, false);
                    Width = size.Width; Height = size.Height; UpdateLayout(); LayoutCards(); UpdateLayout();
                    CapturePreview(Path.Combine(Path.GetDirectoryName(_options.PreviewPath)!, Path.GetFileNameWithoutExtension(_options.PreviewPath) + "-" + size.Width + "x" + size.Height + (language == originalLanguage ? "" : "-" + language) + ".png"));
                    smallLayouts.Add(MeasureGrid());
                }
                ChangeLanguage(originalLanguage, false);
                Width = originalWidth; Height = originalHeight; UpdateLayout(); LayoutCards();
            }
            object appearanceSwitch;
            try { appearanceSwitch = ProbeAppearanceControls(); }
            catch (Exception error) { appearanceSwitch = new { passed = false, error = error.Message, stack = error.StackTrace }; }
            var motion = await ProbeTankMotionAsync();
            var switchStages = _options.PreviewPath == null ? null : ProbeSwitchStages();
            if (_options.PreviewPath != null)
            {
                var driveDialog = new DriveSettingsWindow(this, _theme, _service.Vault.Data.Drives.ToArray());
                driveDialog.Show(); CaptureWindow(driveDialog, Path.ChangeExtension(_options.PreviewPath, null) + "-drive-settings.png"); driveDialog.Close();
            }
            Diagnostics.WriteReadOnlyReport(_options.ReportPath, _service, new { alternateWindowFrame = _alternateWindowFrame, drives = _drives.Current, hardware = _hardware.Current, sessions = _sessions.Current, duringActualRefresh = _duringRefreshInteraction, loginUiState = ProbeInteractionState(), languageSwitch = _languageInteraction, appearanceSwitch, motion, switchStages, windowFrame = Diagnostics.ProbeWindowFrame(this), tankLayout, smallLayouts });
            await _drives.DisposeAsync(); await _hardware.DisposeAsync(); await _sessions.DisposeAsync(); await _service.StopAsync(); _exit = true; Application.Current.Shutdown(); return;
        }
    }
    private async Task<object> ProbeTankMotionAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var gauges = _tankCards.Select(c => (TankGaugeSurface)c.Children[0]).ToArray();
        var initial = gauges.Select(g => g.AnimationFrames).ToArray();
        if (_options.PreviewPath != null) CapturePreview(Path.ChangeExtension(_options.PreviewPath, null) + "-motion-a.png");
        await Task.Delay(700);
        if (_options.PreviewPath != null) CapturePreview(Path.ChangeExtension(_options.PreviewPath, null) + "-motion-b.png");
        var measured = gauges.Select((g, index) => new { g.Active, g.Remaining, g.AnimationRunning, frames = g.AnimationFrames - initial[index] }).ToArray();
        Hide(); var hiddenStopped = gauges.All(g => !g.AnimationRunning);
        Show(); WindowState = WindowState.Minimized; var minimizedStopped = gauges.All(g => !g.AnimationRunning);
        WindowState = WindowState.Normal; await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ContextIdle);
        var restored = gauges.Where(g => g.Active && g.Remaining is > 0 and < 100).All(g => g.AnimationRunning);
        return new { gauges = measured, hiddenStopped, minimizedStopped, restored };
    }
    private object ProbeAppearanceControls()
    {
        var originalLanguage = Loc.Language; var originalTheme = _theme.Dark;
        var identities = _service.Accounts.Select(a => a.Key + ":" + a.Alias).ToArray();
        var actions = 0;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            foreach (var language in new[] { originalLanguage == "ko" ? "en" : "ko", originalLanguage })
            {
                var button = Descendants(this).OfType<Button>().Single(b => b.ToolTip is string tip && tip.EndsWith(Loc.T("언어 설정"), StringComparison.Ordinal));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = button.ContextMenu;
                try
                {
                    menu.UpdateLayout();
                    var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Loc.Languages.Single(l => l.Code == language).NativeName));
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    UpdateLayout(); actions++;
                    if (Loc.Language != language) throw new InvalidOperationException("Language menu did not apply its selection");
                }
                finally { menu.IsOpen = false; }
            }
            for (var toggle = 0; toggle < 2; toggle++)
            {
                var button = Descendants(this).OfType<Button>().Single(b => Equals(b.ToolTip, Loc.T("Burn0 테마 전환")));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); actions++;
            }
        }
        if (Loc.Language != originalLanguage || _theme.Dark != originalTheme || !identities.SequenceEqual(_service.Accounts.Select(a => a.Key + ":" + a.Alias)))
            throw new InvalidOperationException("Appearance changes altered saved account identities or did not restore the original appearance");
        return new { passed = true, actions, language = Loc.Language, theme = _theme.Dark ? "dark" : "light", registrationsPreserved = true };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private async Task RunAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_closing) return;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _operations.Add(completion.Task);
        SetStatus(status);
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { SetStatus("작업을 취소했습니다."); }
        catch (Exception error) { SetStatus(UserErrors.Describe(error), true); }
        finally { _operations.Remove(completion.Task); completion.TrySetResult(true); UpdateControls(); RenderAccounts(); UpdateTray(); }
    }
    private async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        await RunAsync("사용량 갱신 중… 다른 기능을 계속 사용할 수 있습니다.", async token => { await EnsureInitializedAsync(token); await _service.RefreshAllAsync(null, token); SetRefreshStatus(); });
        await RecoverAutomaticSwitchAsync();
        await TryAutomaticSwitchAsync();
    }
    private Task ImportCurrentAsync() => RunAsync("현재 로그인 가져오는 중…", async token =>
    {
        await EnsureInitializedAsync(token);
        var profile = await Task.Run(() => { var p = _service.SynchronizeCurrent(); _service.Vault.Save(); return p; }, token);
        if (profile == null) throw new InvalidOperationException("Codex에서 먼저 로그인하거나 계정 추가를 선택해 주세요.");
        RenderAccounts(); await _service.RefreshAccountAsync(profile.Key, token); SetRefreshStatus();
    });
    private Task ReauthenticateAsync(string key)
    {
        if (_options.UiEvidenceDirectory != null || _options.ReportPath != null)
        { EvidenceReloginAccount = key; return Task.CompletedTask; }
        return AddAsync(key);
    }
    private async Task AddAsync(string? expectedAccountKey = null)
    {
        if (!_ready || _loggingIn || _closing || _switching) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _loginCancellation = cancellation; _loggingIn = true; _loginText.Text = Loc.T("브라우저에서 추가할 계정으로 로그인해 주세요."); UpdateControls();
        await RunAsync("계정 로그인 중…", async _ =>
        {
            try
            {
                var registration = await _service.AddAccountAsync(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(), cancellation.Token, expectedAccountKey);
                _loggingIn = false; UpdateControls(); RenderAccounts();
                SetStatus("로그인 완료 · 해당 계정의 사용량을 바로 조회하고 있습니다.");
                await registration.ImmediateRefresh;
                SetRefreshStatus();
            }
            finally { if (ReferenceEquals(_loginCancellation, cancellation)) { _loginCancellation = null; _loggingIn = false; UpdateControls(); } }
        });
    }
    private void ServiceChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => { if (!_closing) { RenderAccounts(); UpdateControls(); UpdateTray(); } }, DispatcherPriority.Background);
    }
    internal void PrepareUiEvidence(string language, bool dark)
    {
        if (_options.UiEvidenceDirectory == null) throw new InvalidOperationException("An isolated UI evidence window is required");
        Loc.SetLanguage(language); _theme = new Burn0Theme(dark); ApplyTheme();
    }
    private void SessionChanged(SessionActivity activity)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => { if (!_closing) RenderSessions(); }, DispatcherPriority.Background);
    }
    private void HardwareChanged(HardwareUsage usage)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => { if (!_closing) _hardwarePanel.Refresh(usage, _theme); }, DispatcherPriority.Background);
    }
    private void DrivesChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => { if (!_closing) { _drivePanel.Refresh(_drives.Current, _theme); RenderStatus(); } }, DispatcherPriority.Background);
    }
    private void OpenDriveSettings()
    {
        var dialog = new DriveSettingsWindow(this, _theme, _service.Vault.Data.Drives.ToArray());
        if (dialog.ShowDialog() != true) return;
        lock (_service.Vault.SyncRoot) { _service.Vault.Data.Drives = dialog.Selection.ToList(); _service.Vault.Save(); }
        _drives.Configure(dialog.Selection); RenderStatus();
    }
    private void RenderSessions(SessionActivity? illustrative = null)
    {
        var activity = illustrative ?? _sessions.Current;
        _sessionCount.Refresh(activity, _theme);
        _sessionCount.ToolTip = activity.Available ? Loc.T("이 PC의 로컬 Codex 주 채팅\n하위 에이전트 제외\n계정별 구분 없음") + "\n" +
            Loc.F("승인/입력 대기 {0}", activity.Waiting) + "\n" +
            (activity.Queued.HasValue ? Loc.F("큐는 예약 메시지 수\n보류된 큐 {0}", activity.PausedQueued) : Loc.T(activity.QueueProblem ?? "큐 상태 확인 중…")) + "\n" +
            (activity.Goals.HasValue ? Loc.F("목표는 진행 중인 Goal 수\n멈춘 목표 {0}", activity.PausedGoals) : Loc.T("목표 상태 확인 중…")) : Loc.T(activity.Problem ?? "채팅 상태 확인 중…");
    }
    private void RenderAccounts()
    {
        var offset = _scroll.VerticalOffset; _rows.Children.Clear(); _tankCards.Clear();
        var accounts = _service.Accounts;
        RenderSessions();
        if (accounts.Length == 0)
        { var message = Label(Loc.T("현재 계정을 가져오거나 다른 계정으로 로그인하세요."), 14, "muted"); message.Margin = new Thickness(0, 18, 0, 0); _rows.Children.Add(message); return; }
        foreach (var profile in accounts.OrderByDescending(a => a.IsCurrent).ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var pendingResume = PendingSwitch() is { } pending && pending.TargetAccountKey == profile.Key;
            var label = profile.Key == _queuedKey && _switching ? _switchPhase == SwitchPhase.BackingUp ? "백업 중" : _switchPhase == SwitchPhase.Resuming ? "재개 중" : "전환 중" :
                pendingResume ? "switch.recover" : profile.RequiresApply ? "로그인 적용" : profile.IsCurrent ? "사용 중" : "전환";
            var switchButton = NewButton(label, null, !profile.IsCurrent || profile.RequiresApply || pendingResume);
            switchButton.Tag = "account-switch-action";
            if (pendingResume) switchButton.ToolTip = PendingSwitch()?.Error;
            switchButton.Margin = new Thickness(0); switchButton.FontSize = 12; switchButton.Padding = new Thickness(8, 5, 8, 5);
            switchButton.IsEnabled = _ready && !_switching && !_loggingIn && !_closing && (!profile.IsCurrent || profile.RequiresApply || pendingResume);
            switchButton.Click += async (_, _) => await QueueSwitchAsync(profile); actions.Children.Add(switchButton);
            var more = NewButton("", "more"); more.Padding = new Thickness(6); more.Margin = new Thickness(4, 0, 0, 0); more.ToolTip = Loc.T("계정 메뉴"); more.Click += (_, _) => OpenAccountMenu(more, profile); actions.Children.Add(more);
            var details = profile.RequiresApply ? Loc.T("새 로그인 적용 대기") : "";
            var warning = profile.Error == null ? null : profile.Usage == null ? Loc.Error(profile.Error) : Loc.F("갱신 실패 · 이전 조회값\n{0}", Loc.Error(profile.Error));
            var titleRow = new AccountCardHeader(profile.DisplayName, details, _theme, actions, profile.IsCurrent, warning,
                () => _ = ReauthenticateAsync(profile.Key));
            _selectedQuotas.TryGetValue(profile.Key, out var selected);
            var card = new AccountTankCard(profile, AccountTankCard.SelectQuota(profile, selected), _theme, titleRow, more,
                selection => { _selectedQuotas[profile.Key] = selection; RenderAccounts(); });
            _tankCards.Add(card); _rows.Children.Add(card);
        }
        LayoutCards(); RefreshDetails(accounts);
        _scroll.ScrollToVerticalOffset(offset);
    }
    private void LayoutCards()
    {
        var available = _scroll.ActualWidth > 0 ? _scroll.ActualWidth : Width - 24;
        _rows.Columns = _tankCards.Count <= 1 ? 1 : _tankCards.Count <= 4 ? 2 : Math.Clamp((int)(available / 240), 2, 4);
        _rows.Rows = Math.Max(1, (int)Math.Ceiling(_tankCards.Count / (double)_rows.Columns));
        foreach (var card in _tankCards) { card.Width = double.NaN; card.Height = double.NaN; card.Margin = new Thickness(4); }
    }
    private void OpenDetails(string key)
    {
        var profile = _service.Accounts.FirstOrDefault(a => a.Key == key); if (profile == null) return;
        if (!_details.TryGetValue(key, out var window))
        {
            window = new AccountDetailWindow(this, hours => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.TrendHours = hours; _service.Vault.Save(); } RefreshDetails(_service.Accounts); },
                () => RunAsync("선택한 계정 갱신 중…", async token => { await EnsureInitializedAsync(token); await _service.RefreshAccountAsync(key, token); SetRefreshStatus(); }));
            if (_options.ReportPath != null) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000; }
            _details.Add(key, window); window.Closed += (_, _) => _details.Remove(key);
        }
        window.Refresh(profile, _theme, Resources, _service.Vault.Data.TrendHours); window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate();
    }
    private void RefreshDetails(AccountProfile[] accounts)
    {
        foreach (var pair in _details.ToArray())
        { var profile = accounts.FirstOrDefault(a => a.Key == pair.Key); if (profile == null) pair.Value.Close(); else pair.Value.Refresh(profile, _theme, Resources, _service.Vault.Data.TrendHours); }
    }
    private void UpdateCountdowns()
    { var now = DateTimeOffset.UtcNow; foreach (var card in _tankCards) card.Update(now); foreach (var window in _details.Values) window.Update(now); }
    private object ProbeTankLayout()
    {
        UpdateLayout();
        var cards = _tankCards.Select(c => new { width = c.ActualWidth, height = c.ActualHeight, remaining = ((TankGaugeSurface)c.Children[0]).Remaining }).ToArray();
        var mainEnabled = IsEnabled; var detailOpened = false; object? detailFrame = null;
        if (_tankCards.Count > 0)
        {
            _tankCards[0].MenuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var accountMenu = _tankCards[0].MenuButton.ContextMenu!;
            ((MenuItem)accountMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); accountMenu.IsOpen = false;
            detailOpened = _details.Count == 1 && _details.Values.All(w => w.IsVisible); mainEnabled = IsEnabled;
            if (_details.Count > 0) detailFrame = Diagnostics.ProbeWindowFrame(_details.Values.First());
            if (_options.PreviewPath != null && _details.Count > 0)
            {
                var window = _details.Values.First(); window.UpdateLayout();
                CaptureWindow(window, Path.Combine(Path.GetDirectoryName(_options.PreviewPath)!, Path.GetFileNameWithoutExtension(_options.PreviewPath) + "-detail.png"));
            }
            foreach (var window in _details.Values.ToArray()) window.Close();
        }
        return new { cardCount = cards.Length, cards, detailOpened, detailFrame, mainEnabledWithDetails = mainEnabled, visibleRefreshButtons = CountRefreshButtons(this) };
    }
    private object MeasureGrid()
    {
        var cards = _tankCards.Select(card =>
        {
            var bounds = card.TransformToAncestor(_scroll).TransformBounds(new Rect(card.RenderSize));
            return new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height,
                visible = bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= _scroll.ActualWidth + 0.5 && bounds.Bottom <= _scroll.ActualHeight + 0.5,
                content = card.ProbeBounds() };
        }).ToArray();
        var sessionBounds = _sessionCount.TransformToAncestor(this).TransformBounds(new Rect(_sessionCount.RenderSize));
        var hardwareBounds = _hardwarePanel.TransformToAncestor(this).TransformBounds(new Rect(_hardwarePanel.RenderSize));
        var driveBounds = _drivePanel.TransformToAncestor(this).TransformBounds(new Rect(_drivePanel.RenderSize));
        var sessionScenarios = new List<object>();
        foreach (var scenario in new[] { new SessionActivity(false, 0, 0, 0), new SessionActivity(true, 0, 0, 0, Queued: 0, Goals: 0),
            new SessionActivity(true, 99, 0, 99, Queued: 99, Goals: 99) })
        {
            RenderSessions(scenario); UpdateLayout();
            var bounds = _sessionCount.TransformToAncestor(this).TransformBounds(new Rect(_sessionCount.RenderSize));
            var resourceBounds = _hardwarePanel.TransformToAncestor(this).TransformBounds(new Rect(_hardwarePanel.RenderSize));
            sessionScenarios.Add(new { text = _sessionCount.Text, illustrative = true, fits = bounds.Left >= 0 && bounds.Right <= ActualWidth &&
                Math.Abs((bounds.Left + bounds.Right) / 2 - ActualWidth / 2) <= 0.5 && bounds.Bottom <= resourceBounds.Top && _sessionCount.Fits(),
                noMiddleDot = !_sessionCount.Text.Contains('·') });
        }
        RenderSessions(); UpdateLayout();
        return new { language = Loc.Language, width = ActualWidth, height = ActualHeight, columns = _rows.Columns, rows = _rows.Rows, allCardsVisible = cards.All(c => c.visible),
            drivePanelVisible = driveBounds.Left >= 0 && driveBounds.Right <= ActualWidth && driveBounds.Bottom <= ActualHeight,
            hardware = _hardwarePanel.Probe(), hardwareVisible = hardwareBounds.Left >= 0 && hardwareBounds.Right <= ActualWidth && hardwareBounds.Bottom <= driveBounds.Top,
            sessionCount = _sessionCount.Text, sessionCountVisible = sessionBounds.Left >= 0 && sessionBounds.Right <= ActualWidth && sessionBounds.Top >= 0,
            sessionsCentered = Math.Abs((sessionBounds.Left + sessionBounds.Right) / 2 - ActualWidth / 2) <= 0.5,
            sessionScenarios, cards };
    }
    private void OpenAccountMenu(Button owner, AccountProfile profile)
    {
        var menu = DropdownTheme.Create(owner, _theme);
        var history = new MenuItem { Header = Loc.T("추이 보기") }; history.Click += (_, _) => OpenDetails(profile.Key);
        menu.Items.Add(history); menu.Items.Add(new Separator());
        var order = AccountAutomation.OrderedAccounts(_service);
        var priority = new MenuItem { Header = Loc.F("auto.priority", Array.FindIndex(order, p => p.Key == profile.Key) + 1) };
        for (var index = 1; index <= order.Length; index++)
        {
            var position = index;
            var item = new MenuItem { Header = Loc.F("auto.priority", position), IsCheckable = true, IsChecked = order[position - 1].Key == profile.Key };
            item.Click += (_, _) => { AccountAutomation.SetPriority(_service, profile.Key, position); UpdateControls(); RenderAccounts(); };
            priority.Items.Add(item);
        }
        menu.Items.Add(priority);
        if (_switching && profile.Key == _queuedKey && _switchPhase is SwitchPhase.BackingUp or SwitchPhase.Resuming)
        {
            var cancel = new MenuItem { Header = Loc.T("전환 작업 취소") }; cancel.Click += (_, _) => CancelSwitch(); menu.Items.Add(cancel);
        }
        var rename = new MenuItem { Header = Loc.T("표시 이름 변경") }; rename.Click += (_, _) => { var alias = PromptForAlias(profile); if (alias != null) _service.Rename(profile.Key, alias); };
        var remove = new MenuItem { Header = Loc.T("등록 제거"), IsEnabled = !profile.IsCurrent && profile.Key != _queuedKey };
        remove.Click += async (_, _) => { if (Confirm(Loc.F("{0}의 등록 정보를 제거할까요?", profile.DisplayName), Loc.T("계정 등록 제거"), "등록 제거")) await RunAsync("등록 제거 중…", async _ => { await _service.RemoveAsync(profile.Key); SetStatus("등록 정보를 제거했습니다."); }); };
        menu.Items.Add(rename); menu.Items.Add(remove); owner.ContextMenu = menu; menu.IsOpen = true;
    }
    private string? PromptForAlias(AccountProfile profile)
    {
        var dialog = new Window { Owner = this, Title = Loc.T("계정 표시 이름"), Width = 440, Height = 230, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Color("canvas"), Foreground = Color("text"), FontFamily = FontFamily, WindowStyle = WindowStyle.None };
        WindowFrame.Apply(dialog, 0);
        var root = new DockPanel(); var title = new Border { Background = Color("chrome"), Height = 40, Padding = new Thickness(14, 0, 14, 0), Child = new TextBlock { Text = Loc.T("계정 표시 이름"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 } }; DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var stack = new StackPanel { Margin = new Thickness(20, 15, 20, 15) }; var description = Label(Loc.T("이메일 대신 표시할 이름을 입력하세요."), 12, "muted"); description.TextWrapping = TextWrapping.Wrap; stack.Children.Add(description);
        var input = new TextBox { Text = profile.Alias, MaxLength = 60, Margin = new Thickness(0, 10, 0, 12), Padding = new Thickness(8), FontSize = 14, BorderThickness = new Thickness(0), Background = Color("surface"), Foreground = Color("text") }; stack.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var cancel = NewButton("취소"); cancel.Click += (_, _) => dialog.DialogResult = false;
        var ok = NewButton("저장", null, true); ok.Margin = new Thickness(8, 0, 0, 0); ok.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(ok); stack.Children.Add(buttons); root.Children.Add(stack); dialog.Content = root;
        dialog.Loaded += (_, _) => input.Focus(); return dialog.ShowDialog() == true ? input.Text : null;
    }
    private AccountSwitchPlan? PendingSwitch()
    { lock (_service.Vault.SyncRoot) return _service.Vault.Data.PendingSwitch; }
    private object ProbeSwitchStages()
    {
        var width = Width; var height = Height; var language = Loc.Language; var switching = _switching; var queued = _queuedKey; var phase = _switchPhase;
        var cases = new List<object>();
        try
        {
            Width = 500; Height = 360; _switching = true; _queuedKey = _service.Accounts.FirstOrDefault(profile => !profile.IsCurrent)?.Key;
            foreach (var lang in new[] { "ko", "en" })
            foreach (var stage in new[] { SwitchPhase.BackingUp, SwitchPhase.Closing, SwitchPhase.Resuming })
            {
                _switchPhase = stage; ChangeLanguage(lang, false); UpdateControls(); RenderAccounts(); UpdateLayout(); LayoutCards(); UpdateLayout();
                var snapshot = MeasureGrid();
                if (_options.PreviewPath != null) CapturePreview(Path.ChangeExtension(_options.PreviewPath, null) + "-switch-" + stage + "-" + lang + ".png");
                cases.Add(new { stage = stage.ToString(), lang, illustrative = true, layout = snapshot,
                    otherControlsEnabled = _add.IsEnabled && _refresh.IsEnabled });
            }
        }
        finally
        {
            _switching = switching; _queuedKey = queued; _switchPhase = phase; Width = width; Height = height;
            ChangeLanguage(language, false); UpdateControls(); RenderAccounts(); UpdateLayout();
        }
        return cases;
    }
    private void SaveSwitch(AccountSwitchPlan? plan)
    {
        lock (_service.Vault.SyncRoot)
        {
            _service.Vault.Data.PendingSwitch = plan == null ? null : JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(plan));
            _service.Vault.Save();
            if (plan != null)
                SecureFiles.AtomicWriteText(Path.Combine(_service.Vault.DirectoryPath, "last-switch.json"), JsonSerializer.Serialize(new {
                    at = DateTimeOffset.UtcNow, plan.Id, plan.TargetAccountKey, phase = plan.Phase.ToString(), plan.AccountApplied,
                    chats = plan.Chats.Count, namesSaved = plan.Chats.Count(chat => chat.NameSaved),
                    resumed = plan.Chats.Count(chat => chat.ResumeState is ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed),
                    unconfirmed = plan.Chats.Count(chat => chat.ResumeState == ChatResumeState.Unconfirmed),
                    failed = plan.Chats.Count(chat => chat.ResumeState == ChatResumeState.Failed), error = plan.Error }));
        }
    }
    private void SwitchChanged(AccountSwitchPlan plan)
    {
        void ApplyProgress()
        {
            if (_closing) return;
            _switchPhase = plan.Phase;
            if (plan.Error != null) SetStatus(plan.Error, true);
            else if (plan.Phase == SwitchPhase.BackingUp) SetStatus("채팅 이름 백업 {0}/{1}", false, plan.Chats.Count(chat => chat.NameSaved), plan.Chats.Count);
            else if (plan.Phase == SwitchPhase.Resuming) SetStatus("채팅 재개 {0}/{1}", false,
                plan.Chats.Count(chat => chat.ResumeState is ChatResumeState.Resumed or ChatResumeState.AlreadyRunning or ChatResumeState.Completed), plan.Chats.Count);
            else SetStatus(plan.Phase switch { SwitchPhase.Closing => "Codex 정상 종료 중…", SwitchPhase.Applying => "계정 적용 중…",
                SwitchPhase.Launching => "Codex 여는 중…", _ => "계정을 전환하고 채팅을 재개했습니다." });
            UpdateControls(); RenderAccounts(); UpdateTray();
        }
        if (Dispatcher.CheckAccess()) ApplyProgress(); else Dispatcher.Invoke(ApplyProgress);
    }
    private async Task QueueSwitchAsync(AccountProfile profile, bool automatic = false)
    {
        var pending = PendingSwitch();
        if (_closing || _switching || _loggingIn || profile.IsCurrent && !profile.RequiresApply && pending?.TargetAccountKey != profile.Key) return;
        if (pending != null && pending.TargetAccountKey != profile.Key) { SetStatus("남은 채팅 재개 작업을 먼저 처리해 주세요.", true); return; }
        try { _service.CheckCredentialBackend(profile); }
        catch (Exception error) { SetStatus(UserErrors.Describe(error), true); return; }
        _queuedKey = profile.Key; _switching = true; _switchPhase = SwitchPhase.BackingUp;
        _switchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        UpdateControls(); RenderAccounts(); UpdateTray();
        try
        {
            await RunAsync("채팅 이름 백업 중…", async _ =>
            {
                var cancellation = _switchCancellation.Token;
                await using var backend = new DesktopSwitchBackend(_service, _sessions, profile.Key);
                var coordinator = new AccountSwitchCoordinator(backend, SaveSwitch, SwitchChanged, UserErrors.Describe);
                var plan = pending?.TargetAccountKey == profile.Key ? JsonSerializer.Deserialize<AccountSwitchPlan>(JsonSerializer.Serialize(pending))! :
                    await coordinator.PrepareAsync(profile.Key, DateTimeOffset.UtcNow, cancellation);
                plan.Automatic |= automatic;
                if (!automatic) plan.AutomaticRecoveryAttempts = 0;
                SaveSwitch(plan);
                if (await coordinator.RunAsync(plan, cancellation)) SetStatus("계정을 전환하고 채팅을 재개했습니다.");
                else SetStatus("계정 전환 완료. 재개 확인이 필요한 채팅 {0}개", true,
                    plan.Chats.Count(chat => chat.ResumeState is ChatResumeState.Unconfirmed or ChatResumeState.Failed or ChatResumeState.Pending or ChatResumeState.Sending));
            });
        }
        finally
        {
            if (automatic && PendingSwitch() != null) AccountAutomation.Pause(_service);
            _switchCancellation.Dispose(); _switchCancellation = null; _switching = false; _queuedKey = null;
            UpdateControls(); RenderAccounts(); UpdateTray();
        }
    }
    public async Task SwitchToAccountAsync(string identifier)
    {
        await EnsureInitializedAsync(_lifetime.Token);
        var matches = _service.Accounts.Where(profile => string.Equals(profile.Email, identifier, StringComparison.OrdinalIgnoreCase) || profile.Key == identifier).ToArray();
        if (matches.Length != 1) { SetStatus("전환할 계정을 하나로 확인하지 못했습니다.", true); return; }
        BringToFront(); await QueueSwitchAsync(matches[0]);
    }
    private void CancelSwitch()
    {
        _switchCancellation?.Cancel();
    }
    private void OpenAutomaticSwitchMenu(object sender, RoutedEventArgs args)
    {
        var menu = BuildAutomaticSwitchMenu();
        _automaticSwitch.ContextMenu = menu; menu.IsOpen = true;
    }
    internal ContextMenu BuildAutomaticSwitchMenu()
    {
        var menu = DropdownTheme.Create(_automaticSwitch, _theme);
        var enabled = new MenuItem { Header = Loc.T("auto.enable"), IsCheckable = true, IsChecked = _service.Vault.Data.AutomaticSwitchEnabled };
        enabled.Click += (_, _) =>
        {
            lock (_service.Vault.SyncRoot) { _service.Vault.Data.AutomaticSwitchEnabled = enabled.IsChecked; _service.Vault.Save(); }
            UpdateControls();
        };
        menu.Items.Add(enabled); menu.Items.Add(new Separator());
        foreach (var custom in new[] { false, true })
        {
            var item = new MenuItem { Header = Loc.T(custom ? "auto.custom" : "auto.optimal"), IsCheckable = true, IsChecked = _service.Vault.Data.AutomaticSwitchUsePriority == custom };
            if (custom) item.ToolTip = Loc.T("auto.priority_help");
            item.Click += (_, _) => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.AutomaticSwitchUsePriority = custom; _service.Vault.Save(); } UpdateControls(); };
            menu.Items.Add(item);
        }
        var ranking = new MenuItem { Header = Loc.T("auto.order") };
        foreach (var profile in AccountAutomation.OrderedAccounts(_service))
            ranking.Items.Add(new MenuItem { Header = Loc.F("auto.ranked_account", ranking.Items.Count + 1, profile.DisplayName), IsEnabled = false });
        menu.Items.Add(ranking); menu.Items.Add(new Separator());
        foreach (var value in new[] { 5, 10, 15, 20 })
        {
            var item = new MenuItem { Header = Loc.F("auto.threshold", value), IsCheckable = true, IsChecked = _service.Vault.Data.AutomaticSwitchThreshold == value };
            item.Click += (_, _) => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.AutomaticSwitchThreshold = value; _service.Vault.Save(); } UpdateControls(); };
            menu.Items.Add(item);
        }
        return menu;
    }
    private async Task TryAutomaticSwitchAsync()
    {
        if (_options.ReportPath != null || _options.UiEvidenceDirectory != null || !_ready || _closing || _switching || _loggingIn || !_sessions.Current.Available) return;
        await RunAsync("", async _ =>
        {
            if (!RuntimeLocator.IsDesktopRunning(_service.Installation.DesktopPath)) return;
            var target = AccountAutomation.Reserve(_service, _operations.Count > 1, DateTimeOffset.UtcNow);
            if (target == null) return;
            await QueueSwitchAsync(target, true);
            if (PendingSwitch() != null || !_service.Accounts.Any(p => p.Key == target.Key && p.IsCurrent))
            { AccountAutomation.Pause(_service); SetStatus("auto.paused", true); }
        });
    }
    private async Task RecoverAutomaticSwitchAsync()
    {
        if (_options.ReportPath != null || _options.UiEvidenceDirectory != null || !_ready || _closing || _switching || _loggingIn || _service.IsBusy) return;
        var pending = PendingSwitch();
        if (pending is not { Automatic: true, AutomaticRecoveryAttempts: 0 }) return;
        var target = _service.Accounts.FirstOrDefault(p => p.Key == pending.TargetAccountKey);
        if (target == null) { AccountAutomation.Pause(_service); SetStatus("auto.paused", true); return; }
        pending.AutomaticRecoveryAttempts++; SaveSwitch(pending);
        await QueueSwitchAsync(target, true);
    }
    private void SetRefreshStatus()
    {
        SetStatus(_service.Accounts.Any(a => a.Error != null) ? "" : "사용량을 갱신했습니다.");
    }
    private void SetStatus(string source, bool warning = false, params object?[] arguments) { _statusSource = source; _statusArguments = arguments; _statusWarning = warning; RenderStatus(); }
    private void RenderStatus()
    {
        var template = _statusSource.Length == 0 ? "" : _statusWarning ? Loc.Error(_statusSource) : Loc.T(_statusSource);
        _status.Text = _statusArguments.Length == 0 ? template : string.Format(Loc.Culture, template, _statusArguments);
        _status.ToolTip = _status.Text;
        _status.Foreground = Color(_statusWarning ? "warning" : "muted");
        var empty = _service.Vault.Data.Drives.Count == 0;
        _status.Visibility = _status.Text.Length > 0 && (_statusWarning || empty) ? Visibility.Visible : Visibility.Collapsed;
        _drivePanel.Visibility = Visibility.Visible;
        _drivePanel.HorizontalAlignment = empty ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _drivePanel.Width = empty ? 22 : double.NaN;
    }
    private void UpdateControls()
    {
        var automaticSwitchLabel = Loc.T(_service.Vault.Data.AutomaticSwitchEnabled ? "auto.on" : "auto.off");
        _automaticSwitch.Content = new TextBlock { Text = automaticSwitchLabel + " ▾", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(_automaticSwitch, automaticSwitchLabel);
        _automaticSwitch.ToolTip = Loc.F("auto.help", _service.Vault.Data.AutomaticSwitchThreshold) + "\n" +
            Loc.T(_service.Vault.Data.AutomaticSwitchUsePriority ? "auto.custom" : "auto.optimal") + "\n" + Loc.T("auto.priority_help");
        _automaticSwitch.IsEnabled = _ready && !_closing;
        _refresh.IsEnabled = CanRefresh; _add.IsEnabled = _ready && !_loggingIn && !_closing;
        if (_importCurrent != null) _importCurrent.IsEnabled = !_closing;
        _loginPanel.Visibility = _loggingIn ? Visibility.Visible : Visibility.Collapsed;
        _cancelSwitch.Visibility = Visibility.Collapsed;
    }
    internal object ProbeInteractionState()
    {
        var oldLogin = _loggingIn; _loggingIn = true; UpdateControls();
        var result = new { manualRefreshAvailableDuringLogin = _refresh.IsEnabled, importEnabledDuringLogin = _importCurrent?.IsEnabled == true, automaticRefreshIntervalSeconds = _refreshTimer.Interval.TotalSeconds,
            duplicateLoginDisabled = !_add.IsEnabled, captionHeight = WindowChrome.GetWindowChrome(this).CaptionHeight, theme = _theme.Dark ? "dark" : "light" };
        _loggingIn = oldLogin; UpdateControls(); return result;
    }
    private object CaptureControls() => new { activeQueries = _service.RefreshingCount, visibleRefreshButtons = CountRefreshButtons(this),
        importEnabled = _importCurrent?.IsEnabled == true, addAccountEnabled = _add.IsEnabled, automaticRefreshIntervalSeconds = _refreshTimer.Interval.TotalSeconds };
    private static int CountRefreshButtons(DependencyObject root)
    {
        var count = root is Button button && (Equals(button.Content, Loc.T("새로 고침")) || Equals(button.ToolTip, Loc.T("새로 고침")) || Equals(button.ToolTip, Loc.T("이 계정 즉시 새로 고침"))) ? 1 : 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) count += CountRefreshButtons(VisualTreeHelper.GetChild(root, index));
        return count;
    }
    private void CreateTray() { _tray = new Forms.NotifyIcon { Icon = AppIdentity.TrayIcon(), Visible = true, Text = "Codex Switch" }; _tray.DoubleClick += (_, _) => BringToFront(); UpdateTray(); }
    private void UpdateTray()
    {
        if (_tray == null) return; var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("계정 관리 열기"), null, (_, _) => BringToFront()); var refresh = menu.Items.Add(Loc.T("사용량 새로 고침"), null, async (_, _) => await RefreshAsync()); refresh.Enabled = CanRefresh;
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var profile in _service.Accounts)
        {
            var text = profile.DisplayName + "   " + (profile.IsRefreshing ? Loc.T("갱신 중") : profile.Usage?.LeastRemaining is double remaining ? Loc.F("잔여 {0:0.#}%", remaining) : Loc.T("조회 정보 없음"));
            var item = new Forms.ToolStripMenuItem(text) { Checked = profile.IsCurrent, Enabled = _ready && !_closing && !_switching };
            item.Click += async (_, _) => { BringToFront(); await QueueSwitchAsync(profile); }; menu.Items.Add(item);
        }
        var languages = new Forms.ToolStripMenuItem(Loc.T("언어"));
        foreach (var option in Loc.Languages) { var item = new Forms.ToolStripMenuItem(option.NativeName) { Checked = option.Code == Loc.Language }; item.Click += (_, _) => ChangeLanguage(option.Code); languages.DropDownItems.Add(item); }
        menu.Items.Add(languages);
        if (_switching && _switchPhase is SwitchPhase.BackingUp or SwitchPhase.Resuming) menu.Items.Add(Loc.T("전환 작업 취소"), null, (_, _) => CancelSwitch());
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(Loc.T("프로그램 종료"), null, async (_, _) => await ExitAsync());
        var previous = _tray.ContextMenuStrip; _tray.ContextMenuStrip = menu; previous?.Dispose();
        var current = _service.Accounts.FirstOrDefault(a => a.IsCurrent); var tooltip = "Codex · " + (current?.Usage?.LeastRemaining is double value ? Loc.F("잔여 {0:0.#}%", value) + " · " : "") + (current?.DisplayName ?? Loc.T("로그인 정보 없음")); _tray.Text = tooltip[..Math.Min(tooltip.Length, 63)];
    }
    internal void BringToFront() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs arguments) { if (_exit || _options.ReportPath != null || _options.UiEvidenceDirectory != null) return; arguments.Cancel = true; foreach (var window in _details.Values) window.Hide(); Hide(); }
    internal async Task ExitAsync()
    {
        if (_closing) return; _closing = true; _refreshTimer.Stop(); _clockTimer.Stop(); _lifetime.Cancel(); _loginCancellation?.Cancel();
        WriteRunningState("stopping-operations");
        // Keep IPC/account services alive until a cancelled handoff has reopened the desktop.
        await Task.WhenAll(_operations.ToArray());
        WriteRunningState("stopping-drives"); await _drives.DisposeAsync();
        WriteRunningState("stopping-hardware"); await _hardware.DisposeAsync();
        WriteRunningState("stopping-sessions"); await _sessions.DisposeAsync();
        WriteRunningState("stopping-service"); await _service.StopAsync();
        _exit = true; _tray?.Dispose(); WriteRunningState("stopped"); Application.Current.Shutdown();
    }
    private void WriteRunningState(string lifecycle)
    {
        if (!_options.StartHidden) return;
        SecureFiles.AtomicWriteText(Path.Combine(_service.Vault.DirectoryPath, "running-instance.json"), JsonSerializer.Serialize(new {
            processId = Environment.ProcessId, version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3),
            atUtc = DateTimeOffset.UtcNow, ready = _ready, accountCount = _service.Accounts.Length,
            automaticSwitchEnabled = _service.Vault.Data.AutomaticSwitchEnabled,
            customPriority = _service.Vault.Data.AutomaticSwitchUsePriority,
            windowVisible = IsVisible, startupMode = "tray", lifecycle, status = _status.Text }));
    }
    internal void CapturePreview(string path, double scale = 1)
    {
        CaptureWindow(this, path, scale);
    }
    private static void CaptureWindow(Window window, string path, double scale = 1)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Round(window.ActualWidth * scale), (int)Math.Round(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var output = File.Create(path); encoder.Save(output);
    }
    private SolidColorBrush Color(string name) => _theme.Color(name);
    private TextBlock Label(string text, double size, string color, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = Color(color), FontWeight = weight ?? FontWeights.Normal };
    private void ConfigureButton(Button button, string text, string? icon, bool primary = false, bool rawText = false)
    {
        button.Style = primary ? (Style)Resources["PrimaryButton"] : (Style)Resources[typeof(Button)];
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) content.Children.Add(Burn0Theme.Icon(icon, Color(primary ? "on-action" : "text"), 16));
        if (text.Length > 0) { var label = new TextBlock { Text = rawText ? text : Loc.T(text), VerticalAlignment = VerticalAlignment.Center }; if (icon != null) label.Margin = new Thickness(8, 0, 0, 0); content.Children.Add(label); }
        button.Content = content;
    }
    private Button NewButton(string text, string? icon = null, bool primary = false, bool rawText = false) { var button = new Button(); ConfigureButton(button, text, icon, primary, rawText); return button; }
}
