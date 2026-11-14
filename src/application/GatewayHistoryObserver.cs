using System;
using System.IO;
using System.Text.Json;

namespace CodexAccountDesk;

// Observe only confirmed opaque outputs while forwarding the original stream bytes unchanged.
internal sealed class GatewayHistoryObserver : IDisposable
{
    private readonly GatewayHistoryBridge _bridge;
    private readonly string? _session;
    private readonly GatewayIdentity _identity;
    private readonly GatewayHistorySource _source;
    private readonly bool _events;
    private readonly MemoryStream _line = new();
    private bool _oversized;
    internal GatewayHistoryObserver(GatewayHistoryBridge bridge, string? session, GatewayIdentity identity, GatewayHistorySource source, string? contentType)
    { _bridge = bridge; _session = session; _identity = identity; _source = source; _events = contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true; }
    internal void Observe(ReadOnlySpan<byte> bytes)
    {
        if (!_events) return;
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                if (!_oversized && _line.Length > 6)
                {
                    var line = _line.GetBuffer().AsSpan(0, (int)_line.Length);
                    if (line.StartsWith("data: "u8) && !line[6..].StartsWith("[DONE]"u8))
                    {
                        using var document = JsonDocument.Parse(_line.GetBuffer().AsMemory(6, (int)_line.Length - 6));
                        var root = document.RootElement;
                        if (JsonValue.String(root, "type") == "response.output_item.done" && root.TryGetProperty("item", out var item))
                            _bridge.RememberOutput(_session, _identity, _source, item);
                    }
                }
                _line.SetLength(0); _oversized = false;
            }
            else if (!_oversized)
            {
                if (_line.Length >= 4 * 1024 * 1024) { _line.SetLength(0); _oversized = true; }
                else _line.WriteByte(value);
            }
        }
    }
    public void Dispose() => _line.Dispose();
}
