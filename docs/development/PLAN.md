# Current plan

## Resource and disk temperatures — issue #18

- [x] Add right-aligned Celsius labels to the four resource gauges and disk gauges without increasing footer height.
- [x] Read only supported Windows GPU/storage APIs and an optional existing WMI sensor provider; map values to the actual adapter/backing disks.
- [x] Keep unsupported/denied/malformed readings explicit, with independent VRAM sensors and macOS availability limits; install no driver or elevated service.
- [x] Update nine packs to 349 messages; pass 24 temperature and 39 language checks, read actual non-elevated GPU/storage values, verify nine minimum-size language layouts and inspect before/after/native-resource screenshots. Windows 93/93 and SQLite/WAL 23/23 pass; both Mac cross-publishes compile. Native Mac GUI/WMI-provider acceptance remains separate.
- [ ] Open a formal PR with issue evidence, review final changes, pass exact-head CI and perform the previously authorized merge. No new binary Release or live account/switch action.

Target source version: 1.29.0. Published downloads remain 1.27.6. The selected sensor source and any missing coverage are described in tooltips/documentation.

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
