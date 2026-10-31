using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexAccountDesk;

internal static class HistoryDiagnostics
{
    public static async Task VerifyAsync(DeskWindow owner, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>(); var failures = 0;
        void Check(string name, bool passed)
        {
            if (!passed) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        await Task.Delay(120);
        try
        {
            foreach (var dark in new[] { true, false })
            {
                var theme = dark ? "dark" : "light";
                var temporary = Path.Combine(Path.GetTempPath(), "mac-self-test-history-" + Guid.NewGuid().ToString("N"));
                var vault = new AccountVault(temporary);
                var home = Path.Combine(temporary, "absent-home");
                var service = new AccountService(vault, home);
                try
                {
                    foreach (var scenario in new[] { "overview", "minimum", "empty", "single", "multiple", "unavailable" })
                    {
                        var profile = HistorySamples.Create(scenario);
                        vault.Data.Accounts.Clear(); vault.Data.Accounts.Add(profile); vault.Data.TrendHours = 24;
                        owner.Preview("en", dark, new[] { profile });
                        var history = new HistoryWindow(service, profile.Key, new DeskTheme(dark, owner.Appearance)) { ShowInTaskbar = false };
                        try
                        {
                            if (scenario == "minimum") { history.Width = 500; history.Height = 300; }
                            history.Show(owner); await Ready();
                            Check(theme + "/" + scenario + "/English native window", history.IsVisible && Loc.Language == "en" && history.Title == "Demo · Usage history");
                            var ranges = history.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Name?.StartsWith("HistoryRange", StringComparison.Ordinal) == true).ToArray();
                            Check(theme + "/" + scenario + "/selected range", ranges.Length == 4 && ranges.Select(b => b.Name).OrderBy(n => n).SequenceEqual(new[] { "HistoryRange168", "HistoryRange24", "HistoryRange6", "HistoryRange720" }) && ranges.All(b => b.IsChecked == (b.Name == "HistoryRange24")));
                            var scroll = history.GetVisualDescendants().OfType<ScrollViewer>().First();
                            Check(theme + "/" + scenario + "/no horizontal overflow", scroll.Extent.Width <= scroll.Viewport.Width + 1);
                            if (scenario == "overview")
                            {
                                Check(theme + "/overview/no default overflow", scroll.Extent.Height <= scroll.Viewport.Height + 1);
                                Check(theme + "/overview/reset first explanation", history.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Reset first"));
                                var inactive = (ISolidColorBrush)ranges.Single(b => b.Name == "HistoryRange6").Background!;
                                var active = (ISolidColorBrush)ranges.Single(b => b.Name == "HistoryRange24").Background!;
                                var presented = (ISolidColorBrush)ranges.Single(b => b.Name == "HistoryRange24")
                                    .GetVisualDescendants().OfType<ContentPresenter>().First().Background!;
                                Check(theme + "/overview/selected range contrast", active.Color != inactive.Color && presented.Color == active.Color);
                                ranges.Single(b => b.Name == "HistoryRange6").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                Check(theme + "/overview/range change persists through normal vault save", vault.Data.TrendHours == 6 && File.Exists(vault.FilePath) &&
                                    history.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "HistoryRange6").IsChecked == true);
                                history.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "HistoryRange720").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                Check(theme + "/overview/30-day preset reaches the chart", vault.Data.TrendHours == 720 && history.GetVisualDescendants().OfType<HistoryChart>().Single().Hours == 720);
                                await NativeUiCapture.SaveAsync(history, Path.Combine(directory, "en-" + theme + "-history-30days-native.png"));
                                history.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HistoryCustomRange").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                var dates = history.GetVisualDescendants().OfType<CalendarDatePicker>().ToArray();
                                await NativeUiCapture.SaveAsync(history, Path.Combine(directory, "en-" + theme + "-history-date-editor-native.png"));
                                var today = UsagePresentation.KoreanTime(DateTimeOffset.UtcNow).Date;
                                dates.Single(p => p.Name == "HistoryFrom").SelectedDate = today.AddDays(-1);
                                dates.Single(p => p.Name == "HistoryThrough").SelectedDate = today.AddDays(-2);
                                history.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HistoryApplyRange").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                Check(theme + "/overview/reversed custom dates stay in the editor", history.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T("history.invalid_range")));
                                dates.Single(p => p.Name == "HistoryFrom").SelectedDate = today.AddDays(-3);
                                history.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HistoryApplyRange").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                var customRange = history.GetVisualDescendants().OfType<HistoryChart>().Single().Range;
                                Check(theme + "/overview/custom Korean dates reach the chart", customRange != null && UsagePresentation.KoreanTime(customRange.Start).Date == today.AddDays(-3) && UsagePresentation.KoreanTime(customRange.End).Date == today.AddDays(-1));
                                await NativeUiCapture.SaveAsync(history, Path.Combine(directory, "en-" + theme + "-history-custom-native.png"));
                                history.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "HistoryRange24").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                await Ready();
                                Check(theme + "/overview/preset clears custom bounds", history.GetVisualDescendants().OfType<HistoryChart>().Single().Range == null);
                            }
                            if (scenario == "minimum")
                            {
                                Check(theme + "/minimum/vertical overflow", scroll.Extent.Height > scroll.Viewport.Height);
                                scroll.Offset = new Vector(0, scroll.Extent.Height); await Ready();
                                Check(theme + "/minimum/footer remains reachable", scroll.Offset.Y > 0);
                                scroll.Offset = default; await Ready();
                            }
                            if (scenario == "multiple")
                            {
                                var quota = history.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HistoryQuota");
                                quota.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Ready();
                                var menu = quota.ContextMenu ?? throw new InvalidOperationException("Quota menu did not open.");
                                // Keep the original two quota choices strict. Credit history is
                                // a separate metric, verified by its own native evidence mode.
                                var items = menu.ItemsSource!.Cast<object>().OfType<MenuItem>().Where(item => item.Header as string != Loc.T("credits.history")).ToArray();
                                Check(theme + "/multiple/selected quota menu", menu.IsOpen && items.Length == 2 && items.Count(item => item.IsChecked) == 1);
                                await NativeUiCapture.SaveAsync(TopLevel.GetTopLevel(menu) ?? throw new InvalidOperationException("Quota popup has no native root."),
                                    Path.Combine(directory, "en-" + theme + "-history-quota-native.png"));
                                items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Ready();
                                Check(theme + "/multiple/quota selection updates chart", history.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Codex · Weekly ▾") &&
                                    history.GetVisualDescendants().OfType<HistoryChart>().Single().Points.Length == 289);
                            }
                            Check(theme + "/" + scenario + "/data availability", history.GetVisualDescendants().OfType<HistoryChart>().Any() == (scenario != "unavailable"));
                            await NativeUiCapture.SaveAsync(history, Path.Combine(directory, "en-" + theme + "-history-" + scenario + "-native.png"));
                        }
                        finally { history.Close(); }
                    }
                    Check(theme + "/isolated home remains absent", !Directory.Exists(home));
                }
                finally
                {
                    VaultProtection.ReleaseKeys();
                    MacKeychain.DeleteDiagnosticKey(temporary);
                    Directory.Delete(temporary, true);
                }
            }
        }
        catch (Exception error)
        {
            failures++; Console.Error.WriteLine("History verification failed: " + error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "history-report.json"), JsonSerializer.Serialize(new {
                schema = 1, version = typeof(Program).Assembly.GetName().Version!.ToString(3), os = "macOS",
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                nativeWindowCapture = true, rendering = "Software (native window)", language = "en", failures, checks,
                scope = "Actual app-owned history windows and quota popups, fictional accounts/history, disposable encrypted vault and cleaned diagnostic Keychain key. No real account/home, network or Codex lifecycle action."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Program.DiagnosticExitCode = failures == 0 ? 0 : 1;
            await owner.ExitAsync();
        }
    }

    private static async Task Ready()
    {
        await Task.Delay(120);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

}
