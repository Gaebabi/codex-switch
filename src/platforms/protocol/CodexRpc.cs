using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class CodexRpc : IAsyncDisposable
{
    private readonly Process _process;
    private readonly OwnedProcessJob _job;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<int, string> _methods = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private readonly Task _errorDrain;
    private int _nextId;
    public event Action<string, JsonElement>? Notification;
    public CodexRpc(string executable, string home, bool preserveProvider = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = home };
        foreach (var argument in new[] { "app-server", "--listen", "stdio://", "-c", "cli_auth_credentials_store=\"file\"", "-c", "analytics.enabled=false" })
            start.ArgumentList.Add(argument);
        if (!preserveProvider) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("model_provider=\"openai\""); }
        start.Environment["CODEX_HOME"] = home;
        start.Environment["RUST_LOG"] = "error";
        start.Environment.Remove("CODEX_ACCESS_TOKEN");
        start.Environment.Remove("OPENAI_API_KEY");
        start.Environment.Remove("OPENAI_IDENTITY_TOKEN_FILE");
        start.Environment.Remove("OPENAI_IDENTITY_TOKEN_AUDIENCE");
        _process = Process.Start(start) ?? throw new IOException("Codex 조회 프로세스를 실행할 수 없습니다.");
        try { _job = new OwnedProcessJob(_process); }
        catch { if (!_process.HasExited) _process.Kill(true); _process.Dispose(); throw; }
        _reader = ReadAsync();
        _errorDrain = DrainErrorsAsync();
    }
    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await RequestAsync("initialize", new { clientInfo = new { name = "codex_account_desk", title = "Codex Switch", version = typeof(CodexRpc).Assembly.GetName().Version!.ToString(3) } }, cancellation);
        await SendAsync(new { method = "initialized" }, cancellation);
    }
    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        _methods[id] = method;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _stop.Token);
        linked.CancelAfter(timeout ?? TimeSpan.FromSeconds(25));
        try
        {
            await SendAsync(new { id, method, @params = parameters ?? new { } }, linked.Token);
            return await completion.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !_stop.IsCancellationRequested)
        { throw new TimeoutException("Codex 응답이 지연되고 있습니다. 잠시 후 새로 고침해 주세요."); }
        finally { _pending.TryRemove(id, out _); _methods.TryRemove(id, out _); }
    }
    private async Task SendAsync(object message, CancellationToken cancellation)
    {
        await _writeGate.WaitAsync(cancellation);
        try { await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellation); await _process.StandardInput.FlushAsync(cancellation); }
        finally { _writeGate.Release(); }
    }
    private async Task ReadAsync()
    {
        Exception? failure = null;
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(_stop.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && _pending.TryGetValue(id.GetInt32(), out var completion))
                {
                    if (root.TryGetProperty("error", out var error)) completion.TrySetException(new CodexRequestException(JsonValue.String(error, "message") ?? "Codex 조회에 실패했습니다.", _methods.TryGetValue(id.GetInt32(), out var operation) ? operation : null));
                    else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                    else completion.TrySetException(new InvalidDataException("Codex 응답에 결과가 없습니다."));
                }
                else if (JsonValue.String(root, "method") is string method && root.TryGetProperty("params", out var parameters)) Notification?.Invoke(method, parameters.Clone());
            }
            failure = new IOException("Codex 조회 프로세스가 종료됐습니다.");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { failure = new OperationCanceledException(); }
        catch (Exception error) { failure = error; }
        finally { foreach (var completion in _pending.Values) completion.TrySetException(failure ?? new IOException("Codex 연결이 끊겼습니다.")); }
    }
    private async Task DrainErrorsAsync()
    {
        try { while (await _process.StandardError.ReadLineAsync(_stop.Token) != null) { } }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (!_process.HasExited)
        {
            _process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!_process.HasExited) _process.Kill(true); await _process.WaitForExitAsync(); }
        }
        await Task.WhenAll(_reader, _errorDrain);
        _job.Dispose();
        _process.Dispose();
        _writeGate.Dispose();
        _stop.Dispose();
    }
}

internal sealed class CodexRequestException : Exception
{
    public string? Operation { get; }
    public CodexRequestException(string message, string? operation = null) : base(message) { Operation = operation; }
}
