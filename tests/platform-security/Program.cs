using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexAccountDesk;

var checks = new List<string>();
void Assert(bool value) { if (!value) throw new InvalidOperationException("Platform security contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void Rejected<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected security rejection was not observed.");
}
var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
var root = Path.Combine(parent, "codex-security-contract-" + Guid.NewGuid().ToString("N"));
var keyDirectory = Path.Combine(root, "mac-self-test-" + Guid.NewGuid().ToString("N"));
SecureFiles.CreatePrivateDirectory(root);
try
{
    Check("Native security has one UI/domain-independent assembly", () =>
    {
        var assembly = typeof(VaultProtection).Assembly;
        Assert(assembly.GetName().Name == "CodexSwitch.PlatformSecurity" && typeof(SecureFiles).Assembly == assembly && typeof(MacKeychain).Assembly == assembly);
        Assert(!assembly.GetReferencedAssemblies().Any(a => a.Name is "CodexSwitch" or "CodexSwitch.Core" or "PresentationFramework" or "Avalonia" or "Avalonia.Controls"));
    });
    var privateDirectory = Path.Combine(root, "private");
    Check("Private directory creation is repeatable", () =>
    { SecureFiles.CreatePrivateDirectory(privateDirectory); SecureFiles.CreatePrivateDirectory(privateDirectory); Assert(Directory.Exists(privateDirectory)); });
    var textPath = Path.Combine(privateDirectory, "fixture.txt");
    Check("Atomic creation preserves UTF-8 without BOM", () =>
    {
        SecureFiles.AtomicWriteText(textPath, "한국어 · 日本語 · français");
        Assert(File.ReadAllBytes(textPath).SequenceEqual(new UTF8Encoding(false).GetBytes("한국어 · 日本語 · français")));
    });
    Check("Atomic replacement preserves content without staging residue", () =>
    {
        SecureFiles.AtomicWriteText(textPath, "replacement");
        Assert(File.ReadAllText(textPath) == "replacement" && Directory.GetFiles(privateDirectory).Length == 1);
    });
    if (!OperatingSystem.IsWindows())
    {
        Check("POSIX directory/file modes remain0700/0600", () =>
        {
            Assert(File.GetUnixFileMode(privateDirectory) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert(File.GetUnixFileMode(textPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
        Check("Linked private directory is refused", () =>
        {
            var link = Path.Combine(root, "directory-link"); Directory.CreateSymbolicLink(link, privateDirectory);
            try { Rejected<IOException>(() => SecureFiles.CreatePrivateDirectory(link)); }
            finally { Directory.Delete(link); }
        });
        Check("Linked destination cannot overwrite its target", () =>
        {
            var link = Path.Combine(privateDirectory, "file-link"); File.CreateSymbolicLink(link, textPath);
            try { Rejected<IOException>(() => SecureFiles.AtomicWriteText(link, "unexpected")); Assert(File.ReadAllText(textPath) == "replacement"); }
            finally { File.Delete(link); }
        });
    }
    Check("Runtime deletion refuses an unowned path", () =>
        Rejected<IOException>(() => SecureFiles.DeleteOwnedRuntime(privateDirectory, root)));
    if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
    {
        SecureFiles.CreatePrivateDirectory(keyDirectory);
        var plain = Encoding.UTF8.GetBytes("Synthetic account-free credential storage contract");
        var encrypted = VaultProtection.Protect(plain, keyDirectory);
        Check("Native protector round trip survives cache release", () =>
        {
            VaultProtection.ReleaseKeys(); Assert(VaultProtection.Unprotect(encrypted, keyDirectory).SequenceEqual(plain));
        });
        Check("Encryption uses independent ciphertext for repeated plaintext", () =>
            Assert(!encrypted.SequenceEqual(VaultProtection.Protect(plain, keyDirectory))));
        Check("Ciphertext never contains the fixture plaintext", () =>
            Assert(!Encoding.UTF8.GetString(encrypted).Contains(Encoding.UTF8.GetString(plain), StringComparison.Ordinal)));
        Check("Tamper is rejected by the native protector", () =>
        {
            var changed = (byte[])encrypted.Clone(); changed[^1] ^= 1;
            Rejected<CryptographicException>(() =>
            {
                try { VaultProtection.Unprotect(changed, keyDirectory); }
                catch (System.ComponentModel.Win32Exception error) { throw new CryptographicException("Native DPAPI rejected tampering.", error); }
            });
        });
        Check("Truncated ciphertext is rejected", () =>
        {
            Rejected<CryptographicException>(() =>
            {
                try { VaultProtection.Unprotect(new byte[1], keyDirectory); }
                catch (System.ComponentModel.Win32Exception error) { throw new CryptographicException("Native DPAPI rejected truncation.", error); }
            });
        });
        if (OperatingSystem.IsMacOS())
        {
            Check("Mac envelope retainsCADM1 plus nonce/tag lengths", () =>
                Assert(encrypted.AsSpan(0, 5).SequenceEqual(Encoding.ASCII.GetBytes("CADM1")) && encrypted.Length == plain.Length + 33));
            Check("Unknown envelope is rejected", () =>
            { var changed = (byte[])encrypted.Clone(); changed[0] ^= 1; Rejected<CryptographicException>(() => VaultProtection.Unprotect(changed, keyDirectory)); });
            Check("Missing Keychain key does not get silently replaced", () =>
            {
                VaultProtection.ReleaseKeys(); MacKeychain.DeleteDiagnosticKey(keyDirectory);
                Rejected<CryptographicException>(() => VaultProtection.Unprotect(encrypted, keyDirectory));
                Rejected<CryptographicException>(() => MacKeychain.ReadOrCreateKey(SecureFiles.Hash(keyDirectory), false));
            });
        }
    }
    else if (OperatingSystem.IsLinux())
    {
        Check("Malformed Linux envelopes refuse before any keyring access", () =>
            Rejected<CryptographicException>(() => VaultProtection.Unprotect(new byte[1], root)));
    }
    else
    {
        Check("Unsupported protector never falls through Windows/Mac", () =>
        {
            Rejected<PlatformNotSupportedException>(() => VaultProtection.Protect(new byte[1], root));
            Rejected<PlatformNotSupportedException>(() => VaultProtection.Unprotect(new byte[1], root));
        });
    }
}
finally
{
    VaultProtection.ReleaseKeys();
    if (OperatingSystem.IsMacOS()) MacKeychain.DeleteDiagnosticKey(keyDirectory);
    if (Path.GetDirectoryName(root) != parent || !Path.GetFileName(root).StartsWith("codex-security-contract-", StringComparison.Ordinal) ||
        (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected diagnostic cleanup root.");
    Directory.Delete(root, true);
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, failed = 0, checks,
    scope = "Native private file and vault primitives in disposable storage only; synthetic plaintext, no real accounts/home/Codex access. Linux provider acceptance requires the separate isolated native keyring suite." }));
