using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodexAccountDesk;

internal static class LinuxDesktopConnection
{
    private const int CloseOnExec = 0x80000, PathOnly = 0x200000;
    private static int DirectoryFlag => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x4000 : 0x10000;
    private static int NoFollow => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat
    {
        [FieldOffset(0)] public uint Mask; [FieldOffset(20)] public uint Uid; [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode; [FieldOffset(136)] public uint Major; [FieldOffset(140)] public uint Minor;
    }
    private static int Fd(SafeFileHandle file) => file.DangerousGetHandle().ToInt32();
    private static SafeFileHandle Handle(int descriptor) => descriptor < 0 ? throw LinuxPeerProcess.Refused() : new(new IntPtr(descriptor), true);
    private static Stat Inspect(SafeFileHandle file)
    {
        if (statx(Fd(file), "", 0x1000, 0x10b, out var value) != 0 || (value.Mask & 0x10b) != 0x10b) throw LinuxPeerProcess.Refused();
        return value;
    }
    internal static void VerifyHome(string home) { using var directory = OpenDirectory(home, false); }
    private static SafeFileHandle OpenDirectory(string home, bool ipc = true)
    {
        if (home != Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) || home == "/") throw LinuxPeerProcess.Refused();
        var current = Handle(open("/", DirectoryFlag | NoFollow | CloseOnExec));
        try
        {
            var names = (home + (ipc ? "/ipc" : "")).Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < names.Length; index++)
            {
                var next = Handle(openat(Fd(current), names[index], DirectoryFlag | NoFollow | CloseOnExec));
                try
                {
                    var value = Inspect(next);
                    if ((value.Mode & 0xf000) != 0x4000 || value.Uid != 0 && value.Uid != LinuxPrivateFiles.UserId ||
                        (value.Mode & 0x12) != 0 && !(value.Uid == 0 && (value.Mode & 0x200) != 0) ||
                        index >= names.Length - (ipc ? 2 : 1) && (value.Uid != LinuxPrivateFiles.UserId || (value.Mode & 0xfff) != 0x1c0)) throw LinuxPeerProcess.Refused();
                }
                catch { next.Dispose(); throw; }
                current.Dispose(); current = next;
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }
    internal static async Task<Socket> ConnectSocketAsync(string home, CancellationToken cancellation)
    {
        using var directory = OpenDirectory(home);
        using var entry = Handle(openat(Fd(directory), "ipc.sock", PathOnly | NoFollow | CloseOnExec));
        var before = Inspect(entry);
        if ((before.Mode & 0xf000) != 0xc000 || before.Uid != LinuxPrivateFiles.UserId || (before.Mode & 0xfff) != 0x180) throw LinuxPeerProcess.Refused();
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            var address = "/proc/" + Environment.ProcessId + "/fd/" + Fd(directory) + "/ipc.sock";
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(address), cancellation).ConfigureAwait(false);
            using var afterFile = Handle(openat(Fd(directory), "ipc.sock", PathOnly | NoFollow | CloseOnExec));
            var after = Inspect(afterFile);
            if (after.Inode != before.Inode || after.Major != before.Major || after.Minor != before.Minor || after.Mode != before.Mode || after.Uid != before.Uid) throw LinuxPeerProcess.Refused();
            return socket;
        }
        catch (SocketException error) { socket.Dispose(); throw new IOException("The Linux desktop IPC socket is unavailable.", error); }
        catch { socket.Dispose(); throw; }
    }
    public static async Task<Stream> ConnectAsync(string home, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux desktop IPC requires Linux.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var installed = await Task.Run(() => LinuxClientDiscovery.Open(null, deadline.Token), deadline.Token).ConfigureAwait(false);
        Socket? socket = null; LinuxPeerProcess? peer = null;
        try
        {
            using var manifest = LinuxClientDiscovery.ApprovedManifest();
            var sha = manifest.RootElement.GetProperty("architectures").GetProperty(RuntimeInformation.ProcessArchitecture.ToString()).GetProperty("asarSha256").GetString()!;
            await Task.Run(() => LinuxExecutable.VerifySystemData("/usr/lib/chatgpt/resources/app.asar", sha, deadline.Token), deadline.Token).ConfigureAwait(false);
            socket = await ConnectSocketAsync(home, deadline.Token).ConfigureAwait(false);
            peer = LinuxPeerProcess.Open(socket, installed.Desktop!, home);
            return new PeerStream(new NetworkStream(socket, true), peer, installed);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { peer?.Dispose(); socket?.Dispose(); installed.Dispose(); throw new TimeoutException("Linux desktop IPC verification exceeded its deadline."); }
        catch { peer?.Dispose(); socket?.Dispose(); installed.Dispose(); throw; }
    }
    internal static int PeerId(Stream stream) => stream is PeerStream peer ? peer.ProcessId : throw LinuxPeerProcess.Refused();
    private sealed class PeerStream : Stream
    {
        private readonly NetworkStream _stream;
        private readonly LinuxPeerProcess _peer;
        private readonly LinuxClientInstallation _installation;
        public int ProcessId => _peer.Id;
        public PeerStream(NetworkStream stream, LinuxPeerProcess peer, LinuxClientInstallation installation)
        { _stream = stream; _peer = peer; _installation = installation; }
        public override bool CanRead => _stream.CanRead;
        public override bool CanWrite => _stream.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { _peer.Verify(); _stream.Flush(); }
        public override Task FlushAsync(CancellationToken cancellation) { _peer.Verify(); return _stream.FlushAsync(cancellation); }
        public override int Read(byte[] buffer, int offset, int count) { _peer.Verify(); var read = _stream.Read(buffer, offset, count); _peer.Verify(); return read; }
        public override void Write(byte[] buffer, int offset, int count) { _peer.Verify(); _stream.Write(buffer, offset, count); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        { _peer.Verify(); var count = await _stream.ReadAsync(buffer, cancellation).ConfigureAwait(false); _peer.Verify(); return count; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation = default)
        { _peer.Verify(); return _stream.WriteAsync(buffer, cancellation); }
        protected override void Dispose(bool disposing)
        { if (disposing) { _stream.Dispose(); _peer.Dispose(); _installation.Dispose(); } base.Dispose(disposing); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int openat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
}
