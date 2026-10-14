# Contributing

Code, docs, translations, accessibility, packaging and environment testing are welcome. English is the shared default; reports in other languages are welcome. A paid account, private screenshot or particular OS is not required.

Start with [the support roadmap](docs/PLATFORM_SUPPORT_PLAN.md), [architecture](docs/ARCHITECTURE.md) and [security](SECURITY.md). Search [issues](https://github.com/Gaebabi/codex-switch/issues) first. Vulnerabilities use [private advisories](https://github.com/Gaebabi/codex-switch/security/advisories/new).

## A focused contribution

Branch from `main` and reference an issue. Discuss switching/storage/platform changes before implementation; small docs fixes may go directly to a PR. Keep source moves, behavior changes and runtime upgrades reviewable separately. `Closes #...` means all that issue's acceptance criteria are satisfied.

Issues use type, area, priority and milestone; native GitHub open/closed state tracks status. [Issue and evidence conventions](docs/development/ISSUES.md). Include before/after screenshots for a change, or a confirmation screenshot for a new feature, plus commands/results and unrun checks. A planned issue shows the current baseline and labels post-implementation confirmation pending. Never represent a mockup as native acceptance. Respectful review is expected regardless of language, identity or desktop/distro preference.

## Setup and checks

Clone into any directory. The current SDK is **.NET 10.0.4xx**, with patch roll-forward in `global.json`. The [.NET 10 LTS migration](https://github.com/Gaebabi/codex-switch/issues/4) uses reviewed runtime10.0.13; full published folders and native app-window evidence are checked before promotion. Sources live in `src/`, shared visuals in `assets/` and `resources/`, catalogs in `localization/`, operational guides in `docs/development/`. [Build/install](docs/INSTALL.md)

Python 3.12 repository checks run on Windows, Mac and Linux:

```text
python -m pip install -r requirements-dev.txt
python scripts/verify-repository.py
python scripts/test-audit-public.py
python scripts/audit-public.py --tree-only
git diff --check
```

Stage new files before auditing: the tree scan reads tracked working files. The default audit also checks reachable history/commit identities. Earlier public history contains generic maintainer checkout paths; a tree pass does not certify that history. The scanner prints locations/types without sensitive matches and recognizes selected patterns only.

Windows:

```powershell
.\scripts\build.ps1 -Verify
python scripts/verify-queues.py --executable artifacts/win-x64/CodexSwitch.exe
```

Mac: `bash scripts/verify-macos.sh` runs isolated native checks. [Mac development](docs/development/MACOS.md) explains packaging/limits. Linux runs repository tooling only; **there is no supported Linux application build yet**. Docs/translation contributions do not need an app build on an unsupported OS.

Windows shutdown/handoff changes run the native disposable parent/child regression described in [operations](docs/development/OPERATIONS.md). It verifies the production lifecycle without stopping Codex or accessing real accounts/chats; synthetic credential/launch/resume endpoints do not certify native account acceptance.

## Editing and compatibility

Use UTF-8, `.editorconfig` and focused patches; avoid mass formatting. Preserve async account reads, credential revisions, unavailable-versus-zero behavior, existing data/key identities and read-only databases. Fix causes rather than weakening security or hiding failures.

UI catalogues are versioned JSON files in `localization/packs/`, indexed in `localization/index.json`, with stable English-based message IDs. Nine languages are bundled in source 1.28.0; partial local packs intentionally fall back to English. [Translator guide/schema/review policy](docs/LOCALIZATION.md). Run `python scripts/verify-localization.py` and the cross-platform localization test project before a translation PR. No Node or third-party translation service is needed to contribute.

Windows `python scripts/render-demo.py` renders a fictional screenshot from current UI components, using .NET10 and Python's standard library. It does not prove real switching. Inspect the result when UI changes. Platform PRs additionally require the published-app native window evidence from `--verify-ui`, validated by CI before export; a Windows render cannot establish Mac/Linux execution.

Temperature changes also run `dotnet run --project tests/temperatures/CodexSwitch.Temperatures.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false`. [Sensor/presentation contracts and capture modes](docs/TEMPERATURES.md) distinguish fictional layout inputs from actual read-only hardware observations. Do not install a privileged driver or relabel another component's temperature to make an unavailable sensor appear supported.

## Privacy and review

Use disposable storage/databases/test accounts. Never stop another person's Codex or switch their real account to test a PR. Report source/build/isolated/native results separately. Screenshots must contain no credentials, private chats, account identities or home paths: irreversibly replace those pixels, then blur the replacement if desired. Strip private metadata and inspect the final export; blur alone is insufficient.

PR CI uses read-only tokens, SHA-pinned actions and no signing/account secrets or release publication. [CI details](docs/CI.md). Dependabot proposes dependency updates for review; it does not merge them. Preserve [MIT](LICENSE) and [asset notices](licenses/Burn0Assets.LICENSE.txt).
