# Release 1.45.6

Windows folder measurements now use a private persistent SQLite index. Restarting Switch reuses completed directory/allocation work and replays NTFS change-journal records instead of enumerating unchanged trees. Interrupted first indexing resumes its checkpoints. Canonical file identities preserve allocated bytes, sparse/compressed files and per-drive hard-link deduplication.

Work runs in bounded cancellable slices under the existing32-batch-per-second/query-time budget. Hidden/minimized traversal stays paused. The per-drive spinner reflects actual uncached work and clears on slice completion, cancellation, failure and idle waits; tooltips show indexed folder/file counts. Index-owned writes remain physically accounted for without causing a self-indexing loop. Corrupt indexes stop visibly and an explicit rebuild preserves rejected cache files before replacement.

A drive lock explains missing journal access and requests Windows approval for a narrow selected-drive setup operation. The account app retains ordinary privileges and verifies ordinary access afterward. Cancellation/failure retain a usable action. Unsupported filesystems/remote journal APIs use explicit cached/manual-refresh states. [Behavior and limits](../FOLDER_MONITORING.md) / [index architecture](FOLDER_INDEX.md).

## Verification and delivery

Local acceptance passed72hardware/index contracts,179production WPF UI checks,100package self-tests and all nine595-message language packs. All694local installed files, owner data/bootstrap/settings and running Codex/backend processes were verified. Ordinary journal reads work on all three configured host local volumes after the approved D: setup. Personal trees still require their first visible index; a cached value or fixture is not a claim that a personal tree completed indexing.

Require all six strict protected checks on the final release head/current base, author COMMENT self-review identified as such, expected-head merge and reviewed/merged-tree equality. Verify nine native packages across five RIDs against source/version/runtime10.0.13, archive safety, licenses, Windows backend completeness, Mac staged installation/signatures and Linux payload hashes/modes. Publish the nine packages plus verification archive, VERIFICATION.json and SHA256SUMS.txt; verify all12server digests/sizes and anonymous full downloads. Preserve earlier public releases/tags.

The owner approved installing the released Windows package on host and VM, normal same-account Codex restarts and immediate removal of obsolete installed versions. Preserve original data/home/bootstrap protection identities; do not use fresh-enable --configure during upgrades. Verify100installed self-tests, stable preferences, actual status/settings RPC, newly loaded1.45.6backends/native children and catalog acknowledgment after restart. Delete old package directories only after all consumers exit and source/user-data/recovery checks complete. Keep credential/history stores and private rollback data outside the removal scope.

Windows/Linux remain unsigned; Mac is ad-hoc signed and not notarized. Linux remains a preview; this Windows index correction does not widen other platform support.
