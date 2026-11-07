using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal static class HistoryBeforeCapture
{
    public static async Task RenderAsync(DeskWindow owner, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>(); var failures = 0;
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
                    var profile = HistorySamples.Create("overview");
                    vault.Data.Accounts.Add(profile); vault.Data.TrendHours = 24;
                    owner.Preview("en", dark, new[] { profile });
                    var history = new HistoryWindow(service, profile.Key, new DeskTheme(dark, owner.Appearance)) { ShowInTaskbar = false };
                    try
                    {
                        history.Show(owner); await Task.Delay(150);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        if (!history.IsVisible || Loc.Language != "en" || history.Title != Loc.T("추이 보기"))
                            throw new InvalidOperationException("The original English history window did not open.");
                        checks.Add(theme + "/actual original history window");
                        await NativeUiCapture.SaveAsync(history, Path.Combine(directory, "en-" + theme + "-history-native.png"));
                        if (Directory.Exists(home)) throw new InvalidOperationException("The absent diagnostic home was created.");
                        checks.Add(theme + "/no Codex home or authentication");
                    }
                    finally { history.Close(); }
                }
                finally
                {
                    VaultProtection.ReleaseKeys(); MacKeychain.DeleteDiagnosticKey(temporary);
                    Directory.Delete(temporary, true);
                }
            }
        }
        catch (Exception error)
        {
            failures++; Console.Error.WriteLine("Original history capture failed: " + error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "before-report.json"), JsonSerializer.Serialize(new {
                os = "macOS", architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = RuntimeInformation.FrameworkDescription, version = typeof(Program).Assembly.GetName().Version!.ToString(3),
                nativeWindowCapture = true, language = "en", failures, checks,
                scope = "Original main history renderer, capture-only host and shared fictional growth input. Actual app-owned NSWindows; no personal account, home, network or Codex action."
            }));
            Program.DiagnosticExitCode = failures == 0 ? 0 : 1;
            await owner.ExitAsync();
        }
    }
}
