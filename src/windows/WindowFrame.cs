using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace CodexAccountDesk;

internal static class WindowFrame
{
    private sealed class AppliedFrame { public uint BorderColor; }
    private static readonly ConditionalWeakTable<Window, AppliedFrame> Applied = new();
    public static uint? AppliedBorderColor(Window window) => Applied.TryGetValue(window, out var frame) ? frame.BorderColor : null;
    public static void Apply(Window window, double resizeBorder = 6)
    {
        var nativeCorners = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(resizeBorder),
            // A nonzero glass frame keeps WindowChrome from installing a region that disables DWM rounding.
            GlassFrameThickness = new Thickness(nativeCorners ? 1 : 0),
            CornerRadius = new CornerRadius(8),
            UseAeroCaptionButtons = false
        });
        if (nativeCorners) window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            uint corners = 2;
            DwmSetWindowAttribute(handle, 33, ref corners, sizeof(uint));
            RefreshBorder(window);
        };
        window.Activated += (_, _) => RefreshBorder(window);
        window.Deactivated += (_, _) => RefreshBorder(window);
    }

    public static void RefreshBorder(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = window.Background is SolidColorBrush brush && brush.Color.R + brush.Color.G + brush.Color.B < 384;
        var color = (Color)ColorConverter.ConvertFromString(dark ? window.IsActive ? "#60656C" : "#484C53" : window.IsActive ? "#B5B8BE" : "#C4C7CB");
        uint border = color.R | (uint)color.G << 8 | (uint)color.B << 16;
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(uint));
        Applied.GetValue(window, _ => new AppliedFrame()).BorderColor = border;
    }

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr handle, int attribute, ref uint value, uint size);
}
