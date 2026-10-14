using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Platform.Storage;

namespace CodexAccountDesk;

internal sealed class DeskWindow : Window
{
    private const double AccountRowMinimumHeight = 126;
    private readonly CancellationTokenSource _stop = new();
    private readonly DesktopSessionMonitor _sessions = new(Program.CodexHome);
    private readonly MacResourceMonitor _resources = new();
    private readonly DriveMonitor _drives = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, string> _quotas = new();
    private readonly List<MacAccountCard> _cards = new();
    private readonly ResourcePressure[] _pressure = new ResourcePressure[4];
    private readonly ResourcePressure[] _temperaturePressure = new ResourcePressure[4];
    private readonly Dictionary<string, ResourcePressure> _volumeTemperaturePressure = new();
    private readonly List<Task> _operations = new();
    private AccountService? _service;
    private DeskTheme _theme = new(true);
    private Grid _accounts = new(), _hardware = new();
    private ScrollViewer _accountScroll = new();
    private StackPanel _sessionValues = new(), _volumes = new();
    private TextBlock _status = new();
    private Button _add = new(), _cancel = new(), _refresh = new(), _settingsButton = new();
    private MacMenuBar? _tray;
    private readonly Dictionary<string, NativeMenuItem> _nativeItems = new();
    private CancellationTokenSource? _login, _switch;
    private string? _switchTarget;
    private SwitchPhase _phase;
    private bool _ready;
    private bool _initializing;
    private string _statusMessage = "";
    private AccountProfile[]? _preview;
    private int _ticks;
    public bool Exiting { get; private set; }
    public DeskWindow(AccountProfile[]? preview = null)
    {
        _preview = preview;
        Title = "Codex Switch"; Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://CodexSwitch/Assets/app-icon.png"))); Width = 600; Height = 420; MinWidth = 500; MinHeight = 360;
        ExtendClientAreaToDecorationsHint = true; ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 34; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Build(); UpdateTray();
        Opened += async (_, _) =>
        {
            if (Program.WindowProbePath != null) MacDiagnostics.StartWindowProbe(this, Program.WindowProbePath);
            else if (_preview == null) await RunAsync(InitializeAsync);
            else await MacDiagnostics.RenderAsync(this, Program.PreviewDirectory!);
        };
        Closing += (_, args) => { if (!Exiting) { args.Cancel = true; Hide(); } };
        KeyDown += async (_, args) =>
        {
            if (args.Key == Key.F5 || args.Key == Key.R && args.KeyModifiers == KeyModifiers.Meta)
            { args.Handled = true; await RefreshAsync(); }
            else if (args.Key == Key.W && args.KeyModifiers == KeyModifiers.Meta) { args.Handled = true; Hide(); }
        };
        SizeChanged += (_, _) => { ResizeAccounts(); RenderVolumes(); };
        _sessions.Changed += _ => OnUi(RenderSessions);
        _resources.Changed += () => OnUi(RenderHardware);
        _drives.Changed += () => OnUi(RenderVolumes);
        _clock.Tick += async (_, _) =>
        {
            if (Exiting) return;
            foreach (var card in _cards) card.Update(DateTimeOffset.UtcNow);
            if (++_ticks % 60 == 0 && _ready) await RunAsync(token => _service!.RefreshAllAsync(null, token));
        };
    }
    private void OnUi(Action action) => Dispatcher.UIThread.Post(() => { if (!Exiting) action(); });
    private async Task InitializeAsync(CancellationToken token)
    {
        if (_initializing || _ready) return;
        _initializing = true;
        try
        {
            if (_service == null)
            {
                var vault = await Task.Run(() => { var result = new AccountVault(Program.DataDirectory); result.Save(); return result; }, token);
                _service = new(vault, Program.CodexHome); _service.Changed += () => OnUi(RenderAccounts);
                var languageProblems = Loc.LoadInstalled(Path.Combine(vault.DirectoryPath, "languages"));
                if (!Loc.RestoreLanguage(vault.Data.Language)) _statusMessage = Loc.T("Saved language pack unavailable. Showing English.");
                else if (languageProblems.Count > 0) _statusMessage = Loc.F("pack.invalid_local_packs", languageProblems.Count);
                _theme = new(vault.Data.Theme == "dark"); Build();
            }
            await _service.InitializeAsync(token); _ready = true;
            _sessions.Start(); _resources.Start(); _drives.Configure(_service.Vault.Data.Drives.ToArray()); _drives.Start(); _clock.Start();
            UpdateTray(); RenderAccounts();
            await _service.RefreshAllAsync(null, token);
        }
        finally { _initializing = false; }
    }
    private Task RefreshAsync() => RunAsync(_ready ? token => _service!.RefreshAllAsync(null, token) : InitializeAsync);
    private void Build()
    {
        _theme.Apply(this);
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var shell = new Grid { RowDefinitions = new RowDefinitions("34,22,*,50") };
        var titlebar = new Grid { Background = _theme.Brush("chrome"), ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0) };
        var caption = _theme.Text("Codex Switch", 12); caption.VerticalAlignment = VerticalAlignment.Center;
        var mark = new BrandMark { Width = 18, Height = 18, Ink = _theme.Brush("text") };
        var captionContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        captionContent.Children.Add(mark); captionContent.Children.Add(caption);
        var dragRegion = new Border { Background = Brushes.Transparent, Margin = new Thickness(84, 0, 0, 0), Child = captionContent };
        dragRegion.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(dragRegion).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(args); args.Handled = true;
        };
        titlebar.Children.Add(dragRegion);
        _add = _theme.IconButton(DeskButtonIconKind.Plus, Loc.T("계정 추가"), () => _ = AddAccountAsync());
        _add.Classes.Add("title-action"); Grid.SetColumn(_add, 1); titlebar.Children.Add(_add);
        _refresh = _theme.IconButton(DeskButtonIconKind.Refresh, Loc.T("새로 고침"), () => _ = RefreshAsync());
        _refresh.Classes.Add("title-action"); ToolTip.SetTip(_refresh, Loc.T("새로 고침") + " (⌘R / F5)");
        Grid.SetColumn(_refresh, 2); titlebar.Children.Add(_refresh);
        var language = _theme.Button(Loc.LanguageCode + " ▾", () => { });
        language.Name = "LanguageButton";
        ToolTip.SetTip(language, Loc.LanguageName + " · " + Loc.T("언어 설정"));
        Avalonia.Automation.AutomationProperties.SetName(language, Loc.T("언어 설정") + ": " + Loc.LanguageName);
        language.Click += (_, _) => OpenLanguageMenu(language);
        Grid.SetColumn(language, 3); titlebar.Children.Add(language);
        var theme = _theme.IconButton(_theme.Dark ? DeskButtonIconKind.Sun : DeskButtonIconKind.Moon, Loc.T(_theme.Dark ? "라이트" : "다크"), () =>
        {
            _ = RunAsync(_ =>
            {
                var next = new DeskTheme(!_theme.Dark);
                if (_service != null) { lock (_service.Vault.SyncRoot) { _service.Vault.Data.Theme = next.Dark ? "dark" : "light"; _service.Vault.Save(); } }
                _theme = next; Build(); return Task.CompletedTask;
            });
        }); theme.Name = "ThemeButton"; theme.Classes.Add("title-action"); Grid.SetColumn(theme, 4); titlebar.Children.Add(theme);
        _settingsButton = _theme.IconButton(DeskButtonIconKind.More, Loc.T("설정"), OpenSettings); _settingsButton.Width = 28; _settingsButton.Padding = new Thickness(0);
        Grid.SetColumn(_settingsButton, 5); titlebar.Children.Add(_settingsButton); shell.Children.Add(titlebar);
        var toolbar = new Grid { Margin = new Thickness(8, 1) };
        _cancel = _theme.Button(Loc.T("취소"), () => { _login?.Cancel(); _switch?.Cancel(); }); _cancel.IsVisible = _login != null || _switch != null;
        _cancel.HorizontalAlignment = HorizontalAlignment.Left;
        toolbar.Children.Add(_cancel);
        _sessionValues = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
        toolbar.Children.Add(_sessionValues); Grid.SetRow(toolbar, 1); shell.Children.Add(toolbar);
        _accounts = new Grid { Margin = new Thickness(4, 1) };
        _accountScroll = new ScrollViewer { Content = _accounts, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _accountScroll.SizeChanged += (_, _) => ResizeAccounts(); Grid.SetRow(_accountScroll, 2); shell.Children.Add(_accountScroll);
        var footer = new Grid { RowDefinitions = new RowDefinitions("22,24"), ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 2) };
        _hardware = new Grid { Height = 18, Margin = new Thickness(0, 0, 0, 4), FlowDirection = FlowDirection.LeftToRight }; Grid.SetColumnSpan(_hardware, 2); footer.Children.Add(_hardware);
        _volumes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, FlowDirection = FlowDirection.LeftToRight }; Grid.SetRow(_volumes, 1); footer.Children.Add(_volumes);
        _status = _theme.Text(_statusMessage, 10, "muted"); ToolTip.SetTip(_status, _statusMessage); _status.MaxWidth = 150; _status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_status, 1); Grid.SetRow(_status, 1); footer.Children.Add(_status); Grid.SetRow(footer, 3); shell.Children.Add(footer); Content = shell;
        RenderAccounts(); RenderSessions(); RenderHardware(); RenderVolumes();
    }
    private void ChangeLanguage(string code)
    {
        if (!Loc.Languages.Any(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Unsupported application language.");
        if (_service != null) { lock (_service.Vault.SyncRoot) { _service.Vault.Data.Language = code; _service.Vault.Save(); } }
        Loc.SetLanguage(code);
        Build(); UpdateTray();
    }
    internal ContextMenu OpenLanguageMenu(Button owner)
    {
        var menu = _theme.Menu(owner);
        foreach (var info in Loc.Languages)
        {
            var item = new MenuItem { Header = info.NativeName, ToggleType = MenuItemToggleType.Radio, IsChecked = info.Code == Loc.Language };
            ToolTip.SetTip(item, info.IsCustom ? Loc.F("Local pack · {0}/{1} translated", info.Translated, info.Total) + "\n" + Loc.T("English is shown for missing translations.") : Loc.T("Built-in pack"));
            item.Click += (_, _) => ChangeLanguage(info.Code); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var import = new MenuItem { Header = Loc.T("Import language pack…") }; import.Click += async (_, _) => await ImportLanguagePackAsync(); menu.Items.Add(import);
        if (Loc.Current.IsCustom)
        {
            var restore = new MenuItem { Header = Loc.T("Restore built-in language pack") };
            restore.Click += async (_, _) => { try { ChangeLanguage(Loc.RestoreBuiltIn(Path.Combine(Program.DataDirectory, "languages"))); _statusMessage = Loc.T("Local translations replaced with the built-in pack."); Build(); } catch (Exception error) { await DeskDialogs.Message(this, _theme, Loc.T("Language packs"), error.Message); } };
            menu.Items.Add(restore);
        }
        menu.Open(owner);
        return menu;
    }
    private async Task ImportLanguagePackAsync()
    {
        try
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("Import language pack…"), AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Language pack files (*.json)")) { Patterns = new[] { "*.json" } } } });
            if (selected.Count == 0) return;
            using var file = selected[0];
            var path = file.TryGetLocalPath();
            if (path == null) throw new InvalidDataException("Language packs must be local JSON files.");
            var pack = await Task.Run(() => Loc.Install(path, Path.Combine(Program.DataDirectory, "languages"), PlatformLanguagePackWriter.Instance));
            ChangeLanguage(pack.Code); _statusMessage = Loc.T("Language pack installed. Language changed without restarting."); Build();
        }
        catch (Exception error) { await DeskDialogs.Message(this, _theme, Loc.T("Language packs"), error.Message); }
    }
    private void RenderAccounts()
    {
        _accounts.Children.Clear(); _accounts.ColumnDefinitions.Clear(); _accounts.RowDefinitions.Clear(); _cards.Clear();
        if (_tray != null) UpdateTray();
        _add.IsEnabled = _ready && _login == null; _cancel.IsVisible = _login != null || _switch != null;
        _refresh.IsEnabled = !_initializing && _preview == null;
        var profiles = (_preview ?? _service?.Accounts ?? Array.Empty<AccountProfile>()).OrderByDescending(p => p.IsCurrent).ThenBy(p => p.DisplayName, StringComparer.CurrentCulture).ToArray();
        if (profiles.Length == 0)
        {
            var message = _theme.Text(Loc.T("현재 계정을 가져오거나 다른 계정으로 로그인하세요."), 13, "muted");
            message.HorizontalAlignment = HorizontalAlignment.Center; message.VerticalAlignment = VerticalAlignment.Center;
            message.TextWrapping = TextWrapping.Wrap; message.Margin = new Thickness(24); _accounts.Children.Add(message); ResizeAccounts(); return;
        }
        var columns = profiles.Length == 1 ? 1 : 2;
        for (var i = 0; i < columns; i++) _accounts.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < (profiles.Length + columns - 1) / columns; i++) _accounts.RowDefinitions.Add(new RowDefinition(GridLength.Star) { MinHeight = AccountRowMinimumHeight });
        for (var i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i]; var pending = _service?.Vault.Data.PendingSwitch;
            var resume = pending?.TargetAccountKey == profile.Key && profile.IsCurrent;
            var activeSwitch = _switchTarget == profile.Key && _switch != null;
            var text = activeSwitch ? _phase == SwitchPhase.BackingUp ? "백업 중" : _phase == SwitchPhase.Resuming ? "재개 중" : "전환 중" :
                resume ? "채팅 재개" : profile.RequiresApply ? "로그인 적용" : profile.IsCurrent ? "사용 중" : "전환";
            var button = _theme.Button(Loc.T(text), () => _ = SwitchAsync(profile));
            button.IsEnabled = _ready && _switch == null && (!profile.IsCurrent || profile.RequiresApply || resume);
            button.FontSize = 10; button.Padding = new Thickness(5, 2);
            var more = _theme.IconButton(DeskButtonIconKind.More, Loc.T("계정 메뉴"), () => { }); more.Width = 25; more.Padding = new Thickness(3, 2);
            more.Click += (_, _) => AccountMenu(more, profile);
            var card = new MacAccountCard(profile, MacAccountCard.Select(profile, _quotas.GetValueOrDefault(profile.Key)), _theme, button, more,
                choice => { _quotas[profile.Key] = choice; RenderAccounts(); });
            Grid.SetColumn(card, i % columns); Grid.SetRow(card, i / columns); _accounts.Children.Add(card); _cards.Add(card);
        }
        ResizeAccounts();
    }
    private void ResizeAccounts() => _accounts.Height = Math.Max(
        Math.Max(0, _accountScroll.Bounds.Height - _accounts.Margin.Top - _accounts.Margin.Bottom),
        _accounts.RowDefinitions.Count * AccountRowMinimumHeight);
    internal void Preview(string language, bool dark, AccountProfile[] profiles)
    { Loc.SetLanguage(language); _theme = new(dark); _preview = profiles; Build(); UpdateTray(); }
    internal IReadOnlyList<MacAccountCard> Cards => _cards;
    internal ScrollViewer AccountScroll => _accountScroll;
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Exiting) return;
        var completion = new TaskCompletionSource(); _operations.Add(completion.Task);
        _statusMessage = ""; _status.Text = ""; ToolTip.SetTip(_status, null);
        try { await action(_stop.Token); }
        catch (OperationCanceledException) { _statusMessage = Loc.T("취소"); }
        catch (Exception error) { _statusMessage = Loc.Error(UserErrors.Describe(error)); }
        finally
        {
            _status.Text = _statusMessage; ToolTip.SetTip(_status, _statusMessage);
            _operations.Remove(completion.Task); completion.SetResult(); if (!Exiting) RenderAccounts();
        }
    }
    private async Task AddAccountAsync()
    {
        if (!_ready || _login != null) return;
        _login = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); RenderAccounts();
        try
        {
            await RunAsync(async _ =>
            {
                var registration = await _service!.AddAccountAsync(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(), _login.Token);
                await registration.ImmediateRefresh;
            });
        }
        finally { _login.Dispose(); _login = null; if (!Exiting) RenderAccounts(); }
    }
    private async Task SwitchAsync(AccountProfile profile)
    {
        if (!_ready || _switch != null) return;
        _switch = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); _switchTarget = profile.Key; _phase = SwitchPhase.BackingUp; RenderAccounts();
        try
        {
            await RunAsync(async _ =>
            {
                _service!.CheckCredentialBackend(profile);
                await using var backend = new DesktopSwitchBackend(_service, _sessions);
                var coordinator = new AccountSwitchCoordinator(backend,
                    plan => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.PendingSwitch = plan; _service.Vault.Save(); } },
                    plan => OnUi(() => { _phase = plan.Phase; RenderAccounts(); }), UserErrors.Describe);
                var pending = _service.Vault.Data.PendingSwitch;
                if (pending != null && pending.TargetAccountKey != profile.Key) throw new InvalidOperationException(Loc.T("남은 채팅 재개 작업을 먼저 처리해 주세요."));
                var plan = pending ?? await coordinator.PrepareAsync(profile.Key, DateTimeOffset.UtcNow, _switch.Token);
                await coordinator.RunAsync(plan, _switch.Token);
                await _service.RefreshAccountAsync(profile.Key, _stop.Token, true);
            });
        }
        finally { _switch.Dispose(); _switch = null; _switchTarget = null; if (!Exiting) RenderAccounts(); }
    }
    private void AccountMenu(Button target, AccountProfile profile)
    {
        MenuItem Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item; }
        var menu = _theme.Menu(target); menu.ItemsSource = new[] {
            Item(Loc.T("추이 보기"), () => new HistoryWindow(_service!, profile.Key, _theme).Show(this)),
            Item(Loc.T("이 계정 즉시 새로 고침"), () => _ = RunAsync(token => _service!.RefreshAccountAsync(profile.Key, token, true))),
            Item(Loc.T("이름 변경"), async () => { var value = await DeskDialogs.Prompt(this, _theme, Loc.T("이름 변경"), profile.Alias); if (value != null) await RunAsync(_ => { _service!.Rename(profile.Key, value); return Task.CompletedTask; }); }),
            Item(Loc.T("삭제"), async () => { if (_switch != null) return; if (await DeskDialogs.Confirm(this, _theme, Loc.T("삭제"), profile.DisplayName)) await RunAsync(_ => _service!.RemoveAsync(profile.Key)); }) };
        menu.Open(target);
    }
    private void RenderSessions()
    {
        _sessionValues.Children.Clear(); var current = _sessions.Current;
        var numbers = new int?[] { current.Available ? current.Running : null, current.Available ? current.Queued : null, current.Available ? current.Goals : null };
        var labels = new[] { "실행", "큐", "목표" };
        for (var i = 0; i < 3; i++)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var label = _theme.Text(Loc.T(labels[i]), 10, "muted"); label.VerticalAlignment = VerticalAlignment.Center;
            var value = _theme.Text(numbers[i]?.ToString(Loc.Culture) ?? "—", 16); value.VerticalAlignment = VerticalAlignment.Center; value.FontWeight = FontWeight.SemiBold;
            cell.Children.Add(label); cell.Children.Add(value); _sessionValues.Children.Add(cell);
        }
        ToolTip.SetTip(_sessionValues, current.Available ? Loc.F("승인/입력 대기 {0}", current.Waiting) + "\n" +
            (current.Queued.HasValue ? Loc.F("큐는 예약 메시지 수\n보류된 큐 {0}", current.PausedQueued) : Loc.Error(current.QueueProblem ?? "큐 상태 확인 중…")) + "\n" + Loc.F("목표는 진행 중인 Goal 수\n멈춘 목표 {0}", current.PausedGoals) : Loc.Error(current.Problem ?? "채팅 상태 확인 중…"));
    }
    private void RenderHardware()
    {
        var usage = _resources.Current;
        var now = DateTimeOffset.UtcNow;
        var readings = new[] { usage.Cpu, usage.Memory, usage.Gpu, usage.Vram }; var labels = new[] { "CPU", "MEM", "GPU", "VRAM" };
        var scopes = new[] { "PC 전체 CPU", "PC 전체 물리 메모리", "가장 바쁜 GPU 엔진", "가장 높은 전용 GPU 메모리 사용률" };
        for (var i = 0; i < 4; i++)
        {
            var index = i; var reading = readings[i]; _pressure[i] = HardwareMath.Pressure(reading.Percent, _pressure[i]);
            var color = _pressure[i] switch { ResourcePressure.Warning => "warning", ResourcePressure.Critical => "danger", ResourcePressure.Unknown => "muted", _ => "accent" };
            Grid cell;
            if (_hardware.Children.Count <= i)
            {
                _hardware.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); cell = new Grid { ClipToBounds = true, Margin = new Thickness(1) }; cell.Children.Add(new PercentChart());
                var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(5, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var label = _theme.Text(labels[i], 10, "muted"); label.VerticalAlignment = VerticalAlignment.Center; text.Children.Add(label);
                text.Children.Add(_theme.Text("", 12)); content.Children.Add(text);
                var temperature = _theme.Text("", 10, "muted"); temperature.FontWeight = FontWeight.SemiBold; temperature.Margin = new Thickness(6, 0, 0, 0); temperature.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(temperature, 1); content.Children.Add(temperature);
                cell.Children.Add(content); Grid.SetColumn(cell, i); _hardware.Children.Add(cell);
            }
            else cell = (Grid)_hardware.Children[i];
            var chart = (PercentChart)cell.Children[0]; chart.Palette = _theme; chart.ColorName = color;
            chart.Points = (usage.History ?? Array.Empty<HardwareObservation>()).Select(p => new ResourceTrendPoint(p.At, index switch { 0 => p.Cpu, 1 => p.Memory, 2 => p.Gpu, _ => p.Vram })).ToArray(); chart.InvalidateVisual();
            var contentGrid = (Grid)cell.Children[1]; var value = (TextBlock)((StackPanel)contentGrid.Children[0]).Children[1]; value.Text = reading.Percent is double actual ? actual.ToString("0", Loc.Culture) + "%" : "—";
            var temperatureValue = (TextBlock)contentGrid.Children[1]; temperatureValue.Text = TemperaturePresentation.Label(reading.Temperature, now);
            _temperaturePressure[i] = TemperatureColors.Pressure(reading.Temperature, (TemperatureKind)i, _temperaturePressure[i], now);
            temperatureValue.Foreground = _theme.Brush(TemperatureColors.Color(_temperaturePressure[i]));
            value.Foreground = _theme.Brush(_pressure[i] is ResourcePressure.Warning or ResourcePressure.Critical ? color : "text");
            var details = Loc.T(scopes[i]) + (reading.Device == null ? "" : "\n" + reading.Device) + (reading.Problem == null ? "" : "\n" + Loc.Error(reading.Problem)) +
                (reading.UsedBytes.HasValue && reading.TotalBytes.HasValue ? "\n" + Loc.F("{0:0.##} / {1:0.##} GiB", reading.UsedBytes.Value / 1073741824.0, reading.TotalBytes.Value / 1073741824.0) : "") +
                "\n" + Loc.F("2초 간격\n경고 {0}% / 위험 {1}%\n해제 기준 {2}%p 낮음", HardwareMath.WarningPercent, HardwareMath.CriticalPercent, HardwareMath.ReleaseMargin) + "\n\n" + TemperaturePresentation.Tooltip(reading.Temperature, now);
            details += "\n" + TemperatureColors.Tooltip(reading.Temperature, (TemperatureKind)i);
            MacToolTipTheme.Resource(cell, labels[i], reading.Percent is double percent ? percent.ToString("0.0", Loc.Culture) + "%" : "—", details, _theme,
                _pressure[i] switch { ResourcePressure.Warning or ResourcePressure.Critical => color, ResourcePressure.Unknown => "muted", _ => "text" });
        }
    }
    private void RenderVolumes()
    {
        var readings = _drives.Current;
        var visibleCount = Math.Min(readings.Length, Math.Max(1, (int)((Bounds.Width - 170) / 125)));
        var visible = readings.Take(visibleCount).ToArray();
        if (!_volumes.Children.Select(control => (string?)control.Tag).SequenceEqual(visible.Select(drive => drive.Root))) _volumes.Children.Clear();
        for (var index = 0; index < visible.Length; index++)
        {
            var drive = visible[index]; var width = Math.Min(180, Math.Max(119, (Bounds.Width - 170) / visibleCount - 6));
            Grid cell;
            if (_volumes.Children.Count <= index)
            {
                cell = new Grid { Height = 22, ClipToBounds = true, Tag = drive.Root }; cell.Children.Add(new Border { CornerRadius = new CornerRadius(6) });
                cell.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6) });
                var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(5, 2) };
                var label = _theme.Text("", 10); label.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(label);
                var temperature = _theme.Text("", 10, "muted"); temperature.FontWeight = FontWeight.SemiBold; temperature.Margin = new Thickness(6, 0, 0, 0); temperature.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(temperature, 1); content.Children.Add(temperature); cell.Children.Add(content); _volumes.Children.Add(cell);
            }
            else cell = (Grid)_volumes.Children[index];
            cell.Width = width; ((Border)cell.Children[0]).Background = _theme.Brush("surface");
            var fill = (Border)cell.Children[1]; fill.Width = width * (drive.FreePercent ?? 0) / 100;
            var color = drive.FreePercent is double value ? value <= 15 ? "danger" : value <= 30 ? "warning" : "selected" : "muted"; fill.Background = _theme.Brush(color);
            var freeText = drive.Free.HasValue ? (drive.Free.Value / 1073741824.0).ToString("0.#", Loc.Culture) + " GiB" : "—";
            var contentGrid = (Grid)cell.Children[2]; var text = (TextBlock)contentGrid.Children[0]; text.Text = drive.Label + " " + freeText;
            var now = DateTimeOffset.UtcNow; var temperatureValue = (TextBlock)contentGrid.Children[1]; temperatureValue.Text = TemperaturePresentation.Label(drive.Temperature, now);
            _volumeTemperaturePressure[drive.Root] = TemperatureColors.Pressure(drive.Temperature, TemperatureKind.Disk, _volumeTemperaturePressure.GetValueOrDefault(drive.Root), now);
            temperatureValue.Foreground = _theme.Brush(TemperatureColors.Color(_volumeTemperaturePressure[drive.Root]));
            var details = drive.Root + "\n" + (drive.Free.HasValue ? Loc.F("남음 {0:0.##} / {1:0.##} GiB\n잔여 {2:0.0}%", drive.Free.Value / 1073741824.0, drive.Total!.Value / 1073741824.0, drive.FreePercent) : Loc.T("드라이브 조회 불가"));
            details += "\n\n" + TemperaturePresentation.Tooltip(drive.Temperature, now);
            details += "\n" + TemperatureColors.Tooltip(drive.Temperature, TemperatureKind.Disk);
            MacToolTipTheme.Resource(cell, drive.Label, freeText, details, _theme, color is "warning" or "danger" ? color : "text");
        }
    }
    private void OpenSettings()
    {
        if (_service == null) return;
        var menu = _theme.Menu(_settingsButton);
        var drives = new MenuItem { Header = Loc.T("드라이브 설정") }; drives.Click += async (_, _) =>
        {
            var selection = await DeskDialogs.Volumes(this, _theme, _service.Vault.Data.Drives.ToArray());
            if (selection != null) await RunAsync(_ => { lock (_service.Vault.SyncRoot) { _service.Vault.Data.Drives = selection.ToList(); _service.Vault.Save(); } _drives.Configure(selection); return Task.CompletedTask; });
        };
        var current = new MenuItem { Header = Loc.T("현재 계정 가져오기") }; current.Click += async (_, _) => await RefreshAsync();
        var quit = new MenuItem { Header = Loc.T("종료") }; quit.Click += async (_, _) => await ExitAsync();
        menu.ItemsSource = new[] { drives, current, quit }; menu.Open(_settingsButton);
    }
    private void UpdateTray()
    {
        if (_tray == null)
        {
            _tray = new MacMenuBar();
            NativeMenuItem Item(string key, string text, Action action, KeyGesture? gesture = null)
            {
                var item = new NativeMenuItem(Loc.T(text)) { Gesture = gesture };
                item.Click += (_, _) => action(); _nativeItems.Add(key, item); return item;
            }
            void OpenWindow() { Show(); Activate(); }
            void Add() { OpenWindow(); _ = AddAccountAsync(); }
            _tray.Add("show", Loc.T("열기"), OpenWindow); _tray.Add("add", Loc.T("계정 추가"), Add);
            _tray.Add("refresh", Loc.T("새로 고침"), () => _ = RefreshAsync());
            _tray.Add("cancel", Loc.T("취소"), () => { _login?.Cancel(); _switch?.Cancel(); });
            _tray.Separator(); _tray.Add("quit", Loc.T("종료"), () => _ = ExitAsync());
            var actions = new NativeMenu();
            actions.Add(Item("open", "열기", OpenWindow, new KeyGesture(Key.O, KeyModifiers.Meta)));
            actions.Add(Item("accounts", "계정 추가", Add, new KeyGesture(Key.N, KeyModifiers.Meta)));
            actions.Add(Item("update", "새로 고침", () => _ = RefreshAsync(), new KeyGesture(Key.R, KeyModifiers.Meta)));
            actions.Add(new NativeMenuItemSeparator()); actions.Add(Item("close", "닫기", Hide, new KeyGesture(Key.W, KeyModifiers.Meta)));
            var appMenu = new NativeMenu(); var root = new NativeMenuItem(Loc.T("Codex 계정")) { Menu = actions };
            _nativeItems.Add("root", root); appMenu.Add(root); NativeMenu.SetMenu(Application.Current!, appMenu);
        }
        foreach (var (key, text) in new[] { ("open", "열기"), ("accounts", "계정 추가"), ("update", "새로 고침"), ("close", "닫기"), ("root", "Codex 계정") }) _nativeItems[key].Header = Loc.T(text);
        _nativeItems["accounts"].IsEnabled = _ready && _login == null;
        _nativeItems["update"].IsEnabled = !_initializing && _preview == null;
        _tray.SetItem("show", Loc.T("열기"), true); _tray.SetItem("quit", Loc.T("종료"), true);
        _tray.SetItem("add", Loc.T("계정 추가"), _ready && _login == null);
        _tray.SetItem("refresh", Loc.T("새로 고침"), !_initializing && _preview == null);
        _tray.SetItem("cancel", Loc.T("취소"), _login != null || _switch != null);
        var profile = (_preview ?? _service?.Accounts)?.FirstOrDefault(p => p.IsCurrent);
        var quota = profile == null ? null : MacAccountCard.Select(profile, _quotas.GetValueOrDefault(profile.Key));
        var value = quota == null ? "—" : quota.Window.Remaining.ToString("0.#", Loc.Culture) + "%";
        var tooltip = "Codex Switch\n" + Loc.T("잔여량") + " " + value + (quota == null ? "" : " · " + quota.Window.PeriodLabel);
        if (profile?.Error != null) tooltip += "\n" + Loc.Error(profile.Error);
        _tray.Update(value, tooltip);
    }
    internal MenuBarState? MenuBarProbe => _tray?.Probe();
    public async Task ExitAsync()
    {
        if (Exiting) return; Exiting = true; _clock.Stop(); _stop.Cancel(); _login?.Cancel(); _switch?.Cancel();
        if (_service != null) await _service.StopAsync();
        await Task.WhenAll(_operations.ToArray());
        await _sessions.DisposeAsync(); await _resources.DisposeAsync(); await _drives.DisposeAsync(); _tray?.Dispose();
        Close(); (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(Program.DiagnosticExitCode);
    }
}
