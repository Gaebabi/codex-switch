using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class DesktopSessionMonitor : IAsyncDisposable
{
    private readonly string _home;
    private readonly CancellationTokenSource _stop = new();
    private readonly SessionActivityLedger _ledger = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _requests = new();
    private Task? _run;
    private Stream? _pipe;
    private readonly object _connectionSync = new();
    private readonly Channel<bool> _queueChanges = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private CancellationTokenSource? _connection;
    private SessionActivity _current = new(false, 0, 0, 0);
    public event Action<SessionActivity>? Changed;
    public SessionActivity Current => Volatile.Read(ref _current);
    public DesktopSessionMonitor(string home) { _home = home; }
    public void Start() { _run ??= Task.Run(RunAsync); }
    public DesktopThreadState[] Threads => _ledger.Threads();
    public void RestartDiscovery()
    {
        _ledger.Reset(); Publish(_ledger.Read());
        lock (_connectionSync) _connection?.Cancel();
    }
    public async Task WaitForDiscoveryAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { while (!Current.Available) await Task.Delay(150, timeout.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("진행 중인 채팅 상태를 확인하지 못했습니다."); }
    }
    public async Task<DesktopThreadState> WaitForThreadAsync(string id, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var thread = Threads.FirstOrDefault(t => t.Id == id);
                if (thread?.ResumeState == "resumed" && thread.Status is "idle" or "active") return thread;
                await Task.Delay(150, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Codex에서 채팅을 다시 열지 못했습니다."); }
    }
    public async Task<JsonElement> SendToOwnerAsync(DesktopThreadState thread, string method, object parameters, int version, CancellationToken cancellation)
    {
        var pipe = Volatile.Read(ref _pipe) ?? throw new IOException("Codex 연결 끊김");
        var response = await RequestAsync(pipe, method, parameters, version, cancellation, thread.Owner, TimeSpan.FromSeconds(25));
        if (JsonValue.String(response, "resultType") != "success") throw new CodexRequestException(JsonValue.String(response, "error") ?? "Codex 요청에 실패했습니다.", method);
        if (JsonValue.String(response, "handledByClientId") != thread.Owner) throw new InvalidDataException("Codex 채팅 소유자가 변경됐습니다.");
        var result = response.GetProperty("result");
        if (result.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Codex 채팅 응답 형식이 올바르지 않습니다.");
        return result.Clone();
    }
    private void Publish(SessionActivity activity)
    {
        if (activity == Current) return;
        Volatile.Write(ref _current, activity); Changed?.Invoke(activity);
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _ledger.Reset(); Publish(_ledger.Read());
                try { await ObserveAsync(_stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { continue; }
                catch (TimeoutException) { Publish(new(false, 0, 0, 0, "Codex 연결 없음")); }
                catch (InvalidDataException) { Publish(new(false, 0, 0, 0, "Codex 상태 형식 변경")); }
                catch (UnauthorizedAccessException) { Publish(new(false, 0, 0, 0, "Codex 상태 접근 거절")); }
                catch (IOException) { Publish(new(false, 0, 0, 0, "Codex 연결 끊김")); }
                await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ObserveAsync(CancellationToken cancellation)
    {
        await using var pipe = await ConnectAsync(cancellation);
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lock (_connectionSync) _connection = connection;
        var reader = ReadAsync(pipe, connection.Token);
        try
        {
            await RequestAsync(pipe, "initialize", new { clientType = "codex-account-desk" }, 0, connection.Token);
            Volatile.Write(ref _pipe, pipe);
            using var watcher = new FileSystemWatcher(_home) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, change) => { if (IsQueueFile(change.Name)) _queueChanges.Writer.TryWrite(true); };
            watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed;
            watcher.Renamed += (_, change) => { if (IsQueueFile(change.Name) || IsQueueFile(change.OldName)) _queueChanges.Writer.TryWrite(true); };
            watcher.EnableRaisingEvents = true;
            var known = new HashSet<string>();
            var subscribed = new HashSet<string>();
            while (!connection.IsCancellationRequested)
            {
                if (reader.IsCompleted) { await reader; throw new EndOfStreamException(); }
                await ReadQueuesAsync(connection.Token);
                var directory = Path.Combine(_home, "thread-writer-locks");
                var ids = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.lock").Select(Path.GetFileNameWithoutExtension)
                    .Where(id => Guid.TryParse(id, out _)).Select(id => id!).ToHashSet() : new HashSet<string>();
                foreach (var id in known.Except(ids).ToArray())
                {
                    if (subscribed.Remove(id)) await FollowAsync(pipe, id, false, connection.Token);
                    _ledger.Remove(id); known.Remove(id);
                }
                foreach (var id in ids.Except(known).ToArray())
                {
                    var owner = await RequestAsync(pipe, "thread-owner-discovery", new { hostId = "local", conversationId = id }, 1, connection.Token);
                    if (JsonValue.String(owner, "resultType") == "error")
                    {
                        if (JsonValue.String(owner, "error") != "no-client-found") throw new InvalidDataException("Desktop owner discovery failed.");
                        continue;
                    }
                    var ownerId = JsonValue.String(owner, "handledByClientId") ?? throw new InvalidDataException("Desktop owner missing.");
                    _ledger.Discover(id, ownerId); subscribed.Add(id); known.Add(id);
                    await FollowAsync(pipe, id, true, connection.Token);
                }
                _ledger.CompleteDiscovery(); Publish(_ledger.Read());
                await WaitForQueueChangeAsync(connection.Token);
            }
        }
        finally
        {
            lock (_connectionSync) _connection = null;
            Volatile.Write(ref _pipe, null);
            connection.Cancel();
            pipe.Dispose();
            try { await reader; }
            catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (connection.IsCancellationRequested) { }
            foreach (var request in _requests.Values) request.TrySetCanceled();
            _requests.Clear();
        }
    }
    private async Task ReadQueuesAsync(CancellationToken cancellation)
    {
        var sequence = _ledger.BeginQueueRead();
        try
        {
            _ledger.SetQueues(await DesktopQueueState.ReadAsync(_home, cancellation), sequence);
        }
        catch (JsonException) { _ledger.SetQueues(null, sequence, "큐 상태 형식 변경"); }
        catch (InvalidDataException) { _ledger.SetQueues(null, sequence, "큐 상태 형식 변경"); }
        catch (InvalidOperationException) { _ledger.SetQueues(null, sequence, "큐 상태 형식 변경"); }
        catch (UnauthorizedAccessException) { _ledger.SetQueues(null, sequence, "큐 상태 접근 거절"); }
        catch (IOException) { _ledger.SetQueues(null, sequence, "큐 상태 조회 불가"); }
    }
    private async Task<Stream> ConnectAsync(CancellationToken cancellation)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", "codex-ipc", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(1500, cancellation); return pipe; }
            catch { await pipe.DisposeAsync(); throw; }
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(1500);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(_home, "ipc", "ipc.sock")), timeout.Token);
            if (OperatingSystem.IsMacOS()) MacDesktop.VerifyPeer(socket);
            return new NetworkStream(socket, true);
        }
        catch (SocketException error) { socket.Dispose(); throw new IOException("Codex 연결 없음", error); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { socket.Dispose(); throw new TimeoutException("Codex 연결 없음"); }
        catch { socket.Dispose(); throw; }
    }
    private static bool IsQueueFile(string? name) => name == ".codex-global-state.json" || name?.StartsWith("queue_", StringComparison.Ordinal) == true &&
        (name.EndsWith(".sqlite", StringComparison.Ordinal) || name.EndsWith(".sqlite-wal", StringComparison.Ordinal) || name.EndsWith(".sqlite-journal", StringComparison.Ordinal));
    private async Task WaitForQueueChangeAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _queueChanges.Reader.ReadAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        while (_queueChanges.Reader.TryRead(out _)) { }
    }
    private async Task<JsonElement> RequestAsync(Stream pipe, string method, object parameters, int version, CancellationToken cancellation,
        string? targetClientId = null, TimeSpan? timeout = null)
    {
        var id = Guid.NewGuid().ToString();
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests[id] = response;
        try
        {
            var duration = timeout ?? TimeSpan.FromSeconds(5);
            var message = new Dictionary<string, object> { ["type"] = "request", ["requestId"] = id, ["method"] = method,
                ["params"] = parameters, ["version"] = version, ["timeoutMs"] = (int)duration.TotalMilliseconds };
            if (targetClientId != null) message["targetClientId"] = targetClientId;
            await SendAsync(pipe, message, cancellation);
            return await response.Task.WaitAsync(duration, cancellation);
        }
        finally { _requests.TryRemove(id, out _); }
    }
    private Task FollowAsync(Stream pipe, string id, bool following, CancellationToken cancellation) =>
        SendAsync(pipe, new { type = "broadcast", method = "thread-stream-following-changed", version = 1,
            @params = new { hostId = "local", conversationId = id, following } }, cancellation);
    private async Task SendAsync(Stream pipe, object message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var frame = new byte[4 + bytes.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length); bytes.CopyTo(frame, 4);
        await _writeGate.WaitAsync(cancellation);
        try { await pipe.WriteAsync(frame, cancellation); await pipe.FlushAsync(cancellation); }
        finally { _writeGate.Release(); }
    }
    private async Task ReadAsync(Stream pipe, CancellationToken cancellation)
    {
        try
        {
            var header = new byte[4];
            while (!cancellation.IsCancellationRequested)
            {
                await pipe.ReadExactlyAsync(header, cancellation);
                var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
                if (size <= 0 || size > 268435456) throw new InvalidDataException("Invalid desktop frame length.");
                var body = new byte[size]; await pipe.ReadExactlyAsync(body, cancellation);
                using var document = JsonDocument.Parse(body);
                var message = document.RootElement;
                var type = JsonValue.String(message, "type");
                var id = JsonValue.String(message, "requestId");
                if (type == "response" && id != null && _requests.TryGetValue(id, out var request)) request.TrySetResult(message.Clone());
                else if (type == "client-discovery-request")
                    await SendAsync(pipe, new { type = "client-discovery-response", requestId = id, response = new { canHandle = false } }, cancellation);
                else if (type == "broadcast")
                {
                    var method = JsonValue.String(message, "method");
                    var parameters = message.GetProperty("params");
                    if (method == "client-status-changed" && JsonValue.String(parameters, "status") == "disconnected" &&
                        _ledger.OwnsClient(JsonValue.String(parameters, "clientId"))) throw new IOException("Desktop owner disconnected.");
                    if (method == "thread-queued-followups-changed" && JsonValue.String(parameters, "hostId") == "local")
                    {
                        if (JsonValue.Int(message, "version") != 2) { _ledger.SetQueues(null, _ledger.BeginQueueRead(), "큐 상태 형식 변경"); Publish(_ledger.Read()); continue; }
                        _ledger.ApplyQueue(parameters.GetProperty("conversationId").GetString()!, JsonValue.String(message, "sourceClientId")!, parameters.GetProperty("messages"));
                        _queueChanges.Writer.TryWrite(true);
                        Publish(_ledger.Read()); continue;
                    }
                    if (method != "thread-stream-state-changed" || JsonValue.String(parameters, "hostId") != "local") continue;
                    if (JsonValue.Int(message, "version") != 11) throw new InvalidDataException("Unsupported desktop protocol version.");
                    var threadId = JsonValue.String(parameters, "conversationId") ?? throw new InvalidDataException("Desktop thread ID missing.");
                    var owner = JsonValue.String(message, "sourceClientId") ?? throw new InvalidDataException("Desktop owner missing.");
                    if (_ledger.Apply(threadId, owner, parameters.GetProperty("change")))
                    {
                        await FollowAsync(pipe, threadId, false, cancellation);
                        await FollowAsync(pipe, threadId, true, cancellation);
                    }
                    Publish(_ledger.Read());
                }
            }
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid desktop state JSON.", error); }
        catch (KeyNotFoundException error) { throw new InvalidDataException("Required desktop status field missing.", error); }
        catch (InvalidOperationException error) { throw new InvalidDataException("Invalid desktop status field type.", error); }
        finally { foreach (var request in _requests.Values) request.TrySetException(new IOException("Desktop connection closed.")); }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_run != null) await _run;
        _writeGate.Dispose(); _stop.Dispose();
    }
}
