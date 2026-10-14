using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CodexAccountDesk;

internal sealed class MacMenuBar : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly IntPtr AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    private static readonly ConcurrentDictionary<IntPtr, MacMenuBar> Owners = new();
    private static readonly MenuAction Callback = Dispatch;
    private static readonly Lazy<IntPtr> TargetClass = new(CreateTargetClass);
    private readonly Dictionary<string, IntPtr> _items = new();
    private readonly Dictionary<nint, Action> _actions = new();
    private readonly IntPtr _status, _button, _menu, _target, _image;
    private string? _title, _tooltip;
    private bool _disposed;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MenuAction(IntPtr target, IntPtr selector, IntPtr sender);
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public double Width, Height; public NativeSize(double width, double height) { Width = width; Height = height; } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public double X, Y, Width, Height; }

    public MacMenuBar()
    {
        _ = AppKit;
        _status = SendDouble(Send(GetClass("NSStatusBar"), "systemStatusBar"), Selector("statusItemWithLength:"), -1);
        if (_status == IntPtr.Zero) throw new InvalidOperationException("Could not create the macOS menu bar item.");
        Send(_status, "retain"); _button = Send(_status, "button");
        _target = Send(Send(TargetClass.Value, "alloc"), "init"); Owners[_target] = this;
        _menu = SendArg(Send(GetClass("NSMenu"), "alloc"), Selector("initWithTitle:"), Text("Codex Switch"));
        SendBool(_menu, Selector("setAutoenablesItems:"), 0);
        SendArg(_status, Selector("setMenu:"), _menu);
        _image = LoadSymbol(); SendArg(_button, Selector("setImage:"), _image);
        SendInteger(_button, Selector("setImagePosition:"), 2);
        SendInteger(_button, Selector("setImageScaling:"), 2);
        SendArg(_button, Selector("setFont:"), SendFont(GetClass("NSFont"), Selector("monospacedDigitSystemFontOfSize:weight:"), 13, 0));
        SendBool(_status, Selector("setVisible:"), 1);
    }
    public void Add(string key, string label, Action action)
    {
        var tag = (nint)(_actions.Count + 1);
        var item = InitMenuItem(Send(GetClass("NSMenuItem"), "alloc"), Selector("initWithTitle:action:keyEquivalent:"), Text(label), Selector("activate:"), Text(""));
        SendArg(item, Selector("setTarget:"), _target); SendInteger(item, Selector("setTag:"), tag);
        SendArg(_menu, Selector("addItem:"), item); Send(item, "release");
        _items.Add(key, item); _actions.Add(tag, action);
    }
    public void Separator() => SendArg(_menu, Selector("addItem:"), Send(GetClass("NSMenuItem"), "separatorItem"));
    public void SetItem(string key, string label, bool enabled)
    {
        var item = _items[key]; SendArg(item, Selector("setTitle:"), Text(label));
        SendBool(item, Selector("setEnabled:"), enabled ? (byte)1 : (byte)0);
    }
    public void Update(string title, string tooltip)
    {
        if (_title != title) { SendArg(_button, Selector("setTitle:"), Text(" " + title)); _title = title; }
        if (_tooltip != tooltip)
        {
            SendArg(_button, Selector("setToolTip:"), Text(tooltip));
            SendArg(_button, Selector("setAccessibilityLabel:"), Text("Codex Switch · " + title)); _tooltip = tooltip;
        }
    }
    internal MenuBarState Probe()
    {
        var frame = Rect(Send(_button, "window")); var screen = Rect(Send(GetClass("NSScreen"), "mainScreen"));
        return new(ReadText(Send(_button, "title")) ?? "", GetBool(_status, Selector("isVisible")) != 0,
            GetBool(_image, Selector("isTemplate")) != 0, _items.Count, frame.X, screen.Y + screen.Height - frame.Y - frame.Height, frame.Width, frame.Height);
    }
    private static NativeRect Rect(IntPtr target)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64) { GetRectX64(out var value, target, Selector("frame")); return value; }
        return GetRect(target, Selector("frame"));
    }
    private static void Dispatch(IntPtr target, IntPtr selector, IntPtr sender)
    {
        if (Owners.TryGetValue(target, out var owner) && owner._actions.TryGetValue(GetInteger(sender, Selector("tag")), out var action))
            Dispatcher.UIThread.Post(() => { if (!owner._disposed) action(); });
    }
    private static IntPtr CreateTargetClass()
    {
        const string name = "CodexAccountDeskMenuActions";
        var existing = GetClass(name); if (existing != IntPtr.Zero) return existing;
        var type = AllocateClass(GetClass("NSObject"), name, 0);
        if (type == IntPtr.Zero || !AddMethod(type, Selector("activate:"), Marshal.GetFunctionPointerForDelegate(Callback), "v@:@"))
            throw new InvalidOperationException("Could not register macOS menu actions.");
        RegisterClass(type); return type;
    }
    private static IntPtr LoadSymbol()
    {
        var icon = new MenuBarSymbol { Width = 20, Height = 20 };
        icon.Measure(new Size(20, 20)); icon.Arrange(new Rect(0, 0, 20, 20));
        using var bitmap = new RenderTargetBitmap(new PixelSize(40, 40), new Vector(192, 192)); bitmap.Render(icon);
        using var stream = new MemoryStream(); bitmap.Save(stream); var bytes = stream.ToArray();
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var data = SendData(GetClass("NSData"), Selector("dataWithBytes:length:"), memory, (nuint)bytes.Length);
            var image = SendArg(Send(GetClass("NSImage"), "alloc"), Selector("initWithData:"), data);
            if (image == IntPtr.Zero) throw new InvalidOperationException("Could not render the menu bar symbol.");
            SendSize(image, Selector("setSize:"), new NativeSize(18, 18)); SendBool(image, Selector("setTemplate:"), 1); return image;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Owners.TryRemove(_target, out _);
        SendArg(_status, Selector("setMenu:"), IntPtr.Zero);
        SendArg(Send(GetClass("NSStatusBar"), "systemStatusBar"), Selector("removeStatusItem:"), _status);
        Send(_menu, "release"); Send(_status, "release"); Send(_target, "release"); Send(_image, "release");
    }
    private static IntPtr Send(IntPtr target, string selector) => Message(target, Selector(selector));
    private static IntPtr Text(string value) => StringMessage(GetClass("NSString"), Selector("stringWithUTF8String:"), value);
    private static string? ReadText(IntPtr value) => value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(value, "UTF8String"));
    [DllImport(ObjC, EntryPoint = "objc_getClass")] private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_allocateClassPair")] private static extern IntPtr AllocateClass(IntPtr parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extra);
    [DllImport(ObjC, EntryPoint = "class_addMethod")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AddMethod(IntPtr type, IntPtr selector, IntPtr implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature);
    [DllImport(ObjC, EntryPoint = "objc_registerClassPair")] private static extern void RegisterClass(IntPtr type);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Message(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendArg(IntPtr target, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr StringMessage(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendDouble(IntPtr target, IntPtr selector, double value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendFont(IntPtr target, IntPtr selector, double size, double weight);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendData(IntPtr target, IntPtr selector, IntPtr bytes, nuint length);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendSize(IntPtr target, IntPtr selector, NativeSize size);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr target, IntPtr selector, byte value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte GetBool(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendInteger(IntPtr target, IntPtr selector, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint GetInteger(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr InitMenuItem(IntPtr target, IntPtr selector, IntPtr title, IntPtr action, IntPtr equivalent);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern NativeRect GetRect(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern void GetRectX64(out NativeRect rectangle, IntPtr target, IntPtr selector);
}

internal sealed record MenuBarState(string Title, bool Visible, bool Template, int MenuItems, double X, double Y, double Width, double Height);

internal sealed class MenuBarSymbol : BrandMark { }
