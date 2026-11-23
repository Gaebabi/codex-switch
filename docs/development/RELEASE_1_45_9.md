# Release 1.45.9

## Changes

- Launch the Windows Store build of Codex through its registered Windows application identity so it groups under the existing taskbar icon. Standalone installations use their executable.
- Reopen chats after a same-account restart and leave automatic continuation to Codex. Active native goals also keep continuation ownership during account handoff.
- Publish Windows tray readiness as soon as the controls are usable. The first quota refresh runs after the UI can render and accept input.
- Show newly received quota and usage history while the subscription request is pending. Retain the last verified subscription date when that request fails.
- Use verified full-size screenshots for the model-provider documentation. [Screenshots and capture scope](../media/custom-models/README.md).

## Verification scope

Disposable Windows checks cover registration-aware launch, ordinary restart continuation, an in-flight quota refresh and subscription lookup failures. The live taskbar repair kept the existing window and restored its grouping. Native IPC was unavailable in the live restart check. Startup timing on shared storage varied across launches, so the release makes no cold-start time claim.

[Windows startup comparison](../evidence/release-1.45.9/README.md).

Windows x64, macOS Apple Silicon and Intel, and Linux x64 and ARM64 preview packages include their runtime. Linux preview packages retain their limited account handoff support status.
