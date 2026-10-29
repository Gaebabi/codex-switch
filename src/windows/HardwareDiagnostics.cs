using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexAccountDesk;

internal static class HardwareDiagnostics
{
    public static async Task<int> VerifyAsync(string path)
    {
        using var process = Process.GetCurrentProcess(); var startedCpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        var samples = new List<HardwareUsage>();
        await using var monitor = new HardwareResourceMonitor(); monitor.Start();
        for (var index = 0; index < 36; index++)
        {
            await Task.Delay(500); var usage = monitor.Current;
            if (usage.CheckedAt != DateTimeOffset.MinValue && (samples.Count == 0 || usage.CheckedAt != samples[^1].CheckedAt)) samples.Add(usage);
        }
        process.Refresh();
        var cpuPercent = 100 * (process.TotalProcessorTime - startedCpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount;
        var available = samples.Any(s => s.Cpu.Percent != null && s.Memory.Percent != null && s.Gpu.Percent != null && s.Vram.Percent != null);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = typeof(Program).Assembly.GetName().Version!.ToString(3), available, scope = "Actual Windows system counters and DXGI capacities; no account access or generated system load.",
            observerCpuPercent = cpuPercent, observerWorkingSetMiB = process.WorkingSet64 / 1048576.0,
            meanWarmSampleMilliseconds = samples.Skip(1).Select(s => s.SampleMilliseconds).DefaultIfEmpty().Average(), samples }, new JsonSerializerOptions { WriteIndented = true }));
        return available ? 0 : 1;
    }
    public static void CaptureScenarios(string directory)
    {
        Directory.CreateDirectory(directory); var originalLanguage = Loc.Language;
        foreach (var dark in new[] { false, true })
        foreach (var language in new[] { "ko", "en" })
        {
            Loc.SetLanguage(language); var theme = new Burn0Theme(dark);
            var root = new StackPanel { Background = theme.Color("canvas"), Margin = new Thickness(12) };
            foreach (var values in new double?[][] { new double?[] { 28, 52, 41, 34 }, new double?[] { 70, 85, 78, 94 }, new double?[] { null, null, null, null }, new double?[] { 0, 0, 0, 100 } })
            {
                var at = DateTimeOffset.UtcNow;
                var steps = new[] { 0.0, 0.0, 0.15, 0.15, 0.25, 0.2, 0.9, 1.0 };
                var history = steps.Select((step, index) => new HardwareObservation(at.AddSeconds((index - steps.Length + 1) * 2),
                    values[0] * step, values[1] * step, values[2] * step, values[3] * step)).ToArray();
                var panel = new HardwareUsagePanel { Margin = new Thickness(0, 6, 0, 6) };
                panel.Refresh(new(at, new(values[0]), new(values[1]), new(values[2]), new(values[3]), Array.Empty<GpuResource>(), 0, history), theme);
                root.Children.Add(panel);
            }
            var window = new Window { Width = 218, Height = 224, Content = root, Background = theme.Color("canvas"), WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, Left = -12000, Top = -12000 };
            window.Show(); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, (dark ? "dark" : "light") + "-" + language + ".png"))) encoder.Save(output);
            window.Close();
        }
        Loc.SetLanguage(originalLanguage);
    }
}
