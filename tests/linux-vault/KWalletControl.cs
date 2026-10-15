using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class KWalletControl
{
    public static void ConnectUnlockedCollection()
    {
        IntPtr cancel = IntPtr.Zero, service = IntPtr.Zero, collection = IntPtr.Zero, objects = IntPtr.Zero, unlocked = IntPtr.Zero;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationTokenRegistration registration = default;
        try
        {
            cancel = g_cancellable_new(); registration = deadline.Token.Register(() => g_cancellable_cancel(cancel));
            service = secret_service_get_sync(0, cancel, out var error); Check(error);
            if (service == IntPtr.Zero) throw new IOException("Owned KDE Secret Service is missing.");
            collection = secret_collection_for_alias_sync(service, "default", 0, cancel, out error); Check(error);
            if (collection == IntPtr.Zero) throw new IOException("Owned KDE collection is missing.");
            objects = g_list_append(IntPtr.Zero, collection);
            var count = secret_service_unlock_sync(service, objects, cancel, out unlocked, out error); Check(error);
            if (count != 1 || unlocked == IntPtr.Zero) throw new IOException("Owned KDE collection did not connect to its unlocked wallet.");
            var result = Marshal.ReadIntPtr(unlocked);
            if (secret_collection_get_locked(result) != 0) throw new IOException("Owned KDE collection remains locked.");
        }
        finally
        {
            registration.Dispose();
            if (unlocked != IntPtr.Zero) { for (var node = unlocked; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size)) g_object_unref(Marshal.ReadIntPtr(node)); g_list_free(unlocked); }
            if (objects != IntPtr.Zero) g_list_free(objects);
            if (collection != IntPtr.Zero) g_object_unref(collection);
            if (service != IntPtr.Zero) g_object_unref(service);
            if (cancel != IntPtr.Zero) g_object_unref(cancel);
        }
    }
    private static void Check(IntPtr error)
    { if (error != IntPtr.Zero) { g_error_free(error); throw new IOException("Owned KDE collection control failed."); } }
    [DllImport("libsecret-1.so.0")] private static extern IntPtr secret_service_get_sync(uint flags, IntPtr cancel, out IntPtr error);
    [DllImport("libsecret-1.so.0")] private static extern IntPtr secret_collection_for_alias_sync(IntPtr service, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, uint flags, IntPtr cancel, out IntPtr error);
    [DllImport("libsecret-1.so.0")] private static extern int secret_service_unlock_sync(IntPtr service, IntPtr objects, IntPtr cancel, out IntPtr unlocked, out IntPtr error);
    [DllImport("libsecret-1.so.0")] private static extern int secret_collection_get_locked(IntPtr collection);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_cancellable_new();
    [DllImport("libgio-2.0.so.0")] private static extern void g_cancellable_cancel(IntPtr cancel);
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_list_append(IntPtr list, IntPtr data);
    [DllImport("libglib-2.0.so.0")] private static extern void g_list_free(IntPtr list);
    [DllImport("libglib-2.0.so.0")] private static extern void g_error_free(IntPtr error);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr value);
}
