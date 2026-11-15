# Release 1.45.1

This maintenance release adds colored folder allocation segments to the Windows drive gauge. Register folder paths in drive settings to see their share of used space; hover shows the path, allocated size and percentage, and clicking opens the exact folder in Explorer. Mapped network drives, long paths, sparse/compressed files and hard-link deduplication are included. Missing and partial scans remain explicit. [Storage contract](../FOLDER_MONITORING.md).

Subscription dates now come from the selected account's current billing entitlement rather than cached login-token claims. Automatically renewing plans show **Renews**; non-renewing plans show **Expires**. Billing snapshots survive encrypted storage reload and credential refresh. A failed billing query remains visible without blocking quota updates. [Account contract](ACCOUNT_AUTOMATION.md).

The Windows desktop backend decodes JSON-RPC input as strict UTF-8, preserves current deferred-tool definitions and applies per-turn reasoning settings before provider validation. Returning from custom models to OpenAI converts only recorded foreign reasoning and optional tool-item identities through official native compaction, preserving visible history and native reasoning. Configured history directory aliases resolve to their physical roots without rewriting the owner's history. [Backend contract](../DESKTOP_MODEL_BACKEND.md).

## Verification and delivery

The release requires all six protected checks at the reviewed head/current base, author review and an expected-head merge with identical trees. Collect the nine native Windows, Mac and Linux packages from those exact successful workflows, verify their source/runtime manifests, and publish checksums plus provenance. Verify uploaded asset digests and anonymous downloads before announcing completion. Existing releases remain intact.

Local host/VM acceptance confirmed current billing data, unchanged account/provider identity and existing Codex processes. Folder hover/click contracts and allocated-size regressions passed. Public CI uses fictional accounts and application evidence only. Windows backend updates take effect at the next ordinary Codex launch; release publication must not restart an owner's active client. macOS/Linux retain their existing platform limits. Windows/Linux are unsigned; Mac bundles are ad-hoc signed, not notarized. Linux remains an integration preview.
