# Operations

Public `Gaebabi/codex-switch`, baseline `main`, is the canonical source. Clone into any directory. Source/app version is **1.28.0**; published binary version remains **1.27.6**. Binary delivery is manual GitHub Release ZIPs plus SHA256SUMS; no automatic updater or CI release publisher exists.

## Build and verify

Run from the repository root with .NET 8.0.4xx. `global.json` allows current patch updates.

```powershell
.\scripts\build.ps1 -Verify
python scripts/verify-queues.py --executable artifacts/win-x64/CodexSwitch.exe
python scripts/verify-localization.py
dotnet run --project tests/localization/CodexSwitch.Localization.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
```

Equivalent direct commands:

```text
dotnet publish src/windows/CodexSwitch.Windows.csproj -c Release -r win-x64 --self-contained true --nologo -p:UseSharedCompilation=false -o artifacts/win-x64
dotnet build src/macos/CodexSwitch.Mac.csproj -c Release --nologo -p:UseSharedCompilation=false
```

[Contributor checks](../../CONTRIBUTING.md) / [Mac native/build guide](MACOS.md) / [CI boundaries](../CI.md). Cross-publish is not native account acceptance. Linux security/handoff and the .NET LTS migration are [planned](../PLATFORM_SUPPORT_PLAN.md).

## Data and installed packages

Windows data remains `%LOCALAPPDATA%/CodexAccountDesk`; Mac remains `~/Library/Application Support/CodexAccountDesk`. Default Codex home is `CODEX_HOME` or `~/.codex`. Preserve data/key-service identities, vault format, bundle ID and IPC names during source cleanup.

Windows per-user installations use `%LOCALAPPDATA%/Programs/CodexSwitch/<version>`. Stage an update in a new version folder; use `--request-exit --data-dir <existing-directory>` to stop only the utility, then start the new complete copy with the existing data/home. Do not overwrite a running folder or terminate native Codex for installation verification.

Authentication, vaults, raw captures, reports, packages and recovery archives stay ignored. `--record-dashboard` captures are unredacted. `python scripts/render-demo.py` uses temporary fictional data and does not start Codex. README icons share `assets/macos/app-icon.png`; keep one canonical asset.

Built-in language resources are `localization/index.json`, `localization/packs/*.json` and `localization/legacy-keys.json`; [schema/import/contribution contracts](../LOCALIZATION.md) are versioned. Local user packs live privately in `<application-data>/languages/pack-<code>.json`, independently of credential encryption. Missing saved packs display English and report their absence while preserving the preference. Do not translate authored account names or change vault/client identities.

Documentation capture accepts `--language`, `--output`, `--width`, `--height` and an optional `--pack` local fixture. Use the actual application minimum of 500×360. The capture process alone enables WPF rendering in a disconnected display session and rejects empty bitmaps; it does not change production graphics or system settings.

## Git and publication

Use issues and scoped PRs with screenshot evidence. The owner authorized the repository foundation and language-pack commit/push/PR, review and merge. A push/merge is not a binary Release. CI runs on PR/main/manual dispatch with read-only permissions; no signing/account secrets or privileged fork events. [Issue conventions](ISSUES.md)

Full-history publication audits still report historical generic checkout paths; current published instructions are portable. Do not hide those findings or rewrite public history as routine cleanup.
