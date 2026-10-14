using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CodexAccountDesk;

internal static class MacDesktop
{
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private static readonly Lazy<IntPtr> AppKitHandle = new(() => NativeLibrary.Load(AppKit));
    private static readonly ConcurrentDictionary<string, string> BundleIds = new(StringComparer.Ordinal);
    public static async Task<CodexInstallation> FindAsync(CancellationToken cancellation)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS desktop discovery is only available on macOS.");
        _ = AppKitHandle.Value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var bundles = new List<string>();
        var explicitDesktop = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_DESKTOP");
        var overrideBundle = string.IsNullOrWhiteSpace(explicitDesktop) ? null : BundleForExecutable(Path.GetFullPath(explicitDesktop));
        if (overrideBundle != null) bundles.Add(overrideBundle);
        WithPool(() =>
        {
            var applications = Send(GetClass("NSRunningApplication"), "runningApplicationsWithBundleIdentifier:", NativeString("com.openai.codex"));
            for (nuint index = 0; index < Count(applications); index++)
            {
                var app = At(applications, index);
                var path = String(Send(Send(app, "bundleURL"), "path"));
                if (path != null) bundles.Add(path);
            }
            return true;
        });
        bundles.AddRange(new[] { "/Applications/Codex.app", Path.Combine(home, "Applications", "Codex.app"), "/Applications/ChatGPT.app" });
        string desktop = "";
        foreach (var bundle in bundles.Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(bundle)) continue;
            var info = await ReadInfoAsync(bundle, cancellation);
            if (bundle != overrideBundle && info.GetValueOrDefault("CFBundleIdentifier") != "com.openai.codex") continue;
            var executable = info.GetValueOrDefault("CFBundleExecutable") ?? throw new InvalidDataException("Codex app bundle executable missing.");
            if (Path.GetFileName(executable) != executable) throw new InvalidDataException("Invalid Codex bundle executable.");
            var canonicalBundle = new DirectoryInfo(bundle).ResolveLinkTarget(true)?.FullName ?? bundle;
            var candidate = Path.Combine(canonicalBundle, "Contents", "MacOS", executable);
            if (File.Exists(candidate)) { desktop = candidate; BundleIds[candidate] = info.GetValueOrDefault("CFBundleIdentifier") ?? "com.openai.codex"; break; }
        }
        if (overrideBundle != null && desktop == "") throw new FileNotFoundException("지정한 Codex 앱 경로가 없습니다.");
        var candidates = new List<string>();
        var explicitCli = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI");
        if (!string.IsNullOrWhiteSpace(explicitCli)) candidates.Add(Path.GetFullPath(explicitCli));
        if (desktop != "")
        {
            var resources = Path.Combine(BundleForExecutable(desktop), "Contents", "Resources");
            candidates.Add(Path.Combine(resources, "codex"));
            if (Directory.Exists(resources)) candidates.AddRange(Directory.GetFiles(resources, "codex-*-apple-darwin"));
        }
        candidates.AddRange(new[] { "/opt/homebrew/bin/codex", "/usr/local/bin/codex" });
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => p.Length > 0).Select(p => Path.Combine(p, "codex")));
        var cli = candidates.FirstOrDefault(IsExecutable)
            ?? throw new FileNotFoundException("Codex가 설치되지 않았습니다. Codex 데스크톱 앱 또는 CLI를 먼저 설치해 주세요.");
        return new(Path.GetFullPath(cli), desktop);
    }
    private static bool IsExecutable(string path) => OperatingSystem.IsMacOS() && File.Exists(path) && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    private static async Task<Dictionary<string, string>> ReadInfoAsync(string bundle, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo("/usr/bin/plutil") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-convert", "xml1", "-o", "-", Path.Combine(bundle, "Contents", "Info.plist") }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot read Codex app bundle.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation); var errors = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation); await errors;
        if (process.ExitCode != 0) throw new IOException("Cannot read Codex app bundle metadata.");
        var elements = XDocument.Parse(await output).Root?.Element("dict")?.Elements().ToArray() ?? throw new InvalidDataException("Invalid app bundle metadata.");
        var result = new Dictionary<string, string>();
        for (var i = 0; i + 1 < elements.Length; i += 2) if (elements[i].Name == "key" && elements[i + 1].Name == "string") result[elements[i].Value] = elements[i + 1].Value;
        return result;
    }
    public static string BundleForExecutable(string executable)
    {
        var path = Path.GetFullPath(executable);
        while (path.Length > 1)
        {
            if (path.EndsWith(".app", StringComparison.Ordinal)) return path;
            path = Path.GetDirectoryName(path) ?? "/";
        }
        throw new InvalidDataException("CODEX_ACCOUNT_DESK_DESKTOP must point to a Codex .app or its executable.");
    }
    private static int[] Running(string executable)
    {
        if (string.IsNullOrEmpty(executable)) return Array.Empty<int>();
        _ = AppKitHandle.Value;
        return WithPool(() =>
        {
            var apps = Send(GetClass("NSRunningApplication"), "runningApplicationsWithBundleIdentifier:", NativeString(BundleIds.GetValueOrDefault(executable) ?? "com.openai.codex"));
            var result = new List<int>();
            for (nuint i = 0; i < Count(apps); i++)
            {
                var app = At(apps, i);
                var path = String(Send(Send(app, "executableURL"), "path"));
                if (path == executable) result.Add(checked((int)MsgCount(app, Selector("processIdentifier"))));
            }
            return result.ToArray();
        });
    }
    public static bool IsRunning(string executable) => Running(executable).Length > 0;
    public static async Task CloseAsync(string executable, CancellationToken cancellation)
    {
        var applications = Running(executable);
        foreach (var pid in applications)
        {
            cancellation.ThrowIfCancellationRequested();
            var accepted = WithPool(() =>
            {
                var application = MsgPid(GetClass("NSRunningApplication"), Selector("runningApplicationWithProcessIdentifier:"), pid);
                return application == IntPtr.Zero || SendBool(application, Selector("terminate")) != 0;
            });
            if (!accepted) throw new IOException("Codex를 정상 종료하지 못했습니다. Codex 메뉴에서 종료한 뒤 다시 전환해 주세요.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { while (IsRunning(executable)) await Task.Delay(150, timeout.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException("Codex가 아직 실행 중입니다. Codex 메뉴에서 완전히 종료해 주세요."); }
    }
    public static void Launch(string executable, string? url = null)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Codex 데스크톱 앱을 찾을 수 없습니다.");
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        start.ArgumentList.Add("-a"); start.ArgumentList.Add(BundleForExecutable(executable));
        if (url != null) start.ArgumentList.Add(url);
        Process.Start(start)?.Dispose();
    }
    public static void VerifyPeer(System.Net.Sockets.Socket socket)
    {
        var credentials = new byte[128]; uint length = (uint)credentials.Length;
        if (getsockopt(socket.Handle.ToInt32(), 0, 1, credentials, ref length) != 0 || length < 8 || BitConverter.ToUInt32(credentials, 4) != getuid())
            throw new UnauthorizedAccessException("Codex IPC peer does not belong to the current user.");
    }
    private static T WithPool<T>(Func<T> action)
    {
        var pool = Send(Send(GetClass("NSAutoreleasePool"), "alloc"), "init");
        try { return action(); }
        finally { Send(pool, "drain"); }
    }
    private static IntPtr NativeString(string value) => SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), value);
    private static string? String(IntPtr value) => value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(value, "UTF8String"));
    private static IntPtr Send(IntPtr value, string selector) => Msg(value, Selector(selector));
    private static IntPtr Send(IntPtr value, string selector, IntPtr argument) => MsgArg(value, Selector(selector), argument);
    private static nuint Count(IntPtr array) => MsgCount(array, Selector("count"));
    private static IntPtr At(IntPtr array, nuint index) => MsgIndex(array, Selector("objectAtIndex:"), index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")] private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr MsgArg(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nuint MsgCount(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr MsgIndex(IntPtr receiver, IntPtr selector, nuint index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern byte SendBool(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr MsgPid(IntPtr receiver, IntPtr selector, int pid);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int getsockopt(int socket, int level, int option, byte[] value, ref uint length);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint getuid();
}
