using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace CodexAccountDesk;

internal sealed class AccountDetailWindow : Window
{
    private readonly Action<int> _chooseRange;
    private readonly Func<System.Threading.Tasks.Task> _refresh;
    private readonly List<UsageOutlook> _outlooks = new();
    private CreditTrendChart? _creditChart;
    private TextBlock? _creditObserved;
    private CreditTrack? _creditTrack;
    private int _creditHours;
    private long _creditMinute;
    private string? _selectedQuota;
    private bool _moreOpen;
    public AccountDetailWindow(Window owner, Action<int> chooseRange, Func<System.Threading.Tasks.Task> refresh)
    {
        Owner = owner; _chooseRange = chooseRange; _refresh = refresh; Width = 620; Height = 430; MinWidth = 560; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ShowInTaskbar = false;
        WindowFrame.Apply(this);
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        PreviewKeyDown += async (_, args) =>
        { if (args.Key == System.Windows.Input.Key.Escape) { Close(); args.Handled = true; } else if (args.Key == System.Windows.Input.Key.F5) { args.Handled = true; await _refresh(); } };
    }
    public void Refresh(AccountProfile profile, Burn0Theme theme, ResourceDictionary resources, int hours)
    {
        _outlooks.Clear(); _creditChart = null; _creditObserved = null; _creditTrack = null; Resources.MergedDictionaries.Clear(); Resources.MergedDictionaries.Add(resources);
        Title = profile.DisplayName + " · " + Loc.T("사용량 추이"); Background = theme.Color("canvas"); Foreground = theme.Color("text"); FontFamily = theme.Font;
        WindowFrame.RefreshBorder(this);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag);
        FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(40) }); root.RowDefinitions.Add(new());
        var caption = new DockPanel { Height = 40, Background = theme.Color("chrome") };
        var close = new Button { Width = 46, Height = 40, Style = (Style)resources["CaptionButton"], Background = Brushes.Transparent,
            Content = Burn0Theme.Icon("window-close", theme.Color("muted"), 12, true), ToolTip = Loc.T("닫기") };
        close.Click += (_, _) => Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true); DockPanel.SetDock(close, Dock.Right); caption.Children.Add(close);
        caption.Children.Add(new TextBlock { Text = Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), TextTrimming = TextTrimming.CharacterEllipsis }); root.Children.Add(caption);
        var body = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var choices = AccountTankCard.Choices(profile); var choice = AccountTankCard.SelectQuota(profile, _selectedQuota);
        var creditMode = CreditPresentation.Selected(_selectedQuota) || choice == null && profile.CreditHistory.Count > 0;
        var creditTrack = CreditPresentation.Track(profile, _selectedQuota);
        var creditTracks = profile.CreditHistory.Count > 0 ? profile.CreditHistory.ToArray() : new[] { creditTrack };
        var quota = Button((creditMode ? Loc.T("credits.history") : choice?.Window.PeriodLabel ?? Loc.T("사용 한도")) + " ▾"); quota.Background = Brushes.Transparent;
        quota.Name = "HistoryMetricPicker"; quota.ToolTip = creditMode ? creditTrack.BucketId : choice?.BucketLabel;
        quota.IsEnabled = choices.Length + creditTracks.Length > 1;
        quota.Click += (_, _) =>
        {
            var menu = DropdownTheme.Create(quota, theme);
            foreach (var entry in choices)
            {
                var item = new MenuItem { Header = entry.BucketLabel + "  " + entry.Window.PeriodLabel, IsCheckable = true, IsChecked = !creditMode && entry == choice };
                item.Click += (_, _) => { _selectedQuota = entry.BucketId + ":" + entry.Slot; Refresh(profile, theme, resources, hours); }; menu.Items.Add(item);
            }
            if (choices.Length > 0) menu.Items.Add(new Separator());
            foreach (var track in creditTracks)
            {
                var item = new MenuItem { Header = Loc.T("credits.history") + (creditTracks.Length > 1 ? "  " + track.BucketId : ""), IsCheckable = true, IsChecked = creditMode && track.BucketId == creditTrack.BucketId };
                item.Click += (_, _) => { _selectedQuota = CreditPresentation.Key(track); Refresh(profile, theme, resources, hours); }; menu.Items.Add(item);
            }
            quota.ContextMenu = menu; menu.IsOpen = true;
        };
        DockPanel.SetDock(quota, Dock.Left); controls.Children.Add(quota);
        var ranges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var range in new[] { (Hours: 1, Source: "1시간"), (Hours: 24, Source: "24시간"), (Hours: 168, Source: "7일") })
        {
            var button = Button(Loc.T(range.Source)); button.Margin = new Thickness(0, 0, 4, 0); button.Background = theme.Color(hours == range.Hours ? "selected" : "surface");
            button.Foreground = theme.Color(hours == range.Hours ? "accent" : "muted"); button.Click += (_, _) => _chooseRange(range.Hours); ranges.Children.Add(button);
        }
        controls.Children.Add(ranges); DockPanel.SetDock(controls, Dock.Top); body.Children.Add(controls);
        var content = new StackPanel();
        if (creditMode) content.Children.Add(Credits(profile, creditTrack, theme, hours));
        else if (choice != null)
        {
            var outlook = new UsageOutlook(choice.Window, UsageHistory.ForWindow(profile, choice.BucketId, choice.Slot, choice.Window), theme, hours);
            _outlooks.Add(outlook); content.Children.Add(outlook);
        }
        else AddText(Loc.T("조회 정보 없음"), 14, "muted");
        if (profile.Usage?.OrdinaryUsageAllowed == false) AddText(Loc.T("서버가 일반 사용을 허용하지 않고 있습니다."), 12, "warning");
        if (profile.Error != null) AddText(Loc.F("갱신 실패 · 이전 조회값\n{0}", Loc.Error(profile.Error)), 12, "warning");
        var extra = new StackPanel { Visibility = _moreOpen ? Visibility.Visible : Visibility.Collapsed };
        var more = Button(Loc.T("기타 정보") + (_moreOpen ? " ▴" : " ▾")); more.HorizontalAlignment = HorizontalAlignment.Left; more.Background = Brushes.Transparent; more.FontSize = 11; more.Margin = new Thickness(0, 6, 0, 0);
        more.Click += (_, _) => { _moreOpen = !_moreOpen; extra.Visibility = _moreOpen ? Visibility.Visible : Visibility.Collapsed; more.Content = Loc.T("기타 정보") + (_moreOpen ? " ▴" : " ▾"); };
        content.Children.Add(more); content.Children.Add(extra);
        foreach (var bucket in profile.Usage?.Buckets ?? new())
        {
            if (bucket.CreditBalance != null || bucket.UnlimitedCredits == true)
                Extra(bucket.UnlimitedCredits == true ? Loc.T("크레딧 제한 없음") : Loc.F("크레딧 {0}", decimal.TryParse(bucket.CreditBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance) ? balance.ToString("N2", Loc.Culture) : bucket.CreditBalance));
            if (bucket.LimitState != null || bucket.SpendControlReached == true) AddText(Loc.T("사용 한도 또는 지출 한도에 도달했습니다."), 12, "warning");
        }
        if (profile.Usage != null)
        {
            Extra(Loc.F("{0:MM/dd HH:mm:ss} 조회", UsagePresentation.KoreanTime(profile.Usage.CheckedAt)));
            if (profile.Usage.AvailableResets.HasValue) Extra(Loc.Count("초기화권 {0}개", profile.Usage.AvailableResets.Value));
        }
        Extra(profile.SubscriptionActiveUntil is DateTimeOffset until ? Loc.F("구독 종료 {0:yyyy/MM/dd HH:mm} (한국 시간)", UsagePresentation.KoreanTime(until)) : Loc.T("구독 종료일 정보 없음"));
        if (profile.SubscriptionLastChecked is DateTimeOffset checkedAt) Extra(Loc.F("구독 정보 확인 {0:MM/dd HH:mm}", UsagePresentation.KoreanTime(checkedAt)));
        Extra(Loc.T("로그인 정보의 구독 기간 기준"));
        Extra(Loc.T("시각은 한국 시간 기준"));
        body.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(body, 1); root.Children.Add(body); Content = root;
        Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 6, 12, 6) };
        void AddText(string text, double size, string color, double top = 8) => content.Children.Add(new TextBlock { Text = text, FontSize = size, Foreground = theme.Color(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) });
        void Extra(string text) => extra.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = theme.Color("muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
    }
    private UIElement Credits(AccountProfile profile, CreditTrack track, Burn0Theme theme, int hours)
    {
        var panel = new StackPanel(); var metrics = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        _creditTrack = track; _creditHours = hours; _creditMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        metrics.ColumnDefinitions.Add(new()); metrics.ColumnDefinitions.Add(new());
        Metric(0, "credits.balance", CreditPresentation.Balance(profile, track));
        Metric(1, "credits.observed", CreditPresentation.Number(CreditHistory.ObservedUse(track, hours, DateTimeOffset.UtcNow)));
        panel.Children.Add(metrics);
        _creditChart = new(track, theme, hours); panel.Children.Add(_creditChart);
        panel.Children.Add(new TextBlock { Text = Loc.T("credits.history_help"), FontSize = 11, Foreground = theme.Color("muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        return panel;
        void Metric(int column, string label, string value)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            cell.Children.Add(new TextBlock { Text = Loc.T(label), FontSize = 11, Foreground = theme.Color("muted") });
            var number = new TextBlock { Text = value, ToolTip = value, FontSize = 28, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 5, 0, 0) };
            if (column == 1) _creditObserved = number;
            cell.Children.Add(number);
            Grid.SetColumn(cell, column); metrics.Children.Add(cell);
        }
    }
    internal void SelectCredits(AccountProfile profile, Burn0Theme theme, ResourceDictionary resources, int hours)
    { _selectedQuota = CreditPresentation.Key(CreditPresentation.Track(profile, null)); Refresh(profile, theme, resources, hours); }
    public void Update(DateTimeOffset now)
    {
        foreach (var outlook in _outlooks) outlook.Update(now); _creditChart?.Update(now);
        var minute = now.ToUnixTimeSeconds() / 60;
        if (_creditObserved != null && _creditTrack != null && minute != _creditMinute)
        { _creditMinute = minute; _creditObserved.Text = CreditPresentation.Number(CreditHistory.ObservedUse(_creditTrack, _creditHours, now)); _creditObserved.ToolTip = _creditObserved.Text; }
    }
}
