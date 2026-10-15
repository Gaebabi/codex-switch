using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexAccountDesk;

internal enum LinuxKeyFailure { Unavailable, Locked, Missing, Ambiguous, Invalid, Busy, TimedOut, Cancelled }
internal sealed class LinuxVaultException : CryptographicException
{
    public LinuxKeyFailure Failure { get; }
    public LinuxVaultException(LinuxKeyFailure failure) : base(failure switch
    {
        LinuxKeyFailure.Locked => "Linux keyring is locked. Unlock it and try again.",
        LinuxKeyFailure.Missing => "The Linux vault key is missing. Restore the original keyring; the vault has not been replaced.",
        LinuxKeyFailure.Ambiguous => "More than one Linux vault key matches. Resolve the duplicate keyring items before continuing.",
        LinuxKeyFailure.Invalid => "The Linux vault key is invalid. The vault has not been replaced.",
        LinuxKeyFailure.Busy => "The Linux vault key is in use by another process. Try again after it finishes.",
        LinuxKeyFailure.TimedOut => "The Linux keyring operation timed out. Check the keyring and try again.",
        LinuxKeyFailure.Cancelled => "The Linux keyring operation was cancelled. The vault has not been replaced.",
        _ => "Linux Secret Service is unavailable. Install and unlock a compatible keyring before using account storage."
    }) { Failure = failure; }
}

internal static class LinuxVaultProtection
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("CADL1");
    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Keys = new(StringComparer.Ordinal);
    private static byte[] Key(string directory, bool create)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux vault protection requires Linux.");
        directory = Path.GetFullPath(directory);
        lock (Gate)
        {
            if (Keys.TryGetValue(directory, out var cached)) return (byte[])cached.Clone();
            using var held = LinuxPrivateFiles.Lock(directory);
            var allowCreate = create && !LinuxPrivateFiles.Exists(directory, "accounts.vault");
            var key = LinuxSecretService.ReadOrCreateKey(SecureFiles.Hash(directory), allowCreate);
            Keys.Add(directory, key); return (byte[])key.Clone();
        }
    }
    public static byte[] Protect(byte[] plaintext, string directory)
    {
        var key = Key(directory, true);
        try
        {
            var result = new byte[checked(Header.Length + 28 + plaintext.Length)]; Header.CopyTo(result, 0);
            var nonce = result.AsSpan(Header.Length, 12); RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, result.AsSpan(Header.Length + 28), result.AsSpan(Header.Length + 12, 16), Header); return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static byte[] Unprotect(byte[] encrypted, string directory)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux vault protection requires Linux.");
        if (encrypted.Length < Header.Length + 28 || !encrypted.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new CryptographicException("This is not a Linux vault. Register accounts separately; foreign vaults are not migrated automatically.");
        var key = Key(directory, false);
        try
        {
            var plaintext = new byte[encrypted.Length - Header.Length - 28];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(encrypted.AsSpan(Header.Length, 12), encrypted.AsSpan(Header.Length + 28), encrypted.AsSpan(Header.Length + 12, 16), plaintext, Header); return plaintext;
            }
            catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static void ReleaseKeys() { lock (Gate) { foreach (var key in Keys.Values) CryptographicOperations.ZeroMemory(key); Keys.Clear(); } }
}
