using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal enum ModelCatalogRefreshState { None, Pending, Confirmed, Deferred, Failed }
internal sealed record ModelCatalogRefreshStatus(ModelCatalogRefreshState State, string? Error = null)
{
    internal string? Message => State switch
    {
        ModelCatalogRefreshState.Pending => "router.refresh_pending",
        ModelCatalogRefreshState.Confirmed => "router.refresh_confirmed",
        ModelCatalogRefreshState.Deferred => "router.refresh_deferred",
        ModelCatalogRefreshState.Failed => "router.refresh_failed",
        _ => null
    };
    internal string? DisplayMessage => Message is not string key ? null :
        State == ModelCatalogRefreshState.Failed ? Loc.F(key, Loc.Error(Error ?? "")) : Loc.T(key);
}

internal static class LiveModelCatalog
{
    private static readonly string[] SelectionFields = { "display_name", "description", "visibility", "input_modalities", "default_reasoning_level", "supported_reasoning_levels", "supports_reasoning_summary_parameter" };
    internal static bool Enabled(ModelProviderSettings settings) => settings.Connected && settings.ConnectionWriteConfirmed && settings.DesktopProfile &&
        settings.DesktopAppliedConfig?.TryGetValue("model_catalog_json", out var catalog) == true && catalog == null;

    internal static void Invalidate(string home)
    {
        var path = Path.Combine(home, "models_cache.json");
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Codex model cache is a linked file and was not changed.");
        var bytes = SecureFiles.ReadPrivateBytes(path);
        if (bytes.Length > ProviderCatalog.MaximumCatalogBytes + 65536)
            throw new InvalidDataException("The Codex model cache exceeds its supported size.");
        if (JsonNode.Parse(bytes) is not JsonObject cache || cache["models"] is not JsonArray ||
            cache["client_version"] is not System.Text.Json.Nodes.JsonValue version || !version.TryGetValue<string>(out _) ||
            cache["fetched_at"] is not System.Text.Json.Nodes.JsonValue fetched || !fetched.TryGetValue<DateTimeOffset>(out _))
            throw new InvalidDataException("The Codex model cache format changed. Its contents were preserved.");
        File.Delete(path);
    }

    internal static async Task<ModelCatalogRefreshState> ConfirmAsync(string home, JsonArray expected, Func<CancellationToken, Task<bool>> refresh, CancellationToken cancellation)
    {
        var path = Path.Combine(home, "models_cache.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        JsonException? incomplete = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            JsonObject? cache = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > ProviderCatalog.MaximumCatalogBytes + 65536) throw new InvalidDataException("The Codex model cache exceeds its supported size.");
                cache = JsonNode.Parse(stream) as JsonObject ?? throw new InvalidDataException("The Codex model cache format changed. Its contents were preserved.");
                incomplete = null;
            }
            catch (FileNotFoundException) { incomplete = null; }
            catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33) { }
            catch (JsonException error) { incomplete = error; }
            if (cache?["models"] is JsonArray models)
            {
                var entries = models.OfType<JsonObject>().Where(model => ModelRouting.Owned(model["slug"]?.GetValue<string>())).ToArray();
                if (entries.Count(model => model["slug"]?.GetValue<string>() == ModelRouting.Model) != 1)
                    throw new InvalidOperationException("The selected model was saved, but Codex is not using the live Switch catalog. Apply the Codex Switch profile first.");
                if (entries.Length == expected.Count && expected.OfType<JsonObject>().All(wanted =>
                    entries.Count(entry => JsonNode.DeepEquals(entry["slug"], wanted["slug"]) &&
                        SelectionFields.All(key => JsonNode.DeepEquals(entry[key], wanted[key]))) == 1)) return ModelCatalogRefreshState.Confirmed;
                if (!await refresh(cancellation).ConfigureAwait(false)) return ModelCatalogRefreshState.Deferred;
            }
            else if (cache != null) throw new InvalidDataException("The Codex model cache format changed. Its contents were preserved.");
            await Task.Delay(40, cancellation).ConfigureAwait(false);
        }
        if (incomplete != null) throw new InvalidDataException("Codex did not finish writing a valid model cache. Its contents were preserved.", incomplete);
        return ModelCatalogRefreshState.Deferred;
    }
}

internal sealed partial class ModelProviderManager
{
    private CancellationTokenSource? _catalogRefreshCancellation;
    private Task _catalogRefresh = Task.CompletedTask;
    private ModelCatalogRefreshStatus _catalogRefreshStatus = new(ModelCatalogRefreshState.None);
    internal Func<CancellationToken, Task<bool>>? ModelCatalogChanged { get; set; }
    internal Action? CatalogRefreshUpdated { get; set; }
    internal ModelCatalogRefreshStatus CatalogRefreshStatus => Volatile.Read(ref _catalogRefreshStatus);
    internal Task CatalogRefreshCompletion => Volatile.Read(ref _catalogRefresh);

    private void SetCatalogRefreshStatus(ModelCatalogRefreshState state, string? error = null)
    {
        Volatile.Write(ref _catalogRefreshStatus, new(state, error));
        CatalogRefreshUpdated?.Invoke();
    }

    private async Task CancelLiveCatalogRefreshAsync()
    {
        var previous = _catalogRefreshCancellation;
        if (previous != null)
        {
            previous.Cancel();
            try { await _catalogRefresh.ConfigureAwait(false); }
            catch (OperationCanceledException) when (previous.IsCancellationRequested) { }
            finally { previous.Dispose(); _catalogRefreshCancellation = null; }
        }
        _catalogRefresh = Task.CompletedTask;
        SetCatalogRefreshStatus(ModelCatalogRefreshState.None);
    }

    private async Task NotifyLiveCatalogAsync(CancellationToken cancellation)
    {
        await CancelLiveCatalogRefreshAsync().ConfigureAwait(false);
        if (Settings.DesktopBackendEnabled) { await NotifyDesktopBackendAsync(cancellation).ConfigureAwait(false); return; }
        if (!LiveModelCatalog.Enabled(Settings)) { SetCatalogRefreshStatus(ModelCatalogRefreshState.None); return; }
        cancellation.ThrowIfCancellationRequested();
        var home = _home ?? throw new InvalidOperationException("The connected Codex home is unavailable.");
        var expected = new JsonArray(JsonNode.Parse(SecureFiles.ReadPrivateBytes(DesktopCatalogPath))!["models"]!.AsArray().OfType<JsonObject>()
            .Where(model => ModelRouting.Owned(model["slug"]?.GetValue<string>())).Select(model => model.DeepClone()).ToArray());
        LiveModelCatalog.Invalidate(home);
        var notify = ModelCatalogChanged;
        if (notify == null || !await notify(cancellation).ConfigureAwait(false))
        {
            SetCatalogRefreshStatus(ModelCatalogRefreshState.Deferred);
            return;
        }
        SetCatalogRefreshStatus(ModelCatalogRefreshState.Pending);
        _catalogRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _catalogRefresh = ConfirmLiveCatalogAsync(home, expected, notify, _catalogRefreshCancellation.Token);
        await Task.WhenAny(_catalogRefresh, Task.Delay(500, cancellation)).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
    }

    private async Task ConfirmLiveCatalogAsync(string home, JsonArray expected, Func<CancellationToken, Task<bool>> notify, CancellationToken cancellation)
    {
        try
        {
            var result = await LiveModelCatalog.ConfirmAsync(home, expected, async token =>
            {
                await _changes.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!LiveModelCatalog.Enabled(Settings)) return false;
                    LiveModelCatalog.Invalidate(home);
                    return await notify(token).ConfigureAwait(false);
                }
                finally { _changes.Release(); }
            }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            SetCatalogRefreshStatus(result);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            SetCatalogRefreshStatus(ModelCatalogRefreshState.Failed, error.Message);
        }
    }
}
