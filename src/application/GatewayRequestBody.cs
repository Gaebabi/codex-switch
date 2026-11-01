using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

// Counts live request-buffer capacity, including both arrays during growth.
// Large uploads leave capacity available for ordinary small requests.
internal sealed class GatewayBodyBudget
{
    internal const long MaximumBytes = 1024L * 1024 * 1024;
    private const long SmallReserve = 64L * 1024 * 1024;
    private readonly object _gate = new();
    private long _bytes;
    internal long Bytes { get { lock (_gate) return _bytes; } }
    internal bool TryReserve(int count, bool large)
    {
        lock (_gate)
        {
            if (_bytes + count > MaximumBytes - (large ? SmallReserve : 0)) return false;
            _bytes += count; return true;
        }
    }
    internal void Release(int count) { lock (_gate) _bytes -= count; }
}

internal sealed class GatewayBusyException : IOException { }

internal sealed class GatewayRequestBody : IDisposable
{
    private readonly GatewayBodyBudget _budget;
    private readonly bool _large;
    private MemoryStream? _stream;
    private int _reserved;
    internal Memory<byte> Bytes => (_stream ?? throw new ObjectDisposedException(nameof(GatewayRequestBody))).GetBuffer().AsMemory(0, checked((int)_stream.Length));
    private GatewayRequestBody(GatewayBodyBudget budget, bool large) { _budget = budget; _large = large; }
    internal static async Task<GatewayRequestBody> ReadAsync(Stream source, long length, int maximum, GatewayBodyBudget budget, bool large, CancellationToken cancellation)
    {
        if (length > maximum) throw new InvalidDataException("The model request is too large.");
        var body = new GatewayRequestBody(budget, large);
        try
        {
            var capacity = length >= 0 ? checked((int)length) : Math.Min(maximum, 64 * 1024);
            if (!budget.TryReserve(capacity, large)) throw new GatewayBusyException();
            body._reserved = capacity;
            body._stream = new MemoryStream(capacity);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - body._stream.Length + 1)), cancellation).ConfigureAwait(false);
                if (count == 0) break;
                var required = checked((int)body._stream.Length + count);
                if (required > maximum) throw new InvalidDataException("The model request is too large.");
                if (required > body._reserved)
                {
                    var next = (int)Math.Min(maximum, Math.Max(required, Math.Max(64 * 1024L, 2L * body._reserved)));
                    // Reserve the entire new array before releasing the old one.
                    if (!budget.TryReserve(next, large)) throw new GatewayBusyException();
                    try { body._stream.Capacity = next; }
                    catch { budget.Release(next); throw; }
                    budget.Release(body._reserved); body._reserved = next;
                }
                body._stream.Write(buffer, 0, count);
            }
            body._stream.Position = 0; return body;
        }
        catch { body.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_stream != null) { _stream.SetLength(0); _stream.Capacity = 0; _stream.Dispose(); _stream = null; }
        _budget.Release(_reserved); _reserved = 0;
    }
}
