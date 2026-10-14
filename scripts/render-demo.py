"""Render one fictional documentation screenshot with the current Windows UI."""
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'artifacts/documentation-demo'
OUTPUT = ROOT / 'docs/media/demo.png'

PROJECT = r'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <StartupObject>CodexAccountDesk.DemoRenderer</StartupObject>
    <AssemblyName>CodexSwitch.Demo</AssemblyName><RootNamespace>CodexAccountDesk</RootNamespace>
    <ApplicationManifest>../../src/windows/app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../src/windows/*.cs;../../src/shared/*.cs;../../src/platforms/macos/*.cs" /><Compile Include="DemoRenderer.cs" />
    <EmbeddedResource Include="../../assets/brand/mark.path" LogicalName="CodexAccountDesk.Mark.path" />
    <EmbeddedResource Include="../../assets/brand/mark-body.path" LogicalName="CodexAccountDesk.MarkBody.path" />
    <EmbeddedResource Include="../../assets/brand/app-icon.ico" LogicalName="CodexAccountDesk.AppIcon.ico" />
    <EmbeddedResource Include="../../resources/Burn0Tokens.css" LogicalName="CodexAccountDesk.Burn0Tokens.css" />
    <EmbeddedResource Include="../../resources/Burn0Icons.json" LogicalName="CodexAccountDesk.Burn0Icons.json" />
    <EmbeddedResource Include="../../localization/Strings.ko.json" WithCulture="false" LogicalName="CodexAccountDesk.Strings.ko.json" />
    <EmbeddedResource Include="../../localization/Strings.en.json" WithCulture="false" LogicalName="CodexAccountDesk.Strings.en.json" />
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Loc.SetLanguage("en"); var now = DateTimeOffset.UtcNow;
        var vault = new AccountVault(Path.Combine(fixture, "data"));
        vault.Data.Language = "en"; vault.Data.Theme = "dark";
        vault.Data.Accounts.Add(Profile("work", "pro", 38, 1, "25500", 432, 150, 4, now));
        vault.Data.Accounts.Add(Profile("personal", "pro_lite", 63, 0, "8200", 288, 75, 2, now));
        vault.Data.Accounts.Add(Profile("research", "plus", 86, 0, "0", 168, 105, 6, now));
        vault.Data.Accounts.Add(Profile("backup", "pro", 14, 2, "12400", 624, 165, 0, now));
        var service = new AccountService(vault, Path.Combine(fixture, "home"));
        typeof(AccountService).GetProperty(nameof(AccountService.CurrentKey))!.SetValue(service, "work");
        var window = new MainWindow(service, new Program.Options { DataDirectory = vault.DirectoryPath,
            CodexHome = service.CodexHome, ReportPath = Path.Combine(fixture, "unused.json") }) { Width = 800, Height = 560 };
        typeof(MainWindow).GetField("_ready", Private)!.SetValue(window, true);
        Call(window, "RenderAccounts"); Call(window, "UpdateControls");
        var theme = new Burn0Theme(true);
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
        var root = (Grid)window.Content;
        window.Content = null; root.Background = theme.Color("canvas"); root.Resources = window.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(root, theme.Font);
        var titlebar = (Grid)root.Children[0];
        var demo = new TextBlock { Text = "DEMO", FontFamily = theme.Font, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = theme.Color("warning"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(demo, 1); titlebar.Children.Add(demo);
        var size = new Size(800, 560);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Call(window, "LayoutCards");
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var radius = WindowChrome.GetWindowChrome(window).CornerRadius.TopLeft;
        var frame = new DrawingVisual();
        using (var drawing = frame.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(size), radius, radius));
            drawing.DrawRectangle(new VisualBrush(root), null, new Rect(size)); drawing.Pop();
            var edge = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60656C")), 1);
            drawing.DrawRoundedRectangle(null, edge, new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), radius - 0.5, radius - 0.5);
        }
        var bitmap = new RenderTargetBitmap(800, 560, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = File.Create(output)) encoder.Save(stream);
        var receipt = new { kind = "fictional documentation demo", actualUiSource = "MainWindow and current resource/account components",
            renderedAtUtc = now, width = 800, height = 560, profiles = vault.Data.Accounts.Select(a => new { a.Email, a.Plan,
                remaining = a.Usage!.LeastRemaining, credits = a.Usage.Buckets[0].CreditBalance, resets = a.Usage.AvailableResets }),
            scene = "everyday mixed status", cornerRadius = radius, edgePixels = 1,
            resourceHistory = "bounded independent fluctuations over two minutes",
            running = 5, queued = 2, goals = 1, cpu = 26, memory = 73, gpu = 42, vram = 58,
            liveAccountAccess = false, nativeCodexStarted = false, liveTaskInjection = false };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "demo.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
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
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / 'Demo.csproj').write_text(PROJECT + '\n', encoding='utf-8')
    (WORK / 'DemoRenderer.cs').write_text(RENDERER + '\n', encoding='utf-8')
    subprocess.run(['dotnet', 'build', str(WORK / 'Demo.csproj'), '-c', 'Release', '--nologo', '-p:UseSharedCompilation=false'], check=True)
    with tempfile.TemporaryDirectory(prefix='fixture-', dir=WORK) as fixture:
        subprocess.run(['dotnet', str(WORK / 'bin/Release/net8.0-windows/CodexSwitch.Demo.dll'), str(OUTPUT), fixture], check=True, timeout=30)
    manifest_path = OUTPUT.with_suffix('.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    manifest['sourceVersion'] = ET.parse(ROOT / 'src/windows/CodexSwitch.Windows.csproj').findtext('PropertyGroup/Version')
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'screenshot': str(OUTPUT), 'bytes': OUTPUT.stat().st_size, 'data': 'fictional', 'liveAccountsTouched': False}))

if __name__ == '__main__':
    main()
