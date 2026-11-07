# Linux terminal verification

Source1.41.0 / [issue97](https://github.com/Gaebabi/codex-switch/issues/97) / [PR98](https://github.com/Gaebabi/codex-switch/pull/98). These commands are absent from published1.40.7 downloads. [Setup and command contract](../../development/LINUX_TERMINAL.md).

## Existing application baseline

![Existing actual Linux GUI with four fictional accounts and original OS frame](../../media/release-1.40.7/linux-framed.png)

**Before · Linux GUI:** the existing graphical app; this picture does not establish terminal command support. Its own capture source and scope remain in the [versioned gallery](../../media/release-1.40.7/README.md).

## Actual native terminal confirmations

![Linux x64 actual terminal running headless account switch and list](linux-x64-terminal.png)

**Linux x64 · Ubuntu24.04 · actual xterm/Xfwm4 frame:** the published native source executable runs `cli switch` and `cli list` against two fictional encrypted accounts. DISPLAY is removed from each command's environment. The terminal confirms the selected account and retains both accounts. Original terminal/OS-frame pixels are unchanged; no titlebar composition, personal home, login or account is pictured. [Source, checks and image SHA256](linux-x64.json).

![Linux ARM64 actual terminal running headless account switch and list](linux-arm64-terminal.png)

**Linux ARM64 · Ubuntu24.04 ARM runner · actual xterm/Xfwm4 frame:** the same real command flow on its native architecture. Identical deterministic fictional inputs can produce identical pixels; architecture/source proof is separate from image appearance. [Native ARM64 receipt](linux-arm64.json).

Capture checkpoint `6c40f175d34cf07e5c64bab5aa77f954689eca24`, source1.41.0, [native CI39037048257](https://github.com/Gaebabi/codex-switch/actions/runs/39037048257). Both native architectures pass15 headless contracts with the actual standalone Codex CLI0.162.1 and real encrypted GNOME Keyring. These completed early terminal artifacts remain valid independently of that run's later cancellation by a new checkpoint. The captured executable is built from the recorded capture source; the subsequent3e481b4 correction moves the same TTY inspection into its native platform module. The exporter verifies original artifact digest/size/source, image SHA, original bytes and absent PNG metadata. Only the owned terminal's verified direct-parent OS frame is captured; desktop-root and unrelated windows are excluded.

The15 contracts cover credential-free output, existing account/preference preservation, import, exact previous-login backup, already-current idempotency, unknown/ambiguous account refusal, missing/non-CLI session refusal, actual external CLI conflict without process termination, native session-list semantics, TTY preflight, descriptor-bound interactive/exec resume arguments, exact session/folder/follow-up selection and unchanged configuration. No mocked executable, fake successful turn or owner account is used in these images/contracts.

## Real authenticated Linux x64 verification

After the owner unlocked the original native keyring, source `3e481b4d0c96d9d35357c38d3f1b150be94c856a` used the two preserved real accounts and the approved packaged CLI0.162.0-alpha.17.2. The official model catalogue showed the original default was unavailable to the second account; the dedicated new test explicitly selected a model offered to both. No existing configuration or saved model was changed to bypass that incompatibility.

The first account's initial turn failed at its usage limit, after its ordinary user message was persisted. The operator then explicitly invoked the production `cli switch <other account> --resume <that exact session> --exec --continue`. The real authenticated response recalled the marker from the initial message, proving retained context. Native read-only metadata/transcripts confirmed the same UUID/folder/model/approval/sandbox and exactly one additional Continue. Both accounts, the encrypted pretest backup, original configuration and preexisting transcript bytes were retained; normal account commands restored the original active login. [Sanitized scalar receipt](authenticated-linux-x64.json).

There is no successful initial-turn or uninterrupted mid-turn claim, no automatic quota rotation, and no fictional response substituted for inference. Personal accounts, tokens, session IDs, marker text, raw transcripts, private paths and owner screens are absent from the published receipt. The screenshots above are deliberately separate fictional native runs.

## Offline lifecycle and promotion gates

The network-disabled native desktop-session verifier additionally runs a real official CLI exec, normal interruption, production account switch and exact-session exec resume. Real JSON events and persisted metadata must show the same ID/folder and exactly one Continue before normal interruption; the native nonzero exit is preserved. This proves offline lifecycle behavior with fictional credentials, not successful model inference. Its source-bound `terminal-lifecycle.json` and the complete session receipt are attached to the native CI run.

All six exact-head protected checks, final artifact/privacy inspection, author COMMENT review and expected-head merge are recorded in [PR98](https://github.com/Gaebabi/codex-switch/pull/98). Author review is not independent approval. Desktop active-chat/recovery, full GNOME/KDE/Wayland/accessibility, booted installation and authenticated ARM64 remain separate acceptance gates in the original issues. Published1.40.7 and personal installations are unchanged.
