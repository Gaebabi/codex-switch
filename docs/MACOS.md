# Mac setup

Current source1.30.3 uses .NET10 LTS with a reviewed macOS15+ support baseline and Codex desktop/CLI installed. Published1.27.6 bundles retain their older12.0 minimum; consult supported-OS policy rather than treating that old metadata as a support guarantee. [Build and install](INSTALL.md#mac)

Download [Apple Silicon or Intel packages](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6) without installing .NET or building source. Extract the complete ZIP, run `Install.command`, then open the installed app in `~/Applications` rather than the unsigned copy in the download folder. These packages are not Apple-notarized. If macOS blocks the installed app and you trust its source, use **System Settings → Privacy & Security → Open Anyway** for that app. [Apple's instructions](https://support.apple.com/en-us/102445)

- **⌘R / F5:** refresh
- **⌘W:** hide the window
- **⌘Q:** quit
- The menu-bar icon shows the active account's remaining quota and can reopen the window.
- Drag the title or empty caption area to move the window.

## Login and storage

Accounts stay in `~/Library/Application Support/CodexAccountDesk`; the existing directory and Keychain identity are preserved. A changed local app signature can cause a new Keychain authorization prompt. Complete it through macOS.

Account switching supports Codex's file credential storage. If Codex is configured for keyring or auto storage, set this at the top level of `~/.codex/config.toml` before switching:

```toml
cli_auth_credentials_store = "file"
```

Register accounts on the Mac. A Windows DPAPI vault cannot be copied to a Mac and decrypted there.

## Device limits

Unsupported GPU/VRAM readings appear as **—**. Apple Silicon unified memory is not presented as dedicated VRAM. Real login, account switching/chat resumption and Intel-device acceptance need native testing; a Windows cross-build does not verify them.

Developer checks and platform details: [architecture](ARCHITECTURE.md) / [contributing](../CONTRIBUTING.md).
