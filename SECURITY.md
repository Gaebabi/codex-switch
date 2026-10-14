# Security and privacy

Codex Switch stores credentials locally: current-user DPAPI on Windows, AES-GCM with a Keychain-held key and private permissions on Mac. Inactive-account reads use isolated local Codex homes. There is no project-operated account server or analytics endpoint.

A switch changes the shared Codex login and normally restarts the desktop. Clients using the same home can see the new login. [Switching details](docs/SWITCHING.md)

## Keep private

Do not upload `auth.json`, account vaults, local databases, private logs or raw screenshots. Encryption at rest does not protect against software running as your user.

The documentation PNG is a fictional demo with reserved example addresses. Live `--record-dashboard` captures are unredacted and must stay in ignored private storage; do not publish them directly. Blur alone is inadequate redaction.

## Report a vulnerability

Use GitHub's private **Report a vulnerability** option when available. If it is unavailable, request a private contact through a minimal issue. Include no credentials, private chats or identifying account details.

[Open a private advisory](https://github.com/Gaebabi/codex-switch/security/advisories/new). Linux credential protection and authenticated switching are [planned](docs/PLATFORM_SUPPORT_PLAN.md), not currently available.

PR checks use read-only tokens, pinned actions and disposable fixtures without signing/account secrets or publication. [CI details](docs/CI.md). Targeted publication scans complement secret scanning/push protection; they cannot guarantee detection of every secret. Full-history findings remain separate from a tracked-tree pass.
