# Operations

Public `Gaebabi/codex-switch`, baseline `main`, is the canonical source. Clone into any directory. Source/app version is **1.40.1**; published binary version remains **1.27.6**. Binary delivery is manual GitHub Release ZIPs plus SHA256SUMS; no automatic updater or CI release publisher exists. [Linux candidate packaging](LINUX_PACKAGES.md) is separate unsigned PR/CI preview delivery, not a supported account-switching Release.

## Build and verify

The Linux source1.40.0 preview enables ordinary account/usage and [managed desktop integration](LINUX.md#source1400-ordinary-integration--issue74--pr75). Start Codex from this utility before working; an external/unmanaged client cannot be adopted. Existing credential/context policy is respected. Explicit checksum CLI-only mode has no desktop resumption. `scripts/run-local-linux-desktop.py` exercises the production backend plus fictional auth/offline native client in an owned container; authenticated two-account continuation and full desktop/Release acceptance remain separate.

Run from the repository root with .NET10.0.4xx, currently SDK10.0.402/runtime10.0.13. `global.json` allows current patch updates. Use clean output folders for package candidates. .NET10's reviewed current Mac support baseline is macOS15+; the bundle minimum and app version match source1.40.1. [KDE6 provider contracts and native gates](KDE_SECRET_SERVICE.md) describe explicit package/checksum support; runtime providers are not installed or altered by the app. [Linux IPC](LINUX_IPC.md) records controlled launch, native peer trust, kernel/display/container requirements and the remaining lifecycle/account gates. [Local desktop-session verification](LINUX_DESKTOP_SESSION.md) exercises ordinary installed UI and actual provider/client lifecycles without host homes or credentials.

For owner-authenticated local acceptance, use the separate [interactive desktop recipe](LINUX_INTERACTIVE_DESKTOP.md). It publishes only a password-protected loopback viewer and retains its private container/data when the viewer closes. Never run it with account credentials in CI. Source1.40.1 retains the native Linux titlebar and puts session counters beside toolbar actions, avoiding a duplicate app caption while preserving WM close/minimize/maximize/resize controls.

```powershell
.\scripts\build.ps1 -Verify
python scripts/verify-queues.py --executable artifacts/win-x64/CodexSwitch.exe
python scripts/verify-localization.py
dotnet run --project tests/localization/CodexSwitch.Localization.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/temperatures/CodexSwitch.Temperatures.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/platform-security/CodexSwitch.PlatformSecurity.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/desktop-client/CodexSwitch.DesktopClient.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/protocol/CodexSwitch.Protocol.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/hardware/CodexSwitch.Hardware.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
dotnet run --project tests/composition/CodexSwitch.Composition.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
```

Equivalent direct commands:

```text
dotnet publish src/windows/CodexSwitch.Windows.csproj -c Release -r win-x64 --self-contained true --nologo -p:UseSharedCompilation=false -o artifacts/win-x64
dotnet build src/macos/CodexSwitch.Mac.csproj -c Release --nologo -p:UseSharedCompilation=false
```

[Contributor checks](../../CONTRIBUTING.md) / [Mac native/build guide](MACOS.md) / [CI boundaries](../CI.md). Cross-publish is not native account acceptance. The .NET LTS migration is complete; Linux security/handoff and remaining service boundaries follow [the platform roadmap](../PLATFORM_SUPPORT_PLAN.md).

Windows normal-shutdown regression uses disposable parent/child processes and the production shutdown/coordinator; no Codex installation or real account is involved:

```text
dotnet publish tests/windows-handoff/fixture/HandoffFixture.csproj -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false -o artifacts/handoff-fixture
dotnet run --project tests/windows-handoff/CodexSwitch.WindowsHandoff.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false -- artifacts/handoff-fixture/HandoffFixture.exe
```

## Data and installed packages

Windows data remains `%LOCALAPPDATA%/CodexAccountDesk`; Mac remains `~/Library/Application Support/CodexAccountDesk`. Default Codex home is `CODEX_HOME` or `~/.codex`. Preserve data/key-service identities, vault format, bundle ID and IPC names during source cleanup.

Source1.29.3 retires the persisted `AutoRefresh` preference and both platform toggles. Older files, including a disabled preference, load through the existing serializer and omit the retired field on normal save. Initialized-app account reads always use the existing60-second schedule; normal lifetime and read-only verification boundaries remain unchanged. [Application/timer evidence](../evidence/always-refresh/README.md)

Windows per-user installations use `%LOCALAPPDATA%/Programs/CodexSwitch/<version>`. Stage an update in a new version folder; use `--request-exit --data-dir <existing-directory>` to stop only the utility, then start the new complete copy with the existing data/home. Do not overwrite a running folder or terminate native Codex for installation verification.

Authentication, vaults, raw captures, reports, packages and recovery archives stay ignored. `--record-dashboard` captures are unredacted. `python scripts/render-demo.py` uses temporary fictional data and does not start Codex. README icons share `assets/macos/app-icon.png`; keep one canonical asset.

Built-in language resources are `localization/index.json`, `localization/packs/*.json` and `localization/legacy-keys.json`; [schema/import/contribution contracts](../LOCALIZATION.md) are versioned. Local user packs live privately in `<application-data>/languages/pack-<code>.json`, independently of credential encryption. Missing saved packs display English and report their absence while preserving the preference. Do not translate authored account names or change vault/client identities.

Documentation capture accepts `--language`, `--output`, `--width`, `--height` and an optional `--pack` local fixture. Use the actual application minimum of 500×360. The capture process alone enables WPF rendering in a disconnected display session and rejects empty bitmaps; it does not change production graphics or system settings.

Capture `--deadline-preview` for clearly fictional near/far/missing expiry/reset dates and recorded public countdown updates; `--theme dark|light` selects the existing palette. The actual WPF brushes and known off-screen visibility limitation are described in [date evidence](../evidence/date-warnings/README.md). Keep the JSON receipts private. Warning colors use future expiry72h/reset48h, independent of the existing KST calendar-date label.

Resource/disk temperatures use supported Windows read-only WDDM/storage APIs and an existing optional compatible WMI provider. Missing sensors remain unavailable; no driver/service/elevation is installed. Mac temperature collection remains unsupported. [Sources and limits](../TEMPERATURES.md). Capture `--temperature-preview` for fictional sensor-capable layout or `--live-resources` for actual device readings with fictional account/session inputs; keep receipts private and identify each scope accurately.

Application integration now builds once in `src/application`; Avalonia UI builds once in `src/desktop`. No consumer compiles the former `src/shared` sources. Desktop native probes add `--native-ui` to the composition test command on a declared Windows/Mac runner; this displays the actual shared UI with synthetic profiles/pending resources, verifies ordinary trayless close/disposal and never creates a data/home directory. Initialize Avalonia before creating themed controls/brushes in a native probe; an earlier unbound dispatcher cannot run its native event loop. Production Mac still supplies its native tray, appearance and diagnostic/capture adapters. Linux now composes the shared source preview host, with account/client/handoff actions disabled pending their native gates. Its explicit-home `--verify-queues` path reads only queue metadata before GUI/vault/client initialization; `verify-queues.py` uses owned disposable fixtures.

## Git and publication

Use issues and scoped PRs with screenshot evidence. The owner authorized the ongoing open-source issue/commit/push/PR, review and merge workflow. A push/merge is not a binary Release. CI runs on PR/main/manual dispatch with read-only permissions; no signing/account secrets or privileged fork events. [Issue conventions](ISSUES.md)

Full-history publication audits still report historical generic checkout paths; current published instructions are portable. Do not hide those findings or rewrite public history as routine cleanup.
