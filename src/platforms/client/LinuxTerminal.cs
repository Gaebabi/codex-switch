using System;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal static class LinuxTerminal
{
    internal static bool IsInteractive => OperatingSystem.IsLinux() && isatty(0) == 1 && isatty(1) == 1;

    [DllImport("libc")] private static extern int isatty(int descriptor);
}
