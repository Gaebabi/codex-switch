# Repository instructions

- Public `Gaebabi/codex-switch` is the canonical source, baseline branch `main`. Use focused issue-linked branches/PRs. The former codex-account-desk repository is historical recovery material.
- Read `README.md`, `CONTRIBUTING.md`, `docs/development/OPERATIONS.md`, `docs/development/PLAN.md`, `docs/development/STATUS.md` and relevant architecture/release guides. Respect closer instructions and the user's explicit scope.
- Edit sources and Korean documents with `apply_patch` or equivalent UTF-8-safe patches. PowerShell string replacement/save can corrupt encoding.
- Fix root causes; do not swallow failures, fabricate observations, relax trust checks or change fixtures to force the expected result.
- Never commit authentication, tokens, account stores, databases, raw personal captures/diagnostics or build output. Run scoped repository/privacy checks before pushing.
- Do not stop the user's live Codex or switch their real account for verification. Separate source/build/isolated checks from native account/UI/resumption evidence.
- Preserve data directories, vault/key identities, bundle ID and IPC names. Source cleanup does not authorize credential migration.
- Keep development plan/status/operations and public guides consistent when paths/build/CI change. Local/private operating context belongs outside published documents.
- Every issue/PR needs actual application verification screenshots: before/after for changed behavior and a confirmation screen for a new feature. Repository screenshots do not substitute. Planned work shows the existing app baseline and explicitly pending confirmation. Remove private source pixels before applying blur; captions state the actual evidence scope.
- Create automatic backup commits only after satisfaction is confirmed. A direct PR request authorizes scoped branch commits/push/PR. Merge/release/external messages need explicit scope; the current owner request includes review and merge, not a new binary release.

<!-- CODEXWORK_BUILD_CACHE_POLICY_20261019 -->

## VM build and cache storage

CODEXWORK VM builds use `D:/Workstation/Set-ProjectEnvironment.ps1 -Project codex-switch` and the registered `PROJECT_BUILD_ROOT` / `PROJECT_CACHE_ROOT`. Read `D:/Workstation/BUILD_CACHE_POLICY.md`. Reproducible outputs belong under `W:/Builds/CODEXWORK/codex-switch`; use each tool's real output option. Do not silently create a second output on D: when W: is unavailable. Source, real Git worktrees, credentials, databases, emulator userdata, original assets and final acceptance evidence stay on D:. Keep active Unity Library/Temp and executable virtual environments local for filesystem/locking compatibility; do not move or delete active caches. W: is regenerable storage on the acknowledged HDD; only separately backed-up asset copies belong there. Large build admission requires 40 GiB free on D:; archive verified inactive checkpoints to the reliable HostSourceHistory area first, never discard unique state.

<!-- CODEXWORK_REGISTERED_SHARED_BUILDS_20261019 -->

## Codex shared-storage build execution

Windows elevated Codex sandbox accounts have a separate drive/authentication context: they cannot directly use the interactive user's W: SMB mapping. Do not disable the sandbox, add network drives as native writable roots, or replace missing W: with local D: outputs. Submit registered operations to the ordinary-user build worker:

`D:/Tools/Python311/python.exe D:/Workstation/Invoke-ProjectBuild.py --project <project> --operation <operation>`

Operations: `storage-probe` (harmless write/read/delete); `npm-ci`, `npm-build`, `npm-test` (optional `--relative-path` to the package folder); `dotnet-restore`, `dotnet-build`, `dotnet-test` (`--relative-path` to the repository's csproj/sln); `reborn-windows`, `reborn-android`, `reborn-editmode`, `reborn-audit` for DragonMate-Reborn; `infinity-android` for InfinityCrystal-Unity. Only registered projects/operations are accepted; arbitrary shell commands and paths outside the project are rejected. The worker runs as the normal CodexWork user and receives build/cache routing without unrelated credentials. Build admission requires 40 GiB free on local D:.

The client waits up to 30 seconds, then returns a receipt path while the same build continues. Read that receipt and its existing operation lease; do not start a duplicate. Detailed logs stay in D:/Workstation/private/shared-build-logs. Do not kill unrelated build processes. A routing probe is infrastructure evidence, not product compilation or Player acceptance.

The permanent ordinary-user task is CodexWork-RegisteredSharedBuildWorker, starts at CodexWork logon, and accepts structured requests only under D:/Caches/Temp/CodexSharedBuildQueue. Preserve this active queue during temp cleanup. The only extra native sandbox writable root is this local queue, not W: or historical/private data. Normal interactive terminals may use Set-ProjectEnvironment and the project tools directly. In CodexSandbox accounts the environment loader skips user DPAPI Git credentials and uses this registered build path for shared I/O.
