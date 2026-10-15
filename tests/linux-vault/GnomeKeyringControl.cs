using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

internal static class GnomeKeyringControl
{
    public static void UnlockFromInput()
    {
        var password = new byte[256]; var count = 0;
        using (var input = Console.OpenStandardInput())
        {
            while (count < password.Length) { var read = input.Read(password, count, password.Length - count); if (read == 0) break; count += read; }
            if (count == 0 || count == password.Length && input.ReadByte() != -1) throw new InvalidDataException("Unexpected diagnostic password input length.");
        }
        IntPtr cancel = IntPtr.Zero, service = IntPtr.Zero, collection = IntPtr.Zero, value = IntPtr.Zero, encoded = IntPtr.Zero, pathValue = IntPtr.Zero, parameters = IntPtr.Zero;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); CancellationTokenRegistration registration = default;
        try
        {
            cancel = g_cancellable_new(); registration = timeout.Token.Register(() => g_cancellable_cancel(cancel));
            service = secret_service_get_sync(2, cancel, out var error); Check(error); if (service == IntPtr.Zero) throw new IOException("Owned GNOME service missing.");
            collection = secret_collection_for_alias_sync(service, "default", 0, cancel, out error); Check(error); if (collection == IntPtr.Zero) throw new IOException("Owned default collection missing.");
            var path = Marshal.PtrToStringUTF8(g_dbus_proxy_get_object_path(collection)) ?? throw new IOException("Owned collection path missing.");
            value = secret_value_new(password, count, "text/plain");
            encoded = g_variant_ref_sink(secret_service_encode_dbus_secret(service, value));
            pathValue = g_variant_ref_sink(g_variant_new_object_path(path)); parameters = g_variant_ref_sink(g_variant_new_tuple(new[] { pathValue, encoded }, 2));
            var owner = g_dbus_proxy_get_name_owner(service);
            try
            {
                var name = Marshal.PtrToStringUTF8(owner) ?? throw new IOException("Owned service identity missing.");
                var reply = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(service), name, "/org/freedesktop/secrets",
                    "org.gnome.keyring.InternalUnsupportedGuiltRiddenInterface", "UnlockWithMasterPassword", parameters, IntPtr.Zero, 0, 10000, cancel, out error);
                try { Check(error); if (reply == IntPtr.Zero) throw new IOException("Owned collection unlock failed."); }
                finally { if (reply != IntPtr.Zero) g_variant_unref(reply); }
            }
            finally { if (owner != IntPtr.Zero) g_free(owner); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password); registration.Dispose();
            if (parameters != IntPtr.Zero) g_variant_unref(parameters); if (pathValue != IntPtr.Zero) g_variant_unref(pathValue); if (encoded != IntPtr.Zero) g_variant_unref(encoded);
            if (value != IntPtr.Zero) secret_value_unref(value); if (collection != IntPtr.Zero) g_object_unref(collection); if (service != IntPtr.Zero) g_object_unref(service); if (cancel != IntPtr.Zero) g_object_unref(cancel);
        }
    }
    private static void Check(IntPtr error)
    { if (error != IntPtr.Zero) { g_error_free(error); throw new IOException("Native GNOME diagnostic control failed."); } }
    [DllImport("libcodex-switch-secret-1.so.0")] private static extern IntPtr secret_service_get_sync(uint flags, IntPtr cancel, out IntPtr error);
    [DllImport("libcodex-switch-secret-1.so.0")] private static extern IntPtr secret_collection_for_alias_sync(IntPtr service, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, uint flags, IntPtr cancel, out IntPtr error);
    [DllImport("libcodex-switch-secret-1.so.0")] private static extern IntPtr secret_value_new([In] byte[] bytes, nint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [DllImport("libcodex-switch-secret-1.so.0")] private static extern IntPtr secret_service_encode_dbus_secret(IntPtr service, IntPtr value);
    [DllImport("libcodex-switch-secret-1.so.0")] private static extern void secret_value_unref(IntPtr value);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_cancellable_new();
    [DllImport("libgio-2.0.so.0")] private static extern void g_cancellable_cancel(IntPtr cancel);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_dbus_proxy_get_object_path(IntPtr proxy);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_dbus_proxy_get_name_owner(IntPtr proxy);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_dbus_proxy_get_connection(IntPtr proxy);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, IntPtr parameters, IntPtr replyType, uint flags, int timeout, IntPtr cancel, out IntPtr error);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr value);
    [DllImport("libglib-2.0.so.0")] private static extern void g_error_free(IntPtr error);
    [DllImport("libglib-2.0.so.0")] private static extern void g_free(IntPtr value);
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_variant_ref_sink(IntPtr value);
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_variant_new_object_path([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_variant_new_tuple([In] IntPtr[] values, nuint count);
    [DllImport("libglib-2.0.so.0")] private static extern void g_variant_unref(IntPtr value);
}
