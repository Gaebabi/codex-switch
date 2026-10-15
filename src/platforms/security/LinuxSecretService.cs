using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace CodexAccountDesk;

internal static class LinuxSecretService
{
    private const string Secret = "libcodex-switch-secret-1.so.0", Glib = "libglib-2.0.so.0", Gio = "libgio-2.0.so.0", Gobject = "libgobject-2.0.so.0";
    private static readonly Lazy<IntPtr> GlibLibrary = new(() => NativeLibrary.Load(Glib));
    private static readonly Lazy<IntPtr> SecretLibrary = new(() =>
    {
        var handle = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, Secret));
        var abi = Marshal.GetDelegateForFunctionPointer<CompatibilityAbi>(NativeLibrary.GetExport(handle, "secret_codex_compat_abi"));
        if (abi() != 1) { NativeLibrary.Free(handle); throw new DllNotFoundException("Unsupported private Secret Service ABI."); }
        return handle;
    });
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CompatibilityAbi();
    static LinuxSecretService() => NativeLibrary.SetDllImportResolver(typeof(LinuxSecretService).Assembly,
        (name, _, _) => name == Secret ? SecretLibrary.Value : IntPtr.Zero);
    [StructLayout(LayoutKind.Sequential)] private struct Error { public uint Domain; public int Code; public IntPtr Message; }
    [StructLayout(LayoutKind.Sequential)] private struct Node { public IntPtr Data, Next, Previous; }
    private static LinuxVaultException Unavailable() => new(LinuxKeyFailure.Unavailable);
    private static void Check(IntPtr error, bool cancelledByDeadline)
    {
        if (error == IntPtr.Zero) return;
        try
        {
            var value = Marshal.PtrToStructure<Error>(error);
            throw new LinuxVaultException(value.Domain == secret_error_get_quark() && value.Code == 2 ? LinuxKeyFailure.Locked :
                value.Domain == g_io_error_quark() && (value.Code == 24 || value.Code == 19 && cancelledByDeadline) ? LinuxKeyFailure.TimedOut :
                value.Domain == g_io_error_quark() && value.Code == 19 ? LinuxKeyFailure.Cancelled : LinuxKeyFailure.Unavailable);
        }
        finally { g_error_free(error); }
    }
    public static byte[] ReadOrCreateKey(string account, bool create)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux Secret Service requires Linux.");
        if (account.Length != 64 || account.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("A hashed vault identity is required.");
        try
        {
            using var session = new Session(account); using var found = session.Search();
            if (found.Items.Count > 1) throw new LinuxVaultException(LinuxKeyFailure.Ambiguous);
            if (found.Items.Count == 1) return session.Read(found.Items[0]);
            if (!create) throw new LinuxVaultException(LinuxKeyFailure.Missing);
            var key = RandomNumberGenerator.GetBytes(32);
            try
            {
                session.Create(key);
                using var confirmed = session.Search();
                if (confirmed.Items.Count != 1) throw new LinuxVaultException(LinuxKeyFailure.Ambiguous);
                var stored = session.Read(confirmed.Items[0]);
                try { if (!CryptographicOperations.FixedTimeEquals(key, stored)) throw new LinuxVaultException(LinuxKeyFailure.Invalid); }
                finally { CryptographicOperations.ZeroMemory(stored); }
                return key;
            }
            catch { CryptographicOperations.ZeroMemory(key); throw; }
        }
        catch (DllNotFoundException) { throw Unavailable(); }
        catch (EntryPointNotFoundException) { throw Unavailable(); }
    }
    private sealed class ItemList : IDisposable
    {
        private readonly IntPtr _list;
        public List<IntPtr> ItemsList { get; } = new();
        public IReadOnlyList<IntPtr> Items => ItemsList;
        public ItemList(IntPtr list) { _list = list; for (var next = list; next != IntPtr.Zero;) { var node = Marshal.PtrToStructure<Node>(next); ItemsList.Add(node.Data); next = node.Next; } }
        public void Dispose() { foreach (var item in ItemsList) g_object_unref(item); if (_list != IntPtr.Zero) g_list_free(_list); }
    }
    private sealed class Session : IDisposable
    {
        private IntPtr _cancel, _service, _attributes;
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));
        private CancellationTokenRegistration _registration;
        private void Checked(IntPtr error) => Check(error, _deadline.IsCancellationRequested);
        public Session(string account)
        {
            try
            {
                _cancel = g_cancellable_new(); if (_cancel == IntPtr.Zero) throw Unavailable();
                _registration = _deadline.Token.Register(() => g_cancellable_cancel(_cancel));
                _service = secret_service_get_sync(0, _cancel, out var error); Checked(error); if (_service == IntPtr.Zero) throw Unavailable();
                var owner = VerifyOwner();
                var legacyKde = UsesLegacyKdeEncoding(owner);
                var original = _service; _service = IntPtr.Zero;
                try { _service = secret_codex_service_open_sync(owner, legacyKde ? 1 : 0, _cancel, out error); Checked(error); if (_service == IntPtr.Zero) throw Unavailable(); }
                finally { g_object_unref(original); }
                _attributes = g_hash_table_new_full(NativeLibrary.GetExport(GlibLibrary.Value, "g_str_hash"), NativeLibrary.GetExport(GlibLibrary.Value, "g_str_equal"),
                    NativeLibrary.GetExport(GlibLibrary.Value, "g_free"), NativeLibrary.GetExport(GlibLibrary.Value, "g_free"));
                if (_attributes == IntPtr.Zero) throw new OutOfMemoryException();
                g_hash_table_insert(_attributes, g_strdup("application"), g_strdup("com.gaebabi.codex-account-desk.vault.linux"));
                g_hash_table_insert(_attributes, g_strdup("account"), g_strdup(account));
            }
            catch { Dispose(); throw; }
        }
        private string VerifyOwner()
        {
            var owner = g_dbus_proxy_get_name_owner(_service); if (owner == IntPtr.Zero) throw Unavailable();
            try
            {
                var text = Marshal.PtrToStringUTF8(owner) ?? throw Unavailable();
                var parameters = g_variant_ref_sink(g_variant_new_tuple(new[] { g_variant_new_string(text) }, 1));
                try
                {
                    var reply = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(_service), "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus",
                        "GetConnectionUnixUser", parameters, IntPtr.Zero, 0, 10000, _cancel, out var error);
                    try
                    {
                        Checked(error);
                        if (reply == IntPtr.Zero || Marshal.PtrToStringUTF8(g_variant_get_type_string(reply)) != "(u)") throw Unavailable();
                        var child = g_variant_get_child_value(reply, 0);
                        try { if (g_variant_get_uint32(child) != LinuxPrivateFiles.UserId) throw Unavailable(); }
                        finally { g_variant_unref(child); }
                    }
                    finally { if (reply != IntPtr.Zero) g_variant_unref(reply); }
                }
                finally { g_variant_unref(parameters); }
                return text;
            }
            finally { g_free(owner); }
        }
        private bool UsesLegacyKdeEncoding(string owner)
        {
            var kwallet5 = false;
            string? modern = null;
            var names = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(_service), "org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus", "ListNames", IntPtr.Zero, IntPtr.Zero, 0, 10000, _cancel, out var error);
            try
            {
                Checked(error);
                if (names == IntPtr.Zero || Marshal.PtrToStringUTF8(g_variant_get_type_string(names)) != "(as)") throw Unavailable();
                var array = g_variant_get_child_value(names, 0);
                try
                {
                    for (nuint index = 0; index < g_variant_n_children(array); index++)
                    {
                        var child = g_variant_get_child_value(array, index);
                        string? name;
                        try { name = Marshal.PtrToStringUTF8(g_variant_get_string(child, out _)); }
                        finally { g_variant_unref(child); }
                        if (name is not ("org.kde.kwalletd" or "org.kde.kwalletd5" or "org.kde.kwalletd6" or "org.kde.ksecretd")) continue;
                        var parameters = g_variant_ref_sink(g_variant_new_tuple(new[] { g_variant_new_string(name) }, 1));
                        try
                        {
                            var reply = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(_service), "org.freedesktop.DBus", "/org/freedesktop/DBus",
                                "org.freedesktop.DBus", "GetNameOwner", parameters, IntPtr.Zero, 0, 10000, _cancel, out error);
                            try
                            {
                                Checked(error);
                                if (reply == IntPtr.Zero || Marshal.PtrToStringUTF8(g_variant_get_type_string(reply)) != "(s)") throw Unavailable();
                                var value = g_variant_get_child_value(reply, 0);
                                try
                                {
                                    if (Marshal.PtrToStringUTF8(g_variant_get_string(value, out _)) == owner)
                                    {
                                        if (name is "org.kde.kwalletd6" or "org.kde.ksecretd")
                                        {
                                            if (modern != null && modern != name) throw new LinuxVaultException(LinuxKeyFailure.ProviderIncompatible);
                                            modern = name;
                                        }
                                        else kwallet5 = true;
                                    }
                                }
                                finally { g_variant_unref(value); }
                            }
                            finally { if (reply != IntPtr.Zero) g_variant_unref(reply); }
                        }
                        finally { g_variant_unref(parameters); }
                    }
                }
                finally { g_variant_unref(array); }
            }
            finally { if (names != IntPtr.Zero) g_variant_unref(names); }
            return modern == null ? kwallet5 : VerifiedKdeEncoding(owner, modern);
        }
        private bool VerifiedKdeEncoding(string owner, string service)
        {
            var parameters = g_variant_ref_sink(g_variant_new_tuple(new[] { g_variant_new_string(owner) }, 1));
            uint processId;
            try
            {
                var reply = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(_service), "org.freedesktop.DBus", "/org/freedesktop/DBus",
                    "org.freedesktop.DBus", "GetConnectionUnixProcessID", parameters, IntPtr.Zero, 0, 10000, _cancel, out var error);
                try
                {
                    Checked(error);
                    if (reply == IntPtr.Zero || Marshal.PtrToStringUTF8(g_variant_get_type_string(reply)) != "(u)") throw Unavailable();
                    var value = g_variant_get_child_value(reply, 0);
                    try { processId = g_variant_get_uint32(value); }
                    finally { g_variant_unref(value); }
                }
                finally { if (reply != IntPtr.Zero) g_variant_unref(reply); }
            }
            finally { g_variant_unref(parameters); }
            parameters = g_variant_ref_sink(g_variant_new_tuple(new[] { g_variant_new_string("org.qtproject.Qt.QCoreApplication"), g_variant_new_string("applicationVersion") }, 2));
            try
            {
                var reply = g_dbus_connection_call_sync(g_dbus_proxy_get_connection(_service), owner, "/MainApplication",
                    "org.freedesktop.DBus.Properties", "Get", parameters, IntPtr.Zero, 0, 10000, _cancel, out var error);
                try
                {
                    Checked(error);
                    if (reply == IntPtr.Zero || Marshal.PtrToStringUTF8(g_variant_get_type_string(reply)) != "(v)") throw Unavailable();
                    var child = g_variant_get_child_value(reply, 0);
                    try
                    {
                        var value = g_variant_get_variant(child);
                        try
                        {
                            if (Marshal.PtrToStringUTF8(g_variant_get_type_string(value)) != "s") throw Unavailable();
                            return LinuxSecretProvider.UsesLegacyEncoding(service, Marshal.PtrToStringUTF8(g_variant_get_string(value, out _)) ?? "", processId, _deadline.Token);
                        }
                        finally { g_variant_unref(value); }
                    }
                    finally { g_variant_unref(child); }
                }
                finally { if (reply != IntPtr.Zero) g_variant_unref(reply); }
            }
            finally { g_variant_unref(parameters); }
        }
        public ItemList Search()
        {
            var items = new ItemList(secret_service_search_sync(_service, IntPtr.Zero, _attributes, 2, _cancel, out var error));
            try { Checked(error); return items; } catch { items.Dispose(); throw; }
        }
        public byte[] Read(IntPtr item)
        {
            if (secret_item_get_locked(item) != 0) throw new LinuxVaultException(LinuxKeyFailure.Locked);
            var loaded = secret_item_load_secret_sync(item, _cancel, out var error); Checked(error); if (loaded == 0) throw Unavailable();
            var value = secret_item_get_secret(item); if (value == IntPtr.Zero) throw new LinuxVaultException(LinuxKeyFailure.Locked);
            try
            {
                var bytes = secret_value_get(value, out var count);
                if (count != 32 || bytes == IntPtr.Zero)
                    throw new LinuxVaultException(LinuxKeyFailure.Invalid);
                var result = new byte[32]; Marshal.Copy(bytes, result, 0, result.Length); return result;
            }
            finally { secret_value_unref(value); }
        }
        public void Create(byte[] key)
        {
            var collection = secret_collection_for_alias_sync(_service, "default", 0, _cancel, out var error);
            try
            {
                Checked(error); if (collection == IntPtr.Zero) throw Unavailable();
                if (secret_collection_get_locked(collection) != 0) throw new LinuxVaultException(LinuxKeyFailure.Locked);
                var value = secret_value_new(key, key.Length, "application/octet-stream"); if (value == IntPtr.Zero) throw Unavailable();
                try
                {
                    var item = secret_item_create_sync(collection, IntPtr.Zero, _attributes, "Codex Switch vault key", value, 0, _cancel, out error);
                    try { Checked(error); if (item == IntPtr.Zero) throw Unavailable(); }
                    finally { if (item != IntPtr.Zero) g_object_unref(item); }
                }
                finally { secret_value_unref(value); }
            }
            finally { if (collection != IntPtr.Zero) g_object_unref(collection); }
        }
        public void Delete(IntPtr item) { var deleted = secret_item_delete_sync(item, _cancel, out var error); Checked(error); if (deleted == 0) throw Unavailable(); }
        public void Dispose()
        {
            _registration.Dispose(); _deadline.Dispose();
            if (_attributes != IntPtr.Zero) { g_hash_table_unref(_attributes); _attributes = IntPtr.Zero; }
            if (_service != IntPtr.Zero) { g_object_unref(_service); _service = IntPtr.Zero; }
            if (_cancel != IntPtr.Zero) { g_object_unref(_cancel); _cancel = IntPtr.Zero; }
        }
    }
    private static string DiagnosticIdentity(string directory)
    {
        if (!OperatingSystem.IsLinux() || !Path.GetFileName(directory).StartsWith("linux-self-test-", StringComparison.Ordinal)) throw new ArgumentException("Only a disposable Linux diagnostic key may be modified.");
        return SecureFiles.Hash(Path.GetFullPath(directory));
    }
    internal static void DeleteDiagnosticKey(string directory)
    { using var session = new Session(DiagnosticIdentity(directory)); using var items = session.Search(); foreach (var item in items.Items) session.Delete(item); }
    internal static void AddDiagnosticKey(string directory, byte[] bytes)
    { using var session = new Session(DiagnosticIdentity(directory)); session.Create(bytes); }
    internal static int CountDiagnosticKeys(string directory)
    { using var session = new Session(DiagnosticIdentity(directory)); using var items = session.Search(); return items.Items.Count; }
    [DllImport(Secret)] private static extern IntPtr secret_service_get_sync(uint flags, IntPtr cancel, out IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_variant(IntPtr value);
    [DllImport(Secret)] private static extern IntPtr secret_codex_service_open_sync([MarshalAs(UnmanagedType.LPUTF8Str)] string owner, int kwallet, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern IntPtr secret_service_search_sync(IntPtr service, IntPtr schema, IntPtr attributes, uint flags, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern int secret_item_get_locked(IntPtr item);
    [DllImport(Secret)] private static extern int secret_item_load_secret_sync(IntPtr item, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern IntPtr secret_item_get_secret(IntPtr item);
    [DllImport(Secret)] private static extern IntPtr secret_value_get(IntPtr value, out nuint length);
    [DllImport(Secret)] private static extern IntPtr secret_value_new([In] byte[] bytes, nint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);
    [DllImport(Secret)] private static extern void secret_value_unref(IntPtr value);
    [DllImport(Secret)] private static extern IntPtr secret_collection_for_alias_sync(IntPtr service, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, uint flags, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern int secret_collection_get_locked(IntPtr collection);
    [DllImport(Secret)] private static extern IntPtr secret_item_create_sync(IntPtr collection, IntPtr schema, IntPtr attributes, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr value, uint flags, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern int secret_item_delete_sync(IntPtr item, IntPtr cancel, out IntPtr error);
    [DllImport(Secret)] private static extern uint secret_error_get_quark();
    [DllImport(Glib)] private static extern IntPtr g_hash_table_new_full(IntPtr hash, IntPtr equal, IntPtr keyDestroy, IntPtr valueDestroy);
    [DllImport(Glib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(Glib)] private static extern void g_hash_table_unref(IntPtr table);
    [DllImport(Glib)] private static extern IntPtr g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Glib)] private static extern void g_free(IntPtr value);
    [DllImport(Glib)] private static extern void g_list_free(IntPtr list);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(Gobject)] private static extern void g_object_unref(IntPtr value);
    [DllImport(Gio)] private static extern IntPtr g_cancellable_new();
    [DllImport(Gio)] private static extern void g_cancellable_cancel(IntPtr value);
    [DllImport(Gio)] private static extern uint g_io_error_quark();
    [DllImport(Gio)] private static extern IntPtr g_dbus_proxy_get_name_owner(IntPtr proxy);
    [DllImport(Gio)] private static extern IntPtr g_dbus_proxy_get_connection(IntPtr proxy);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, IntPtr parameters, IntPtr replyType, uint flags, int timeout, IntPtr cancel, out IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_tuple([In] IntPtr[] values, nuint count);
    [DllImport(Glib)] private static extern IntPtr g_variant_ref_sink(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_type_string(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_child_value(IntPtr value, nuint index);
    [DllImport(Glib)] private static extern uint g_variant_get_uint32(IntPtr value);
    [DllImport(Glib)] private static extern nuint g_variant_n_children(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_string(IntPtr value, out nuint length);
    [DllImport(Glib)] private static extern void g_variant_unref(IntPtr value);
}
