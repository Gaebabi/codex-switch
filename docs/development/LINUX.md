# Linux implementation and verification

Linux support is in progress in [issue5](https://github.com/Gaebabi/codex-switch/issues/5), starting with [native storage50](https://github.com/Gaebabi/codex-switch/issues/50). Source1.33.0 introduces Secret Service protection; native provider verification, Linux host evidence, client trust, actual manual handoff and packages are required before advertising supported account switching. Published downloads remain1.27.6, without a Linux package. KDE/KWallet, broader environments and actual account/handoff acceptance remain separate gates.

P8.1 is merged in PR51/main3b67458b after all six final-head jobs and author self-review. Native GNOME Keyring18 checks across12 phases and source-app12 captures pass per x64/ARM64 architecture; [actual program evidence and limitations](../evidence/linux-vault/README.md). Source1.33.1 adds the separate [read-only SQLite52](https://github.com/Gaebabi/codex-switch/issues/52) checkpoint; its native acceptance is required before this capability is marked complete.

## Read-only Linux SQLite/WAL

The Linux provider resolves distribution `libsqlite3.so.0` (`libsqlite3-0` on Ubuntu/Debian), using the existing read-only open/statement/busy-timeout path. It does not select a Windows library or immutable mode, rewrite client configuration or enable installed-client IPC. `--codex-home <explicit isolated directory> --verify-queues <private report>` invokes the ordinary metadata reader before GUI/vault/client initialization; missing/incompatible native SQLite reports unavailable, never a zero queue. UI/account actions stay gated.

`python scripts/verify-queues.py --executable artifacts/<native-rid>/CodexSwitch` creates only owned disposable fixtures. It checks committed WAL-only additions, uncommitted isolation, saved/server dedup, child/CLI exclusion, removals, malformed/future schemas and exact database/WAL presence and SHA256 before/after each read. Linux additionally removes write permissions from the database/WAL/SHM and containing directory while keeping the explicit reports subdirectory writable; the actual non-root runner must still read committed rows. Shared-memory read coordination is not represented as byte-immutable SHM: [SQLite WAL read-only rules](https://www.sqlite.org/wal.html#read_only_databases). Production reads fixed SELECT metadata queries; statement read-only status is an additional direct-write guard, not a claim to sandbox arbitrary user SQL ([SQLite scope](https://www.sqlite.org/c3ref/stmt_readonly.html)).

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

## Source preview and actual window evidence

Private-file opens are nonblocking before their descriptor type/UID/mode checks. A named pipe at a vault/file destination therefore fails as a non-regular file without waiting for a writer; native regression verifies read/replacement refusal, bounded completion and preservation of the pipe type.

`bash scripts/build-linux.sh` publishes a complete self-contained source preview for the native x64/ARM64 host and checks its runtime/project DLLs. Run `artifacts/<rid>/CodexSwitch`; the shared UI has an explicit localized account-capability gate while client/handoff verification is incomplete. Account reads, switching and real credential access are disabled in this preview. Resource collection is explicitly unavailable; it never becomes a fabricated zero. Trayless close exits. Linux tooltips reduce motion by default until native accessibility integration is verified.

`--verify-ui <private-directory>` shows the same published app with synthetic profiles, exercises EN/KO, dark/light and500×360/600×420/800×600, checks actual geometry and captures its own visible XID using `xwd`. Image conversion preserves pixels; the common exporter strips metadata and binds exact source/runtime/architecture/hash. CI uses a private Xvfb display and software rendering, with Noto CJK/fontconfig dependencies. This is X11/Xvfb evidence, not a native Wayland or full GNOME/KDE desktop-session acceptance result. Screenshots contain no personal desktop, pointer, home, keyring, account or Codex data. Do not publish raw captures or the keyring sandbox.

The shared row minimum is126 logical pixels including card margins; the118-pixel card body plus8 pixels of margin meets that contract. Native verification checks the row slot, text containment and absence of unexpected account scrolling separately. Linux supplies its own8-pixel title inset; Mac retains84 pixels for native window controls.

Both native provider jobs and the full Windows/Mac regression/evidence must pass at the final reviewed head before issue50 promotion. Neither portable tests nor a Mac dashboard establish Linux provider support. Client discovery/credential backend/SQLite remain in #5, full native switch/resume in #6 and clean-machine packages/accessibility/environment expansion in #7. New binary Release/signing is a separate owner gate.
