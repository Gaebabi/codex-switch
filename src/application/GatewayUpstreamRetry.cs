using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

// Retry only an explicit refusal before any successful upstream response.
// A fresh message wraps the same owned input and immutable route on each attempt.
internal static class GatewayUpstreamRetry
{
    internal const int MaximumRetries = 3;
    internal static TimeSpan Delay(int retry) => TimeSpan.FromSeconds(retry switch { 1 => 2, 2 => 5, 3 => 10, _ => throw new ArgumentOutOfRangeException(nameof(retry)) });

    internal static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> createRequest, bool enabled,
        Func<int, TimeSpan, CancellationToken, Task> beforeRetry, CancellationToken cancellation)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var request = createRequest();
            HttpResponseMessage result;
            try { result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false); }
            finally { request.Content?.Dispose(); request.Content = null; }
            if (!enabled || (int)result.StatusCode != 507 || attempt == MaximumRetries) return result;
            result.Dispose();
            await beforeRetry(attempt + 1, Delay(attempt + 1), cancellation).ConfigureAwait(false);
        }
    }
}
