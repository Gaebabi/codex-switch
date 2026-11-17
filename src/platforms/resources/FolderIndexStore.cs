using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed class FolderIndexStore : IDisposable
{
    internal const int DirectoryKind = 1, RootKind = 2, ReadingKind = 3, AllocationKind = 4, DirtyKind = 5, ResetKind = 6, JournalKind = 7;
    private IntPtr _database;
    private bool _transaction;
    internal bool HasChanges => _transaction;
    internal string DirectoryPath { get; }
    internal string DatabasePath { get; }
    internal FolderIndexStore(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Persistent folder indexing requires Windows.");
        DirectoryPath = Path.GetFullPath(directory);
        SecureFiles.CreatePrivateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "allocation.sqlite3");
        var existed = File.Exists(DatabasePath);
        if (existed && (File.GetAttributes(DatabasePath) & FileAttributes.ReparsePoint) != 0) throw new IOException("The folder index must not be a linked file.");
        var opened = sqlite3_open_v2(DatabasePath, out _database, 2 | 4 | 0x10000, IntPtr.Zero);
        if (opened != 0) { Dispose(); throw new IOException("Cannot open the folder index (SQLite " + opened + ")."); }
        try
        {
            Check(sqlite3_busy_timeout(_database, 500));
            if (existed && Scalar("PRAGMA application_id") != "1129530953") throw new InvalidDataException("The folder index has an unrecognized identity.");
            if (existed && Scalar("PRAGMA user_version") != "1") throw new InvalidDataException("The folder index has an unsupported version.");
            Execute("PRAGMA journal_mode=WAL"); Execute("PRAGMA synchronous=FULL");
            Execute("CREATE TABLE IF NOT EXISTS entries(kind INTEGER NOT NULL, key TEXT NOT NULL, data BLOB NOT NULL, PRIMARY KEY(kind,key)) WITHOUT ROWID");
            if (!existed) { Execute("PRAGMA application_id=1129530953"); Execute("PRAGMA user_version=1"); }
        }
        catch { Dispose(); throw; }
    }
    private static string Key(string value) => value.ToUpperInvariant();
    internal bool Owns(string path) => DriveMonitor.RootComparer.Equals(path, DirectoryPath) ||
        DriveMonitor.RootComparer.Equals(path, DatabasePath) || DriveMonitor.RootComparer.Equals(path, DatabasePath + "-wal") || DriveMonitor.RootComparer.Equals(path, DatabasePath + "-shm");
    internal void Put<T>(int kind, string key, T value)
    {
        Begin();
        using var bytes = new MemoryStream();
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        if (json.Length < 1024) { bytes.WriteByte(0); bytes.Write(json); }
        else { bytes.WriteByte(1); using var compressed = new BrotliStream(bytes, CompressionLevel.Fastest, true); compressed.Write(json); }
        Execute("INSERT INTO entries(kind,key,data) VALUES(?1,?2,?3) ON CONFLICT(kind,key) DO UPDATE SET data=excluded.data", kind, Key(key), bytes.ToArray());
    }
    internal void Delete(int kind, string key)
    { Begin(); Execute("DELETE FROM entries WHERE kind=?1 AND key=?2", kind, Key(key)); }
    internal IEnumerable<(string Key, T Value)> Read<T>(int kind)
    {
        var prepared = sqlite3_prepare_v2(_database, "SELECT key,data FROM entries WHERE kind=?1", -1, out var statement, IntPtr.Zero);
        try
        {
            Check(prepared); Check(sqlite3_bind_int(statement, 1, kind));
            int status;
            while ((status = sqlite3_step(statement)) == 100)
            {
                var key = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0))!;
                var length = sqlite3_column_bytes(statement, 1);
                if (length <= 0) throw new InvalidDataException("An empty folder-index record was found.");
                var bytes = new byte[length]; Marshal.Copy(sqlite3_column_blob(statement, 1), bytes, 0, length);
                T? value;
                if (bytes[0] == 0) value = JsonSerializer.Deserialize<T>(bytes.AsSpan(1));
                else if (bytes[0] == 1)
                {
                    using var input = new MemoryStream(bytes, 1, bytes.Length - 1, false);
                    using var compressed = new BrotliStream(input, CompressionMode.Decompress);
                    value = JsonSerializer.Deserialize<T>(compressed);
                }
                else throw new InvalidDataException("Unsupported folder-index record encoding.");
                if (value == null) throw new InvalidDataException("A folder-index record is invalid.");
                yield return (key, value);
            }
            if (status != 101) Check(status);
        }
        finally { sqlite3_finalize(statement); }
    }
    internal void Flush()
    { if (_transaction) { Execute("COMMIT"); _transaction = false; } }
    private void Begin() { if (!_transaction) { Execute("BEGIN IMMEDIATE"); _transaction = true; } }
    private string Scalar(string sql)
    {
        var result = sqlite3_prepare_v2(_database, sql, -1, out var statement, IntPtr.Zero);
        try { Check(result); var row = sqlite3_step(statement); if (row != 100) { if (row != 101) Check(row); throw new InvalidDataException("Cannot read the folder-index header."); } return Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0))!; }
        finally { sqlite3_finalize(statement); }
    }
    private void Execute(string sql, int? kind = null, string? key = null, byte[]? bytes = null)
    {
        var result = sqlite3_prepare_v2(_database, sql, -1, out var statement, IntPtr.Zero);
        try
        {
            Check(result);
            if (kind.HasValue) Check(sqlite3_bind_int(statement, 1, kind.Value));
            if (key != null) Check(sqlite3_bind_text(statement, 2, key, -1, new IntPtr(-1)));
            if (bytes != null) Check(sqlite3_bind_blob(statement, 3, bytes, bytes.Length, new IntPtr(-1)));
            while ((result = sqlite3_step(statement)) == 100) { }
            if (result != 101) Check(result);
        }
        finally { sqlite3_finalize(statement); }
    }
    private static void Check(int result)
    { if (result != 0) throw new IOException("Folder-index database operation failed (SQLite " + result + ")."); }
    public void Dispose()
    {
        if (_database == IntPtr.Zero) return;
        try { Flush(); }
        finally { sqlite3_close_v2(_database); _database = IntPtr.Zero; }
    }
    internal void Discard()
    { if (_database != IntPtr.Zero) { sqlite3_close_v2(_database); _database = IntPtr.Zero; _transaction = false; } }
    internal static void Quarantine(string directory)
    {
        var root = Path.GetFullPath(directory); SecureFiles.CreatePrivateDirectory(root);
        var suffix = ".invalid-" + Guid.NewGuid().ToString("N");
        foreach (var name in new[] { "allocation.sqlite3", "allocation.sqlite3-wal", "allocation.sqlite3-shm" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked folder index cannot be rebuilt.");
            File.Move(path, path + suffix);
        }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_bind_blob(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] private static extern int sqlite3_column_bytes(IntPtr statement, int index);
}
