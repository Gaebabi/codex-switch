using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record LocalCompactionSource(string Model, string Provider);
internal static class LocalCompactionSources
{
    internal static LocalCompactionSource? Read(string home, string? session, string ciphertext, string? account)
    {
        if (!Guid.TryParse(session, out _) || string.IsNullOrEmpty(account) || !File.Exists(Path.Combine(home, "state_5.sqlite"))) return null;
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var rows = state.Read("SELECT rollout_path, model_provider FROM threads WHERE id = ?", session!);
        if (rows.Count != 1 || rows[0][0] is not string path || rows[0][1] != "openai") return null;
        var root = Path.GetFullPath(Path.Combine(home, "sessions"));
        var sessions = new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? root;
        path = Path.GetFullPath(path).Replace("\\\\?\\", "", StringComparison.Ordinal);
        var prefix = Path.GetFullPath(sessions).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison) || !Path.GetFileName(path).Contains("-" + session, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) != ".jsonl" ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        var source = ReadFile(path, session!, ciphertext, account, out var hasBase);
        if (source != null || !hasBase) return source;
        // Current Codex can start a new rollout segment with history_base pointing at
        // its earlier same-thread segment. Read only that thread's verified files.
        var candidates = Directory.EnumerateFiles(sessions, "*" + session + "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(9).ToArray();
        if (candidates.Length > 8) return null;
        foreach (var candidate in candidates.Where(candidate => !string.Equals(candidate, path, comparison)))
        {
            var current = ReadFile(candidate, session!, ciphertext, account, out _);
            if (current != null)
            {
                if (source != null && source != current) return null;
                source = current;
            }
        }
        return source;
    }
    private static LocalCompactionSource? ReadFile(string path, string session, string ciphertext, string account, out bool hasBase)
    {
        hasBase = false;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64 * 1024 * 1024) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var bound = stream.Length; var bytes = 0; var owner = false; string? model = null;
        LocalCompactionSource? found = null;
        while (reader.ReadLine() is string line)
        {
            bytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes > bound + 1 || line.Length > 4 * 1024 * 1024) break;
            using var document = JsonDocument.Parse(line); var record = document.RootElement;
            if (!record.TryGetProperty("payload", out var payload)) continue;
            switch (JsonValue.String(record, "type"))
            {
                case "session_meta":
                    owner = JsonValue.String(payload, "id") == session && JsonValue.String(payload, "creator_account_id") == account && JsonValue.String(payload, "model_provider") == "openai";
                    hasBase = owner && payload.TryGetProperty("history_base", out var historyBase) && historyBase.ValueKind == JsonValueKind.Object && JsonValue.String(historyBase, "thread_id") == session;
                    break;
                case "turn_context": model = JsonValue.String(payload, "model"); break;
                case "compacted":
                    if (owner && model != null && payload.TryGetProperty("replacement_history", out var history) && history.ValueKind == JsonValueKind.Array)
                        foreach (var item in history.EnumerateArray())
                            if (JsonValue.String(item, "type") is "compaction" or "compaction_summary" or "context_compaction" && JsonValue.String(item, "encrypted_content") == ciphertext)
                                found = new(model, "openai");
                    break;
            }
        }
        return found;
    }
}
