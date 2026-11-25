using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAccountDesk;

internal static class AccountWindowContracts
{
    internal static async Task<int> RunAsync(string backend)
    {
        backend = Path.GetFullPath(backend);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexSwitch-WindowTests", Guid.NewGuid().ToString("N"));
        SecureFiles.CreatePrivateDirectory(root);
        var install = await RuntimeLocator.FindAsync(default);
        var native = Path.Combine(Path.GetDirectoryName(install.DesktopPath)!, "resources", "codex.exe");
        var owned = new Dictionary<string, Process>();
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("ALL_PROXY", "http://127.0.0.1:9");
        try
        {
            var previousBravo = 0;
            foreach (var step in new[] { "alpha", "bravo", "alpha", "reopen-bravo" })
            {
                var label = step == "reopen-bravo" ? "bravo" : step;
                if (step == "reopen-bravo")
                {
                    var closed = owned[label];
                    previousBravo = closed.Id;
                    closed.CloseMainWindow();
                    if (!closed.WaitForExit(5000)) { closed.Kill(true); closed.WaitForExit(); }
                    closed.Dispose(); owned.Remove(label);
                }
                var accountId = "fixture-" + label;
                var userId = "fixture-user-" + label;
                var claims = new Dictionary<string, object>
                {
                    ["sub"] = userId, ["email"] = label + "@example.invalid", ["exp"] = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                    ["https://api.openai.com/auth"] = new { chatgpt_user_id = userId, chatgpt_account_id = accountId, chatgpt_plan_type = "free" }
                };
                var token = "e30." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims)).TrimEnd('=').Replace('+','-').Replace('/','_') + ".fixture";
                var account = AuthDocument.Parse(JsonSerializer.Serialize(new { auth_mode = "chatgpt", last_refresh = DateTimeOffset.UtcNow,
                    tokens = new { account_id = accountId, id_token = token, access_token = token, refresh_token = "fictional" } }));
                var profile = new AccountWindowProfile(root, account.Key);
                var launchCalls = 0;
                void Prepare()
                {
                    launchCalls++;
                    profile.Prepare(account);
                    SecureFiles.AtomicWrite(Path.Combine(profile.Home, "window-backend.json"), JsonSerializer.SerializeToUtf8Bytes(
                        new DesktopBackendHost.Configuration(native, profile.Home, profile.Home, backend)));
                }
                var reused = owned.ContainsKey(label);
                var verified = false;
                async Task Verify(int pid, CancellationToken cancellation)
                {
                    if (owned.TryGetValue(label, out var existing))
                    {
                        if (existing.Id != pid) throw new Exception("Unexpected replacement of a running account.");
                    }
                    else owned.Add(label, Process.GetProcessById(pid));
                    await AccountWindowIdentity.VerifyAsync(profile.Home, account.Key, pid, cancellation);
                    verified = true;
                }
                await FromForegroundButtonAsync(() => WindowsAccountWindows.OpenAsync(
                    install.DesktopPath, profile.Desktop, profile.Home, backend, Prepare, Verify, default));
                if (!verified || launchCalls != (reused ? 0 : 1)) throw new Exception("Account window verification or launch count failed.");
                if (step == "reopen-bravo" && owned[label].Id == previousBravo) throw new Exception("The lost window was not replaced.");
                Console.WriteLine($"{step}: verified, launches={launchCalls}, pid={owned[label].Id}");
            }
            if (owned.Values.Any(process => process.HasExited) || owned.Values.Select(process => process.Id).Distinct().Count() != 2)
                throw new Exception("Both account windows must remain alive.");
            return 0;
        }
        finally
        {
            foreach (var process in owned.Values)
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(); }
                }
                process.Dispose();
            }
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    private static Task FromForegroundButtonAsync(Func<Task> action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form { Text = "Codex Switch window test", TopMost = true, StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(20, 30), Size = new System.Drawing.Size(220, 130) };
            using var button = new Button { Text = "Activate test account", Dock = DockStyle.Fill };
            form.Controls.Add(button);
            var previous = Cursor.Position;
            var clickPoint = previous;
            using var deadline = new System.Windows.Forms.Timer { Interval = 50000 };
            using var ready = new System.Windows.Forms.Timer { Interval = 200 };
            deadline.Tick += (_, _) => { result.TrySetException(new TimeoutException("The foreground button timed out.")); form.Close(); };
            button.Click += async (_, _) =>
            {
                ready.Stop();
                if (Cursor.Position == clickPoint) Cursor.Position = previous;
                button.Enabled = false; form.TopMost = false;
                try { await action(); result.TrySetResult(); }
                catch (Exception error) { result.TrySetException(error); }
                finally { form.Close(); }
            };
            var attempts = 0;
            ready.Tick += (_, _) =>
            {
                if (GetForegroundWindow() == form.Handle) { ready.Stop(); button.PerformClick(); return; }
                var point = button.PointToScreen(new System.Drawing.Point(button.Width / 2, button.Height / 2));
                previous = Cursor.Position; clickPoint = point;
                var window = WindowFromPoint(point);
                GetWindowThreadProcessId(window, out var owner);
                if (GetAncestor(window, 2) == form.Handle && owner == Environment.ProcessId)
                {
                    Cursor.Position = point;
                    if (GetAncestor(WindowFromPoint(point), 2) == form.Handle)
                    { mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
                    else { result.TrySetException(new Exception("The test button moved.")); form.Close(); }
                }
                else if (++attempts >= 20)
                {
                    ready.Stop();
                    result.TrySetException(new InvalidOperationException($"Test button unavailable in input desktop (hit PID {owner}, target PID {Environment.ProcessId}, position {point})."));
                    form.Close();
                }
            };
            form.Shown += (_, _) => { form.Activate(); ready.Start(); };
            deadline.Start(); Application.Run(form);
        });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task;
    }
}
