# Platform support roadmap

Tracking [#1](https://github.com/Gaebabi/codex-switch/issues/1). Updated 2026-10-15. Resumed checkpoints and reviewed native evidence are tracked in [PR27](https://github.com/Gaebabi/codex-switch/pull/27). Account switching and safe automatic continuation are the core product; a window that launches is not evidence of either capability.

Runtime issue4 is completed by merged PR27: source1.30.0/.NET10.0.13, native Windows x64 and macOS15 arm64/Intel app-window evidence, complete runtime/package inspection and four-job reviewed-head CI. Core issue3 starts with a shared UI-neutral project in source1.30.1; native integration-provider boundaries and Linux5/6/7 remain open. Public binaries remain1.27.6 until separately released.

## Current support

| Environment | Package | Evidence boundary |
| --- | --- | --- |
| Windows x64 | 1.27.6 self-contained ZIP | 93 internal / 23 isolated SQLite-WAL checks; live client acceptance is separate |
| Mac arm64 / x64 | 1.27.6 self-contained ZIP | Cross-publish/package checks; native login/switch/resume, Intel-device acceptance and notarization remain open |
| Linux x64 / arm64 | None | No supported executable, credential provider or desktop handoff yet |
| WSL / remote / headless / CLI-only | No dedicated supported setup | Do not assume desktop restoration or share credentials across homes/OSes |

English stays the shared default. Published 1.27.6 UI supports English/Korean. Source 1.28.0 adds nine UI catalogues and extensible local packs, tracked in [#16](https://github.com/Gaebabi/codex-switch/issues/16) and the [translator guide](LOCALIZATION.md). Contributions in other languages and for other environments are welcome.

## Sequence and completion gates

| Phase / issue | Deliverable | Completion |
| --- | --- | --- |
| 0 — [Foundation #2](https://github.com/Gaebabi/codex-switch/issues/2) | Organize root/source/resources/docs, portable contributor workflow, issue evidence/attributes and safe CI | Source bytes/identity preserved; build/render/isolated tests and actual PR checks/review pass |
| 1 — [Core/platform projects #3](https://github.com/Gaebabi/codex-switch/issues/3) | UI-neutral core and explicit OS services beyond the current linked-source layout | Focused behavior-preserving PRs, compatibility and Windows/Mac regression gates |
| 1 — [Supported runtime #4](https://github.com/Gaebabi/codex-switch/issues/4) | Reviewed .NET 10 LTS migration and SDK/dependency maintenance | Full packages/runtime inspection and regression before .NET 8 end of support |
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
src/core/            single UI-neutral domain/presentation/catalogue project
src/shared/          linked integration services and existing platform adapters
src/platforms/macos/ Mac native platform helpers
assets/              canonical brand and Mac icons
resources/           shared visual tokens/icons
localization/        nine UI catalogues, schema and translator guide
licenses/            third-party asset notices
scripts/             build, verify, render and package entry points
docs/development/    plan, status, operations and contributor evidence conventions
docs/evidence/       sanitized issue/PR screenshots
```

#3 subsequently extracts a platform-free core project, Windows/Mac/Linux service projects, shared Avalonia desktop composition and conventional unit/integration test projects. The core owns decisions/checkpoints; OS services own credential protection, client lifecycle, IPC trust, SQLite loading and resource sampling. Select unsupported platforms explicitly instead of falling through Windows paths. Move responsibilities in focused PRs, separate from mass formatting/runtime upgrades.

## Current Linux blockers

| Source | Required change |
| --- | --- |
| `src/shared/Runtime.cs`, `DesktopShutdown.cs` | Replace Mac/Windows-only discovery and lifecycle branches with verified Linux client capabilities and confirmed normal shutdown |
| `src/shared/VaultProtection.cs` | Linux is rejected today; add an OS-held key provider with reviewed encrypted format/migration |
| `src/shared/ReadOnlySqlite.cs` | Resolve Linux SQLite instead of Windows DLL; preserve read-only WAL visibility and database bytes |
| `src/shared/DesktopSessionMonitor.cs` | Unix sockets exist; add Linux same-UID peer/socket ownership/client identity verification |
| `src/macos/Program.cs`, `DeskWindow.cs`, native resources/menu | Share UI by composition; Linux must not load AppKit/Mach or require a tray |
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
