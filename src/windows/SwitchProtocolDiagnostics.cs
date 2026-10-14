using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class SwitchProtocolDiagnostics
{
    public static async Task<int> VerifyAsync(string reportPath)
    {
        var installation = await RuntimeLocator.FindAsync(CancellationToken.None);
        var directory = Path.Combine(Path.GetDirectoryName(reportPath)!, "switch-protocol");
        Directory.CreateDirectory(directory);
        await using var home = new RuntimeHome(directory, null);
        var workspace = Path.Combine(home.Path, "workspace"); Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(home.Path, "config.toml"), "model = \"protocol-fixture\"\nmodel_provider = \"protocol_local\"\nsandbox_mode = \"read-only\"\n[model_providers.protocol_local]\nname = \"Protocol fixture\"\nbase_url = \"http://127.0.0.1:9/v1\"\nwire_api = \"responses\"\nrequires_openai_auth = false\n", new UTF8Encoding(false));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var passed = false; string? error = null;
        try
        {
            string id;
            var name = "[261011] 테스트 채팅";
            var stamped = SwitchTitle.Stamp(name, DateTimeOffset.Parse("2026-10-12T01:00:00+09:00"));
            await using (var owner = new CodexRpc(installation.CliPath, home.Path, true))
            {
                await owner.InitializeAsync(cancellation.Token);
                var start = await owner.RequestAsync("thread/start", new { cwd = workspace }, cancellation.Token);
                id = start.GetProperty("thread").GetProperty("id").GetString()!;
                await owner.RequestAsync("thread/name/set", new { threadId = id, name }, cancellation.Token);
                await using var metadata = new CodexRpc(installation.CliPath, home.Path, true);
                await metadata.InitializeAsync(cancellation.Token);
                await metadata.RequestAsync("thread/name/set", new { threadId = id, name = stamped }, cancellation.Token);
                var read = await metadata.RequestAsync("thread/read", new { threadId = id, includeTurns = false }, cancellation.Token);
                if (JsonValue.String(read.GetProperty("thread"), "name") != stamped) throw new InvalidDataException("Cross-process name update did not persist.");
            }
            await using var resumed = new CodexRpc(installation.CliPath, home.Path, true);
            await resumed.InitializeAsync(cancellation.Token);
            var result = await resumed.RequestAsync("thread/resume", new { threadId = id, excludeTurns = true }, cancellation.Token);
            var thread = result.GetProperty("thread");
            passed = JsonValue.String(thread, "id") == id && JsonValue.String(thread, "name") == stamped && JsonValue.String(thread, "cwd") == workspace;
            if (!passed) throw new InvalidDataException("Persisted thread identity/name/workspace did not resume.");
        }
        catch (Exception failure) { error = failure.Message; }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = "1.21.0", passed, error,
            scope = "Isolated local protocol fixture. No inference turn, live account access, native shutdown, or live thread mutation." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
