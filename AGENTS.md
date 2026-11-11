# Repository instructions

## Unreleased model provider gateway

- Model provider keys and connection recovery are stored separately in model-providers.vault using the existing protection identity. Preserve this file, the account vault and the original Codex sign-in.
- ProviderEditorSession retains drafts and selection in that same encrypted store. A null draft API key retains the saved credential; an explicitly empty key is invalid. Revealing/copying a loaded key must never authorize sending it to a changed destination. Preserve drafts on provider changes, failed saves and close/reopen.
- Connection/restoration edits use the official version-checked configuration RPC. Model choices route per request; never restart the owner's Codex for verification.
- The gateway runs inside Switch. Treat an active gateway as part of ongoing Codex work when planning utility updates; use an owner-approved idle handoff.
- Provider checks use disposable storage, fictional upstreams and an explicit reviewed CLI. Keep component renders, native app-server contracts, native desktop picker confirmation, real provider acceptance and public release as separate states. [Contract](docs/MODEL_PROVIDERS.md).

## General repository rules

- Public `Gaebabi/codex-switch` is the canonical source, baseline branch `main`. Use focused issue-linked branches/PRs. The former codex-account-desk repository is historical recovery material.
- New public OSS maintainer commits use English subjects in `[n/n English task] English description` format. Use an English PR title and squash subject; preserve published history and original contributor/bot ancestry. Other discussion/document languages remain welcome. Follow `docs/development/MAINTAINING.md` for review, settings verification and recoverable housekeeping.
- Read `README.md`, `CONTRIBUTING.md`, `docs/development/OPERATIONS.md`, `docs/development/PLAN.md`, `docs/development/STATUS.md` and relevant architecture/release guides. Respect closer instructions and the user's explicit scope.
- Edit sources and Korean documents with `apply_patch` or equivalent UTF-8-safe patches. PowerShell string replacement/save can corrupt encoding.
- Fix root causes; do not swallow failures, fabricate observations, relax trust checks or change fixtures to force the expected result.
- Never commit authentication, tokens, account stores, databases, raw personal captures/diagnostics or build output. Run scoped repository/privacy checks before pushing.
- Do not stop the user's live Codex or switch their real account for verification. Separate source/build/isolated checks from native account/UI/resumption evidence.
- Preserve data directories, vault/key identities, bundle ID and IPC names. Source cleanup does not authorize credential migration.
- Keep development plan/status/operations and public guides consistent when paths/build/CI change. Local/private operating context belongs outside published documents.
- Every issue/PR needs actual application verification screenshots: before/after for changed behavior and a confirmation screen for a new feature. Repository screenshots do not substitute. Planned work shows the existing app baseline and explicitly pending confirmation. Remove private source pixels before applying blur; captions state the actual evidence scope.
- Create automatic backup commits only after satisfaction is confirmed. A direct PR request authorizes scoped branch commits/push/PR. Merge/release/external messages need explicit scope. A direct owner release request covers only the named version, platforms and verified publication/installation scope; preserve other releases and active Codex sessions.

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

<!-- W_ONLY_BINARY_OUTPUTS_20261022 -->
## 사용자 지시: 빌드 산출물은 W:만 사용 (2026-10-22)

- APK/AAB/Player/배포 ZIP 및 재생성 dist/bin/obj/publish는 `W:/Builds/CODEXWORK/<project>`에만 생성한다. D:/Deliverables 또는 D:/artifacts로 바이너리를 다시 복사하지 않는다. “최종 증거”/durableRoot를 빌드 복사 예외로 해석하지 않는다.
- `D:/Workstation/BuildOutputPolicy.py --project <project> --output <W: 새 경로>`로 실제 경로와 공유 저장소 쓰기/읽기를 검사한다. W: 실패 시 중단하며 D: 대체는 금지한다. 등록 worker의 npm/.NET 출력 검사도 우회하지 않는다.
- D: 검수 증거는 로그·manifest·해시·원본 화면/영상·장부다. 소스·DB·인증·AVD·설치된 도구·원본 에셋/복구 입력을 빌드 정리로 지우지 않는다. Unity Library/Temp와 OS TEMP는 도구 호환성상 로컬 D:를 유지한다. D: 40GiB 여유 조건은 이 로컬 중간 작업 공간 조건이며 APK 출력 위치가 아니다.
- 자세한 정본은 `D:/Workstation/BUILD_CACHE_POLICY.md`와 `build-cache-routing.json`이다. 과거 D: 산출물은 W: 사본 SHA 검증 후 로컬 실데이터만 제거하고 필요한 옛 경로는 링크로 유지한다. 실행 중 쓰기·추적된 원본·검수 증거 혼합 폴더를 일괄 삭제하지 않는다.
