using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexAccountDesk;

internal static class TitleContracts
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int sqlite3_close_v2(IntPtr database);

    private static void CreateFixture(string directory, string sql)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state_5.sqlite");
        var opened = sqlite3_open_v2(path, out var db, 2 | 4, IntPtr.Zero);
        try
        {
            if (opened != 0 || sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
                throw new IOException("Could not initialize the fake native title database.");
        }
        finally { if (db != IntPtr.Zero) sqlite3_close_v2(db); }
    }

    internal static IReadOnlyDictionary<string, string> Verify(string root)
    {
        var native = Path.Combine(root, "native-titles");
        CreateFixture(native, """
            CREATE TABLE threads(id TEXT PRIMARY KEY, title TEXT, name TEXT);
            INSERT INTO threads VALUES ('chat-a', 'Original prompt preview', 'Saved Codex title');
            INSERT INTO threads VALUES ('chat-b', 'Second prompt preview', NULL);
            INSERT INTO threads VALUES ('chat-plain', 'Legacy title', '');
            INSERT INTO threads VALUES ('chat-empty', NULL, NULL);
            """);
        string Index(string id, string title) => JsonSerializer.Serialize(new { id, thread_name = title });
        File.WriteAllLines(Path.Combine(native, "session_index.jsonl"), new[]
        {
            Index("chat-a", "Outdated index title"), Index("chat-b", "Older name"),
            "[]", Index("chat-b", "Current indexed title"), Index("orphan", "Stale index entry"), "{"
        });
        var dbPath = Path.Combine(native, "state_5.sqlite");
        var before = File.ReadAllBytes(dbPath);
        var titles = NativeHistoryThreads.Titles(native, ["chat-a", "chat-b", "chat-plain", "chat-empty", "orphan", "ephemeral"]);
        if (titles.Count != 4 || titles["chat-a"] != "Saved Codex title" || titles["chat-b"] != "Current indexed title" ||
            titles["chat-plain"] != "Legacy title" || titles["chat-empty"] != "" || !before.SequenceEqual(File.ReadAllBytes(dbPath)))
            throw new Exception("Exact native display titles, legacy fallback or read-only behavior changed.");
        var legacy = Path.Combine(root, "legacy-native-titles");
        CreateFixture(legacy, "CREATE TABLE threads(id TEXT PRIMARY KEY,title TEXT); INSERT INTO threads VALUES('chat-a','Old schema title');");
        if (NativeHistoryThreads.Titles(legacy, ["chat-a"])["chat-a"] != "Old schema title")
            throw new Exception("Legacy title lookup requires the new name column.");
        var sidebar = Path.Combine(root, "native-sidebar");
        CreateFixture(sidebar, """
            CREATE TABLE threads(id TEXT PRIMARY KEY, project_id TEXT, is_pinned INTEGER, recency_at_ms INTEGER);
            CREATE TABLE projects(id TEXT PRIMARY KEY, name TEXT, position INTEGER);
            INSERT INTO projects VALUES ('p1','First native project',0), ('p2','Second native project',1);
            INSERT INTO threads VALUES ('a','p1',0,100), ('b','p2',0,200), ('c','p2',0,300), ('d',NULL,1,400);
            """);
        File.WriteAllText(Path.Combine(sidebar, ".codex-global-state.json"), """
            {"pinned-thread-ids":["a"],"project-order":["p2","p1"],"sidebar-project-thread-orders":{"p2":{"threadIds":["b","c"]}}}
            """);
        var display = NativeHistoryThreads.Sidebar(sidebar, ["a", "b", "c", "d", "missing"]);
        if (display.Count != 4 || display["a"].PinnedRank != 0 || display["d"].PinnedRank != int.MaxValue ||
            display["b"] is not { ProjectName: "Second native project", ProjectRank: 0, ChatRank: 0 } ||
            display["c"] is not { ChatRank: 1, Recency: 300 })
            throw new Exception("Inspector order does not follow the native sidebar state.");
        return titles;
    }
}
