# KDE Secret Service contracts

Issue [60](https://github.com/Gaebabi/codex-switch/issues/60) extends encrypted account storage to independently declared KWallet6 and KSecrets builds. Source1.37.0 does not change CADL1, stored32-byte keys, item attributes, system providers or user keyring configuration. Native validation and actual app confirmation are required before promotion.

| Provider | Declared upstream/package | Encoding | Native targets |
| --- | --- | --- | --- |
| KWallet6 | Debian `kwallet6`6.13.0-1 | QCA minimal shared secret; positive129-byte public key | x64, ARM64 |
| KSecrets | Debian `kwallet6`6.26.0-1 (`ksecretd`) | normalized128-byte shared secret/public key | x64, ARM64 |

The [audited manifest](../../src/platforms/security/LinuxSecretProviders.json) pins each native daemon SHA256, package SHA256/download and disposable base image. Its packages come from the [Debian snapshot archive](https://snapshot.debian.org/package/kf6-kwallet/). This is an explicit list of verified builds, not a claim that every distribution/version is supported. Changed or unlisted builds need their own source and native verification before adding a manifest entry. No provider is installed or replaced by the application.

The handshake representation changed in [upstream307db626](https://invent.kde.org/frameworks/kwallet/-/commit/307db62637ac2ca78ff670d746433be9381a923b), first included in6.25.0. A service name or editable Qt version property cannot establish the right contract. Before the single encrypted handshake, the application verifies the original unique D-Bus owner/UID, gets that owner's PID, and compares the advertised version with an embedded contract whose native ELF executable is root-owned, non-writable, SHA256-pinned and descriptor-bound to `/proc/<provider-pid>/exe`. The existing client executable verifier now lives in PlatformSecurity so both callers use the same no-follow/ownership/architecture/capability/checksum logic. Modern KDE identity takes precedence over its legacy alias.

Legacy KWallet5 retains its verified encoding. GNOME and normalized KSecrets use upstream padded derivation. There is no decryption-error retry, alternate derived key, plaintext fallback, implicit unlock, provider switch or credential migration. App-private libsecret ABI1, LGPL source/relinking procedure and encrypted-only sync/async behavior remain unchanged. Unknown providers/versions, changed executables and inaccessible procfs refuse safely; existing data remains intact.

## Verification checkpoints

- [x] Inspect upstream legacy/normalized source and pin actual x64/ARM64 provider packages and native executables.
- [x] Select the attested contract before opening an encrypted session; preserve existing provider/key/vault identities.
- [ ] Run both actual providers on both native architectures:1024 sessions, existing CADL1/original key, opaque bytes, normal AccountService policy, locked/in-place unlock/original PID, restart, concurrency, malformed/ambiguous/missing keys, tamper, timeout and offline refusal.
- [ ] Reject forged version evidence and a declared version with the wrong process image before key access; restore original-provider reads without data changes.
- [ ] Retain GNOME/KWallet5/client/SQLite and Windows/Mac checks. Inspect actual own-window app confirmations and publish stripped, synthetic-only source/runtime/architecture/hash receipts.
- [ ] Complete exact-head Verify/Linux native CI, author self-review and expected-SHA merge. Author review is not independent approval.

Provider tests use separate owned private file-socket D-Bus/HOME/XDG sessions, test-only PAM controls and Qt offscreen. Application captures use own XIDs on X11/Xvfb. Neither proves a complete KDE/Wayland desktop, authenticated Codex switching/resumption (#6), clean-machine packages (#7) or a new binary Release.

## Actual application baseline; confirmation pending

![Actual Linux preview with fictional profiles](https://raw.githubusercontent.com/Gaebabi/codex-switch/495ec6157747ac12e1cb026604a5f09134e5f5eb/docs/evidence/linux-kwallet-storage/linux-x64/en-dark-800x600-native.png)

Source1.36.0, actual native Ubuntu24.04 x64 own-XID X11/Xvfb capture, authored synthetic profiles only, metadata stripped. [Baseline receipts and five-target comparisons](../evidence/linux-kwallet-storage/README.md). This image establishes the preceding application; provider protocol behavior needs the separate native receipts above. No personal desktop, pointer, identity, home, chat, credentials or live Codex is used.
