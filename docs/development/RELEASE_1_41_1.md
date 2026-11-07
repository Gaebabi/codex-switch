# Release1.41.1: Windows warnings and Linux terminal switching

Issue [#103](https://github.com/Gaebabi/codex-switch/issues/103) authorizes Windows x64 and Linux x64/ARM64 DEB/RPM/tar.gz publication from current main. Existing releases and macOS1.40.2 downloads/checksums remain unchanged. A maintainer MacBook image is still pending; native CI does not substitute for it.

## Included changes since1.40.7

- Windows refresh failures appear as an accessible warning beside the affected account. Hover or click shows its explanation; resource and disk rows remain visible. Existing cached usage, sensors, account storage and switching semantics are retained. [Actual before/after](../evidence/account-warnings/README.md).
- Linux packages include `CodexSwitch cli`: existing encrypted account management without DISPLAY, manual switching and exact saved CLI/exec-session resumption. `--continue` explicitly requests a follow-up. A verified standalone native CLI and unlocked supported Secret Service are required. [Commands and genuine two-account x64 verification scope](LINUX_TERMINAL.md).
- Current app captures retain original Windows controls and actual Linux Xfce frames, fictional account cards and per-image provenance. No personal account/screen pixels, tokens, raw diagnostics or authentication data enter publication.

## Required publication gates

- Prepare a focused source/docs branch from current main; preserve the original local branch/index/worktree and personal data.
- Run all six protected checks at final head: repository, Windows, macOS arm64/Intel regression, native Linux x64/ARM64. Mac regression does not publish a new Mac binary.
- Inspect actual native app screenshots, seven package hashes, source/version/RID/runtime, inventories and sensor-worker binding. Attach sanitized receipts/evidence.
- Record author review (not independent approval), merge under unchanged protection and compare reviewed/merged trees.
- Stage draft `v1.41.1`, upload seven packages plus checksums/verification/evidence, check every GitHub asset digest/size, publish and verify downloads and four README languages.

The published Release and its `VERIFICATION.json` provide the authoritative final source, CI, package/evidence checksums and publication record. Until publication, new download links are staged release targets.

[Ten inspected native app/popup/terminal images and their exact capture provenance](../media/release-1.41.1/README.md) are retained in source. The [implementation-tree receipt](../media/release-1.41.1/implementation-tree.json) compares every captured implementation/build-input tree to the release branch. Capture-run cancellation after its completed capture steps does not imply a failed capture or final build success; final package promotion requires separate successful final-head CI and its own provenance.

## Evidence and security boundaries

Linux remains unsigned integration preview with `supportedAccountHandoff:false`. Real x64 CLI contextual resumption, native ARM64 offline checks and desktop active-chat acceptance are separate evidence. Booted GNOME/KDE/Wayland/ARM64 desktop and accessibility criteria in #5/#6/#7/#76 are not closed by this release. GUI integration retains the approved official Debian client26.1007.71326, existing file credential policy and X11/XWayland requirements; headless CLI commands have their own checksum/provider requirements.

Windows/Linux packages are unsigned. Checksums establish byte integrity, not publisher authentication. Existing vault/key/keyring/home/IPC identities are unchanged. No live personal Codex shutdown, account switch, driver installation, automatic UAC or personal installation replacement is required for publication.
