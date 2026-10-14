using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CodexAccountDesk;

internal sealed record QuotaChoice(string BucketId, string Slot, UsageWindow Window, string BucketLabel);
internal sealed class MacAccountCard : Grid
{
    private readonly AccountProfile _profile;
    private readonly QuotaChoice? _quota;
    private readonly UsageTrendModel _trend;
    private readonly TextBlock _depletion, _reset, _subscription;
    public static QuotaChoice[] Choices(AccountProfile profile) => profile.Usage?.Buckets.SelectMany(bucket => new[] {
        bucket.Primary == null ? null : new QuotaChoice(bucket.Id, "primary", bucket.Primary, bucket.Label),
        bucket.Secondary == null ? null : new QuotaChoice(bucket.Id, "secondary", bucket.Secondary, bucket.Label) }).OfType<QuotaChoice>().ToArray() ?? Array.Empty<QuotaChoice>();
    public static QuotaChoice? Select(AccountProfile profile, string? selection) => Choices(profile).FirstOrDefault(q => q.BucketId + ":" + q.Slot == selection)
        ?? Choices(profile).OrderBy(q => q.Window.Remaining).FirstOrDefault();
    public MacAccountCard(AccountProfile profile, QuotaChoice? quota, DeskTheme theme, Button switchButton, Button more, Action<string> select)
    {
        _profile = profile; _quota = quota; _trend = quota == null ? new(ForecastState.Collecting) : UsagePredictor.Learn(quota.Window, UsageHistory.ForWindow(profile, quota.BucketId, quota.Slot, quota.Window));
        ClipToBounds = true; Margin = new Thickness(4);
        Children.Add(new TankSurface(theme, quota?.Window.Remaining, profile.IsCurrent && !profile.RequiresApply));
        var layout = new Grid { RowDefinitions = new RowDefinitions("28,*,42"), Margin = new Thickness(8, 5) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var title = theme.Text(profile.DisplayName, 13); title.FlowDirection = FlowDirection.LeftToRight; title.VerticalAlignment = VerticalAlignment.Center; ToolTip.SetTip(title, profile.DisplayName);
        header.Children.Add(title); Grid.SetColumn(switchButton, 1); header.Children.Add(switchButton); Grid.SetColumn(more, 2); header.Children.Add(more); layout.Children.Add(header);
        var metric = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        var period = theme.Text(profile.Error != null ? Loc.T("갱신 필요") : profile.IsRefreshing ? Loc.T("사용량 조회 중…") :
            quota == null ? Loc.T("잔여량") : Loc.F("{0} 잔여", quota.Window.PeriodLabel), 11, profile.Error == null ? "muted" : "warning");
        period.HorizontalAlignment = HorizontalAlignment.Center; metric.Children.Add(period);
        var number = theme.Text(quota == null ? "—" : quota.Window.Remaining.ToString("0.#", Loc.Culture) + "%", 54);
        number.FlowDirection = FlowDirection.LeftToRight; number.FontWeight = FontWeight.SemiBold; number.HorizontalAlignment = HorizontalAlignment.Center; metric.Children.Add(number);
        var metricHost = new Grid { Background = Brushes.Transparent }; metricHost.Children.Add(metric); Grid.SetRow(metricHost, 1); layout.Children.Add(metricHost);
        metricHost.SizeChanged += (_, _) => { period.IsVisible = metricHost.Bounds.Height >= 62; number.FontSize = Math.Clamp((metricHost.Bounds.Height - (period.IsVisible ? 16 : 0)) / 1.3, 22, 54); };
        var choices = Choices(profile);
        if (choices.Length > 1)
        {
            var menu = theme.Menu(metricHost);
            menu.ItemsSource = choices.Select(q => { var item = new MenuItem { Header = q.BucketLabel + "  " + Loc.F("{0} {1:0.#}%", q.Window.PeriodLabel, q.Window.Remaining), ToggleType = MenuItemToggleType.Radio, IsChecked = q == quota }; item.Click += (_, _) => select(q.BucketId + ":" + q.Slot); return item; }).ToArray();
            metricHost.ContextMenu = menu; metricHost.PointerPressed += (_, args) => { if (args.GetCurrentPoint(metricHost).Properties.IsLeftButtonPressed) menu.Open(metricHost); };
        }
        var footer = new Grid { RowDefinitions = new RowDefinitions("14,14,14"), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        void Put(Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); footer.Children.Add(control); }
        Put(theme.Text(Loc.T("소진"), 10, "muted"), 0, 0); Put(theme.Text(Loc.T("리셋"), 10, "muted"), 1, 0);
        _depletion = theme.Text("", 11); _depletion.Margin = new Thickness(6, 0); Put(_depletion, 0, 1);
        _reset = theme.Text("", 11); _reset.Margin = new Thickness(6, 0); Put(_reset, 1, 1);
        var plan = theme.Text(profile.Plan is "unknown" or "" ? Loc.T("요금제 —") : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(profile.Plan.Replace('_', ' ')), 11);
        plan.FlowDirection = FlowDirection.LeftToRight; plan.HorizontalAlignment = HorizontalAlignment.Right; Put(plan, 0, 2);
        var resets = theme.Text(profile.Usage?.AvailableResets is int count ? Loc.F("초기화권 {0}장", count) : Loc.T("초기화권 —"), 11);
        resets.FlowDirection = FlowDirection.LeftToRight; resets.HorizontalAlignment = HorizontalAlignment.Right; Put(resets, 1, 2);
        _subscription = theme.Text(Subscription(profile.SubscriptionActiveUntil), 10, "muted"); Grid.SetColumnSpan(_subscription, 2); Put(_subscription, 2, 0);
        var bucket = profile.Usage?.Buckets.FirstOrDefault(b => b.Id == quota?.BucketId) ?? profile.Usage?.Buckets.FirstOrDefault();
        var credit = theme.Text(Credits(bucket), 10); credit.FlowDirection = FlowDirection.LeftToRight; credit.HorizontalAlignment = HorizontalAlignment.Right; Put(credit, 2, 2);
        ToolTip.SetTip(credit, bucket?.CreditBalance ?? (bucket?.UnlimitedCredits == true ? Loc.T("크레딧 제한 없음") : Loc.T("크레딧 정보 없음")));
        ToolTip.SetTip(this, profile.Error == null ? null : Loc.F("갱신 실패 · 이전 조회값\n{0}", Loc.Error(profile.Error))); Grid.SetRow(footer, 2); layout.Children.Add(footer); Children.Add(layout); Update(DateTimeOffset.UtcNow);
    }
    public void Update(DateTimeOffset now)
    {
        _depletion.FlowDirection = FlowDirection.LeftToRight; _reset.FlowDirection = FlowDirection.LeftToRight; _subscription.FlowDirection = FlowDirection.LeftToRight;
        _subscription.Text = Subscription(_profile.SubscriptionActiveUntil);
        if (_quota == null) { _depletion.Text = Loc.T(_profile.IsRefreshing ? "측정중" : "정보 없음"); _reset.Text = "—"; return; }
        var forecast = UsagePredictor.Evaluate(_quota.Window, _trend, now);
        _depletion.Text = forecast.State == ForecastState.ResetsFirst ? Loc.T("리셋 먼저") : forecast.ExhaustsAt is DateTimeOffset end ? Compact(end, now) : Loc.T(forecast.State switch {
            ForecastState.Collecting or ForecastState.InsufficientChange => "측정중", ForecastState.NoGrowth => "증가 없음", ForecastState.Stale => "갱신 필요", ForecastState.Exhausted => "한도 도달", _ => "갱신 대기" });
        if (_profile.Usage?.OrdinaryUsageAllowed == false || _profile.Usage?.Buckets.Any(b => b.LimitState != null || b.SpendControlReached == true) == true) _depletion.Text = Loc.T("사용 제한");
        ToolTip.SetTip(_depletion, UsagePresentation.Outlook(forecast) + "\n" + UsagePresentation.Basis(forecast));
        _reset.Text = _quota.Window.ResetsAt is long time ? Compact(DateTimeOffset.FromUnixTimeSeconds(time), now) : "—";
        ToolTip.SetTip(_reset, _quota.Window.ResetLabel + "\n" + _quota.Window.TimeUntilReset(now));
    }
    internal static string Compact(DateTimeOffset target, DateTimeOffset now)
    {
        var minutes = (long)Math.Ceiling((target - now).TotalMinutes);
        return minutes <= 0 ? Loc.T("갱신 대기") : minutes >= 1440 ? Loc.F("{0}일 {1}시간", minutes / 1440, minutes % 1440 / 60) :
            minutes >= 60 ? Loc.F("{0}시간 {1}분", minutes / 60, minutes % 60) : Loc.F("{0}분", minutes);
    }
    private static string Subscription(DateTimeOffset? until)
    {
        if (until == null) return "—";
        var end = UsagePresentation.KoreanTime(until.Value); var now = UsagePresentation.KoreanTime(DateTimeOffset.UtcNow); var days = (end.Date - now.Date).Days;
        return end.ToString("MM/dd", Loc.Culture) + "  " + (days == 0 ? Loc.T("오늘") : days > 0 ? "D-" + days : "D+" + -days);
    }
    private static string Credits(UsageBucket? bucket)
    {
        if (bucket?.UnlimitedCredits == true) return Loc.T("크레딧 ∞");
        if (bucket?.CreditBalance is not string raw) return Loc.T("크레딧 —");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return Loc.F("크레딧 {0}", raw);
        return Loc.F("크레딧 {0}", Math.Abs(value) >= 10000 ? (value / 1000).ToString("0.#", Loc.Culture) + "K" : value.ToString("#,0.##", Loc.Culture));
    }
}
