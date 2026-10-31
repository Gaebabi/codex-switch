# Development map

Release1.43.3 adds ModelProviderManager, ModelProviderStore, ProviderCatalog and ModelGateway in the existing application assembly, with WPF/shared desktop editors. Provider keys and connection recovery use the existing native protection identity in a separate encrypted file. The loopback gateway routes each selected alias using an immutable request snapshot; OpenAI authentication is confined to its OpenAI route. Official config RPCs atomically prepare or restore only owned settings. [Compatibility, history and acceptance](MODEL_PROVIDERS.md).

Source1.41.0 adds `LinuxTerminalCommands` in the UI-neutral Application assembly. The Linux host dispatches `cli` before Avalonia/native display initialization. It composes the existing vault/account/policy/native executable services, serializes with GUI and terminal operations, refuses conflicting CLI clients, and delegates exact CLI/exec session restoration to the descriptor-bound official executable. It uses no shell, permission override, desktop adoption or plaintext credential path. [Terminal contract and current evidence](development/LINUX_TERMINAL.md).

## Main parts

`AutomaticSwitchPolicy` compares fresh matching quota windows independently of the card's selected display. `AccountAutomation` reserves a persisted cooldown before invoking the existing switch coordinator. UI hosts retain login/switch exclusion and pending-plan recovery; the coordinator durably marks shutdown/restart risk and attempts bounded desktop reopening after failure without automatically resending ambiguous Continue. `SubscriptionPresentation` shares official product-family labels and Korean-calendar remaining days. [Contracts and acceptance boundaries](development/ACCOUNT_AUTOMATION.md).

Linux source composes `src/linux` with the shared desktop, verified account/usage policy, native Secret Service/private files, read-only SQLite and the shared encrypted switch/recovery coordinator. A home-scoped managed desktop runtime owns original launch/normal Quit/relaunch/thread-open; CLI-only selection has no desktop resumption. Metadata checks each thread's actual project policy. [Linux contracts](development/LINUX.md) and [offline desktop-session verification](development/LINUX_DESKTOP_SESSION.md) distinguish implementation from pending authenticated two-account/Continue and full desktop/Release acceptance. Hardware resources remain unavailable. Versioned paragraphs below retain their original extraction checkpoints; extraction #3 is complete.

`src/core/CodexSwitch.Core.csproj` builds one UI-neutral .NET10 assembly consumed by both desktop apps and contract tests. It owns account/quota/authentication models, usage/resource presentation, refresh scheduling, checkpointed switch policy and language catalogues. It references no WPF, Avalonia, native P/Invoke or platform-service project. `src/application` owns account/storage/switch integration once; consumers no longer link the former `src/shared` files. Provider/composition work is tracked in [issue3](https://github.com/Gaebabi/codex-switch/issues/3).

The coordinator accepts its platform error formatter explicitly; its `IAccountSwitchBackend` still owns actual client/storage/IPC actions. Language installation accepts an `ILanguagePackWriter`; the existing private-directory/atomic-write adapter preserves file protection before publishing the new in-memory catalogue. Vault configuration/default drives remain in the integration layer. Unsupported vault defaults/discovery now fail before taking a Windows path. `Directory.Build.props` owns the managed version; Mac bundle metadata must match it.

Source1.31.0 added `src/platforms/security/CodexSwitch.PlatformSecurity.csproj`, independent of the UI and domain core. Both apps and contract tests reference its single internal native implementation. It owns `VaultProtection`, Windows DPAPI, Mac Keychain/CoreFoundation helpers and `SecureFiles` (permissions/atomic writes/owned-runtime cleanup). Existing native algorithm bytes, paths, Keychain service/account hashing, DPAPI description and CADM1 envelope are preserved. `AccountVault`/`VaultData` serialization and refresh/credential revisions remain in integration code. Linux now has a separate Secret Service provider with its own [security acceptance limits](development/LINUX.md).

Source1.31.1 adds `src/platforms/client/CodexSwitch.DesktopClient.csproj` for existing RuntimeLocator/CodexInstallation, normal desktop shutdown, owned-child lifetime and Mac app discovery/lifecycle/peer verification. It references native security helpers, without a UI or core dependency. RuntimeHome stays in integration code. Supported native algorithms/identities remain unchanged; unsupported discovery/running/launch/thread-open/shutdown rejects before any Windows fallback. Both apps/tests/rendering use the same assembly; no sources remain linked from the former `src/platforms/macos` folder. IPC observation, SQLite and resource-provider extraction are still follow-ups.

Source1.31.2 adds `src/platforms/protocol` for CodexRpc, desktop IPC/requests, session ledger and saved/server queue projection. It references core/client/SQLite and owns no UI/account storage. RPC client version reads this assembly's shared managed version; client name/title/protocol arguments stay unchanged. `src/platforms/sqlite` owns the native loader/read-only wrapper with no domain/UI dependency; read-only open/query checks and WAL behavior are preserved. Unsupported IPC start/SQLite construction refuse before unverified Unix or Windows-library fallback. Current supported peer/pipe policies are unchanged; Linux trust is still a separate gate.

Source1.31.3 adds `src/platforms/resources/CodexSwitch.Hardware.csproj` for existing Windows/Mac resource sampling, Windows temperature providers, drive monitoring and sensor selection. It references core/security helpers, without a UI/client/protocol dependency. Only the unchanged DrivePreference JSON model moves into core; vault defaults and serialization remain integration code. Supported sampling algorithms/intervals/unknown states remain unchanged. Wrong-OS Windows/Mac monitor starts, samplers and WMI refresh reject before native operations; a valid Windows disk root on another OS reports PlatformUnsupported without Celsius. No Linux sensors, drivers or privilege requests are added. System.Management remains pinned10.0.13 in its new owning project.

Source1.32.0 adds `src/application/CodexSwitch.Application.csproj` and `src/desktop/CodexSwitch.Desktop.csproj`. Application references core/security/client/protocol and has no UI/native imports; its eight former shared files preserve their algorithms and serialization. Desktop owns Avalonia views/themes/visual resources. Its host supplies data/home paths, window title, resources, optional menu factory, command modifier, root-volume label, font/reduced-motion and diagnostics. No concrete Mac menu/appearance/capture or entry-point type is referenced by shared UI. Mac's native adapters and capture/diagnostics stay in `src/macos`; its existing provider values and behavior are preserved. Without a tray, ordinary close exits/disposes instead of hiding an unreachable window. Linux support still requires verified providers and its own host.

| Part | Responsibility |
| --- | --- |
| `AccountService`, `AccountVault` | Account identity, encrypted storage and asynchronous query/login coordination |
| `AccountSwitchCoordinator`, desktop automation | Checkpointed chat naming, normal shutdown, login application and chat resumption |
| `src/core` | UI-neutral models, quota/forecast, refresh/coordinator policy, language/resource presentation |
| `src/application` | Single account/vault/runtime-home/switch/language-write integration assembly |
| `src/desktop` | Shared Avalonia views/themes/resources with explicit host composition and optional tray |
| `src/platforms/security` | UI/domain-independent native key/protection/private-file assembly; no credential migration |
| `src/platforms/client` | Native client discovery/lifecycle, normal shutdown and Mac peer-identity helper assembly |
| `src/platforms/protocol`, `src/platforms/sqlite` | Codex RPC/desktop IPC/session/queue services and independent native read-only database wrapper |
| `src/platforms/resources` | UI-independent Windows/Mac sampling, drives and native temperature providers |
| `src/sensors` | Account-free Windows NativeAOT CPU worker; fixed signed modules/read registers, output-only process-bound IPC |
| `DesktopSessionMonitor`, queue stores | Read-only local desktop activity and saved/SQLite queue observations |
| `HardwareSampler`, `DriveMonitor` | Independent machine-resource and volume collection |
| `MainWindow`, account/resource panels | Windows WPF UI and shared Burn0 theme |
| `src/macos/`, native service projects | Mac entry/menu/appearance/capture/diagnostics host plus shared application/UI/native services |

## Important contracts

- Usage reads call the local Codex app-server. Inactive accounts use isolated homes. Missing data stays unknown rather than becoming zero.
- Background reads stay asynchronous; login triggers an immediate read for that account. Credentials use serialized revisions to avoid overwriting newer authentication.
- Windows vault protection is current-user DPAPI. Mac uses AES-GCM with a Keychain-held key. The existing CodexAccountDesk data paths and IPC identity are retained.
- Desktop counts use observed root-chat state. Queue reads combine saved JSON and committed SQLite/WAL, filter desktop roots and deduplicate client/submission IDs. Observer protocol or schema failure hides unsupported counts.
- CPU/MEM/GPU/VRAM sample every 2 seconds, drives every 15 seconds, account auto-refresh every 60 seconds. Resource warning/critical thresholds are 70/85%, released 5 percentage points lower.
- Windows GPU usage is the busiest aggregated engine; VRAM is the highest supported adapter ratio. Mac driver limitations remain visible as unknown.
- Only the active account animates its water surface. Menus/tooltips share theme components and stable hover targets across updates.
- Depletion is conditional on measured recent usage. [Forecast details](FORECAST.md)

## Verification

Core assembly/resource identity and platform write-failure preservation are checked by the production language API tests. Windows/Mac native screenshot CI still runs the published app after extraction; a successful portable core build does not establish Linux account support. Security/client/protocol/SQLite/resource projects own the native services, while application and desktop projects own integration and reusable views. Linux GNOME vault/private storage and source host are verified; read-only SQLite has its own native checkpoint. Remaining KWallet/client/backend/peer/resource capabilities and native handoff/package acceptance stay separate gates.

[Native program before/after and core contracts](evidence/shared-core/README.md) record the first extraction checkpoint. Complete package inspection also requires all declared project runtime DLLs, including the core; a folder containing only the app/runtime is rejected.

`scripts/build.ps1 -Verify` runs Windows isolated checks. `scripts/verify-queues.py` uses disposable SQLite/WAL fixtures. Mac checks run through `scripts/verify-macos.sh` on a Mac. Do not stop the user's Codex or switch a real account as a test.

`python scripts/render-demo.py` renders one fictional Windows screenshot using the current UI sources. It uses a temporary synthetic vault, never starts the native Codex, and does not observe or inject live tasks.

See [operations](development/OPERATIONS.md) for package replacement and [account-sync review](ACCOUNT_SYNC_REVIEW.md) for the separately proposed cloud feature.
