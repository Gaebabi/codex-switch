using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodexAccountDesk;

// Only the paired backend knows the key. Native Codex sees an authorization for one loopback port.
internal static class DiagnosticGatewayEndpoint
{
    internal const string RelayUrlVariable = "CODEX_SWITCH_DIAGNOSTIC_URL";
    internal const string KeyVariable = "CODEX_SWITCH_DIAGNOSTIC_KEY";

    private static byte[] Sign(byte[] key, string port) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(port));

    internal static string ForPort(string relayUrl, byte[] key, int port)
    {
        var value = port.ToString(CultureInfo.InvariantCulture);
        return relayUrl + "/gateway/" + value + "/" + Convert.ToHexString(Sign(key, value));
    }

    internal static bool TryResolve(byte[] key, string? path, out Uri? destination)
    {
        destination = null;
        var parts = path?.Split('/', 4);
        if (parts is not { Length: 4 } || parts[0] != "" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535 || parts[2].Length != 64 ||
            parts[3] is not ("responses" or "responses/compact" or "models")) return false;
        Span<byte> supplied = stackalloc byte[32];
        if (Convert.FromHexString(parts[2], supplied, out _, out var written) != System.Buffers.OperationStatus.Done ||
            written != supplied.Length || !CryptographicOperations.FixedTimeEquals(supplied, Sign(key, parts[1]))) return false;
        destination = new UriBuilder(Uri.UriSchemeHttp, "127.0.0.1", port, "/backend-api/codex/" + parts[3]).Uri;
        return true;
    }
}
