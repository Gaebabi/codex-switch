using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexSwitch.Diagnostics;

namespace CodexAccountDesk;

internal sealed class DiagnosticRelayWindow : Window
{
    private readonly RelayInstallation _installation;
    private readonly DiagnosticStatistics _statistics;
    private readonly DiagnosticEventFeed _feed;
    private readonly DiagnosticStatisticsView _board;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _events = new() { Height = 96, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _native = new() { IsReadOnly = true, Margin = new Thickness(0, 7, 0, 7) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly CancellationTokenSource _stop = new();
    private readonly string _relay;
    private readonly Expander _raw;
    private readonly MenuItem _mode;
    private readonly Dictionary<string, string> _titles = new();
    private readonly Func<IEnumerable<string>, IReadOnlyDictionary<string, string>>? _lookupTitles;
    private readonly Func<IEnumerable<string>, IReadOnlyDictionary<string, NativeHistoryThreads.SidebarEntry>>? _lookupSidebar;
    private readonly Dictionary<string, NativeHistoryThreads.SidebarEntry> _sidebar = new();
    private readonly Dictionary<string, DateTimeOffset> _titleTimes = new();
    private bool _reading, _closed;

    internal DiagnosticRelayWindow(Window owner, string native, RelayInstallation? installation = null, string? home = null,
        string? relayExecutable = null, Burn0Theme? theme = null, DiagnosticStatistics? statistics = null,
        Func<IEnumerable<string>, IReadOnlyDictionary<string, string>>? titleLookup = null, string? stateDirectory = null, Action<string>? openChat = null,
        Func<IEnumerable<string>, IReadOnlyDictionary<string, NativeHistoryThreads.SidebarEntry>>? sidebarLookup = null,
        IReadOnlyDictionary<string, DiagnosticModelBadge>? modelBadges = null)
    {
        var palette = theme ?? new Burn0Theme(false);
        Title = Loc.T("diagnostics.title"); Width = 1000; Height = 670; MinWidth = 540; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = palette.Color("canvas"); Foreground = palette.Color("text"); FontFamily = palette.Font;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Resources[typeof(Button)] = palette.ButtonStyles().Standard;
        ScrollTheme.Install(Resources, palette);
        ToolTipTheme.Install(Resources, palette);
        _relay = relayExecutable ?? Path.Combine(AppContext.BaseDirectory, "diagnostics", "CodexSwitch.Diagnostics.exe");
        _installation = installation ?? new RelayInstallation(directory: stateDirectory, executable: _relay, home: home);
        _statistics = statistics ?? new(); _feed = new(_installation, _statistics); _lookupTitles = titleLookup; _lookupSidebar = sidebarLookup;
        _native.Text = native;
        var root = new Grid { Margin = new Thickness(16) }; Content = root;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel();
        root.Children.Add(new Border { Child = header, BorderBrush = palette.Color("divider"), BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 10), Margin = new Thickness(0, 0, 0, 12) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        _status.FontSize = 12; _status.Foreground = palette.Color("muted"); _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 14, 0); header.Children.Add(_status);
        Button ActionButton(string key, string name, Action action)
        {
            var button = new Button { Name = name, Content = Loc.T(key), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
            button.Click += (_, _) => { try { action(); RefreshStatus(); } catch (Exception error) { _status.Text = error.Message; } };
            return button;
        }
        _mode = DropdownTheme.Switch(Loc.T("diagnostics.on"), false, palette);
        _mode.Name = "DiagnosticModeSwitch";
        _mode.Foreground = palette.Color("text");
        _mode.Click += (_, _) =>
        {
            try
            {
                if (_installation.RequestedMode == "on_requested") _installation.Disable();
                else
                {
                    if (!File.Exists(_relay)) throw new IOException(Loc.T("diagnostics.missing_package"));
                    _installation.Enable(_native.Text);
                }
                RefreshStatus();
            }
            catch (Exception error) { RefreshStatus(); _status.Text = error.Message; }
        };
        var switches = new Menu { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        switches.Items.Add(_mode); actions.Children.Add(switches);
        _board = new(palette, openChat, modelBadges) { Name = "DiagnosticSessionBoard" }; Grid.SetRow(_board, 1); root.Children.Add(_board);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var setupBody = new StackPanel { Margin = new Thickness(10, 8, 10, 0) };
        var nativeRow = new DockPanel();
        var selectNative = ActionButton("diagnostics.select_native", "DiagnosticSelectNative", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("diagnostics.native_filter") + "|codex.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) _native.Text = picker.FileName;
        });
        DockPanel.SetDock(selectNative, Dock.Right); nativeRow.Children.Add(selectNative);
        PanelLayout.Input(_native, palette); _native.Margin = new Thickness(0); _native.ToolTip = _native.Text;
        _native.TextChanged += (_, _) => _native.ToolTip = _native.Text;
        nativeRow.Children.Add(_native); setupBody.Children.Add(nativeRow);
        setupBody.Children.Add(new TextBlock { Text = Loc.T("diagnostics.restart_hint"), TextWrapping = TextWrapping.Wrap, Foreground = palette.Color("muted"), Margin = new Thickness(0, 8, 0, 8) });
        _events.Background = palette.Color("canvas"); _events.Foreground = palette.Color("muted"); _events.ItemContainerStyle = palette.ListRowStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_events, ScrollBarVisibility.Disabled);
        var disclosure = DisclosureTemplate(palette);
        footer.Children.Add(PanelLayout.Rule(palette, 0, 5));
        footer.Children.Add(new Expander { Name = "DiagnosticSetup", Header = Loc.T("diagnostics.stats.connection"), Content = setupBody, Template = disclosure });
        _raw = new Expander { Name = "DiagnosticRawEvents", Header = Loc.T("diagnostics.stats.raw"), Content = _events, Template = disclosure };
        _raw.Expanded += (_, _) => RenderEvents(); footer.Children.Add(_raw);
        _timer.Tick += async (_, _) => await ReadEvents();
        _elapsedTimer.Tick += (_, _) => _board.UpdateLiveElapsed();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _timer.Stop(); _elapsedTimer.Stop(); return; }
            _timer.Start(); _elapsedTimer.Start();
            _ = Dispatcher.InvokeAsync(ReadEvents, DispatcherPriority.Background);
        };
        Closed += (_, _) => { _closed = true; _stop.Cancel(); _timer.Stop(); _elapsedTimer.Stop(); };
        RefreshStatistics();
    }

    internal void OpenModeless() => Show();

    private static ControlTemplate DisclosureTemplate(Burn0Theme theme)
    {
        var template = new ControlTemplate(typeof(Expander));
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var header = new FrameworkElementFactory(typeof(ToggleButton)); header.Name = "DisclosureHeader";
        header.SetValue(FrameworkElement.MinHeightProperty, theme.Number("control-height"));
        header.SetValue(Control.ForegroundProperty, theme.Color("text"));
        header.SetValue(Control.FontSizeProperty, PanelLayout.BodySize);
        header.SetValue(Control.CursorProperty, Cursors.Hand);
        var parent = RelativeSource.TemplatedParent;
        header.SetBinding(ContentControl.ContentProperty, new Binding("Header") { RelativeSource = parent });
        header.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsExpanded") { RelativeSource = parent, Mode = BindingMode.TwoWay });
        var toggle = new ControlTemplate(typeof(ToggleButton));
        var surface = new FrameworkElementFactory(typeof(Border)); surface.Name = "DisclosureSurface";
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.SetValue(Border.PaddingProperty, new Thickness(10, 0, 10, 0));
        var line = new FrameworkElementFactory(typeof(DockPanel));
        var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)); arrow.Name = "DisclosureArrow";
        arrow.SetValue(FrameworkElement.WidthProperty, 9.0);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M2,1 L6,5 L2,9"));
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeProperty, theme.Color("muted"));
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
        line.AppendChild(arrow);
        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        line.AppendChild(label); surface.AppendChild(line); toggle.VisualTree = surface;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, theme.Color("hover"), "DisclosureSurface")); toggle.Triggers.Add(hover);
        var expanded = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        expanded.Setters.Add(new Setter(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M0,3 L4,7 L8,3"), "DisclosureArrow")); toggle.Triggers.Add(expanded);
        header.SetValue(Control.TemplateProperty, toggle); stack.AppendChild(header);
        var body = new FrameworkElementFactory(typeof(ContentPresenter)); body.Name = "DisclosureContent";
        body.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); stack.AppendChild(body); template.VisualTree = stack;
        var reveal = new Trigger { Property = Expander.IsExpandedProperty, Value = true };
        reveal.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "DisclosureContent")); template.Triggers.Add(reveal);
        return template;
    }

    private string ChatTitle(string id) => _titles.TryGetValue(id, out var title) && !string.IsNullOrWhiteSpace(title) ? title : Loc.T("diagnostics.stats.title_unavailable");
    private bool MatchedChat(string id) => _titles.ContainsKey(id);

    private void RenderEvents()
    {
        _events.Items.Clear();
        foreach (var entry in _feed.RecentEvents.Reverse().Take(30))
        {
            var details = entry.Thread != null && _titles.TryGetValue(entry.Thread, out var title) && !string.IsNullOrWhiteSpace(title)
                ? title + "\n" + entry.Details : entry.Details;
            var text = new TextBlock { Text = entry.At.ToLocalTime().ToString("HH:mm:ss", Loc.Culture) + "   " + entry.Label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } };
            ToolTipService.SetShowDuration(text, 20000); _events.Items.Add(text);
        }
    }

    private void RefreshStatus()
    {
        var live = _statistics.LastHeartbeat.HasValue && DateTimeOffset.UtcNow - _statistics.LastHeartbeat.Value < TimeSpan.FromSeconds(5);
        var requested = _installation.RequestedMode;
        _mode.IsChecked = requested == "on_requested";
        _mode.Header = Loc.T(_mode.IsChecked ? "diagnostics.on" : "diagnostics.off");
        var key = requested switch
        {
            "on_requested" => live ? "diagnostics.stats.live" : "diagnostics.on_requested",
            "off_requested" => live ? "diagnostics.stats.off_live" : "diagnostics.off_requested",
            "custom_backend" => live ? "diagnostics.stats.off_live" : "diagnostics.custom_backend",
            "external_override" => "diagnostics.external_override",
            _ => "diagnostics.unconfigured"
        };
        _status.Text = _feed.Loading ? Loc.T("diagnostics.stats.loading") : Loc.T(key);
        _status.ToolTip = _feed.InvalidRecords > 0 || _statistics.Dropped > 0 ? Loc.T("diagnostics.stats.partial_hint") : null;
    }

    internal void RefreshStatistics()
    {
        _board.Render(_statistics.Snapshot(DateTimeOffset.UtcNow), ChatTitle, MatchedChat, _sidebar.Count > 0 ? _sidebar : null);
        RefreshStatus();
    }

    private async Task ReadEvents()
    {
        if (_reading || _closed) return;
        _reading = true;
        try
        {
            var token = _stop.Token;
            do
            {
                var (snapshot, observedAt) = await Task.Run(async () =>
                {
                    var ready = _feed.ReadySources.Count;
                    await _feed.Read(token).ConfigureAwait(false);
                    var observedAt = Stopwatch.GetTimestamp();
                    var current = _feed.Loading && ready == _feed.ReadySources.Count
                        ? null : _statistics.Snapshot(DateTimeOffset.UtcNow, _feed.ReadySources);
                    return (current, observedAt);
                }, token);
                if (_closed) return;
                RefreshStatus();
                if (snapshot == null || !IsVisible) continue;
                if (_lookupTitles != null)
                {
                    var ids = snapshot.Select(c => c.Id).Where(id => !_titleTimes.TryGetValue(id, out var checkedAt) || DateTimeOffset.UtcNow - checkedAt > TimeSpan.FromSeconds(30)).Take(64).ToArray();
                    if (ids.Length > 0)
                    {
                        try
                        {
                            var titles = await Task.Run(() => _lookupTitles(ids), token);
                            var sidebar = _lookupSidebar == null ? null : await Task.Run(() => _lookupSidebar(ids), token);
                            foreach (var id in ids)
                            {
                                if (titles.TryGetValue(id, out var title)) _titles[id] = title;
                                else _titles.Remove(id);
                                if (sidebar != null && sidebar.TryGetValue(id, out var entry)) _sidebar[id] = entry;
                                else _sidebar.Remove(id);
                            }
                        }
                        catch (IOException) { /* Keep the previous exact-ID matches until the next read. */ }
                        catch (System.Text.Json.JsonException) { /* The desktop may be replacing the sidebar preference file. */ }
                        foreach (var id in ids) _titleTimes[id] = DateTimeOffset.UtcNow;
                    }
                }
                if (_closed) return;
                _board.Render(snapshot, ChatTitle, MatchedChat, _sidebar.Count > 0 ? _sidebar : null, observedAt);
                if (_raw.IsExpanded) RenderEvents();
            }
            while (_feed.Loading && IsVisible);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (IOException error) { _status.Text = Loc.F("diagnostics.viewer_error", error.Message); }
        finally { _reading = false; }
    }
}
