using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

[SupportedOSPlatform("windows")]
internal static class WindowsAccountWindows
{
    private const string Marker = "--codex-switch-account-window";
    private const string ProfileArgument = "--user-data-dir=";
    private sealed record Root(int Pid, long Created, string? Directory, bool Managed);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool FlashWindow(IntPtr window, bool invert);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);

    private static string[] Arguments(string line)
    {
        var argv = CommandLineToArgvW(line, out var count);
        if (argv == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++) args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return args;
        }
        finally { LocalFree(argv); }
    }

    private static List<Root> Roots(string executable, bool requireComplete = false)
    {
        using var self = Process.GetCurrentProcess();
        var name = Path.GetFileName(executable);
        if (!name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) && !name.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected Codex desktop executable.");
        using var query = new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId,ExecutablePath,CommandLine,CreationDate FROM Win32_Process " +
            $"WHERE (Name='ChatGPT.exe' OR Name='Codex.exe') AND SessionId={self.SessionId}");
        query.Options.Timeout = TimeSpan.FromSeconds(4);
        using var rows = query.Get();
        var processes = new Dictionary<int, (int Parent, string? Command, DateTime Created)>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                if (row["ExecutablePath"] is string path && !string.Equals(path, executable, StringComparison.OrdinalIgnoreCase) &&
                    !RuntimeLocator.IsWindowsDesktopExecutable(path)) continue;
                processes[(int)(uint)row["ProcessId"]] = ((int)(uint)row["ParentProcessId"], row["CommandLine"] as string,
                    ManagementDateTimeConverter.ToDateTime((string)row["CreationDate"]).ToUniversalTime());
            }
        }
        var roots = new List<Root>();
        foreach (var (pid, item) in processes)
        {
            if (processes.TryGetValue(item.Parent, out var parent) && parent.Created <= item.Created) continue;
            if (string.IsNullOrWhiteSpace(item.Command) && !requireComplete) continue;
            WindowsProcessImage? candidate;
            try { candidate = WindowsProcessImage.Read(pid); }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5 && !requireComplete &&
                (item.Command == null || !Arguments(item.Command).Contains(Marker, StringComparer.Ordinal))) { continue; }
            if (candidate is not { } image) continue;
            if (!image.MatchesPath(executable) && !RuntimeLocator.IsWindowsDesktopExecutable(image.Path)) continue;
            // WMI reports microseconds, GetProcessTimes reports 100 ns ticks.
            if (DateTime.FromFileTimeUtc(image.CreationTime).Ticks / 10 != item.Created.Ticks / 10) continue;
            if (string.IsNullOrEmpty(item.Command)) throw new InvalidOperationException("Codex window ownership is unavailable.");
            var args = Arguments(item.Command);
            if (args.Any(arg => arg.StartsWith("--type=", StringComparison.Ordinal))) continue;
            var dirs = args.Where(arg => arg.StartsWith(ProfileArgument, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (dirs.Length > 1) throw new InvalidOperationException("Ambiguous Codex desktop profile.");
            var dir = dirs.Length == 0 ? null : Path.GetFullPath(dirs[0].Substring(ProfileArgument.Length));
            roots.Add(new(pid, image.CreationTime, dir, args.Contains(Marker, StringComparer.Ordinal)));
        }
        return roots;
    }

    private static IntPtr Window(Root root)
    {
        try
        {
            using var process = Process.GetProcessById(root.Pid);
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var owner) == 0 || owner != root.Pid) return IntPtr.Zero;
            return WindowsProcessImage.Read(root.Pid)?.CreationTime == root.Created ? window : IntPtr.Zero;
        }
        catch (ArgumentException) { return IntPtr.Zero; } // PID exited between enumeration and window lookup.
    }

    internal static bool HasManagedWindow(string executable) => Roots(executable, true).Any(root => root.Managed);
    internal static Dictionary<string, int> Inventory(string executable)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots(executable))
        {
            if (!root.Managed && root.Directory != null) continue;
            var directory = root.Directory ?? "";
            if (!result.TryAdd(directory, root.Pid)) result[directory] = 0;
        }
        return result;
    }
    internal static bool UsesProfile(string executable, string profile) => Roots(executable, true).Any(root =>
        string.Equals(root.Directory, profile, StringComparison.OrdinalIgnoreCase));

    internal static bool DescendsFrom(int child, int parent)
    {
        var seen = new HashSet<int>();
        for (var pid = child; pid > 0 && seen.Count < 24 && seen.Add(pid);)
        {
            using var item = new ManagementObject("Win32_Process.Handle='" + pid + "'");
            item.Get();
            var created = ManagementDateTimeConverter.ToDateTime((string)item["CreationDate"]).ToUniversalTime();
            pid = (int)(uint)item["ParentProcessId"];
            if (WindowsProcessImage.Read(pid) is not { } ancestor || DateTime.FromFileTimeUtc(ancestor.CreationTime) > created.AddMilliseconds(1))
                return false;
            if (pid == parent) return true;
        }
        return false;
    }

    internal static Task OpenAsync(string executable, string? profile, string home, string backend, Action prepare,
        Func<int, CancellationToken, Task> verify, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var requested = false;
            var observed = false;
            var recoveredExit = false;
            var waiting = Stopwatch.StartNew();
            void Launch()
            {
                if (profile == null) { RuntimeLocator.LaunchDesktop(executable); return; }
                var image = executable;
                var package = Path.GetDirectoryName(Path.GetDirectoryName(executable));
                if (package != null && Path.GetFileName(package).StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(package).EndsWith("_2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase))
                {
                    image = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "WindowsApps", "OpenAI.Codex_2p2nqsd0c76g0", "chatgpt-desktop.exe");
                    if (!File.Exists(image)) throw new FileNotFoundException("switch.window_alias");
                }
                var start = new ProcessStartInfo(image) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
                start.ArgumentList.Add(ProfileArgument + profile);
                start.ArgumentList.Add(Marker);
                start.Environment["CODEX_ELECTRON_USER_DATA_PATH"] = profile;
                start.Environment["CODEX_HOME"] = home;
                start.Environment["CODEX_SWITCH_WINDOW_HOME"] = home;
                start.Environment["CODEX_CLI_PATH"] = backend;
                start.Environment["CODEX_SWITCH_BACKEND_CONFIG"] = Path.Combine(home, "window-backend.json");
                start.Environment["CODEX_ELECTRON_START_IN_BACKGROUND"] = "0";
                if (!File.Exists(backend)) throw new FileNotFoundException("Codex desktop runtime is missing.");
                using var child = Process.Start(start) ?? throw new IOException("Codex did not start.");
            }
            try
            {
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var roots = Roots(executable);
                    var matches = roots.Where(root => root.Managed == (profile != null) &&
                        string.Equals(root.Directory, profile, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (profile == null && matches.Length == 0 && roots.Any(root => !root.Managed))
                        throw new InvalidOperationException("switch.window_identity");
                    if (profile != null && roots.Any(root => !root.Managed && string.Equals(root.Directory, profile, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("switch.window_identity");
                    if (matches.Length > 1) throw new InvalidOperationException("Multiple Codex windows claim this profile.");
                    if (matches.Length == 1)
                    {
                        observed = true;
                        var root = matches[0];
                        var window = Window(root);
                        if (window != IntPtr.Zero)
                        {
                            try { await verify(root.Pid, deadline.Token); }
                            catch (TimeoutException) { await Task.Delay(200, deadline.Token); continue; }
                            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 109)
                            { await Task.Delay(200, deadline.Token); continue; }
                            if (Window(root) != window) continue;
                            AllowSetForegroundWindow(root.Pid);
                            if (IsIconic(window)) ShowWindowAsync(window, 9);
                            SetForegroundWindow(window);
                            for (var i = 0; i < 30; i++)
                            {
                                var front = GetForegroundWindow();
                                GetWindowThreadProcessId(GetAncestor(front, 3), out var foreground);
                                if (foreground == root.Pid) return;
                                if (i == 9 && !requested && Window(root) == window)
                                {
                                    Launch(); requested = true;
                                }
                                await Task.Delay(50, deadline.Token);
                            }
                            if (Window(root) == window) FlashWindow(window, true);
                            throw new InvalidOperationException("switch.window_focus");
                        }
                        if (!requested && waiting.Elapsed > TimeSpan.FromSeconds(3))
                        {
                            try { await verify(root.Pid, deadline.Token); }
                            catch (TimeoutException) { await Task.Delay(200, deadline.Token); continue; }
                            AllowSetForegroundWindow(root.Pid);
                            Launch(); requested = true;
                        }
                    }
                    else if (!requested)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        try { prepare(); }
                        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
                        { await Task.Delay(200, deadline.Token); continue; }
                        Launch();
                        requested = true; observed = false;
                    }
                    else if (observed)
                    {
                        if (recoveredExit) throw new IOException("switch.window_lost");
                        recoveredExit = true; requested = false; observed = false;
                    }
                    await Task.Delay(450, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new TimeoutException("switch.window_wait"); }
        }, cancellation);
}
