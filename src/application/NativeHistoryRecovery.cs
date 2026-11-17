using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class NativeHistoryRecovery
{
    internal sealed record Candidate(string ThreadId, string Model, string RolloutPath, string[] Checkpoints, string[]? ForeignReasoning = null, string[]? ForeignItemIds = null);

    internal static GatewayIdentity ReadIdentity(string home)
    {
        using var auth = JsonDocument.Parse(SecureFiles.ReadPrivateBytes(Path.Combine(home, "auth.json")));
        var root = auth.RootElement;
        var key = JsonValue.String(root, "OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(key)) return new(key, null, true);
        var tokens = root.GetProperty("tokens");
        var token = JsonValue.String(tokens, "access_token");
        var account = JsonValue.String(tokens, "account_id");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(account))
            throw new InvalidDataException("The original OpenAI sign-in is required to recover native history.");
        return new(token, account, false);
    }

    internal static IReadOnlyList<Candidate> Find(string home, string directory, CancellationToken cancellation, string? onlyThread = null, string? targetNativeModel = null)
    {
        if (targetNativeModel != null && (onlyThread == null || ModelRouting.Owned(targetNativeModel)))
            throw new ArgumentException("Native handoff requires one conversation and a native destination model.");
        if (!File.Exists(Path.Combine(home, "state_5.sqlite"))) return Array.Empty<Candidate>();
        var rows = NativeHistoryThreads.Read(home, onlyThread);
        var result = new List<Candidate>();
        var hasStore = File.Exists(Path.Combine(directory, "model-history.vault"));
        GatewayIdentity? identity = hasStore ? ReadIdentity(home) : null;
        GatewayHistoryStore? store = hasStore ? new GatewayHistoryStore(directory) : null;
        string[]? rollouts = null;
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var id = row.Id; var model = targetNativeModel ?? row.Model;
            if (model == null || ModelRouting.Owned(model)) continue;
            var path = Path.GetFullPath(row.Path.Replace("\\\\?\\", "", StringComparison.Ordinal));
            if (targetNativeModel == null && (store == null || !store.HasPortableConversation(GatewayHistoryStore.Conversation(id, identity!))))
            {
                rollouts ??= new[] { Path.Combine(home, "sessions"), Path.Combine(home, "archived_sessions") }.Where(Directory.Exists)
                    .SelectMany(root => Directory.EnumerateFiles(root, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })).ToArray();
                if (!rollouts.Where(file => Path.GetFileName(file).Contains("-" + id, StringComparison.Ordinal))
                    .Any(file => ContainsPortableReference(file, cancellation))) continue;
            }
            var checkpoint = ReadLatest(home, path, id, cancellation);
            if (checkpoint.Tokens.Length == 0 && checkpoint.ForeignReasoning.Length == 0 && checkpoint.ForeignItemIds.Length == 0) continue;
            identity ??= ReadIdentity(home);
            if (checkpoint.Account != identity.AccountId)
                throw new InvalidDataException("A native history checkpoint belongs to a different OpenAI account. Restore that account before changing the connection.");
            store ??= new GatewayHistoryStore(directory);
            var conversation = GatewayHistoryStore.Conversation(id, identity);
            foreach (var token in checkpoint.Tokens)
                if (string.IsNullOrWhiteSpace(store.Read(token, conversation)?.Summary))
                    throw new InvalidDataException("A native chat still requires an unavailable Switch history checkpoint. Restore its original model-history.vault before changing the connection.");
            result.Add(new(id, model, path, checkpoint.Tokens, checkpoint.ForeignReasoning, checkpoint.ForeignItemIds));
        }
        return result;
    }

    private static bool ContainsPortableReference(string path, CancellationToken cancellation)
    {
        var marker = Encoding.UTF8.GetBytes(GatewayHistoryStore.Prefix);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var remaining = file.Length;
        var buffer = new byte[65536 + marker.Length]; var retained = 0;
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = file.Read(buffer, retained, (int)Math.Min(65536, remaining));
            if (count == 0) throw new IOException("The history file changed during recovery preflight.");
            remaining -= count;
            var available = retained + count;
            if (buffer.AsSpan(0, available).IndexOf(marker) >= 0) return true;
            retained = Math.Min(marker.Length - 1, available);
            buffer.AsSpan(available - retained, retained).CopyTo(buffer);
        }
        return false;
    }

    internal static (string? Account, string[] Tokens, string[] ForeignReasoning, string[] ForeignItemIds) ReadLatest(string home, string path, string thread, CancellationToken cancellation)
    {
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var locations = new[] { Path.Combine(home, "sessions"), Path.Combine(home, "archived_sessions") }.Where(Directory.Exists)
            .Select(root => (Alias: Path.GetFullPath(root), Target: Path.GetFullPath(new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? root))).ToArray();
        var roots = locations.Select(root => root.Target).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        long? bound = null;
        var segmentThread = thread;
        string? owner = null; var ownerRead = false;
        var foreignReasoning = new HashSet<string>(StringComparer.Ordinal);
        var foreignItemIds = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            foreach (var location in locations)
                if (path.StartsWith(location.Alias + Path.DirectorySeparatorChar, comparison))
                { path = Path.Combine(location.Target, Path.GetRelativePath(location.Alias, path)); break; }
            var containingRoot = roots.FirstOrDefault(root => path.StartsWith(root + Path.DirectorySeparatorChar, comparison));
            if (containingRoot == null ||
                !Path.GetFileName(path).Contains("-" + segmentThread, StringComparison.Ordinal) || Path.GetExtension(path) != ".jsonl" ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || !visited.Add(path) || visited.Count > 256)
                throw new InvalidDataException("The native history segment chain could not be verified.");
            for (var directory = Path.GetDirectoryName(path)!; !directory.Equals(containingRoot, comparison); directory = Path.GetDirectoryName(directory)!)
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("A native history segment crosses an unconfigured linked directory.");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = bound ?? file.Length;
            if (length < 0 || length > file.Length || length > 512 * 1024 * 1024)
                throw new InvalidDataException("The native history segment boundary is invalid.");
            var bytes = new byte[checked((int)length)]; file.ReadExactly(bytes);
            if (bytes.Length == 0 || bytes[^1] != '\n') throw new InvalidDataException("The native history segment is incomplete.");
            string? account = null, parent = null, model = null; long? parentBound = null; string[]? tokens = null;
            var segmentReasoning = new HashSet<string>(StringComparer.Ordinal);
            var segmentItemIds = new HashSet<string>(StringComparer.Ordinal);
            using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
            var first = true;
            while (reader.ReadLine() is string line)
            {
                cancellation.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(line); var record = document.RootElement;
                var payload = record.GetProperty("payload"); var type = JsonValue.String(record, "type");
                if (first)
                {
                    if (type != "session_meta" || JsonValue.String(payload, "id") != segmentThread || JsonValue.String(payload, "model_provider") != "openai")
                        throw new InvalidDataException("The native history owner could not be verified.");
                    account = JsonValue.String(payload, "creator_account_id"); first = false;
                    if (ownerRead && account != owner) throw new InvalidDataException("The native history segment account changed.");
                    owner = account; ownerRead = true;
                    if (payload.TryGetProperty("history_base", out var historyBase) && historyBase.ValueKind == JsonValueKind.Object)
                    { parent = historyBase.GetProperty("thread_id").GetString(); parentBound = historyBase.GetProperty("end_byte_offset").GetInt64(); }
                }
                if (type == "turn_context") model = JsonValue.String(payload, "model");
                if (type == "response_item" && model?.StartsWith("cs_", StringComparison.Ordinal) == true && NativeReasoningHandoff.HasState(payload))
                    segmentReasoning.Add(NativeReasoningHandoff.Key(payload));
                if (type == "response_item" && model?.StartsWith("cs_", StringComparison.Ordinal) == true && NativeReasoningHandoff.HasOutputId(payload))
                    segmentItemIds.Add(NativeReasoningHandoff.OutputKey(payload));
                if (type == "compacted" && payload.TryGetProperty("replacement_history", out var history) && history.ValueKind == JsonValueKind.Array)
                {
                    segmentReasoning.IntersectWith(history.EnumerateArray().Where(NativeReasoningHandoff.HasState).Select(NativeReasoningHandoff.Key));
                    segmentItemIds.IntersectWith(history.EnumerateArray().Where(NativeReasoningHandoff.HasOutputId).Select(NativeReasoningHandoff.OutputKey));
                    tokens = history.EnumerateArray().Where(item => JsonValue.String(item, "type") is "compaction" or "compaction_summary" or "context_compaction")
                        .Select(item => JsonValue.String(item, "encrypted_content")).Where(value => value?.StartsWith(GatewayHistoryStore.Prefix, StringComparison.Ordinal) == true).Cast<string>().ToArray();
                    // Replacement history is a new native checkpoint. Its visible items
                    // retain their original provenance; compaction does not reissue them.
                }
            }
            foreignReasoning.UnionWith(segmentReasoning);
            foreignItemIds.UnionWith(segmentItemIds);
            if (tokens != null || parent == null) return (account, tokens ?? Array.Empty<string>(), foreignReasoning.OrderBy(value => value, StringComparer.Ordinal).ToArray(), foreignItemIds.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            if (!Guid.TryParse(parent, out _)) throw new InvalidDataException("The native history parent is invalid.");
            var suffix = parent == segmentThread ? "-" + segmentThread + ".jsonl" : "-" + segmentThread + "_" + parent + ".jsonl";
            var candidates = roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*" + suffix,
                new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                .Select(file => (Path: file, Thread: segmentThread)).ToList();
            // A native fork references its parent's ordinary rollout, whereas a rollover
            // segment retains the current conversation ID in both filename and metadata.
            if (parent != segmentThread)
                candidates.AddRange(roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*-" + parent + ".jsonl",
                    new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    .Select(file => (Path: file, Thread: parent)));
            if (candidates.Count != 1) throw new InvalidDataException("The original native history segment is missing or ambiguous.");
            path = candidates[0].Path; segmentThread = candidates[0].Thread; bound = parentBound;
        }
    }

    internal static async Task RecoverAsync(string executable, string home, string directory, ModelProviderStore store,
        Func<string?, string?, GatewayIdentity?> authenticate, HttpClient http, IReadOnlyList<Candidate> candidates, CancellationToken cancellation, string? targetNativeModel = null)
    {
        foreach (var candidate in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            var current = Find(home, directory, cancellation, candidate.ThreadId, targetNativeModel).SingleOrDefault();
            if (current == null) continue;
            if (current.Model != candidate.Model || !current.Checkpoints.SequenceEqual(candidate.Checkpoints) ||
                !(current.ForeignReasoning ?? Array.Empty<string>()).SequenceEqual(candidate.ForeignReasoning ?? Array.Empty<string>()) ||
                !(current.ForeignItemIds ?? Array.Empty<string>()).SequenceEqual(candidate.ForeignItemIds ?? Array.Empty<string>()))
                throw new InvalidOperationException("Native history changed after recovery preflight. No connection settings were changed.");
            await using var gateway = new ModelGateway(store, authenticate, http, directory, () => home, current);
            gateway.Start();
            var overrides = new Dictionary<string, string>
            {
                ["openai_base_url"] = JsonSerializer.Serialize(gateway.BaseUrl),
                ["features.enable_request_compression"] = "false",
                ["features.responses_websockets_v2"] = "false"
            };
            await using (var rpc = new CodexRpc(executable, home, preserveProvider: true, configOverrides: overrides))
            {
                await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
                var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var compacted = false; var turnCompleted = false;
                void Notification(string method, JsonElement value)
                {
                    if (JsonValue.String(value, "threadId") != current.ThreadId) return;
                    if (method == "error") complete.TrySetException(new InvalidOperationException("Native history compaction failed; the original chat remains intact. " + gateway.LastError));
                    if (method == "item/completed" && JsonValue.String(value.GetProperty("item"), "type") == "contextCompaction") compacted = true;
                    if (method == "turn/completed")
                    {
                        if (JsonValue.String(value.GetProperty("turn"), "status") != "completed")
                            complete.TrySetException(new InvalidOperationException("The native recovery turn did not complete. The connection was not changed."));
                        else turnCompleted = true;
                    }
                    if (compacted && turnCompleted) complete.TrySetResult();
                }
                rpc.Notification += Notification;
                try
                {
                    await rpc.RequestAsync("thread/resume", new { threadId = current.ThreadId, model = current.Model, modelProvider = "openai", excludeTurns = true }, cancellation).ConfigureAwait(false);
                    await rpc.RequestAsync("thread/compact/start", new { threadId = current.ThreadId }, cancellation).ConfigureAwait(false);
                    await complete.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellation).ConfigureAwait(false);
                }
                finally { rpc.Notification -= Notification; }
            }
            if (Find(home, directory, cancellation, current.ThreadId, targetNativeModel).Count != 0)
                throw new InvalidOperationException("The native compaction was not durably confirmed. The connection was not changed.");
        }
    }
}
