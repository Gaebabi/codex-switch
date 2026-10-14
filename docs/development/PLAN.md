# Current plan

## Documentation destinations and screenshots — issue33

- [x] Inspect tracked README/developer/evidence file destinations; direct latest-UI links to current evidence and label the old foundation comparison as historical.
- [x] Add heading/reference/HTML-link checks and regression coverage to repository CI; read screenshot source version from the shared version owner.
- [x] Re-render and inspect the current source documentation image, preserve the previous image for comparison and collect fresh native Windows/macOS15 arm64/Intel gallery evidence. Capture checkpoint1a2cc58 passes all four jobs in run38633951637; six native PNGs and the component-render before/after are inspected, metadata stripped and digests checked.
- [ ] Complete scoped privacy/link checks, reviewed-head CI, author review and PR promotion. No binary Release or account action.

## Common scrollbars and theme icons — issue31

- [x] Install one rounded theme-aware scrollbar for WPF windows/popups and Avalonia windows/popups, keeping native scrolling/track behavior. History/settings and future overflow inherit it.
- [x] Replace Light/Dark text with sun/moon vector actions; preserve translated tooltips/accessible names, keyboard operation and existing saved-theme callbacks. Source1.30.2.
- [x] Native isolated Windows dropdown/history overflow scrolling and all eight main-window size/theme/language captures pass; Windows/Mac builds and353-message validation pass. Uniform captures are rejected; Windows diagnostic mode uses software rendering.
- [x] Record actual Windows program before/after and native Mac arm64/Intel menu/history confirmation. Code checkpointa0f875b passes all four jobs in run38631880233; screenshots are inspected and metadata/digests verified.
- [x] Complete latest-head34f96cd CI (run38632498454), author self-review and merge PR32 to main331d735. No real account, personal screen or binary Release.

## Resumed platform roadmap — issues #1 / #3–#7

- [x] Inventory current issues and open dependency PRs; initially3–7 were open, runtime4 is now closed and no Linux account/handoff support is claimed.
- [x] Checkpoint P1: add isolated native published-app screenshot modes and sanitized CI artifact validation for Windows/Mac. Local Windows HWND captures and existing93 checks pass; native Mac CI remains a gate.
- [x] Checkpoint P2: migrate all projects/scripts/CI to .NET10 LTS (SDK10.0.402/runtime10.0.13), including System.Management10.0.13 and Mac supported-OS/bundle metadata. Local Windows93, pack39/temperature29 and Mac publish pass; full runtime/package inspection and native CI remain promotion gates. Manage PR20 only after reviewed replacement.
- [x] Checkpoint P3 evidence: attach inspected native Windows before/after and native Mac arm64/Intel confirmation images, with metadata-free PNGs and exact-source receipts. Run38622544372 passes all four jobs; runtime folders and Mac ZIP bytes/modes are checked.
- [x] P3 promotion: latest-head run38623109273 passed all four jobs, author review recorded, PR27 squash-merged to main de66231 and issue4 closed. Superseded Dependabot20 closed. No new binary Release.
- [x] Checkpoint P2.2: correct the Mac minimum-window content budget exposed by native CI; preserve126px row minimum and strict no-clipping/no-scroll checks. Align current installation/developer guides with .NET10 and disclose software rendering in exported Mac provenance. Native confirmation remains required.
- [ ] Continue core/platform extraction (#3), then Linux credential/client trust (#5), handoff (#6) and package/environment acceptance (#7), each with separate checkpoints and target-OS evidence.

Every checkpoint receives a scoped commit/push. The PR stays draft until target-OS app screenshots are validated and privacy checked. Personal desktops/vaults are not captured or used. Public binary Release/signing and full Linux account support are separate gates.

## Core extraction — issue3, focused first part

- [x] Checkpoint P4: move eleven domain/presentation/catalogue files into one UI-neutral `src/core` project consumed by Windows/Mac/tests/rendering; centralize managed version1.30.1. Keep native integration and vault defaults outside it, inject existing error/private-file-write adapters, and reject unsupported discovery/vault defaults explicitly.
- [x] Local Windows93/SQLite23, core/language41, temperature29 and Windows/Mac compilation pass. Catalogue resource identity and refusal-before-cache-update are verified.
- [x] Checkpoint P5 evidence: inspect and attach native Windows/Mac arm64/Intel before/after program screenshots from source9433b79, all four jobs in run38626725906 pass. Windows's eight PNGs are byte-identical; Mac strict UI checks pass. Missing-core packages are rejected.
- [x] P5 promotion: complete-head572de6d passed all four jobs in run38627343669, author self-review recorded and PR28 merged to main18d33a7. Reviewed/merged trees match and the completed branch is removed. Issue3 remains open for native-provider boundaries.
- [x] Checkpoint P4.1: add bounded own-window capture failure diagnostics after Intel CI's OS screenshot command failed. Windows/Apple Silicon and all Intel non-capture checks passed; no fallback, retries or relaxed assertions are introduced. Failed raw stderr remains private and is never exported with PNGs. Native Intel success is still required.
- [x] Checkpoint P4.2: repeated Intel failures identify the first window's WindowServer image as unavailable. Capture preparation now activates only the app's own window, checks AppKit native visibility with a bounded deadline and displays pending views before the single OS capture. Native state is reported safely on failure; no capture retries/fallbacks or weakened UI assertions. Package inspection also requires all declared project runtime DLLs, including the new core assembly. Native confirmation remains required.
- [ ] Continue explicit key provider, lifecycle/discovery, authenticated IPC, SQLite and hardware adapters before Linux credential/client acceptance5 and handoff6. Unsupported Linux remains disabled; no insecure fallback or release claim.

## Next provider checkpoints

1. Issue3: isolate existing Windows/Mac key/private-file and client lifecycle/discovery adapters, then authenticated IPC, read-only SQLite and hardware providers. Each focused part must preserve identities/async/revisions and pass native before/after evidence before merge.
2. Issue5: enable secure Linux primitives only after explicit providers exist. Native GNOME/KWallet lock/missing-key/restart/tamper/private-XDG/SQLite preservation and verified client/backend discovery are required; no plaintext or Windows fallback.
3. Issue6: add Linux GUI/IPC/shutdown/recovery and prove unchanged chat IDs/models/permissions with one eligible continuation. X11/XWayland and native architecture evidence are separate from a window launch.
4. Issue7: promote declared packages only after clean-VM lifecycle and their advertised capabilities pass. Signing, binary publication, native Wayland and broader architecture/desktop coverage remain separate gates.

Historical privacy coordination29 has its own approval boundary: no public history rewrite/force-push without explicit owner authorization. No private path values or raw evidence are posted.

## Always-on refresh — issue #25

- [x] Remove Windows footer switch/caption and Mac settings toggle; retire the opt-out preference and use fixed60s initialized-app refresh.
- [x] Preserve manual refresh, existing asynchronous/coalescing/switch/shutdown paths, vault identities and translation compatibility.
- [x] Inspect matching before/after, Korean minimum and light program captures; check legacy disabled preference and actual timer callback in disposable storage. Windows93/93, SQLite23/23, Mac compile and nine×353 catalogues pass.

[Issue #25](https://github.com/Gaebabi/codex-switch/issues/25) owns formal PR, final reviewed-head CI/review and owner-authorized merge. Source1.29.3; no binary Release, installed-app replacement or real-account action.

## Account date warnings — issue #23

- [x] Share inclusive future expiry72h/reset48h warning eligibility across Windows/Mac without changing dates, quotas or data identities.
- [x] Update date/countdown colors through existing one-second ticks, including missing quota/date values and normal-color restoration.
- [x] Inspect matching actual-program before/after, Korean minimum and light-theme captures; observe real WPF brushes at boundary/time transitions with fictional inputs. Pass Windows93/93, SQLite23/23, Mac compilation and catalogue verification.

[Issue #23](https://github.com/Gaebabi/codex-switch/issues/23) owns the formal PR, final exact-head CI, author review and owner-authorized merge record. Source1.29.2; no new binary Release, installed-app replacement or real-account action.

## Single-click handoff — issue #21

- [x] Trace the post-close interruption and reproduce the production Windows shutdown boundary using disposable parent/child processes.
- [x] Await actual process exit after successful normal shutdown, bounded to 30 seconds and cancellable; preserve shutdown refusal, identity checks and uncertain-send handling.
- [x] Pass five native Windows disposable-process/coordinator checks, Windows 93/93 and SQLite/WAL 23/23, compile Mac and confirm nine catalogues. Inspect actual WPF application before/after layout; the UI is unchanged and these screenshots do not claim native account acceptance. Run repository/privacy checks before pushing.
- [x] Open [PR #22](https://github.com/Gaebabi/codex-switch/pull/22), pass all four remote jobs for the complete code/configuration head and record the [author self-review](https://github.com/Gaebabi/codex-switch/pull/22#pullrequestreview-5521688270). The owner authorized merge; PR #22 owns final reviewed-head checks and merge outcome. Source 1.29.1; no binary Release or real-account switch test.

## Resource and disk temperatures — issue #18

- [x] Add right-aligned Celsius labels to the four resource gauges and disk gauges without increasing footer height.
- [x] Read only supported Windows GPU/storage APIs and an optional existing WMI sensor provider; map values to the actual adapter/backing disks.
- [x] Keep unsupported/denied/malformed readings explicit, with independent VRAM sensors and macOS availability limits; install no driver or elevated service.
- [x] Update nine packs to 353 messages; pass 29 temperature and 39 language checks, read actual non-elevated GPU/storage values, verify nine minimum-size language layouts and inspect before/after/native-resource screenshots. Windows 93/93 and SQLite/WAL 23/23 pass; both Mac cross-publishes compile. Native Mac GUI/WMI-provider acceptance remains separate.
- [x] Open [PR #19](https://github.com/Gaebabi/codex-switch/pull/19) with application before/after/native-reading evidence, pass all four remote jobs for the complete code/configuration head and record the [author self-review](https://github.com/Gaebabi/codex-switch/pull/19#pullrequestreview-5521254219). The owner authorized merge; PR #19 owns the final reviewed-head checks and merge outcome. No new binary Release or live account/switch action.

Target source version: 1.29.0. Published downloads remain 1.27.6. The selected sensor source and any missing coverage are described in tooltips/documentation.

The owner's follow-up adds normal/caution/high temperature colors using reported GPU/storage limits where available, documented per-component display defaults and a three-degree release margin. Unknown values stay muted. Portable classification/native-unit/multi-device tests are part of the 29-check suite.

## Language packs — issue #16

- [x] Shared versioned JSON packs, stable English-based IDs, strict validation, fallback/culture/plural/RTL behavior.
- [x] Windows and Mac menus, trusted local pack import/restoration, persistent choice and live UI updates.
- [x] Nine complete built-in catalogues and an English-first translator/schema/CI workflow.
- [x] Isolated positive/negative tests, application screenshots, Windows/Mac builds and privacy checks. Remote/native verification limits are recorded in the PR.
- [x] Open [PR #17](https://github.com/Gaebabi/codex-switch/pull/17), pass all four remote jobs for the complete code/configuration head and record the [author self-review](https://github.com/Gaebabi/codex-switch/pull/17#pullrequestreview-5516319818). The owner authorized merge; PR #17 records the final reviewed-head checks and merge outcome. No binary Release is included.

English/Korean preferences, user names, credential/data identities, switching/continuation and labeled KST semantics are preserved. New source version: 1.28.0; published packages remain 1.27.6 until separately released.

## Completed repository foundation

Tracking [#1](https://github.com/Gaebabi/codex-switch/issues/1); foundation [#2](https://github.com/Gaebabi/codex-switch/issues/2), branch `chore/oss-foundation`.

- [x] Inspect platform/security boundaries and create scoped roadmap issues.
- [x] Move tracked sources/resources/operational guides out of the root; preserve original source bytes and English filenames.
- [x] Update project/build/render/localization/icon paths while preserving runtime/vault identity.
- [x] Add contribution forms, editing/SDK baseline, minimal issue metadata and read-only CI.
- [x] Verify Windows internal 93/93, SQLite 23/23, both Mac cross-publishes and before/after application component rendering.
- [x] Finish repository/privacy checks and attach application screenshot evidence to every issue; distinguish planned implementation from completed verification.
- [x] Open [PR #8](https://github.com/Gaebabi/codex-switch/pull/8) and fix the Windows CI prerequisite: install the locked official CLI instead of skipping account checks or changing application algorithms.
- [x] Confirm the code/configuration head passes all four remote jobs and record the [self-review](https://github.com/Gaebabi/codex-switch/pull/8#pullrequestreview-5515049754). The owner authorized merge; [PR #8](https://github.com/Gaebabi/codex-switch/pull/8) is the authority for the final reviewed-head CI and merge outcome.

Follow-ups: separate core/platform projects, supported .NET LTS, Linux security/client adapters, native switching/continuation and verified packages/environment coverage. [Roadmap](../PLATFORM_SUPPORT_PLAN.md)

This foundation does not implement Linux or publish a new binary version. Completed release/media history remains in Git.
