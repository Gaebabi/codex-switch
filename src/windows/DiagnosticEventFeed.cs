using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexSwitch.Diagnostics;

namespace CodexAccountDesk;

internal sealed record DiagnosticEventLine(DateTimeOffset At, string Label, string Details, string? Thread);

internal sealed class DiagnosticEventFeed(RelayInstallation installation, DiagnosticStatistics statistics)
{
    private sealed class Cursor
    {
        internal long Offset; internal readonly MemoryStream Pending = new(); internal bool Oversized;
    }
    private readonly Dictionary<string, Cursor> _cursors = new();
    private readonly Dictionary<string, (long Size, long Written)> _rotations = new();
    internal bool Loading { get; private set; }
    internal int InvalidRecords { get; private set; }
    internal readonly HashSet<string> ReadySources = new(StringComparer.Ordinal);
    private readonly object _eventGate = new();
    private readonly List<DiagnosticEventLine> _recentEvents = new();
    internal IReadOnlyList<DiagnosticEventLine> RecentEvents { get { lock (_eventGate) return _recentEvents.ToArray(); } }
    private void Summarize(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("at", out var at) || !at.TryGetDateTimeOffset(out var date)) return;
        string? Text(string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var kind = Text("kind"); var method = Text("method"); var type = Text("type");
        var visible = kind is "turn_summary" or "transport_failure" || kind == "headers" || kind == "native_protocol" && method is "turn/started" or "item/completed" || kind is "response_metadata" or "partial_metadata" && type is "response.completed" or "response.failed" or "error";
        if (!visible) return;
        var label = kind == "native_protocol" ? Text("itemType") ?? method! : type ?? kind!;
        if (data.TryGetProperty("status", out var status) && status.ValueKind != JsonValueKind.Null) label += " · " + status.ToString();
        foreach (var key in new[] { "itemMs", "elapsedMs" })
            if (data.TryGetProperty(key, out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetDouble(out var ms)) { label += " · " + DiagnosticStatisticsView.Duration(ms); break; }
        var details = new List<string> { date.ToLocalTime().ToString("G", Loc.Culture), label };
        foreach (var key in new[] { "thread", "turn", "itemId", "id", "model", "serviceTier", "errorCode", "errorType", "retryAfter" })
            if (Text(key) is string value) details.Add(key + ": " + value);
        lock (_eventGate)
        {
            _recentEvents.Add(new(date, label, string.Join("\n", details), Text("thread")));
            if (_recentEvents.Count > 60) _recentEvents.RemoveRange(0, _recentEvents.Count - 60);
        }
    }
    internal async Task Read(CancellationToken cancellation)
    {
        var budget = 4 * 1024 * 1024;
        Loading = false;
        var buffer = new byte[65536];
        foreach (var current in installation.EventFiles())
        {
            var previous = current + ".previous";
            if (File.Exists(previous))
            {
                var info = new FileInfo(previous); var signature = (info.Length, info.LastWriteTimeUtc.Ticks);
                if (!_rotations.TryGetValue(current, out var oldSignature) || oldSignature != signature)
                {
                    if (_cursors.TryGetValue(current, out var moved)) { _cursors[previous] = moved; _cursors[current] = new(); }
                    else _cursors[previous] = new();
                    _rotations[current] = signature;
                }
            }
            if (File.Exists(previous) && !await ReadFile(previous, current).ConfigureAwait(false) ||
                !await ReadFile(current, current).ConfigureAwait(false))
            {
                Loading = true;
                break;
            }
            ReadySources.Add(current);
        }

        async Task<bool> ReadFile(string path, string source)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Diagnostic event links are not supported.");
            if (!_cursors.TryGetValue(path, out var cursor)) _cursors[path] = cursor = new();
            if (cursor.Offset == new FileInfo(path).Length) return true;
            if (budget <= 0) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
            if (file.Length < cursor.Offset) { cursor.Offset = 0; cursor.Pending.SetLength(0); cursor.Oversized = false; }
            file.Position = cursor.Offset;
            int count;
            while (budget > 0 && (count = await file.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, budget)), cancellation).ConfigureAwait(false)) != 0)
            {
                cursor.Offset += count; budget -= count;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == 10)
                    {
                        if (!cursor.Oversized && cursor.Pending.Length > 0)
                        {
                            try
                            {
                                var line = cursor.Pending.GetBuffer().AsMemory(0, (int)cursor.Pending.Length);
                                if (!TransportOnly(line))
                                {
                                    using var document = JsonDocument.Parse(line);
                                    statistics.Apply(source, document.RootElement);
                                    Summarize(document.RootElement);
                                }
                            }
                            catch (JsonException) { InvalidRecords++; }
                        }
                        else if (cursor.Oversized) InvalidRecords++;
                        cursor.Pending.SetLength(0); cursor.Oversized = false;
                    }
                    else if (!cursor.Oversized && cursor.Pending.Length < 1024 * 1024) cursor.Pending.WriteByte(buffer[i]); else cursor.Oversized = true;
                }
            }
            return cursor.Offset >= file.Length;
        }
    }

    private static bool TransportOnly(ReadOnlyMemory<byte> line)
    {
        var reader = new Utf8JsonReader(line.Span);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals("data"u8)) continue;
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject ||
                !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("kind"u8) ||
                !reader.Read() || reader.TokenType != JsonTokenType.String) return false;
            return reader.ValueTextEquals("network_phase"u8) || reader.ValueTextEquals("websocket_message"u8) ||
                reader.ValueTextEquals("protocol_pipe_write"u8) || reader.ValueTextEquals("upload"u8) ||
                reader.ValueTextEquals("download"u8) || reader.ValueTextEquals("first_body_byte"u8);
        }
        return false;
    }
}
