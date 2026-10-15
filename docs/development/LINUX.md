# Linux implementation and verification

Linux support is in progress in [issue5](https://github.com/Gaebabi/codex-switch/issues/5), starting with [native storage50](https://github.com/Gaebabi/codex-switch/issues/50). Source1.33.0 introduces Secret Service protection; native provider verification, Linux host evidence, client trust, actual manual handoff and packages are required before advertising supported account switching. Published downloads remain1.27.6, without a Linux package. KDE/KWallet, broader environments and actual account/handoff acceptance remain separate gates.

## Storage contract

Linux uses a32-byte key in the configured Secret Service and a distinct `CADL1` authenticated AES-GCM envelope (12-byte random nonce/16-byte tag). Windows DPAPI and Mac `CADM1` algorithms/identities remain unchanged; foreign envelopes refuse before key lookup. There is no automatic migration, plaintext fallback, implicit unlock or key replacement. Provider unavailable/locked/missing/ambiguous/invalid states remain distinct. Existing ciphertext never authorizes creation of a replacement master key. Keys remain in process memory until explicit release/exit; startup/locked-key tests release the cache or use a new process.

The backend verifies the service owner's effective UID on D-Bus and pins a new proxy to that unique owner before transferring secrets. Native operations use a10-second cancellable deadline off the UI initialization thread. Cross-process key creation uses a no-follow, user-owned0600 lock file in the private vault directory; contention has its own bounded wait. Missing/locked/ambiguous keys fail without altering the existing ciphertext or deleting native keyring items.

Linux private writes walk directories with descriptor-relative no-follow opens, verify ownership/private modes using `statx`, create0600 staging files exclusively, flush, atomically rename within the same directory and sync the directory. Reads use the verified file descriptor. Existing insecure directories/files are refused instead of silently changing a shared directory's permissions. New directories are0700. Final private directories must belong to the effective user; symlinked components cannot redirect a write. These operations require the declared native Linux environment; no Windows fallback exists.

Native open flags follow the declared process ABI: ARM64's directory/no-follow constants differ from x64. Both require native execution; unknown ABIs refuse instead of guessing. The `statx` layout follows the stable256-byte Linux UAPI. [ARM64 flags](https://github.com/torvalds/linux/blob/v6.8/arch/arm64/include/uapi/asm/fcntl.h), [generic flags](https://github.com/torvalds/linux/blob/v6.8/include/uapi/asm-generic/fcntl.h), [statx UAPI](https://github.com/torvalds/linux/blob/v6.8/include/uapi/linux/stat.h).

Secret values are opaque binary bytes. The key contract requires exactly32 bytes and checks newly stored/read values with constant-time comparison; MIME metadata does not define the cryptographic format. GNOME Keyring46.1 labels returned secrets `text/plain` regardless of the original binary type in [its native implementation](https://github.com/GNOME/gnome-keyring/blob/46.1/daemon/dbus/gkd-secret-secret.c). Native regression explicitly includes NUL/non-UTF-8 key bytes; no text decoding, trimming or base64 repair is used.

## Native provider suite

Run on the declared Ubuntu24.04 x64/arm64 runner with .NET10.0.4xx, libsecret, actual GNOME Keyring, D-Bus/gdbus tools and Python3.12:

```text
dotnet build tests/linux-vault/CodexSwitch.LinuxVault.Tests.csproj -c Release -p:UseSharedCompilation=false -o artifacts/linux-native/tests
python scripts/verify-linux-keyring.py --assembly artifacts/linux-native/tests/CodexSwitch.LinuxVault.Tests.dll --architecture X64 --report artifacts/linux-native/keyring-summary.json
```

Use `Arm64` only on an actual ARM64 runner. `Linux native` CI declares both native runners; cross-compilation does not satisfy this gate. The wrapper creates a private file-socket D-Bus with no activation directories and isolated XDG paths, starts an owned foreground GNOME Keyring, then exercises missing service, actual encrypted application save/reopen, modes/ownership/links, ambiguity/malformed keys, concurrent independent processes, lock/unlock, provider/process restart, bounded timeout, missing key and offline refusal. It never uses the user's bus. Its foreign-owner fixture uses `chown` only inside the verified disposable test root; application execution does not require elevation. Only sanitized semantic results may be published; never upload keyring files, temporary passwords or raw logs.

The test executable unlocks only its disposable GNOME collection through the provider's native diagnostic interface, with the temporary master password encoded by libsecret and supplied on stdin. The production protection assembly has no unlock operation. The harness checks that Secret Service remains the originally spawned foreground PID before/after the unlock; a new helper daemon cannot substitute for the locked provider. [GNOME native interface](https://github.com/GNOME/gnome-keyring/blob/46.1/daemon/dbus/org.gnome.keyring.InternalUnsupportedGuiltRiddenInterface.xml).

The source Linux application/window confirmation and full Windows/Mac regression/evidence are still required for issue50. Neither portable tests nor a Mac dashboard establish Linux provider support. Client discovery/credential backend/SQLite remain in #5, full native switch/resume in #6 and clean-machine packages/accessibility/environment expansion in #7. New binary Release/signing is a separate owner gate.
