using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CodexAccountDesk;

internal static class RecoveryEvidence
{
    internal static async Task CaptureAsync(MainWindow window, AccountService service, string directory)
    {
        if (window == null || !Path.GetFileName(Path.GetDirectoryName(service.Vault.DirectoryPath))!.StartsWith("codex-ui-evidence-", StringComparison.Ordinal))
            throw new InvalidOperationException("An isolated fictional evidence vault is required.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var profiles = service.Accounts;
        var plan = new AccountSwitchPlan { Phase = SwitchPhase.Finished, AccountApplied = true,
            SourceAccountKey = profiles[0].Key, TargetAccountKey = profiles[0].Key,
            Chats = new() { new() { ResumeState = ChatResumeState.Resumed } } };
        var data = service.Vault.Data;
        var old = data.PendingSwitch;
        var current = typeof(AccountService).GetProperty("CurrentKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var ready = typeof(MainWindow).GetField("_ready", flags)!;
        var render = typeof(MainWindow).GetMethod("RenderAccounts", flags)!;
        var savedKey = current.GetValue(service);
        var savedReady = ready.GetValue(window);
        var inventory = typeof(MainWindow).GetField("_accountWindows", flags)!;
        var savedInventory = inventory.GetValue(window);
        void Draw() { render.Invoke(window, null); window.UpdateLayout(); }
        void Capture(string name) => typeof(NativeUiEvidence).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(Window), typeof(string) }, null)!.Invoke(null, new object[] { window, Path.Combine(directory, name) });
        try
        {
            window.PrepareUiEvidence("en", true); current.SetValue(service, profiles[1].Key); ready.SetValue(window, true);
            inventory.SetValue(window, new Dictionary<string, int>());
            await Task.Delay(300);
            plan.Phase = SwitchPhase.Resuming; data.PendingSwitch = plan; Draw(); await Task.Delay(300);
            var pending = window.EvidenceAccountActions(profiles[0].Key);
            if (!pending.SequenceEqual(new[] { Loc.T("switch.open_window"), Loc.T("switch.recover") }))
                throw new InvalidOperationException("An unresolved handoff must offer Open and Recover.");
            Capture("recovery-before.png");
            plan.Phase = SwitchPhase.Finished; Draw(); await Task.Delay(300);
            var finished = window.EvidenceAccountActions(profiles[0].Key);
            if (!finished.SequenceEqual(new[] { Loc.T("switch.open_window") }) || !ReferenceEquals(data.PendingSwitch, plan))
                throw new InvalidOperationException("A completed handoff still blocks or its receipt was discarded.");
            Capture("recovery-after.png");
            File.WriteAllText(Path.Combine(directory, "recovery-receipt.json"), JsonSerializer.Serialize(new {
                passed = true, pendingActions = pending, completedActions = finished,
                scope = "Actual Windows UI, fictional account and handoff; no account switch, Codex process or Continue." }));
            await Task.CompletedTask;
        }
        finally { data.PendingSwitch = old; current.SetValue(service, savedKey); ready.SetValue(window, savedReady); inventory.SetValue(window, savedInventory); Draw(); }
    }
}
