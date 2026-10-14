# Remaining work

Broader support is tracked in [#1](https://github.com/Gaebabi/codex-switch/issues/1) and the [platform roadmap](../PLATFORM_SUPPORT_PLAN.md). Linux implementation and core/platform extraction are follow-ups to repository cleanup.

Runtime4 is completed in merged PR27. Core3 proceeds through a single UI-neutral project first, then explicit native providers. Linux5/6 must meet Secret Service/private XDG storage, intended-client discovery, authenticated IPC and native same-chat recovery acceptance before packages7 are promoted. Current source1.30.1 has no Linux executable/account support.

- Native Mac acceptance: installation, Keychain reload, real login/switch/chat resumption and Intel-device checks.
- Native Mac temperature collection and sensor-provider/device coverage beyond the currently verified Windows GPU/storage host. [Current sources and unavailable behavior](../TEMPERATURES.md)
- Current Codex observer/schema compatibility: running, queue and goal changes/recovery on each platform.
- Credit-only depletion forecasting remains a separate pending implementation; the demo shows credit balance without claiming a credit ETA.
- Multi-computer account synchronization is a proposal, not an implemented service. [Review](../ACCOUNT_SYNC_REVIEW.md)

The owner resumed public distribution and authorized the 1.27.6 Release refresh. Developer ID signing/notarization remains incomplete; historical task details remain in Git history.
