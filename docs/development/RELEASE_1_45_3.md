# Release 1.45.3

Windows desktop custom-model selections no longer overwrite the ordinary Codex CLI's shared default model or reasoning effort. Switch retains these desktop defaults in its existing encrypted provider store and applies them only to desktop conversations. Native model selections and unrelated configuration edits retain the official version-checked writer.

This repairs the unsupported `cs_...` model failure observed when launching a normal ChatGPT-authenticated CLI after selecting a custom model in the desktop. Restore an already affected native default through the official configuration writer; updating the adapter prevents subsequent leakage but does not guess or overwrite an existing user setting.

## Verification and publication

Actual native-backend contracts cover both persistence RPCs, concurrent native configuration reads, adapter reopening, stale versions, removed models, unrelated edits and native restoration. Local verification passed55backend checks,354provider checks and100installed self-tests. Real Qwen and ordinary native responses succeeded with shared model/authentication unchanged. The running desktop acknowledged the corrected catalog.

Require all six protected checks on the release head, exact-source native package manifests and archive validation, sanitized CI evidence, uploaded checksums and anonymous download verification before publishing. Preserve existing releases and owner sessions. Windows/Linux packages are unsigned; macOS bundles are ad-hoc signed and not notarized. Linux remains an integration preview.
