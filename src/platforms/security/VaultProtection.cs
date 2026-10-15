using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CodexAccountDesk;

internal static class VaultProtection
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("CADM1");
    private static readonly ConcurrentDictionary<string, byte[]> Keys = new(StringComparer.Ordinal);
    private static byte[] Key(string directory, bool create) => (byte[])Keys.GetOrAdd(directory, path => MacKeychain.ReadOrCreateKey(SecureFiles.Hash(path), create)).Clone();
    public static byte[] Protect(byte[] plaintext, string directory)
    {
        if (OperatingSystem.IsWindows()) return WindowsProtection.Protect(plaintext);
        if (OperatingSystem.IsLinux()) return LinuxVaultProtection.Protect(plaintext, directory);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Account storage supports Windows and macOS.");
        var key = Key(directory, true);
        try
        {
            var result = new byte[Header.Length + 12 + 16 + plaintext.Length]; Header.CopyTo(result, 0);
            var nonce = result.AsSpan(Header.Length, 12); RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, result.AsSpan(Header.Length + 28), result.AsSpan(Header.Length + 12, 16), Header);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static byte[] Unprotect(byte[] encrypted, string directory)
    {
        if (OperatingSystem.IsWindows()) return WindowsProtection.Unprotect(encrypted);
        if (OperatingSystem.IsLinux()) return LinuxVaultProtection.Unprotect(encrypted, directory);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Account storage supports Windows and macOS.");
        if (encrypted.Length < Header.Length + 28 || !encrypted.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new CryptographicException("This vault was not encrypted on this Mac. Register accounts on macOS separately.");
        var key = Key(directory, false);
        try
        {
            var plaintext = new byte[encrypted.Length - Header.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(encrypted.AsSpan(Header.Length, 12), encrypted.AsSpan(Header.Length + 28), encrypted.AsSpan(Header.Length + 12, 16), plaintext, Header);
            return plaintext;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static void ReleaseKeys() { foreach (var key in Keys.Values) CryptographicOperations.ZeroMemory(key); Keys.Clear(); LinuxVaultProtection.ReleaseKeys(); }
}
