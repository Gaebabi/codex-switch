using CodexAccountDesk;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ShowcaseEvidence
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long GiB = 1073741824;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static void Call(object value, string name) => value.GetType().GetMethod(name, Private)!.Invoke(value, null);

    internal static int Run(string output)
    {
        if (!Path.IsPathFullyQualified(output)) throw new ArgumentException("Provide an absolute showcase output directory.");
        Directory.CreateDirectory(output);
        var temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "CodexSwitchShowcase", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var now = DateTimeOffset.UtcNow;
        var files = new List<object>();
        try
        {
            foreach (var language in new[] { "en", "ko" })
            foreach (var dark in new[] { false, true })
            {
                Loc.SetLanguage(language);
                var data = Path.Combine(temporary, language + (dark ? "-dark" : "-light"));
                var vault = new AccountVault(data);
                vault.Data.Theme = dark ? "dark" : "light"; vault.Data.Language = language;
                vault.Data.Accounts.AddRange(Profiles(now));
                var provider = Provider();
                new ModelProviderStore(data).Update(settings =>
                {
                    settings.Providers.Add(provider);
                    settings.DesktopBackendEnabled = true;
                    settings.NativeContext = new(262144, 196608);
                });
                var service = new AccountService(vault, Path.Combine(temporary, "absent-home"));
                var theme = new Burn0Theme(dark);
                var owner = new MainWindow(service, new Program.Options
                {
                    DataDirectory = data, CodexHome = service.CodexHome,
                    UiEvidenceDirectory = output, ReportPath = Path.Combine(temporary, "unused.json")
                }) { Width = 900, Height = 600, Left = -12000, Top = -12000 };
                try
                {
                    new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
                    typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, vault.Data.Accounts[0].Key);
                    typeof(MainWindow).GetField("_ready", Private)!.SetValue(owner, true);
                    Call(owner, "RenderAccounts"); Call(owner, "UpdateControls");
                    Field<SessionUsagePanel>(owner, "_sessionCount").Refresh(new SessionActivity(true, 7, 0, 7, Queued: 3, Goals: 2), theme);
                    var history = Enumerable.Range(0, 61).Select(i => new HardwareObservation(now.AddSeconds((i - 60) * 2),
                        28 + 9 * Math.Sin(i * .31), 57 + 4 * Math.Sin(i * .16), 46 + 16 * Math.Sin(i * .21), 64 + 5 * Math.Sin(i * .12))).ToArray();
                    TemperatureReading Temperature(double value) => TemperatureReading.From(value, "Fictional showcase sensor", "Demo", now);
                    Field<HardwareUsagePanel>(owner, "_hardwarePanel").Refresh(new(now,
                        new(28, Temperature: Temperature(61)), new(57, Temperature: Temperature(42)),
                        new(46, Temperature: Temperature(68)), new(64, Temperature: Temperature(62)), Array.Empty<GpuResource>(), 0, history), theme);
                    var drives = new[]
                    {
                        new DriveReading(@"C:\", "System", 156 * GiB, 512 * GiB, Temperature(41)),
                        new DriveReading(@"D:\", "Projects", 318 * GiB, 1024 * GiB, Temperature(46), Folders: new[]
                        {
                            new FolderReading(@"D:\Projects", 0, 312 * GiB, FolderState.Available),
                            new FolderReading(@"D:\Models", 1, 188 * GiB, FolderState.Available),
                            new FolderReading(@"D:\Media", 2, 96 * GiB, FolderState.Available)
                        }),
                        new DriveReading(@"W:\", "Builds", 1240 * GiB, 2048 * GiB, Temperature(38), Folders: new[]
                        {
                            new FolderReading(@"W:\Builds", 3, 420 * GiB, FolderState.Available),
                            new FolderReading(@"W:\Caches", 4, 260 * GiB, FolderState.Available)
                        })
                    };
                    Field<DriveUsagePanel>(owner, "_drivePanel").Refresh(drives, theme);
                    var root = (Grid)owner.Content;
                    var titlebar = (Grid)root.Children[0];
                    var demo = new TextBlock { Text = "DEMO", FontSize = 10, Foreground = theme.Color("muted"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(demo, 1); titlebar.Children.Add(demo);
                    Capture(owner, "dashboard", 900, 600, () =>
                    {
                        Call(owner, "LayoutCards");
                        foreach (var card in Field<List<AccountTankCard>>(owner, "_tankCards")) card.Update(now);
                    });
                    var draft = ProviderEditorDraft.From(provider); draft.Step = 1;
                    service.ModelProviders.RememberEditorDraft(provider.Id, draft);
                    var models = new ModelProvidersWindow(owner, theme, service, _ => { }, _ => { });
                    Capture(models, "models", 620, 660); models.Close();
                    var detail = new AccountDetailWindow(owner, _ => { }, () => Task.CompletedTask);
                    var historyProfile = vault.Data.Accounts[3];
                    detail.Refresh(historyProfile, theme, owner.Resources, 168);
                    Capture(detail, "history", 620, 660); detail.Close();
                    var credits = new AccountDetailWindow(owner, _ => { }, () => Task.CompletedTask);
                    typeof(AccountDetailWindow).GetField("_selectedQuota", Private)!.SetValue(credits, CreditPresentation.Key(historyProfile.CreditHistory[0]));
                    credits.Refresh(historyProfile, theme, owner.Resources, 24);
                    Capture(credits, "credits", 620, 660); credits.Close();
                    var preferences = new[]
                    {
                        new DrivePreference { Root = @"D:\", Label = "Projects", Folders = new[]
                        {
                            new FolderPreference { Path = @"D:\Projects", Color = 0 },
                            new FolderPreference { Path = @"D:\Models", Color = 1 },
                            new FolderPreference { Path = @"D:\Media", Color = 2 }
                        } },
                        new DrivePreference { Root = @"W:\", Label = "Builds", Folders = new[]
                        {
                            new FolderPreference { Path = @"W:\Builds", Color = 3 },
                            new FolderPreference { Path = @"W:\Caches", Color = 4 }
                        } },
                        new DrivePreference { Root = @"C:\", Label = "System" }
                    };
                    var folders = new DriveSettingsWindow(owner, theme, preferences);
                    Capture(folders, "folders", 620, 660); folders.Close();
                }
                finally { owner.Close(); service.StopAsync().GetAwaiter().GetResult(); }

                void Capture(Window window, string scene, int width, int height, Action? afterLayout = null)
                {
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    root.Resources = window.Resources;
                    TextElement.SetFontFamily(root, window.FontFamily); TextElement.SetForeground(root, window.Foreground);
                    var canvas = new Grid { Width = width, Height = height, Background = window.Background, UseLayoutRounding = true };
                    canvas.Children.Add(root);
                    void Layout() { canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout(); }
                    Layout(); afterLayout?.Invoke(); Layout();
                    var frame = new DispatcherFrame();
                    canvas.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame); Layout();
                    var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(canvas);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    var name = scene + "-" + language + "-" + (dark ? "dark" : "light") + ".png";
                    using (var file = File.Create(Path.Combine(output, name))) encoder.Save(file);
                    files.Add(new { file = name, width, height, scene, language, dark });
                }
            }
            File.WriteAllText(Path.Combine(output, "showcase.json"), JsonSerializer.Serialize(new
            {
                version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3), capturedAtUtc = now,
                scope = "Released production Windows controls rendered offscreen with fictional data; no native OS window, actual Codex picker or provider/account acceptance.",
                accounts = 6, models = 5, usagePointsPerAccount = 2017, hardwarePoints = 61, trackedFolders = 5,
                liveAccountAccess = false, nativeCodexStarted = false, networkRequests = false, files
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { app.Shutdown(); Directory.Delete(temporary, true); }
    }

    private static AccountProfile[] Profiles(DateTimeOffset now)
    {
        var names = new[] { "Build", "Design", "Explore", "Personal", "Research", "Spare" };
        var plans = new[] { "promax", "pro", "prolite", "plus", "pro", "free" };
        var used = new[] { 23.6, 45.2, 68.1, 87.4, 8.8, 100.0 };
        return names.Select((name, index) =>
        {
            var deadline = now.AddHours(36 + index * 19).ToUnixTimeSeconds();
            var profile = new AccountProfile { Key = "showcase-" + index, Alias = name, Email = name.ToLowerInvariant() + "@example.invalid",
                AccountId = "showcase-" + index, Plan = plans[index], IsCurrent = index == 0,
                SubscriptionActiveUntil = index == 5 ? null : now.AddDays(22 - index * 4),
                Usage = new() { CheckedAt = now, OrdinaryUsageAllowed = index != 5, AvailableResets = index % 3,
                    Buckets = new() { new() { Id = "codex", Label = "Codex", Plan = plans[index], CreditBalance = (index == 5 ? 0 : 42500 - index * 7250).ToString(CultureInfo.InvariantCulture),
                        Secondary = new() { UsedPercent = used[index], Minutes = 10080, ResetsAt = deadline } } } } };
            var track = new UsageTrack { BucketId = "codex", Slot = "secondary", Minutes = 10080 };
            var weights = Enumerable.Range(0, 2016).Select(i => .15 + Math.Pow(Math.Max(0, Math.Sin(i / 144.0 * Math.PI + index)), 2)).ToArray();
            var total = weights.Sum(); var accumulated = 0.0;
            for (var i = 0; i <= 2016; i++)
            {
                var at = now.AddMinutes((i - 2016) * 5);
                if (i > 0) accumulated += weights[i - 1];
                var fraction = .08 + .92 * accumulated / total;
                track.Points.Add(new(at, used[index] * fraction, deadline, 0));
            }
            profile.History.Add(track);
            var credit = new CreditTrack { BucketId = "codex" };
            var decreases = Enumerable.Range(1, 288).Select(i => i % 12 < 3 ? 0 : 6 + i * 7 % 23).ToArray();
            decimal balance = (index == 5 ? 0 : 42500 - index * 7250) + decreases.Sum();
            for (var i = 0; i <= 288; i++)
            {
                if (i > 0) balance -= decreases[i - 1];
                credit.Points.Add(new(now.AddMinutes((i - 288) * 5), balance,
                    i == 0 ? null : decreases[i - 1], i == 0 ? null : now.AddMinutes((i - 289) * 5), 0));
            }
            profile.CreditHistory.Add(credit);
            return profile;
        }).ToArray();
    }

    private static ModelProvider Provider()
    {
        var ids = new[] { "atlas-code", "orion-reasoner", "lumen-fast", "cedar-vision", "nova-long" };
        var capacities = new[] { 262144, 131072, 65536, 131072, 1048576 };
        return new ModelProvider { Name = "Demo Models", BaseUrl = "https://models.example.invalid/v1", ApiKey = "fictional-showcase-key",
            Models = ids.Select((id, index) =>
            {
                var metadata = ProviderCatalog.Generic(id); metadata["context_window"] = capacities[index];
                return new ProviderModel { UpstreamId = id, Metadata = metadata, EnabledInCodex = index != 3,
                    ContextOverride = index == 0 ? new(262144, 196608) : null };
            }).ToList() };
    }
}
