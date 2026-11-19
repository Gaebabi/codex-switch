using System.Diagnostics;
using System.Text.Json;

namespace CodexSwitch.Diagnostics;

internal static class NativeRelayProcess
{
    internal static ProcessStartInfo StartInfo(string native, string[] args, string? baseUrl, string? home = null)
    {
        var start = new ProcessStartInfo(native) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("CODEX_CLI_PATH");
        start.Environment.Remove(CodexAccountDesk.DiagnosticGatewayEndpoint.KeyVariable);
        start.Environment.Remove(CodexAccountDesk.DiagnosticGatewayEndpoint.RelayUrlVariable);
        if (home != null) start.Environment["CODEX_HOME"] = home;
        if (baseUrl != null)
        {
            start.ArgumentList.Add("-c"); start.ArgumentList.Add("openai_base_url=" + JsonSerializer.Serialize(baseUrl));
        }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
}
