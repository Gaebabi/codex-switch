using System;
using System.Diagnostics;
using System.Threading;

namespace CodexAccountDesk;

internal sealed class FolderScanBudget
{
    internal const int QueriesPerSecond = 32;
    internal const int RestMultiplier = 4;
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(1.0 / QueriesPerSecond);
    private readonly Func<TimeSpan> _now;
    private readonly Action<TimeSpan, CancellationToken> _wait;
    private TimeSpan _next;
    private long _queries;
    internal long Queries => Interlocked.Read(ref _queries);

    internal FolderScanBudget(Func<TimeSpan>? now = null, Action<TimeSpan, CancellationToken>? wait = null)
    {
        var clock = Stopwatch.StartNew();
        _now = now ?? (() => clock.Elapsed);
        _wait = wait ?? ((delay, cancellation) =>
        {
            cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(delay.TotalMilliseconds))));
            cancellation.ThrowIfCancellationRequested();
        });
    }

    internal T Query<T>(Func<T> query, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        while (true)
        {
            var remaining = _next - _now();
            if (remaining <= TimeSpan.Zero) break;
            _wait(remaining, cancellation);
            cancellation.ThrowIfCancellationRequested();
        }
        var started = _now();
        Interlocked.Increment(ref _queries);
        try { return query(); }
        finally
        {
            var finished = _now(); var elapsed = finished - started;
            var rest = elapsed * RestMultiplier;
            if (Spacing - elapsed > rest) rest = Spacing - elapsed;
            _next = finished + rest;
        }
    }
}
