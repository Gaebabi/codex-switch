using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexAccountDesk;

internal sealed record GatewayHistorySource(string Route, string Model, string? ProviderId = null);
internal sealed class GatewayHistoryCheckpoint
{
    public string Conversation { get; set; } = "";
    public GatewayHistorySource? Source { get; set; }
    public string? Summary { get; set; }
    public string? PendingRequest { get; set; }
}

internal sealed class GatewayHistoryStore
{
    internal const string Prefix = "codex-switch-history:v1:";
    internal const int MaximumSummaryBytes = 256 * 1024;
    private const int MaximumEntries = 4096, MaximumVaultBytes = 16 * 1024 * 1024;
    private readonly string _directory, _path;
    private readonly object _gate = new();
    private Dictionary<string, GatewayHistoryCheckpoint> _items;
    internal GatewayHistoryStore(string directory)
    {
        _directory = directory; _path = Path.Combine(directory, "model-history.vault"); _items = new(StringComparer.Ordinal);
        Reload();
    }
    private void Reload()
    {
        _items = new(StringComparer.Ordinal);
        if (!File.Exists(_path)) return;
        var encrypted = SecureFiles.ReadPrivateBytes(_path);
        if (encrypted.Length > MaximumVaultBytes + 1024) throw new InvalidDataException("The model history checkpoint store exceeds its bound.");
        var plaintext = VaultProtection.Unprotect(encrypted, _directory);
        try { _items = JsonSerializer.Deserialize<Dictionary<string, GatewayHistoryCheckpoint>>(plaintext) ?? throw new InvalidDataException("The model history checkpoint store is empty."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (_items.Count > MaximumEntries || _items.Any(pair => pair.Key.Length > 160 || pair.Value.Conversation.Length != 64 || pair.Value.Summary != null && Encoding.UTF8.GetByteCount(pair.Value.Summary) > MaximumSummaryBytes))
            throw new InvalidDataException("The model history checkpoint store is invalid.");
    }
    internal static string Conversation(string? session, GatewayIdentity identity) =>
        !string.IsNullOrEmpty(session) && session.Length <= 256 ? SecureFiles.Hash((identity.AccountId ?? identity.Token) + "\n" + session) :
        throw new GatewayHistoryException("A stable conversation identity is required to transfer compressed history. Keep the original chat and send a new turn from Codex.");
    internal static string OpaqueKey(string ciphertext) => "opaque:" + SecureFiles.Hash(ciphertext);
    internal GatewayHistoryCheckpoint? Read(string key, string conversation)
    {
        lock (_gate)
        {
            Reload();
            if (!_items.TryGetValue(key, out var item)) return null;
            if (item.Conversation != conversation) throw new GatewayHistoryException("This compressed checkpoint belongs to another conversation or signed-in account. The original history was not sent.");
            return new() { Conversation = item.Conversation, Source = item.Source, Summary = item.Summary, PendingRequest = item.PendingRequest };
        }
    }
    internal bool HasPortableConversation(string conversation)
    { lock (_gate) { Reload(); return _items.Any(pair => pair.Key.StartsWith(Prefix, StringComparison.Ordinal) && pair.Value.Conversation == conversation); } }
    internal void RememberSource(string ciphertext, string conversation, GatewayHistorySource source)
    {
        var key = OpaqueKey(ciphertext);
        lock (_gate)
        {
            using var transaction = new VaultTransaction(_path);
            Reload();
            if (_items.TryGetValue(key, out var item))
            {
                if (item.Conversation != conversation || item.Source != null && item.Source != source)
                    throw new GatewayHistoryException("The compressed checkpoint has conflicting source provenance. No destination was selected for it.");
                if (item.Source != null) return;
            }
            Save(key, new() { Conversation = conversation, Source = source });
        }
    }
    internal string? BeginSummary(string key, string conversation, GatewayHistorySource source, string request)
    {
        lock (_gate)
        {
            using var transaction = new VaultTransaction(_path);
            var existing = Read(key, conversation);
            if (existing?.Summary != null) return existing.Summary;
            if (existing?.PendingRequest == request) throw new GatewayHistoryException("The previous summary request has no confirmed result. It was not automatically sent again. Keep the source chat; send a new explicit turn to retry.");
            Save(key, new() { Conversation = conversation, Source = source, PendingRequest = request });
            return null;
        }
    }
    internal void CompleteSummary(string key, string conversation, GatewayHistorySource source, string summary)
    { lock (_gate) Save(key, new() { Conversation = conversation, Source = source, Summary = summary }); }
    internal string CreatePortable(string conversation, GatewayHistorySource source, string summary)
    {
        var token = Prefix + Guid.NewGuid().ToString("N");
        lock (_gate) Save(token, new() { Conversation = conversation, Source = source, Summary = summary });
        return token;
    }
    private void Save(string key, GatewayHistoryCheckpoint item)
    {
        using var transaction = new VaultTransaction(_path);
        Reload();
        if (item.Summary != null && (string.IsNullOrWhiteSpace(item.Summary) || Encoding.UTF8.GetByteCount(item.Summary) > MaximumSummaryBytes)) throw new GatewayHistoryException("The source summary is empty or exceeds the portable checkpoint limit. The original history is unchanged.");
        if (!_items.ContainsKey(key) && _items.Count >= MaximumEntries) throw new GatewayHistoryException("The protected model history store is full. Existing compressed checkpoints were preserved; use a readable handoff before creating another.");
        var next = new Dictionary<string, GatewayHistoryCheckpoint>(_items, StringComparer.Ordinal) { [key] = item };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next);
        try
        {
            if (bytes.Length > MaximumVaultBytes) throw new GatewayHistoryException("The protected model history store is full. Existing checkpoints were not evicted.");
            SecureFiles.CreatePrivateDirectory(_directory);
            SecureFiles.AtomicWrite(_path, VaultProtection.Protect(bytes, _directory)); _items = next;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

internal sealed class GatewayHistoryException : Exception
{ internal GatewayHistoryException(string message) : base(message) { } }
