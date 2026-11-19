using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexAccountDesk;
using CodexSwitch.Diagnostics;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    private static IEnumerable<DependencyObject> All(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var descendant in All(child)) yield return descendant;
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--inspect-events")
        {
            var measured = new DiagnosticStatistics();
            foreach (var line in File.ReadLines(args[1])) { using var record = JsonDocument.Parse(line); measured.Apply("real-probe", record.RootElement); }
            var result = measured.Snapshot(DateTimeOffset.UtcNow).Select(chat => new { chat.Id, Turns = chat.Turns.Select(turn => new
            {
                turn.Id, turn.ElapsedMs, turn.Calls, turn.Errors, turn.Input, turn.Output, turn.FirstTextMs, turn.Shares, turn.Tools,
                turn.Model, turn.Effort, turn.Tier, turn.ReturnedModel, turn.ReturnedTier,
                Timeline = turn.Timeline.Select(segment => new { segment.StartMs, segment.EndMs, Phase = segment.Phase.ToString() }),
                Llm = turn.Llm.Select(call => new { call.Precise, call.Ambiguous, call.TtftMs, call.StreamTpsApprox, call.EndToEndTps, call.MaxGapMs, call.P95GapMs, call.Chunks, call.DeliveredTokens })
            }) });
            Console.WriteLine(JsonSerializer.Serialize(result)); return;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "RelayUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var checks = StatisticsContracts.Run(); Loc.SetLanguage("en");
            var nativeTitles = TitleContracts.Verify(root); checks += 2;
            string? launcher = null;
            var native = Path.Combine(root, "codex.exe"); var relay = Path.Combine(root, "relay.exe"); File.WriteAllText(native, "fixture"); File.WriteAllText(relay, "fixture");
            var state = new RelayInstallation(Path.Combine(root, "state"), relay, () => launcher, v => launcher = v);
            var application = new Application(); var owner = new Window();
            new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
            var statistics = StatisticsContracts.Fixture();
            var capturedSnapshot = statistics.Snapshot(DateTimeOffset.UtcNow);
            var openedChats = new List<string>();
            var window = new DiagnosticRelayWindow(owner, native, state, relayExecutable: relay, statistics: statistics, openChat: openedChats.Add);
            if (launcher != null) throw new Exception("Opening diagnostics changed the launch configuration."); checks++;
            Button ButtonNamed(string name) => All(window).OfType<Button>().Single(b => b.Name == name);
            var mode = All(window).OfType<MenuItem>().Single(item => item.Name == "DiagnosticModeSwitch");
            mode.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (!state.Enabled || !mode.IsChecked) throw new Exception("ON did not use the shared switch or activate the relay."); checks++;
            mode.ApplyTemplate();
            if (!mode.IsCheckable || mode.Template.FindName("track", mode) == null)
                throw new Exception("The relay mode is not using the model visibility template."); checks++;
            mode.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (launcher != null || state.RequestedMode != "off_requested" || mode.IsChecked) throw new Exception("OFF did not restore bundled native discovery."); checks++;
            if (!Equals(mode.Header, Loc.T("diagnostics.off"))) throw new Exception("The toggle label did not follow the requested mode."); checks++;
            var incremental = new DiagnosticStatistics(); var feed = new DiagnosticEventFeed(state, incremental);
            var feedEpoch = DateTimeOffset.UtcNow;
            string Packet(int ms, string method) => JsonSerializer.Serialize(new { at = feedEpoch.AddMilliseconds(ms), monotonicTicks = (long)ms * 10000, frequency = 10000000, data = new { kind = "native_protocol", thread = "feed-chat", turn = "feed-turn", method, status = "completed" } });
            var firstPacket = System.Text.Encoding.UTF8.GetBytes(Packet(0, "turn/started") + "\n");
            File.WriteAllBytes(state.Events, firstPacket[..(firstPacket.Length / 2)]); feed.Read(CancellationToken.None).GetAwaiter().GetResult();
            if (incremental.Snapshot(DateTimeOffset.UtcNow).Count != 0) throw new Exception("An incomplete log line was treated as a complete event."); checks++;
            using (var append = new FileStream(state.Events, FileMode.Append)) append.Write(firstPacket[(firstPacket.Length / 2)..]);
            feed.Read(CancellationToken.None).GetAwaiter().GetResult(); feed.Read(CancellationToken.None).GetAwaiter().GetResult();
            File.Move(state.Events, state.Events + ".previous"); File.WriteAllText(state.Events, Packet(10, "turn/completed") + "\n"); feed.Read(CancellationToken.None).GetAwaiter().GetResult();
            var captured = incremental.Snapshot(DateTimeOffset.UtcNow).Single().Turns;
            if (captured.Count != 1 || captured[0].Running || Math.Abs(captured[0].ElapsedMs - 10) > .1) throw new Exception("Incremental/rotated logs duplicated or lost a turn."); checks++;
            var recent = Path.Combine(Path.GetDirectoryName(state.Events)!, "events-new.jsonl");
            var history = Path.Combine(Path.GetDirectoryName(state.Events)!, "events-old.jsonl");
            using (var gap = File.Create(history)) gap.SetLength(5 * 1024 * 1024);
            File.SetLastWriteTimeUtc(history, DateTime.UtcNow.AddHours(-2));
            File.WriteAllText(recent, Packet(0, "turn/started") + "\n" + Packet(20, "turn/completed") + "\n");
            feed.Read(CancellationToken.None).GetAwaiter().GetResult();
            if (!feed.Loading || !feed.ReadySources.Contains(recent) || feed.ReadySources.Contains(history))
                throw new Exception("Cold loading waits on old captures ahead of recent turns."); checks++;
            feed.Read(CancellationToken.None).GetAwaiter().GetResult();
            if (feed.Loading || !feed.ReadySources.Contains(history)) throw new Exception("Incremental loading did not finish the backlog."); checks++;
            string Texts(DependencyObject node) => string.Join("\n", All(node).OfType<TextBlock>().Select(t => t.Text));
            if (window.Title != "Diagnostic Relay" || System.Text.RegularExpressions.Regex.IsMatch(Texts(window), "[가-힣]")) throw new Exception("English diagnostics contains fixed Korean text."); checks++;
            var sessions = All(window).OfType<ListBox>().Single(b => b.Name == "DiagnosticSessions");
            sessions.SelectedItem = sessions.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "chat-b"));
            var board = All(window).OfType<DiagnosticStatisticsView>().Single();
            if (board.SelectedChat != "chat-b") throw new Exception("Chat selection did not change the visible turns."); checks++;
            if (!Texts((ListBoxItem)sessions.SelectedItem).Contains("Chat title unavailable") ||
                ButtonNamed("DiagnosticOpenChat").IsEnabled) throw new Exception("An unmatched capture used its UUID as the title or enabled chat navigation."); checks++;
            sessions.SelectedItem = sessions.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "chat-a"));
            var selected = sessions.SelectedItem;
            var turn = All(board).OfType<Border>().First(b => b.Name == "DiagnosticTurnRow");
            board.Render(capturedSnapshot, id => nativeTitles.TryGetValue(id, out var title) ? title : Loc.T("diagnostics.stats.title_unavailable"), nativeTitles.ContainsKey);
            if (!Texts((ListBoxItem)selected).Contains("Saved Codex title")) throw new Exception("The inspector did not render the saved Codex title."); checks++;
            bool HoverContains(string badge, string value) => All(board).OfType<Border>()
                .Any(control => control.Name == badge && control.ToolTip is TextBlock tip && tip.Text.Contains(value, StringComparison.Ordinal));
            if (!HoverContains("DiagnosticModelBadge", "gpt-6.1-sol") || !HoverContains("DiagnosticEffortBadge", "high") ||
                !HoverContains("DiagnosticSpeedBadge", "Fast") || !HoverContains("DiagnosticSpeedBadge", "Response tier · default") ||
                DiagnosticStatisticsView.SpeedName("ultrafast") != "UltraFast" || DiagnosticStatisticsView.SpeedName(null) == "Standard")
                throw new Exception("Turn badges lost exact metadata or inferred an unrecorded speed."); checks++;
            if (!DiagnosticModelBadge.IsHuggingFace("https://router.huggingface.co/v1") ||
                DiagnosticModelBadge.IsHuggingFace("https://huggingface.co.example.invalid/v1") ||
                DiagnosticModelBadge.IsHuggingFace("https://example.invalid/huggingface.co"))
                throw new Exception("Hugging Face branding was assigned to an unrelated provider."); checks++;
            if (!ReferenceEquals(selected, sessions.SelectedItem) || !ReferenceEquals(turn, All(board).OfType<Border>().First(b => b.Name == "DiagnosticTurnRow"))) throw new Exception("A live refresh reset chat selection or turn rows."); checks++;
            if (DiagnosticStatisticsView.Duration(11900) != "11.9s" || DiagnosticStatisticsView.Duration(70000) != "1m 10s" ||
                DiagnosticStatisticsView.Duration(3670000) != "1h 1m 10s") throw new Exception("Elapsed duration formatting changed."); checks++;
            var ordering = new Dictionary<string, NativeHistoryThreads.SidebarEntry>
            {
                ["chat-a"] = new("project", "Native project", 0, int.MaxValue, 0, 300),
                ["chat-b"] = new("project", "Native project", 0, 0, 1, 100)
            };
            board.Render(capturedSnapshot, id => id, _ => true, ordering);
            if (!Equals(sessions.Items.OfType<ListBoxItem>().First(item => item.Tag != null).Tag, "chat-b") ||
                !ReferenceEquals(selected, sessions.SelectedItem) || !Texts(sessions).Contains("Native project"))
                throw new Exception("Sidebar grouping, pin ordering or stable selection failed."); checks++;
            var spinner = All((ListBoxItem)sessions.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "chat-b")))
                .OfType<System.Windows.Shapes.Ellipse>().Single(x => x.Name == "DiagnosticChatSpinner");
            if (spinner.Visibility != Visibility.Visible) throw new Exception("A live chat has no spinner."); checks++;
            sessions.SelectedItem = sessions.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "chat-b"));
            var turnSpinner = All(board).OfType<System.Windows.Shapes.Ellipse>().Single(x => x.Name == "DiagnosticTurnSpinner");
            if (turnSpinner.Visibility != Visibility.Visible || DockPanel.GetDock(turnSpinner) != Dock.Left || Texts(board).Contains("Running"))
                throw new Exception("The running turn must show a leading spinner without inline status text."); checks++;
            var liveItem = (ListBoxItem)sessions.SelectedItem;
            var stoppedLabel = Texts((ListBoxItem)selected);
            board.UpdateLiveElapsed(100);
            var firstTenth = Texts(liveItem);
            board.UpdateLiveElapsed(200);
            if (firstTenth == Texts(liveItem) || stoppedLabel != Texts((ListBoxItem)selected))
                throw new Exception("Tenths must update active time without refreshing inactive rows."); checks++;
            board.UpdateLiveElapsed(6000);
            var expiredLabel = Texts(liveItem);
            board.UpdateLiveElapsed(6100);
            if (expiredLabel != Texts(liveItem) || turnSpinner.Visibility != Visibility.Collapsed ||
                DiagnosticStatisticsView.Duration(70100, true) != "1m 10.1s")
                throw new Exception("Elapsed projection outlived source freshness or lost tenths after a minute."); checks++;
            sessions.SelectedItem = selected;
            board.Render(statistics.Snapshot(DateTimeOffset.UtcNow), id => id, id => id == "chat-a");
            All(board).OfType<Button>().Single(b => b.Name == "DiagnosticOpenChat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!openedChats.SequenceEqual(new[] { "chat-a" })) throw new Exception("Open in Codex did not retain the exact selected chat ID."); checks++;
            if (sessions.Items.OfType<ListBoxItem>().Any(item => item.Content is not Grid row || row.Height > 36)) throw new Exception("A chat occupies more than one compact row."); checks++;
            if (!All(turn).OfType<TextBlock>().Any(label => label.ToolTip is TextBlock tip && tip.Text.Length > 0) || All(board).OfType<Expander>().Any()) throw new Exception("Timing details must be concise hover content."); checks++;
            if (All(window).OfType<Expander>().Single(e => e.Name == "DiagnosticRawEvents").IsExpanded) throw new Exception("Raw logs should start collapsed."); checks++;
            window.Close();
            var modeless = new DiagnosticRelayWindow(owner, native, state, relayExecutable: relay, statistics: StatisticsContracts.Fixture()) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false };
            modeless.OpenModeless();
            if (!IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(owner).Handle) || !modeless.IsVisible || modeless.Owner != null) throw new Exception("The analyzer blocked Switch or retained an always-above owner relationship."); checks++;
            modeless.UpdateLayout();
            var settings = All(modeless).OfType<Expander>().Single(e => e.Name == "DiagnosticSetup");
            var disclosure = (ToggleButton)settings.Template.FindName("DisclosureHeader", settings);
            disclosure.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            if (!settings.IsExpanded || ((ContentPresenter)settings.Template.FindName("DisclosureContent", settings)).Visibility != Visibility.Visible) throw new Exception("The themed disclosure did not expand."); checks++;
            disclosure.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            if (settings.IsExpanded) throw new Exception("The themed disclosure did not collapse."); checks++;
            var timeline = All(modeless).OfType<DiagnosticStripeGauge>().Last(g => g.Height == 20);
            var painted = DiagnosticStripeGauge.OverlayFirstText(
                [new(0, 20, DiagnosticPhase.Model), new(20, 30, DiagnosticPhase.Tool), new(30, 100, DiagnosticPhase.Model)],
                [new(5, 25, DiagnosticPhase.FirstText), new(45, 60, DiagnosticPhase.FirstText)]);
            if (painted.Count(p => p.Phase == DiagnosticPhase.FirstText) != 2 ||
                !painted.Any(p => p is { StartMs: 20, EndMs: 30, Phase: DiagnosticPhase.Tool }) ||
                !painted.Any(p => p is { StartMs: 45, EndMs: 60, Phase: DiagnosticPhase.FirstText }))
                throw new Exception("First-text intervals did not preserve individual calls and tool timing."); checks++;
            var ttftDetail = timeline.DetailAt(timeline.ActualWidth * .08);
            if (ttftDetail == null || !ttftDetail.Contains("TTFT") || !ttftDetail.Contains("950 ms")) throw new Exception("The pointer did not find the measured first-text band."); checks++;
            var toolDetail = timeline.DetailAt(timeline.ActualWidth * .43);
            if (toolDetail == null || !toolDetail.StartsWith("Tools", StringComparison.Ordinal) || !toolDetail.Contains("commandExecution") || !toolDetail.Contains("mcpToolCall")) throw new Exception("The pointer did not find both active tools."); checks++;
            timeline.PointAt(timeline.ActualWidth * .43);
            if (!timeline.HoverOpen) throw new Exception("Gauge hover did not show the popup immediately."); checks++;
            timeline.PointAt(null);
            if (timeline.HoverOpen) throw new Exception("The gauge popup remained open after the pointer left."); checks++;
            modeless.Close();
            var images = new List<string>();
            foreach (var scenario in new[] { (Width:1000, Height:670, Dark:false), (Width:560, Height:540, Dark:false), (Width:1000, Height:670, Dark:true) })
            {
                var palette = new Burn0Theme(scenario.Dark);
                var preview = new DiagnosticRelayWindow(owner, native, state, relayExecutable: relay, theme: palette, statistics: StatisticsContracts.Fixture());
                var view = All(preview).OfType<DiagnosticStatisticsView>().Single();
                view.Render(StatisticsContracts.Fixture().Snapshot(DateTimeOffset.UtcNow), id => id switch
                {
                    "chat-a" => "Retry investigation", "chat-b" => "Tool execution", _ => "Completed response"
                }, _ => true, ordering);
                var list = All(preview).OfType<ListBox>().Single(b => b.Name == "DiagnosticSessions"); list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, "chat-a"));
                if (scenario.Width == 560) All(preview).OfType<Expander>().Single(e => e.Name == "DiagnosticSetup").IsExpanded = true;
                var surface = (Grid)preview.Content;
                surface.Measure(new Size(scenario.Width, scenario.Height)); surface.Arrange(new Rect(0, 0, scenario.Width, scenario.Height)); surface.UpdateLayout();
                if (surface.ActualWidth <= 0 || All(preview).OfType<DiagnosticStripeGauge>().Any(g => double.IsNaN(g.ActualWidth))) throw new Exception("Statistics layout failed.");
                var scale = All(view).OfType<DiagnosticStripeGauge>().Last(g => g.Height == 20);
                scale.PointAt(scale.ActualWidth * .59); surface.UpdateLayout();
                var image = new RenderTargetBitmap(scenario.Width, scenario.Height, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(palette.Color("canvas"), null, new Rect(0, 0, scenario.Width, scenario.Height));
                image.Render(background); image.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                var path = Path.Combine(root, $"statistics-{scenario.Width}-{scenario.Dark}.png"); using (var file = File.Create(path)) encoder.Save(file);
                images.Add(path); preview.Close(); checks++;
            }
            foreach (var language in new[] { "en", "ko", "ja", "zh-CN", "zh-TW", "es", "fr", "de", "pt-BR" })
            {
                Loc.SetLanguage(language);
                var localized = new DiagnosticRelayWindow(owner, native, state, relayExecutable: relay, statistics: StatisticsContracts.Fixture());
                var surface = (Grid)localized.Content; surface.Measure(new Size(780, 540)); surface.Arrange(new Rect(0, 0, 780, 540)); surface.UpdateLayout();
                if (localized.Title != Loc.T("diagnostics.title") || Texts(localized).Contains("diagnostics.")) throw new Exception("A diagnostic label did not resolve: " + language);
                localized.Close(); checks++;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = checks, scope = "Measured interval partitions, token deduplication, real session/turn controls, live refresh state, nine locales and narrow/light/dark component renders; fictional data", images }));
            owner.Close(); application.Shutdown();
        }
        catch { Directory.Delete(root, true); throw; }
    }
}
