# PR verification

[Verify](../.github/workflows/verify.yml) runs on PRs, pushes to `main` and manual dispatch. It uses read-only contents permission, SHA-pinned official actions, non-persistent checkout credentials and disposable hosted runners. No account/signing secrets, privileged fork events or release publication.

| Check | Host | Evidence |
| --- | --- | --- |
| Repository checks | Ubuntu 24.04 | UTF-8/local links/YAML, audit rejection tests, targeted tracked-tree scan and whitespace |
| Windows isolated checks | Windows 2022 x64 | Complete publish, internal checks, disposable SQLite/WAL byte preservation |
| Mac compilation / queues | macOS 15 arm64 and Intel | Architecture publish and disposable native SQLite/WAL checks |

These do not prove live login/switch/resume, Mac Keychain/GUI acceptance, notarization or Linux application support. Ubuntu runs repository tooling only. The workflow uploads no binaries or raw diagnostics.

Tool dependencies are pinned in [requirements-dev.txt](../requirements-dev.txt), SDK in [global.json](../global.json). Dependabot proposes Actions, Mac NuGet and Python updates for review. This is not a complete transitive lockfile or bit-for-bit reproducibility claim. [GitHub security guidance](https://docs.github.com/en/actions/reference/security/secure-use)

The public audit covers selected credential formats, private tracked paths, non-example emails, local checkout paths and media metadata, including YAML/TOML/editor settings. `--tree-only` explicitly omits history; default full audit retains reachable blobs/commit identity checks. Historical generic checkout paths remain detectable. Never report a tree pass as a full-history pass or suppress findings to make CI green.

Required-check branch protection/signing are separate repository settings. Future release workflows require separate publication/signing review. [Roadmap](PLATFORM_SUPPORT_PLAN.md)
