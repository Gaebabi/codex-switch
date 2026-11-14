using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexAccountDesk;

internal static class NativeHistoryThreads
{
    internal sealed record Entry(string Id, string Path, string? Model);
    internal static IReadOnlyList<Entry> Read(string home, string? thread)
    {
        using var state = new ReadOnlySqlite(Path.Combine(home, "state_5.sqlite"));
        var rows = thread == null
            ? state.Read("SELECT id, rollout_path, model FROM threads WHERE model_provider = 'openai'")
            : state.Read("SELECT id, rollout_path, model FROM threads WHERE model_provider = 'openai' AND id = ?", thread);
        return rows.Select(row => new Entry(row[0]!, row[1]!, row[2])).ToArray();
    }
}
