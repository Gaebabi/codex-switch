using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class DesktopBackendHost : IAsyncDisposable
{
    internal sealed record Configuration(string NativeExecutable, string Home, string Directory);
    private sealed class Session
    {
        internal string? Model, Revision;
        internal bool Custom, Ephemeral;
        internal JsonObject Settings = new();
    }
    private sealed class RpcFailure(JsonObject error) : Exception(error["message"]?.GetValue<string>())
    { internal JsonObject Error { get; } = error; }
    private readonly Configuration _config;
    private readonly Process _native;
    private readonly OwnedProcessJob _nativeJob;
    private readonly SemaphoreSlim _nativeWrite = new(1, 1), _clientWrite = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _active = new(), _handoffs = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _requestGates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonArray> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pageRevisions = new(StringComparer.Ordinal);
    private readonly HttpClient _http = ProviderCatalog.CreateHttpClient();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _prefix = "switch:" + Guid.NewGuid().ToString("N") + ":";
    private int _sequence;
    private ModelGateway? _gateway;
    private readonly object _gatewayGate = new();

    internal static async Task<int> RunAsync(string[] arguments)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("CODEX_SWITCH_BACKEND_CONFIG") ?? Path.Combine(AppContext.BaseDirectory, "CodexSwitch.Backend.json");
            if (arguments.Length == 4 && arguments[0] == "--configure")
            {
                var config = new Configuration(Path.GetFullPath(arguments[3]), Path.GetFullPath(arguments[2]), Path.GetFullPath(arguments[1]));
                if (!File.Exists(config.NativeExecutable) || Path.GetFileName(config.NativeExecutable).Equals("CodexSwitch.Backend.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Select the original native Codex executable.");
                await using var manager = new ModelProviderManager(config.Directory, (_, _) => null, codexHome: config.Home);
                await manager.EnableDesktopBackendAsync(config.NativeExecutable, CancellationToken.None).ConfigureAwait(false);
                SecureFiles.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(config));
                return 0;
            }
            var configuration = JsonSerializer.Deserialize<Configuration>(SecureFiles.ReadPrivateBytes(path)) ?? throw new InvalidDataException("The desktop backend configuration is empty.");
            await using var host = new DesktopBackendHost(configuration, arguments);
            return await host.RunAsync(arguments.Contains("app-server", StringComparer.Ordinal)).ConfigureAwait(false);
        }
        catch (Exception error) { await Console.Error.WriteLineAsync("Codex Switch backend: " + error.Message).ConfigureAwait(false); return 1; }
    }
    private DesktopBackendHost(Configuration config, string[] arguments)
    {
        _config = config;
        var start = new ProcessStartInfo(config.NativeExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["CODEX_HOME"] = config.Home;
        start.Environment.Remove("CODEX_CLI_PATH");
        if (arguments.Contains("app-server", StringComparer.Ordinal))
        { start.ArgumentList.Add("-c"); start.ArgumentList.Add("thread_unload_delay_secs=0"); }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        _native = Process.Start(start) ?? throw new IOException("The native Codex backend did not start.");
        try { _nativeJob = new OwnedProcessJob(_native); }
        catch { if (!_native.HasExited) _native.Kill(true); _native.Dispose(); throw; }
    }
    private async Task<int> RunAsync(bool appServer)
    {
        var errors = _native.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        if (!appServer)
        {
            var output = _native.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
            _ = Console.OpenStandardInput().CopyToAsync(_native.StandardInput.BaseStream);
            await _native.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            return _native.ExitCode;
        }
        var queue = Channel.CreateUnbounded<JsonObject>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var outputPump = ReadNativeAsync();
        var worker = ProcessRequestsAsync(queue.Reader);
        try
        {
            while (await Console.In.ReadLineAsync(_stop.Token).ConfigureAwait(false) is string line)
            {
                var message = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Invalid JSON-RPC input.");
                if (message["method"] != null && message["id"] != null) await queue.Writer.WriteAsync(message, _stop.Token).ConfigureAwait(false);
                else await WriteNativeAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            queue.Writer.TryComplete();
            _stop.Cancel();
            foreach (var waiter in _pending.Values) waiter.TrySetCanceled();
            await worker.ConfigureAwait(false);
            _native.StandardInput.Close();
        }
        await outputPump.ConfigureAwait(false);
        await _native.WaitForExitAsync().ConfigureAwait(false);
        await errors.ConfigureAwait(false);
        return _native.ExitCode;
    }
    private async Task ProcessRequestsAsync(ChannelReader<JsonObject> requests)
    {
        var active = new List<Task>();
        await foreach (var message in requests.ReadAllAsync().ConfigureAwait(false))
        {
            active.RemoveAll(task => task.IsCompletedSuccessfully);
            active.Add(ProcessRequestAsync(message));
        }
        await Task.WhenAll(active).ConfigureAwait(false);
    }
    private async Task ProcessRequestAsync(JsonObject message)
    {
        var method = message["method"]!.GetValue<string>();
        var parameters = message["params"] as JsonObject ?? new();
        var key = method == "turn/interrupt" ? "interrupt" : parameters["threadId"]?.GetValue<string>() ?? "global";
        var gate = _requestGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? catalogReceipt = null;
            JsonObject reply;
            try
            {
                JsonNode? result;
                if (method == "model/list") (result, catalogReceipt) = await ModelsAsync(parameters).ConfigureAwait(false);
                else result = await HandleAsync(method, parameters).ConfigureAwait(false);
                reply = new() { ["id"] = message["id"]!.DeepClone(), ["result"] = result };
            }
            catch (RpcFailure error) { reply = new() { ["id"] = message["id"]!.DeepClone(), ["error"] = error.Error.DeepClone() }; }
            catch (Exception error)
            { reply = new() { ["id"] = message["id"]!.DeepClone(), ["error"] = new JsonObject { ["code"] = -32000, ["message"] = error.Message } }; }
            await WriteClientAsync(reply).ConfigureAwait(false);
            if (catalogReceipt != null)
                SecureFiles.AtomicWrite(Path.Combine(_config.Directory, DesktopBackendCatalog.ObservedFileName), JsonSerializer.SerializeToUtf8Bytes(new { revision = catalogReceipt, backendPid = Environment.ProcessId, nativePid = _native.Id }));
        }
        finally { gate.Release(); }
    }
    private async Task ReadNativeAsync()
    {
        try
        {
            while (await _native.StandardOutput.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                var message = JsonNode.Parse(line)!.AsObject();
                var id = message["id"]?.ToJsonString();
                if (id != null && _pending.TryRemove(id, out var waiter)) { waiter.TrySetResult(message); continue; }
                var method = message["method"]?.GetValue<string>();
                var thread = message["params"]?["threadId"]?.GetValue<string>();
                if (thread != null)
                {
                    if (method == "turn/started") _active[thread] = 0;
                    if (method is "turn/completed" or "thread/closed") _active.TryRemove(thread, out _);
                    if (method == "thread/closed" && _handoffs.ContainsKey(thread)) continue;
                }
                await WriteClientAsync(message).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var waiter in _pending.Values) waiter.TrySetException(new IOException("The native Codex backend stopped."));
            _stop.Cancel();
        }
    }
    private async Task WriteNativeAsync(JsonObject value)
    {
        await _nativeWrite.WaitAsync().ConfigureAwait(false);
        try { await _native.StandardInput.WriteLineAsync(value.ToJsonString()).ConfigureAwait(false); await _native.StandardInput.FlushAsync().ConfigureAwait(false); }
        finally { _nativeWrite.Release(); }
    }
    private async Task WriteClientAsync(JsonObject value)
    {
        await _clientWrite.WaitAsync().ConfigureAwait(false);
        try { await Console.Out.WriteLineAsync(value.ToJsonString()).ConfigureAwait(false); await Console.Out.FlushAsync().ConfigureAwait(false); }
        finally { _clientWrite.Release(); }
    }
    private async Task<JsonNode?> RequestAsync(string method, JsonObject parameters)
    {
        var id = _prefix + Interlocked.Increment(ref _sequence);
        var waiter = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = JsonSerializer.Serialize(id);
        if (!_pending.TryAdd(key, waiter)) throw new InvalidOperationException("Duplicate backend request identity.");
        try
        {
            await WriteNativeAsync(new() { ["id"] = id, ["method"] = method, ["params"] = parameters.DeepClone() }).ConfigureAwait(false);
            var result = await waiter.Task.WaitAsync(_stop.Token).ConfigureAwait(false);
            if (result["error"] is JsonObject error) throw new RpcFailure(error);
            return result["result"]?.DeepClone();
        }
        finally { _pending.TryRemove(key, out _); }
    }
    private JsonObject Catalog() => JsonNode.Parse(SecureFiles.ReadPrivateBytes(Path.Combine(_config.Directory, DesktopBackendCatalog.FileName)))!.AsObject();
    private static string? Selected(JsonObject parameters) => parameters["collaborationMode"]?["settings"]?["model"]?.GetValue<string>() ?? parameters["model"]?.GetValue<string>();
    private async Task<JsonNode?> HandleAsync(string method, JsonObject parameters)
    {
        if (method == "initialize")
        {
            var capabilities = parameters["capabilities"] as JsonObject ?? new();
            capabilities["experimentalApi"] = true;
            parameters["capabilities"] = capabilities;
        }
        var thread = parameters["threadId"]?.GetValue<string>();
        _sessions.TryGetValue(thread ?? "", out var session);
        var selected = Selected(parameters) ?? session?.Model;
        if (method is "thread/resume" or "thread/fork" && selected == null && thread != null && File.Exists(Path.Combine(_config.Home, "state_5.sqlite")))
            selected = NativeHistoryThreads.Read(_config.Home, thread).SingleOrDefault()?.Model;
        if (method is "thread/start" or "thread/resume" or "thread/fork")
        {
            if (method == "thread/resume" && thread != null && await LoadedAsync(thread).ConfigureAwait(false))
            {
                await EnsureRouteAsync(thread, selected, session).ConfigureAwait(false);
                var resumed = await RequestAsync(method, parameters).ConfigureAwait(false);
                if (_sessions.TryGetValue(thread, out var current))
                { current.Model = resumed?["model"]?.GetValue<string>() ?? selected; Capture(current, parameters); }
                return resumed;
            }
            if (selected != null) parameters["model"] = selected;
            var revision = Configure(parameters, selected);
            if (method == "thread/resume" && thread != null && selected != null && !ModelRouting.Owned(selected))
                await RecoverAsync(thread, selected).ConfigureAwait(false);
            var result = await RequestAsync(method, parameters).ConfigureAwait(false);
            Remember(result, selected, revision);
            if (method == "thread/fork" && selected != null && !ModelRouting.Owned(selected) && result?["thread"]?["id"]?.GetValue<string>() is string fork)
                await EnsureRouteAsync(fork, selected, null).ConfigureAwait(false);
            return result;
        }
        if (method == "turn/start" && thread != null)
        {
            await EnsureRouteAsync(thread, selected, session).ConfigureAwait(false);
            var result = await RequestAsync(method, parameters).ConfigureAwait(false);
            if (_sessions.TryGetValue(thread, out var current)) Capture(current, parameters);
            return result;
        }
        var response = await RequestAsync(method, parameters).ConfigureAwait(false);
        if (method == "thread/settings/update" && thread != null && session != null)
        {
            Capture(session, parameters);
            if (Selected(parameters) is string model) session.Model = model;
        }
        return response;
    }
    private string? Configure(JsonObject parameters, string? model)
    {
        if (!ModelRouting.Owned(model)) return null;
        var catalog = Catalog();
        if (!catalog["models"]!.AsArray().Any(item => item?["model"]?.GetValue<string>() == model))
            throw new InvalidOperationException("The selected custom model was removed. Select a registered model; the conversation was preserved.");
        EnsureGateway();
        var config = parameters["config"] as JsonObject ?? new();
        config["openai_base_url"] = _gateway!.BaseUrl;
        config["model_catalog_json"] = catalog["catalogPath"]!.DeepClone();
        config["features.enable_request_compression"] = false;
        config["features.responses_websockets_v2"] = false;
        parameters["config"] = config;
        parameters["modelProvider"] = "openai";
        return catalog["revision"]!.GetValue<string>();
    }
    private void EnsureGateway()
    {
        lock (_gatewayGate)
        {
        if (_gateway != null) return;
        var gateway = new ModelGateway(new ModelProviderStore(_config.Directory), Authenticate, _http, _config.Directory, () => _config.Home, customOnly: true);
        try { gateway.Start(); _gateway = gateway; }
        catch { gateway.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        }
    }
    private GatewayIdentity? Authenticate(string? authorization, string? account)
    {
        var identity = NativeHistoryRecovery.ReadIdentity(_config.Home);
        return authorization == "Bearer " + identity.Token && (identity.ApiKey || account == identity.AccountId) ? identity : null;
    }
    private async Task RecoverAsync(string thread, string model)
    {
        var candidates = NativeHistoryRecovery.Find(_config.Home, _config.Directory, _stop.Token, thread, model);
        if (candidates.Count == 0) return;
        await NativeHistoryRecovery.RecoverAsync(_config.NativeExecutable, _config.Home, _config.Directory,
            new ModelProviderStore(_config.Directory), Authenticate, _http, candidates, _stop.Token, model).ConfigureAwait(false);
    }
    private async Task<bool> LoadedAsync(string thread)
    {
        string? cursor = null;
        do
        {
            var page = await RequestAsync("thread/loaded/list", new() { ["cursor"] = cursor, ["limit"] = 100 }).ConfigureAwait(false);
            if (page!["data"]!.AsArray().Any(item => item!.GetValue<string>() == thread)) return true;
            cursor = page["nextCursor"]?.GetValue<string>();
        } while (cursor != null);
        return false;
    }
    private async Task EnsureRouteAsync(string thread, string? model, Session? previous)
    {
        if (model == null) return;
        var custom = ModelRouting.Owned(model);
        var revision = custom ? Catalog()["revision"]!.GetValue<string>() : null;
        if (previous != null && previous.Custom == custom && previous.Revision == revision) return;
        if (previous?.Ephemeral == true) throw new InvalidOperationException("An ephemeral conversation cannot be unloaded and resumed. Start it with the desired model.");
        if (_active.ContainsKey(thread)) throw new InvalidOperationException("Wait for the current turn to finish before changing model connections.");
        var resume = new JsonObject { ["threadId"] = thread, ["model"] = model, ["excludeTurns"] = true };
        Configure(resume, model);
        _handoffs[thread] = 0;
        try
        {
            if (await LoadedAsync(thread).ConfigureAwait(false))
            {
                await RequestAsync("thread/unsubscribe", new() { ["threadId"] = thread }).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (await LoadedAsync(thread).ConfigureAwait(false))
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new IOException("Codex has not released the conversation. Its connection was not changed.");
                    await Task.Delay(40, _stop.Token).ConfigureAwait(false);
                }
            }
            if (!custom) await RecoverAsync(thread, model).ConfigureAwait(false);
            var result = await RequestAsync("thread/resume", resume).ConfigureAwait(false);
            Remember(result, model, revision);
            if (previous != null)
            {
                var settings = (JsonObject)previous.Settings.DeepClone();
                settings["threadId"] = thread; settings["model"] = model;
                if (settings["collaborationMode"]?["settings"] is JsonObject collaboration) collaboration["model"] = model;
                await RequestAsync("thread/settings/update", settings).ConfigureAwait(false);
                _sessions[thread].Settings = settings;
            }
        }
        finally { _handoffs.TryRemove(thread, out _); }
    }
    private static readonly string[] SettingFields = { "approvalPolicy", "approvalsReviewer", "collaborationMode", "cwd", "disabledPluginIds", "effort", "multiAgentMode", "permissions", "personality", "sandboxPolicy", "serviceTier", "summary" };
    private static void Capture(Session session, JsonObject parameters)
    { foreach (var field in SettingFields) if (parameters.ContainsKey(field)) session.Settings[field] = parameters[field]?.DeepClone(); }
    private void Remember(JsonNode? result, string? model, string? revision)
    {
        var thread = result?["thread"]?["id"]?.GetValue<string>();
        if (thread == null) return;
        var session = new Session { Model = result?["model"]?.GetValue<string>() ?? model, Custom = ModelRouting.Owned(model), Revision = revision };
        session.Ephemeral = result?["thread"]?["ephemeral"]?.GetValue<bool>() == true;
        Capture(session, result!.AsObject());
        if (result["sandbox"] != null) session.Settings["sandboxPolicy"] = result["sandbox"]!.DeepClone();
        if (result["reasoningEffort"] != null) session.Settings["effort"] = result["reasoningEffort"]!.DeepClone();
        _sessions[thread] = session;
    }
    private async Task<(JsonNode Result, string? Receipt)> ModelsAsync(JsonObject parameters)
    {
        var limit = parameters["limit"]?.GetValue<int>() ?? 100;
        if (limit <= 0 || limit > 1000) throw new ArgumentException("Model page size must be between 1 and 1000.");
        var cursor = parameters["cursor"]?.GetValue<string>();
        string identity; int offset; JsonArray models;
        if (cursor != null)
        {
            var pieces = cursor.Split(':');
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out offset) || !_pages.TryGetValue(pieces[0], out models!) || offset < 0 || offset > models.Count)
                throw new ArgumentException("The model cursor expired. Refresh the model list.");
            identity = pieces[0];
        }
        else
        {
            models = new(); identity = Guid.NewGuid().ToString("N"); offset = 0;
            var nativeParameters = (JsonObject)parameters.DeepClone(); nativeParameters["limit"] = 100;
            do
            {
                var page = await RequestAsync("model/list", nativeParameters).ConfigureAwait(false);
                foreach (var entry in page!["data"]!.AsArray()) models.Add(entry!.DeepClone());
                nativeParameters["cursor"] = page["nextCursor"]?.DeepClone();
            } while (nativeParameters["cursor"] != null);
            var catalog = Catalog();
            foreach (var item in catalog["models"]!.AsArray())
            {
                if (item!["hidden"]?.GetValue<bool>() == true && parameters["includeHidden"]?.GetValue<bool>() != true) continue;
                if (models.Any(existing => existing!["model"]!.GetValue<string>() == item["model"]!.GetValue<string>()))
                    throw new InvalidDataException("A native model conflicts with a registered custom alias.");
                var entry = item.DeepClone(); entry["isDefault"] = false; models.Add(entry);
            }
            while (_pages.Count >= 32) { var oldest = _pages.Keys.First(); _pages.Remove(oldest); _pageRevisions.Remove(oldest); }
            _pages[identity] = models;
            _pageRevisions[identity] = catalog["revision"]!.GetValue<string>();
        }
        var data = new JsonArray(models.Skip(offset).Take(limit).Select(model => model!.DeepClone()).ToArray());
        var receipt = offset + data.Count == models.Count ? _pageRevisions[identity] : null;
        return (new JsonObject { ["data"] = data, ["nextCursor"] = offset + data.Count < models.Count ? identity + ":" + (offset + data.Count) : null }, receipt);
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_gateway != null) await _gateway.DisposeAsync().ConfigureAwait(false);
        if (!_native.HasExited) { _native.StandardInput.Close(); if (!await Task.Run(() => _native.WaitForExit(5000)).ConfigureAwait(false)) _native.Kill(true); }
        _nativeJob.Dispose(); _native.Dispose(); _http.Dispose(); _stop.Dispose(); _nativeWrite.Dispose(); _clientWrite.Dispose();
    }
}
