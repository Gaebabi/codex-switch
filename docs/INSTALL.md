# Build and install

Download complete [1.42.2 Windows/macOS packages and Linux previews](https://github.com/Gaebabi/codex-switch/releases/tag/v1.42.2), or build from this repository. Packages include .NET10.0.13; a global .NET installation is unnecessary. Building requires .NET10.0.4xx SDK. Codex desktop/CLI must be installed separately.

## Windows

Run these commands from the repository root:

```powershell
.\scripts\build.ps1 -Verify
.\artifacts\win-x64\CodexSwitch.exe
```

`artifacts/win-x64` is the complete self-contained package. Keep the entire folder; an EXE alone is insufficient. An existing installed copy can be opened using its Codex Switch shortcut.

Source1.40.5 builds additionally require the Windows NativeAOT prerequisites (Visual Studio C++ desktop build tools and Windows SDK); hosted Windows CI supplies them. Use `scripts/build.ps1` so the worker is published first and its exact hash is embedded in the account app. A direct account-project publish does not create a complete CPU-capable package. Keep `sensors/` with its executable and source/license files. The visible CPU download/authorization button is optional and requires explicit driver/worker approval; no separate monitor app is needed. [CPU setup and limits](TEMPERATURES.md#built-in-windows-cpu-sensor).

## Mac

Current packages require macOS15 or later. Choose Apple Silicon or Intel to match your Mac. For a source build:

```bash
bash scripts/build-macos.sh osx-arm64 # Apple Silicon
bash scripts/build-macos.sh osx-x64   # Intel
```

The output is beneath `artifacts/macos/<rid>/`.

1. Extract the complete ZIP.
2. Quit an existing Codex Switch with **⌘Q**.
3. Run `Install.command`, then open the installed **Codex Switch.app** from Finder.

The installer validates the new app and preserves the previous installation. Local builds use ad-hoc signing unless a Developer ID identity is configured. macOS may require its normal opening or Keychain authorization flow. [Mac details](MACOS.md)

## Linux preview

Release1.42.2 includes complete unsigned x64/ARM64 tar.gz/deb/rpm integration previews. [Download/use/build/native verification](development/LINUX_PACKAGES.md). DEB/RPM install system dependencies through the package manager; tar packages include a per-user installer. An existing supported Secret Service provider is required and is never replaced automatically. Full desktop/authenticated handoff acceptance remains separate from UI/package checks. [Requirements and pending gates](development/LINUX.md)

Desktop integration pins official OpenAI Linux client26.1007.71326; newer versions require compatibility review. The selected context must already use file credentials. Start Codex through the utility before working. Explicit-checksum CLI-only mode provides account/usage actions without desktop resumption. [Local desktop-session scope](development/LINUX_DESKTOP_SESSION.md)

## First use

Select **+**, complete browser sign-in, compare the cards and choose **Switch**. In Windows, right-click **↻** to import the current Codex login.

If sign-in automatically reuses the previous browser account, open the new authorization request in a separate browser profile/private window. Never paste authorization URLs, tokens or login codes into an issue.

Release1.42.2's SHA256SUMS.txt covers its nine native-built Windows/macOS/Linux packages and verification attachments. No automatic updater is configured. Mac packages are not notarized; use the normal first-launch approval flow in [Mac setup](MACOS.md). Existing releases remain available; current documentation describes current requirements.
