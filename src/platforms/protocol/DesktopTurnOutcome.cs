using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record DesktopTurnOutcome(string Id, string Status, string? Error, string Model, string Provider, DateTimeOffset At);

// Read only the bounded end of the original rollout. Never modify a transcript or
// infer failure from idle/EOF alone: a persisted task_complete must name the turn.
internal static class DesktopTurnOutcomes
{
    internal static DesktopTurnOutcome? Read(string home, string id)
    {
        if (!Guid.TryParse(id, out _)) return null;
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var rows = state.Read("SELECT rollout_path, model, model_provider, archived, originator, source, agent_path FROM threads WHERE id = ?", id);
        if (rows.Count != 1) return null;
        var row = rows[0];
        if (row[3] != "0" || row[4] != "Codex Desktop" || row[5] != "vscode" || row[6] != null || string.IsNullOrEmpty(row[1]) || string.IsNullOrEmpty(row[2])) return null;
        var root = new DirectoryInfo(Path.Combine(home, "sessions"));
        var sessions = (root.ResolveLinkTarget(true)?.FullName ?? root.FullName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        static string Normal(string path) => Path.GetFullPath(path).Replace("\\\\?\\", "", StringComparison.Ordinal);
        var path = Normal(row[0]!);
        if (!path.StartsWith(Normal(sessions).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Path.GetExtension(path) != ".jsonl" ||
            !Path.GetFileName(path).EndsWith("-" + id + ".jsonl", StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = Math.Max(0, file.Length - 4 * 1024 * 1024);
        file.Position = offset;
        var bytes = new byte[checked((int)(file.Length - offset))]; file.ReadExactly(bytes);
        return ParseTail(bytes, offset != 0, row[1]!, row[2]!);
    }
    internal static DesktopTurnOutcome? ParseTail(byte[] bytes, bool partialFirstLine, string model, string provider)
    {
        DesktopTurnOutcome? latest = null;
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            var length = i - start; var line = start; start = i + 1;
            if (partialFirstLine) { partialFirstLine = false; continue; }
            // Very large tool/assistant items cannot be completion metadata. If a
            // completion itself exceeds the bound, leave the outcome unknown.
            if (length > 512 * 1024) { latest = null; continue; }
            using var document = JsonDocument.Parse(bytes.AsMemory(line, length));
            var root = document.RootElement;
            if (JsonValue.String(root, "type") != "event_msg" || !root.TryGetProperty("payload", out var payload)) continue;
            var type = JsonValue.String(payload, "type");
            if (type is "task_started" or "turn_aborted" or "task_interrupted") { latest = null; continue; }
            if (type != "task_complete") continue;
            var turn = JsonValue.String(payload, "turn_id");
            if (turn == null || !Guid.TryParse(turn, out _) || !DateTimeOffset.TryParse(JsonValue.String(root, "timestamp"), out var at)) { latest = null; continue; }
            var hasError = payload.TryGetProperty("error", out var value) && value.ValueKind != JsonValueKind.Null;
            var error = value.ValueKind == JsonValueKind.Object ? JsonValue.String(value, "message") :
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var status = JsonValue.String(payload, "status");
            // An explicit interruption wins over any attached error text. Missing
            // error prose must not turn a persisted failure into a success.
            if (status == "interrupted") latest = new(turn, status, error, model, provider, at);
            else if (status == "failed" || hasError) latest = new(turn, "failed", error, model, provider, at);
            else if (status is null or "completed") latest = new(turn, "completed", null, model, provider, at);
            else latest = null;
        }
        return latest;
    }
}
