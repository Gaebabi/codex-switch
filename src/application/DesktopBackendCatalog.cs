using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class DesktopBackendCatalog
{
    internal const string FileName = "desktop-backend-catalog.json";
    internal const string ObservedFileName = "desktop-backend-observed.json";
    internal const string ReviewedClientVersion = "0.167.0-alpha.4.1";
    internal static void RequireCompatibleClient(JsonElement? initialization)
    {
        var agent = initialization is JsonElement hello ? JsonValue.String(hello, "userAgent") : null;
        var version = Regex.Match(agent ?? "", @"(?<=/)\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?(?=\s|$)").Value;
        if (version != ReviewedClientVersion)
            throw new InvalidDataException("Live desktop models require the reviewed native Codex CLI " + ReviewedClientVersion + ". The existing configuration and catalog were preserved.");
    }
    internal static string Revision(ModelProviderSettings settings) => SecureFiles.Hash(ModelRouting.Catalog(settings).ToJsonString());
    internal static async Task<string> PublishAsync(string directory, ModelProviderSettings settings, CancellationToken cancellation)
    {
        var executable = settings.DesktopBackendExecutable ?? throw new InvalidDataException("The native Codex executable is not configured.");
        var revision = Revision(settings);
        var catalog = ProviderCatalog.Serialize(new JsonObject { ["models"] = ModelRouting.Catalog(settings) });
        var list = await CatalogValidation.ReadModelListAsync(executable, catalog, ModelRouting.Model, cancellation, desktopBackend: true).ConfigureAwait(false);
        var catalogPath = Path.Combine(directory, "desktop-backend-models-" + revision + ".json");
        SecureFiles.AtomicWrite(catalogPath, catalog);
        var result = new JsonObject
        {
            ["revision"] = revision, ["catalogPath"] = catalogPath,
            ["models"] = JsonNode.Parse(list.GetProperty("data").GetRawText())
        };
        SecureFiles.AtomicWrite(Path.Combine(directory, FileName), JsonSerializer.SerializeToUtf8Bytes(result));
        return revision;
    }
    internal static async Task<ModelCatalogRefreshState> ConfirmAsync(string directory, string revision, CancellationToken cancellation)
    {
        var path = Path.Combine(directory, ObservedFileName);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(path) && JsonNode.Parse(SecureFiles.ReadPrivateBytes(path))?["revision"]?.GetValue<string>() == revision)
                return ModelCatalogRefreshState.Confirmed;
            await Task.Delay(250, cancellation).ConfigureAwait(false);
        }
        return ModelCatalogRefreshState.Deferred;
    }
}

internal sealed partial class ModelProviderManager
{
    internal async Task EnableDesktopBackendAsync(string executable, CancellationToken cancellation)
    {
        await _changes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var candidate = Settings;
            candidate.DesktopBackendEnabled = true;
            candidate.DesktopBackendExecutable = Path.GetFullPath(executable);
            candidate.RouterEnabled = true;
            await DesktopBackendCatalog.PublishAsync(Path.GetDirectoryName(_catalogPath)!, candidate, cancellation).ConfigureAwait(false);
            _store.Update(settings =>
            {
                settings.DesktopBackendEnabled = true;
                settings.DesktopBackendExecutable = candidate.DesktopBackendExecutable;
                settings.RouterEnabled = true;
            });
        }
        finally { _changes.Release(); }
    }
    private async Task NotifyDesktopBackendAsync(CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(_catalogPath)!;
        var revision = await DesktopBackendCatalog.PublishAsync(directory, Settings, cancellation).ConfigureAwait(false);
        if (ModelCatalogChanged == null || !await ModelCatalogChanged(cancellation).ConfigureAwait(false))
        { SetCatalogRefreshStatus(ModelCatalogRefreshState.Deferred); return; }
        SetCatalogRefreshStatus(ModelCatalogRefreshState.Pending);
        _catalogRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _catalogRefresh = ConfirmDesktopBackendAsync(directory, revision, _catalogRefreshCancellation.Token);
    }
    private async Task ConfirmDesktopBackendAsync(string directory, string revision, CancellationToken cancellation)
    {
        try { SetCatalogRefreshStatus(await DesktopBackendCatalog.ConfirmAsync(directory, revision, cancellation).ConfigureAwait(false)); }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        { SetCatalogRefreshStatus(ModelCatalogRefreshState.Failed, error.Message); }
    }
}
