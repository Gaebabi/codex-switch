using CodexAccountDesk;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

internal static class IsolatedProfileContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "isolated-contracts");
        var home = Path.Combine(root, "isolated-home"); Directory.CreateDirectory(home);
        var profile = Path.Combine(home, "codex-switch.config.toml");
        const string content = "# fictional owned profile\n";
        await File.WriteAllTextAsync(profile, content);
        var store = new ModelProviderStore(directory);
        store.Update(settings => { settings.IsolatedProfile = true; settings.Connected = true; settings.ConnectionWriteConfirmed = true; settings.RouterEnabled = true; settings.CustomProfilePath = profile; settings.CustomProfileContent = content; settings.CustomExecutablePath = Environment.ProcessPath; settings.CustomExecutableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))); });
        await File.WriteAllTextAsync(Path.Combine(directory, "model-providers-catalog.json"), new JsonObject { ["models"] = new JsonArray(ModelRouting.Metadata()) }.ToJsonString());
        var upstream = new Upstreams();
        GatewayIdentity? Authenticate(string? auth, string? account) => GatewayIdentity.Authenticate(auth, account,
            () => "{\"tokens\":{\"access_token\":\"fictional-isolated-token\",\"account_id\":\"fictional-isolated-account\"}}");
        await using var manager = new ModelProviderManager(directory, Authenticate, new HttpClient(upstream));
        var provider = await manager.SaveAsync(null, "Isolated custom", "https://a.example.invalid/v1", "fictional-custom-key", CancellationToken.None);
        manager.SelectRoute(provider.Models.Single().Alias(provider.Id)); manager.StartConfigured();
        using var client = ProviderCatalog.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fictional-isolated-token");
        client.DefaultRequestHeaders.Add("ChatGPT-Account-ID", "fictional-isolated-account");
        var before = upstream.Requests.Count;
        var catalog = JsonNode.Parse(await client.GetStringAsync(manager.BaseUrl + "/models?client_version=0.167.0"))!["models"]!.AsArray();
        check("Isolated catalog exposes only the virtual custom model", catalog.Count == 1 && catalog[0]!["slug"]!.GetValue<string>() == ModelRouting.Model);
        check("Isolated model discovery does not request the native OpenAI catalog", upstream.Requests.Count == before);
        foreach (var endpoint in new[] { "responses", "images/generations", "images/edits" })
        {
            using var response = await client.PostAsync(manager.BaseUrl + "/" + endpoint, new StringContent("{\"model\":\"fixture-openai\",\"input\":[]}", Encoding.UTF8, "application/json"));
            check("Isolated gateway explicitly refuses native endpoint " + endpoint, response.StatusCode == HttpStatusCode.BadRequest && (await response.Content.ReadAsStringAsync()).Contains("native Codex", StringComparison.OrdinalIgnoreCase));
        }
        check("Native refusals send no request to any provider", upstream.Requests.Count == before);
        using (var response = await client.PostAsync(manager.BaseUrl + "/responses", new StringContent("{\"model\":\"codex-switch\",\"stream\":true,\"input\":[]}", Encoding.UTF8, "application/json")))
            check("Isolated custom request succeeds", response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("response.completed"));
        check("Custom-only request uses only the provider key without native catalog metadata", upstream.Requests.Skip(before).All(request => request.Host == "a.example.invalid" && request.Token == "fictional-custom-key" && request.Account == null));
        var executable = Environment.ProcessPath!;
        var command = manager.GetCustomTerminalCommand(executable, home, true);
        check("Windows custom command fixes the named profile and avoids the shared daemon", command.Contains("--no-daemon --profile codex-switch") && command.Contains("EnvironmentVariables['CODEX_HOME'] = '") && !command.Contains("fictional-"));
        check("POSIX custom command fixes the same Codex home and isolated profile", manager.GetCustomTerminalCommand(executable, home, false).StartsWith("env -u OPENAI_API_KEY") && manager.GetCustomTerminalCommand(executable, home, false).EndsWith("--no-daemon --profile codex-switch"));
        check("Copied commands isolate credentials in the child without changing the parent environment", !command.Contains("$env:") && command.Contains("EnvironmentVariables.Remove($name)") && command.Contains("OPENAI_IDENTITY_TOKEN_FILE"));
        await File.WriteAllTextAsync(profile, content + "# owner edit\n");
        var refused = false;
        try { manager.GetCustomTerminalCommand(executable, home, true); } catch (InvalidOperationException) { refused = true; }
        check("Launching refuses an owner-diverged profile", refused);
        refused = false;
        try { await manager.DisconnectAsync(executable, home, CancellationToken.None); } catch (InvalidOperationException) { refused = true; }
        check("Disconnect preserves a diverged profile and gateway state", refused && File.ReadAllText(profile).Contains("owner edit") && manager.Running);
        await File.WriteAllTextAsync(profile, content);
        await manager.DisconnectAsync(executable, home, CancellationToken.None);
        check("Disconnect removes only the owned profile and stops the custom listener", !File.Exists(profile) && !manager.Running && !manager.Settings.Connected);
        await manager.DisconnectAsync(executable, home, CancellationToken.None);
        check("Repeated disconnect preserves provider credentials and identities", manager.Settings.Providers.Single().Id == provider.Id && manager.Settings.Providers.Single().ApiKey == "fictional-custom-key");
        refused = false;
        try { manager.GetCustomTerminalCommand(executable, home, true); } catch (InvalidOperationException) { refused = true; }
        check("Offline custom terminal launch reports that preparation is required", refused);
    }
}
