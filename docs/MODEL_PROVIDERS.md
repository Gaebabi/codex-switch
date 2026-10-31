# Configurable model providers

Unreleased source work for [issue138](https://github.com/Gaebabi/codex-switch/issues/138). Published downloads do not contain this feature; this guide covers source builds.

Keep eligible OpenAI models and custom Responses models in the existing Codex model picker. OpenAI models retain their original display names; custom models include their provider name in parentheses. Selecting a model changes that chat's next request; an in-flight response keeps the route and key it started with.

## Set up a provider

1. Open Models in the Windows toolbar, or Model providers in the shared desktop settings menu.
2. Select Add a provider, enter a display name, API base URL and API key, then choose Discover and save.
3. If the provider has a large catalog, enter the model IDs to include, one per line. Leave this field empty to discover all models within the supported bounds.
4. Choose Connect Codex, then use Restart Codex on the main window when ready. Connecting prepares settings; the separate restart action normally closes and reopens Codex and resumes eligible unfinished chats with the same login.
5. Keep Switch running, including in its tray. Choose OpenAI or a custom model inside each Codex chat. Model-list refresh can take a few minutes after registering models.

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

Switch supports at most 32 providers and 128 selected models per provider. Discovery reads are bounded at 4MiB; the complete Codex-facing catalog must fit the client's 1MiB limit. Requests are bounded at 16MiB. Select fewer model IDs when necessary. A discovery result alone does not certify the provider's tool or conversation compatibility.

Changing providers sends the replayed conversation to the selected provider. Provider-encrypted reasoning is omitted after a conversation crosses routes. An existing provider-encrypted compaction cannot be reconstructed by another provider: that request fails explicitly and requires a portable text summary. Stateful response references are refused rather than silently dropping history. New gateway sessions are configured for local compaction.

The connection uses existing file-backed OpenAI authentication. The local listener accepts authenticated requests on127.0.0.1 only, refuses browser-origin requests, and never forwards an OpenAI credential or account header to a custom provider. A custom provider receives only its saved key. Redirects are not followed and generation requests are not retried by the gateway.

Unregistered model names fail explicitly. OpenAI routes are checked against the current sign-in's catalog, while custom routes use their registered aliases. Managed provider/catalog requirements are refused before preparing a local connection.

The initial configuration selects the codex_switch_router provider with an explicit catalog URL, and also sets the built-in OpenAI base URL to the same listener for pre-existing OpenAI-provider chats. WebSockets and request compression are disabled for portable full-input HTTP replay. Unrelated custom provider definitions are preserved; already-pinned chats using those independent definitions are outside the verified migration scope.

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
