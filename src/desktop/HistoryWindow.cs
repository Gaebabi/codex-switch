using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal sealed class HistoryWindow : Window
{
    private readonly AccountService _service;
    private readonly string _key;
    private readonly DeskTheme _theme;
    private string? _selection;
    private bool _closed;
    public HistoryWindow(AccountService service, string key, DeskTheme theme)
    {
        _service = service; _key = key; _theme = theme; Title = Loc.T("추이 보기"); Width = 640; Height = 380; MinWidth = 500; MinHeight = 300;
        theme.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        service.Changed += Changed; Loc.Changed += Changed; Closed += (_, _) => { _closed = true; service.Changed -= Changed; Loc.Changed -= Changed; }; Render();
    }
    private void Changed() => Dispatcher.UIThread.Post(() => { if (!_closed) Render(); });
    private void Render()
    {
        Title = Loc.T("추이 보기"); FlowDirection = Loc.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var profile = _service.Accounts.FirstOrDefault(p => p.Key == _key);
        if (profile == null) { Close(); return; }
        var choices = MacAccountCard.Choices(profile); var selected = MacAccountCard.Select(profile, _selection);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(16) };
        var selectors = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var choice in choices)
        {
            var button = _theme.Button(choice.BucketLabel + " " + choice.Window.PeriodLabel, () => { _selection = choice.BucketId + ":" + choice.Slot; Render(); }); button.Margin = new Thickness(0, 0, 6, 6); selectors.Children.Add(button);
        }
        foreach (var hours in new[] { 6, 24, 168 })
        {
            var button = _theme.Button(hours == 168 ? Loc.T("7일") : Loc.F("{0}시간", hours), () =>
            { lock (_service.Vault.SyncRoot) { _service.Vault.Data.TrendHours = hours; _service.Vault.Save(); } Render(); });
            button.Margin = new Thickness(0, 0, 6, 6); selectors.Children.Add(button);
        }
        root.Children.Add(selectors);
        if (selected != null)
        {
            var points = UsageHistory.ForWindow(profile, selected.BucketId, selected.Slot, selected.Window);
            var now = DateTimeOffset.UtcNow; var forecast = UsagePredictor.Evaluate(selected.Window, points, now);
            var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 14, 0, 12) };
            var values = new[] { selected.Window.Remaining.ToString("0.#", Loc.Culture) + "%",
                forecast.State != ForecastState.ResetsFirst && forecast.ExhaustsAt is DateTimeOffset end ? MacAccountCard.Compact(end, now) : "—",
                selected.Window.ResetsAt is long reset ? MacAccountCard.Compact(DateTimeOffset.FromUnixTimeSeconds(reset), now) : "—" };
            var labels = new[] { "잔여량", "예상 소진", "다음 리셋" };
            for (var i = 0; i < 3; i++)
            {
                var cell = new StackPanel(); cell.Children.Add(_theme.Text(Loc.T(labels[i]), 11, "muted")); var number = _theme.Text(values[i], i == 0 ? 38 : 24); number.FontWeight = FontWeight.SemiBold; number.FlowDirection = FlowDirection.LeftToRight;
                cell.Children.Add(number); Grid.SetColumn(cell, i); metrics.Children.Add(cell);
            }
            Grid.SetRow(metrics, 1); root.Children.Add(metrics);
            var chart = new HistoryChart { Points = points, Palette = _theme, Hours = _service.Vault.Data.TrendHours, MinHeight = 160 };
            Grid.SetRow(chart, 2); root.Children.Add(chart);
            var hint = _theme.Text(UsagePresentation.Outlook(forecast) + "  " + UsagePresentation.Basis(forecast), 11, "muted"); hint.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(hint, 3); root.Children.Add(hint);
        }
        var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        scroll.SizeChanged += (_, _) => root.MinHeight = System.Math.Max(0, scroll.Bounds.Height - root.Margin.Top - root.Margin.Bottom);
        Content = scroll;
    }
}
