using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexAccountDesk;

[SupportedOSPlatform("linux")]
internal static class LinuxNativeText
{
    private static readonly Lazy<IntPtr> Library = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "libHarfBuzzSharp.so");
        if (!File.Exists(path)) throw new DllNotFoundException("The complete Linux package must include its native text library.");
        var handle = dlopen(path, 2 | 8);
        if (handle == IntPtr.Zero) throw new DllNotFoundException("The packaged native text library could not be bound in its own symbol scope.");
        return handle;
    });
    public static void Initialize() => _ = Library.Value;
    [DllImport("libc", SetLastError = true)] private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
