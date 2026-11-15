using CodexAccountDesk;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class BackendEncodingContracts
{
    internal static async Task Run(string backend, string home, CancellationToken cancellation, Action<string, bool> check)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var start = new ProcessStartInfo(backend)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = home,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--listen");
        start.ArgumentList.Add("stdio://");
        start.Environment["CODEX_HOME"] = home;
        foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL" }) start.Environment.Remove(name);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(token);
        async Task<JsonElement> Exchange(string request)
        {
            await process.StandardInput.WriteLineAsync(request.AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            while (await process.StandardOutput.ReadLineAsync(token) is string line)
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out _)) return document.RootElement.Clone();
            }
            throw new IOException("Encoding fixture backend closed before replying.");
        }
        try
        {
            var initialized = await Exchange("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"encoding_contract","version":"1"}}}""");
            check("The encoding fixture initializes the actual backend", initialized.TryGetProperty("result", out _));
            const string label = "사용자 메시지 한글 깨짐 / 日本語 / 中文 / café / 😀";
            var escaped = await Exchange(JsonSerializer.Serialize(new { id = label, method = "config/read", @params = new { includeLayers = false } }));
            check("ASCII-escaped Unicode survives the actual backend transport", escaped.GetProperty("id").GetString() == label && escaped.TryGetProperty("result", out _));
            var literal = await Exchange("""{"id":"사용자 메시지 한글 깨짐 / 日本語 / 中文 / café / 😀","method":"config/read","params":{"includeLayers":false}}""");
            check("Literal UTF-8 Korean, Japanese, Chinese, accents and emoji survive the actual backend transport", literal.GetProperty("id").GetString() == label && literal.TryGetProperty("result", out _));
            byte[] malformed = [.. Encoding.ASCII.GetBytes("{\"id\":\""), 0xff, .. Encoding.ASCII.GetBytes("\",\"method\":\"config/read\",\"params\":{\"includeLayers\":false}}\n")];
            await process.StandardInput.BaseStream.WriteAsync(malformed, token);
            await process.StandardInput.BaseStream.FlushAsync(token);
            await process.WaitForExitAsync(token);
            check("Malformed UTF-8 is refused instead of silently replacing input characters", process.ExitCode != 0);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(stop.Token); }
                catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
            }
            await errors;
        }
    }
}
