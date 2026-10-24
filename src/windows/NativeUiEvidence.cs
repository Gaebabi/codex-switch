using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal static class NativeUiEvidence
{
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rectangle bounds);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codex-ui-evidence-" + Guid.NewGuid().ToString("N")));
        var vault = new AccountVault(Path.Combine(temporary, "data")); vault.Data.Accounts.AddRange(SyntheticDashboard.Accounts());
        // These fictional accounts also exercise real encrypted persistence. Keep the
        // parsed identity consistent instead of bypassing AccountVault's reload checks.
        foreach (var profile in vault.Data.Accounts)
        {
            profile.AuthJson = Diagnostics.FakeAuth(profile.Key, profile.Email, "native-ui-evidence-workspace");
            var identity = AuthDocument.Parse(profile.AuthJson);
            profile.Key = identity.Key; profile.AccountId = identity.AccountId;
        }
        var service = new AccountService(vault, Path.Combine(temporary, "home"));
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(service, new Program.Options { DataDirectory = vault.DirectoryPath, CodexHome = service.CodexHome,
            UiEvidenceDirectory = directory }) { ShowInTaskbar = false, Left = 20, Top = 20, WindowStartupLocation = WindowStartupLocation.Manual };
        var cases = new List<object>(); var exit = 0;
        ContextMenu? openedMenu = null;
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((sender, _) => openedMenu = (ContextMenu)sender));
        window.Loaded += async (_, _) =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                foreach (var language in new[] { "en", "ko" })
                foreach (var size in new[] { (800, 560), (500, 360) })
                {
                    window.PrepareUiEvidence(language, dark); window.Width = size.Item1; window.Height = size.Item2;
                    window.UpdateLayout(); await Task.Delay(300); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    var name = language + "-" + (dark ? "dark" : "light") + "-" + size.Item1 + "x" + size.Item2;
                    var version = Descendants<TextBlock>(window).Single(t => t.Name == "RunningVersionLabel");
                    var actions = Descendants<StackPanel>(window).Single(t => t.Name == "TitleBarActions");
                    Assert(version.IsVisible && version.Text == "v" + typeof(Program).Assembly.GetName().Version!.ToString(3), "Titlebar does not identify the running assembly version");
                    var versionBounds = version.TransformToAncestor(window).TransformBounds(new Rect(new System.Windows.Size(version.ActualWidth, version.ActualHeight)));
                    var actionBounds = actions.TransformToAncestor(window).TransformBounds(new Rect(new System.Windows.Size(actions.ActualWidth, actions.ActualHeight)));
                    Assert(versionBounds.Width > 0 && versionBounds.Height > 0 && versionBounds.Left >= 0 && versionBounds.Right <= window.ActualWidth && versionBounds.Top >= 0 && versionBounds.Bottom <= 40,
                        "Running version is clipped outside the titlebar");
                    Assert(!versionBounds.IntersectsWith(actionBounds), "Running version overlaps titlebar actions");
                    Capture(window, Path.Combine(directory, name + ".png")); cases.Add(new { name, passed = true, runningVersion = version.Text, versionWithinTitlebar = true, titlebarActionsSeparate = true });
                }
                foreach (var dark in new[] { true, false })
                {
                    var name = "en-" + (dark ? "dark" : "light");
                    window.PrepareUiEvidence("en", dark); window.Width = 800; window.Height = 560; window.UpdateLayout();
                    var language = Descendants<Button>(window).Single(b => b.Name == "LanguageButton");
                    language.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
                    var menu = openedMenu ?? throw new InvalidOperationException("Language dropdown did not open");
                    var scroll = Descendants<ScrollViewer>(menu).Single();
                    Assert(scroll.ScrollableHeight > 0, "Language dropdown has no reachable overflow");
                    var bar = Descendants<ScrollBar>(menu).First(b => b.Orientation == Orientation.Vertical && b.IsVisible);
                    Assert(ReferenceEquals(bar.Style, menu.FindResource(typeof(ScrollBar))), "Popup scrollbar does not inherit its theme");
                    scroll.ScrollToEnd(); await Task.Delay(80); Assert(scroll.VerticalOffset > 0, "Language dropdown cannot scroll");
                    scroll.ScrollToHome(); await Task.Delay(80);
                    var source = PresentationSource.FromVisual(menu) as HwndSource ?? throw new InvalidOperationException("Native popup handle unavailable");
                    Capture(source.Handle, Path.Combine(directory, name + "-language.png")); menu.IsOpen = false;
                    var detail = new AccountDetailWindow(window, _ => { }, () => Task.CompletedTask) { Height = 380, ShowInTaskbar = false };
                    try
                    {
                        detail.Refresh(service.Accounts[0], new Burn0Theme(dark), window.Resources, 24); detail.Show(); await Task.Delay(100);
                        var more = Descendants<Button>(detail).Single(b => b.Content as string == Loc.T("기타 정보") + " ▾");
                        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); detail.UpdateLayout(); await Task.Delay(100);
                        var history = Descendants<ScrollViewer>(detail).Single();
                        Assert(history.ScrollableHeight > 0, "Expanded history has no scrollable overflow");
                        history.ScrollToEnd(); await Task.Delay(80); Assert(history.VerticalOffset > 0, "History cannot scroll");
                        history.ScrollToHome(); await Task.Delay(80); Capture(detail, Path.Combine(directory, name + "-history.png"));
                    }
                    finally { detail.Close(); }
                    cases.Add(new { name = name + "-scroll-surfaces", passed = true });
                    VerifyTooltipLifecycle(window, directory, name);
                    cases.Add(new { name = name + "-tooltip-lifecycle", passed = true });
                    window.PrepareUiEvidence("en", dark); window.Width = 800; window.Height = 560; window.UpdateLayout();
                    var cpuPanel = Descendants<HardwareUsagePanel>(window).Single();
                    var at = DateTimeOffset.UtcNow;
                    foreach (var state in new[] { "setup", "denied", "available" })
                    {
                        var reading = state == "available" ? TemperatureReading.From(78.125, CodexSwitch.Sensors.CpuSensorProtocol.Source, "AMD CPU (Tctl)", at) :
                            TemperatureReading.Missing(state == "setup" ? TemperatureState.ProviderMissing : TemperatureState.AccessDenied,
                            CodexSwitch.Sensors.CpuSensorProtocol.Source, code: state == "setup" ? "thermal.cpu_setup_required" : "thermal.cpu_enable_required", at: at);
                        cpuPanel.Refresh(HardwareUsage.Pending with { CheckedAt = at, Cpu = new ResourceReading(28, Temperature: reading), Memory = new ResourceReading(42) }, new Burn0Theme(dark));
                        window.UpdateLayout(); await Task.Delay(100); Capture(window, Path.Combine(directory, name + "-cpu-" + state + ".png"));
                        Assert(Descendants<TextBlock>(cpuPanel).Any(t => t.Text == (state == "available" ? "78°C" : "—°C")), "CPU temperature state not rendered");
                        var setup = Descendants<Button>(cpuPanel).Single(b => b.Name == "CpuTemperatureSetupButton");
                        Assert(setup.IsVisible == (state != "available"), "CPU setup discovery does not match sensor capability");
                        Assert(System.Windows.Automation.AutomationProperties.GetName(setup) == Loc.T("thermal.cpu_enable"), "CPU setup accessible action missing");
                        cpuPanel.SetCpuTemperatureBusy(true); Assert(!setup.IsEnabled, "Repeated CPU authorization is possible while busy");
                        cpuPanel.SetCpuTemperatureBusy(false); Assert(setup.IsEnabled, "CPU action did not recover after completion/cancel");
                        if (state != "available")
                        {
                            cpuPanel.Refresh(HardwareUsage.Pending with { CheckedAt = at, Cpu = new ResourceReading(100, Temperature: reading), Memory = new ResourceReading(42) }, new Burn0Theme(dark));
                            window.Width = 500; window.Height = 360; window.UpdateLayout(); await Task.Delay(80);
                            var cell = (FrameworkElement)cpuPanel.Children[0];
                            var edge = setup.TransformToAncestor(cell).Transform(new System.Windows.Point(setup.ActualWidth, 0));
                            Assert(setup.ActualWidth >= 16 && setup.ActualHeight >= 16 && edge.X <= cell.ActualWidth + 0.5, "CPU setup action clipped at minimum size");
                            var percentage = Descendants<TextBlock>(cpuPanel).Single(t => t.Text == "100%");
                            var valueEdge = percentage.TransformToAncestor(cell).Transform(new System.Windows.Point(percentage.ActualWidth, 0));
                            Assert(valueEdge.X + 2 <= setup.TransformToAncestor(cell).Transform(new System.Windows.Point()).X, "CPU setup overlaps full utilization");
                            Capture(window, Path.Combine(directory, name + "-cpu-" + state + "-compact.png"));
                            window.Width = 800; window.Height = 560; window.UpdateLayout();
                        }
                    }
                    var cpuMenu = ((FrameworkElement)cpuPanel.Children[0]).ContextMenu!; cpuMenu.PlacementTarget = cpuPanel.Children[0] as UIElement;
                    cpuMenu.IsOpen = true; await Task.Delay(100);
                    Assert(cpuMenu.Items.OfType<MenuItem>().Single().Header as string == Loc.T("thermal.cpu_enable"), "CPU action missing");
                    var cpuSource = PresentationSource.FromVisual(cpuMenu) as HwndSource ?? throw new InvalidOperationException("Native CPU menu unavailable");
                    Capture(cpuSource.Handle, Path.Combine(directory, name + "-cpu-menu.png")); cpuMenu.IsOpen = false;
                    cases.Add(new { name = name + "-cpu-confirmation", passed = true, scope = "Fictional sensor states in real app; not physical CPU acceptance" });
                    var diskPanel = Descendants<DriveUsagePanel>(window).Single();
                    foreach (var diskState in new[] { "normal", "pressure", "unknown", "compact" })
                    {
                        window.Width = diskState == "compact" ? 500 : 800; window.Height = diskState == "compact" ? 360 : 560;
                        var ioAt = DateTimeOffset.UtcNow; var history = new DiskIoHistory(); DiskIoReading io = DiskIoReading.Missing(DiskIoState.Pending);
                        for (var seconds = 0; seconds <= 60; seconds += 2)
                            io = history.Observe(new(ioAt.AddSeconds(seconds - 60), DiskIoState.Available, diskState == "unknown" ? DiskIoProfile.Unknown : DiskIoProfile.NoSeekPenalty,
                                new(240 * 1048576, diskState == "pressure" ? 15 : 2), new(96 * 1048576, diskState == "pressure" ? 25 : 3), 90));
                        if (diskState == "unknown") io = DiskIoReading.Missing(DiskIoState.Unavailable);
                        var secondary = new DiskIoHistory().Observe(new(ioAt, DiskIoState.Available, DiskIoProfile.SeekPenalty, new(16 * 1048576, 8), new(0, null), 20));
                        diskPanel.Refresh(new[] { new DriveReading("C:\\", "System", 80L * 1073741824, 250L * 1073741824,
                            TemperatureReading.From(48, "Fictional disk sensor", "", ioAt), io),
                            new DriveReading("D:\\", "Data", 600L * 1073741824, 1000L * 1073741824,
                            TemperatureReading.From(55, "Fictional disk sensor", "", ioAt), diskState == "unknown" ? io : secondary) }, new Burn0Theme(dark));
                        window.UpdateLayout(); await Task.Delay(100);
                        var labels = Descendants<FrameworkElement>(diskPanel).Select(System.Windows.Automation.AutomationProperties.GetName).ToArray();
                        Assert(labels.Any(t => t.StartsWith(Loc.T("io.read"), StringComparison.Ordinal)) && labels.Any(t => t.StartsWith(Loc.T("io.write"), StringComparison.Ordinal)), "Disk bars have no accessible direction/rate names");
                        Assert(diskPanel.ActualHeight == 24, "Disk throughput changed footer height");
                        Assert(diskPanel.Fits, "Disk throughput obscures the disk text/temperature at this size");
                        Capture(window, Path.Combine(directory, name + "-disk-" + diskState + ".png"));
                        if (diskState == "pressure")
                        {
                            var owner = Descendants<FrameworkElement>(diskPanel).First(element => element.ToolTip is ResourceToolTip);
                            var tip = (ToolTip)owner.ToolTip; tip.PlacementTarget = owner; tip.IsOpen = true;
                            await Task.Delay(150);
                            var popup = PresentationSource.FromVisual(tip) as HwndSource ?? throw new InvalidOperationException("Disk tooltip has no native popup");
                            Capture(popup.Handle, Path.Combine(directory, name + "-disk-tooltip.png")); tip.IsOpen = false;
                        }
                    }
                    window.Width = 800; window.Height = 560;
                    cases.Add(new { name = name + "-disk-confirmation", passed = true, scope = "Fictional disk throughput/profile/latency in real native app; not manufacturer rating or physical benchmark" });
                }
                await AccountWarningEvidence.CaptureAsync(window, service, directory);
                await AccountAutomationEvidence.CaptureAsync(window, service, directory);
            }
            catch (Exception error)
            {
                var cause = error.GetBaseException(); exit = 1;
                cases.Add(new { name = "native-window", passed = false, error = cause.GetType().Name, message = cause.Message, stack = cause.StackTrace });
                Console.Error.WriteLine("Isolated UI verification: " + cause.Message);
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(new { schema = 1, os = "Windows",
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                    appVersion = typeof(Program).Assembly.GetName().Version!.ToString(3), failures = exit, cases,
                    scope = "Published app/native HWND captures and labeled WPF tooltip component renders; synthetic dashboard only. No network, credential, Codex home/process or personal screen access." }));
                window.Close(); app.Shutdown();
            }
        };
        app.Run(window);
        var target = Path.GetFullPath(temporary);
        if (Path.GetDirectoryName(target) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
            !Path.GetFileName(target).StartsWith("codex-ui-evidence-", StringComparison.Ordinal)) throw new IOException("Unexpected evidence cleanup target");
        Directory.Delete(target, true); return exit;
    }
    private static void VerifyTooltipLifecycle(MainWindow window, string directory, string name)
    {
        var serviceType = typeof(ToolTip).Assembly.GetType("System.Windows.Controls.PopupControlService")!;
        var service = serviceType.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var show = serviceType.GetMethod("ShowToolTip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dismiss = serviceType.GetMethod("DismissToolTips", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var current = serviceType.GetProperty("CurrentToolTip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ToolTip Open(FrameworkElement owner)
        {
            dismiss.Invoke(service, null); show.Invoke(service, new object[] { owner, false });
            var tip = (ToolTip)current.GetValue(service)!; tip.ApplyTemplate(); return tip;
        }
        try
        {
            var card = Descendants<AccountTankCard>(window).First();
            var owner = Descendants<Grid>(card).First(grid => grid.ToolTip is string && grid.ActualWidth > 100);
            var attached = Open(owner);
            Assert(attached.Template.FindName("tipSurface", attached) is Border, "Attached tooltip lost custom template");
            dismiss.Invoke(service, null);
            typeof(MainWindow).GetMethod("RenderAccounts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            window.UpdateLayout();
            Assert(!owner.IsDescendantOf(window), "Tooltip owner was not replaced by account refresh");
            var detached = Open(owner);
            Assert(ReferenceEquals(detached.Style, Application.Current.Resources[typeof(ToolTip)]), "Delayed tooltip fell outside the application theme");
            Assert(detached.Template.FindName("tipSurface", detached) is Border, "Detached owner reverted to the system tooltip template");
            CaptureTooltip(detached, Path.Combine(directory, name + "-tooltip.png"));
            dismiss.Invoke(service, null);
            var theme = (Burn0Theme)typeof(MainWindow).GetField("_theme", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var panel = Descendants<HardwareUsagePanel>(window).Single();
            var drives = Descendants<DriveUsagePanel>(window).Single();
            const long gib = 1073741824;
            drives.Refresh(new[] { new DriveReading("C:\\", "Synthetic disk", 80 * gib, 120 * gib) }, theme);
            var owners = panel.Children.OfType<FrameworkElement>().Concat(Descendants<FrameworkElement>(drives).Where(element => element.ToolTip is ResourceToolTip)).ToArray();
            Assert(owners.Length == 5, "Expected four resource gauges and one synthetic disk gauge");
            foreach (var cell in owners)
            {
                var resource = Open(cell);
                for (var update = 0; update < 4; update++)
                {
                    ToolTipTheme.Resource(cell, "CPU", update + "%", "Synthetic refresh observation", theme, "text");
                    Assert(ReferenceEquals(cell.ToolTip, resource) && resource.Template.FindName("tipSurface", resource) is Border,
                        "Gauge refresh replaced or unstyled the active tooltip");
                }
                dismiss.Invoke(service, null);
            }
        }
        finally { dismiss.Invoke(service, null); }
    }
    private static void CaptureTooltip(ToolTip tip, string path)
    {
        tip.Measure(new System.Windows.Size(340, 900)); tip.Arrange(new Rect(tip.DesiredSize)); tip.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tip.ActualWidth), (int)Math.Ceiling(tip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(tip); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Capture(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Capture(handle, path);
    }
    private static void Capture(IntPtr handle, string path)
    {
        GetWindowThreadProcessId(handle, out var process);
        if (process != Environment.ProcessId || !GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Capture requires this app's own window");
        var width = bounds.Right - bounds.Left; var height = bounds.Bottom - bounds.Top;
        if (width < 160 || height < 25 || width > 4000 || height > 3000) throw new InvalidOperationException("Invalid native window bounds");
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Black); var context = graphics.GetHdc();
            try { if (!PrintWindow(handle, context, 2)) throw new IOException("Native window capture failed"); }
            finally { graphics.ReleaseHdc(context); }
        }
        bitmap.Save(path, ImageFormat.Png);
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
