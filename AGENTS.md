# Repository instructions

## Unreleased model provider gateway

- Model provider keys and connection recovery are stored separately in model-providers.vault using the existing protection identity. Preserve this file, the account vault and the original Codex sign-in.
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
