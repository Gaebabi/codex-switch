using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexAccountDesk;

// Only an explicit provider-editor click calls this. Never restart or adopt a desktop process.
internal static class CustomTerminalLauncher
{
    internal static ProcessStartInfo WindowsStartInfo(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A prepared custom profile command is required.", nameof(command));
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static void OpenWindows(string command)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Copy the custom terminal command and run it in a POSIX shell.");
        using var process = Process.Start(WindowsStartInfo(command)) ?? throw new IOException("The custom terminal could not be opened. Copy its command instead.");
    }
}
