using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CodexAccountDesk;

internal static class LinuxTerminalProcesses
{
    public static bool AnyRunning(int? inspectionPid, string executable, CancellationToken cancellation)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == Environment.ProcessId || pid == inspectionPid) continue;
            try
            {
                var status = LinuxPeerProcess.Read(pid, "status", 64 * 1024);
                var user = Regex.Match(status, @"^Uid:\s+([0-9]+)\s", RegexOptions.Multiline);
                if (!user.Success) throw new IOException("The client process owner could not be verified.");
                if (user.Groups[1].Value != LinuxPrivateFiles.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)) continue;
                var image = new FileInfo(directory + "/exe").LinkTarget;
                if (image == null) continue;
                image = image.EndsWith(" (deleted)", StringComparison.Ordinal) ? image[..^10] : image;
                if (image == executable || Path.GetFileName(image) == "codex") return true;
            }
            catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
            { /* A process that exited during /proc enumeration cannot use the shared login. */ }
        }
        return false;
    }
}
