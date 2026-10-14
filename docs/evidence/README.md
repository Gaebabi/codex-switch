# Application screenshots and verification

Related: [roadmap #1](https://github.com/Gaebabi/codex-switch/issues/1), [foundation #2](https://github.com/Gaebabi/codex-switch/issues/2), [PR #8](https://github.com/Gaebabi/codex-switch/pull/8).

The comparison shows the application's actual Windows WPF `MainWindow`/account/resource components, built and rendered from the baseline and reorganized sources with the same isolated fictional inputs. These are application UI images, not repository pages. They are synthetic rendering evidence, not screenshots of live account switching.

| Before: source `0ed757e` | After: source `0545810` |
| --- | --- |
| ![Application UI before source reorganization, fictional accounts](app-before.png) | ![Application UI after source reorganization, same fictional accounts](app-after.png) |

Both exports are 800×560 lossless PNGs, 73,560 bytes and byte-identical. The source move preserves the program's UI. No real identity, private chat, credential or home path is present. The displayed addresses use reserved `example.invalid`; raw personal pixels never enter these exports. Final metadata is inspected. A real-account capture must replace private pixels before blur and must never upload originals.

Git inspection confirms **70 → 11 tracked root files**, 88 source/resource/doc moves and all **63 C# files byte-preserved**. Project/build/resource/link paths match the new structure; product version, executable/namespace, data/key and IPC identities remain unchanged.

## Issue-specific evidence boundary

| Issue | Current application evidence | Required before completion |
| --- | --- | --- |
| [#1 roadmap](https://github.com/Gaebabi/codex-switch/issues/1) | Existing program baseline above | Native Linux functionality and download-ready release confirmation |
| [#2 foundation](https://github.com/Gaebabi/codex-switch/issues/2) | Before/after program UI preserved | Repository checks, architecture builds, isolated tests, PR CI and review |
| [#3 core/platform projects](https://github.com/Gaebabi/codex-switch/issues/3) | Existing program before/after repository organization | After-extraction application screenshots and regression evidence |
| [#4 runtime](https://github.com/Gaebabi/codex-switch/issues/4) | Existing Windows program | Upgraded application/runtime package and regression confirmation |
| [#5 Linux credential/client trust](https://github.com/Gaebabi/codex-switch/issues/5) | Existing supported program | Actual Linux app key-provider states and lock/unlock/restart/tamper tests |
| [#6 Linux handoff](https://github.com/Gaebabi/codex-switch/issues/6) | Existing supported program | Actual Linux switch/recovery/same-chat continuation screenshots and native results |
| [#7 package/coverage](https://github.com/Gaebabi/codex-switch/issues/7) | Existing supported program | Installed Linux app confirmation and clean-machine lifecycle/environment evidence |

Unimplemented features explicitly have pending application confirmation. No repository screenshot or mockup can close a functional acceptance gate. Invisible security/storage behavior also requires automated/native results; a dashboard alone cannot prove it.
