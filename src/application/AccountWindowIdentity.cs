using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal static class AccountWindowIdentity
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    internal static string Pipe(string home) => "CodexSwitch.Window." + SecureFiles.Hash(Path.GetFullPath(home).ToUpperInvariant());

    internal static async Task ServeAsync(string home, Func<string, JsonObject, Task<JsonNode?>> rpc, AccountWindowActivity activity, CancellationToken stop)
    {
        var clients = new List<Task>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                clients.RemoveAll(client => client.IsCompletedSuccessfully);
                if (clients.Count >= 6) await (await Task.WhenAny(clients).ConfigureAwait(false)).ConfigureAwait(false);
                var pipe = new NamedPipeServerStream(Pipe(home), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                clients.Add(AnswerAsync(pipe, home, rpc, activity, stop));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { await Task.WhenAll(clients).ConfigureAwait(false); }
    }

    private static async Task AnswerAsync(NamedPipeServerStream pipe, string home,
        Func<string, JsonObject, Task<JsonNode?>> rpc, AccountWindowActivity activity, CancellationToken stop)
    {
        using (pipe)
        try
        {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                request.CancelAfter(TimeSpan.FromSeconds(6));
                var command = new byte[1];
                try { if (await pipe.ReadAsync(command, request.Token).ConfigureAwait(false) != 1 || command[0] is not (1 or 2)) return; }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { return; }
                string key;
                var generation = activity.Snapshot().AccountGeneration;
                try
                {
                    var sessions = await rpc("accountSession/list", new() { ["refreshWorkspaceMetadata"] = false }).WaitAsync(TimeSpan.FromSeconds(4), stop).ConfigureAwait(false);
                    var json = File.ReadAllText(Path.Combine(home, "auth.json"));
                    key = sessions?["activeSessionId"]?.GetValue<string>() == null ? "?" : VerifiedKey(json, sessions);
                }
                catch (Exception error) when (!stop.IsCancellationRequested && error is IOException or TimeoutException or
                    InvalidOperationException or FormatException or JsonException or UnauthorizedAccessException)
                {
                    await Console.Error.WriteLineAsync("Account window identity unavailable: " + error.GetType().Name).ConfigureAwait(false);
                    key = "?";
                }
                if (generation != activity.Snapshot().AccountGeneration) key = "?";
                await pipe.WriteAsync(Encoding.ASCII.GetBytes(key + "\n"), stop).ConfigureAwait(false);
                if (command[0] != 2 || key.Length != 64) return;
                var disconnected = pipe.ReadAsync(command, stop).AsTask();
                while (!disconnected.IsCompleted)
                {
                    var view = activity.Snapshot();
                    if (view.AccountGeneration != generation) return;
                    await pipe.WriteAsync(BitConverter.GetBytes(view.Running), stop).ConfigureAwait(false);
                    await Task.WhenAny(view.Changed, disconnected).ConfigureAwait(false);
                }
                await disconnected.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (IOException) { /* Client disconnected while waiting for a state change. */ }
    }

    internal static string VerifiedKey(string json, JsonNode? sessions)
    {
        var account = AuthDocument.Parse(json);
        using var auth = JsonDocument.Parse(json);
        using var claims = AuthDocument.DecodeClaims(auth.RootElement.GetProperty("tokens").GetProperty("id_token").GetString()!);
        var user = claims.RootElement.TryGetProperty("https://api.openai.com/auth", out var ids)
            ? JsonValue.String(ids, "chatgpt_user_id") ?? JsonValue.String(ids, "user_id") : null;
        var sessionId = sessions?["activeSessionId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(sessionId)) return "";
        var active = sessions?["sessions"]?.AsArray().SingleOrDefault(row => row?["sessionId"]?.GetValue<string>() == sessionId);
        return active?["isActive"]?.GetValue<bool>() == true && active["selectedWorkspaceAccountId"]?.GetValue<string>() == account.AccountId &&
            active["userId"]?.GetValue<string>() == user ? account.Key : "";
    }

    internal static async Task VerifyAsync(string home, string key, int rootPid, CancellationToken cancellation, Action<int>? activity = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var config = JsonSerializer.Deserialize<DesktopBackendHost.Configuration>(SecureFiles.ReadPrivateBytes(Path.Combine(home, "window-backend.json")));
        if (config?.AccountWindowBackend == null) throw new InvalidOperationException("switch.window_identity");
        using var pipe = new NamedPipeClientStream(".", Pipe(home), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(750, cancellation).ConfigureAwait(false);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer) || WindowsProcessImage.Read((int)peer) is not { } image ||
            !image.MatchesPath(Path.GetFullPath(config.AccountWindowBackend))) throw new InvalidOperationException("switch.window_peer");
        if (!WindowsAccountWindows.DescendsFrom((int)peer, rootPid)) throw new InvalidOperationException("switch.window_parent");
        await pipe.WriteAsync(new byte[] { activity == null ? (byte)1 : (byte)2 }, cancellation).ConfigureAwait(false);
        var expected = Encoding.ASCII.GetBytes(key + "\n");
        var actual = new byte[expected.Length];
        var count = 0;
        while (count < actual.Length)
        {
            var size = await pipe.ReadAsync(actual.AsMemory(count), cancellation).ConfigureAwait(false);
            if (size == 0) break;
            count += size;
        }
        if (count == 2 && actual[0] == '?' && actual[1] == '\n') throw new TimeoutException("switch.window_wait");
        if (count != expected.Length || !actual.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("switch.window_identity");
        if (activity == null) return;
        var frame = new byte[sizeof(int)];
        while (true)
        {
            await pipe.ReadExactlyAsync(frame, cancellation).ConfigureAwait(false);
            var running = BitConverter.ToInt32(frame);
            if (running < 0) throw new InvalidDataException("Invalid account window activity.");
            activity(running);
        }
    }
}
