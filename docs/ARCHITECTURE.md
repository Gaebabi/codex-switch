# Development map

## Main parts

Sources are grouped under `src/shared`, `src/windows`, `src/macos` and `src/platforms/macos`. The next step is explicit shared-core/platform projects, tracked in the [roadmap](PLATFORM_SUPPORT_PLAN.md).

| Part | Responsibility |
| --- | --- |
| `AccountService`, `AccountVault` | Account identity, encrypted storage and asynchronous query/login coordination |
| `AccountSwitchCoordinator`, desktop automation | Checkpointed chat naming, normal shutdown, login application and chat resumption |
| `Models`, `UsageAnalysis` | Reported quota, credits/reset counts and usage-history forecasts |
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

`scripts/build.ps1 -Verify` runs Windows isolated checks. `scripts/verify-queues.py` uses disposable SQLite/WAL fixtures. Mac checks run through `scripts/verify-macos.sh` on a Mac. Do not stop the user's Codex or switch a real account as a test.

`python scripts/render-demo.py` renders one fictional Windows screenshot using the current UI sources. It uses a temporary synthetic vault, never starts the native Codex, and does not observe or inject live tasks.

See [operations](development/OPERATIONS.md) for package replacement and [account-sync review](ACCOUNT_SYNC_REVIEW.md) for the separately proposed cloud feature.
