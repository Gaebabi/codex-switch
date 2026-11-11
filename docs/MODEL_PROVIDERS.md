# Configurable model providers

Available in the 1.43.3 release. [Issue138](https://github.com/Gaebabi/codex-switch/issues/138) tracks remaining actual desktop picker and external-provider compatibility acceptance.

Keep eligible OpenAI models and custom Responses models in the existing Codex model picker. OpenAI models retain their original display names; custom models include their provider name in parentheses. Selecting a model changes that chat's next request; an in-flight response keeps the route and key it started with.

## Set up a provider

1. In1.44.0, open Model providers in the titlebar Settings gear. Older1.43.3 Windows packages use Models in the toolbar.
2. Select Add a provider, enter a display name, API base URL and API key, then choose Discover and save.
3. If the provider has a large catalog, enter the model IDs to include, one per line. Leave this field empty to discover all models within the supported bounds.
4. Choose Connect Codex, then use Restart Codex in Settings when ready (on the main window in older1.43.3). Saving provider details alone does not connect Codex. The editor keeps the connection state visible after saving. Connecting prepares settings; the separate restart action normally closes and reopens Codex and resumes eligible unfinished chats with the same login.
5. Keep Switch running, including in its tray. Choose OpenAI or a custom model inside each Codex chat. New connections use an explicit local model catalog; after changing its models or reasoning settings, restart Codex when ready to load the updated catalog.

Select an existing provider to edit it. An empty key field retains its saved key; changing the destination URL requires a replacement key. Keys remain masked in the editor and encrypted in the separate local model-providers.vault file. The account vault and original OpenAI login are preserved.

Restart Codex keeps the current account and original chat names. It uses the existing durable recovery and uncertain-Continue suppression. Running work and active unfinished goals are captured; completed or already-running work is checked before another Continue is sent. Existing credit-protection settings still apply. The action stays disabled while another login/restart/handoff is in progress.

Common full endpoint URLs ending in /chat/completions, /responses, /responses/compact or /models are normalized to their API base without changing the server. For OpenRouter, the base is https://openrouter.ai/api/v1.

Restore connection restores the values Switch changed while preserving unrelated settings and comments. If one of those values was edited elsewhere, restoration refuses to overwrite it. Restart Codex once after restoring. A saved custom default is restored to the original default; chat histories are not rewritten.

## Compatibility and history

The custom endpoint must implement Responses streaming, tools and follow-up conversations. A Chat Completions or Anthropic Messages endpoint alone is insufficient. HTTPS is required, with HTTP allowed only for loopback services.

A provider's native Codex catalog retains its model instructions and capabilities. A plain OpenAI-compatible model list receives conservative text-only settings and the attributed Codex generic fallback instructions. If its exact model ID also appears in the signed-in account's current OpenAI catalog, Switch uses that model's reasoning levels and default. Other capabilities are not inferred from a name. [Fallback attribution](../licenses/OpenAI-Codex.NOTICE.txt).

### GPT reasoning controls

Custom GPT models can use the ordinary Codex reasoning control. Switch passes the selected `reasoning.effort` to the custom Responses endpoint. A native Codex catalog supplies the supported levels; a plain list uses the exact-ID OpenAI match described above. The destination must support the same effort values; matching IDs do not certify external provider behavior.

For a model absent from that catalog or a route with different support, expand **GPT reasoning in Codex**, select the saved model, enable **Set supported reasoning levels**, select its documented levels and a default, then save. Disable the override and save to return to automatic metadata. These settings apply to one model and remain after rediscovery at the same destination. A changed destination resets them. Refresh the Codex model list after saving; its cache may take a few minutes to update.

Supported control values are `none`, `minimal`, `low`, `medium`, `high`, `xhigh` and `max`, subject to the installed Codex version and the model's API. Unsupported selections fail before inference rather than silently using another strength. A plain list with no exact match or explicit settings keeps provider-default behavior. No additional reasoning API or token-budget conversion is used.

Switch supports at most32 providers and128 selected models per provider. Discovery reads are bounded at4MiB; the complete Codex-facing catalog must fit the client's1MiB limit. Full-input conversation and image requests are bounded at512MiB, including chunked bodies. Request buffers share a1GiB capacity budget, with64MiB reserved for small requests and resize overlap included. At most eight bodies larger than16MiB or of unknown length upload concurrently, within16 upload/routing slots. Up to48 upstream responses can stream concurrently; successful streams do not occupy upload slots. Local overload reports503 with `Retry-After: 1` and is never retried internally. Above-limit bodies report413; unsupported media types report415. Upstream limits still apply. A discovery result alone does not certify tool or conversation compatibility.

JSON validation reads tokens without a payload-sized document metadata pool. It refuses duplicate properties, nesting above64levels, more than one million tokens, property names over1KiB or total property-name bytes over8MiB. Routing controls (`reasoning` and `text`) are each limited to64KiB. These bounds apply before upstream inference and do not remove conversation items. Unchanged OpenAI requests retain exact body bytes; custom-provider rewrites serialize only small controls and stream original history/image value slices. The buffer budget covers request arrays, not all process/runtime/HTTP allocations.

Changing providers sends the replayed conversation to the selected provider. Provider-encrypted reasoning is omitted after a conversation crosses routes. An existing provider-encrypted compaction cannot be reconstructed by another provider: that request fails explicitly and requires a portable text summary. Stateful response references are refused rather than silently dropping history. Compaction remains tied to its original provider.

The connection uses existing file-backed OpenAI authentication. The local listener accepts authenticated requests on127.0.0.1 only, refuses browser-origin requests, and never forwards an OpenAI credential or account header to a custom provider. A custom provider receives only its saved key. Redirects are not followed. The only automatic inference retry is the explicit HTTP507 recovery described below.

For Codex requests carrying the native turn identity, the gateway retains a hashed receipt for each distinct input and route for30minutes. Automatic client retries of that same turn request are refused before another upstream send; a new user turn or changed full-input tool follow-up remains eligible. The receipt budget is4096 and a full budget reports503. Receipts contain no request body or credentials and last only for the current gateway process. This prevents the built-in client's retry policy from multiplying the gateway's own recovery budget.

### Automatic HTTP507 recovery

For streaming `POST /responses`, an upstream HTTP507 refusal before its response begins is retried at most three times, after2,5and10seconds. The original attempt plus retries makes at most four upstream requests. All attempts retain the original selected model, provider URL/key, signed-in account, complete input bytes/history and tool-call identities. The current sign-in is revalidated before another attempt. There is no new chat, `Continue` message, model/account change, history trimming or context compression.

While waiting, Switch sends only SSE comment keep-alives to the original caller. These are not assistant messages or model response events. Successful upstream events continue in the same request. After exhaustion or a different upstream failure, Switch emits one terminal `response.failed` event with the HTTP status and retry count; upstream error bodies are never echoed. Success alone records conversation provenance. The total request deadline remains30minutes, and request arrays remain within the existing aggregate buffer/admission limits until retry eligibility ends.

### Continue after a failed response stream

A later hotfix observes confirmed desktop turn failures of every error type, including HTTP507exhaustion, invalid event streams, authentication, quota and context errors. Eligibility uses the persisted failed status, without matching error words or requiring an error message. After the original turn has stopped, Switch can send one identified `Continue` through that chat's existing desktop owner, inheriting its settings. It does not replay the original Responses input after tools or partial output may already have run. Consecutive failed automatic continuation turns share at most three attempts, with3/10/30second backoff; successful completion closes that recovery chain. This is separate from the pre-response HTTP507request budget, so each new continuation has its own bounded HTTP507retries. A persistent error can still require user action after all three continuations fail.

The observer requires a recent persisted `task_complete` failure, an observed stopped main chat with no approval/input/queued follow-up, matching current model/provider, a connected original OpenAI/Switch provider and unchanged current sign-in/route during backoff. Custom model aliases must still be registered. An earlier successful request is not required: the first request can fail before successful-route history exists. This never fabricates route history or bypasses encrypted-compaction and portable-history checks in the gateway. It skips active, completed, interrupted, paused/limited-goal and unsupported-provider chats. The bounded read-only rollout tail comes from the existing SQLite thread record; a newer `task_started` invalidates a historical failure. No original transcript is rewritten, no account or model is changed and no window is opened or activated.

Encrypted recovery receipts persist attempt count and message/turn identities before submission. A lost/ambiguous IPC response or persistence error stops that recovery rather than resending. Exhaustion survives a Switch restart. Reauthentication, route edits, new user turns, queues and actual user interruptions prevent resubmission. Generic idle/EOF alone does not establish a failed turn. The observer applies only to chats observed in this local desktop with a connected OpenAI/Switch provider; it does not revive archived or unloaded historical chats. The local `stream-recovery-state.json` also retains up to128deduplicated decisions with fixed reason codes, hashed chat/turn references and attempt counts. It omits chat text, raw errors, account identities and provider secrets.

Cancellation, a disconnected client, sign-in changes, gateway shutdown, transport exceptions and any failure after a successful upstream response stop recovery. Images, compaction, nonstream requests and other HTTP errors retain their single-send behavior. A manual new send has a new bounded budget. An already failed turn from before this hotfix needs one manual resume; the gateway does not scan old chats or submit work on its own. This recovery cannot guarantee that a server-side507condition clears and does not prove an upstream performed no internal work.

Built-in image generation and editing use the original OpenAI sign-in through `images/generations` and `images/edits`. These requests preserve their JSON body, image references and request IDs and do not change the chat's model route. A custom text model selection does not redirect built-in images to that provider or its API key. Image availability and usage limits belong to the original OpenAI account. This is separate from a text model's ability to receive image attachments.

The image-route and 64MiB corrections are source hotfixes after release1.43.3. Older packages can report an unsupported-endpoint404 for the built-in image tool, or `A bounded JSON Responses request is required` when a full-input conversation exceeds16MiB. The hotfix does not remove conversation history to make it fit. [Verification and delivery scope](evidence/image-gateway/README.md).

A subsequent source correction raises the64MiB intermediary limit to512MiB because an image-heavy full-input chat can exceed it. The [OpenAI image input guide](https://developers.openai.com/api/docs/guides/images-vision?api-mode=responses) documents up to512MB total payload for the public API; authenticated Codex and custom upstream acceptance must be verified separately. This change preserves image data and encrypted history and does not silently compact or discard them.

The large-upload admission slot and request buffers are released when final upstream response headers arrive, so ongoing response streams do not occupy large-upload capacity. Explicit retryable507refusals retain the same budgeted input until the next attempt or terminal failure; they never allocate a second full body.

Unregistered model names fail explicitly. OpenAI routes are checked against the current sign-in's catalog, while custom routes use their registered aliases. Managed provider/catalog requirements are refused before preparing a local connection.

New connections preserve the existing built-in `openai` provider identity and set `openai_base_url` to the loopback listener. An explicit private `model_catalog_json` file supplies the combined native and custom model list to Codex and its desktop picker. The catalog contains model metadata, not API keys. It is refreshed on provider edits, account changes and the existing refresh cycle; Codex loads the file at startup. WebSockets and request compression are disabled for portable full-input HTTP replay. Existing provider definitions are preserved.

Older connections that use `codex_switch_router` remain untouched. Recent Codex versions reject a chat when its saved provider identity differs from the current provider, even if both use the same gateway. Restoring/reconnecting an older installation can therefore affect which provider-pinned chats it can reopen. Switch does not rewrite chat history or bypass that check. Keep the older connection for its existing router-pinned chats; new built-in OpenAI connections retain compatibility with original OpenAI chats.

Workspace-enforced routing, keyring-only authentication, API-key-only dashboard startup, external production provider compatibility, and the actual desktop picker still require their own acceptance evidence. Do not bypass managed policy or replace the owner's running client to verify them.


## Continue a chat with encrypted history

The refusal protects context that only its original provider can read. Switch cannot decode it, and copying the opaque item is not a portable summary.

1. Keep the original chat. In Switch **Models**, expand **Continue an existing chat** and copy the handoff prompt. This copies a fixed instruction only; it does not read or send a chat, access a key, generate a response or change settings.
2. In the original Codex chat, select its original provider and model using the same signed-in account. Paste the prompt and request a readable handoff summary. The original provider generates it using that account's normal allowance; the destination API cannot generate it from the source provider's encrypted compaction.
3. Start a new chat, select the intended destination model and paste the readable summary. Check the provider label before sending. The source chat remains available for reference, and the handoff does not grant new permissions.

If the original model still receives the refusal (for example, the old route was not identifiable), use **Restore connection**, then the main-window **Restart Codex** when no work is running. Select the original model and account before requesting the summary. Reconnect Switch later for the destination chat. Do not clear compaction, delete the source chat or send opaque history to an unrelated provider.

A refused transition no longer changes the recorded conversation route. Once an upstream accepts a request, mixed-route safeguards remain in effect. Missing conversation identity, stateful response references and encrypted compaction crossing providers are still refused. The tests exercise refusal and return to the source, plus a fresh destination chat and its follow-up with a fictional readable handoff; they do not generate or validate a real user's summary.

OpenAI API credits require a provider configured for `https://api.openai.com/v1` and the corresponding OpenAI API key. Selecting an OpenAI-branded model through another service does not change that service's billing destination.

## Verification

The isolated contract suite uses two fictional upstreams and disposable encrypted storage. Its optional official-client probe exercises real model discovery and successive model changes in one app-server process, with no owner account or desktop. It also checks the old OpenAI provider identity separately.

~~~text
dotnet run --project tests/model-providers/CodexSwitch.ModelProviders.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false -- --codex <official-cli-executable>
~~~

Follow the repository's build-output routing for all generated files. The Windows component check renders the production editor offscreen; it does not display a window or establish native desktop acceptance.

[Evidence and limits](evidence/model-providers/README.md) / [official gateway contract](https://learn.chatgpt.com/docs/enterprise/gateway-compatibility) / [Codex model catalog format](https://learn.chatgpt.com/docs/model-catalogs).
