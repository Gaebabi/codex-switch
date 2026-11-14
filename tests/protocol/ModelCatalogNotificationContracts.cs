using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

internal static class ModelCatalogNotificationContracts
{
    internal static async Task RunAsync(List<string> checks)
    {
        var absent = Path.Combine(Path.GetTempPath(), "codex-model-notify-contract-" + Guid.NewGuid().ToString("N"));
        await using var monitor = new DesktopSessionMonitor(absent);
        await monitor.NotifyModelCatalogChangedAsync(CancellationToken.None);
        Assert(!Directory.Exists(absent) && !monitor.Current.Available);
        checks.Add("Absent desktop notification is a no-op without creating a home or starting discovery");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await RejectedAsync<OperationCanceledException>(() => monitor.NotifyModelCatalogChangedAsync(cancelled.Token));
        }
        checks.Add("Cancelled model refresh is not reported as a completed notification");

        var pipeName = "CodexSwitch.ModelNotification.Contract." + Guid.NewGuid().ToString("N");
        await using var peer = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var connection = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(peer.WaitForConnectionAsync(deadline.Token), connection.ConnectAsync(deadline.Token));
        var pipeField = typeof(DesktopSessionMonitor).GetField("_pipe", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Desktop notification transport was not found.");
        pipeField.SetValue(monitor, connection);
        try
        {
            var read = ReadFrameAsync(peer, deadline.Token);
            await monitor.NotifyModelCatalogChangedAsync(deadline.Token);
            using var frame = await read;
            var message = frame.RootElement;
            Assert(message.EnumerateObject().Count() == 4 && message.GetProperty("type").GetString() == "broadcast" &&
                message.GetProperty("method").GetString() == "query-cache-invalidate" && message.GetProperty("version").GetInt32() == 0);
            var parameters = message.GetProperty("params");
            Assert(parameters.EnumerateObject().Count() == 1 && parameters.GetProperty("queryKey").EnumerateArray()
                .Select(value => value.GetString()).SequenceEqual(new[] { "models", "list", "local" }));
            checks.Add("Actual IPC frame invalidates only the local model-list query without a request, restart or thread mutation");

            await connection.DisposeAsync();
            await RejectedAsync<ObjectDisposedException>(() => monitor.NotifyModelCatalogChangedAsync(deadline.Token));
            checks.Add("A failed connected transport surfaces instead of reporting model refresh success");
        }
        finally { pipeField.SetValue(monitor, null); }
    }

    private static async Task<JsonDocument> ReadFrameAsync(Stream peer, CancellationToken cancellation)
    {
        var header = new byte[4];
        await peer.ReadExactlyAsync(header, cancellation);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert(length > 0 && length < 1024);
        var payload = new byte[length];
        await peer.ReadExactlyAsync(payload, cancellation);
        return JsonDocument.Parse(payload);
    }

    private static async Task RejectedAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected model notification failure was not observed.");
    }

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Model catalog notification contract failed.");
    }
}
