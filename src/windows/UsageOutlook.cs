using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexAccountDesk;

internal sealed class UsageOutlook : StackPanel
{
    private readonly UsageWindow _window;
    private readonly UsageTrendModel _trend;
    private readonly Burn0Theme _theme;
    private readonly TextBlock _depletion, _reset, _hint;
    private readonly UsageTrendChart _chart;
    public UsageOutlook(UsageWindow window, UsageObservation[] history, Burn0Theme theme, int hours)
    {
        _window = window; _theme = theme; _trend = UsagePredictor.Learn(window, history);
        var overview = new Grid(); overview.ColumnDefinitions.Add(new() { Width = new GridLength(0.95, GridUnitType.Star) });
        overview.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) }); overview.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) });
        var remaining = Metric(Loc.T("잔여량"), out var percent); percent.Text = window.Remaining.ToString("0.#", Loc.Culture) + "%"; percent.FontSize = 52; overview.Children.Add(remaining);
        var depletion = Metric(Loc.T("예상 소진"), out _depletion); Grid.SetColumn(depletion, 1); overview.Children.Add(depletion);
        _hint = Text("", 11, "muted"); _hint.Margin = new Thickness(0, 4, 0, 0); depletion.Children.Add(_hint);
        var reset = Metric(Loc.T("다음 리셋"), out _reset); Grid.SetColumn(reset, 2); overview.Children.Add(reset);
        Children.Add(overview);
        _chart = new UsageTrendChart(window, history, theme, hours, DateTimeOffset.UtcNow, true) { Margin = new Thickness(0, 16, 0, 0) }; Children.Add(_chart);
        Update(DateTimeOffset.UtcNow);
    }
    private StackPanel Metric(string title, out TextBlock value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; panel.Children.Add(Text(title, 12, "muted"));
        value = Text("", 28, "text", FontWeights.SemiBold); value.Margin = new Thickness(0, 7, 0, 0); value.TextWrapping = TextWrapping.NoWrap; value.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(value); return panel;
    }
    private TextBlock Text(string text, double size, string color, FontWeight? weight = null) => new()
    { Text = text, FontSize = size, Foreground = _theme.Color(color), FontFamily = _theme.Font, FontWeight = weight ?? FontWeights.Normal };
    public void Update(DateTimeOffset now)
    {
        var forecast = UsagePredictor.Evaluate(_window, _trend, now);
        _depletion.Text = forecast.State == ForecastState.ResetsFirst ? "—" : forecast.ExhaustsAt is DateTimeOffset target ? AccountTankCard.CompactTime(target, now) : "—";
        _hint.Text = Loc.T(forecast.State switch
        {
            ForecastState.ResetsFirst => "리셋 먼저", ForecastState.Collecting or ForecastState.InsufficientChange => "측정중",
            ForecastState.NoGrowth => "증가 없음", ForecastState.Stale => "갱신 필요", ForecastState.Exhausted => "한도 도달",
            ForecastState.ResetElapsed => "갱신 대기", _ => "계속 사용 기준"
        });
        _depletion.ToolTip = UsagePresentation.Outlook(forecast) + "\n" + UsagePresentation.Basis(forecast)
            + (forecast.ExhaustsAt is DateTimeOffset time ? "\n" + Loc.T(forecast.State == ForecastState.ResetsFirst ? "예상 소진 · 리셋이 없을 경우" : "예상 소진 (한국 시간)") + "\n" + UsagePresentation.KoreanTime(time).ToString("MM'/'dd HH:mm", Loc.Culture) : "");
        _reset.Text = _window.ResetsAt is long reset ? AccountTankCard.CompactTime(DateTimeOffset.FromUnixTimeSeconds(reset), now) : "—";
        _reset.ToolTip = _window.ResetLabel + "\n" + _window.TimeUntilReset(now);
        _chart.Update(now, forecast);
    }
}
