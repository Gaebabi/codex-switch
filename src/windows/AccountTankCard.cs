using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed record QuotaChoice(string BucketId, string Slot, UsageWindow Window, string BucketLabel);

internal sealed class AccountTankCard : Grid
{
    internal Button MenuButton { get; }
    private readonly AccountProfile _profile;
    private readonly QuotaChoice? _choice;
    private readonly Burn0Theme _theme;
    private readonly UsageTrendModel _trend;
    private readonly TextBlock _depletion = new(), _reset = new();
    private readonly TextBlock _plan, _resetCredits, _subscriptionDate, _subscriptionDays, _credits;
    private readonly StackPanel _subscriptionLine;
    private readonly TextBlock _depletionLabel, _resetLabel;
    private readonly AccountCardHeader _header;
    private readonly Grid _layout, _metricHost;
    private readonly StackPanel _numberRow;
    private readonly TextBlock _number, _period, _state;
    private readonly TextBlock? _percent;
    private readonly DockPanel _last;
    private readonly WrapPanel? _windows;
    private bool? _compact;
    public static QuotaChoice[] Choices(AccountProfile profile) => profile.Usage?.Buckets.SelectMany(bucket =>
        new[] { bucket.Primary == null ? null : new QuotaChoice(bucket.Id, "primary", bucket.Primary, bucket.Label),
            bucket.Secondary == null ? null : new QuotaChoice(bucket.Id, "secondary", bucket.Secondary, bucket.Label) }).OfType<QuotaChoice>().ToArray() ?? Array.Empty<QuotaChoice>();
    public static QuotaChoice? SelectQuota(AccountProfile profile, string? selected = null)
    {
        var choices = Choices(profile);
        return choices.FirstOrDefault(c => c.BucketId + ":" + c.Slot == selected) ?? choices.OrderBy(c => c.Window.Remaining).FirstOrDefault();
    }
    public AccountTankCard(AccountProfile profile, QuotaChoice? choice, Burn0Theme theme, AccountCardHeader header, Button menuButton,
        Action<string> selectWindow)
    {
        _profile = profile; _choice = choice; _theme = theme; MenuButton = menuButton;
        var history = choice == null ? Array.Empty<UsageObservation>() : UsageHistory.ForWindow(profile, choice.BucketId, choice.Slot, choice.Window);
        _trend = choice == null ? new(ForecastState.Collecting) : UsagePredictor.Learn(choice.Window, history);
        var choices = Choices(profile); _header = header; ClipToBounds = true;
        Children.Add(new TankGaugeSurface(theme, choice?.Window.Remaining, profile.IsCurrent && !profile.RequiresApply));
        _layout = new Grid { Margin = new Thickness(14, 12, 14, 10) };
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); _layout.RowDefinitions.Add(new()); _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.Children.Add(header);
        var metric = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var period = choice == null ? Loc.T("잔여량") : Loc.F("{0} 잔여", choice.Window.PeriodLabel);
        if (choice != null && profile.Usage!.Buckets.Count > 1) period = choice.BucketLabel + " · " + period;
        _period = Text(period, 12, "muted"); metric.Children.Add(_period);
        _numberRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _number = Text(choice == null ? "—" : choice.Window.Remaining.ToString("0.#", Loc.Culture), 64, choice?.Window.Remaining < 15 ? "warning" : "text", FontWeights.SemiBold); _numberRow.Children.Add(_number);
        if (choice != null) { _percent = Text("%", 27, "muted", FontWeights.Medium); _percent.VerticalAlignment = VerticalAlignment.Bottom; _percent.Margin = new Thickness(3, 0, 0, 13); _numberRow.Children.Add(_percent); }
        metric.Children.Add(_numberRow);
        if (choices.Length > 1)
        {
            _windows = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var quota in choices)
            {
                var label = profile.Usage!.Buckets.Count > 1 ? quota.BucketLabel + " " + quota.Window.PeriodLabel : quota.Window.PeriodLabel;
                var button = new Button { Content = Loc.F("{0} {1:0.#}%", label, quota.Window.Remaining), FontSize = 11, MinHeight = 22, Padding = new Thickness(6, 1, 6, 1),
                    Background = Brushes.Transparent, Foreground = theme.Color(quota == choice ? "text" : "muted"), ToolTip = Loc.T("이 한도의 잔여량 보기") };
                button.Click += (_, _) => selectWindow(quota.BucketId + ":" + quota.Slot); _windows.Children.Add(button);
            }
            metric.Children.Add(_windows);
        }
        _metricHost = new Grid { Background = Brushes.Transparent, ToolTip = period + (profile.Error == null ? "" : "\n" + Loc.Error(profile.Error)) }; _metricHost.Children.Add(metric);
        if (choices.Length > 1)
        {
            var menu = DropdownTheme.Create(_metricHost, theme);
            foreach (var quota in choices)
            {
                var item = new MenuItem { Header = quota.BucketLabel + "  " + Loc.F("{0} {1:0.#}%", quota.Window.PeriodLabel, quota.Window.Remaining), IsCheckable = true, IsChecked = quota == choice };
                item.Click += (_, _) => selectWindow(quota.BucketId + ":" + quota.Slot); menu.Items.Add(item);
            }
            _metricHost.ContextMenu = menu; _metricHost.Cursor = System.Windows.Input.Cursors.Hand; _metricHost.ToolTip = period + "\n" + Loc.T("클릭하여 다른 한도 선택");
            _metricHost.MouseLeftButtonDown += (_, args) => { menu.PlacementTarget = _metricHost; menu.IsOpen = true; args.Handled = true; };
        }
        _metricHost.SizeChanged += (_, _) => FitNumber(); Grid.SetRow(_metricHost, 1); _layout.Children.Add(_metricHost);
        var footer = new Grid(); footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _depletionLabel = Text(Loc.T("소진"), 11, "muted"); _resetLabel = Text(Loc.T("리셋"), 11, "muted");
        foreach (var label in new[] { _depletionLabel, _resetLabel }) { label.HorizontalAlignment = HorizontalAlignment.Left; label.Margin = new Thickness(0, 0, 10, 0); label.LineHeight = 14; }
        ConfigureSummary(_depletion); ConfigureSummary(_reset); footer.Children.Add(_depletionLabel); Grid.SetRow(_resetLabel, 1); footer.Children.Add(_resetLabel);
        Grid.SetColumn(_depletion, 1); footer.Children.Add(_depletion); Grid.SetRow(_reset, 1); Grid.SetColumn(_reset, 1); footer.Children.Add(_reset);
        _plan = Text(PlanLabel(profile.Plan), 12, "text", FontWeights.SemiBold);
        _plan.ToolTip = Loc.T("요금제") + " · " + (profile.Plan == "unknown" ? Loc.T("요금제 정보 없음") : _plan.Text);
        _subscriptionDate = Text(profile.SubscriptionActiveUntil is DateTimeOffset end
            ? UsagePresentation.KoreanTime(end).ToString("MM'/'dd", System.Globalization.CultureInfo.InvariantCulture) : "—", 11, "muted", FontWeights.Medium);
        _subscriptionDate.ToolTip = profile.SubscriptionActiveUntil is DateTimeOffset until
            ? Loc.F("구독 종료 {0:yyyy/MM/dd HH:mm} (한국 시간)", UsagePresentation.KoreanTime(until)) : Loc.T("구독 종료일 정보 없음");
        _subscriptionDate.ToolTip += "\n" + Loc.T("로그인 정보의 구독 기간 기준");
        if (profile.SubscriptionLastChecked is DateTimeOffset checkedAt)
            _subscriptionDate.ToolTip += "\n" + Loc.F("구독 정보 확인 {0:MM/dd HH:mm}", UsagePresentation.KoreanTime(checkedAt));
        _plan.LineHeight = _subscriptionDate.LineHeight = 14;
        _plan.HorizontalAlignment = HorizontalAlignment.Right; _plan.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_plan, 2); footer.Children.Add(_plan);
        _subscriptionDays = Text("", 12, "text", FontWeights.SemiBold);
        _subscriptionDays.LineHeight = 14; _subscriptionDays.Margin = new Thickness(6, 0, 0, 0);
        _subscriptionDays.Visibility = profile.SubscriptionActiveUntil.HasValue ? Visibility.Visible : Visibility.Collapsed;
        _subscriptionLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = _subscriptionDate.ToolTip };
        _subscriptionLine.Children.Add(_subscriptionDate); _subscriptionLine.Children.Add(_subscriptionDays);
        var resetCredits = profile.Usage?.AvailableResets;
        _resetCredits = Text(resetCredits is int count ? Loc.F("초기화권 {0}장", count) : Loc.T("초기화권 —"), 12, "text", FontWeights.SemiBold);
        _resetCredits.ToolTip = resetCredits is int available ? (available == 1 ? Loc.T("초기화권 1개") : Loc.F("초기화권 {0}개", available)) : Loc.T("초기화권 정보 없음");
        if (profile.Error != null) _resetCredits.ToolTip += "\n" + Loc.T("갱신 실패 · 이전 값");
        _resetCredits.HorizontalAlignment = HorizontalAlignment.Right; _resetCredits.LineHeight = 14; _resetCredits.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_resetCredits, 2); Grid.SetRow(_resetCredits, 1); footer.Children.Add(_resetCredits);
        _last = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var creditBucket = profile.Usage?.Buckets.FirstOrDefault(b => b.Id == choice?.BucketId) ?? profile.Usage?.Buckets.FirstOrDefault(b => b.Id == "codex") ?? profile.Usage?.Buckets.FirstOrDefault();
        _credits = Text(CreditLabel(creditBucket), 11, "text", FontWeights.SemiBold);
        _credits.LineHeight = 14; _credits.VerticalAlignment = VerticalAlignment.Center; _credits.Margin = new Thickness(8, 0, 0, 0);
        _credits.ToolTip = creditBucket?.UnlimitedCredits == true ? Loc.T("크레딧 제한 없음") : creditBucket?.CreditBalance is string balance ? Loc.F("크레딧 {0}", balance) : Loc.T("크레딧 정보 없음");
        DockPanel.SetDock(_credits, Dock.Right); _last.Children.Add(_credits);
        _state = Text(profile.Error != null ? Loc.T("갱신 실패 · 이전 값") : profile.IsRefreshing ? Loc.T("갱신 중…") : profile.IsCurrent ? Loc.T("현재 로그인") : Loc.T("등록 계정"), 11, profile.Error != null ? "warning" : "muted");
        _state.VerticalAlignment = VerticalAlignment.Center; _state.TextTrimming = TextTrimming.CharacterEllipsis; _state.ToolTip = profile.Error == null ? null : Loc.Error(profile.Error);
        _state.Margin = new Thickness(10, 0, 0, 0); _subscriptionLine.Children.Add(_state); _last.Children.Add(_subscriptionLine);
        Grid.SetRow(_last, 2); Grid.SetColumnSpan(_last, 3); footer.Children.Add(_last); Grid.SetRow(footer, 2); _layout.Children.Add(footer);
        Children.Add(_layout); SizeChanged += (_, _) => Reflow(); Update(DateTimeOffset.UtcNow);
    }
    private void Reflow()
    {
        var compact = ActualWidth < 330 || ActualHeight < 210;
        if (_compact != compact)
        {
            _compact = compact; _header.SetCompact(compact);
            _state.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        _layout.Margin = compact ? new Thickness(8, 4, 8, 4) : new Thickness(14, 12, 14, 10);
        _last.Margin = new Thickness(0, compact ? 0 : 6, 0, 0); _reset.Margin = new Thickness(0);
        _depletion.FontSize = _reset.FontSize = 12;
        _depletion.Visibility = Visibility.Visible;
        _reset.Visibility = Visibility.Visible;
        _depletionLabel.Visibility = _depletion.Visibility; _resetLabel.Visibility = _reset.Visibility;
        FitNumber(); Update(DateTimeOffset.UtcNow);
    }
    private void FitNumber()
    {
        var room = _metricHost.ActualHeight; if (room <= 0) return;
        var showPeriod = room >= 58;
        var showWindows = _compact == false && room >= 128;
        _period.Visibility = showPeriod ? Visibility.Visible : Visibility.Collapsed;
        _period.FontSize = _compact == true ? 10 : 12;
        if (_windows != null) _windows.Visibility = showWindows ? Visibility.Visible : Visibility.Collapsed;
        var available = room - (showPeriod ? 16 : 0) - (showWindows && _windows != null ? 22 : 0);
        var size = Math.Clamp((available - 2) / 1.18, 24, 64);
        _number.FontSize = size; _number.LineHeight = size * 1.16;
        if (_percent != null) { _percent.FontSize = Math.Max(14, size * 0.42); _percent.Margin = new Thickness(3, 0, 0, size * 0.14); }
    }
    internal object ProbeBounds()
    {
        var number = _numberRow.TransformToAncestor(this).TransformBounds(new Rect(_numberRow.RenderSize));
        var actions = _last.TransformToAncestor(this).TransformBounds(new Rect(_last.RenderSize));
        var headerActions = _header.Actions.TransformToAncestor(this).TransformBounds(new Rect(_header.Actions.RenderSize));
        var metadata = new[] { _plan, _resetCredits, _subscriptionDate, _credits }.Concat(_profile.SubscriptionActiveUntil.HasValue ? new[] { _subscriptionDays } : Array.Empty<TextBlock>());
        var metadataFits = metadata.All(text =>
        {
            var bounds = text.TransformToAncestor(this).TransformBounds(new Rect(text.RenderSize));
            return text.IsVisible && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= ActualWidth + 0.5 && bounds.Bottom <= ActualHeight + 0.5;
        });
        return new { numberFont = _number.FontSize, numberFits = number.Top >= 0 && number.Bottom <= ActualHeight + 0.5,
            actionsFit = actions.Left >= 0 && actions.Right <= ActualWidth + 0.5 && actions.Bottom <= ActualHeight + 0.5
                && headerActions.Left >= 0 && headerActions.Right <= ActualWidth + 0.5 && headerActions.Top >= 0 && headerActions.Bottom <= ActualHeight + 0.5,
            headerActionsAtTop = ReferenceEquals(_header.Actions.Parent, _header) && headerActions.Top <= _layout.Margin.Top + 0.5,
            metadataFits, displayedPlan = _plan.Text, displayedCredits = _credits.Text, displayedResetCredits = _resetCredits.Text, availableResets = _profile.Usage?.AvailableResets,
            displayedSubscriptionEnd = _subscriptionDate.Text, subscriptionCountdown = _subscriptionDays.Text,
            subscriptionActiveUntil = _profile.SubscriptionActiveUntil, compact = _compact };
    }
    internal static string CreditLabel(UsageBucket? bucket)
    {
        if (bucket?.UnlimitedCredits == true) return Loc.T("크레딧 ∞");
        if (bucket?.CreditBalance is not string raw) return Loc.T("크레딧 —");
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)) return Loc.F("크레딧 {0}", raw);
        var value = Math.Abs(amount) >= 10000 ? (amount / 1000).ToString("0.#", Loc.Culture) + "K" : amount.ToString("#,0.##", Loc.Culture);
        return Loc.F("크레딧 {0}", value);
    }
    private static string PlanLabel(string plan) => plan.Trim().ToLowerInvariant() switch
    {
        "" or "unknown" => Loc.T("요금제 —"),
        "prolite" or "pro_lite" => "Pro Lite",
        var value => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' '))
    };
    internal static string SubscriptionCountdown(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not DateTimeOffset end) return "";
        var days = (UsagePresentation.KoreanTime(end).Date - UsagePresentation.KoreanTime(now).Date).Days;
        return days == 0 ? Loc.T("오늘") : days > 0 ? "D-" + days : "D+" + -days;
    }
    private TextBlock Text(string text, double size, string color, FontWeight? weight = null) => new()
    { Text = text, FontSize = size, Foreground = _theme.Color(color), FontFamily = _theme.Font, FontWeight = weight ?? FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
    private void ConfigureSummary(TextBlock text) { text.FontSize = 12; text.LineHeight = 14; text.FontWeight = FontWeights.SemiBold; text.Foreground = _theme.Color("text"); text.TextAlignment = TextAlignment.Left; text.VerticalAlignment = VerticalAlignment.Center; text.TextTrimming = TextTrimming.CharacterEllipsis; }
    public void Update(DateTimeOffset now)
    {
        _subscriptionDays.Text = SubscriptionCountdown(_profile.SubscriptionActiveUntil, now);
        if (_choice == null)
        {
            _depletion.Text = Loc.T(_profile.IsRefreshing ? "측정중" : "정보 없음"); _reset.Text = "—"; return;
        }
        var window = _choice.Window; var forecast = UsagePredictor.Evaluate(window, _trend, now);
        _depletion.Text = forecast.State == ForecastState.ResetsFirst ? Loc.T("리셋 먼저")
            : forecast.ExhaustsAt is DateTimeOffset target ? CompactTime(target, now)
            : Loc.T(forecast.State switch { ForecastState.Collecting or ForecastState.InsufficientChange => "측정중", ForecastState.NoGrowth => "증가 없음", ForecastState.Stale => "갱신 필요", ForecastState.Exhausted => "한도 도달", _ => "갱신 대기" });
        _depletion.Foreground = _theme.Color("text");
        if (_profile.Usage!.OrdinaryUsageAllowed == false || _profile.Usage.Buckets.Any(b => b.LimitState != null || b.SpendControlReached == true))
        { _depletion.Text = Loc.T("사용 제한"); }
        _depletion.ToolTip = UsagePresentation.Outlook(forecast) + "\n" + UsagePresentation.Basis(forecast)
            + (forecast.ExhaustsAt is DateTimeOffset time ? "\n" + UsagePresentation.KoreanTime(time).ToString("MM'/'dd HH:mm", Loc.Culture) + " · " + UsagePresentation.TimeLeft(time, now) : "");
        _reset.Text = window.ResetsAt is long reset ? CompactTime(DateTimeOffset.FromUnixTimeSeconds(reset), now) : "—";
        _reset.ToolTip = window.ResetLabel + "\n" + window.TimeUntilReset(now);
    }
    internal static string CompactTime(DateTimeOffset target, DateTimeOffset now)
    {
        var minutes = (long)Math.Ceiling((target - now).TotalMinutes);
        if (minutes <= 0) return Loc.T("갱신 대기");
        return minutes >= 1440 ? Loc.F("{0}일 {1}시간", minutes / 1440, minutes % 1440 / 60)
            : minutes >= 60 ? Loc.F("{0}시간 {1}분", minutes / 60, minutes % 60) : Loc.F("{0}분", minutes);
    }
}
