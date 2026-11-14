# Release 1.45.0

This release delivers live Windows Codex model integration and native encrypted-history recovery. Existing OpenAI models and registered external models share the same native menu. An optional Codex-owned backend keeps OpenAI HTTP direct and remains independent of the Switch editor. After its initial setup/restart, registration, visibility, selection and editor stop/start need no additional Codex restart. [Setup and architecture](../DESKTOP_MODEL_BACKEND.md).

Native compaction keeps the original upstream encrypted result. Recovery converts older Switch checkpoints through official native compaction without editing chat databases or old rollout bytes. Custom catalogs use native code-mode execution and deferred tool discovery to retain available tools without expanding every connected app schema into the initial request. Concurrent protected-store writers preserve the latest provider/history state.

Windows model settings also expose context size and automatic compaction thresholds: shared defaults for native OpenAI models, separate overrides for each registered model, and empty fields to restore defaults. Changes apply to the next request of an existing conversation without restarting either process or interrupting an active response. Native model capacity and safety margins remain authoritative. The running backend must include the new context-setting support; the editor identifies an older backend instead of claiming live application.

## Platform and upgrade scope

The live desktop backend is Windows x64 only and requires explicit configuration with the original Codex executable and existing data directories. Existing installations that already enabled it retain their current backend until their next ordinary Codex launch; replacing the Switch editor must not restart the owner client. A fresh ZIP does not silently change the desktop launch override. macOS Apple Silicon/Intel and Linux x64/ARM64 retain the shared provider/history fixes and their existing platform capabilities; a unified native picker is not claimed for those platforms.

Account switching still uses the existing restart/recovery coordinator. Native model changes and account switches are different operations. An already open model menu may need closing/reopening to display a refreshed list. Remote inference can take minutes or fail; no context-limit inflation, fabricated stream progress or ambiguous request replay hides those failures.

## Verification and publication

Before public promotion, require all six protected checks on the final head/current base, recorded author review and a normal expected-head merge with identical reviewed/merged trees. CI builds and verifies complete native Windows, both Mac and both Linux packages. Windows uses the separately locked official0.167.0-alpha.4.1 desktop CLI and tests the packaged backend; stable0.167.0 has a different tool contract and is refused before enabling live integration. The reviewed Linux standalone prerequisite stays0.164.0. Package inspection requires the backend runtime/dependencies and refuses any owner bootstrap configuration.

Prior local owner acceptance confirms the same native menu, live visibility, Switch-off native execution, all seven registered custom inference routes and real custom command execution. It preserves configuration, sign-in and existing provider/account identities. Local owner checks are separate from final-head CI and release asset provenance. [Redacted native menu evidence](../evidence/live-model-backend/README.md).

Publish a draft against the reviewed merged tree, collect the nine native packages and sanitized verification evidence, bind source/runtime/size/SHA256 in `VERIFICATION.json` and `SHA256SUMS.txt`, verify uploaded digests, then publish and verify anonymous downloads. Preserve existing releases. Windows/Linux remain unsigned; Mac bundles are ad-hoc signed and not Apple-notarized. Linux remains an integration preview with `supportedAccountHandoff:false`.

The [GitHub release](https://github.com/Gaebabi/codex-switch/releases/tag/v1.45.0), its verification manifest and completed public download checks are the publication record. A source checkout or CI candidate alone does not establish publication.
