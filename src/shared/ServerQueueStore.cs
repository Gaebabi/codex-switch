using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexAccountDesk;

internal static class ServerQueueStore
{
    public static Dictionary<string, DesktopQueueMessage[]> Read(string home)
    {
        if (Directory.GetFiles(home, "queue_*.sqlite").Any(path => Path.GetFileName(path) != "queue_1.sqlite"))
            throw new InvalidDataException("Unsupported server queue database version.");
        var path = Path.Combine(home, "queue_1.sqlite");
        try { File.GetAttributes(path); }
        catch (FileNotFoundException) { return new(); }
        using var queue = new ReadOnlySqlite(path);
        var rows = queue.Read("SELECT id, thread_id, json_type(payload_json, '$.UserInput'), json_type(payload_json, '$.UserInput.client_id'), json_extract(payload_json, '$.UserInput.client_id') FROM queued_items");
        var result = new Dictionary<string, List<DesktopQueueMessage>>();
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row[0]) || !Guid.TryParse(row[1], out _) || row[2] != "object" || row[3] is not (null or "null" or "text") || row[4] == "")
                throw new InvalidDataException("Invalid server queue identity metadata.");
            if (!result.TryGetValue(row[1]!, out var messages)) result.Add(row[1]!, messages = new());
            messages.Add(new(row[0]!, row[4]));
        }
        if (result.Count == 0) return new();
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var roots = new HashSet<string>();
        foreach (var id in result.Keys)
        {
            var metadata = state.Read("SELECT originator, source, agent_path FROM threads WHERE id = ?", id);
            if (metadata.Count != 1) throw new InvalidDataException("Queued thread metadata missing.");
            var row = metadata[0];
            if (row[0] == "Codex Desktop" && row[1] == "vscode" && row[2] == null) roots.Add(id);
        }
        return result.Where(pair => roots.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
