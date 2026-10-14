using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace CodexAccountDesk;

internal static class DashboardRecording
{
    public static async Task RecordAsync(MainWindow window, string directory)
    {
        SecureFiles.CreatePrivateDirectory(directory);
        var prior = (window.Width, window.Height, window.Left, window.Top, window.WindowState);
        try
        {
            window.BringToFront(); window.WindowState = WindowState.Normal;
            foreach (var (name, width, height) in new[] { ("compact", 500d, 360d), ("standard", 700d, 550d), ("large", 1000d, 700d) })
            {
                window.Width = width; window.Height = height;
                window.Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - width) / 2;
                window.Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - height) / 2;
                window.UpdateLayout(); await Task.Delay(600);
                var output = Path.Combine(directory, name); SecureFiles.CreatePrivateDirectory(output);
                var times = new List<long>(); var clock = Stopwatch.StartNew();
                for (var index = 0; index < 90; index++)
                {
                    await Task.Delay(Math.Max(1, (int)(index * 100 - clock.ElapsedMilliseconds)));
                    times.Add(clock.ElapsedMilliseconds);
                    window.CapturePreview(Path.Combine(output, index.ToString("000") + ".png"), 2);
                }
                File.WriteAllText(Path.Combine(output, "timing.json"), JsonSerializer.Serialize(times));
            }
            File.WriteAllText(Path.Combine(directory, "complete.json"), "{\"source\":\"live WPF visual\",\"scale\":2,\"cursorIncluded\":false,\"framesPerSize\":90}");
        }
        finally
        {
            window.WindowState = WindowState.Normal;
            (window.Width, window.Height, window.Left, window.Top) = (prior.Width, prior.Height, prior.Left, prior.Top);
            window.WindowState = prior.WindowState;
        }
    }
}
