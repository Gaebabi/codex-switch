using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodexAccountDesk;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native Linux desktop contracts require Linux.");
var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Native desktop contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Refused(Action action) { try { action(); } catch (IOException) { return; } throw new InvalidOperationException("Expected desktop refusal was not observed."); }
if (args.Length != 2 || args[0] is not ("--contracts" or "--peer")) throw new ArgumentException("Select the owned contract root or actual peer home.");
var root = Path.GetFullPath(args[1]);
if (!Path.GetFileName(root).StartsWith("codex-linux-desktop-test-", StringComparison.Ordinal) || new DirectoryInfo(root).LinkTarget != null || File.GetUnixFileMode(root) != (UnixFileMode)0x1c0 || LinuxPrivateFiles.UserId == 0)
    throw new InvalidOperationException("Only an owned disposable private native desktop sandbox is permitted.");
if (args[0] == "--contracts")
{
    var home = Path.Combine(root, "home"); Directory.CreateDirectory(home); File.SetUnixFileMode(home, (UnixFileMode)0x1c0);
    var ipc = Path.Combine(home, "ipc"); Directory.CreateDirectory(ipc); File.SetUnixFileMode(ipc, (UnixFileMode)0x1c0);
    var path = Path.Combine(ipc, "ipc.sock");
    void ConnectRefused() => Refused(() => LinuxDesktopConnection.ConnectSocketAsync(home, CancellationToken.None).GetAwaiter().GetResult().Dispose());
    Check("Absent sockets refuse without creating home credentials or a socket", () => { ConnectRefused(); Assert(!File.Exists(path) && !File.Exists(Path.Combine(home, "auth.json"))); });
    Check("Regular-file and symlink collisions refuse with original bytes intact", () =>
    {
        var bytes = Encoding.UTF8.GetBytes("Synthetic unrelated file"); File.WriteAllBytes(path, bytes); File.SetUnixFileMode(path, (UnixFileMode)0x180);
        ConnectRefused(); Assert(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)); File.Delete(path);
        var target = Path.Combine(root, "untouched"); File.WriteAllBytes(target, bytes); File.CreateSymbolicLink(path, target);
        ConnectRefused(); Assert(File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)); File.Delete(path);
    });
    using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(4); File.SetUnixFileMode(path, (UnixFileMode)0x180);
    Check("Unsafe socket permissions and IPC/home directories refuse without rewriting permissions", () =>
    {
        File.SetUnixFileMode(path, (UnixFileMode)0x1b6); ConnectRefused(); Assert(File.GetUnixFileMode(path) == (UnixFileMode)0x1b6); File.SetUnixFileMode(path, (UnixFileMode)0x180);
        foreach (var directory in new[] { ipc, home })
        { File.SetUnixFileMode(directory, (UnixFileMode)0x1ed); ConnectRefused(); Assert(File.GetUnixFileMode(directory) == (UnixFileMode)0x1ed); File.SetUnixFileMode(directory, (UnixFileMode)0x1c0); }
    });
    Check("Directory symlinks refuse without following an alternate socket", () =>
    {
        var alias = Path.Combine(root, "home-alias"); Directory.CreateSymbolicLink(alias, home);
        Refused(() => LinuxDesktopConnection.ConnectSocketAsync(alias, CancellationToken.None).GetAwaiter().GetResult().Dispose()); Directory.Delete(alias);
    });
    using var installed = LinuxClientDiscovery.Open();
    Check("A same-UID forged listener is rejected before receiving an application byte", () =>
    {
        using var connection = LinuxDesktopConnection.ConnectSocketAsync(home, CancellationToken.None).GetAwaiter().GetResult();
        using var accepted = listener.Accept();
        Refused(() => LinuxPeerProcess.Open(connection, installed.Desktop!, home).Dispose()); Assert(accepted.Available == 0);
    });
    Check("The ordinary production connector rejects the same forged listener before initialization", () =>
    {
        Refused(() => LinuxDesktopConnection.ConnectAsync(home, CancellationToken.None).GetAwaiter().GetResult().Dispose());
        using var accepted = listener.Accept(); Assert(accepted.Available == 0);
    });
    Check("Only the verified packaged entry and explicitly supported arguments are admitted", () =>
    {
        LinuxPeerProcess.VerifyArguments(new[] { LinuxClientDiscovery.DesktopPath });
        foreach (var argument in new[] { "--inspect=0", "--remote-debugging-port=0", "--no-sandbox", "--require=untrusted.js", "untrusted.js", "--type=renderer", "--enable-codex", "--ozone-platform=x11", "codex://threads/00000000-0000-4000-8000-000000000001", "codex://threads/not-a-guid" })
            Refused(() => LinuxPeerProcess.VerifyArguments(new[] { LinuxClientDiscovery.DesktopPath, argument }));
        Refused(() => LinuxPeerProcess.VerifyArguments(new[] { "/untrusted/ChatGPT" }));
    });
    Check("Loader/Node/Electron injection and another selected home refuse without inspecting credentials", () =>
    {
        var normal = new[] { "HOME=" + root, "CODEX_HOME=" + home }; LinuxPeerProcess.VerifyEnvironment(normal, home);
        LinuxPeerProcess.VerifyEnvironment(new[] { "HOME=" + root }, Path.Combine(root, ".codex"));
        foreach (var key in new[] { "LD_PRELOAD", "LD_LIBRARY_PATH", "NODE_OPTIONS", "NODE_PATH", "ELECTRON_RUN_AS_NODE", "GLIBC_TUNABLES", "GCONV_PATH", "VITE_DEV_SERVER_URL" })
            Refused(() => LinuxPeerProcess.VerifyEnvironment(new[] { normal[0], normal[1], key + "=synthetic" }, home));
        Refused(() => LinuxPeerProcess.VerifyEnvironment(normal, Path.Combine(root, "different")));
        Refused(() => LinuxPeerProcess.VerifyEnvironment(new[] { normal[0], normal[1], normal[1] }, home));
    });
    Check("Wrong application-resource checksums refuse without changing the root-managed file", () =>
        Refused(() => LinuxExecutable.VerifySystemData("/usr/lib/chatgpt/resources/app.asar", new string('0', 64), CancellationToken.None)));
    Check("Cancelled connections cannot publish a peer stream", () =>
    {
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { LinuxDesktopConnection.ConnectAsync(home, stopped.Token).GetAwaiter().GetResult().Dispose(); }
        catch (OperationCanceledException) { return; } throw new InvalidOperationException("Cancelled peer connection was accepted.");
    });
    Check("A real stale native socket reports unavailable and remains unchanged for its owning client", () =>
    {
        listener.Dispose(); if (File.Exists(path)) File.Delete(path);
        var address = new byte[110]; address[0] = 1; var name = Encoding.UTF8.GetBytes(path); Assert(name.Length < 108); name.CopyTo(address, 2);
        var file = NativeSocket.socket(1, 1 | 0x80000, 0); Assert(file >= 0);
        try { Assert(NativeSocket.bind(file, address, (uint)(2 + name.Length + 1)) == 0); }
        finally { Assert(NativeSocket.close(file) == 0); }
        File.SetUnixFileMode(path, (UnixFileMode)0x180); ConnectRefused(); Assert(File.Exists(path) && File.GetUnixFileMode(path) == (UnixFileMode)0x180);
    });
}
else
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var marker = Path.Combine(root, "injected-marker"); var injected = Path.Combine(root, "injected.js");
    File.WriteAllText(injected, "require('node:fs').writeFileSync(" + JsonSerializer.Serialize(marker) + ", 'unexpected');");
    var originalOptions = Environment.GetEnvironmentVariable("NODE_OPTIONS"); LinuxDesktopLaunch owned;
    try { Environment.SetEnvironmentVariable("NODE_OPTIONS", "--require=" + injected); owned = LinuxDesktopLaunch.Start(root, deadline.Token); }
    finally { Environment.SetEnvironmentVariable("NODE_OPTIONS", originalOptions); }
    using var launch = owned;
    while (!File.Exists(Path.Combine(root, "ipc/ipc.sock"))) await Task.Delay(100, deadline.Token);
    await using var stream = await LinuxDesktopConnection.ConnectAsync(root, deadline.Token);
    var id = Guid.NewGuid().ToString();
    var body = JsonSerializer.SerializeToUtf8Bytes(new { type = "request", requestId = id, method = "initialize", version = 0, timeoutMs = 5000, @params = new { clientType = "codex-account-desk" } });
    var frame = new byte[4 + body.Length]; BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length); body.CopyTo(frame, 4);
    await stream.WriteAsync(frame, deadline.Token); await stream.FlushAsync(deadline.Token);
    var header = new byte[4]; await stream.ReadExactlyAsync(header, deadline.Token); var size = BinaryPrimitives.ReadInt32LittleEndian(header);
    Assert(size > 0 && size < 1024 * 1024); var response = new byte[size]; await stream.ReadExactlyAsync(response, deadline.Token);
    using var document = JsonDocument.Parse(response); var result = document.RootElement;
    Check("Actual sandboxed official desktop authenticates with a peer-bound pidfd and exact root-managed image/resources", () => Assert(stream.CanRead && stream.CanWrite));
    Check("Actual desktop answers the existing framed initialize protocol successfully", () => Assert(result.GetProperty("type").GetString() == "response" && result.GetProperty("requestId").GetString() == id && result.GetProperty("resultType").GetString() == "success"));
    Check("No authentication file is created by peer verification", () => Assert(!File.Exists(Path.Combine(root, "auth.json"))));
    Check("The actual managed native launch excludes a parent Node preload without executing its marker", () => Assert(!File.Exists(marker)));
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "peer-connected", ownedProcess = launch.Id })); Console.Out.Flush();
    if (await Console.In.ReadLineAsync(deadline.Token) != "owned-peer-exited") throw new InvalidOperationException("The owned peer-exit synchronization is missing.");
    launch.WaitForExit(deadline.Token);
    Check("A retained connection refuses sends after the original native peer exits", () =>
        Refused(() => stream.WriteAsync(frame, deadline.Token).GetAwaiter().GetResult()));
}
Console.WriteLine(JsonSerializer.Serialize(new { schema = 1, passed = checks.Count, failed = 0, architecture = RuntimeInformation.ProcessArchitecture.ToString(), checks,
    scope = "Owned native sockets and the actual approved sandboxed desktop, without accounts, credentials, inference, client shutdown, chat mutation or authenticated handoff acceptance." }));

internal static class NativeSocket
{
    [DllImport("libc", SetLastError = true)] internal static extern int socket(int domain, int type, int protocol);
    [DllImport("libc", SetLastError = true)] internal static extern int bind(int socket, byte[] address, uint length);
    [DllImport("libc", SetLastError = true)] internal static extern int close(int descriptor);
}
