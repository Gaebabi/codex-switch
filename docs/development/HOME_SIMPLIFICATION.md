# Home simplification

Source1.44.0 preserves the tank-fill metaphor, active-account wave, large quota percentage, account grid and direct per-account Switch/Recover actions. It changes visual hierarchy and global action placement only. Native verification, published downloads and installed acceptance are separate [release gates](RELEASE_1_44_0.md).

## Global actions

The titlebar has Add account, Refresh and one Settings gear, alongside ordinary window controls. Settings contains model providers, automatic switching and credit protection, Restart Codex, language, dark/light appearance, current-account import and drive settings. Shared desktop hosts also retain their existing Quit and Linux Start Codex actions inside Settings.

Automatic switching retains its existing persisted preferences, thresholds, priority and credit-use controls. Its enabled state stays visible on Home as noninteractive text; its Off/On state is always named in Settings. Restart/model actions retain their busy guards. No account, vault, provider routing or switch coordinator behavior is changed.

## Account cards

Tank level, active wave, quota selection, percentage, reset countdown, plan, reset credits, balance and per-account actions remain. Secondary values use normal weight and muted text, with warning colors retained for low quota, stale data, usage limits and near deadlines. Unknown subscription dates do not create a repeated empty date row; the account detail still explains unavailable subscription data. Routine registered/current state text is omitted where the account action already conveys it. Login warnings remain actionable in the account header.

Four accounts keep the existing grid and fit the500×360minimum. Larger Windows account collections use vertical overflow with a126pxminimum row, so eight accounts remain readable and the last cards can be reached. Shared desktop hosts retain their existing row/scroll policy.

## Evidence scope

The local Windows production-control renderer exports before/after previews in English/Korean, dark/light, and800×560/600×400/500×360sizes. It never shows or activates a window and uses fictional accounts/resources. These are production WPF component renders, not native window screenshots or owner account acceptance. [Images, native evidence and checks](../evidence/home-simplification/README.md).

The provider-editor/home UI harness passes54checks, including minimum-width Settings reachability, retained menu entries, real language/theme/automation persistence in disposable storage, busy restart refusal,0/1/4/8accounts, long names, warnings, enabled-state visibility and scroll access. Windows, shared desktop, Mac and Linux source compilation and localization/link checks are recorded separately. Native popup placement, keyboard interaction and native Mac/Linux acceptance remain pending before publication or installation.
