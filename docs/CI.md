# PR verification

[Verify](../.github/workflows/verify.yml) runs on PRs, pushes to `main` and manual dispatch. It uses read-only contents permission, SHA-pinned official actions, non-persistent checkout credentials and disposable hosted runners. No account/signing secrets, privileged fork events or release publication.

| Check | Host | Evidence |
| --- | --- | --- |
| Repository checks | Ubuntu 24.04 | UTF-8/local links/YAML, audit rejection tests, targeted tracked-tree scan and whitespace |
| Windows isolated checks | Windows 2022 x64 | Complete publish, internal checks, disposable SQLite/WAL byte preservation |
| Mac compilation / queues | macOS 15 arm64 and Intel | Architecture publish and disposable native SQLite/WAL checks |

These do not prove live login/switch/resume, Mac Keychain/GUI acceptance, notarization or Linux application support. Ubuntu runs repository tooling and portable production contract tests only. The workflow uploads no binaries or raw diagnostics.

Language packs additionally run structural/source-key checks and the production parser/culture/installation test project on all three hosts. Nine built-in catalogues are complete; custom-pack rejection, English fallback, integer plural rules, UTF-8/BOM, restart reload and RTL culture are exercised. [Localization guide](LOCALIZATION.md)

Temperature contract checks run on all three hosts: production storage parsing, volume/device/channel identity, partial coverage, invalid/unavailable/stale readings and multilingual formatting. Hosted CI does not claim physical temperature sensors. [Temperature support](TEMPERATURES.md)

Tool dependencies are pinned in [requirements-dev.txt](../requirements-dev.txt), SDK in [global.json](../global.json). Windows checks require the actual official Codex CLI for app-server login cancellation and isolated credential-switch initialization; CI installs version 0.162.1 from the [locked development prerequisite](../tools/ci-codex/package.json), with lifecycle scripts disabled and its exact native executable path selected. No desktop install or successful browser authentication is required. Those checks do not fake the app-server protocol or use a real account.

Dependabot proposes Actions, Windows/Mac NuGet, Python and the locked CLI prerequisite updates for review. This is not a complete lockfile of every project dependency or a bit-for-bit reproducibility claim. [GitHub security guidance](https://docs.github.com/en/actions/reference/security/secure-use)

The public audit covers selected credential formats, private tracked paths, non-example emails, local checkout paths and media metadata, including YAML/TOML/editor settings. `--tree-only` explicitly omits history; default full audit retains reachable blobs/commit identity checks. Historical generic checkout paths remain detectable. Never report a tree pass as a full-history pass or suppress findings to make CI green.

Required-check branch protection/signing are separate repository settings. Future release workflows require separate publication/signing review. [Roadmap](PLATFORM_SUPPORT_PLAN.md)
