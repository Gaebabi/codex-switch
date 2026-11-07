# Linux terminal account switching and session resume

Source1.41.0 implements the terminal commands in [issue97](https://github.com/Gaebabi/codex-switch/issues/97), with real two-account Linux x64 contextual CLI resumption verified. [Windows/Linux Release1.41.1](https://github.com/Gaebabi/codex-switch/releases/tag/v1.41.1) includes these Linux commands in the x64/ARM64 packages; no source build or global .NET installation is required. GUI preview, fictional-card screenshots and desktop active-chat acceptance retain their separate scope.

## Commands

The complete Linux app also handles `cli` commands before GUI initialization. DISPLAY/Wayland is not needed. Use an existing unlocked configured GNOME Keyring/KWallet Secret Service and the same Codex home/data directory as the GUI. No new credentials, vault or provider are silently substituted.

Select an official native Linux executable, not the npm JavaScript launcher. Reviewed configuration-protocol versions are exact0.162.1, exact standalone0.163.0(source1.42.6), exact standalone0.164.0(source1.42.7), and the separately approved packaged0.162.0-alpha.17.2. Other versions, including0.163.1/0.164.1/0.165.0, fail the protocol check. The0.164.0 checkpoint requires16 native terminal checks per architecture, preserving the original15 and adding actual initialization/effective-file-policy verification; it does not approve a newer desktop package or certify authenticated continuation. Supply its exact SHA256 and native path together, or use the approved installed desktop package's CLI. Verify where your executable came from before trusting its checksum. The wrapper never searches PATH for an arbitrary program. Examples use shell variables for an explicitly chosen native executable and expected digest:

```sh
# Set these to the actual trusted native executable and its reviewed SHA256.
native=/absolute/path/to/native/codex
expected=REPLACE_WITH_EXPECTED_SHA256
cs() { /absolute/path/to/CodexSwitch cli "$@" --codex-cli "$native" --codex-cli-sha256 "$expected"; }

# Sign in using the official Codex CLI, then save each login under a unique name.
cs import --name personal
cs list
cs sessions

# Quit clients using the shared login normally first. Switching always needs this command.
cs switch personal
cs switch personal --resume 00000000-0000-4000-8000-000000000001
# Add --continue to submit one explicit follow-up after restoring the chosen session.
cs switch personal --resume 00000000-0000-4000-8000-000000000001 --continue
# SSH/batch without a terminal: noninteractive continuation is explicitly selected.
cs resume 00000000-0000-4000-8000-000000000001 --exec --continue
```

The UUID above is an example: replace it with a real saved CLI/exec session from `cs sessions`. The wrapper rejects a nonexistent session or a desktop/app-server metadata-only thread before applying an account. `sessions` returns at most100 CLI/exec records and a `nextCursor`; use `cs sessions --cursor '<nextCursor>'` for the next page. It does not infer tasks from a desktop screenshot. Commands accept `--codex-home`, `--data-dir` and `--client-context` for an explicitly selected existing environment. `list` uses stable account IDs/aliases and omits emails/tokens; duplicate aliases require the full ID.

`--resume` restores the original exact session in its recorded directory. Interactive mode inherits the real terminal and uses official `codex resume --no-daemon`; it does not reuse a background server holding an earlier login. `--exec --continue` delegates to official `codex exec resume --json`, exposing upstream progress events on stdout and wrapper status on stderr. The wrapper sets no sandbox/approval/model/provider override. Original transcript/settings restoration remains the official CLI's responsibility and existing project/managed credential policy must permit the selected account. No automatic rotation, inferred last-session selection, forced client shutdown or automatic repeat after an uncertain send.

Close existing CLI/IDE/desktop clients before switching the shared login. If a normal Codex shared daemon remains after clients exit, stop it explicitly with the official `codex app-server daemon stop` before using the wrapper; it never kills or adopts an external process. Do not stop a daemon that still owns work. Wrapper-launched interactive resumes use their own foreground lifetime. A pending desktop recovery must be completed first. A failed model turn is returned as a failure; a completed account application is not reported as a completed task.

References: [official interactive/noninteractive resume](https://developers.openai.com/codex/cli/reference), [upstream CLI features](https://developers.openai.com/codex/cli/features).

## Delivery checkpoints

1. Define the headless command and safety contract in an issue-linked branch. Preserve the existing encrypted vault, native key provider, public data identities and published packages.
2. Add `CodexSwitch cli list/import/switch/sessions/resume` before GUI initialization. Explicit native executable/SHA selection and fresh file/workspace policy are mandatory. Reuse existing account application and previous-login backup.
3. Bind resume to an exact persisted session UUID and saved working directory. Launch the verified official native `codex resume` with inherited terminal descriptors and no shell, permission override or invented session. `--continue` is an explicit optional follow-up, never an automatic quota/account rotation.
4. Verify normal-user native x64/ARM64 account/data/policy/process conflict contracts and actual PTY/UI resumption. Keep offline lifecycle evidence separate from real account identity/inference/continuation. Capture only an owned terminal with fictional identifiers; strip metadata and bind source/hash.
5. Update all README languages and the support matrix with the actual completed scope. Require all six exact-head protected checks, recorded author review, expected-head merge and public-main verification. Existing desktop/full-environment acceptance issues retain their own criteria.

## Safety contract

- No desktop, DISPLAY, browser or GUI is needed for the terminal command surface. An available unlocked configured Secret Service/keyring is still required for the existing encrypted vault; SSH does not provide one automatically. No plaintext or implicit new key fallback.
- Import saves a login established by the official CLI into the existing vault. It never accepts tokens on the command line. Accounts use explicit stable IDs or unique aliases, never an account index that can silently select a different login.
- Quit existing Codex CLI/IDE/desktop clients normally before switching the shared login. Never adopt or kill another terminal's process. Commands serialize with the existing GUI and other terminal commands; conflicting clients prevent mutation.
- Resume uses an exact local saved session UUID, validates its recorded project policy for the target account, and starts in that saved directory. It does not edit session logs/configuration or downgrade model/sandbox/approval policy. The official CLI owns transcript/settings restoration.
- No automatic retry of a submitted follow-up. A failed launch after a completed account application must report that boundary clearly; a new manual resume is explicit user action.
- Existing pending desktop recovery is not overwritten by terminal commands. No new binary Release is part of this issue.

## Verification status

Native input-descriptor inspection belongs to `src/platforms/client/LinuxTerminal`; the application command orchestrator retains its existing no-native-import boundary. All six local composition checks pass after correcting this ownership. Real-account verification also checks model compatibility: a saved model unsupported by the target account is an upstream turn failure, never a reason to change the saved model or claim successful continuation. A fresh dedicated test may explicitly select a model offered to both accounts before creating its session; the wrapper still supplies no model override.

The implementation builds without errors/warnings. Native x64/ARM64 pass15 headless-command/storage/native-client contracts and actual terminal capture at6c40f17. The normal-user network-disabled lifecycle verification runs ordinary CLI exec, normal SIGINT, production account application and official exact-session exec resume, observing the same ID/folder and exactly one persisted Continue. It returns the native interruption failure and makes no model-success claim. The native desktop-session CI repeats this verifier alongside existing data-preservation tests.

Real authenticated x64 verification at3e481b4 uses the owner's existing two-account vault/keyring, a new dedicated session and a model offered to both accounts. The initial user message persisted, but that account's first turn failed at its usage limit. One explicit switch/resume to the preselected other account produced a genuine response recalling that earlier message, with exactly one Continue and unchanged UUID/folder/model/approval/sandbox. Original configuration/preexisting transcripts and both encrypted accounts were preserved; the original login was restored. No initial successful response, uninterrupted mid-turn completion, automatic quota rotation or desktop/ARM64 authenticated acceptance is claimed. [Actual fictional terminal screenshots, separate authenticated scalar receipt and full scope](../evidence/linux-terminal/README.md).

Final exact-head six-job CI, artifact/privacy inspection, author review and expected-head promotion are recorded in [PR98](https://github.com/Gaebabi/codex-switch/pull/98). Published binaries are not replaced by this source change.
