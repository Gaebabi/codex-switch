using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed class ProviderEditorDraft
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? ApiKey { get; set; }
    public string ModelIds { get; set; } = "";
    public int Step { get; set; }
    internal ProviderEditorDraft Copy() => new() { Name = Name, BaseUrl = BaseUrl, ApiKey = ApiKey, ModelIds = ModelIds, Step = Step };
    internal static ProviderEditorDraft From(ModelProvider provider, int step = 0) => new()
    { Name = provider.Name, BaseUrl = provider.BaseUrl, ModelIds = string.Join("\n", provider.IncludedModelIds ?? new()), Step = step };
    internal void Validate()
    {
        if (Name == null || Name.Length > 80 || BaseUrl == null || BaseUrl.Length > 32768 ||
            ApiKey?.Length > 16384 || ModelIds == null || ModelIds.Length > 65536 || Step is < 0 or > 1)
            throw new InvalidDataException("The provider editor draft is invalid or too large.");
    }
}

internal sealed partial class ModelProviderManager
{
    internal const string NewEditorProvider = "new";
    internal ProviderEditorDraft ReadEditorDraft(string? id)
    {
        var settings = Settings;
        if (settings.EditorDrafts.TryGetValue(id ?? NewEditorProvider, out var draft)) return draft;
        var provider = settings.Providers.FirstOrDefault(item => item.Id == id);
        return provider == null ? new() : ProviderEditorDraft.From(provider);
    }
    internal void RememberEditorDraft(string? id, ProviderEditorDraft draft)
    {
        if (id != null && !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid provider selection.");
        draft.Validate(); var copy = draft.Copy();
        _store.Update(settings =>
        {
            var key = id ?? NewEditorProvider;
            if (!settings.EditorDrafts.ContainsKey(key) && settings.EditorDrafts.Count >= 33)
                throw new InvalidOperationException("The provider editor draft limit was reached.");
            settings.EditorDrafts[key] = copy; settings.EditorSelection = key;
        });
    }
    internal void ForgetEditorDraft(string? id) => _store.Update(settings => settings.EditorDrafts.Remove(id ?? NewEditorProvider));
    private static void RememberSavedEditor(ModelProviderSettings settings, ModelProvider provider, string? previousId)
    {
        var draft = ProviderEditorDraft.From(provider, 1); draft.Validate();
        settings.EditorDrafts.Remove(previousId ?? NewEditorProvider);
        settings.EditorDrafts[provider.Id] = draft;
        settings.EditorSelection = provider.Id;
    }
}

internal sealed class ProviderEditorSession
{
    private readonly ModelProviderManager _manager;
    private string _savedKey = "";
    public string? SelectedId { get; private set; }
    public ProviderEditorDraft Draft { get; private set; } = new();
    public string ApiKey => Draft.ApiKey ?? _savedKey;
    public bool IsSaved => SelectedId != null && _manager.Settings.Providers.Any(item => item.Id == SelectedId);
    internal ProviderEditorSession(ModelProviderManager manager)
    {
        _manager = manager;
        var settings = manager.Settings;
        var selected = settings.EditorSelection;
        Load(selected == ModelProviderManager.NewEditorProvider ? null : selected ?? settings.Providers.FirstOrDefault()?.Id);
    }
    private void Load(string? id)
    {
        SelectedId = id;
        _savedKey = _manager.Settings.Providers.FirstOrDefault(item => item.Id == id)?.ApiKey ?? "";
        Draft = _manager.ReadEditorDraft(id);
    }
    public void Persist() => _manager.RememberEditorDraft(SelectedId, Draft);
    public void Select(string? id) { Persist(); Load(id); Persist(); }
    public void SetApiKey(string key) => Draft.ApiKey = key;
    public void Edit() { Draft.Step = 0; Persist(); }
    public void LoadSaved() { _manager.ForgetEditorDraft(SelectedId); Load(SelectedId); Draft.Step = 0; Persist(); }
    public void UseForNewProvider()
    {
        Persist(); var draft = new ProviderEditorDraft { BaseUrl = Draft.BaseUrl, ApiKey = ApiKey, ModelIds = Draft.ModelIds };
        _manager.RememberEditorDraft(null, draft); Load(null);
    }
    public async Task SaveAsync(CancellationToken cancellation)
    {
        Persist(); var draft = Draft.Copy();
        if (draft.ApiKey != null && string.IsNullOrWhiteSpace(draft.ApiKey)) throw new ArgumentException("Enter an API key.");
        var provider = await _manager.SaveAsync(SelectedId, draft.Name, draft.BaseUrl, draft.ApiKey ?? "", cancellation,
            draft.ModelIds.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ConfigureAwait(false);
        Load(provider.Id);
    }
}
