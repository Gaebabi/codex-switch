using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal sealed record DiagnosticModelBadge(string Name, bool HuggingFace)
{
    internal static bool IsHuggingFace(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host is "huggingface.co" or "hf.co" || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".endpoints.huggingface.cloud", StringComparison.OrdinalIgnoreCase));
}

internal sealed class DiagnosticStripeGauge : FrameworkElement
{
    private static readonly string[] PhaseKeys = { "diagnostics.stats.other", "diagnostics.stats.model_request", "diagnostics.stats.tools", "diagnostics.stats.approval", "diagnostics.stats.first_text_wait" };
    private static readonly string[] Palette = { "muted", "accent", "success", "warning" };
    internal static readonly DiagnosticPhase[] PhaseOrder = { DiagnosticPhase.Model, DiagnosticPhase.Tool, DiagnosticPhase.Approval, DiagnosticPhase.Other };
    internal static string Label(DiagnosticPhase phase) => Loc.T(PhaseKeys[(int)phase]);
    internal static Brush Color(Burn0Theme theme, DiagnosticPhase phase) => theme.Color(Palette[(int)phase]);
    internal static Brush FirstTextColor(Burn0Theme theme) => theme.Dark ? Brushes.MediumPurple : Brushes.DarkViolet;

    private readonly Brush[] _phases;
    private readonly Brush _track, _firstText;
    private readonly Pen _stripes, _cursor, _cursorOutline;
    private readonly TextBlock _hoverText;
    private readonly Popup _hover;
    private IReadOnlyList<DiagnosticInterval> _intervals = Array.Empty<DiagnosticInterval>();
    private IReadOnlyList<DiagnosticTool> _tools = Array.Empty<DiagnosticTool>();
    private bool _isTimeline;
    private double _elapsed;
    private double? _firstTextMs, _pointerX;
    internal bool HoverOpen => _hover.IsOpen;

    internal DiagnosticStripeGauge(Burn0Theme theme)
    {
        Height = 20;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        FlowDirection = FlowDirection.LeftToRight;
        _track = theme.Color("divider");
        _firstText = FirstTextColor(theme);
        _phases = Palette.Select(theme.Color).Cast<Brush>().Append(_firstText).ToArray();
        var stripe = theme.Color("canvas").Clone(); stripe.Opacity = .22;
        _stripes = new Pen(stripe, 1);
        _cursorOutline = new Pen(Brushes.Black, 3);
        _cursor = new Pen(Brushes.White, 1);
        _hoverText = new TextBlock { FontFamily = theme.Font, FontSize = 12, Foreground = theme.Color("text"), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        _hover = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.RelativePoint, AllowsTransparency = true, IsHitTestVisible = false,
            Child = new Border { Child = _hoverText, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(theme.Number("radius")),
                Background = theme.Color("elevated"), BorderBrush = theme.Color("divider"), BorderThickness = new Thickness(1), IsHitTestVisible = false }
        };
        Unloaded += (_, _) => PointAt(null);
    }

    internal void Set(DiagnosticShares shares)
    {
        var durations = new[] { shares.Model, shares.Tools, shares.Approval, shares.Other };
        var offset = 0d;
        var intervals = new List<DiagnosticInterval>();
        for (var i = 0; i < durations.Length; i++)
        {
            if (durations[i] > 0) intervals.Add(new(offset, offset + durations[i], PhaseOrder[i]));
            offset += durations[i];
        }
        Set(shares, offset, intervals, Array.Empty<DiagnosticTool>(), null, false);
    }

    internal void Set(DiagnosticTurn turn) => Set(turn.Shares, turn.ElapsedMs, OverlayFirstText(turn.Timeline, turn.FirstTextWindows), turn.Tools, turn.FirstTextMs, true);

    internal static IReadOnlyList<DiagnosticInterval> OverlayFirstText(IReadOnlyList<DiagnosticInterval> timeline, IReadOnlyList<DiagnosticInterval> windows)
    {
        if (windows.Count == 0) return timeline;
        var painted = new List<DiagnosticInterval>();
        void Add(double start, double end, DiagnosticPhase phase)
        {
            if (end <= start) return;
            if (painted.Count > 0 && painted[^1].Phase == phase && painted[^1].EndMs == start)
                painted[^1] = painted[^1] with { EndMs = end };
            else painted.Add(new(start, end, phase));
        }
        foreach (var phase in timeline)
        {
            var start = phase.StartMs;
            if (phase.Phase is DiagnosticPhase.Model or DiagnosticPhase.Other)
            {
                foreach (var wait in windows)
                {
                    if (wait.EndMs <= start) continue;
                    if (wait.StartMs >= phase.EndMs) break;
                    var waitStart = Math.Max(start, wait.StartMs);
                    Add(start, waitStart, phase.Phase);
                    start = Math.Min(phase.EndMs, wait.EndMs);
                    Add(waitStart, start, DiagnosticPhase.FirstText);
                }
            }
            Add(start, phase.EndMs, phase.Phase);
        }
        return painted;
    }

    private void Set(DiagnosticShares shares, double elapsed, IReadOnlyList<DiagnosticInterval> intervals, IReadOnlyList<DiagnosticTool> tools, double? firstText, bool isTimeline)
    {
        _intervals = intervals;
        _tools = tools;
        _isTimeline = isTimeline;
        _elapsed = elapsed;
        _firstTextMs = firstText;
        var summary = string.Join("\n", DiagnosticStatisticsView.ShareLabels(shares));
        if (firstText is double duration) summary += "\n" + Loc.T("diagnostics.stats.first_text_wait") + " · " + Timing(duration);
        AutomationProperties.SetName(this, summary);
        PointAt(_pointerX);
    }

    private static string Timing(double milliseconds) => milliseconds < 1000
        ? milliseconds.ToString("0.#", Loc.Culture) + " ms"
        : (milliseconds / 1000).ToString("0.##", Loc.Culture) + " s";

    internal string? DetailAt(double x)
    {
        if (_elapsed <= 0 || ActualWidth <= 0 || _intervals.Count == 0 || x < 0 || x > ActualWidth) return null;
        var offset = _elapsed * x / ActualWidth;
        var phase = _intervals.FirstOrDefault(p => offset < p.EndMs) ?? _intervals[^1];
        var duration = phase.EndMs - phase.StartMs;
        if (!_isTimeline) return Loc.F("diagnostics.stats.share", Label(phase.Phase), Timing(duration), duration * 100 / _elapsed);
        var detail = Label(phase.Phase) + " · " + Timing(duration) + "\n" +
            Loc.F("diagnostics.stats.hover_time", Timing(offset), Timing(phase.StartMs), Timing(phase.EndMs));
        if (_firstTextMs is double ttft && offset <= ttft)
            detail += "\n" + Loc.T("diagnostics.stats.first_text_wait") + " · " + Timing(ttft);
        if (phase.Phase is DiagnosticPhase.Tool or DiagnosticPhase.Approval)
        {
            var active = _tools.Where(t => t.StartMs <= offset && offset < t.EndMs).ToArray();
            foreach (var tool in active.Take(3))
                detail += "\n" + tool.Name + " · " + Timing(tool.EndMs - tool.StartMs) + (tool.Completed ? "" : " · " + Loc.T("diagnostics.stats.partial"));
            if (active.Length > 3) detail += "\n" + Loc.F("diagnostics.stats.more_tools", active.Length - 3);
        }
        return detail;
    }

    internal void PointAt(double? x)
    {
        var detail = x.HasValue ? DetailAt(x.Value) : null;
        _pointerX = detail == null ? null : x;
        if (detail == null)
            _hover.IsOpen = false;
        else
        {
            _hoverText.Text = detail;
            _hover.HorizontalOffset = x!.Value + 6;
            _hover.VerticalOffset = ActualHeight + 6;
            _hover.IsOpen = IsLoaded;
        }
        InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); PointAt(e.GetPosition(this).X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); PointAt(e.GetPosition(this).X); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); PointAt(null); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 3, 3));
        drawing.DrawRectangle(_track, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_elapsed > 0)
        {
            var scale = ActualWidth / _elapsed;
            foreach (var phase in _intervals)
                drawing.DrawRectangle(_phases[(int)phase.Phase], null, new Rect(scale * phase.StartMs, 0, scale * (phase.EndMs - phase.StartMs), ActualHeight));
            for (double x = -ActualHeight; x < ActualWidth; x += 8)
                drawing.DrawLine(_stripes, new Point(x, ActualHeight), new Point(x + ActualHeight, 0));
        }
        if (_pointerX is double pointer)
        {
            var line = Math.Clamp(Math.Floor(pointer) + .5, .5, Math.Max(.5, ActualWidth - .5));
            drawing.DrawLine(_cursorOutline, new Point(line, 0), new Point(line, ActualHeight));
            drawing.DrawLine(_cursor, new Point(line, 0), new Point(line, ActualHeight));
        }
        drawing.Pop();
    }
}

internal sealed class DiagnosticStatisticsView : Grid
{
    private sealed record ChatRow(ListBoxItem Item, TextBlock Title, TextBlock Info, DiagnosticStripeGauge Gauge, FrameworkElement Spinner);
    private sealed record TurnRow(Border Item, TextBlock Heading, TextBlock Metrics, DiagnosticStripeGauge Gauge, FrameworkElement Spinner, StackPanel Badges)
    {
        internal (string? Model, string? Tier, string? Effort, string? ReturnedModel, string? ReturnedTier)? Metadata;
    }
    private readonly Burn0Theme _theme;
    private readonly IReadOnlyDictionary<string, DiagnosticModelBadge>? _models;
    private readonly ListBox _sessions;
    private readonly StackPanel _turns = new();
    private readonly TextBlock _heading, _summary, _empty;
    private readonly Button _openChat;
    private readonly Dictionary<string, ChatRow> _chatRows = new();
    private readonly Dictionary<string, ListBoxItem> _groupRows = new();
    private readonly Dictionary<string, TurnRow> _turnRows = new();
    private IReadOnlyList<DiagnosticChat> _snapshot = Array.Empty<DiagnosticChat>();
    private DiagnosticChat[] _liveChats = Array.Empty<DiagnosticChat>();
    private long _snapshotTick;
    private Func<string, string> _titles = id => id;
    private Func<string, bool> _matched = _ => false;
    private bool _updating;
    internal string? SelectedChat { get; private set; }
    internal static IEnumerable<string> ShareLabels(DiagnosticShares shares)
    {
        var values = new[] { shares.Model, shares.Tools, shares.Approval, shares.Other };
        return DiagnosticStripeGauge.PhaseOrder.Select((phase, i) => Loc.F("diagnostics.stats.share",
            DiagnosticStripeGauge.Label(phase), Duration(values[i]), shares.Total > 0 ? values[i] * 100 / shares.Total : 0));
    }
    internal static string Duration(double milliseconds, bool showTenths = false)
    {
        var rounded = Math.Round(milliseconds / 100, MidpointRounding.AwayFromZero) / 10;
        if (rounded < 60) return rounded.ToString("0.0", Loc.Culture) + "s";
        var seconds = Math.Floor(rounded);
        var duration = TimeSpan.FromSeconds(seconds);
        var hours = (long)duration.TotalHours;
        var secondsLabel = showTenths ? (rounded % 60).ToString("0.0", Loc.Culture) : duration.Seconds.ToString(Loc.Culture);
        return (hours > 0 ? hours.ToString(Loc.Culture) + "h " : "") +
            duration.Minutes.ToString(Loc.Culture) + "m " + secondsLabel + "s";
    }

    internal DiagnosticStatisticsView(Burn0Theme theme, Action<string>? openChat = null, IReadOnlyDictionary<string, DiagnosticModelBadge>? models = null)
    {
        _theme = theme; _models = models; MinHeight = 180;
        ColumnDefinitions.Add(new() { Width = new GridLength(260) }); ColumnDefinitions.Add(new());
        SizeChanged += (_, _) => ColumnDefinitions[0].Width = new GridLength(ActualWidth < 720 ? 190 : 260);
        var side = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        var caption = Label(Loc.T("diagnostics.stats.sessions"), 12, true); caption.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(caption, Dock.Top); side.Children.Add(caption);
        _sessions = new ListBox { Name = "DiagnosticSessions", Background = theme.Color("canvas"), Foreground = theme.Color("text"),
            BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemContainerStyle = theme.ListRowStyle() };
        ScrollViewer.SetHorizontalScrollBarVisibility(_sessions, ScrollBarVisibility.Disabled);
        _sessions.SelectionChanged += (_, _) => { if (!_updating && _sessions.SelectedItem is ListBoxItem item && item.Tag is string id) { SelectedChat = id; RenderTurns(); } };
        side.Children.Add(_sessions); Children.Add(side);
        var main = new DockPanel(); Grid.SetColumn(main, 1); Children.Add(main);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        var titleRow = new DockPanel();
        _openChat = new Button { Name = "DiagnosticOpenChat", Content = Loc.T("diagnostics.stats.open_chat"), FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        _openChat.Click += (_, _) => { if (SelectedChat != null && _matched(SelectedChat)) openChat?.Invoke(SelectedChat); }; DockPanel.SetDock(_openChat, Dock.Right); titleRow.Children.Add(_openChat);
        _heading = Label(Loc.T("diagnostics.stats.turns"), 15, true); _heading.TextTrimming = TextTrimming.CharacterEllipsis; titleRow.Children.Add(_heading); header.Children.Add(titleRow);
        _summary = Label("", 12); _summary.Foreground = theme.Color("muted"); _summary.Margin = new Thickness(0, 5, 0, 10); header.Children.Add(_summary);
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ToolTip = Loc.T("diagnostics.stats.legend_hint") };
        void AddLegend(string label, Brush brush)
        {
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 3) };
            entry.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = brush, Margin = new Thickness(0, 0, 5, 0) });
            entry.Children.Add(Label(label, 11)); legend.Children.Add(entry);
        }
        AddLegend(Loc.T("diagnostics.stats.first_text_wait"), DiagnosticStripeGauge.FirstTextColor(theme));
        foreach (var phase in DiagnosticStripeGauge.PhaseOrder) AddLegend(DiagnosticStripeGauge.Label(phase), DiagnosticStripeGauge.Color(theme, phase));
        header.Children.Add(legend);
        _empty = Label(Loc.T("diagnostics.stats.empty"), 13); _empty.TextWrapping = TextWrapping.Wrap; _empty.Margin = new Thickness(0, 20, 0, 0);
        Grid.SetIsSharedSizeScope(_turns, true);
        _turns.Children.Add(_empty);
        main.Children.Add(new ScrollViewer { Name = "DiagnosticTurns", Content = _turns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private TextBlock Label(string text, double size, bool bold = false) => new() { Text = text, FontFamily = _theme.Font, FontSize = size, Foreground = _theme.Color("text"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    private FrameworkElement Spinner(string name, Thickness margin)
    {
        var spinner = Burn0Theme.LoadingSpinner();
        spinner.Name = name; spinner.Margin = margin; spinner.Stroke = _theme.Color("accent");
        spinner.VerticalAlignment = VerticalAlignment.Center;
        return spinner;
    }

    internal static string SpeedName(string? tier) => tier switch
    {
        "priority" or "fast" => "Fast", "ultrafast" => "UltraFast", "default" or "standard" => "Standard",
        _ => tier ?? Loc.T("diagnostics.stats.badge_unknown")
    };

    private static readonly Lazy<Task<BitmapSource?>> HuggingFaceIcon = new(LoadHuggingFaceIcon);

    private static async Task<BitmapSource?> LoadHuggingFaceIcon()
    {
        const string logo = "https://huggingface.co/datasets/huggingface/brand-assets/resolve/7e7df7da9642aaae2ad311be8bf12a63dddbc6d2/hf-logo.png";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 256 * 1024 };
        try
        {
            var bytes = await client.GetByteArrayAsync(logo).ConfigureAwait(false);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != "9CF16F4F32604EAF76DABBDF47701EEA5A768EBCC7296ACC1D1758181F71DB73")
                return null;
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 36; bitmap.StreamSource = stream; bitmap.EndInit();
            bitmap.Freeze(); return bitmap;
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) { return null; }
    }

    private static async void ApplyHuggingFaceIcon(Border badge)
    {
        if (await HuggingFaceIcon.Value is { } logo) badge.Child = new Image { Source = logo, Width = 15, Height = 15 };
    }

    private Border Badge(string name, string path, string detail, string color)
    {
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = _theme.Color(color),
            StrokeThickness = 1.25, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform, Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var badge = new Border { Name = name, Child = glyph, Width = 22, Height = 21, Margin = new Thickness(3, 0, 0, 0),
            Background = _theme.Color("surface"), CornerRadius = new CornerRadius(4), ToolTip = Hover(detail) };
        AutomationProperties.SetName(badge, detail);
        return badge;
    }

    private void RenderBadges(TurnRow row, DiagnosticTurn turn)
    {
        var metadata = (turn.Model, turn.Tier, turn.Effort, turn.ReturnedModel, turn.ReturnedTier);
        if (row.Metadata == metadata) return;
        row.Metadata = metadata;
        row.Badges.Children.Clear();
        var unknown = Loc.T("diagnostics.stats.badge_unknown");
        var model = turn.Model != null ? _models?.GetValueOrDefault(turn.Model) : null;
        var requested = model?.Name ?? turn.Model ?? unknown;
        var modelBadge = Badge("DiagnosticModelBadge", "M3 3H13V13H3Z M6 6H10V10H6Z M0 6H3 M0 10H3 M13 6H16 M13 10H16 M6 0V3 M10 0V3 M6 13V16 M10 13V16",
            Loc.F("diagnostics.stats.badge_model", requested, turn.ReturnedModel ?? unknown), "text");
        row.Badges.Children.Add(modelBadge);
        if (model?.HuggingFace == true) ApplyHuggingFaceIcon(modelBadge);
        var effortPath = turn.Effort switch
        {
            "minimal" or "low" => "M2 14H12 M3 12V8",
            "medium" => "M2 14H12 M3 12V8 M7 12V5",
            "high" => "M2 14H12 M3 12V8 M7 12V5 M11 12V2",
            "xhigh" or "max" or "ultra" => "M1 14H15 M2 12V9 M6 12V7 M10 12V4 M14 12V1",
            _ => "M3 13H13"
        };
        row.Badges.Children.Add(Badge("DiagnosticEffortBadge", effortPath, Loc.F("diagnostics.stats.badge_effort", turn.Effort ?? unknown), "text"));
        var speed = SpeedName(turn.Tier);
        var speedPath = speed switch
        {
            "UltraFast" => "M5 1L1 8H4L2 15L8 7H5 M12 1L8 8H11L9 15L15 7H12",
            "Fast" => "M9 1L3 9H7L5 15L13 6H9Z",
            "Standard" => "M8 1A7 7 0 1 0 8 15A7 7 0 1 0 8 1 M8 4V8H11",
            _ => "M3 8H13"
        };
        row.Badges.Children.Add(Badge("DiagnosticSpeedBadge", speedPath,
            Loc.F("diagnostics.stats.badge_speed", speed, turn.Tier ?? unknown, turn.ReturnedTier ?? unknown),
            speed is "Fast" or "UltraFast" ? "accent" : "muted"));
    }

    internal void Render(IReadOnlyList<DiagnosticChat> chats, Func<string, string> titles, Func<string, bool>? matched = null,
        IReadOnlyDictionary<string, NativeHistoryThreads.SidebarEntry>? sidebar = null, long? snapshotTick = null)
    {
        _snapshot = chats; _titles = titles; _matched = matched ?? (_ => false); _updating = true;
        _snapshotTick = snapshotTick ?? Stopwatch.GetTimestamp();
        _liveChats = chats.Where(chat => chat.Turns.Any(turn => turn.Running)).ToArray();
        NativeHistoryThreads.SidebarEntry? Entry(DiagnosticChat chat) => sidebar?.GetValueOrDefault(chat.Id);
        bool Pinned(DiagnosticChat chat) => Entry(chat) is { PinnedRank: < int.MaxValue };
        string Group(DiagnosticChat chat) => Pinned(chat) ? "pinned" : Entry(chat)?.Project ?? "ungrouped";
        var ordered = chats.OrderBy(c => Pinned(c) ? 0 : Entry(c)?.Project != null ? 1 : 2)
            .ThenBy(c => Pinned(c) ? 0 : Entry(c)?.ProjectRank ?? int.MaxValue)
            .ThenBy(Group, StringComparer.Ordinal)
            .ThenBy(c => Pinned(c) ? Entry(c)!.PinnedRank : Entry(c)?.ChatRank ?? int.MaxValue)
            .ThenByDescending(c => Entry(c)?.Recency ?? 0).ToArray();
        var desired = new List<ListBoxItem>();
        foreach (var absent in _chatRows.Keys.Where(id => !chats.Any(c => c.Id == id)).ToArray()) { _sessions.Items.Remove(_chatRows[absent].Item); _chatRows.Remove(absent); }
        string? lastGroup = null;
        foreach (var chat in ordered)
        {
            var group = Group(chat);
            if (sidebar != null && lastGroup != group)
            {
                var name = Pinned(chat) ? Loc.T("diagnostics.stats.pinned") : Entry(chat)?.ProjectName ?? Loc.T("diagnostics.stats.sessions");
                if (!_groupRows.TryGetValue(group, out var header))
                {
                    var label = Label(name, 11, true); label.Foreground = _theme.Color("muted");
                    label.Margin = new Thickness(7, 6, 0, 2);
                    header = new ListBoxItem { Content = label, IsEnabled = false, Focusable = false, Padding = new Thickness(0) };
                    _groupRows[group] = header;
                }
                ((TextBlock)header.Content).Text = name;
                desired.Add(header);
                lastGroup = group;
            }
            if (!_chatRows.TryGetValue(chat.Id, out var row))
            {
                var content = new Grid { Height = 32, Margin = new Thickness(7, 0, 7, 0) };
                content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new GridLength(44) });
                var title = Label("", 12, true); title.TextTrimming = TextTrimming.CharacterEllipsis;
                var info = Label("", 11); info.Foreground = _theme.Color("muted"); info.Margin = new Thickness(8, 0, 8, 0);
                var gauge = new DiagnosticStripeGauge(_theme) { Height = 12 };
                title.VerticalAlignment = VerticalAlignment.Center; info.VerticalAlignment = VerticalAlignment.Center; gauge.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(info, 1); Grid.SetColumn(gauge, 2);
                var caption = new DockPanel();
                var spinner = Spinner("DiagnosticChatSpinner", new Thickness(0, 0, 5, 0));
                DockPanel.SetDock(spinner, Dock.Left); caption.Children.Add(spinner); caption.Children.Add(title);
                content.Children.Add(caption); content.Children.Add(info); content.Children.Add(gauge);
                var item = new ListBoxItem { Content = content, Tag = chat.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
                row = new(item, title, info, gauge, spinner); _chatRows[chat.Id] = row;
            }
            row.Title.Text = titles(chat.Id);
            row.Title.ToolTip = Hover(titles(chat.Id) + "\n" + chat.Id + "\n" + Loc.F("diagnostics.stats.session_summary", chat.Turns.Count, Duration(chat.Shares.Total)) + (_matched(chat.Id) ? "" : "\n" + Loc.T("diagnostics.stats.unmatched")));
            row.Info.Text = Duration(chat.Shares.Total); row.Gauge.Set(chat.Shares);
            row.Spinner.Visibility = chat.Turns.Any(turn => turn.Running) ? Visibility.Visible : Visibility.Collapsed;
            desired.Add(row.Item);
        }
        foreach (var stale in _groupRows.Where(pair => !desired.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
        { _sessions.Items.Remove(_groupRows[stale]); _groupRows.Remove(stale); }
        for (var i = 0; i < desired.Count; i++)
        {
            var current = _sessions.Items.IndexOf(desired[i]);
            if (current == i) continue;
            if (current >= 0) _sessions.Items.RemoveAt(current);
            _sessions.Items.Insert(i, desired[i]);
        }
        if (SelectedChat == null || !chats.Any(c => c.Id == SelectedChat)) SelectedChat = ordered.FirstOrDefault()?.Id;
        _sessions.SelectedItem = SelectedChat == null ? null : _chatRows[SelectedChat].Item;
        _updating = false; RenderTurns();
        UpdateLiveElapsed();
    }

    internal void UpdateLiveElapsed() => UpdateLiveElapsed(Stopwatch.GetElapsedTime(_snapshotTick).TotalMilliseconds);

    internal void UpdateLiveElapsed(double sinceSnapshot)
    {
        foreach (var chat in _liveChats)
        {
            var running = false;
            var elapsed = chat.Shares.Total;
            foreach (var turn in chat.Turns)
            {
                if (!turn.Running) continue;
                elapsed += Math.Min(sinceSnapshot, turn.LiveRemainingMs);
                running |= sinceSnapshot < turn.LiveRemainingMs;
            }
            var row = _chatRows[chat.Id];
            row.Info.Text = Duration(elapsed, running);
            row.Spinner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            if (chat.Id != SelectedChat) continue;
            _summary.Text = Summary(chat, elapsed, running);
            for (var i = 0; i < chat.Turns.Count; i++)
            {
                var turn = chat.Turns[i];
                if (!turn.Running) continue;
                var active = sinceSnapshot < turn.LiveRemainingMs;
                var current = _turnRows[turn.Key];
                current.Heading.Text = Loc.F("diagnostics.stats.turn_label", chat.Turns.Count - i) + "   " +
                    Duration(turn.ElapsedMs + Math.Min(sinceSnapshot, turn.LiveRemainingMs), active);
                current.Spinner.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static string Summary(DiagnosticChat chat, double elapsed, bool showTenths = false) =>
        Loc.T("diagnostics.stats.total") + " " + Duration(elapsed, showTenths) + " · " + Loc.F("diagnostics.stats.calls", chat.Turns.Sum(t => t.Calls), chat.Turns.Sum(t => t.Errors));

    private void RenderTurns()
    {
        var chat = _snapshot.FirstOrDefault(c => c.Id == SelectedChat);
        _openChat.IsEnabled = chat != null && _matched(chat.Id);
        _empty.Visibility = chat == null ? Visibility.Visible : Visibility.Collapsed;
        if (chat == null)
        {
            foreach (var row in _turnRows.Values) _turns.Children.Remove(row.Item);
            _turnRows.Clear(); _heading.Text = Loc.T("diagnostics.stats.turns"); _summary.Text = ""; return;
        }
        _heading.Text = _titles(chat.Id); _heading.ToolTip = chat.Id;
        _summary.Text = Summary(chat, chat.Shares.Total);
        foreach (var absent in _turnRows.Keys.Where(id => !chat.Turns.Any(t => t.Key == id)).ToArray()) { _turns.Children.Remove(_turnRows[absent].Item); _turnRows.Remove(absent); }
        for (var i = 0; i < chat.Turns.Count; i++)
        {
            var turn = chat.Turns[i];
            if (!_turnRows.TryGetValue(turn.Key, out var row))
            {
                var content = new StackPanel { Margin = new Thickness(0, 7, 4, 7) };
                var top = new DockPanel(); var heading = Label("", 12, true); heading.TextTrimming = TextTrimming.CharacterEllipsis;
                var spinner = Spinner("DiagnosticTurnSpinner", new Thickness(0, 0, 5, 0));
                var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(badges, Dock.Right); top.Children.Add(badges);
                DockPanel.SetDock(spinner, Dock.Left); top.Children.Add(spinner); top.Children.Add(heading); content.Children.Add(top);
                heading.VerticalAlignment = VerticalAlignment.Center;
                var bottom = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto, SharedSizeGroup = "TimingMetrics" }); bottom.ColumnDefinitions.Add(new());
                var metrics = Label("", 11); metrics.Margin = new Thickness(0, 0, 10, 0);
                var gauge = new DiagnosticStripeGauge(_theme) { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(gauge, 1); bottom.Children.Add(metrics); bottom.Children.Add(gauge); content.Children.Add(bottom);
                var item = new Border { Name = "DiagnosticTurnRow", Child = content, BorderBrush = _theme.Color("divider"), BorderThickness = new Thickness(0, 0, 0, 1) };
                ToolTipService.SetShowDuration(heading, 20000);
                row = new(item, heading, metrics, gauge, spinner, badges); _turnRows[turn.Key] = row; _turns.Children.Insert(Math.Min(i + 1, _turns.Children.Count), item);
            }
            var status = turn.Running ? Loc.T("diagnostics.stats.running") : turn.Status switch { "failed" => Loc.T("diagnostics.stats.failed"), "captureEnded" or "interrupted" => Loc.T("diagnostics.stats.capture_ended"), _ => Loc.T("diagnostics.stats.completed") };
            row.Heading.Text = Loc.F("diagnostics.stats.turn_label", chat.Turns.Count - i) + "   " + Duration(turn.ElapsedMs);
            row.Spinner.Visibility = turn.Running ? Visibility.Visible : Visibility.Collapsed;
            row.Gauge.Set(turn);
            RenderBadges(row, turn);
            var timed = turn.Llm.Where(c => c.Precise && !c.Ambiguous).ToArray();
            var eligible = timed.Where(c => c.StreamTpsApprox.HasValue).ToArray();
            var rate = eligible.Length > 0 ? eligible.Sum(c => c.DeliveredTokens!.Value) * 1000d / eligible.Sum(c => c.End!.Value - c.FirstOutput!.Value) : (double?)null;
            string Rate(double? value) => value.HasValue ? value.Value.ToString("0.0", Loc.Culture) : "—";
            row.Metrics.Text = Loc.F("diagnostics.stats.llm_metrics", turn.FirstTextMs is double firstText ? Duration(firstText) : "—", rate.HasValue ? "≈" + Rate(rate) : "—");
            row.Metrics.ToolTip = Hover(Loc.T("diagnostics.stats.timing_hint") + (!turn.Llm.Any(c => c.Precise) ? "\n" + Loc.T("diagnostics.stats.timing_unavailable") : "\n" + Loc.T("diagnostics.stats.recent_calls") + "\n" + string.Join("\n", turn.Llm.TakeLast(3).Select((call, index) => Loc.F("diagnostics.stats.call_timing", turn.Llm.Count - Math.Min(3, turn.Llm.Count) + index + 1, call.TtftMs.HasValue ? Duration(call.TtftMs.Value) : "—", Rate(call.StreamTpsApprox), Rate(call.EndToEndTps), call.MaxGapMs.HasValue && !call.Ambiguous ? Duration(call.MaxGapMs.Value) : "—", call.P95GapMs.HasValue && !call.Ambiguous ? Duration(call.P95GapMs.Value) : "—", call.Chunks)))));
            row.Heading.ToolTip = Hover(status + (turn.Partial ? " · " + Loc.T("diagnostics.stats.partial") : "") + "\n" +
                turn.Started.ToLocalTime().ToString("G", Loc.Culture) + "\n" + Loc.F("diagnostics.stats.calls", turn.Calls, turn.Errors) + "\n" + string.Join("\n", ShareLabels(turn.Shares)) +
                "\n\n" + (turn.Model ?? "—") + (turn.Tier == null ? "" : " · " + turn.Tier) + "\n" + Loc.F("diagnostics.stats.tokens", turn.Input, turn.Output, turn.Cached) + "\n" + turn.Id +
                (turn.Partial ? "\n" + Loc.T("diagnostics.stats.partial_hint") : ""));
        }
    }
    private static TextBlock Hover(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
}
