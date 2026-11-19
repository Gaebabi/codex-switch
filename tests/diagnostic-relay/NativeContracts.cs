using System.Diagnostics;
using System.Text.Json;
using CodexSwitch.Diagnostics;

internal static class NativeContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var installations = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        var executable = Environment.GetEnvironmentVariable("CODEX_ACCOUNT_DESK_CLI") ?? Directory.EnumerateFiles(installations, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
        var home = Path.Combine(root, "native-home"); Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml");
        await File.WriteAllTextAsync(config, "cli_auth_credentials_store=\"file\"\n");
        var original = await File.ReadAllBytesAsync(config);
        const string endpoint = "http://127.0.0.1:1234/native-contract";
        foreach (var active in new[] { true, false })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var start = NativeRelayProcess.StartInfo(executable, ["app-server"], active ? endpoint : null);
            start.Environment["CODEX_HOME"] = home;
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"diagnostic_contract\",\"version\":\"1\"},\"capabilities\":{\"experimentalApi\":true}}}");
                await process.StandardInput.FlushAsync();
                async Task<JsonElement> Response(int id)
                {
                    while (await process.StandardOutput.ReadLineAsync(deadline.Token) is string line)
                    {
                        using var document = JsonDocument.Parse(line);
                        if (document.RootElement.TryGetProperty("id", out var actual) && actual.ValueKind == JsonValueKind.Number && actual.GetInt32() == id) return document.RootElement.Clone();
                    }
                    throw new IOException("The fixture native server exited before its response.");
                }
                var initialized = await Response(1);
                check("Actual native engine accepts " + (active ? "ON" : "OFF") + " startup without a Switch custom backend", initialized.TryGetProperty("result", out _));
                await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
                await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"config/read\",\"params\":{\"includeLayers\":true}}"); await process.StandardInput.FlushAsync();
                var read = await Response(2);
                check("Actual native effective endpoint is " + (active ? "diagnostic" : "original"), read.TryGetProperty("result", out _) && read.GetRawText().Contains(endpoint) == active);
                process.StandardInput.Close();
                await process.WaitForExitAsync(deadline.Token);
                var after = await File.ReadAllBytesAsync(config);
                check("Actual native startup leaves root configuration unchanged", original.SequenceEqual(after));
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } await stderr; }
        }
    }
}
