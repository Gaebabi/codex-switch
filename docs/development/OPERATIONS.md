# Operations

[Account reauthentication and optional automatic switching](ACCOUNT_AUTOMATION.md) documents the toolbar threshold menu, failure pause, Recover action and bounded startup recovery. Account automation is Off by default; it does not require a vault format or credential migration.

Windows source 1.42.2 adds `--start-hidden` for an ordinary tray-only launch. It initializes the normal utility without showing/activating a window; a duplicate hidden launch does not activate an existing window. Use it to restart the utility after an authorized local installation update while leaving Codex and user focus untouched. Opening the app through its tray/normal shortcut still shows the window. The option rejects diagnostic/account-action combinations. Startup/background errors remain available in status or redirected stderr without a forced popup.

A tray-only launch writes private `running-instance.json` in the existing data directory after startup: PID, loaded assembly version, readiness, account count and automation/window state. It contains no credentials. Match its PID to the running executable and package hashes; a stale receipt alone is not activation evidence.

The session monitor treats an absent Codex home as retryable unavailability before IPC/watcher setup. This avoids an invalid watcher fault preventing normal utility shutdown when initialization cannot yet complete. The private tray receipt also records shutdown stages for diagnosis.

Self-contained builds explicitly pin reviewed runtime10.0.13 in `Directory.Build.props`. An SDK patch can otherwise select a different runtime; the complete-package verifier must still pass without changing its expected runtime.

Issue105/source1.41.2 exposes the Windows running assembly version beside the titlebar name. Tray close retains that process; use tray Exit and a normal launch when applying an installation replacement. The existing disposable hosted `--verify-ui` mode checks eight caption/version/action-bound combinations and exports only own-app fictional evidence. [Contract](RUNNING_VERSION.md). Do not stop the owner's app or publish new binaries as part of this UI-only checkpoint.

Release1.41.1 publishes seven reviewed Windows/Linux packages with exact-head six-job native CI, immutable package provenance, unchanged protection, author self-review and merged-tree equality. [Publication gates](RELEASE_1_41_1.md) require draft asset SHA256/size checks and public download verification. Keep capture source distinct from final candidate source; preserve existing releases and macOS1.40.2. Owner data/installations are outside release verification.

Issue99 Windows warning verification uses the existing isolated native `--verify-ui` mode and fictional account/disk inputs. The current host captures actual failed/healthy/recovered card controls, tooltip HWNDs and footer bounds; a pinned external source archive receives diagnostic-only hosting for the same-input baseline while original production source hashes stay unchanged. Never run these visible captures on the owner's gaming desktop or publish the attached personal screenshot. The full verified Windows package can be staged for the next ordinary restart; preserve current process/sensor lifetime and original shortcut arguments.

Issue97's [terminal delivery contract](LINUX_TERMINAL.md) requires early headless dispatch, the existing encrypted vault and explicit verified native CLI, serialized account mutation and exact saved-session resumption. Native x64/ARM64 and owned PTY evidence must distinguish offline policy/lifecycle checks from real authenticated continuation. Existing owner accounts/keyring and published binaries remain preserved.

For issue95 framed account pictures, `CODEX_SWITCH_CAPTURE_WM_FRAMES=1` applies only within the existing explicit Linux `--verify-ui` path and requires `CODEX_SWITCH_DISPOSABLE_RUNNER=true`. The installed app verifies `_NET_WM_PID`, direct-parent frame/child membership, non-root capture and actual Xfwm4 before dumping that original frame. It preserves normal native client captures and exports a separate `wm-report.json`. The owned Xfce harness requires twelve framed language/theme/size captures, exports both English800×600 themes with `framed-accounts.json`, and verifies original vault bytes/empty auth. No normal application action, real login, owner desktop or frame synthesis is involved.

Release1.40.7 explicitly authorizes seven Windows/Linux packages, sanitized verification/evidence attachments and all-language README links. Use the existing six protected native checks, then inspect exact-head artifact digests/source/runtime/packages and publish a verified draft manually as described in [the release checkpoint](RELEASE_1_40_7.md). macOS1.40.2 assets/checksums remain unchanged. CI stays read-only and owner account/Linux environments are not used. Earlier no-new-Release entries are historical for their own PRs.

Issue89/source1.40.7: CPU setup is available directly beside unavailable CPU Celsius. The first missing-driver action restarts only the verified, account-free normal worker and awaits a fresh packet before choosing install versus session authorization. This discovers externally installed drivers without an app restart and avoids reinstalling from a stale ProviderMissing result. The account app remains unprivileged; download/UAC still require explicit user confirmation. Installer3010 is a restart notice, never an automatic reboot. Native UI checks verify visible/hidden/busy/restored accessible controls and compact geometry, with no owner data or local GUI in CI.

Issue87/source1.40.6: `WindowsDiskIoSampler` uses native logical-volume PDH counters every2seconds; `DriveMonitor` retains15second cached space/temperature. `WindowsDiskCapabilities` resolves extents and bounded device seek/bus properties, refreshed once a minute. No throughput benchmark, device writes or counter-policy change. `tests/hardware` adds scale/latency/profile/missing/stale contracts and an actual read-only native counter sample; all9×400 catalogues explain the source/policy. Published-app native Windows evidence adds dark/light normal/pressure/unknown/compact cases with fictional inputs. Real counters and fictional rendering are distinct from device benchmarks. [Policy](../DISK_IO.md).

Issue85/source1.40.5: `scripts/build.ps1` also publishes the account-free NativeAOT CPU worker with corresponding module source/license in `sensors/`. `python scripts/verify-cpu-assets.py` checks its two exact reviewed binaries; temperature contracts check register validity and bounded fresh framing. `tests/sensors` verifies actual malformed launches, PID-bound current-user output pipe, fresh status and exit on disconnect. Only disposable hosted Windows CI runs `scripts/verify-cpu-driver.ps1`: fixed installer hash/Authenticode, running driver, signed-module acceptance/tampered-module rejection and real worker lifecycle. It refuses personal-machine invocation; never bypass kernel policy if installation fails. Native UI confirmation uses fictional states, not physical CPU acceptance. No local GUI/UAC, owner data operation or binary Release is included.

Source1.40.4 dependency work follows [issue83's review](DEPENDENCY_REVIEW.md). Avalonia.Desktop/Fluent are grouped and upgraded together. Main protection enforces PRs, the six existing named GitHub Actions checks with strict freshness and resolved conversations for admins too; force push/deletion are forbidden and independent approval count is0. Preserve bot head ancestry using a reviewed integration merge commit, then verify all original PR merged states. No new Release or owner environment startup is included.

PR65's shared history verification uses the published native Mac app's `--verify-history` mode with disposable encrypted storage, fictional accounts and guarded diagnostic Keychain cleanup. `scripts/capture-history-baseline.py` builds an external pinned main archive with a capture-only host and the same fictional input factory, preserving SHA256 of the original history/chart/theme sources. Both actual before/after windows are captured on each native Mac worker; the ordinary owner environment is not started. Keep raw outputs ignored, validate the complete English image sets/metadata/hashes and label source/runtime/architecture. These diagnostics do not establish real-account switching acceptance.

Release1.40.2 uses [the patch release checklist](RELEASE_1_40_2.md), retaining [the completed1.40.1 publication receipt](RELEASE_1_40_1.md): native Windows/Mac build/test/package artifacts, Linux three-format previews, exact-source receipt/checksum review, inspected actual app images, author review/expected-SHA merge and manually verified draft-to-public upload. CI remains read-only and receives no owner credentials. MacBook real-device screenshot remains an owner-provided follow-up; reserve its gallery slot and mention this explicitly in the documentation commit message.

Public `Gaebabi/codex-switch`, baseline `main`, is the canonical source. Clone into any directory. Source/app version is **1.42.2**; [latest published binaries](https://github.com/Gaebabi/codex-switch/releases/latest) and [versioned downloads/verification](../RELEASES.md) provide complete Windows/macOS ZIPs, unsigned Linux preview DEB/RPM/tar.gz packages, `SHA256SUMS.txt` and source-bound `VERIFICATION.json`. No automatic updater or CI release publisher exists. [Linux packaging](LINUX_PACKAGES.md) remains an integration preview with `supportedAccountHandoff: false`; publication does not complete authenticated active-chat/continuation acceptance.

Issue81 moves the shared desktop clock into the normal window lifetime so a failed initial keyring/client load cannot prevent all scheduled retries. The existing60-second schedule calls the same guarded initialization/refresh path as the ordinary Refresh action. Synthetic capture windows do not start that clock; account controls remain gated until actual initialization succeeds. The native ordinary-session test uses an actual locked/unlocked disposable keyring and no app input to verify recovery.

## Build and verify

The Linux source1.40.0 preview enables ordinary account/usage and [managed desktop integration](LINUX.md#source1400-ordinary-integration--issue74--pr75). Start Codex from this utility before working; an external/unmanaged client cannot be adopted. Existing credential/context policy is respected. Explicit checksum CLI-only mode has no desktop resumption. `scripts/run-local-linux-desktop.py` exercises the production backend plus fictional auth/offline native client in an owned container; authenticated two-account continuation and full desktop/Release acceptance remain separate.

Run from the repository root with .NET10.0.4xx, currently SDK10.0.402/runtime10.0.13. `global.json` allows current patch updates. Use clean output folders for package candidates. .NET10's reviewed current Mac support baseline is macOS15+; the bundle minimum and app version match source1.40.2. [KDE6 provider contracts and native gates](KDE_SECRET_SERVICE.md) describe explicit package/checksum support; runtime providers are not installed or altered by the app. [Linux IPC](LINUX_IPC.md) records controlled launch, native peer trust, kernel/display/container requirements and the remaining lifecycle/account gates. [Local desktop-session verification](LINUX_DESKTOP_SESSION.md) exercises ordinary installed UI and actual provider/client lifecycles without host homes or credentials.

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
