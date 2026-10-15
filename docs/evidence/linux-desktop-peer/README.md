# Linux desktop peer verification

For [issue62](https://github.com/Gaebabi/codex-switch/issues/62), [PR63](https://github.com/Gaebabi/codex-switch/pull/63) and parent5/6. Source **1.38.0**, exact code `fc3247610d8eea881f3502ddc26a8ca63c40f218`, passes all four [Verify jobs](https://github.com/Gaebabi/codex-switch/actions/runs/38700266620) and both actual-architecture [Linux native jobs](https://github.com/Gaebabi/codex-switch/actions/runs/38700266624). [Source/runtime/native checks/image hashes/CI and artifact digest receipts](native-results.json). Final complete-head CI, author self-review and expected-SHA promotion are recorded in PR63; self-review is not independent approval.

Each native Linux architecture passes **16 desktop contracts**:11 socket/resource/refusal checks and5 actual sandboxed official-client checks. The real pinned client completes framed initialize with a successful response; its original kernel peer UID/pidfd/start/image, root-managed package/resources and controlled launch are verified. A parent Node preload marker does not execute. Forged same-UID listeners receive zero application bytes; stale endpoints remain unchanged, and sends after the original peer exits refuse. [Trust, erased startup metadata and native requirements](../../development/LINUX_IPC.md).

## Actual official-client confirmation

These screenshots show only the original verified client's visible window on an owned Xvfb display in the declared Debian test image. Network is disabled, so they show the offline startup/splash surface. They prove native window startup; the separate kernel/protocol receipts prove IPC behavior. They do **not** show a signed-in account, working chat, normal shutdown or handoff.

| Native x64 | Native ARM64 |
| --- | --- |
| ![Actual official client x64 offline startup](official-linux-x64/official-client-native.png) | ![Actual official client ARM64 offline startup](official-linux-arm64/official-client-native.png) |
| [16 checks and image hash](official-linux-x64/peer-summary.json) | [16 checks and image hash](official-linux-arm64/peer-summary.json) |

## Actual utility: before and after

Before images retain source1.37.0/merged PR61. After images are the actual published source1.38.0 utility on the native target, with fictional profiles only. The shared Linux account/handoff UI remains gated and resources unknown. These images establish application regression separately from nonvisual peer tests. Windows same-host200% dark images are byte-identical; native Mac captures use macOS15, and Linux utility captures use Ubuntu24.04 X11/Xvfb/software rendering.

| Target | Before | After | Minimum confirmation and receipt |
| --- | --- | --- | --- |
| Linux x64 | ![Before](../linux-kde6/linux-x64/en-dark-800x600-native.png) | ![After](linux-x64/en-dark-800x600-native.png) | [500×360](linux-x64/en-light-500x360-native.png) · [Hashes](linux-x64/evidence.json) |
| Linux ARM64 | ![Before](../linux-kde6/linux-arm64/en-light-800x600-native.png) | ![After](linux-arm64/en-light-800x600-native.png) | [500×360](linux-arm64/en-dark-500x360-native.png) · [Hashes](linux-arm64/evidence.json) |
| Windows x64 | ![Before](../linux-kde6/windows/en-dark-800x560.png) | ![After](windows/en-dark-800x560.png) | [500×360](windows/en-light-500x360.png) · [Hashes](windows/evidence.json) |
| Mac Apple Silicon | ![Before](../linux-kde6/mac-arm64/en-dark-800x600-native.png) | ![After](mac-arm64/en-dark-800x600-native.png) | [500×360](mac-arm64/en-light-500x360-native.png) · [Hashes](mac-arm64/evidence.json) |
| Mac Intel | ![Before](../linux-kde6/mac-intel/en-light-800x600-native.png) | ![After](mac-intel/en-light-800x600-native.png) | [500×360](mac-intel/en-dark-500x360-native.png) · [Hashes](mac-intel/evidence.json) |

All12 exported PNGs were inspected individually. Native HWND/NSWindow/XID crops contain only their owned program windows. No personal screen, account identity, chats, credentials, home paths, key material or cursor enters the capture. Images have no metadata; dimensions and SHA256 match receipts. No private source pixels require blur because fictional profiles/empty authentication are used from capture time. Raw logs and temporary state remain private and untracked.

Existing GNOME29/13 phases, KWallet5 31/15, all six declared KWallet6/KSecrets34/18 targets, client/context120, SQLite26 and source utility12 per Linux architecture pass. Existing Windows93/SQLite23/handoff5 and native Mac UI21/SQLite23/key16 plus other supported-OS regressions pass. Linux protocol boundary counts now exclude the retired blanket IPC refusal because the verified native provider exists; this is not blanket GUI handoff enablement.

Normal intended-client lifecycle, active account identity, chat naming/IDs/folders/models/permissions, one eligible Continue, recovery/cancellation, full GNOME/KDE/XWayland/accessibility and clean install packages remain separate5/6/7 gates. Original keys/data/identity and published1.27.6 downloads are unchanged; no personal account/live client or installed utility was touched.
