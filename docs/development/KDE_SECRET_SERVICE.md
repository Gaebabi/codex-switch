# KDE Secret Service contracts

Issue [#60](https://github.com/Gaebabi/codex-switch/issues/60) extends encrypted account storage to independently declared KWallet6 and KSecrets builds. Source 1.37.0 preserves CADL1, stored 32-byte keys, item attributes, system providers and user keyring configuration. [Actual native acceptance, 22 inspected program images and source/runtime/hash receipts](../evidence/linux-kde6/README.md) bind code `b46b382`; all six code-head CI jobs pass. [PR #61](https://github.com/Gaebabi/codex-switch/pull/61) records final-head review and promotion.

| Provider | Declared upstream/package | Encoding | Native targets |
| --- | --- | --- | --- |
| KWallet6 | Debian `kwallet6`6.13.0-1 | QCA minimal shared secret; positive129-byte public key | x64, ARM64 |
| KSecrets | Debian `kwallet6`6.26.0-1 (`ksecretd`) | normalized128-byte shared secret/public key | x64, ARM64 |
| KSecrets6.30 | Debian `kwallet6`6.30.0-1 (`ksecretd`) | normalized128-byte shared secret/public key; current PAM/GUI unlock API | x64, ARM64 |

The [audited manifest](../../src/platforms/security/LinuxSecretProviders.json) pins each native daemon SHA256, package SHA256/download and disposable base image. Its packages come from the [Debian snapshot archive](https://snapshot.debian.org/package/kf6-kwallet/). This is an explicit list of verified builds, not a claim that every distribution/version is supported. Changed or unlisted builds need their own source and native verification before adding a manifest entry. No provider is installed or replaced by the application.

The handshake representation changed in [upstream307db626](https://invent.kde.org/frameworks/kwallet/-/commit/307db62637ac2ca78ff670d746433be9381a923b), first included in6.25.0. A service name or editable Qt version property cannot establish the right contract. Before the single encrypted handshake, the application verifies the original unique D-Bus owner/UID, gets that owner's PID, and compares the advertised version with an embedded contract whose native ELF executable is root-owned, non-writable, SHA256-pinned and descriptor-bound to `/proc/<provider-pid>/exe`. The existing client executable verifier now lives in PlatformSecurity so both callers use the same no-follow/ownership/architecture/capability/checksum logic. Modern KDE identity takes precedence over its legacy alias.

Legacy KWallet5 retains its verified encoding. GNOME and normalized KSecrets use upstream padded derivation. There is no decryption-error retry, alternate derived key, plaintext fallback, implicit unlock, provider switch or credential migration. App-private libsecret ABI1, LGPL source/relinking procedure and encrypted-only sync/async behavior remain unchanged. Unknown providers/versions, changed executables and inaccessible procfs refuse safely; existing data remains intact.

## Verification checkpoints

- [x] Inspect upstream legacy/normalized source and pin actual x64/ARM64 provider packages and native executables.
- [x] Select the attested contract before opening an encrypted session; preserve existing provider/key/vault identities.
- [x] Run all three declared providers on both native architectures: 34 contracts/18 phases each, 1,024 sessions, existing CADL1/original key, opaque bytes, normal AccountService policy, locked/in-place unlock/original PID, restart, concurrency, malformed/ambiguous/missing keys, tamper, timeout and offline refusal.
- [x] Reject forged version evidence and a declared version with the wrong process image before key access; restore original-provider reads without data changes.
- [x] Retain GNOME/KWallet5/client/SQLite and Windows/Mac checks. Inspect 22 actual own-window app confirmations and publish stripped, synthetic-only source/runtime/architecture/hash receipts. KSecrets 6.30 uses its actual owned Qt password dialog on both architectures.

Promotion requires final exact-head Verify/Linux native CI, author self-review and expected-SHA merge. PR #61 is the live promotion record; author review is not independent approval.

Provider tests use separate owned private file-socket D-Bus/HOME/XDG sessions. KWallet6/early KSecrets use test-only PAM controls and Qt offscreen. KSecrets6.30 removed the old public PAM control: its test starts through the actual PAM pipe/socket interface with an ephemeral synthetic password/hash, then proves in-place unlock through its real own-PID Qt password dialog on a newly owned Xvfb display. The password is passed only through stdin; hashes travel only through inherited pipes, never argv/environment/logs. Test-only salt/PAM/dialog helpers cannot unlock a production wallet. Original provider PID and original stored key/ciphertext are rechecked after unlock and restart.

Actual source-app captures use own XIDs on X11/Xvfb with a separate non-activating private session D-Bus and private HOME/XDG. The identical published app hangs during initialization in a container without a desktop session bus; providing a proper private session fixes the capture environment. No production UI or timeout was relaxed. Container runs have exact named/labelled ownership and clean up on failures as well as success. These tests do not prove a complete KDE/Wayland desktop, authenticated Codex switching/resumption (#6), clean-machine packages (#7) or a new binary Release.

## Actual application baseline and verified confirmations

![Actual Linux preview with fictional profiles](https://raw.githubusercontent.com/Gaebabi/codex-switch/495ec6157747ac12e1cb026604a5f09134e5f5eb/docs/evidence/linux-kwallet-storage/linux-x64/en-dark-800x600-native.png)

Source 1.36.0, actual native Ubuntu24.04 x64 own-XID X11/Xvfb capture, authored synthetic profiles only, metadata stripped. [Baseline receipts](../evidence/linux-kwallet-storage/README.md) and [source 1.37.0 before/after plus six new KDE target confirmations](../evidence/linux-kde6/README.md). Provider protocol behavior is established by the separate native receipts. No personal desktop, pointer, identity, home, chat, credentials or live Codex is used.
