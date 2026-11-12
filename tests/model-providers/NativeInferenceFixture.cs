using System.Net;
using System.Net.Sockets;
using System.Text;

// Native inference has its own listener and transport. It never passes through Switch.
internal sealed class NativeInferenceFixture : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly HttpClient transport;
    private readonly Task loop;
    internal Upstreams Upstream { get; } = new();
    internal string BaseUrl { get; }
    internal NativeInferenceFixture()
    {
        transport = new HttpClient(Upstream);
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        BaseUrl = "http://127.0.0.1:" + port + "/v1";
        listener.Prefixes.Add(BaseUrl + "/"); listener.Start(); loop = Run();
    }
    private async Task Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stop.Token);
                if (context.Request.HttpMethod != "POST")
                { context.Response.StatusCode = 405; context.Response.Close(); continue; }
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://chatgpt.com/backend-api/codex/responses")
                { Content = new StringContent(await reader.ReadToEndAsync(stop.Token), Encoding.UTF8, "application/json") };
                request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers["Authorization"]);
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", context.Request.Headers["ChatGPT-Account-ID"]);
                using var result = await transport.SendAsync(request, stop.Token);
                context.Response.StatusCode = (int)result.StatusCode;
                context.Response.ContentType = "text/event-stream";
                var bytes = await result.Content.ReadAsByteArrayAsync(stop.Token);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stop.Token); context.Response.Close();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    { stop.Cancel(); listener.Close(); await loop; transport.Dispose(); stop.Dispose(); }
}
