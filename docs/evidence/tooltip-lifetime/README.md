# Tooltip style lifetime

Tracks [issue35](https://github.com/Gaebabi/codex-switch/issues/35) and [PR36](https://github.com/Gaebabi/codex-switch/pull/36). Synthetic accounts and app-owned UI only; no private account, chat, credential, home path or personal desktop is captured.

| Before: dark window, system tooltip template | After: dark window, custom tooltip template |
| --- | --- |
| ![Actual WPF tooltip before](before/en-dark-tooltip.png) | ![Actual WPF tooltip after](after/en-dark-tooltip.png) |

| Before: light window, system tooltip template | After: light window, custom tooltip template |
| --- | --- |
| ![Actual WPF tooltip before in light mode](before/en-light-tooltip.png) | ![Actual WPF tooltip after in light mode](after/en-light-tooltip.png) |

These four images render the actual WPF tooltip control created by the production framework popup service at the replaced-owner boundary. They are program UI components, not mockups. They do not claim that a removed control's popup remains visible: the framework can close an obsolete popup. The test observes and renders its selected template; native main-window captures are separate below.

Account refresh replaces card controls. Previously the tooltip's implicit style depended on the old owner's window resources; after removal a delayed popup could no longer resolve that style and selected the system template. Installing the same style in application resources preserves the theme throughout popup creation/teardown, without replacing the popup service or altering callbacks, quota data, positioning or animation. Existing explicit resource tooltips keep their instances/styles during updates.

| Native Windows before | Native Windows after |
| --- | --- |
| ![Own HWND app before](before/en-dark-800x560.png) | ![Own HWND app after](after/en-dark-800x560.png) |

Before production source: `e92b8fe49bf9d4e5beeb54df6a2539a8a7e073b9`, app1.30.2. An isolated archive adds capture-only diagnostics that assert the observed missing style/system template; original production sources are unchanged. After: `386cde7cc6136903a1c5ce7cea9118550c4b2046`, app1.30.3. Both use the same local native host, WPF software evidence rendering and .NET10.0.13. [Before selected-image receipt](before/evidence.json) / [after selected-image receipt](after/evidence.json). Metadata is stripped and dimensions/SHA256 are verified.

Repeated resource-gauge hovers and active updates were also checked privately on .NET8.0.30 and .NET10.0.13; normal resource tooltips retained their custom templates. The permanent Windows native app checks use the real WPF popup service with connected/replaced account owners, both themes, four resource gauges and a synthetic disk, preserving tooltip identity/template across updates. This establishes the reproduced style-scope defect, not every possible intermittent display cause.

| Native macOS15 Apple Silicon | Native macOS15 Intel |
| --- | --- |
| ![Native Mac arm64 app](mac-arm64/en-dark-800x600-native.png) | ![Native Mac Intel app](mac-x64/en-light-800x600-native.png) |

Capture checkpoint `386cde7cc6136903a1c5ce7cea9118550c4b2046` passes all four jobs in [run38638804892](https://github.com/Gaebabi/codex-switch/actions/runs/38638804892), including Windows internal93, SQLite23, disposable handoff5, resource/tooltip UI checks and native Mac UI21 on both architectures. [Apple Silicon receipt](mac-arm64/evidence.json) / [Intel receipt](mac-x64/evidence.json). Mac tooltip implementation is unchanged. Its images verify native bootstrap/layout regression, not this WPF-specific tooltip correction.

The linked PR records final complete-head CI and author self-review before promotion. Public binaries remain1.27.6 and the user's existing installation is preserved; no account switch, Codex restart or Release is performed.
