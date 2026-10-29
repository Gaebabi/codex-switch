using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexAccountDesk;

internal sealed class HistoryWindow : Window
{
    private readonly AccountService _service;
    private readonly string _key;
    private readonly DeskTheme _theme;
    private string? _selection;
    private bool _closed;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private UsageWindow? _window;
    private UsageTrendModel? _trend;
    private TextBlock? _depletion, _depletionNote, _reset, _basis;
    private HistoryChart? _chart;
    private CreditChart? _creditChart;
    private TextBlock? _creditObserved;
    private CreditTrack? _creditTrack;
    private long _creditMinute;

    public HistoryWindow(AccountService service, string key, DeskTheme theme)
    {
        _service = service; _key = key; _theme = theme;
        Width = 640; Height = 440; MinWidth = 500; MinHeight = 300;
        theme.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        service.Changed += Changed; Loc.Changed += Changed;
        Opened += (_, _) => _clock.Start();
        _clock.Tick += (_, _) => Update(DateTimeOffset.UtcNow);
        Closed += (_, _) => { _closed = true; _clock.Stop(); service.Changed -= Changed; Loc.Changed -= Changed; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Render();
    }

    private void Changed() => Dispatcher.UIThread.Post(() => { if (!_closed) Render(); });

    private void Render()
    {
        var profile = _service.Accounts.FirstOrDefault(p => p.Key == _key);
        if (profile == null) { Close(); return; }
        Title = profile.DisplayName + " · " + Loc.T("사용량 추이");
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var selected = MacAccountCard.Select(profile, _selection);
        var creditMode = CreditPresentation.Selected(_selection) || selected == null && profile.CreditHistory.Count > 0;
        var creditTrack = CreditPresentation.Track(profile, _selection);
        var creditTracks = profile.CreditHistory.Count > 0 ? profile.CreditHistory.ToArray() : new[] { creditTrack };
        _window = creditMode ? null : selected?.Window; _trend = null; _chart = null; _creditChart = null; _creditObserved = null; _creditTrack = null;
        _depletion = _depletionNote = _reset = _basis = null;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20), MinHeight = 360 };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 24) };
        var choices = MacAccountCard.Choices(profile);
        var quotaLabel = creditMode ? Loc.T("credits.history") : selected == null ? Loc.T("사용 한도") : selected.BucketLabel + " · " + selected.Window.PeriodLabel;
        Control quota;
        if (choices.Length + creditTracks.Length > 1)
        {
            var button = new Button { Name = "HistoryQuota", Content = _theme.Text(quotaLabel + " ▾"),
                HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, quotaLabel);
            button.Click += (_, _) =>
            {
                var menu = _theme.Menu(button);
                var items = choices.Select(choice =>
                {
                    var item = new MenuItem { Header = choice.BucketLabel + " · " + choice.Window.PeriodLabel,
                        ToggleType = MenuItemToggleType.Radio, IsChecked = !creditMode && choice == selected };
                    item.Click += (_, _) => { _selection = choice.BucketId + ":" + choice.Slot; Render(); };
                    return item;
                }).Cast<object>().ToList();
                if (choices.Length > 0) items.Add(new Separator());
                foreach (var track in creditTracks)
                {
                    var item = new MenuItem { Header = Loc.T("credits.history") + (creditTracks.Length > 1 ? "  " + track.BucketId : ""), ToggleType = MenuItemToggleType.Radio, IsChecked = creditMode && track.BucketId == creditTrack.BucketId };
                    item.Click += (_, _) => { _selection = CreditPresentation.Key(track); Render(); }; items.Add(item);
                }
                menu.ItemsSource = items;
                button.ContextMenu = menu; menu.Open(button);
            };
            quota = button;
        }
        else quota = _theme.Text(quotaLabel, 13);
        quota.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(quota);
        var ranges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var hours in new[] { 6, 24, 168 })
        {
            var label = hours == 168 ? Loc.T("7일") : Loc.F("{0}시간", hours);
            var active = _service.Vault.Data.TrendHours == hours;
            var button = new ToggleButton { Name = "HistoryRange" + hours, Content = label, IsChecked = active,
                Theme = this.FindResource(typeof(Button)) as ControlTheme,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), MinHeight = 26,
                Padding = new Thickness(8, 3), FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            button.Click += (_, _) =>
            {
                if (active) { button.IsChecked = true; return; }
                lock (_service.Vault.SyncRoot) { _service.Vault.Data.TrendHours = hours; _service.Vault.Save(); }
                Render();
                this.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(b => b.Name == "HistoryRange" + hours)?.Focus();
            };
            button.Background = active ? _theme.Brush("selected") : Brushes.Transparent;
            button.Foreground = _theme.Brush(active ? "text" : "muted");
            button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetName(button, label);
            ranges.Children.Add(button);
        }
        Grid.SetColumn(ranges, 1); toolbar.Children.Add(ranges); root.Children.Add(toolbar);
        if (creditMode)
        {
            _creditTrack = creditTrack; _creditMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 18) };
            CreditMetric(0, "credits.balance", CreditPresentation.Balance(profile, creditTrack));
            CreditMetric(1, "credits.observed", CreditPresentation.Number(CreditHistory.ObservedUse(creditTrack, _service.Vault.Data.TrendHours, DateTimeOffset.UtcNow)));
            Grid.SetRow(metrics, 1); root.Children.Add(metrics);
            _creditChart = new() { Track = creditTrack, Palette = _theme, Hours = _service.Vault.Data.TrendHours, MinHeight = 170, ClipToBounds = true };
            Grid.SetRow(_creditChart, 2); root.Children.Add(_creditChart);
            var footer = new StackPanel { Spacing = 5, Margin = new Thickness(0, 14, 0, 0) };
            var note = _theme.Text(Loc.T("credits.history_help"), 11, "muted"); note.TextWrapping = TextWrapping.Wrap; footer.Children.Add(note);
            if (profile.Usage != null) footer.Children.Add(_theme.Text(Loc.F("{0:MM/dd HH:mm:ss} 조회", UsagePresentation.KoreanTime(profile.Usage.CheckedAt)), 11, "muted"));
            if (profile.Error != null)
            { var error = _theme.Text(Loc.F("갱신 실패 · 이전 조회값\n{0}", Loc.Error(profile.Error)), 11, "warning"); error.TextWrapping = TextWrapping.Wrap; footer.Children.Add(error); }
            Grid.SetRow(footer, 3); root.Children.Add(footer);
            void CreditMetric(int column, string label, string value)
            {
                var cell = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 12, 0) };
                cell.Children.Add(_theme.Text(Loc.T(label), 11, "muted"));
                var number = _theme.Text(value, 30); number.FontWeight = FontWeight.SemiBold; number.TextTrimming = TextTrimming.CharacterEllipsis; number.FlowDirection = FlowDirection.LeftToRight;
                if (column == 1) _creditObserved = number;
                ToolTip.SetTip(number, value); cell.Children.Add(number); Grid.SetColumn(cell, column); metrics.Children.Add(cell);
            }
        }
        else if (selected == null)
        {
            var empty = _theme.Text(Loc.T("조회 정보 없음"), 13, "muted");
            empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(empty, 2); root.Children.Add(empty);
        }
        else
        {
            var points = UsageHistory.ForWindow(profile, selected.BucketId, selected.Slot, selected.Window);
            _trend = UsagePredictor.Learn(selected.Window, points);
            var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("1.12*,*,*"), Margin = new Thickness(0, 0, 0, 24) };
            AddMetric(0, Loc.T("잔여량"), selected.Window.Remaining.ToString("0.#", Loc.Culture) + "%", 36,
                Loc.T("실제 조회값 기준"));
            (_depletion, _depletionNote) = AddMetric(1, Loc.T("예상 소진"), "—", 22, "");
            (_reset, _) = AddMetric(2, Loc.T("다음 리셋"), "—", 22,
                selected.Window.ResetsAt is long at ? UsagePresentation.KoreanTime(DateTimeOffset.FromUnixTimeSeconds(at)).ToString("MM'/'dd HH:mm", Loc.Culture) : Loc.T("초기화 시간 정보 없음"));
            (TextBlock Value, TextBlock Detail) AddMetric(int column, string label, string value, double size, string detail)
            {
                var cell = new Grid { RowDefinitions = new RowDefinitions("Auto,48,Auto"), Margin = new Thickness(0, 0, 10, 0) };
                cell.Children.Add(_theme.Text(label, 11, "muted"));
                var number = _theme.Text(value, size); number.FontWeight = FontWeight.SemiBold;
                number.FlowDirection = FlowDirection.LeftToRight; number.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(number, value); Grid.SetRow(number, 1); cell.Children.Add(number);
                var note = _theme.Text(detail, 11, "muted"); ToolTip.SetTip(note, detail); Grid.SetRow(note, 2); cell.Children.Add(note);
                Grid.SetColumn(cell, column); metrics.Children.Add(cell);
                return (number, note);
            }
            Grid.SetRow(metrics, 1); root.Children.Add(metrics);
            _chart = new HistoryChart { Points = points, Palette = _theme, Hours = _service.Vault.Data.TrendHours, MinHeight = 170 };
            Grid.SetRow(_chart, 2); root.Children.Add(_chart);
            var footer = new StackPanel { Spacing = 5, Margin = new Thickness(0, 16, 0, 0) };
            _basis = _theme.Text("", 11, "muted"); _basis.TextWrapping = TextWrapping.Wrap; footer.Children.Add(_basis);
            var checkedAt = profile.Usage == null ? "" : Loc.F("{0:MM/dd HH:mm:ss} 조회", UsagePresentation.KoreanTime(profile.Usage.CheckedAt)) + " · ";
            footer.Children.Add(_theme.Text(checkedAt + Loc.T("시각은 한국 시간 기준"), 11, "muted"));
            if (profile.Error != null)
            {
                var error = _theme.Text(Loc.F("갱신 실패 · 이전 조회값\n{0}", Loc.Error(profile.Error)), 11, "warning");
                error.TextWrapping = TextWrapping.Wrap; footer.Children.Add(error);
            }
            Grid.SetRow(footer, 3); root.Children.Add(footer);
        }
        var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        scroll.SizeChanged += (_, _) => root.MinHeight = Math.Max(360, scroll.Bounds.Height - root.Margin.Top - root.Margin.Bottom);
        Content = scroll;
        Update(DateTimeOffset.UtcNow);
    }

    internal void SelectCredits()
    {
        var profile = _service.Accounts.FirstOrDefault(p => p.Key == _key);
        if (profile == null) return;
        _selection = CreditPresentation.Key(CreditPresentation.Track(profile, null)); Render();
    }
    private void Update(DateTimeOffset now)
    {
        if (_creditChart != null)
        {
            _creditChart.Update(now); var minute = now.ToUnixTimeSeconds() / 60;
            if (_creditTrack != null && _creditObserved != null && minute != _creditMinute)
            { _creditMinute = minute; _creditObserved.Text = CreditPresentation.Number(CreditHistory.ObservedUse(_creditTrack, _service.Vault.Data.TrendHours, now)); ToolTip.SetTip(_creditObserved, _creditObserved.Text); }
            return;
        }
        if (_window == null || _trend == null) return;
        var forecast = UsagePredictor.Evaluate(_window, _trend, now);
        _depletion!.Text = forecast.State == ForecastState.ResetsFirst ? Loc.T("리셋 먼저")
            : forecast.ExhaustsAt is DateTimeOffset end ? MacAccountCard.Compact(end, now) : "—";
        _depletionNote!.Text = forecast.State == ForecastState.ResetsFirst ? Loc.T("계속 사용 기준") : UsagePresentation.Outlook(forecast);
        ToolTip.SetTip(_depletion, UsagePresentation.Outlook(forecast) + "\n" + UsagePresentation.Basis(forecast));
        ToolTip.SetTip(_depletionNote, _depletionNote.Text);
        _reset!.Text = _window.ResetsAt is long reset ? MacAccountCard.Compact(DateTimeOffset.FromUnixTimeSeconds(reset), now) : "—";
        ToolTip.SetTip(_reset, _window.ResetLabel + "\n" + _window.TimeUntilReset(now));
        _basis!.Text = UsagePresentation.Basis(forecast);
        _chart?.Update(now);
    }
}
