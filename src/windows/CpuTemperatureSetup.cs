using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class CpuTemperatureSetup
{
    internal const string InstallerSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    public static async Task<bool> InstallAsync(CancellationToken cancellation)
    {
        var file = Path.Combine(Path.GetTempPath(), "CodexSwitch-PawnIO-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            using (var response = await client.GetAsync("https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe", HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 8388608) throw new InvalidDataException("CPU driver download is too large.");
                using var input = await response.Content.ReadAsStreamAsync(cancellation); var bytes = new byte[81920]; int count; long total = 0;
                while ((count = await input.ReadAsync(bytes, cancellation)) != 0)
                { total += count; if (total > 8388608) throw new InvalidDataException("CPU driver download is too large."); await output.WriteAsync(bytes.AsMemory(0, count), cancellation); }
            }
            using var verified = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(verified, cancellation)).ToLowerInvariant();
            if (digest != InstallerSha256) throw new InvalidDataException("CPU driver installer hash mismatch.");
            using var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas", Arguments = "-install -silent", WindowStyle = ProcessWindowStyle.Hidden }) ??
                throw new IOException("CPU driver setup did not start.");
            await installer.WaitForExitAsync();
            if (installer.ExitCode is not (0 or 3010)) throw new Win32Exception(installer.ExitCode);
            return installer.ExitCode == 3010;
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
