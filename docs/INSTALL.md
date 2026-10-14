# Build and install

Use an existing complete package, or build from this repository. Codex desktop/CLI must already be installed. Current source1.30.2 requires .NET10.0.4xx SDK and packages runtime10.0.13; published1.27.6 binaries remain unchanged.

## Windows

Run these commands from the repository root:

```powershell
.\scripts\build.ps1 -Verify
.\artifacts\win-x64\CodexSwitch.exe
```

`artifacts/win-x64` is the complete self-contained package. Keep the entire folder; an EXE alone is insufficient. An existing installed copy can be opened using its Codex Switch shortcut.

## Mac

Current source packages declare macOS15 or later, matching the reviewed .NET10 support baseline. Published1.27.6 bundles have the older12.0 minimum; that metadata is not a current support guarantee for an unsupported OS. Choose your processor:

```bash
bash scripts/build-macos.sh osx-arm64 # Apple Silicon
bash scripts/build-macos.sh osx-x64   # Intel
```

The output is beneath `artifacts/macos/<rid>/`.

1. Extract the complete ZIP.
2. Quit an existing Codex Switch with **⌘Q**.
3. Run `Install.command`, then open the installed **Codex Switch.app** from Finder.

The installer validates the new app and preserves the previous installation. Local builds use ad-hoc signing unless a Developer ID identity is configured. macOS may require its normal opening or Keychain authorization flow. [Mac details](MACOS.md)

## First use

Select **+**, complete browser sign-in, compare the cards and choose **Switch**. In Windows, right-click **↻** to import the current Codex login.

Download current [Windows x64, Mac Apple Silicon or Intel packages](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6). They include the .NET runtime; keep the complete folder/bundle intact. SHA256SUMS.txt lists the ZIP checksums. No automatic updater is configured. Mac packages are not notarized; use the normal macOS first-launch approval flow described in [Mac setup](MACOS.md).
