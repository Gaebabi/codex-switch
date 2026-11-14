# Native desktop model integration

The Windows desktop backend combines the original native `model/list` response with the registered fixed custom aliases. OpenAI inference still goes directly from the original Codex engine to its configured OpenAI destination. The Switch window is an editor for registrations and visibility; neither native requests nor the desktop backend lifetime depends on that window staying open.

This supersedes the separate-terminal-only restriction for the native picker. The old combined HTTP profile remains retired: never install a root `openai_base_url`, root provider, or root catalog override for this integration. The separate V2 terminal profile remains available as an independent legacy workflow.

## Components and installation

The Windows package includes `backend/CodexSwitch.Backend.exe`. Configure it with `--configure <exact Switch data directory> <Codex home> <original Codex executable>`. Configuration requires the reviewed native CLI `0.167.0-alpha.4.1` and validates its generated catalog in a credential-free home before enabling integration. Stable `0.167.0` has a different native tool contract and is not interchangeable with this desktop build. Incompatible engines are refused before changing the catalog or protected settings. The exact original data directory matters for packaged-parent installations; do not substitute an empty default LocalAppData store.

The desktop's supported `CODEX_CLI_PATH` launch override selects the backend executable. Record and preserve any prior override when installing. One initial normal same-account Codex restart activates the new executable. Subsequent model registration, selection, visibility changes, deletion, and Switch window restarts require no Codex restart. Restore only the recorded owned launch override when uninstalling; preserve native configuration, authentication and chat files.

For a fresh Windows installation, extract and keep the complete versioned ZIP in a writable user-owned directory. Close the Switch editor normally before configuration. Use its existing `--data-dir` and `--codex-home` arguments to identify the actual stores; the default LocalAppData path can differ from a packaged parent's active directory. Select the original native `codex.exe`, never another adapter. Run:

```powershell
& '<installation>\backend\CodexSwitch.Backend.exe' --configure '<existing Switch data directory>' '<existing Codex home>' '<original codex.exe>'
```

After successful validation, record the existing user/process `CODEX_CLI_PATH` values privately and set that user environment variable to the absolute `backend\CodexSwitch.Backend.exe` path. Launch Switch with the same data/home arguments and that environment value, then perform the one initial normal same-account Codex restart when its work is idle. Verify the combined menu before using a custom model. Do not edit root `openai_base_url`, credentials or history. On later upgrades keep the old installation available while its backend is running, configure the new version and update only the owned launch override for the next ordinary launch. The running backend receives subsequent catalog changes without replacement.

The adapter owns an original native app-server child for the lifetime of Codex. It relays native requests, server requests, responses and notifications, and negotiates the experimental APIs needed for scoped conversation settings. The child is in an owned Windows process job so an interrupted adapter cannot leave an inference process behind. The custom-only HTTP gateway starts lazily inside this backend, never as a machine service or a global OpenAI proxy.

## Live model lists

Switch validates custom metadata through the actual native catalog parser, writes an immutable catalog revision, then atomically publishes `desktop-backend-catalog.json`. This file contains model metadata and paths, never provider credentials. Normal OpenAI requests do not read the provider vault. Model-list pagination uses bounded snapshots and preserves native entries, defaults and metadata. Only custom entries are appended; their `isDefault` flag is false.

The existing desktop IPC model-query invalidation refreshes the native picker. A currently mounted menu can retain its rendered snapshot until it is reopened; reopening the menu is not a client restart. `desktop-backend-observed.json` acknowledges a revision only after the last page of that list has been written to the client. A saved file alone is not a confirmed desktop refresh. Labels include both upstream model and provider name so identical upstream IDs remain distinguishable. Hidden aliases remain routable for existing chats. Removed aliases fail explicitly and are never redirected to a different provider.

Desktop custom catalogs declare `tool_mode="code_mode_only"` and `supports_search_tool=true`. These are native tool presentation/discovery capabilities: the native engine exposes code execution and discovers deferred app tools as needed. Both fields are necessary; enabling a general code-mode feature alone still expands account-enabled app schemas into the request. A synthetic request through the actual native engine measured about 497 KB before and 46 KB after this correction. Tools and the provider's real context limit are retained. Native model metadata is untouched. Verify the actual request protocol and real tool execution, rather than disabling tools or enlarging the advertised context to hide the failure.

## Conversation routing and history

Custom conversations use the original `openai` provider identity with thread-scoped `openai_base_url` and `model_catalog_json` overrides. Native conversations use their original configuration. A model name alone cannot change a running native session's endpoint: the adapter unsubscribes the idle conversation, waits for confirmed unload, then resumes the same ID with its new scoped configuration and restores its settings. The native process is not restarted. Requests are serialized per conversation so unrelated native chats remain available during a handoff; active turns keep their existing destination.

Ephemeral sessions have no durable resume contract, so a connection handoff is refused before unloading them. Start such sessions with the intended model. Ordinary desktop chats retain their original IDs and history.

Before a custom conversation returns to native OpenAI, the existing native-history recovery preflights any Switch-owned checkpoint, verifies its exact account/conversation/segment binding and source summary, and performs official native compaction. It requires durable native replacement before allowing ordinary native inference. Neither SQLite nor rollout bytes are edited. Missing source summaries and changed histories remain visible failures. Opaque native compaction output is never replaced by a manufactured Switch token.

The provider and history stores reload their latest protected state and serialize write transactions across backend and editor instances. A saved provider edit must not be overwritten by a concurrent successful-route receipt, and a second history writer must preserve the first writer's checkpoint.

## Verification

Run the model-provider test executable with `--backend-only --codex <reviewed native CLI> --backend <published backend executable>`. Its disposable homes and fictional HTTP upstreams exercise the real app-server, combined pagination, native/custom routing in one conversation, visibility, addition/deletion, exact credential routing, process identity, editor shutdown independence and unchanged root configuration/authentication. Run the existing provider and native recovery suites after storage/history changes.

These contracts are separate from installed native picker inspection, real provider inference, the one authorized owner installation, and public release. The current implementation is reviewed against native CLI `0.167.0-alpha.4.1`; a future native protocol change requires renewed compatibility validation, not a silent root HTTP fallback.
