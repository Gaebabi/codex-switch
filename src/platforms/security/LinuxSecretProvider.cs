using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace CodexAccountDesk;

internal static class LinuxSecretProvider
{
    private sealed record Provider(string Service, string Architecture, string Version, string Contract, string Executable, string Sha256);
    private static readonly Provider[] Providers = ReadManifest();
    private static Provider[] ReadManifest()
    {
        using var input = typeof(LinuxSecretProvider).Assembly.GetManifestResourceStream("CodexAccountDesk.LinuxSecretProviders.json")!;
        using var manifest = JsonDocument.Parse(input);
        return manifest.RootElement.GetProperty("providers").EnumerateArray().Select(value =>
            new Provider(value.GetProperty("service").GetString()!, value.GetProperty("architecture").GetString()!,
                value.GetProperty("version").GetString()!, value.GetProperty("contract").GetString()!,
                value.GetProperty("executable").GetString()!, value.GetProperty("sha256").GetString()!)).ToArray();
    }
    internal static bool UsesLegacyEncoding(string service, string version, uint processId, CancellationToken cancellation)
    {
        var provider = Providers.SingleOrDefault(value => value.Service == service && value.Version == version &&
            value.Architecture == RuntimeInformation.ProcessArchitecture.ToString());
        if (provider == null) throw new LinuxVaultException(LinuxKeyFailure.ProviderIncompatible);
        try
        {
            using var executable = LinuxExecutable.Verify(provider.Executable, provider.Sha256, true, cancellation);
            if (!executable.IsProcessImage(processId)) throw new LinuxVaultException(LinuxKeyFailure.ProviderIncompatible);
        }
        catch (IOException) { throw new LinuxVaultException(LinuxKeyFailure.ProviderIncompatible); }
        return provider.Contract switch
        {
            "legacy-minimal" => true,
            "normalized-padded" => false,
            _ => throw new LinuxVaultException(LinuxKeyFailure.ProviderIncompatible)
        };
    }
}
