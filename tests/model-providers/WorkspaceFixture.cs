using System.Net;
using System.Net.Sockets;
using System.Text;

// Mirrors the public Codex workspace-routing discovery fixture, on a separate
// origin from inference. No real credential, policy endpoint or TLS bypass is used.
internal sealed class WorkspaceFixture : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _task;
    internal string BaseUrl { get; }
    internal WorkspaceFixture()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        BaseUrl = "http://127.0.0.1:" + port + "/backend-api/";
        _listener.Prefixes.Add(BaseUrl); _listener.Start(); _task = RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                var body = context.Request.Url!.AbsolutePath switch
                {
                    "/backend-api/wham/accounts/check" => "{\"accounts\":[{\"id\":\"fictional-account\",\"workspace_backend_origin\":\"https://chatgpt.com\",\"account_routing_override\":\"NO_CONSTRAINT\"}],\"default_account_id\":\"fictional-account\"}",
                    "/backend-api/wham/config/bundle" => "{}",
                    _ => "{}"
                };
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Close(); await _task; _stop.Dispose(); }
}
