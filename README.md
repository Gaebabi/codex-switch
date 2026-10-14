<p align="center">
  <img src="assets/macos/app-icon.png" width="88" height="88" alt="Codex Switch app icon" />
</p>
<h1 align="center">Codex Switch</h1>

Switch Codex accounts, automatically resume eligible unfinished local chats, and see what is left before you choose the next account. **Windows and macOS / 1.27.6**

[English](README.md) / [한국어](docs/README.ko.md) / [日本語](docs/README.ja.md) / [简体中文](docs/README.zh-CN.md)

![Fictional everyday demo: varied balances, stable resources and status colors](docs/media/demo.png)

*Fictional everyday data: different account balances, gentle resource fluctuations and normal/caution states. Rendered with the current Windows UI and rounded window frame.*

## Get started

**Download:** [Windows x64](https://github.com/Gaebabi/codex-switch/releases/download/v1.27.6/CodexSwitch-v1.27.6-win-x64.zip) / [Mac Apple Silicon](https://github.com/Gaebabi/codex-switch/releases/download/v1.27.6/CodexSwitch-v1.27.6-osx-arm64.zip) / [Mac Intel](https://github.com/Gaebabi/codex-switch/releases/download/v1.27.6/CodexSwitch-v1.27.6-osx-x64.zip)

[Release notes and checksums](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6). No source build or .NET installation is needed. Mac packages are not notarized; see [Mac setup](docs/MACOS.md).

**Linux:** planned; no Linux package yet. Follow the [support roadmap](docs/PLATFORM_SUPPORT_PLAN.md) and [issue #1](https://github.com/Gaebabi/codex-switch/issues/1). Contributions across languages, distributions and accessibility needs are welcome.

1. Use a complete Windows or Mac package. Windows: run `CodexSwitch.exe`. Mac: run `Install.command`, then open **Codex Switch.app**. [Build and install](docs/INSTALL.md)
2. Select **+** in the title bar to add accounts. On Windows, right-click **↻** to import the current Codex login.
3. Compare the cards and select **Switch**. Reads refresh every 60 seconds; **↻** or **F5** refreshes immediately.

Keep the full package together. Packaged apps need no .NET installation; building source requires the .NET 8 SDK and installed Codex desktop/CLI.

## What you can see

- Remaining quota, reset countdown, plan, subscription end date, reset credits and credit balance.
- Recent usage and a depletion estimate based on usage history. Open **History** from an account's **...** menu.
- Local running/queued/goal counts, resource trends and configurable drive free space. Hover a gauge for details.
- **Source 1.29.0:** right-aligned resource/disk temperatures with independent normal/caution/high colors when supported sensors are available; missing values show `—°C`. Device-reported limits take precedence over documented display defaults. CPU/RAM may require an existing compatible sensor provider. [Temperature support and scope](docs/TEMPERATURES.md)

The title bar has language and light/dark settings. **Source 1.28.0** adds nine UI languages (English, Korean, Japanese, Simplified/Traditional Chinese, Spanish, French, German and Brazilian Portuguese) and validated local JSON language packs. Current **1.27.6 downloads** still have English/Korean. [Language packs and translation contributions](docs/LOCALIZATION.md)

## Switch and resume

- **Back up before switching:** save active local chat names with a date prefix and verify them, then back up the current login before applying the selected account. Naming or normal-shutdown failures stop the login change.
- **Automatically resume unfinished work:** restart Codex, reopen the same chat IDs with their existing folders/models/permissions, and send one **Continue** to eligible idle chats. Already running or completed work is left alone.
- **Recover an interrupted handoff:** progress is checkpointed in the encrypted vault. Use **Resume chats** when a handoff remains pending; cancellation is available from the account menu/tray. Unclear sends are not automatically repeated.

**Switching restarts Codex** and can also affect CLI/IDE clients sharing the login. [Switching details](docs/SWITCHING.md)

## More

[Mac setup](docs/MACOS.md) / [Forecast](docs/FORECAST.md) / [Security](SECURITY.md) / [Contributing](CONTRIBUTING.md) / [Roadmap](docs/PLATFORM_SUPPORT_PLAN.md)

Developer reference: [Architecture](docs/ARCHITECTURE.md) / [CI](docs/CI.md) / [Operations](docs/development/OPERATIONS.md) / [Application comparison](docs/evidence/README.md).

This is independent open-source software, not an official OpenAI product. Credentials stay in a local encrypted vault. [MIT](LICENSE) / [Asset notices](licenses/Burn0Assets.LICENSE.txt)
