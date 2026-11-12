using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed partial class ModelGateway
{
    private static async Task WritePortableCompactionAsync(HttpListenerResponse response, string checkpoint, string model, bool compactEndpoint, CancellationToken cancellation)
    {
        var id = "cs_history_" + Guid.NewGuid().ToString("N");
        var item = new { id = "cmp_" + Guid.NewGuid().ToString("N"), type = "compaction", encrypted_content = checkpoint };
        response.StatusCode = 200; response.Headers["Cache-Control"] = "no-store";
        if (compactEndpoint)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, @object = "response.compaction", created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), output = new[] { item } });
            response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); return;
        }
        response.ContentType = "text/event-stream"; response.SendChunked = true;
        // The current Codex compaction collector requires exactly one compaction output.
        // This is a local checkpoint result, never a fabricated assistant answer or token bill.
        async Task Event(object value)
        { var bytes = Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(value) + "\n\n"); await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false); }
        await Event(new { type = "response.created", response = new { id, status = "in_progress", model, output = Array.Empty<object>() } }).ConfigureAwait(false);
        await Event(new { type = "response.output_item.added", output_index = 0, item }).ConfigureAwait(false);
        await Event(new { type = "response.output_item.done", output_index = 0, item }).ConfigureAwait(false);
        await Event(new { type = "response.completed", response = new { id, @object = "response", status = "completed", model, output = new[] { item } } }).ConfigureAwait(false);
        await response.OutputStream.FlushAsync(cancellation).ConfigureAwait(false);
    }
}
