using System;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal static class MacAppearance
{
    public static bool ReducedMotion() => OperatingSystem.IsMacOS() && GetBool(Send(GetClass("NSWorkspace"), Selector("sharedWorkspace")), Selector("accessibilityDisplayShouldReduceMotion"));
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")] private static extern IntPtr GetClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool GetBool(IntPtr receiver, IntPtr selector);
}
