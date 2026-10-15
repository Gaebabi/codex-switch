# Native Linux client inspection and application comparison

Tracks [issue54](https://github.com/Gaebabi/codex-switch/issues/54), [PR56](https://github.com/Gaebabi/codex-switch/pull/56) and parent5. Source1.34.0 adds native binary/discovery leases and effective configuration/requirements inspection in the explicitly selected home context. This read-only checkpoint does not enable normal Linux account/RuntimeLocator/lifecycle/IPC/handoff actions, attest all Electron resources or complete project/runtime-profile-aware account integration. KWallet55, full native handoff6 and clean packages7 remain separate gates.

## Actual program before and after

| Linux x64 before: source1.33.1 | Linux x64 after: source1.34.0 |
| --- | --- |
| ![Linux x64 before](../linux-sqlite/linux-x64/en-dark-800x600-native.png) | ![Linux x64 after](linux-x64/en-dark-800x600-native.png) |

| Linux ARM64 before | Linux ARM64 after |
| --- | --- |
| ![Linux ARM64 before](../linux-sqlite/linux-arm64/en-light-800x600-native.png) | ![Linux ARM64 after](linux-arm64/en-light-800x600-native.png) |

| Windows before | Windows after |
| --- | --- |
| ![Windows before](../linux-sqlite/windows/en-dark-800x560.png) | ![Windows after](windows/en-dark-800x560.png) |

| Mac Apple Silicon before | Mac Apple Silicon after |
| --- | --- |
| ![Mac ARM64 before](../linux-sqlite/mac-arm64/en-dark-800x600-native.png) | ![Mac ARM64 after](mac-arm64/en-dark-800x600-native.png) |

| Mac Intel before | Mac Intel after |
| --- | --- |
| ![Mac Intel before](../linux-sqlite/mac-intel/en-light-800x600-native.png) | ![Mac Intel after](mac-intel/en-light-800x600-native.png) |

Before images bind sourcea726ab5/app1.33.1. All ten selected after PNGs bind exact source `b01be7c5ed8fa1e817b9269393f6c86a79820aa0`/app1.34.0: [Linux x64](linux-x64/evidence.json), [Linux ARM64](linux-arm64/evidence.json), [Windows](windows/evidence.json), [Mac ARM64](mac-arm64/evidence.json) and [Mac Intel](mac-intel/evidence.json). Each includes a minimum-size image. All ten are visually inspected, metadata stripped and dimensions/SHA256 verified. No personal account, home, chat, pointer or desktop is present; profiles and the Mac alias are authored synthetic data. Same-host Windows200% dark before/after is byte-identical.

These are actual self-contained application windows. Native Linux Ubuntu24.04 x64/ARM64 uses X11/Xvfb/software rendering and captures only the owned XID. Each architecture passes12 EN/KO, dark/light and500×360/600×420/800×600 images with geometry/text/scroll assertions. Native macOS15 arm64/Intel uses software evidence rendering and owns its window; Windows captures its own HWND. The new inspection is headless, so images prove source UI bootstrap/regression rather than native binary/policy behavior. Independent actual-client checks below establish that nonvisual behavior. Full GNOME/KDE/Wayland, real accounts, running-peer trust and handoff are not claimed.

## Actual native client and policy evidence

Sourceb01be7c passes all four [Verify jobs38676489809](https://github.com/Gaebabi/codex-switch/actions/runs/38676489809) and both [Linux native jobs38676489811](https://github.com/Gaebabi/codex-switch/actions/runs/38676489811). Each native Linux architecture passes94 client assertions, including14 executable-trust contracts. The checksum-pinned official ChatGPT26.1007.71326 package is installed normally on an explicitly disposable native runner; its root-managed metadata, desktop ELF and embedded CLI0.162.0-alpha.17.2 are checked. The independently locked official standalone CLI0.162.1 also passes the explicit-checksum/CLI-only path. No personal installed app is used.

Native trust checks cover actual approved package checksums and retained descriptors, explicit CLI-only selection, mismatch/no fallback, user-managed system paths, final/intermediate links, readable foreign ownership, writable file/ancestor, wrong architecture/class/truncated ELF/script, FIFO refusal/type preservation, setuid/setgid/file-capability refusal, changed metadata, disposed leases and cancellation. Pinned scripts cannot execute even when their checksum matches the explicitly selected input. Native process creation through the retained descriptor is exercised by the real CLI.

Ordinary configuration/requirements RPCs exercise file/keyring/auto/ephemeral, user-over-system precedence, managed-keyring precedence over user file settings, forced API login, managed login restrictions, workspace restriction, explicit/standalone provenance and deprecated-profile refusal. Config, empty-auth input and each test-created system-policy file are byte-identical before/after. Reports are0600, exclude personal paths/config/workspace IDs and leave desktop handoff unavailable. Tests refuse pre-existing system policies and remove only their verified, self-created policy files. Full raw config, temporary homes, package/provider state and key material are not uploaded. Empty-auth/config fixtures do not prove authenticated account behavior.

Existing native Linux GNOME18, SQLite/WAL26, protocol10 and source-app12 remain green per architecture. Windows93/SQLite23/handoff5/protocol9 and native Mac SQLite23/protocol9/UI21/key16 pass with existing catalogue/temperature/client/hardware/composition checks. Complete self-contained runtime/project dependencies, local protocol9, native-test/Linux/Windows compilation and scoped document/catalogue/privacy checks pass. [Implementation and exact limits](../../development/LINUX.md#trusted-client-inspection--source1340).

PR56 owns final complete-head six-job CI, author self-review and expected-SHA promotion for this read-only checkpoint. Issue54/parent5 retain the remaining integration/context gates; normal Linux account actions remain disabled. Public binaries remain1.27.6. No real login/inference/switch, personal home/keyring/live Codex, installed utility replacement, history rewrite (#29), new Release or signing is involved.
