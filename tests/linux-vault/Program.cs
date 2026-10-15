using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexAccountDesk;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native vault verification requires Linux.");
var root = Environment.GetEnvironmentVariable("CODEX_SWITCH_LINUX_TEST_ROOT") ?? throw new InvalidOperationException("An isolated test root is required.");
root = Path.GetFullPath(root);
var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? "";
if (!Path.GetFileName(root).StartsWith("codex-linux-keyring-test-", StringComparison.Ordinal) || address.Contains(';') || !address.StartsWith("unix:path=", StringComparison.Ordinal))
    throw new InvalidOperationException("Only the disposable file-socket D-Bus session is permitted.");
var socket = Uri.UnescapeDataString(address[10..].Split(',')[0]);
if (!Path.GetFullPath(socket).StartsWith(root + "/bus/", StringComparison.Ordinal)) throw new InvalidOperationException("Personal/system D-Bus sessions are prohibited.");
LinuxPrivateFiles.CreateDirectory(root);
if (args.Length < 1) throw new ArgumentException("A native test phase is required.");
var checks = new List<string>(); var phase = args[0];
var directory = Path.Combine(root, "data", "linux-self-test-protector");
var appDirectory = Path.Combine(root, "data", "linux-self-test-app");
var file = Path.Combine(directory, "accounts.vault");
var plain = Encoding.UTF8.GetBytes("Synthetic native Linux vault; no account credential");
void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Native Linux vault contract failed."); }
void Check(string name, Action action) { action(); checks.Add(name); }
void CryptoFailure(Action action)
{ try { action(); } catch (CryptographicException) { return; } throw new InvalidOperationException("Expected authenticated-envelope refusal."); }
void KeyFailure(LinuxKeyFailure expected, Action action)
{ try { action(); } catch (LinuxVaultException error) when (error.Failure == expected) { return; } throw new InvalidOperationException("Expected native key failure was not observed."); }
void FileFailure(Action action)
{ try { action(); } catch (IOException) { return; } throw new InvalidOperationException("Expected private-file refusal."); }
SecureFiles.CreatePrivateDirectory(directory);
try
{
    switch (phase)
    {
        case "no-service":
            Check("Missing service refuses encryption without writing a vault", () =>
            { KeyFailure(LinuxKeyFailure.Unavailable, () => VaultProtection.Protect(plain, directory)); Assert(!File.Exists(file)); });
            break;
        case "roundtrip":
            var encrypted = VaultProtection.Protect(plain, directory); SecureFiles.AtomicWrite(file, encrypted);
            Check("Linux envelope usesCADL1, random nonce and authenticated payload", () =>
            {
                Assert(encrypted.AsSpan(0, 5).SequenceEqual(Encoding.ASCII.GetBytes("CADL1")) && encrypted.Length == plain.Length + 33);
                Assert(!encrypted.SequenceEqual(VaultProtection.Protect(plain, directory)));
                VaultProtection.ReleaseKeys(); Assert(VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(file), directory).SequenceEqual(plain));
            });
            Check("Tamper/truncation/foreign envelopes refuse without changing ciphertext", () =>
            {
                var changed = (byte[])encrypted.Clone(); changed[^1] ^= 1; CryptoFailure(() => VaultProtection.Unprotect(changed, directory));
                CryptoFailure(() => VaultProtection.Unprotect(encrypted[..32], directory)); changed = (byte[])encrypted.Clone(); changed[3] = (byte)'M';
                CryptoFailure(() => VaultProtection.Unprotect(changed, directory)); Assert(File.ReadAllBytes(file).SequenceEqual(encrypted));
            });
            Check("Binary keys preserve exact bytes independently of provider MIME tagging", () =>
            {
                var path = Path.Combine(root, "data", "linux-self-test-binary"); SecureFiles.CreatePrivateDirectory(path);
                var key = RandomNumberGenerator.GetBytes(32); key[0] = 0; key[1] = 255;
                try
                {
                    LinuxSecretService.AddDiagnosticKey(path, key);
                    var actual = LinuxSecretService.ReadOrCreateKey(SecureFiles.Hash(path), false);
                    try { Assert(CryptographicOperations.FixedTimeEquals(key, actual)); }
                    finally { CryptographicOperations.ZeroMemory(actual); }
                }
                finally { CryptographicOperations.ZeroMemory(key); LinuxSecretService.DeleteDiagnosticKey(path); }
            });
            Check("Actual application vault save/reopen preserves JSON and private modes", () =>
            {
                var vault = new AccountVault(appDirectory); vault.Data.Theme = "light"; vault.Save(); VaultProtection.ReleaseKeys();
                var reopened = new AccountVault(appDirectory); Assert(reopened.Data.Theme == "light" && reopened.Data.Version == 1 && reopened.Data.Accounts.Count == 0 && reopened.Data.Drives.Single().Root == "/");
                Assert(File.GetUnixFileMode(appDirectory) == (UnixFileMode)0x1c0 && File.GetUnixFileMode(vault.FilePath) == (UnixFileMode)0x180);
            });
            Check("No-follow file and intermediate-directory links cannot alter targets", () =>
            {
                var target = Path.Combine(directory, "target.txt"); SecureFiles.AtomicWriteText(target, "unchanged");
                var link = Path.Combine(directory, "file-link"); File.CreateSymbolicLink(link, target);
                try { FileFailure(() => SecureFiles.AtomicWriteText(link, "unexpected")); FileFailure(() => SecureFiles.ReadPrivateBytes(link)); Assert(File.ReadAllText(target) == "unchanged"); }
                finally { File.Delete(link); }
                var folder = Path.Combine(directory, "directory-link"); Directory.CreateSymbolicLink(folder, appDirectory);
                try { FileFailure(() => SecureFiles.AtomicWriteText(Path.Combine(folder, "unexpected.txt"), "unexpected")); Assert(!File.Exists(Path.Combine(appDirectory, "unexpected.txt"))); }
                finally { Directory.Delete(folder); }
            });
            Check("Insecure modes reject before replacement and preserve existing bytes", () =>
            {
                var target = Path.Combine(directory, "mode.txt"); SecureFiles.AtomicWriteText(target, "unchanged"); File.SetUnixFileMode(target, (UnixFileMode)0x1a4);
                try { FileFailure(() => SecureFiles.AtomicWriteText(target, "unexpected")); FileFailure(() => SecureFiles.ReadPrivateBytes(target)); Assert(File.ReadAllText(target) == "unchanged"); }
                finally { File.SetUnixFileMode(target, (UnixFileMode)0x180); }
            });
            Check("Ambiguous and malformed keys never get replaced", () =>
            {
                foreach (var mode in new[] { "ambiguous", "invalid" })
                {
                    var path = Path.Combine(root, "data", "linux-self-test-" + mode); SecureFiles.CreatePrivateDirectory(path);
                    var key = RandomNumberGenerator.GetBytes(mode == "invalid" ? 17 : 32);
                    try
                    {
                        LinuxSecretService.AddDiagnosticKey(path, key); if (mode == "ambiguous") LinuxSecretService.AddDiagnosticKey(path, key);
                        KeyFailure(mode == "ambiguous" ? LinuxKeyFailure.Ambiguous : LinuxKeyFailure.Invalid, () => VaultProtection.Protect(plain, path));
                        Assert(LinuxSecretService.CountDiagnosticKeys(path) == (mode == "ambiguous" ? 2 : 1));
                    }
                    finally { CryptographicOperations.ZeroMemory(key); LinuxSecretService.DeleteDiagnosticKey(path); }
                }
            });
            break;
        case "read":
            Check("OS key and encrypted app data survive process/provider restart", () =>
            { Assert(VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(file), directory).SequenceEqual(plain)); Assert(new AccountVault(appDirectory).Data.Theme == "light"); });
            break;
        case "locked":
            var before = File.ReadAllBytes(file);
            Check("Locked existing key refuses read/save without creating a replacement", () =>
            {
                KeyFailure(LinuxKeyFailure.Locked, () => VaultProtection.Unprotect(before, directory)); KeyFailure(LinuxKeyFailure.Locked, () => VaultProtection.Protect(plain, directory));
                Assert(File.ReadAllBytes(file).SequenceEqual(before) && LinuxSecretService.CountDiagnosticKeys(directory) == 1);
            });
            break;
        case "unlock":
            GnomeKeyringControl.UnlockFromInput();
            Check("Actual owned collection unlock restores the original encrypted data", () =>
                Assert(VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(file), directory).SequenceEqual(plain)));
            break;
        case "missing":
            var original = File.ReadAllBytes(file); LinuxSecretService.DeleteDiagnosticKey(directory); VaultProtection.ReleaseKeys();
            Check("Missing key with existing ciphertext never creates or rotates a key", () =>
            {
                KeyFailure(LinuxKeyFailure.Missing, () => VaultProtection.Unprotect(original, directory)); KeyFailure(LinuxKeyFailure.Missing, () => VaultProtection.Protect(plain, directory));
                Assert(LinuxSecretService.CountDiagnosticKeys(directory) == 0 && File.ReadAllBytes(file).SequenceEqual(original));
            });
            break;
        case "offline":
            Check("Offline provider refuses existing ciphertext without altering it", () =>
            { var bytes = File.ReadAllBytes(Path.Combine(appDirectory, "accounts.vault")); KeyFailure(LinuxKeyFailure.Unavailable, () => new AccountVault(appDirectory)); Assert(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(appDirectory, "accounts.vault")))); });
            break;
        case "timeout":
            Check("Paused owned provider cancels at the bounded native deadline", () =>
            { var clock = Stopwatch.StartNew(); KeyFailure(LinuxKeyFailure.TimedOut, () => new AccountVault(appDirectory)); Assert(clock.Elapsed >= TimeSpan.FromSeconds(8) && clock.Elapsed < TimeSpan.FromSeconds(15)); });
            break;
        case "foreign-owner":
            Check("Foreign-owned private files refuse read and replacement", () =>
            { var path = Path.Combine(directory, "foreign.txt"); FileFailure(() => SecureFiles.ReadPrivateBytes(path)); FileFailure(() => SecureFiles.AtomicWriteText(path, "unexpected")); });
            break;
        case "concurrent-child":
            var shared = Path.GetFullPath(args[1]); if (!shared.StartsWith(root + "/data/linux-self-test-concurrent", StringComparison.Ordinal)) throw new ArgumentException("Unexpected child data path.");
            SecureFiles.CreatePrivateDirectory(shared); SecureFiles.AtomicWrite(Path.Combine(shared, "cipher-" + Environment.ProcessId), VaultProtection.Protect(plain, shared));
            checks.Add("Independent native key-creation participant completed");
            break;
        case "concurrent":
            var sharedDirectory = Path.Combine(root, "data", "linux-self-test-concurrent"); SecureFiles.CreatePrivateDirectory(sharedDirectory);
            var children = new List<Process>();
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Owned test executable missing.");
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    if (Path.GetFileNameWithoutExtension(executable) == "dotnet") start.ArgumentList.Add(typeof(Program).Assembly.Location);
                    start.ArgumentList.Add("concurrent-child"); start.ArgumentList.Add(sharedDirectory); children.Add(Process.Start(start) ?? throw new IOException("Owned native participant did not start."));
                }
                foreach (var child in children) { if (!child.WaitForExit(45000) || child.ExitCode != 0) throw new InvalidOperationException("Concurrent native key creation failed."); }
                Check("Independent processes share exactly one OS key and decrypt every ciphertext", () =>
                { var files = Directory.GetFiles(sharedDirectory, "cipher-*"); Assert(files.Length == 4 && LinuxSecretService.CountDiagnosticKeys(sharedDirectory) == 1); foreach (var path in files) Assert(VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(path), sharedDirectory).SequenceEqual(plain)); });
            }
            finally { foreach (var child in children) { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } child.Dispose(); } }
            break;
        default: throw new ArgumentException("Unknown native phase.");
    }
}
finally { VaultProtection.ReleaseKeys(); CryptographicOperations.ZeroMemory(plain); }
Console.WriteLine(JsonSerializer.Serialize(new { phase, architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
    passed = checks.Count, failed = 0, checks, scope = "Actual configured Secret Service/GNOME Keyring and Linux private storage in an owned disposable D-Bus/XDG session only. Synthetic bytes/empty account vault; no real account, home, Codex or personal keyring access." }));
