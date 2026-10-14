using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexAccountDesk;

internal sealed class ReadOnlySqlite : IDisposable
{
    private const string Library = "codex_account_desk_sqlite";
    static ReadOnlySqlite() => NativeLibrary.SetDllImportResolver(typeof(ReadOnlySqlite).Assembly, (name, assembly, searchPath) => name == Library
        ? NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "winsqlite3.dll") : IntPtr.Zero);
    private IntPtr _database;
    public ReadOnlySqlite(string path)
    {
        var result = sqlite3_open_v2(path, out _database, 0x00000001 | 0x00008000, IntPtr.Zero);
        if (result != 0) { Dispose(); throw new IOException("Cannot open queue metadata database (SQLite " + result + ")."); }
        result = sqlite3_busy_timeout(_database, 250);
        if (result != 0) { Dispose(); Check(result); }
    }
    public List<string?[]> Read(string sql, params string[] parameters)
    {
        var prepared = sqlite3_prepare_v2(_database, sql, -1, out var statement, IntPtr.Zero);
        try
        {
            Check(prepared);
            if (sqlite3_stmt_readonly(statement) != 1) throw new InvalidOperationException("Queue metadata queries must be read-only.");
            for (var i = 0; i < parameters.Length; i++) Check(sqlite3_bind_text(statement, i + 1, parameters[i], -1, new IntPtr(-1)));
            var rows = new List<string?[]>();
            int result;
            while ((result = sqlite3_step(statement)) == 100)
            {
                var row = new string?[sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i));
                rows.Add(row);
            }
            if (result != 101) Check(result);
            return rows;
        }
        finally { sqlite3_finalize(statement); }
    }
    private static void Check(int result)
    {
        if (result != 0) throw new IOException("Cannot read queue metadata (SQLite " + result + ").");
    }
    public void Dispose() { if (_database != IntPtr.Zero) { sqlite3_close_v2(_database); _database = IntPtr.Zero; } }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_stmt_readonly(IntPtr statement);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_finalize(IntPtr statement);
}
