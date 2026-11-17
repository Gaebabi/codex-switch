using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class FolderIndexSetup
{
    internal static async Task EnableAsync(string root, CancellationToken cancellation)
    {
        if (!WindowsFolderJournal.LocalRoot(root)) throw new ArgumentException("Invalid folder-index drive.");
        if (WindowsFolderJournal.Probe(root).Access == FolderIndexAccess.Ready) return;
        var executable = Environment.ProcessPath ?? throw new IOException("The folder-index setup executable is unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--enable-folder-index"); start.ArgumentList.Add(root);
        using var setup = Process.Start(start) ?? throw new IOException("Folder-index permission setup did not start.");
        await setup.WaitForExitAsync(cancellation);
        if (setup.ExitCode != 0) throw new Win32Exception(setup.ExitCode);
        if (WindowsFolderJournal.Probe(root).Access != FolderIndexAccess.Ready)
            throw new UnauthorizedAccessException(Loc.T("folders.permission_failed"));
    }
}
