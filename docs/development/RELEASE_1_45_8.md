# Release 1.45.8

Windows discovery distinguishes the registered Codex desktop from its bundled CLI, including when the CLI remains running after the desktop closes. Explicit paths, downloaded CLI versions and standalone installations retain their selection order. Cancellation applies before discovery and after the desktop lookup returns.

Windows process observation reads the executable and start time from one process handle. Exited processes leave the running set, and a mapped drive can match the exact UNC path returned by Windows. Isolated account RPCs remove inherited workload identity parameters together so that a shell's federation settings cannot interrupt an account query.

## Acceptance

- Verify the reported startup failure before and after the change using the same signed Windows desktop package and an isolated home. Cover the installed desktop, bundled and standalone CLI, explicit paths, installation gaps, cancellation, concurrent discovery, shutdown and the next launch.
- Measure ordinary startup to usable controls. Record the first attempt, repeated launches and any delay or error the user would see. A second launch or manual recovery must be recorded as a failed first attempt.
- Exercise continuation with a disposable active chat. Confirm the original continuation identity is submitted once, including after a restart or ambiguous response, and verify that terminal failures leave an explicit state. Keep the owner's active chats and credentials outside these fixtures.
- Require the six protected CI checks, expected-head merge and exact reviewed-tree equality. Verify the nine existing platform packages, runtime and application versions, package contents and public checksums before publication.
- Install the complete published Windows package on CodexWork, preserving its data and home paths, credentials, settings and active Codex processes. Measure the installed editor's first startup and verify normal controls. Record the loaded backend version and any pending restart separately.
