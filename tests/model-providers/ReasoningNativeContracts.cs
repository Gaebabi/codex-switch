using CodexAccountDesk;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ReasoningNativeContracts
{
    internal static async Task Run(string executable, string root, Action<string, bool> check)
    {
        var home = Path.Combine(root, "reasoning-native-home"); Directory.CreateDirectory(home);
        await using var workspace = new WorkspaceFixture();
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\"fictional@example.invalid\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"fictional-account\",\"chatgpt_plan_type\":\"plus\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "fictional-reasoning-token", refresh_token = "fictional-refresh", id_token = "eyJhbGciOiJub25lIn0." + payload + ".fixture", account_id = "fictional-account" }, last_refresh = DateTimeOffset.UtcNow });
        await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), auth);
        var native = ProviderCatalog.Generic("native-fixture"); var original = native.ToJsonString();
        var nativeCatalog = Path.Combine(home, "native-catalog.json");
        await File.WriteAllTextAsync(nativeCatalog, new JsonObject { ["models"] = new JsonArray(native) }.ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "model_provider=\"openai\"\nmodel=\"native-fixture\"\nmodel_catalog_json=" + JsonSerializer.Serialize(nativeCatalog) + "\ncli_auth_credentials_store=\"file\"\nchatgpt_base_url=" + JsonSerializer.Serialize(workspace.BaseUrl) + "\n");
        GatewayIdentity? Authenticate(string? token, string? account) => GatewayIdentity.Authenticate(token, account, () => auth);
        await using var manager = new ModelProviderManager(Path.Combine(root, "reasoning-native-store"), Authenticate, new HttpClient(new ReasoningControlContracts.ReasoningUpstream()));
        var qwen = await manager.SaveAsync(null, "Native Qwen", "https://qwen.example.invalid/v1", "fictional-qwen-key", CancellationToken.None);
        var gemma = await manager.SaveAsync(null, "Native Gemma", "https://gemma.example.invalid/v1", "fictional-gemma-key", CancellationToken.None);
        manager.SelectRoute(qwen.Models.Single().Alias(qwen.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var cancellation = timeout.Token;
        await manager.ApplyDesktopProfileAsync(executable, home, CodexModelProfile.Switch, cancellation);
        async Task<JsonElement> List(CodexRpc rpc) => await rpc.RequestAsync("model/list", new { includeHidden = true }, cancellation);
        string[] Efforts(JsonElement catalog) => catalog.GetProperty("data").EnumerateArray().Single(model => model.GetProperty("model").GetString() == ModelRouting.Model)
            .GetProperty("supportedReasoningEfforts").EnumerateArray().Select(level => level.GetProperty("reasoningEffort").GetString()!).ToArray();
        await using var current = new CodexRpc(executable, home, preserveProvider: true); await current.InitializeAsync(cancellation);
        var qwenCatalog = await List(current);
        check("The actual native picker exposes Qwen Low, Medium and XHigh in the existing Switch entry", Efforts(qwenCatalog).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        check("Native OpenAI models remain visible alongside exactly one Switch entry", qwenCatalog.GetProperty("data").GetArrayLength() == 2 && qwenCatalog.GetProperty("data")[0].GetProperty("model").GetString() == "native-fixture");
        manager.SelectRoute(gemma.Models.Single().Alias(gemma.Id));
        check("The current native client retains its loaded static catalog until a client reload", Efforts(await List(current)).SequenceEqual(new[] { "low", "medium", "xhigh" }));
        await using var reloaded = new CodexRpc(executable, home, preserveProvider: true); await reloaded.InitializeAsync(cancellation);
        check("After native reload the same Switch entry exposes Gemma None and Medium", Efforts(await List(reloaded)).SequenceEqual(new[] { "none", "medium" }));
        var files = Directory.GetFiles(Path.Combine(root, "reasoning-native-store"), "*desktop-catalog.json");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(files.Single(), cancellation))!["models"]!.AsArray();
        check("Refreshing selected-model reasoning does not change any native model document", saved.OfType<JsonObject>().Single(model => model["slug"]!.GetValue<string>() == "native-fixture").ToJsonString() == original);
        check("Native reasoning setup and refresh preserve the original sign-in file", await File.ReadAllTextAsync(Path.Combine(home, "auth.json"), cancellation) == auth);
    }
}
