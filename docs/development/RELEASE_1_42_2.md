# Release1.42.2: account recovery and optional automatic switching

Implementation [#108](https://github.com/Gaebabi/codex-switch/issues/108) and delivery [#109](https://github.com/Gaebabi/codex-switch/issues/109) cover the owner-requested Windows/macOS/Linux release and downloaded Windows installation. Existing releases and account/vault/key/IPC identities remain unchanged.

## Changes

- Account warning activation requests browser sign-in for that account. A different account is rejected before replacing credentials; hover retains the explanation.
- Account auto-switch is Off by default. A successful refresh can select an eligible account below5/10/15/20% remaining, using fresh matching windows, five-minute cooldown and busy/recovery guards. Choose the best available account or a saved custom priority from account menus.
- Saved handoff checkpoints survive restart. Failure/cancellation after closing Codex attempts bounded reopening; a pending automatic handoff gets one saved startup retry, then remains available through Recover. Ambiguous Continue is not blindly repeated.
- Subscription expiry and days remaining have headings. Pro tiers retain official **Pro 100/200/500** names. The Windows titlebar identifies its loaded version.
- Tray-only startup supports an authorized utility installation update. Existing Codex processes and account data do not need to be stopped or switched for this installation.

## Required gates

1. Scoped source/privacy checks and fictional identity/quota/restart tests.
2. Actual native application confirmation and before/after evidence; component renders alone are insufficient. [Evidence and pending confirmation](../evidence/account-automation/README.md).
3. All six up-to-date protected checks at final head, recorded author review, resolved discussions and expected-head merge. Author review is not independent approval.
4. Nine native-built packages: Windows x64 ZIP, macOS arm64/x64 ZIPs, Linux x64/ARM64 DEB/RPM/tar.gz previews. Check version, runtime10.0.13, architecture, inventory, sensor binding, signatures and package provenance.
5. Draft v1.42.2 with SHA256SUMS.txt, VERIFICATION.json and sanitized evidence. Check every uploaded asset digest/size before manual publication; CI retains read-only permissions.
6. Download public assets, verify their hashes, install downloaded Windows bytes in a new version directory, preserve previous installation/data/shortcut arguments and normally restart only Codex Switch. Confirm its loaded assembly and preserved account/auth/preferences state.

Links in the release branch are staged targets until the public Release and its verification manifest exist. That manifest records the exact final source, CI runs, packages, evidence hashes and publication scope.

## Acceptance boundaries

Native fictional controls and policy/restart contracts do not establish authenticated two-account switching or active-chat continuation. Linux remains an unsigned integration preview with `supportedAccountHandoff:false`; [#76](https://github.com/Gaebabi/codex-switch/issues/76) and the wider Linux support gates remain open. Windows/Linux are unsigned; Mac uses native ad-hoc signatures without Apple notarization. The maintainer MacBook image remains a separate follow-up. No personal screenshots, account credentials, owner Codex shutdown, driver installation or real account switch are needed for publication/installation verification.
