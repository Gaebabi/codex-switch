using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        var bytes = VaultProtection.Unprotect(SecureFiles.ReadPrivateBytes(FilePath), DirectoryPath);
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
