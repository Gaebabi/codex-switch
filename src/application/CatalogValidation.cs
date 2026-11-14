using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal static class CatalogValidation
{
    internal static RuntimeHome CreateHome() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexSwitchCatalogs"), null);

    internal static async Task<JsonElement> ReadModelListAsync(string executable, byte[]? catalog, string? model, CancellationToken cancellation, bool desktopBackend = false)
    {
        await using var home = CreateHome();
        var config = model == null ? "" : "model=" + JsonSerializer.Serialize(model) + "\n";
        if (catalog != null)
        {
            if (catalog.Length > ProviderCatalog.MaximumCatalogBytes) throw new InvalidDataException("The model catalog exceeds the client limit.");
            var path = Path.Combine(home.Path, "catalog.json");
            SecureFiles.AtomicWrite(path, catalog);
            config += "model_catalog_json=" + JsonSerializer.Serialize(path) + "\n";
        }
        SecureFiles.AtomicWriteText(Path.Combine(home.Path, "config.toml"), config);
        await using var rpc = new CodexRpc(executable, home.Path, preserveProvider: true,
            configOverrides: new Dictionary<string, string> { ["features.plugins"] = "false" });
        await rpc.InitializeAsync(cancellation).ConfigureAwait(false);
        if (desktopBackend) DesktopBackendCatalog.RequireCompatibleClient(rpc.Initialization);
        await rpc.RequestAsync("config/read", new { includeLayers = true }, cancellation).ConfigureAwait(false);
        var list = await rpc.RequestAsync("model/list", new { includeHidden = true }, cancellation).ConfigureAwait(false);
        if (catalog != null)
        {
            using var expected = JsonDocument.Parse(catalog);
            var slugs = expected.RootElement.GetProperty("models").EnumerateArray().Select(item => item.GetProperty("slug").GetString()).OrderBy(value => value, StringComparer.Ordinal);
            var actual = list.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("model").GetString()).OrderBy(value => value, StringComparer.Ordinal);
            if (!slugs.SequenceEqual(actual)) throw new InvalidDataException("Codex did not load the candidate model catalog. The existing configuration was preserved.");
        }
        return list;
    }
}
