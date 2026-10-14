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
