# CPU module provenance

Unmodified signed `AMDFamily17.bin` and `IntelMSR.bin` from [official PawnIO.Modules 0.1.6](https://github.com/namazso/PawnIO.Modules/releases/tag/0.1.6), commit `4aa792beb020a14c8261072e9786d2dfb38489d9`. Corresponding module sources and build files are in `source/` in this repository (`module-source/` in the published worker folder); see `PawnIO-Modules.LGPL-2.1.txt` in the package or [the repository license](https://github.com/Gaebabi/codex-switch/blob/main/licenses/PawnIO-Modules.LGPL-2.1.txt). The original compiler flags are retained in `BUILD.txt`; this flattened source folder uses `-i.` instead of `-iinclude`.

| Signed module | SHA-256 |
| --- | --- |
| AMDFamily17.bin | cd59598344b54a23178ccf9223239f60a3111bd86ff2c7bea2c9d1cef89e095b |
| IntelMSR.bin | f3ab69f0a2686813de2f47efcda6271e176c4ee50fab5f107254f7d6a4e77361 |

Copyright namazso and contributors, LGPL-2.1-or-later. These modules remain separate embedded libraries, not MIT relicensed code. Users may inspect/rebuild the worker, relink replacement modules, and reverse engineer it for debugging modifications under that license; official PawnIO enforces upstream module signatures. No account code or third-party hardware monitor application is linked. Only fixed read entry points/registers are used, even though the upstream modules also contain other capabilities. No write/control entry points are exposed through the worker.

The optional official PawnIO 2.2.0 driver installer is **not redistributed**. On explicit user consent the app downloads the fixed [upstream release](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0), verifies SHA-256 `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`, then requests Windows elevation. The inspected release has valid Authenticode from namazso. This pin is not a promise that future driver releases are safe; updates require a fresh review. No driver install, UAC prompt, security-policy change or network request happens automatically.
