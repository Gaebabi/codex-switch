# Windows account warning placement

Issue99 corrects the Windows footer and account header in source1.41.1. Published1.40.7 is unchanged. The disk monitor previously shared a grid cell with global status and was explicitly hidden whenever any warning was set. A refresh error on one account therefore removed unrelated resource information.

## Delivery and acceptance

1. Give disk readings and global operation status separate footer rows. Keep hardware/disk controls visible during account failures, initialization failures and switch warnings, including500×360.
2. Put a compact vector warning button beside each affected account name. Reuse existing localized refresh/error messages and one themed tooltip lifecycle for hover/click. Hover opens a transient explanation; click pins it until a second click, Escape, focus loss or window deactivation. Support keyboard access, wrapped text and closing detached controls. Healthy cards have no warning; a successful refresh removes only its own warning.
3. Retain cached usage and ordinary Switch/menu actions. Account details stay with that account; global initialization/switch failures keep their separate status. No authentication, storage, sensor privilege or account-switch algorithm changes.
4. Capture original and corrected native Windows app/popup HWNDs with identical fictional inputs on a disposable hosted Windows worker. Never upload the owner's screenshot, account names, desktop or credentials. Preserve original production-source hashes for the external baseline and actual image/source/metadata receipts.
5. Run all six exact-head protected checks, inspect screenshots/artifacts, record author COMMENT review and merge the expected head. Stage the complete verified Windows package and shortcut for the owner's next normal restart without stopping the active app/sensor session or displaying a local window. No new public binary Release.

[Actual native before/after,42 corrected assertions and source/hash receipts](../evidence/account-warnings/README.md) are inspected at captured sourced376f0c. Final native CI additionally checks the global warning row at minimum size. Final review and private package staging are tracked in [issue99](https://github.com/Gaebabi/codex-switch/issues/99) and its linked PR. Windows WPF is the affected surface; existing Mac/Linux shared desktop behavior and Linux CLI commands are preserved.
