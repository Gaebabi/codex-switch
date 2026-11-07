# Release1.40.7: Windows resources and Linux preview refresh

The owner explicitly authorizes publishing **Windows x64** and **Linux x64/ARM64 DEB/RPM/tar.gz**, verification materials and current README links. This supersedes earlier no-new-Release notes for those seven packages. Existing releases and macOS1.40.2 assets remain unchanged; no macOS1.40.7 download is advertised.

## Included changes

- Built-in Windows CPU temperature in an account-free NativeAOT worker, optional pinned signed PawnIO driver and explicit session authorization. A visible download/lock+°C button replaces right-click-only discovery. Current capability is re-probed before installation. Account UI stays unprivileged; no automatic UAC/reboot or security-policy bypass.
- Thin disk read/write gauges, actual volume counters and device-derived seek/bus profiles. The scale is observed throughput, not rated bandwidth; sustained busy/latency pressure is separate from drive health. Unknown/mixed devices retain unknowns.
- Reviewed dependency/paired Avalonia and quota-history changes, existing startup recovery, manual switching and eligible continuation. Data/key/vault/bundle/IPC identities and nine complete language packs are retained.

## Verification boundaries

The final source passes all six protected checks: repository, Windows, native Mac arm64/Intel compilation/regression and actual Linux x64/ARM64. Mac regression is not a1.40.7 Mac publication. Windows verifies complete runtime, worker hash binding, CPU app/normal/driver contracts, actual disk counters and native UI. Linux verifies GNOME/KWallet5/KWallet6/KSecrets, client trust/IPC, per-user lifecycle, fresh Ubuntu/Debian/Fedora packages and ordinary isolated x64 Xfce. The owner's stopped Linux accounts/keyring remain untouched.

[Current native gallery](../media/release-1.40.7/README.md), [CPU before/after](../evidence/cpu-setup/README.md) and [disk evidence](../evidence/disk-io/README.md) retain source/runtime/hashes and distinguish fictional UI from actual collection. No personal screen, account identity, home path, authentication, encrypted store or raw private diagnostic belongs in public source/packages/attachments. Original sensitive pixels are never retained under reversible blur.

Linux remains **unsigned integration preview**, `supportedAccountHandoff:false`, with approved official client26.1007.71326, existing file credentials and X11/XWayland. Authenticated active-chat continuation, full booted GNOME/KDE/Wayland/ARM64 desktop and historical privacy criteria remain open. Owner physical CPU confirmation is separate from CI and UI fixtures. [Linux requirements](LINUX_PACKAGES.md).

## Publication procedure

1. Verify exact-head native artifact outer digests/size, source/version/RID/runtime, all seven package hashes, safe archive paths, inventories, bundled notices/source and sanitized receipts. Inspect app images and metadata. No personal app/account action is needed.
2. Record author self-review (not independent approval), merge the passing head under unchanged protection, and verify identical reviewed/merged trees.
3. Create draft `v1.40.7` against that merged revision. Upload seven packages, `SHA256SUMS.txt`, `VERIFICATION.json` and a privacy-reviewed evidence archive. Check every GitHub asset digest/size before publishing as latest; preserve previous releases.
4. Verify public downloads, receipts/evidence and all four README languages. Windows/Linux target1.40.7; Mac stays1.40.2 with original assets/checksums. The GitHub Release/receipt is the authoritative publication record. CI remains read-only without publisher/account/signing secrets.
