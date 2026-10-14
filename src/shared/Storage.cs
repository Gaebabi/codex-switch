using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed class AccountVault
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "accounts.vault");
    public VaultData Data { get; private set; } = new();
    public object SyncRoot { get; } = new();
    public AccountVault(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        SecureFiles.CreatePrivateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) return;
        var bytes = VaultProtection.Unprotect(File.ReadAllBytes(FilePath), DirectoryPath);
        try { Data = JsonSerializer.Deserialize<VaultData>(bytes) ?? throw new InvalidDataException("계정 저장소가 비어 있습니다."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        if (Data.Version != 1) throw new InvalidDataException("이 버전에서 읽을 수 없는 계정 저장소입니다.");
        foreach (var profile in Data.Accounts)
        {
            var identity = AuthDocument.Parse(profile.AuthJson);
            if (identity.Key != profile.Key) throw new InvalidDataException("계정 저장소의 사용자 정보가 일치하지 않습니다.");
            profile.SubscriptionActiveUntil = identity.SubscriptionActiveUntil;
            profile.SubscriptionLastChecked = identity.SubscriptionLastChecked;
            if (profile.Usage != null) UsageHistory.Record(profile, profile.Usage);
        }
        if (Data.Accounts.Select(a => a.Key).Distinct().Count() != Data.Accounts.Count)
            throw new InvalidDataException("계정 저장소에 중복 식별자가 있습니다.");
    }
    public AccountProfile Upsert(string authJson)
    {
        lock (SyncRoot)
        {
        var parsed = AuthDocument.Parse(authJson);
        var existing = Data.Accounts.Find(a => a.Key == parsed.Key);
        if (existing == null) { Data.Accounts.Add(parsed); return parsed; }
        if (existing.AuthJson != authJson) { existing.AuthJson = authJson; existing.CredentialRevision++; }
        if (parsed.Email.Length > 0) existing.Email = parsed.Email;
        if (parsed.Plan != "unknown") existing.Plan = parsed.Plan;
        existing.SubscriptionActiveUntil = parsed.SubscriptionActiveUntil;
        existing.SubscriptionLastChecked = parsed.SubscriptionLastChecked;
        return existing;
        }
    }
    public void Save()
    {
        lock (SyncRoot)
        {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Data);
        try { SecureFiles.AtomicWrite(FilePath, VaultProtection.Protect(bytes, DirectoryPath)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
}

internal static class SecureFiles
{
    public static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("계정 저장소로 연결 경로를 사용할 수 없습니다.");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows 사용자 정보를 읽을 수 없습니다.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(owner);
        acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    public static void AtomicWrite(string target, byte[] bytes)
    {
        var absolute = Path.GetFullPath(target);
        var directory = Path.GetDirectoryName(absolute) ?? throw new IOException("저장 경로가 올바르지 않습니다.");
        Directory.CreateDirectory(directory);
        if (File.Exists(absolute) && (File.GetAttributes(absolute) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("로그인 파일이 연결 경로여서 교체할 수 없습니다.");
        var staging = Path.Combine(directory, ".account-desk-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(staging, options))
            { stream.Write(bytes); stream.Flush(true); }
            if (!OperatingSystem.IsWindows()) File.Move(staging, absolute, true);
            else if (File.Exists(absolute)) File.Replace(staging, absolute, null);
            else File.Move(staging, absolute);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public static void AtomicWriteText(string target, string text) => AtomicWrite(target, new UTF8Encoding(false).GetBytes(text));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void DeleteOwnedRuntime(string root, string path)
    {
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var absolutePath = Path.GetFullPath(path);
        if (!absolutePath.StartsWith(absoluteRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Guid.TryParseExact(Path.GetFileName(absolutePath), "N", out _))
            throw new IOException("임시 로그인 폴더의 경로 검증에 실패했습니다.");
        if (!Directory.Exists(absolutePath)) return;
        VerifyNoLinks(absolutePath);
        Directory.Delete(absolutePath, true);
    }
    private static void VerifyNoLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("임시 폴더에 연결 경로가 있어 정리를 중단했습니다.");
        if (!Directory.Exists(path)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) VerifyNoLinks(child);
    }
}

internal static class WindowsProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = encrypt ? CryptProtectData(ref input, "Codex Account Desk", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows 계정 암호화 저장소를 사용할 수 없습니다.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            var zeros = new byte[bytes.Length];
            Marshal.Copy(zeros, 0, input.Data, zeros.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
