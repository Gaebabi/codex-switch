"""Render one fictional documentation screenshot with the current Windows UI."""
import json
import argparse
from pathlib import Path
import subprocess
import tempfile
import hashlib
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'artifacts/documentation-demo'
OUTPUT = ROOT / 'docs/media/demo.png'

PROJECT = r'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <StartupObject>CodexAccountDesk.DemoRenderer</StartupObject>
    <AssemblyName>CodexSwitch.Demo</AssemblyName><RootNamespace>CodexAccountDesk</RootNamespace>
    <ApplicationManifest>../../src/windows/app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Management" Version="10.0.13" />
    <ProjectReference Include="../../src/core/CodexSwitch.Core.csproj" />
    <ProjectReference Include="../../src/platforms/security/CodexSwitch.PlatformSecurity.csproj" />
    <ProjectReference Include="../../src/platforms/client/CodexSwitch.DesktopClient.csproj" />
    <Compile Include="../../src/windows/*.cs;../../src/shared/*.cs" /><Compile Include="DemoRenderer.cs" />
    <EmbeddedResource Include="../../assets/brand/mark.path" LogicalName="CodexAccountDesk.Mark.path" />
    <EmbeddedResource Include="../../assets/brand/mark-body.path" LogicalName="CodexAccountDesk.MarkBody.path" />
    <EmbeddedResource Include="../../assets/brand/app-icon.ico" LogicalName="CodexAccountDesk.AppIcon.ico" />
    <EmbeddedResource Include="../../resources/Burn0Tokens.css" LogicalName="CodexAccountDesk.Burn0Tokens.css" />
    <EmbeddedResource Include="../../resources/Burn0Icons.json" LogicalName="CodexAccountDesk.Burn0Icons.json" />
  </ItemGroup>
</Project>'''

RENDERER = r'''using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace CodexAccountDesk;
internal static class DemoRenderer
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static void Call(MainWindow window, string name) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, null);

    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]); var fixture = Path.GetFullPath(args[1]);
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length > 5 && args[5].Length > 0) Loc.Install(args[5], Path.Combine(fixture, "languages"), PlatformLanguagePackWriter.Instance);
        Loc.SetLanguage(args.Length > 2 ? args[2] : "en"); var now = DateTimeOffset.UtcNow;
        var vault = new AccountVault(Path.Combine(fixture, "data"));
        vault.Data.Language = Loc.Language; vault.Data.Theme = args.Length > 8 ? args[8] : "dark";
        vault.Data.Accounts.Add(Profile("work", "pro", 38, 1, "25500", 432, 150, 4, now));
        vault.Data.Accounts.Add(Profile("personal", "pro_lite", 63, 0, "8200", 288, 75, 2, now));
        vault.Data.Accounts.Add(Profile("research", "plus", 86, 0, "0", 168, 105, 6, now));
        vault.Data.Accounts.Add(Profile("backup", "pro", 14, 2, "12400", 624, 165, 0, now));
        var deadlinePreview = args.Length > 7 && args[7] == "dates";
        if (deadlinePreview)
        {
            vault.Data.Accounts[0].SubscriptionActiveUntil = now.AddDays(3);
            vault.Data.Accounts[0].Usage!.Buckets[0].Secondary!.ResetsAt = now.AddDays(2).ToUnixTimeSeconds();
            vault.Data.Accounts[1].SubscriptionActiveUntil = now.AddDays(2);
            vault.Data.Accounts[1].Usage!.Buckets[0].Secondary!.ResetsAt = now.AddDays(1).ToUnixTimeSeconds();
            vault.Data.Accounts[3].SubscriptionActiveUntil = null;
            vault.Data.Accounts[3].Usage!.Buckets[0].Secondary!.ResetsAt = null;
        }
        var service = new AccountService(vault, Path.Combine(fixture, "home"));
        typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, "work");
        var width = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 800;
        var height = args.Length > 4 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 560;
        var window = new MainWindow(service, new Program.Options { DataDirectory = vault.DirectoryPath,
            CodexHome = service.CodexHome, ReportPath = Path.Combine(fixture, "unused.json") }) { Width = width, Height = height };
        typeof(MainWindow).GetField("_ready", Private)!.SetValue(window, true);
        Call(window, "RenderAccounts"); Call(window, "UpdateControls");
        var theme = new Burn0Theme(vault.Data.Theme != "light");
        Field<SessionUsagePanel>(window, "_sessionCount").Refresh(new SessionActivity(true, 5, 0, 5, Queued: 2, Goals: 1), theme);
        var history = Enumerable.Range(0, 61).Select(i =>
        {
            var t = i - 60;
            return new HardwareObservation(now.AddSeconds(t * 2),
                26 + 4 * Math.Sin(t * 0.7) + 2 * Math.Sin(t * 1.3),
                73 + 1.6 * Math.Sin(t * 0.22),
                42 + 7 * Math.Sin(t * 0.39) + 3 * Math.Sin(t * 0.81),
                58 + 1.4 * Math.Sin(t * 0.11));
        }).ToArray();
        Field<HardwareUsagePanel>(window, "_hardwarePanel").Refresh(new HardwareUsage(now,
            new(26), new(73), new(42), new(58), Array.Empty<GpuResource>(), 0, history), theme);
        const long gib = 1073741824;
        Field<DriveUsagePanel>(window, "_drivePanel").Refresh(new[] {
            new DriveReading("C:\\", "C:", (long)(40.6 * gib), 512 * gib), new DriveReading("D:\\", "D:", 624 * gib, 1024 * gib) }, theme);
        var thermalMode = args.Length > 6 ? args[6] : "";
        if (thermalMode == "demo")
        {
            TemperatureReading Sensor(double value) => TemperatureReading.From(value, "Documentation sensor fixture", "Fictional temperature", now);
            Field<HardwareUsagePanel>(window, "_hardwarePanel").Refresh(new HardwareUsage(now,
                new(26, Temperature: Sensor(77)), new(73, Temperature: Sensor(44)), new(42, Temperature: Sensor(92)), new(58, Temperature: Sensor(98)), Array.Empty<GpuResource>(), 0, history), theme);
            Field<DriveUsagePanel>(window, "_drivePanel").Refresh(new[] {
                new DriveReading("C:\\", "C:", (long)(40.6*gib), 512*gib, Sensor(71)), new DriveReading("D:\\", "D:", 624*gib, 1024*gib, Sensor(49)) }, theme);
        }
        else if (thermalMode == "live")
        {
            using var sampler = new HardwareSampler(); var observations = new HardwareHistory(); var usage = observations.Observe(sampler.Sample());
            for (var index = 0; index < 3; index++) { System.Threading.Thread.Sleep(1000); usage = observations.Observe(sampler.Sample()); }
            Field<HardwareUsagePanel>(window, "_hardwarePanel").Refresh(usage, theme);
            Field<DriveUsagePanel>(window, "_drivePanel").Refresh(new[] { "C:\\", "D:\\" }.Select(path => {
                var drive = new DriveInfo(path); return new DriveReading(path, path[..2], drive.AvailableFreeSpace, drive.TotalSize, WindowsDiskTemperature.Read(path)); }).ToArray(), theme);
        }
        var root = (Grid)window.Content;
        window.Content = null; root.Background = theme.Color("canvas"); root.Resources = window.Resources;
        if (Loc.RightToLeft) root.FlowDirection = FlowDirection.RightToLeft;
        root.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag);
        System.Windows.Documents.TextElement.SetFontFamily(root, theme.Font);
        var titlebar = (Grid)root.Children[0];
        var demo = new TextBlock { Text = "DEMO", FontFamily = theme.Font, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = theme.Color("warning"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(demo, 1); titlebar.Children.Add(demo);
        var size = new Size(width, height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Call(window, "LayoutCards");
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var cards = Field<System.Collections.Generic.List<AccountTankCard>>(window, "_tankCards");
        foreach (var card in cards) card.Update(now);
        var accountBounds = cards.Select(card => card.ProbeBounds()).ToArray();
        var dateObservations = new System.Collections.Generic.List<object>();
        if (deadlinePreview)
        {
            foreach (var at in new[] { now.AddSeconds(-1), now, now.AddHours(73) })
            {
                foreach (var card in cards) card.Update(at);
                dateObservations.Add(new { at, readings = cards.Select(card => card.ProbeBounds()).ToArray() });
            }
            var noQuota = new AccountTankCard(new AccountProfile { SubscriptionActiveUntil = now.AddDays(2) }, null, theme,
                new AccountCardHeader("Fictional no-quota account", "", theme, new StackPanel()), new Button(), _ => { });
            noQuota.Update(now);
            dateObservations.Add(new { at = now, noQuota = noQuota.ProbeBounds() });
            foreach (var card in cards) card.Update(now);
        }
        var hardwareBounds = Field<HardwareUsagePanel>(window, "_hardwarePanel").Probe();
        if (JsonSerializer.SerializeToElement(hardwareBounds).GetProperty("readings").EnumerateArray().Any(value => !value.GetProperty("fits").GetBoolean()))
            throw new InvalidOperationException("Resource usage and temperature labels do not fit the application width.");
        var radius = WindowChrome.GetWindowChrome(window).CornerRadius.TopLeft;
        var frame = new DrawingVisual();
        using (var drawing = frame.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(size), radius, radius));
            drawing.DrawRectangle(new VisualBrush(root), null, new Rect(size)); drawing.Pop();
            var edge = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60656C")), 1);
            drawing.DrawRoundedRectangle(null, edge, new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), radius - 0.5, radius - 0.5);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        if (Enumerable.Range(0, width * height).Count(i => pixels[i * 4 + 3] == 255) < width * height * 0.95)
            throw new InvalidOperationException("Application render is empty: root " + root.ActualWidth + "x" + root.ActualHeight + "; radius " + radius);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = File.Create(output)) encoder.Save(stream);
        var receipt = new { kind = "fictional documentation demo", actualUiSource = "MainWindow and current resource/account components",
            renderedAtUtc = now, width, height, profiles = vault.Data.Accounts.Select(a => new { a.Email, a.Plan,
                remaining = a.Usage!.LeastRemaining, credits = a.Usage.Buckets[0].CreditBalance, resets = a.Usage.AvailableResets }),
            scene = "everyday mixed status", cornerRadius = radius, edgePixels = 1,
            resourceHistory = "bounded independent fluctuations over two minutes",
            running = 5, queued = 2, goals = 1, cpu = 26, memory = 73, gpu = 42, vram = 58,
            language = Loc.Language, culture = Loc.Culture.Name,
            temperatures = thermalMode == "live" ? "Actual read-only native resources; fictional account/session inputs" : thermalMode == "demo" ? "Fictional sensor-capable fixture" : "No temperature values injected", hardwareBounds,
            datePreview = deadlinePreview, theme = vault.Data.Theme, asOfUtc = now, accountBounds, dateObservations,
            warningColor = theme.Color("warning").ToString(), textColor = theme.Color("text").ToString(), mutedColor = theme.Color("muted").ToString(),
            liveAccountAccess = false, nativeCodexStarted = false, liveTaskInjection = false };
        File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        window.Close(); app.Shutdown(); return 0;
    }
    private static AccountProfile Profile(string id, string plan, double used, int resets, string credits,
        double subscriptionHours, double resetHours, double ratePerHour, DateTimeOffset now)
    {
        var deadline = now.AddHours(resetHours).ToUnixTimeSeconds();
        var window = new UsageWindow { UsedPercent = used, Minutes = 10080, ResetsAt = deadline };
        var profile = new AccountProfile { Key = id, Email = id + "@example.invalid", AccountId = "demo-" + id, Plan = plan,
            SubscriptionActiveUntil = now.AddHours(subscriptionHours), Usage = new UsageSnapshot { CheckedAt = now,
                AvailableResets = resets, OrdinaryUsageAllowed = true, Buckets = new() {
                    new UsageBucket { Id = "codex", Label = "Codex", Plan = plan, Secondary = window, CreditBalance = credits } } } };
        var track = new UsageTrack { BucketId = "codex", Slot = "secondary", Minutes = 10080 };
        for (var i = 0; i <= 60; i++) track.Points.Add(new(now.AddMinutes(i - 60),
            used - (60 - i) * ratePerHour / 60, deadline, 0));
        profile.History.Add(track); return profile;
    }
}'''

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--language', default='en')
    parser.add_argument('--output', type=Path, default=OUTPUT)
    parser.add_argument('--width', type=int, default=800)
    parser.add_argument('--height', type=int, default=560)
    parser.add_argument('--pack', type=Path)
    parser.add_argument('--deadline-preview', action='store_true', help='Show fictional near/far/missing subscription and reset deadlines.')
    parser.add_argument('--theme', choices=['dark', 'light'], default='dark')
    thermal = parser.add_mutually_exclusive_group()
    thermal.add_argument('--temperature-preview', action='store_true', help='Show clearly fictional sensor-capable Celsius values for layout verification.')
    thermal.add_argument('--live-resources', action='store_true', help='Read actual local hardware/disk counters and sensors; account/session inputs remain fictional.')
    options = parser.parse_args()
    output = options.output.resolve()
    if options.width < 500 or options.height < 360:
        parser.error('Use at least the application minimum size, 500x360.')
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / 'Demo.csproj').write_text(PROJECT + '\n', encoding='utf-8')
    (WORK / 'DemoRenderer.cs').write_text(RENDERER + '\n', encoding='utf-8')
    subprocess.run(['dotnet', 'build', str(WORK / 'Demo.csproj'), '-c', 'Release', '--nologo', '-p:UseSharedCompilation=false'], check=True)
    with tempfile.TemporaryDirectory(prefix='fixture-', dir=WORK) as fixture:
        subprocess.run(['dotnet', str(WORK / 'bin/Release/net10.0-windows/CodexSwitch.Demo.dll'), str(output), fixture, options.language, str(options.width), str(options.height), str(options.pack.resolve()) if options.pack else '', 'live' if options.live_resources else 'demo' if options.temperature_preview else '', 'dates' if options.deadline_preview else '', options.theme], check=True, timeout=30)
    with Image.open(output) as raw:
        raw.load()
        clean = Image.new('RGBA', raw.size)
        clean.paste(raw.convert('RGBA'))
    clean.save(output, format='PNG')
    manifest_path = output.with_suffix('.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    manifest['sourceVersion'] = ET.parse(ROOT / 'Directory.Build.props').findtext('PropertyGroup/Version')
    manifest['sourceRevision'] = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
    manifest['language'] = options.language
    manifest['theme'] = options.theme
    manifest['sha256'] = hashlib.sha256(output.read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'screenshot': str(output), 'bytes': output.stat().st_size, 'language':options.language, 'data': 'fictional', 'liveAccountsTouched': False}))

if __name__ == '__main__':
    main()
