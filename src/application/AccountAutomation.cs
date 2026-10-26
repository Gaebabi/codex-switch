using System;
using System.Linq;

namespace CodexAccountDesk;

internal static class AccountAutomation
{
    // Called only after a complete refresh, on the UI thread. Reserve the attempt durably
    // before handing off so timer re-entry or a utility restart cannot trigger a second switch.
    public static AccountProfile? Reserve(AccountService service, bool busy, DateTimeOffset now)
    {
        lock (service.Vault.SyncRoot)
        {
            var data = service.Vault.Data;
            if (busy || service.IsBusy || !data.AutomaticSwitchEnabled || data.PendingSwitch != null ||
                data.LastAutomaticSwitchAttempt is DateTimeOffset last && now - last < AutomaticSwitchPolicy.Cooldown) return null;
            var target = AutomaticSwitchPolicy.Select(service.Accounts, data.AutomaticSwitchThreshold, now,
                data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null);
            if (target == null) return null;
            data.LastAutomaticSwitchAttempt = now; service.Vault.Save(); return target;
        }
    }
    public static void Pause(AccountService service)
    {
        lock (service.Vault.SyncRoot) { service.Vault.Data.AutomaticSwitchEnabled = false; service.Vault.Save(); }
    }
    public static AccountProfile? Next(AccountService service, DateTimeOffset now)
    {
        lock (service.Vault.SyncRoot)
        {
            var data = service.Vault.Data;
            if (data.PendingSwitch != null) return service.Accounts.FirstOrDefault(p => p.Key == data.PendingSwitch.TargetAccountKey);
            return AutomaticSwitchPolicy.Next(service.Accounts, data.AutomaticSwitchThreshold, now,
                data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null);
        }
    }
    public static string NextLabel(AccountService service, DateTimeOffset now)
    {
        var next = Next(service, now);
        return next == null ? Loc.T("auto.next_unavailable") : Loc.F("auto.next", next.DisplayName);
    }
    public static AccountProfile[] OrderedAccounts(AccountService service)
    {
        lock (service.Vault.SyncRoot)
        {
            var accounts = service.Accounts.ToDictionary(p => p.Key);
            return service.Vault.Data.AutomaticSwitchPriority.Where(accounts.ContainsKey).Distinct(StringComparer.Ordinal)
                .Concat(accounts.Keys.OrderBy(k => k, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).Select(k => accounts[k]).ToArray();
        }
    }
    public static void SetPriority(AccountService service, string key, int position)
    {
        lock (service.Vault.SyncRoot)
        {
            var order = OrderedAccounts(service).Select(p => p.Key).ToList();
            if (!order.Remove(key)) throw new InvalidOperationException("선택한 계정이 삭제됐습니다.");
            order.Insert(Math.Clamp(position - 1, 0, order.Count), key);
            service.Vault.Data.AutomaticSwitchPriority = order;
            service.Vault.Data.AutomaticSwitchUsePriority = true; service.Vault.Save();
        }
    }
}
