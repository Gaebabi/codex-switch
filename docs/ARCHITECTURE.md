# Development map

## Main parts

`src/core/CodexSwitch.Core.csproj` builds one UI-neutral .NET10 assembly consumed by both desktop apps and contract tests. It owns account/quota/authentication models, usage/resource presentation, refresh scheduling, checkpointed switch policy and language catalogues. It references no WPF, Avalonia, native P/Invoke or platform-service project. The existing `src/shared` integration sources and `src/platforms/macos` remain linked by the apps while platform providers are extracted in later focused parts of [issue3](https://github.com/Gaebabi/codex-switch/issues/3).

The coordinator accepts its platform error formatter explicitly; its `IAccountSwitchBackend` still owns actual client/storage/IPC actions. Language installation accepts an `ILanguagePackWriter`; the existing private-directory/atomic-write adapter preserves file protection before publishing the new in-memory catalogue. Vault configuration/default drives remain in the integration layer. Unsupported vault defaults/discovery now fail before taking a Windows path. `Directory.Build.props` owns the managed version; Mac bundle metadata must match it.

Source1.31.0 adds `src/platforms/security/CodexSwitch.PlatformSecurity.csproj`, independent of the UI and domain core. Both apps and contract tests reference its single internal native implementation. It owns `VaultProtection`, Windows DPAPI, Mac Keychain/CoreFoundation helpers and `SecureFiles` (permissions/atomic writes/owned-runtime cleanup). Existing native algorithm bytes, paths, Keychain service/account hashing, DPAPI description and CADM1 envelope are preserved. `AccountVault`/`VaultData` serialization and refresh/credential revisions remain in integration code. Linux vault protection still rejects explicitly. Other native service projects remain in #3.

Source1.31.1 adds `src/platforms/client/CodexSwitch.DesktopClient.csproj` for existing RuntimeLocator/CodexInstallation, normal desktop shutdown, owned-child lifetime and Mac app discovery/lifecycle/peer verification. It references native security helpers, without a UI or core dependency. RuntimeHome stays in integration code. Supported native algorithms/identities remain unchanged; unsupported discovery/running/launch/thread-open/shutdown rejects before any Windows fallback. Both apps/tests/rendering use the same assembly; no sources remain linked from the former `src/platforms/macos` folder. IPC observation, SQLite and resource-provider extraction are still follow-ups.

| Part | Responsibility |
| --- | --- |
| `AccountService`, `AccountVault` | Account identity, encrypted storage and asynchronous query/login coordination |
| `AccountSwitchCoordinator`, desktop automation | Checkpointed chat naming, normal shutdown, login application and chat resumption |
| `src/core` | UI-neutral models, quota/forecast, refresh/coordinator policy, language/resource presentation |
| `src/platforms/security` | UI/domain-independent native key/protection/private-file assembly; no credential migration |
| `src/platforms/client` | Native client discovery/lifecycle, normal shutdown and Mac peer-identity helper assembly |
| `DesktopSessionMonitor`, queue stores | Read-only local desktop activity and saved/SQLite queue observations |
| `HardwareSampler`, `DriveMonitor` | Independent machine-resource and volume collection |
| `MainWindow`, account/resource panels | Windows WPF UI and shared Burn0 theme |
| `src/macos/`, `src/platforms/macos/` | Avalonia UI, Keychain/POSIX/Unix IPC and native Mac integrations |

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

Core assembly/resource identity and platform write-failure preservation are checked by the production language API tests. Windows/Mac native screenshot CI still runs the published app after extraction; a successful portable core build does not establish Linux account support. Key provider, lifecycle/discovery, peer-authenticated IPC, SQLite and resource-provider project boundaries remain follow-ups.

[Native program before/after and core contracts](evidence/shared-core/README.md) record the first extraction checkpoint. Complete package inspection also requires all declared project runtime DLLs, including the core; a folder containing only the app/runtime is rejected.

`scripts/build.ps1 -Verify` runs Windows isolated checks. `scripts/verify-queues.py` uses disposable SQLite/WAL fixtures. Mac checks run through `scripts/verify-macos.sh` on a Mac. Do not stop the user's Codex or switch a real account as a test.

`python scripts/render-demo.py` renders one fictional Windows screenshot using the current UI sources. It uses a temporary synthetic vault, never starts the native Codex, and does not observe or inject live tasks.

See [operations](development/OPERATIONS.md) for package replacement and [account-sync review](ACCOUNT_SYNC_REVIEW.md) for the separately proposed cloud feature.
