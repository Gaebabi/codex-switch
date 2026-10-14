# Repository and recovery

Use public [Gaebabi/codex-switch](https://github.com/Gaebabi/codex-switch), baseline `main`, cloned into any directory. [Release 1.27.6](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6) remains the current binary delivery.

The old codex-account-desk local/GitHub repository was removed after consolidation. Recovery bundles/snapshots are private ignored material; do not publish them or treat them as a second source.

```text
git bundle verify <bundle>
git clone <bundle> <separate-recovery-directory>
```

Source layout is independent of installed apps and account storage. Preserve user data/credentials. [Install](../INSTALL.md) / [Operations](OPERATIONS.md) / [Status](STATUS.md)
