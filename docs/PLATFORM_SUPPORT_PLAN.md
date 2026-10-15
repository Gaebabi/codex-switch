# Platform support roadmap

Tracking [#1](https://github.com/Gaebabi/codex-switch/issues/1). Updated 2026-10-15. Resumed checkpoints and reviewed native evidence are tracked in [PR27](https://github.com/Gaebabi/codex-switch/pull/27). Account switching and safe automatic continuation are the core product; a window that launches is not evidence of either capability.

Declared native GNOME/KWallet5 storage is verified in PR51/59, [KWallet6/KSecrets in PR61](evidence/linux-kde6/README.md), and [authenticated Linux IPC/controlled startup in merged PR63](evidence/linux-desktop-peer/README.md), all on actual x64/ARM64 with existing key/data preservation. The former PR58 incompatibility refusal is historical; declared KWallet storage is available. Source1.39.0/[PR67](https://github.com/Gaebabi/codex-switch/pull/67) adds controlled normal close and [complete unsigned installation/UI preview candidates](development/LINUX_PACKAGES.md). None of these checkpoints proves authenticated account switching/automatic continuation.

The owner authorizes local VM/container verification before providing disposable accounts. Source1.39.1/[PR69](https://github.com/Gaebabi/codex-switch/pull/69) passes an actual local normal-user Debian13/Xfce session with35 orchestration checks, ordinary installed UI, GNOME encrypted storage and two sandboxed official-client Quit/relaunch cycles in the same home with US/French mappings. [Seven inspected program images and receipts](evidence/linux-desktop-session/README.md) distinguish this userspace session from a booted VM or actual accounts. [P6–P10 delivery gates](development/PLAN.md#multiplatform-delivery-goal--resumed2026-10-15) retain checkpoint pushes, final-head CI and author review/merge. Native SQLite52 and context-aware policy54 are complete. Source1.39.1 and published1.27.6 remain distinct. Parent5/6/7 stay open for ordinary lifecycle/metadata/relaunch/thread-open/handoff integration, authenticated acceptance and broader desktop/package promotion.

Runtime4 and core/provider/application/UI separation3 are complete through reviewed native PR27/28/38/40/42/44/47. Windows/Mac regressions remain mandatory for every Linux checkpoint. Public binaries remain1.27.6 until separately released; unsigned Linux PR/CI candidates are not that supported Release.

The first core part is now merged as [PR28](https://github.com/Gaebabi/codex-switch/pull/28), main18d33a7. [Actual native program before/after evidence](evidence/shared-core/README.md) and [complete-head four-job CI](https://github.com/Gaebabi/codex-switch/actions/runs/38627343669) pass. Issue3 is now complete through PR47; [remaining Linux checkpoints](development/PLAN.md) keep capability/trust gates ahead of account enablement. Historical text privacy coordination is tracked in [29](https://github.com/Gaebabi/codex-switch/issues/29) without publishing its private values.

## Current support

Source1.40.0/issue74/PR75 implements ordinary Linux account/usage, explicit checksum CLI-only mode and managed metadata/shutdown/relaunch/thread-open into the shared encrypted switch/recovery/eligible-Continue coordinator. Offline Docker contracts and own-window screenshots are its scoped verification; real two-account identity/settings/inference/resumption remains pending in6, and full desktop/accessibility/Release promotion remains in7. Earlier source1.39 checkpoint descriptions retain their historical scope.

| Environment | Package | Evidence boundary |
| --- | --- | --- |
| Windows x64 | 1.27.6 self-contained ZIP | 93 internal / 23 isolated SQLite-WAL checks; live client acceptance is separate |
| Mac arm64 / x64 | 1.27.6 self-contained ZIP | Cross-publish/package checks; native login/switch/resume, Intel-device acceptance and notarization remain open |
| Linux x64 / arm64 | Unsigned tar.gz/deb/rpm installation/UI preview candidates | Declared storage/client/policy/SQLite/peer and controlled close; fresh Ubuntu24.04/Debian13/Fedora44 minimal-container install/update/removal/bootstrap UI. Actual account/handoff, full desktop/display/accessibility and supported Release promotion remain pending |
| WSL / remote / headless / CLI-only | No dedicated supported setup | Do not assume desktop restoration or share credentials across homes/OSes |

English stays the shared default. Published 1.27.6 UI supports English/Korean. Source 1.28.0 adds nine UI catalogues and extensible local packs, tracked in [#16](https://github.com/Gaebabi/codex-switch/issues/16) and the [translator guide](LOCALIZATION.md). Contributions in other languages and for other environments are welcome.

## Sequence and completion gates

| Phase / issue | Deliverable | Completion |
| --- | --- | --- |
| 0 — [Foundation #2](https://github.com/Gaebabi/codex-switch/issues/2) | Organized root/source/resources/docs, contributor workflow and evidence/CI | Complete through PR8 |
| 1 — [Core/platform projects #3](https://github.com/Gaebabi/codex-switch/issues/3) | UI-neutral core, independent native services and shared composition | Complete through PR28/38/40/42/44/47 with native regression evidence |
| 1 — [Supported runtime #4](https://github.com/Gaebabi/codex-switch/issues/4) | Reviewed .NET10 LTS migration | Complete through PR27; package/runtime/native regression gates retained |
| 2 — [Linux security/client adapters #5](https://github.com/Gaebabi/codex-switch/issues/5) | Secret Service keys, private storage, verified client discovery and read-only SQLite | Native Linux lock/unlock/restart/tamper/database-preservation evidence |
| 3 — [Linux handoff #6](https://github.com/Gaebabi/codex-switch/issues/6) | Shared Avalonia UI, peer-authenticated IPC, graceful shutdown and continuation | Native disposable-account proof of complete switch/resume/recovery without duplication |
| 4 — [Packages/coverage #7](https://github.com/Gaebabi/codex-switch/issues/7) | Download-ready packages, wider environments and accessible use | Clean-VM install/update/uninstall and declared native capability matrix before promotion |

Runtime work can proceed alongside extraction. Linux credential/client trust precedes desktop handoff. Package prototypes may come earlier; stable support claims require native evidence. No delivery date is promised before feasibility gates.

Source1.30.1 targets .NET10 LTS, SDK10.0.402/runtime10.0.13; published1.27.6 packages remain .NET8 until separately released. .NET8 support ends **2026-11-10**; .NET10 LTS is supported through **2028-11-14**. The reviewed current macOS baseline is15+, matching native runners and the bundle minimum. [Microsoft policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [current .NET10 release](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [supported OS versions](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

## Project organization

The foundation and focused core extraction use the following structure while keeping executable/namespace/data/key identities and existing Windows/Mac behavior intact:

```text
src/windows/         Windows WPF application and diagnostics
src/macos/           Mac Avalonia application and native UI integration
src/linux/           capability-gated shared source host and native diagnostics
src/core/            single UI-neutral domain/presentation/catalogue project
src/platforms/security/ native key/protection/private-file assembly
src/platforms/client/ native desktop discovery/lifecycle/peer trust assembly
src/platforms/protocol/ Codex RPC/IPC/session/queue services
src/platforms/resources/ Windows/Mac resource, drive and temperature services
src/platforms/sqlite/ independent native read-only database assembly
src/application/     single account/storage/switch integration assembly
src/desktop/         reusable Avalonia views/themes/resources and host contracts
assets/              canonical brand and Mac icons
resources/           shared visual tokens/icons
localization/        nine UI catalogues, schema and translator guide
licenses/            third-party asset notices
scripts/             build, verify, render and package entry points
docs/development/    plan, status, operations and contributor evidence conventions
docs/evidence/       sanitized issue/PR screenshots
```

Completed #3 extracts a platform-free core project, independent platform services, shared Avalonia desktop composition and conventional contract/integration test projects. The core owns decisions/checkpoints; OS services own credential protection, client lifecycle, IPC trust, SQLite loading and resource sampling. Select unsupported platforms explicitly instead of falling through Windows paths. Move responsibilities in focused PRs, separate from mass formatting/runtime upgrades.

## Current Linux blockers

| Source | Required change |
| --- | --- |
| `src/platforms/client/RuntimeLocator.cs`, `DesktopShutdown.cs` and Linux session adapters | Integrate managed native close/relaunch/thread-open with ordinary handoff, retaining original client/child identity and actual effective desktop/context policy |
| `src/platforms/security/LinuxVaultProtection.cs` | Declared GNOME/KWallet5/KWallet6/KSecrets native storage is complete; broader provider builds need explicit attestation, not a guessed compatible version |
| `src/platforms/sqlite/ReadOnlySqlite.cs` | Native read-only SQLite/WAL26 is complete in merged PR53; retain actual installed-client/session acceptance separately |
| `src/platforms/protocol/DesktopSessionMonitor.cs` | Verified native Linux IPC is complete in merged PR63; actual chat ownership/settings/one-Continue/recovery acceptance remains in6 |
| `src/desktop`, `src/linux` | Source host/UI is implemented; account/client/handoff remain gated until trusted providers and complete native acceptance pass |
| Hardware/drive collectors and defaults | Implement Linux capabilities/mount selection; unsupported readings remain unknown |

The upstream Linux ChatGPT desktop preview includes Codex and uses a `chatgpt` launcher. Its availability does not prove this switcher's auth/IPC/chat compatibility. Inspect the installed client before enabling actions. [Official Linux documentation](https://learn.chatgpt.com/docs/linux/linux-app)

## Environment expansion

| Stage | Candidates | Gate |
| --- | --- | --- |
| First beta | Ubuntu 24.04/26.04 LTS x64, GNOME, X11/XWayland | Vault, account reads, reachable UI and full handoff |
| Wider beta | Debian 13 KDE; Fedora 43/44 GNOME/KDE | Provider/native dependency/install/handoff differences |
| Architecture | Native arm64 environments | Actual runtime/native acceptance, beyond cross-compilation |
| Community | Current Arch and other distributions/window managers | Sanitized reproducible reports with declared limits |
| Separate experiments | Native Wayland, WSL/headless/CLI-only, AppImage/Flatpak | Focus/deep-link/keyring/host-client boundaries and distinct capabilities |

Current Avalonia is **11.3.22**. Begin with X11/XWayland. Avalonia 12.1's native Wayland backend is experimental and needs explicit setup; upgrade/regression is a separate gate. Do not call XWayland evidence native Wayland acceptance. [Avalonia release details](https://avaloniaui.net/blog/release-12-1)

CLI-only mode advertises verified account/usage operations; it must not claim desktop chat restoration or automatically resume existing CLI processes. Shared login affects CLI/IDE clients, so ownership/quiescence must be checked before changes.

## Security and release gates

- Linux vaults: AES-GCM with an OS-held key via configured Secret Service. Test GNOME Keyring and KDE/KWallet independently. Missing/locked services never trigger plaintext, silent key replacement or vault loss; lookup attributes contain no account email. An alternative password/session-only mode needs its own review. [Secret Service specification](https://specifications.freedesktop.org/secret-service/latest/)
- Storage: same-user ownership, 0700/0600, atomic writes, symlink/collision checks; test corrupt ciphertext, missing keys, interrupted writes and credential revisions. Preserve user data on install/update/uninstall and other-platform vaults.
- Client trust: installed executable/PID lifetime, same-UID socket peer/owner and exact intended-client graceful shutdown. Reject unknown peers; no unrelated process kills, timeout-as-success or credential-backend rewrites.
- Handoff: verified chat-name save/login backup, correct target identity and identical chat IDs/folders/models/permissions. One Continue for eligible idle unfinished work; no duplicate running/completed/uncertain sends. Encrypted recovery/cancel remains restart-safe.
- Observation: no database checkpoints/repair/writes. Protocol/schema failure reports unavailable capabilities rather than zero or success.
- CI: untrusted PRs use disposable hosted runners, read-only tokens and SHA-pinned actions; no signing/account secrets, privileged fork events or release publication. Future trusted-tag/protected release builds add reviewed dependency locks, SBOM/licenses and provenance. Checksums do not establish publisher authenticity; bit-for-bit reproducibility is a later measured claim.
- Privacy: never publish raw captures, credentials, private chats/database/logs. Issue screenshots remove original private pixels before blur. Targeted scans and GitHub secret scanning/push protection/private reporting complement each other without guaranteeing detection of every secret.

Begin delivery with full self-contained tar.gz and per-user desktop/icon integration; add tested `.deb`/`.rpm`, then separately evaluate AppImage/Flatpak. Declare minimum dependencies and native architecture coverage. Keep Windows/Mac regressions; Mac Developer ID/notarization and Windows arm64 have separate native release gates. WSL must not auto-copy credentials between homes/OSes. Cloud sync remains a separate [proposal](ACCOUNT_SYNC_REVIEW.md).

Test keyboard-only/screen-reader/high-contrast use, fractional scaling, Unicode names/paths, date/time conventions, offline behavior and missing tray/keyring providers. No forced hooks, real names, paid accounts or Windows-only requirements for contributors. Accept docs, translations, packaging and tester participation. [Contributor guide](../CONTRIBUTING.md)

## Evidence and support labels

Record source/package checksum, client version, OS/CPU, desktop/display protocol, credential provider and exercised capabilities. Keep raw evidence private. Separate source/config checks, architecture packages, isolated native vault/IPC/database/UI checks, authorized disposable-account switching/resumption, and clean-machine lifecycle acceptance. The first three cannot substitute for the last two.

Every issue has an application baseline/problem screenshot; change PRs add application before/after, new features add native application confirmation. Repository screenshots do not substitute. Planned issues explicitly mark implementation evidence pending. [Evidence conventions](development/ISSUES.md). #1 remains open until declared Linux capabilities are natively verified and downloadable.
