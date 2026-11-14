using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDesk;

internal sealed record ModelContextOptions(long? ContextWindow, long? AutoCompactTokenLimit)
{
    internal static ModelContextOptions? Parse(string window, string compact)
    {
        static long? Number(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException(Loc.T("context.invalid"));
            return value;
        }
        var result = new ModelContextOptions(Number(window), Number(compact));
        Validate(result);
        return result.ContextWindow == null && result.AutoCompactTokenLimit == null ? null : result;
    }
    internal static void Validate(ModelContextOptions? value)
    {
        if (value == null) return;
        if (value.ContextWindow is <= 0 or > long.MaxValue / 100 || value.AutoCompactTokenLimit is <= 0 ||
            value.ContextWindow is long window && value.AutoCompactTokenLimit > window)
            throw new ArgumentException(Loc.T("context.invalid"));
    }
    internal JsonObject Configuration()
    {
        Validate(this);
        var result = new JsonObject();
        if (ContextWindow is long window) result["model_context_window"] = window;
        if (AutoCompactTokenLimit is long compact) result["model_auto_compact_token_limit"] = compact;
        return result;
    }
    internal static void ApplyMetadata(JsonObject metadata, ModelContextOptions? options)
    {
        Validate(options);
        if (options?.ContextWindow is long window) metadata["context_window"] = window;
        if (options?.AutoCompactTokenLimit is long compact) metadata["auto_compact_token_limit"] = compact;
    }
}

internal sealed partial class ModelProviderManager
{
    internal const string NativeContextTarget = "openai";
    internal bool ContextLiveAvailable
    {
        get
        {
            var path = Path.Combine(Path.GetDirectoryName(_catalogPath)!, DesktopBackendCatalog.ObservedFileName);
            return Settings.DesktopBackendEnabled && File.Exists(path) &&
                JsonNode.Parse(SecureFiles.ReadPrivateBytes(path))?["contextSettingsVersion"]?.GetValue<int>() == 1;
        }
    }
    internal IReadOnlyList<ModelRouteChoice> ContextChoices =>
        new[] { new ModelRouteChoice(NativeContextTarget, Loc.T("context.native")) }.Concat(RoutingChoices).ToArray();
    internal ModelContextOptions? ContextOptions(string target)
    {
        var settings = Settings;
        return target == NativeContextTarget ? settings.NativeContext : settings.Providers
            .SelectMany(provider => provider.Models.Select(model => (provider, model)))
            .Single(pair => pair.model.Alias(pair.provider.Id) == target).model.ContextOverride;
    }
    internal async Task SaveContextAsync(string target, ModelContextOptions? options, CancellationToken cancellation)
    {
        ModelContextOptions.Validate(options);
        if (options is { ContextWindow: null, AutoCompactTokenLimit: null }) options = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _changes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            _store.Update(settings =>
            {
                if (target == NativeContextTarget) settings.NativeContext = options;
                else settings.Providers.SelectMany(provider => provider.Models.Select(model => (provider, model)))
                    .Single(pair => pair.model.Alias(pair.provider.Id) == target).model.ContextOverride = options;
            });
            if (Settings.RouterEnabled) await NotifyLiveCatalogAsync(lifetime.Token).ConfigureAwait(false);
            else await RefreshCatalogCoreAsync(lifetime.Token, true).ConfigureAwait(false);
        }
        finally { _changes.Release(); }
    }
}
