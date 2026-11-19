using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexAccountDesk;

internal static class NativeHistoryThreads
{
    internal sealed record Entry(string Id, string Path, string? Model);
    internal sealed record SidebarEntry(string? Project, string? ProjectName, int ProjectRank, int PinnedRank, int ChatRank, long Recency);

    internal static IReadOnlyDictionary<string, SidebarEntry> Sidebar(string home, IEnumerable<string> ids)
    {
        var selected = ids.Distinct().Take(64).ToArray();
        var result = new Dictionary<string, SidebarEntry>();
        if (selected.Length == 0) return result;
        JsonElement settings = default;
        var preferences = Path.Combine(home, ".codex-global-state.json");
        if (File.Exists(preferences))
        {
            using var stream = new FileStream(preferences, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var content = JsonDocument.Parse(stream);
            settings = content.RootElement.Clone();
        }
        static JsonElement Property(JsonElement value, string key) =>
            value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : default;
        static string? Value(JsonElement value, string key) =>
            Property(value, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
        static IEnumerable<string> Values(JsonElement value) => value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)
            : Enumerable.Empty<string>();
        static Dictionary<string, int> Ranks(IEnumerable<string> values) =>
            values.Distinct().Select((id, rank) => (id, rank)).ToDictionary(pair => pair.id, pair => pair.rank);
        var pinSetting = Property(settings, "pinned-thread-ids");
        var pinOrder = Ranks(Values(pinSetting));
        var projectOrder = Ranks(Values(Property(settings, "pinned-project-ids")).Concat(Values(Property(settings, "project-order"))));
        var legacy = Property(Property(settings, "app-server-project-id-by-legacy-project-id-by-host"), "local:" + home);
        var legacyIds = legacy.ValueKind == JsonValueKind.Object
            ? legacy.EnumerateObject().Where(pair => pair.Value.ValueKind == JsonValueKind.String)
                .GroupBy(pair => pair.Value.GetString()!).ToDictionary(group => group.Key, group => group.First().Name)
            : new Dictionary<string, string>();
        var orders = Property(settings, "sidebar-project-thread-orders");
        var assignments = Property(settings, "thread-project-assignments");
        var localProjects = Property(settings, "local-projects");
        using var db = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var columns = db.Read("PRAGMA table_info(threads)").Select(row => row[1]).ToHashSet();
        string Column(string name, string fallback) => columns.Contains(name) ? name : fallback;
        var projects = db.Read("PRAGMA table_info(projects)").Count == 0 ? new List<string?[]>() : db.Read("SELECT id, name FROM projects ORDER BY position, id");
        var projectNames = projects.ToDictionary(row => row[0]!, row => row[1]);
        var projectRanks = Ranks(projectOrder.OrderBy(pair => pair.Value).Select(pair => Value(legacy, pair.Key) ?? pair.Key)
            .Concat(projects.Select(row => row[0]!)));
        var query = "SELECT id, " + Column("project_id", "NULL") + ", " + Column("is_pinned", "0") + ", COALESCE(" +
            Column("recency_at_ms", "NULL") + ", " + Column("updated_at_ms", "0") + ", 0) FROM threads WHERE id IN (" +
            string.Join(",", selected.Select(_ => "?")) + ")";
        var threadRanks = new Dictionary<string, Dictionary<string, int>>();
        foreach (var row in db.Read(query, selected))
        {
            var id = row[0]!;
            var assigned = Value(Property(assignments, id), "projectId");
            var project = row[1] ?? (assigned == null ? null : Value(legacy, assigned) ?? assigned);
            var pinned = pinOrder.GetValueOrDefault(id, pinSetting.ValueKind != JsonValueKind.Array && row[2] == "1" ? 0 : int.MaxValue);
            var rank = int.MaxValue;
            string? name = null;
            if (project != null)
            {
                var legacyId = assigned ?? legacyIds.GetValueOrDefault(project);
                if (!threadRanks.TryGetValue(project, out var order))
                {
                    var explicitOrder = Property(orders, project);
                    if (explicitOrder.ValueKind == JsonValueKind.Undefined && legacyId != null) explicitOrder = Property(orders, legacyId);
                    threadRanks[project] = order = Ranks(Values(Property(explicitOrder, "threadIds")));
                }
                rank = order.GetValueOrDefault(id, int.MaxValue);
                name = projectNames.GetValueOrDefault(project) ?? Value(Property(localProjects, legacyId ?? project), "name");
                if (name == null) project = null;
            }
            _ = long.TryParse(row[3], out var recency);
            result[id] = new(project, name, project == null ? int.MaxValue : projectRanks.GetValueOrDefault(project, int.MaxValue), pinned, rank, recency);
        }
        return result;
    }
    internal static IReadOnlyDictionary<string, string> Titles(string home, IEnumerable<string> ids)
    {
        var selected = ids.Distinct().Take(64).ToArray();
        var titles = new Dictionary<string, string>();
        if (selected.Length == 0) return titles;
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var hasName = state.Read("PRAGMA table_info(threads)").Any(column => column[1] == "name");
        var query = "SELECT id, " + (hasName ? "name" : "NULL") + ", title FROM threads WHERE id IN (" + string.Join(",", selected.Select(_ => "?")) + ")";
        var fallbacks = new Dictionary<string, string>();
        foreach (var row in state.Read(query, selected))
        {
            if (!string.IsNullOrWhiteSpace(row[1])) titles[row[0]!] = row[1]!;
            else fallbacks[row[0]!] = row[2] ?? "";
        }
        if (fallbacks.Count > 0)
        {
            var index = Path.Combine(home, "session_index.jsonl");
            if (File.Exists(index))
            {
                using var stream = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is string line)
                {
                    // The native client can append to this index while it is read.
                    try
                    {
                        using var record = JsonDocument.Parse(line);
                        if (record.RootElement.ValueKind != JsonValueKind.Object) continue;
                        var id = JsonValue.String(record.RootElement, "id");
                        var name = JsonValue.String(record.RootElement, "thread_name");
                        if (id != null && fallbacks.ContainsKey(id) && !string.IsNullOrWhiteSpace(name)) titles[id] = name;
                    }
                    catch (JsonException) { }
                }
            }
            foreach (var fallback in fallbacks) titles.TryAdd(fallback.Key, fallback.Value);
        }
        return titles;
    }
    internal static IReadOnlyList<Entry> Read(string home, string? thread)
    {
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var rows = thread == null
            ? state.Read("SELECT id, rollout_path, model FROM threads WHERE model_provider = 'openai'")
            : state.Read("SELECT id, rollout_path, model FROM threads WHERE model_provider = 'openai' AND id = ?", thread);
        return rows.Select(row => new Entry(row[0]!, row[1]!, row[2])).ToArray();
    }
}
