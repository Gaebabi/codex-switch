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
            if (busy || service.IsBusy || !data.AutomaticSwitchEnabled || data.ActivePendingSwitch is { CreditProtectionPause: false } ||
                data.ActivePendingSwitch is { CreditProtectionPause: true, Phase: not SwitchPhase.CreditPaused } ||
                data.LastAutomaticSwitchAttempt is DateTimeOffset last && now - last < AutomaticSwitchPolicy.Cooldown) return null;
            var accounts = service.Accounts;
            var currentAccounts = accounts.Where(p => p.IsCurrent).ToArray();
            var current = currentAccounts.Length == 1 ? currentAccounts[0] : null;
            if (data.ActivePendingSwitch?.CreditProtectionPause == true && current?.Key != data.ActivePendingSwitch.SourceAccountKey) return null;
            var target = AutomaticSwitchPolicy.Select(accounts, data.AutomaticSwitchThreshold, now,
                data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null);
            if (target == null && data.ActivePendingSwitch?.CreditProtectionPause == true && current != null && AutomaticSwitchPolicy.OrdinaryAvailable(current, now)) target = current;
            if (target == null) return null;
            data.LastAutomaticSwitchAttempt = now; service.Vault.Save(); return target;
        }
    }
    public static void Pause(AccountService service)
    {
        lock (service.Vault.SyncRoot) { service.Vault.Data.AutomaticSwitchEnabled = false; service.Vault.Save(); }
    }
    public static bool CanUse(AccountService service, AccountProfile profile, DateTimeOffset now) =>
        service.Vault.Data.AllowCreditUse || AutomaticSwitchPolicy.OrdinaryAvailable(profile, now, allowPendingCredentials: true);
    public static AccountProfile? CreditStopTarget(AccountService service, bool busy, DateTimeOffset now)
    {
        lock (service.Vault.SyncRoot)
        {
            var data = service.Vault.Data;
            if (data.AllowCreditUse || busy || service.IsBusy || data.ActivePendingSwitch is { CreditProtectionPause: false } ||
                data.ActivePendingSwitch is { CreditProtectionPause: true, Phase: not SwitchPhase.CreditPaused }) return null;
            var current = service.Accounts.Where(p => p.IsCurrent).ToArray();
            if (data.AutomaticSwitchEnabled && !(data.LastAutomaticSwitchAttempt is DateTimeOffset last && now - last < AutomaticSwitchPolicy.Cooldown) &&
                AutomaticSwitchPolicy.Select(service.Accounts, data.AutomaticSwitchThreshold, now,
                    data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null) != null) return null;
            return current.Length == 1 && (data.ActivePendingSwitch == null || data.ActivePendingSwitch.TargetAccountKey == current[0].Key) &&
                AutomaticSwitchPolicy.Exhausted(current[0], now) ? current[0] : null;
        }
    }
    public static AccountProfile? Next(AccountService service, DateTimeOffset now)
    {
        lock (service.Vault.SyncRoot)
        {
            var data = service.Vault.Data;
            if (data.ActivePendingSwitch is { CreditProtectionPause: false }) return service.Accounts.FirstOrDefault(p => p.Key == data.ActivePendingSwitch.TargetAccountKey);
            if (data.ActivePendingSwitch?.CreditProtectionPause == true)
            {
                var current = service.Accounts.Where(p => p.IsCurrent).ToArray();
                if (current.Length != 1 || current[0].Key != data.ActivePendingSwitch.SourceAccountKey) return null;
                var candidate = AutomaticSwitchPolicy.Select(service.Accounts, data.AutomaticSwitchThreshold, now,
                    data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null);
                if (candidate != null) return candidate;
                if (current.Length == 1 && AutomaticSwitchPolicy.OrdinaryAvailable(current[0], now)) return current[0];
                return null;
            }
            return AutomaticSwitchPolicy.Next(service.Accounts, data.AutomaticSwitchThreshold, now,
                data.AutomaticSwitchUsePriority ? OrderedAccounts(service).Select(p => p.Key).ToArray() : null);
        }
    }
    public static string NextLabel(AccountService service, DateTimeOffset now)
    {
        var next = Next(service, now);
        return next == null ? Loc.T(service.Vault.Data.ActivePendingSwitch?.CreditProtectionPause == true ? "credits.paused" : "auto.next_unavailable") : Loc.F("auto.next", next.DisplayName);
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
