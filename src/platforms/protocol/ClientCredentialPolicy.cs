using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record ClientCredentialPolicy(string Store, string? ForcedLoginMethod, string? ForcedWorkspaceId, bool ChatGptAllowed)
{
    public bool FileCredentialsAllowed => Store == "file" && ForcedLoginMethod != "api" && ChatGptAllowed;
    public bool AllowsAccount(string workspace) => FileCredentialsAllowed && (ForcedWorkspaceId == null || ForcedWorkspaceId == workspace);
    private static InvalidDataException Invalid() => new("The client's effective credential policy is unsupported or inconsistent.");
    private static string? Text(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value)) throw Invalid();
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var result = value.GetString()!;
        if (result.Length == 0 || result.Length > 512 || result.Any(char.IsControl)) throw Invalid();
        return result;
    }
    public static ClientCredentialPolicy Parse(JsonElement configuration, JsonElement requirementsResult)
    {
        if (configuration.ValueKind != JsonValueKind.Object || requirementsResult.ValueKind != JsonValueKind.Object ||
            !configuration.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
            !requirementsResult.TryGetProperty("requirements", out var requirements)) throw Invalid();
        var store = Text(config, "cli_auth_credentials_store");
        if (store is not ("file" or "keyring" or "auto" or "ephemeral")) throw Invalid();
        var forced = Text(config, "forced_login_method"); if (forced is not (null or "api" or "chatgpt")) throw Invalid();
        var workspace = Text(config, "forced_chatgpt_workspace_id"); var chatGptAllowed = true;
        if (requirements.ValueKind == JsonValueKind.Object)
        {
            var requiredStore = Text(requirements, "cliAuthCredentialsStore");
            if (requiredStore != null && (requiredStore is not ("file" or "keyring" or "auto" or "ephemeral") || requiredStore != store)) throw Invalid();
            if (!requirements.TryGetProperty("allowedLoginMethods", out var methods)) throw Invalid();
            if (methods.ValueKind == JsonValueKind.Array)
            {
                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var method in methods.EnumerateArray())
                    if (method.ValueKind != JsonValueKind.String || method.GetString() is not ("api" or "chatgpt") || !allowed.Add(method.GetString()!)) throw Invalid();
                if (allowed.Count == 0) throw Invalid();
                chatGptAllowed = allowed.Contains("chatgpt");
            }
            else if (methods.ValueKind != JsonValueKind.Null) throw Invalid();
        }
        else if (requirements.ValueKind != JsonValueKind.Null) throw Invalid();
        return new(store, forced, workspace, chatGptAllowed);
    }
}
