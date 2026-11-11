using System.Net.Http;
using System.Text;
using CodexAccountDesk;

internal static class ProviderEditorContracts
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var directory = Path.Combine(root, "editor");
        await using var manager = new ModelProviderManager(directory, (_, _) => null, new HttpClient(new Upstreams()));
        var first = await manager.SaveAsync(null, "Editor first", "https://a.example.invalid/v1", "fictional-editor-first", CancellationToken.None);
        var second = await manager.SaveAsync(null, "Editor second", "https://b.example.invalid/v1", "fictional-editor-second", CancellationToken.None);
        var editor = new ProviderEditorSession(manager); editor.Select(first.Id);
        check("Saved provider fields load with the original masked-key value", editor.Draft.Name == first.Name && editor.Draft.BaseUrl == first.BaseUrl && editor.ApiKey == first.ApiKey && editor.Draft.ApiKey == null);
        editor.Edit();
        editor.Draft.Name = "Unsaved editor name"; editor.Draft.ModelIds = "shared-model"; editor.SetApiKey("fictional-editor-draft");
        editor.Select(second.Id); editor.Select(first.Id);
        check("Switching providers retains each unfinished field and replacement key", editor.Draft.Name == "Unsaved editor name" && editor.Draft.ModelIds == "shared-model" && editor.ApiKey == "fictional-editor-draft");
        await using var reopenedManager = new ModelProviderManager(directory, (_, _) => null, new HttpClient(new Upstreams()));
        var reopened = new ProviderEditorSession(reopenedManager);
        check("Selected provider and unfinished fields survive encrypted reload", reopened.SelectedId == first.Id && reopened.Draft.Name == editor.Draft.Name && reopened.ApiKey == editor.ApiKey);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "model-providers.vault")));
        check("Unfinished keys are stored only inside the encrypted provider vault", !raw.Contains("fictional-editor-draft") && !raw.Contains("Unsaved editor name"));
        editor.Draft.BaseUrl = "not a URL";
        var failed = false;
        try { await editor.SaveAsync(CancellationToken.None); } catch (ArgumentException) { failed = true; }
        check("Failed discovery preserves input and stays on the details step", failed && editor.Draft.BaseUrl == "not a URL" && editor.ApiKey == "fictional-editor-draft" && editor.Draft.Step == 0);
        editor.LoadSaved();
        check("Explicit load restores the saved values without exposing a replacement key", editor.Draft.Name == first.Name && editor.Draft.BaseUrl == first.BaseUrl && editor.ApiKey == first.ApiKey && editor.Draft.ApiKey == null);
        editor.SetApiKey(""); failed = false;
        try { await editor.SaveAsync(CancellationToken.None); } catch (ArgumentException) { failed = true; }
        check("Explicitly clearing a key is retained and cannot silently reuse the saved key", failed && editor.ApiKey == "" && manager.Settings.Providers.Single(item => item.Id == first.Id).ApiKey == first.ApiKey);
        editor.LoadSaved();
        editor.Draft.BaseUrl = "https://b.example.invalid/v2"; failed = false;
        try { await editor.SaveAsync(CancellationToken.None); } catch (ArgumentException) { failed = true; }
        check("Displaying a saved key does not authorize sending it to a changed destination", failed && manager.Settings.Providers.Single(item => item.Id == first.Id).BaseUrl == first.BaseUrl);
        editor.LoadSaved(); editor.UseForNewProvider();
        check("Explicit reuse copies endpoint, key and model filter into a new draft", editor.SelectedId == null && editor.Draft.BaseUrl == first.BaseUrl && editor.ApiKey == first.ApiKey && editor.Draft.ApiKey != null);
        editor.Draft.Name = "Editor copy"; await editor.SaveAsync(CancellationToken.None);
        check("Saving a reused provider retains fields and advances to connection", editor.SelectedId != null && editor.SelectedId != first.Id && editor.Draft.Name == "Editor copy" && editor.ApiKey == first.ApiKey && editor.Draft.Step == 1);
        editor.Edit(); editor.Persist();
        await using var finalManager = new ModelProviderManager(directory, (_, _) => null, new HttpClient(new Upstreams()));
        var final = new ProviderEditorSession(finalManager);
        check("Closing and reopening retains the chosen provider and current step", final.SelectedId == editor.SelectedId && final.Draft.Step == 0 && final.ApiKey == editor.ApiKey);
        var before = File.ReadAllBytes(Path.Combine(directory, "model-providers.vault"));
        failed = false;
        try { manager.RememberEditorDraft(null, new ProviderEditorDraft { Name = new string('x', 81) }); } catch (InvalidDataException) { failed = true; }
        check("Invalid editor state cannot replace the persisted draft", failed && before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "model-providers.vault"))));
    }
}
