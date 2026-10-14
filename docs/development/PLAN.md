# Current plan

## Resumed platform roadmap — issues #1 / #3–#7

- [x] Inventory current issues and open dependency PRs; #3–#7 remain open and no Linux account/handoff support is claimed.
- [x] Checkpoint P1: add isolated native published-app screenshot modes and sanitized CI artifact validation for Windows/Mac. Local Windows HWND captures and existing93 checks pass; native Mac CI remains a gate.
- [x] Checkpoint P2: migrate all projects/scripts/CI to .NET10 LTS (SDK10.0.402/runtime10.0.13), including System.Management10.0.13 and Mac supported-OS/bundle metadata. Local Windows93, pack39/temperature29 and Mac publish pass; full runtime/package inspection and native CI remain promotion gates. Manage PR20 only after reviewed replacement.
- [ ] Checkpoint P3: attach inspected native OS before/after images, complete exact-head regression/review and merge the scoped runtime PR. Close only issue4 when its criteria pass.
- [x] Checkpoint P2.2: correct the Mac minimum-window content budget exposed by native CI; preserve126px row minimum and strict no-clipping/no-scroll checks. Align current installation/developer guides with .NET10 and disclose software rendering in exported Mac provenance. Native confirmation remains required.
- [ ] Continue core/platform extraction (#3), then Linux credential/client trust (#5), handoff (#6) and package/environment acceptance (#7), each with separate checkpoints and target-OS evidence.

Every checkpoint receives a scoped commit/push. The PR stays draft until target-OS app screenshots are validated and privacy checked. Personal desktops/vaults are not captured or used. Public binary Release/signing and full Linux account support are separate gates.

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
