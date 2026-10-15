using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record DesktopQueueCount(int Total, int Paused);
internal sealed record DesktopQueueMessage(string Id, string? ClientId = null, bool Paused = false);
internal sealed record DesktopQueueSnapshot(Dictionary<string, DesktopQueueMessage[]> Saved, Dictionary<string, DesktopQueueMessage[]> Server);

internal static class DesktopQueueState
{
    public static async Task<DesktopQueueSnapshot> ReadAsync(string home, CancellationToken cancellation)
    {
        await using var file = new FileStream(Path.Combine(home, ".codex-global-state.json"), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var state = await JsonDocument.ParseAsync(file, cancellationToken: cancellation);
        var saved = Parse(state.RootElement);
        var server = ServerQueueStore.Read(home);
        cancellation.ThrowIfCancellationRequested();
        return new(saved, server);
    }
    public static Dictionary<string, DesktopQueueMessage[]> Parse(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid desktop global state.");
        var result = new Dictionary<string, DesktopQueueMessage[]>();
        if (!state.TryGetProperty("queued-follow-ups", out var queues)) return result;
        if (queues.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid desktop queue state.");
        foreach (var queue in queues.EnumerateObject())
        {
            if (!Guid.TryParse(queue.Name, out _)) throw new InvalidDataException("Invalid queued thread ID.");
            result.Add(queue.Name, Messages(queue.Value));
        }
        return result;
    }
    public static DesktopQueueMessage[] Messages(JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid queued messages.");
        var result = new Dictionary<string, DesktopQueueMessage>(StringComparer.Ordinal);
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid queued message.");
            var id = JsonValue.String(message, "id");
            if (string.IsNullOrEmpty(id)) throw new InvalidDataException("Queued message ID missing.");
            var host = ReadHost(message, "submission", "hostId");
            var executionHost = ReadHost(message, "submissionOptions", "executionHostId");
            if (host is not (null or "local") || executionHost is not (null or "local")) continue;
            var paused = JsonValue.String(message, "pausedReason") is { Length: > 0 } || result.GetValueOrDefault(id)?.Paused == true;
            result[id] = new(id, JsonValue.String(message, "clientUserMessageId"), paused);
        }
        return result.Values.ToArray();
    }
    public static DesktopQueueCount Count(JsonElement messages) => Count(Messages(messages), Array.Empty<DesktopQueueMessage>());
    public static DesktopQueueCount Count(DesktopQueueMessage[] saved, DesktopQueueMessage[] server, bool serverPaused = false)
    {
        var submissions = server.DistinctBy(m => m.Id).ToArray();
        var ids = submissions.Select(m => m.Id).Concat(submissions.Where(m => m.ClientId != null).Select(m => m.ClientId!)).ToHashSet(StringComparer.Ordinal);
        var local = saved.DistinctBy(m => m.Id).Where(m => !ids.Contains(m.Id) && (m.ClientId == null || !ids.Contains(m.ClientId))).ToArray();
        return new(submissions.Length + local.Length, local.Count(m => m.Paused) + (serverPaused ? submissions.Length : submissions.Count(m => m.Paused)));
    }
    public static bool Same(DesktopQueueMessage[] a, DesktopQueueMessage[] b) =>
        a.OrderBy(m => m.Id, StringComparer.Ordinal).SequenceEqual(b.OrderBy(m => m.Id, StringComparer.Ordinal));
    private static string? ReadHost(JsonElement message, string property, string key)
    {
        if (!message.TryGetProperty(property, out var metadata) || metadata.ValueKind == JsonValueKind.Null) return null;
        if (metadata.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid queued host metadata.");
        if (!metadata.TryGetProperty(key, out var host) || host.ValueKind == JsonValueKind.Null) return null;
        if (host.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(host.GetString())) throw new InvalidDataException("Invalid queued host.");
        return host.GetString();
    }
}
