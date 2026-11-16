# Release 1.45.4

Windows folder monitoring no longer repeats complete directory-tree scans every minute. It retains directory allocation snapshots, reuses unchanged root readings and recalculates only affected drives after filesystem notifications. Large HDD archives and build trees therefore avoid repeated metadata reads when another tracked folder changes.

Folder allocation traversal pauses while the main window is hidden or minimized. Opening the window resumes retained measurements and pending changes; the required first measurement can still perform disk I/O while the window is visible. Lightweight capacity and disk-I/O counters continue in the background. Actual allocated storage, sparse/compressed files, per-drive hard-link deduplication, reparse exclusion, explicit missing/partial states and cancellation remain supported. [Folder monitoring contract](../FOLDER_MONITORING.md).

## Verification and delivery

The hardware/folder suite passes52checks, including65seconds without an idle rescan, zero unchanged-root queries, one-directory invalidation, hard-link growth, rename/replacement, real filesystem notifications, hidden startup, active cancellation and resume. Both locally installed complete Windows packages pass100self-tests. A quiet editor-on/off/restart comparison observed zero volume reads and average activity below1%; longer observations contained concurrent VM workloads and do not establish permanent whole-disk idleness.

Require all six protected checks on the exact release head/current base, native application before/after evidence, reviewed-tree equality after merge and nine source/runtime-verified packages. Publish checksums and provenance, then verify anonymous downloads. Preserve existing releases and active owner Codex/backend processes. Public delivery does not restart an already running client.

This performance correction is Windows-specific. macOS/Linux retain their existing interfaces and support boundaries. Windows/Linux packages are unsigned; Mac bundles are ad-hoc signed and not notarized. Linux remains an integration preview.
