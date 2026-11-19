using System.Diagnostics;
using System.Text.Json;
using CodexSwitch.Diagnostics;
using CodexAccountDesk;

return await Entry.Run(args);

namespace CodexSwitch.Diagnostics
{
    internal static class Entry
    {
        internal static async Task<int> Run(string[] args)
        {
            try
            {
                var state = new RelayInstallation(home: args.Length == 4 && args[0] == "on" && args[2] == "--home" ? Path.GetFullPath(args[3]) : null);
                if (args.Length == 0 || args[0] == "help")
                {
                    Console.WriteLine("Diagnostic Relay\non <original-codex.exe> | off | status | watch | upgrade\nChanges apply after a normal Codex restart. No process is stopped automatically.");
                    return 0;
                }
                if (args[0] == "on" && (args.Length == 2 || args.Length == 4 && args[2] == "--home")) { state.Enable(args[1]); Console.WriteLine("ON requested. Restart Codex normally when idle to activate Diagnostic Relay."); return 0; }
                if (args[0] == "off" && args.Length == 1) { state.Disable(); Console.WriteLine("OFF requested. Restart Codex normally when idle. The next launch uses the original native executable, without Switch."); return 0; }
                if (args[0] == "upgrade" && args.Length == 1) { state.Upgrade(); Console.WriteLine("Diagnostic installation updated. The requested ON/OFF state was preserved."); return 0; }
                if (args[0] == "status") { Console.WriteLine(JsonSerializer.Serialize(state.Status())); return 0; }
                if (args[0] == "watch")
                {
                    Console.WriteLine("Diagnostic Relay: metadata only. Ctrl+C stops this viewer, not Codex.");
                    var offsets = new Dictionary<string, long>();
                    while (true)
                    {
                        foreach (var path in state.EventFiles())
                        {
                            var offset = offsets.GetValueOrDefault(path);
                            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (file.Length < offset) offset = 0;
                            file.Position = offset;
                            using var reader = new StreamReader(file);
                            while (await reader.ReadLineAsync() is string line) Console.WriteLine(line);
                            offsets[path] = file.Position;
                        }
                        await Task.Delay(250);
                    }
                }
                var receipt = state.Read();
                // An old desktop may retain the launch override after OFF. Do not create a relay in that process.
                var diagnose = state.Enabled && args.Any(a => a is "app-server" or "exec-server" or "exec");
                var backend = diagnose && args.Contains("app-server", StringComparer.Ordinal) ? state.PairedBackend(receipt) : null;
                await using var relay = diagnose ? await DiagnosticRelay.Start(state.NewEvents()) : null;
                var start = NativeRelayProcess.StartInfo(backend ?? receipt.NativeExecutable, args, relay?.BaseUrl, receipt.CodexHome);
                if (backend != null)
                {
                    start.Environment["CODEX_SWITCH_BACKEND_CONFIG"] = Path.ChangeExtension(backend, ".json");
                    start.Environment[DiagnosticGatewayEndpoint.RelayUrlVariable] = relay!.BaseUrl;
                    start.Environment[DiagnosticGatewayEndpoint.KeyVariable] = Convert.ToHexString(relay.GatewayKey);
                }
                using var native = Process.Start(start) ?? throw new IOException("The Codex launch path did not start.");
                using var job = new CodexAccountDesk.OwnedProcessJob(native);
                var protocol = relay == null ? null : new ProtocolTelemetry(relay.Emit);
                await using var resources = relay == null ? null : new ResourceTelemetry(native, relay.Emit, backend == null ? "native_engine" : "switch_backend");
                resources?.Start();
                var output = protocol == null ? native.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput()) : protocol.Copy(native.StandardOutput.BaseStream, Console.OpenStandardOutput(), "native_to_client");
                var errors = native.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
                var input = Task.Run(async () =>
                {
                    try
                    {
                        if (protocol == null) await Console.OpenStandardInput().CopyToAsync(native.StandardInput.BaseStream);
                        else await protocol.Copy(Console.OpenStandardInput(), native.StandardInput.BaseStream, "client_to_native");
                    }
                    catch (IOException) when (native.HasExited) { }
                    finally { if (!native.HasExited) native.StandardInput.Close(); }
                });
                await native.WaitForExitAsync();
                await Task.WhenAll(output, errors);
                return native.ExitCode;
            }
            catch (Exception error) { Console.Error.WriteLine("Diagnostic Relay: " + error.GetType().Name + ": " + error.Message); return 1; }
        }

    }
}
