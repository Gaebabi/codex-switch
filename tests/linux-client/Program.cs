using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using CodexAccountDesk;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native Linux client contracts require Linux.");
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_SWITCH_LINUX_CLIENT_TEST_ROOT") ?? throw new InvalidOperationException("An owned diagnostic root is required."));
if (!Path.GetFileName(root).StartsWith("codex-linux-client-test-", StringComparison.Ordinal) || new DirectoryInfo(root).LinkTarget != null || File.GetUnixFileMode(root) != (UnixFileMode)0x1c0 || !NativeFixture.OwnedByRegularUser(root))
    throw new InvalidOperationException("Only the owned disposable native client sandbox is permitted.");
var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Native client contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Refused(Action action) { try { action(); } catch (IOException) { return; } throw new InvalidOperationException("Expected native trust refusal was not observed."); }
var bytes = File.ReadAllBytes("/usr/bin/true"); var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string Fixture(string name, byte[]? content = null)
{ var path = Path.Combine(root, name); File.WriteAllBytes(path, content ?? bytes); File.SetUnixFileMode(path, (UnixFileMode)0x140); return path; }
Check("Actual installed approved package and both native binary checksums verify", () =>
{ using var installed = LinuxClientDiscovery.Open(); Assert(installed.Provenance == "ApprovedSystemPackage" && installed.Desktop != null && installed.PackageVersion == "26.1007.71326"); });
var selected = Fixture("explicit-native");
Check("Explicit checksum selection is CLI-only and retains its verified descriptor", () =>
{ using var installed = LinuxClientDiscovery.Open(new(selected, digest)); Assert(installed.Desktop == null && installed.Provenance == "ExplicitChecksum" && installed.Cli.ExecutionPath.StartsWith("/proc/", StringComparison.Ordinal)); });
Check("Checksum mismatch refuses without a discovery fallback", () => Refused(() => LinuxClientDiscovery.Open(new(selected, new string('0', 64))).Dispose()));
Check("Unprivileged system trust cannot accept a user-managed installation tree", () => Refused(() => LinuxExecutable.Verify(selected, digest, true).Dispose()));
Check("Final and intermediate symbolic links refuse before launch", () =>
{
    var link = Path.Combine(root, "client-link"); File.CreateSymbolicLink(link, selected);
    try { Refused(() => LinuxExecutable.Verify(link, digest, false).Dispose()); } finally { File.Delete(link); }
    var folder = Path.Combine(root, "directory-link"); Directory.CreateSymbolicLink(folder, root);
    try { Refused(() => LinuxExecutable.Verify(Path.Combine(folder, "explicit-native"), digest, false).Dispose()); } finally { Directory.Delete(folder); }
});
Check("Foreign ownership refuses a readable valid native binary with the correct checksum", () =>
    Refused(() => LinuxExecutable.Verify(Path.Combine(root, "foreign-client"), digest, false).Dispose()));
Check("Writable executable and writable user ancestor refuse with original bytes intact", () =>
{
    File.SetUnixFileMode(selected, (UnixFileMode)0x1ff);
    try { Refused(() => LinuxExecutable.Verify(selected, digest, false).Dispose()); Assert(File.ReadAllBytes(selected).SequenceEqual(bytes)); }
    finally { File.SetUnixFileMode(selected, (UnixFileMode)0x140); }
    var folder = Path.Combine(root, "writable"); Directory.CreateDirectory(folder); File.SetUnixFileMode(folder, (UnixFileMode)0x1ff); File.Copy(selected, Path.Combine(folder, "client")); File.SetUnixFileMode(Path.Combine(folder, "client"), (UnixFileMode)0x140);
    try { Refused(() => LinuxExecutable.Verify(Path.Combine(folder, "client"), digest, false).Dispose()); }
    finally { File.SetUnixFileMode(folder, (UnixFileMode)0x1c0); }
});
Check("Wrong architecture, class, truncated program headers and scripts refuse despite correct input checksums", () =>
{
    foreach (var name in new[] { "architecture", "class", "truncated", "script" })
    {
        var altered = (byte[])bytes.Clone();
        if (name == "architecture") altered[18] = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (byte)62 : (byte)183;
        if (name == "class") altered[4] = 1;
        if (name == "truncated") altered = altered[..64];
        if (name == "script") altered = Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n");
        var path = Fixture(name, altered); var checksum = Convert.ToHexString(SHA256.HashData(altered));
        Refused(() => LinuxExecutable.Verify(path, checksum, false).Dispose());
    }
});
Check("Named-pipe collision refuses promptly and preserves its native type", () =>
{
    var path = Path.Combine(root, "client.fifo"); Assert(NativeFixture.mkfifo(path, 0x1c0) == 0);
    var clock = Stopwatch.StartNew(); Refused(() => LinuxExecutable.Verify(path, digest, false).Dispose()); Assert(clock.Elapsed < TimeSpan.FromSeconds(2) && NativeFixture.IsPipe(path));
});
Check("Setuid and setgid executables are not eligible for a client lease", () =>
{
    foreach (var mode in new[] { 0x940, 0x540 })
    { File.SetUnixFileMode(selected, (UnixFileMode)mode); try { Refused(() => LinuxExecutable.Verify(selected, digest, false).Dispose()); } finally { File.SetUnixFileMode(selected, (UnixFileMode)0x140); } }
});
Check("File capabilities cannot elevate an explicitly selected client", () =>
    Refused(() => LinuxExecutable.Verify(Path.Combine(root, "capability-client"), digest, false).Dispose()));
Check("Metadata or mode changes invalidate the execution descriptor before launch", () =>
{
    using var executable = LinuxExecutable.Verify(selected, digest, false); File.SetUnixFileMode(selected, (UnixFileMode)0x180);
    try { Refused(() => _ = executable.ExecutionPath); } finally { File.SetUnixFileMode(selected, (UnixFileMode)0x140); }
});
Check("A disposed lease cannot provide an execution descriptor", () =>
{
    var executable = LinuxExecutable.Verify(selected, digest, false); executable.Dispose();
    try { _ = executable.ExecutionPath; } catch (ObjectDisposedException) { return; } throw new InvalidOperationException("Disposed executable was usable.");
});
Check("Cancelled verification cannot publish an executable lease", () =>
{
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { LinuxClientDiscovery.Open(new(selected, digest), cancellation.Token).Dispose(); }
    catch (OperationCanceledException) { return; } throw new InvalidOperationException("Cancelled verification was accepted.");
});
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, architecture = RuntimeInformation.ProcessArchitecture.ToString(), checks,
    scope = "Actual pinned official system package plus owned native trust-negative fixtures only. No personal home, real credentials, installed app lifecycle, authentication or handoff." }));

internal static class NativeFixture
{
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat { [FieldOffset(20)] public uint Uid; [FieldOffset(28)] public ushort Mode; }
    internal static bool OwnedByRegularUser(string path) => getuid() != 0 && statx(-100, path, 0x100, 0xb, out var value) == 0 && value.Uid == getuid();
    internal static bool IsPipe(string path) => statx(-100, path, 0x100, 1, out var value) == 0 && (value.Mode & 0xf000) == 0x1000;
    [DllImport("libc", SetLastError = true)] internal static extern int mkfifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
    [DllImport("libc")] private static extern uint getuid();
}
