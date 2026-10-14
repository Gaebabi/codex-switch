using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexAccountDesk;

internal static class MacDiagnostics
{
    private static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    public static void StartWindowProbe(DeskWindow window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var initial = window.Position; var current = initial; var moves = 0;
        void Write() => File.WriteAllText(path, JsonSerializer.Serialize(new { initial, current, moves,
            width = window.ClientSize.Width, height = window.ClientSize.Height, language = Loc.Language, menuBar = window.MenuBarProbe,
            scope = "Synthetic native window movement only; no account, Keychain or Codex home access." }, new JsonSerializerOptions { WriteIndented = true }));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => Write(); timer.Start();
        window.PositionChanged += (_, _) => { current = window.Position; moves++; Write(); };
        window.SizeChanged += (_, _) => Write(); window.Closed += (_, _) => { timer.Stop(); Write(); }; Write();
    }
    public static async Task<int> VerifyAsync(string reportPath)
    {
        var directory = Path.Combine(Path.GetDirectoryName(reportPath)!, "mac-self-test-" + Guid.NewGuid().ToString("N"));
        var checks = new List<object>(); var failures = 0;
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); checks.Add(new { name, passed = true }); }
            catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.Message }); }
        }
        Task Test(Action action) { action(); return Task.CompletedTask; }
        try
        {
            await Check("Korean and English catalog consistency", () => Test(Loc.ValidateCatalogs));
            await Check("POSIX directory and atomic replacement", () => Test(() =>
            {
                SecureFiles.CreatePrivateDirectory(directory); var path = Path.Combine(directory, "private.txt");
                SecureFiles.AtomicWriteText(path, "first"); SecureFiles.AtomicWriteText(path, "한국어 replacement");
                Assert(File.ReadAllText(path) == "한국어 replacement", "Atomic replacement lost text.");
                if (OperatingSystem.IsMacOS())
                    Assert((int)File.GetUnixFileMode(directory) == 448 && (int)File.GetUnixFileMode(path) == 384, "Private modes are not 0700/0600.");
                else throw new PlatformNotSupportedException("These checks require macOS.");
            }));
            await Check("Keychain vault round trip after key cache release", () => Test(() =>
            {
                var vault = new AccountVault(directory); vault.Data.Language = "en"; vault.Data.Theme = "light"; vault.Save();
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(vault.FilePath)).Contains("Language", StringComparison.Ordinal), "Vault contains plaintext.");
                VaultProtection.ReleaseKeys(); var loaded = new AccountVault(directory);
                Assert(loaded.Data.Language == "en" && loaded.Data.Theme == "light", "Vault preferences were not restored from Keychain.");
            }));
            await Check("Authenticated vault rejects tampering", () => Test(() =>
            {
                var bytes = File.ReadAllBytes(Path.Combine(directory, "accounts.vault")); bytes[^1] ^= 1;
                try { VaultProtection.Unprotect(bytes, directory); throw new InvalidOperationException("Tampered vault was accepted."); }
                catch (CryptographicException) { }
            }));
            await Check("Missing Keychain key does not create a replacement", () => Test(() =>
            {
                try { MacKeychain.ReadOrCreateKey("mac-diagnostic-missing-" + Guid.NewGuid().ToString("N"), false); throw new InvalidOperationException("Missing key was accepted."); }
                catch (CryptographicException) { }
            }));
            await Check("Symlink authentication target is preserved", () => Test(() =>
            {
                var original = Path.Combine(directory, "original.txt"); File.WriteAllText(original, "original");
                var link = Path.Combine(directory, "linked.txt"); File.CreateSymbolicLink(link, original);
                try { SecureFiles.AtomicWriteText(link, "changed"); throw new InvalidOperationException("Linked target was replaced."); }
                catch (IOException) { }
                Assert(File.ReadAllText(original) == "original", "Symlink target changed.");
            }));
            await Check("Unix socket peer UID verification", async () =>
            {
                var path = Path.Combine("/tmp", "cad-" + Guid.NewGuid().ToString("N") + ".sock");
                try
                {
                    using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    server.Bind(new UnixDomainSocketEndPoint(path)); server.Listen(1);
                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await client.ConnectAsync(new UnixDomainSocketEndPoint(path)); using var peer = await server.AcceptAsync();
                    MacDesktop.VerifyPeer(client); MacDesktop.VerifyPeer(peer);
                }
                finally { File.Delete(path); }
            });
            await Check("Installed app bundle and CLI discovery without launch", async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var installation = await MacDesktop.FindAsync(timeout.Token);
                Assert(File.Exists(installation.CliPath) && File.Exists(installation.DesktopPath), "Codex desktop and CLI were not found.");
                Assert(MacDesktop.BundleForExecutable(installation.DesktopPath).EndsWith(".app", StringComparison.Ordinal), "Invalid bundle path.");
                _ = MacDesktop.IsRunning(installation.DesktopPath);
            });
            await Check("Native Mach and IOKit sampling", async () =>
            {
                using var sampler = new MacResourceSampler(); var first = sampler.Sample(); await Task.Delay(250); var next = sampler.Sample();
                Assert(first.Cpu.Percent == null && next.Cpu.Percent is >= 0 and <= 100, "CPU tick sampling failed.");
                Assert(next.Memory.Percent is >= 0 and <= 100 && next.Memory.TotalBytes > 0, "Physical memory sampling failed.");
                Assert(next.Gpu.Percent == null || next.Gpu.Percent is >= 0 and <= 100, "Invalid GPU sample.");
                if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                    Assert(next.Vram.Percent == null, "Unified memory was labeled dedicated VRAM.");
            });
            await Check("Mounted root volume sampling", async () =>
            {
                await using var monitor = new DriveMonitor(); monitor.Configure(new[] { new DrivePreference { Root = "/", Label = "Macintosh HD" } }); monitor.Start();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (monitor.Current.Length == 0) await Task.Delay(20, timeout.Token);
                Assert(monitor.Current[0].Free >= 0 && monitor.Current[0].Total > 0, "Root volume capacity unavailable.");
            });
        }
        finally
        {
            VaultProtection.ReleaseKeys();
            await Check("Disposable Keychain item cleanup", () => Test(() => MacKeychain.DeleteDiagnosticKey(directory)));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = "1.25.0", failures, checks,
            scope = "Disposable vault/socket fixtures, installed bundle discovery and OS resources. No real account access, Codex launch, shutdown, login or switch." }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
    public static AccountProfile[] Profiles(int count)
    {
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, count).Select(i => new AccountProfile
        {
            Key = "synthetic-" + i, Alias = i == 0 ? "MacBook · 기본 계정" : "Demo " + (i + 1), Email = "demo@example.invalid",
            Plan = i % 2 == 0 ? "pro" : "plus", IsCurrent = i == 0, SubscriptionActiveUntil = now.AddDays(i * 4),
            Usage = i == 4 ? null : new UsageSnapshot { CheckedAt = now, AvailableResets = i, Buckets = new() { new UsageBucket
            { Id = "codex", Label = "Codex", CreditBalance = i % 4 == 0 ? "0" : i % 4 == 1 ? "12345.67" : null, UnlimitedCredits = i % 4 == 2,
                Primary = new UsageWindow { UsedPercent = new[] { 27.4, 91.2, 100.0, 0.0 }[i % 4], Minutes = 10080, ResetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } }
        }).ToArray();
    }
    public static async Task RenderAsync(DeskWindow window, string directory)
    {
        var checks = new List<object>(); var failures = 0;
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var language in new[] { "ko", "en" })
            foreach (var dark in new[] { true, false })
            foreach (var size in new[] { new Size(500, 360), new Size(600, 420), new Size(800, 600) })
            {
                var name = language + "-" + (dark ? "dark" : "light") + "-" + size.Width + "x" + size.Height;
                window.Width = size.Width; window.Height = size.Height; window.Preview(language, dark, Profiles(4));
                await Task.Delay(120); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                try
                {
                    Assert(window.Cards.Count == 4 && window.Cards.All(c => c.Bounds.Height >= 118), "Cards are too short.");
                    foreach (var card in window.Cards)
                    foreach (var text in card.GetVisualDescendants().OfType<TextBlock>())
                    {
                        if (!text.IsVisible) continue; var position = text.TranslatePoint(new Point(), card);
                        Assert(position.HasValue && position.Value.Y >= -1 && position.Value.Y + text.Bounds.Height <= card.Bounds.Height + 1, "Card text is vertically clipped: " + text.Text);
                    }
                    Assert(window.AccountScroll.Extent.Height <= window.AccountScroll.Viewport.Height + 1, "Four cards unexpectedly require scrolling.");
                    var menuBar = window.MenuBarProbe;
                    Assert(menuBar is { Visible: true, Template: true, MenuItems: 5 } && menuBar.Title.Trim() == "72.6%", "Native menu bar lost its symbol, percentage or actions after rebuilding the view.");
                    foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible))
                    {
                        var content = button.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBlock or DeskButtonIcon)
                            ?? throw new InvalidOperationException("Button content was not rendered.");
                        var position = content.TranslatePoint(new Point(), button);
                        Assert(position.HasValue && Math.Abs(position.Value.X + content.Bounds.Width / 2 - button.Bounds.Width / 2) <= 1 &&
                            Math.Abs(position.Value.Y + content.Bounds.Height / 2 - button.Bounds.Height / 2) <= 1, "Button content is not centered: " + button.Content);
                    }
                    checks.Add(new { name, passed = true });
                }
                catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.Message }); }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width * 2, (int)window.ClientSize.Height * 2), new Vector(192, 192));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"));
                if (Program.NativeUiEvidence) await NativeUiCapture.SaveAsync(window, Path.Combine(directory, name + "-native.png"));
            }
            foreach (var count in new[] { 0, 1, 8 })
            {
                window.Width = 500; window.Height = 360; window.Preview("en", true, Profiles(count)); await Task.Delay(120);
                try
                {
                    Assert(window.Cards.Count == count, "Account count changed.");
                    if (count == 8)
                    {
                        Assert(window.AccountScroll.Extent.Height > window.AccountScroll.Viewport.Height, "Additional accounts cannot scroll.");
                        window.AccountScroll.Offset = new Vector(0, window.AccountScroll.Extent.Height); await Task.Delay(80);
                        Assert(window.AccountScroll.Offset.Y > 0, "Scroll position did not change.");
                    }
                    checks.Add(new { name = "account-count-" + count, passed = true });
                }
                catch (Exception error) { failures++; checks.Add(new { name = "account-count-" + count, passed = false, error = error.Message }); }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width * 2, (int)window.ClientSize.Height * 2), new Vector(192, 192));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, "accounts-" + count + ".png"));
            }
            foreach (var (name, used, current, expected) in new[] {
                ("empty", (double?)100, true, "0%"), ("full", (double?)0, true, "100%"),
                ("unavailable", (double?)null, true, "—"), ("inactive-only", (double?)25, false, "—") })
            {
                var profiles = Profiles(1); profiles[0].IsCurrent = current;
                if (used.HasValue) profiles[0].Usage!.Buckets[0].Primary!.UsedPercent = used.Value;
                else profiles[0].Usage = null;
                window.Preview("ko", true, profiles); await Task.Delay(80);
                var passed = window.MenuBarProbe is { Visible: true, Template: true, MenuItems: 5 } state && state.Title.Trim() == expected;
                if (!passed) failures++; checks.Add(new { name = "menu-bar-" + name, passed });
            }
        }
        catch (Exception error) { failures++; checks.Add(new { name = "render", passed = false, error = error.Message }); }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "render-report.json"), JsonSerializer.Serialize(new { schema = 1, version = typeof(Program).Assembly.GetName().Version!.ToString(3),
                os = "macOS", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, nativeWindowCapture = Program.NativeUiEvidence, failures, checks,
                scope = "Synthetic native Avalonia window renders; no account, Keychain, Codex home or network access." }, new JsonSerializerOptions { WriteIndented = true }));
            Program.DiagnosticExitCode = failures == 0 ? 0 : 1; await window.ExitAsync();
        }
    }
}
