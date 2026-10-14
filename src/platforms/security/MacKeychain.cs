using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CodexAccountDesk;

internal static class MacKeychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly Lazy<IntPtr> Library = new(() => NativeLibrary.Load(Security));
    private static IntPtr Symbol(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Library.Value, name));
    public static byte[] ReadOrCreateKey(string account, bool create)
    {
        var service = MacCf.String("com.gaebabi.codex-account-desk.vault"); var identity = MacCf.String(account);
        try
        {
            var query = MacCf.Dictionary(new[] { Symbol("kSecClass"), Symbol("kSecAttrService"), Symbol("kSecAttrAccount"), Symbol("kSecReturnData"), Symbol("kSecMatchLimit") },
                new[] { Symbol("kSecClassGenericPassword"), service, identity, MacCf.True, Symbol("kSecMatchLimitOne") });
            var status = SecItemCopyMatching(query, out var found); MacCf.Release(query);
            if (status == 0)
            {
                try
                {
                    var bytes = MacCf.Bytes(found);
                    if (bytes.Length != 32) throw new CryptographicException("Invalid Keychain vault key.");
                    return bytes;
                }
                finally { MacCf.Release(found); }
            }
            if (status != -25300 || !create) throw new CryptographicException("Cannot read account vault key from Keychain (" + status + ").");
            var key = RandomNumberGenerator.GetBytes(32); var data = MacCf.Data(key);
            var attributes = MacCf.Dictionary(new[] { Symbol("kSecClass"), Symbol("kSecAttrService"), Symbol("kSecAttrAccount"), Symbol("kSecValueData") },
                new[] { Symbol("kSecClassGenericPassword"), service, identity, data });
            try { status = SecItemAdd(attributes, IntPtr.Zero); }
            finally { MacCf.Release(attributes); MacCf.Release(data); }
            if (status == 0) return key;
            CryptographicOperations.ZeroMemory(key);
            if (status == -25299) return ReadOrCreateKey(account, false);
            throw new CryptographicException("Cannot create account vault key in Keychain (" + status + ").");
        }
        finally { MacCf.Release(service); MacCf.Release(identity); }
    }
    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    internal static void DeleteDiagnosticKey(string directory)
    {
        if (!System.IO.Path.GetFileName(directory).StartsWith("mac-self-test-", StringComparison.Ordinal))
            throw new ArgumentException("Only a disposable diagnostic key may be removed.");
        var service = MacCf.String("com.gaebabi.codex-account-desk.vault"); var account = MacCf.String(SecureFiles.Hash(directory));
        var query = MacCf.Dictionary(new[] { Symbol("kSecClass"), Symbol("kSecAttrService"), Symbol("kSecAttrAccount") },
            new[] { Symbol("kSecClassGenericPassword"), service, account });
        try { var status = SecItemDelete(query); if (status != 0 && status != -25300) throw new CryptographicException("Cannot remove diagnostic Keychain key (" + status + ")."); }
        finally { MacCf.Release(query); MacCf.Release(service); MacCf.Release(account); }
    }
    [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
}

internal static class MacCf
{
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<IntPtr> Library = new(() => NativeLibrary.Load(Foundation));
    public static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(Library.Value, "kCFBooleanTrue"));
    public static IntPtr String(string value) => CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100);
    public static IntPtr Dictionary(IntPtr[] keys, IntPtr[] values) => CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length,
        NativeLibrary.GetExport(Library.Value, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(Library.Value, "kCFTypeDictionaryValueCallBacks"));
    public static IntPtr Data(byte[] bytes) => CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
    public static byte[] Bytes(IntPtr data)
    {
        var result = new byte[checked((int)CFDataGetLength(data))]; Marshal.Copy(CFDataGetBytePtr(data), result, 0, result.Length); return result;
    }
    public static IntPtr Value(IntPtr dictionary, string key)
    { var name = String(key); try { return CFDictionaryGetValue(dictionary, name); } finally { Release(name); } }
    public static double? Number(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, 13, out var number) ? number : null;
    public static double? Number(IntPtr dictionary, string key) => Number(Value(dictionary, key));
    public static string? Text(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID()) return null;
        var buffer = new byte[4096];
        return CFStringGetCString(value, buffer, buffer.Length, 0x08000100) ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0') : null;
    }
    public static bool IsData(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFDataGetTypeID();
    public static bool IsDictionary(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFDictionaryGetTypeID();
    public static void Release(IntPtr value) { if (value != IntPtr.Zero) CFRelease(value); }
    [DllImport(Foundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(Foundation)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(Foundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(Foundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(Foundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(Foundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(Foundation)] private static extern void CFRelease(IntPtr value);
    [DllImport(Foundation)] private static extern nuint CFGetTypeID(IntPtr value);
    [DllImport(Foundation)] private static extern nuint CFNumberGetTypeID();
    [DllImport(Foundation)] private static extern nuint CFDataGetTypeID();
    [DllImport(Foundation)] private static extern nuint CFDictionaryGetTypeID();
    [DllImport(Foundation)] private static extern nuint CFStringGetTypeID();
    [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, int type, out double value);
    [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr text, byte[] bytes, nint length, uint encoding);
}
