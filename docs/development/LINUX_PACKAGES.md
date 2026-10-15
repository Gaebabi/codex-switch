# Linux preview package candidates

Source1.40.0/PR75 candidates include [ordinary account and managed desktop integration](LINUX.md#source1400-ordinary-integration--issue74--pr75). They still declare `supportedAccountHandoff: false` until real authenticated acceptance passes; an implemented action is not a supported Release. The earlier source checkpoints below retain their capture-time scope.

Source1.39.1/PR69 fixes installed self-uninstall reporting found during [ordinary local desktop-session verification](LINUX_DESKTOP_SESSION.md): explicit UTF-8 standard-descriptor streams and the actual buffered JSON writer are prepared before removal; the actual retained-item count and success response are emitted only after completion, without first loading deleted Console/JsonSerializer dependencies. The installed executable succeeds with redirected output and a real PTY. The same session also verifies original-window application Quit with a tray and actual original-listener readiness on same-home relaunch. [Seven inspected actual program images and local native receipts](../evidence/linux-desktop-session/README.md) bind code dc320a86; final six-job CI/review/merge is recorded by PR69. The original1.39.0 checkpoint below retains its capture-time details.

Issue [66](https://github.com/Gaebabi/codex-switch/issues/66) / [PR67](https://github.com/Gaebabi/codex-switch/pull/67), within parent5/6/7. Source1.39.0 builds complete native **x64 and ARM64** `tar.gz`, `.deb` and `.rpm` candidates. These are unsigned source previews; ordinary account/handoff UI remains gated until authorized actual-account and desktop acceptance. They are not promoted Linux Release downloads.

[Actual installed-program confirmations, supported-OS comparisons and native source/runtime/hash/package receipts](../evidence/linux-installation/README.md) bind codefc548875 to all six passing CI jobs. Each architecture passes native controlled lifecycle18 and per-user544; all six fresh system targets pass9. Candidate artifacts are linked there; final reviewed-head promotion is recorded by PR67.

## Use a complete candidate

Keep the entire extracted tar folder. It contains its .NET10.0.13 runtime, application assemblies/native libraries, canonical1024px icon, licenses, LGPL libsecret source/patch/relink recipe and a package file inventory. No .NET, Python or source build is needed to run it. System C/C++/ICU/OpenSSL/Kerberos/zlib/GLib/libgcrypt/SQLite and X11/XCB/ICE/SM/fontconfig dependencies remain distribution-provided. Current framework/client use X11/XWayland; pure Wayland is a separate gate.

```text
./CodexSwitch
./CodexSwitch --package-info
./CodexSwitch --install-package
~/.local/share/codex-switch/current/CodexSwitch --uninstall-package
```

Run per-user operations as your normal desktop user, without sudo. `XDG_DATA_HOME` selects a different absolute share directory when configured. Its `codex-switch/versions/<version>` program folder is separate from existing `CodexAccountDesk` vault/settings and the selected Codex home. The menu uses the program's stable `current` link; icon integration uses the canonical PNG. A native atomic rename promotes the directory link. The root-managed `env -C` launcher avoids interpreting paths as shell code; the desktop entry uses both required escaping layers, including literal percentage characters. [Desktop entry command rules](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html).

The installer checks the complete manifest, ownership, permissions, ancestor links and existing integration before copying/promoting files. Cross-process file locking serializes package operations. Updates retain earlier complete versions. Modified or unmanaged integration is never overwritten; uninstall verifies recorded payload before removing it, retains user-added/modified files, and refuses while another installed Codex Switch utility process is running. It never stops native Codex, opens/decrypts/migrates account/vault/keyring data or recursively deletes user folders. Invalid relative XDG paths, overlapping Codex-home/program roots and unsafe symlink/collision paths refuse. Remove system packages with their package manager instead of the per-user command.

For `.deb`: `sudo apt install ./CodexSwitch-v<version>-preview-linux-<architecture>.deb`, then launch **Codex Switch** from the menu or run `codex-switch`. Remove with `sudo apt remove codex-switch`. For `.rpm`: use `sudo dnf install ./<candidate>.rpm` and `sudo dnf remove codex-switch`. System payload lives under `/opt/codex-switch`; there are no package maintainer scripts or account/service migration operations. Configured Secret Service providers are not installed/replaced automatically.

## Build and verify

Use an actual declared native Linux runner and a clean committed checkout. `scripts/build-linux.sh` publishes the self-contained app/private native secret library. Package tools additionally require `rpm`/`rpmbuild` and `dpkg-deb`:

```text
python3 scripts/package-linux.py --app artifacts/linux-x64 --rid linux-x64 --output artifacts/linux-packages
```

Replace `linux-x64` with `linux-arm64` only on actual ARM64. The builder verifies runtime/project completeness and actual application version/architecture, rejects private-data inputs, inventories every payload file and includes restored public dependency license notices. Outputs contain three package formats, `SHA256SUMS`, `RUNTIME.json`, `DEPENDENCIES.json`, `SBOM.spdx.json` and `PROVENANCE.json`. Source SHA, build time from that commit, runtime, native architecture and artifact bytes/hashes are recorded. SBOM/checksums are inventory/integrity aids, not a signing or publisher-authenticity claim. An explicit same-source1.38.99 compiled package exists only to exercise ordinary upgrades; it is never uploaded as a Release candidate.

The app loads its bundled HarfBuzz text engine in a private ELF binding scope before the UI framework. This prevents system HarfBuzz functions with a different internal object layout from handling bundled objects. It does not use a loader environment override, replace a system library or change Windows/Mac loading. Native installed-app captures exercise actual text shaping instead of treating successful package metadata inspection as GUI acceptance.

`Linux native` builds both actual architectures and retains all existing provider/client/SQLite/Windows/Mac gates. `verify-linux-packages.py` uses the digest-pinned Ubuntu24.04, Debian13 and Fedora44 images in `linux-package-images.json`, with no global .NET host/SDK/runtime. Each case installs the actual system package after owned marker data exists, launches the complete installed app and captures only its own native windows, then removes the package and checks marker preservation. A separate normal-user lifecycle runs ordinary native install/update/uninstall, cross-process lock, modified/unmanaged preservation, Unicode/special-character desktop parsing/launch and installed own-window UI checks. Raw diagnostics/fixture homes stay private. Public exports require exact-source/runtime/hash/privacy verification and visual inspection.

These are **fresh minimal containers with X11/Xvfb**, not complete GNOME/KDE sessions or VMs. Actual accounts, active identity, chat naming/IDs/folders/models/permissions, one eligible Continue, recovery/cancellation, XWayland/pure-Wayland, scaling/accessibility and broader desktop/distro acceptance remain distinct5/6/7 work. No automatic cross-home/OS credential copying, new signing credentials or trusted Release publication is introduced.
