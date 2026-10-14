using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodexAccountDesk;

internal static class Program
{
    internal sealed class Options
    {
        public string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountDesk");
        public string CodexHome { get; set; } = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        public string? ReportPath { get; set; }
        public string? PreviewPath { get; set; }
        public bool SelfTest { get; set; }
        public bool ExitExisting { get; set; }
        public string? LanguageOverride { get; set; }
        public string? TrendPreviewDirectory { get; set; }
        public string? TankPreviewDirectory { get; set; }
        public string? SessionReportPath { get; set; }
        public string? QueueReportPath { get; set; }
        public string? SwitchProtocolReportPath { get; set; }
        public string? SwitchAccount { get; set; }
        public string? HardwareReportPath { get; set; }
        public string? HardwarePreviewDirectory { get; set; }
        public string? RecordDirectory { get; set; }
        public string? IconDirectory { get; set; }
        public static Options Parse(string[] arguments)
        {
            var options = new Options();
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                if (argument == "--self-test") { options.SelfTest = true; continue; }
                if (argument == "--request-exit") { options.ExitExisting = true; continue; }
                if (i + 1 >= arguments.Length) throw new ArgumentException("명령행 옵션 값이 없습니다.");
                var value = arguments[++i];
                switch (argument)
                {
                    case "--data-dir": options.DataDirectory = Path.GetFullPath(value); break;
                    case "--codex-home": options.CodexHome = Path.GetFullPath(value); break;
                    case "--verify-readonly": options.ReportPath = Path.GetFullPath(value); break;
                    case "--preview": options.PreviewPath = Path.GetFullPath(value); break;
                    case "--language": options.LanguageOverride = value; break;
                    case "--preview-trends": options.TrendPreviewDirectory = Path.GetFullPath(value); break;
                    case "--preview-tanks": options.TankPreviewDirectory = Path.GetFullPath(value); break;
                    case "--verify-sessions": options.SessionReportPath = Path.GetFullPath(value); break;
                    case "--verify-queues": options.QueueReportPath = Path.GetFullPath(value); break;
                    case "--verify-switch-protocol": options.SwitchProtocolReportPath = Path.GetFullPath(value); break;
                    case "--switch-account": options.SwitchAccount = value; break;
                    case "--verify-resources": options.HardwareReportPath = Path.GetFullPath(value); break;
                    case "--preview-resources": options.HardwarePreviewDirectory = Path.GetFullPath(value); break;
                    case "--record-dashboard": options.RecordDirectory = Path.GetFullPath(value); break;
                    case "--export-icons": options.IconDirectory = Path.GetFullPath(value); break;
                    default: throw new ArgumentException("알 수 없는 명령행 옵션입니다.");
                }
            }
            if (options.PreviewPath != null && options.ReportPath == null) throw new ArgumentException("미리보기는 조회 검증 모드에서 사용할 수 있습니다.");
            if (options.SwitchAccount != null && options.ReportPath != null) throw new ArgumentException("조회 검증 모드에서 계정을 전환할 수 없습니다.");
            if (options.RecordDirectory != null && (options.SwitchAccount != null || options.ReportPath != null || options.ExitExisting || options.IconDirectory != null || options.SelfTest))
                throw new ArgumentException("Choose dashboard recording separately from other commands.");
            return options;
        }
    }
    [STAThread]
    public static int Main(string[] arguments)
    {
        Loc.SetLanguage("en");
        try
        {
            var options = Options.Parse(arguments);
            if (options.IconDirectory != null) { AppIdentity.ExportIcons(options.IconDirectory); return 0; }
            if (options.SelfTest) return Diagnostics.RunSelfTests(options.ReportPath ?? Path.Combine(options.DataDirectory, "self-tests.json"));
            if (options.SessionReportPath != null) return Diagnostics.VerifySessionsAsync(options.CodexHome, options.SessionReportPath).GetAwaiter().GetResult();
            if (options.QueueReportPath != null) return QueueDiagnostics.VerifyAsync(options.CodexHome, options.QueueReportPath).GetAwaiter().GetResult();
            if (options.SwitchProtocolReportPath != null) return SwitchProtocolDiagnostics.VerifyAsync(options.SwitchProtocolReportPath).GetAwaiter().GetResult();
            if (options.HardwareReportPath != null) return HardwareDiagnostics.VerifyAsync(options.HardwareReportPath).GetAwaiter().GetResult();
            if (options.HardwarePreviewDirectory != null)
            {
                var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                HardwareDiagnostics.CaptureScenarios(options.HardwarePreviewDirectory); previewApp.Shutdown(); return 0;
            }
            if (options.TrendPreviewDirectory != null)
            {
                var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                TrendDiagnostics.CaptureScenarios(options.TrendPreviewDirectory); previewApp.Shutdown(); return 0;
            }
            if (options.TankPreviewDirectory != null)
            {
                var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                TankDiagnostics.CaptureScenarios(options.TankPreviewDirectory); previewApp.Shutdown(); return 0;
            }
            var namespaceKey = SecureFiles.Hash(WindowsIdentity.GetCurrent().User?.Value + "\n" + Path.GetFullPath(options.DataDirectory))[..24];
            if (options.ExitExisting) { RequestExisting(namespaceKey, "exit"); return 0; }
            if (options.RecordDirectory != null) { RequestExisting(namespaceKey, "record:" + options.RecordDirectory); return 0; }
            using var mutex = new Mutex(true, "Local\\CodexAccountDesk-" + namespaceKey, out var firstInstance);
            if (!firstInstance) { RequestExisting(namespaceKey, options.SwitchAccount == null ? "activate" : "switch:" + options.SwitchAccount); return 0; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var vault = new AccountVault(options.DataDirectory);
            Loc.SetLanguage(options.LanguageOverride ?? vault.Data.Language);
            if (options.LanguageOverride != null) { vault.Data.Language = options.LanguageOverride; vault.Save(); }
            var service = new AccountService(vault, options.CodexHome);
            Diagnostics.BeginReadOnlyProbe(service);
            var window = new MainWindow(service, options);
            using var stop = new CancellationTokenSource();
            _ = ListenForActivationAsync(namespaceKey, app, window, stop.Token);
            app.DispatcherUnhandledException += (_, exception) =>
            {
                exception.Handled = true;
                window.ShowError(exception.Exception);
            };
            app.Run(window); stop.Cancel();
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(Loc.Error(UserErrors.Describe(error)), "Codex Switch", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static void RequestExisting(string key, string command)
    {
        using var client = new NamedPipeClientStream(".", "CodexAccountDesk-" + key, PipeDirection.Out);
        try { client.Connect(1500); client.Write(Encoding.UTF8.GetBytes(command)); }
        catch (TimeoutException) { MessageBox.Show(Loc.T("프로그램이 이미 실행 중입니다. 트레이에서 Codex Switch를 열어 주세요."), "Codex Switch"); }
    }
    private static async Task ListenForActivationAsync(string key, Application app, MainWindow window, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream("CodexAccountDesk-" + key, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                var buffer = new byte[1024]; var length = 0;
                while (length < buffer.Length)
                {
                    var count = await pipe.ReadAsync(buffer.AsMemory(length), cancellation);
                    if (count == 0) break; length += count;
                }
                if (length == buffer.Length) throw new InvalidDataException("프로그램 요청이 너무 깁니다.");
                var command = Encoding.UTF8.GetString(buffer, 0, length);
                if (command == "activate") await app.Dispatcher.InvokeAsync(window.BringToFront);
                else if (command == "exit") await app.Dispatcher.InvokeAsync(window.ExitAsync).Task.Unwrap();
                else if (command.StartsWith("switch:", StringComparison.Ordinal)) await app.Dispatcher.InvokeAsync(() => window.SwitchToAccountAsync(command[7..])).Task.Unwrap();
                else if (command.StartsWith("record:", StringComparison.Ordinal)) await app.Dispatcher.InvokeAsync(() => DashboardRecording.RecordAsync(window, command[7..])).Task.Unwrap();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
