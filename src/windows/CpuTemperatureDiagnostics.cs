using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class CpuTemperatureDiagnostics
{
    public static async Task<int> VerifyAsync(string path)
    {
        TemperatureReading? reading = null; string? failure = null;
        try
        {
            await using var sensor = new WindowsCpuTemperature(); sensor.Start();
            var until = DateTimeOffset.UtcNow.AddSeconds(12);
            while (sensor.Current.State == TemperatureState.Pending && DateTimeOffset.UtcNow < until) await Task.Delay(50);
            reading = sensor.Current;
            if (reading.State is TemperatureState.Pending or TemperatureState.InvalidData or TemperatureState.ReadFailed || reading.Code == "thermal.cpu_bundle_missing")
                throw new InvalidDataException("CPU worker application integration failed.");
            var refreshed = await sensor.ProbeAsync(default);
            if (refreshed.CheckedAt <= reading.CheckedAt || refreshed.State is TemperatureState.Pending or TemperatureState.InvalidData || refreshed.Code == "thermal.cpu_bundle_missing")
                throw new InvalidDataException("CPU capability probe did not replace the original worker result.");
            foreach (var delay in new[] { 0, 20 })
            {
                var quick = new WindowsCpuTemperature(); quick.Start();
                if (delay != 0) await Task.Delay(delay);
                await quick.DisposeAsync();
                try { quick.Start(); throw new InvalidDataException("Disposed CPU worker restarted."); }
                catch (ObjectDisposedException) { }
            }
        }
        catch (Exception error) { failure = error.GetType().Name; }
        var value = new { schema = 1, passed = failure == null ? 1 : 0, failed = failure == null ? 0 : 1, failure,
            state = reading?.State.ToString(), nativeCpuValueAvailable = reading?.State == TemperatureState.Available,
            scope = "Published account app verifies its embedded worker digest, current-user process-bound pipe, fresh status and cleanup. No UI/elevation, accounts, Codex actions or private paths are accessed or exported. Unavailable is not physical CPU acceptance." };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value));
        return failure == null ? 0 : 1;
    }
}
