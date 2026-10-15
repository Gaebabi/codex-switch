# Linux desktop IPC trust

Source1.39.1/PR69 adds [actual local desktop-session acceptance](LINUX_DESKTOP_SESSION.md): the original application's default Quit command, rather than closing a window that a tray keeps alive, plus managed original-process listener readiness on same-home relaunch. Targeted XCB events retain native focus/window/original-peer checks and existing confirmation policy; no global keyboard state, force termination or credential change is used. Kernel listening socket inode/FD ownership precedes all existing path/mode/pidfd authentication, without deleting stale endpoints or generic connect retries. Ordinary lifecycle/metadata/thread-open/handoff integration and real account/one-Continue acceptance remain in5/6/7. The source1.38 read-only checkpoint below is historical.

Issue [62](https://github.com/Gaebabi/codex-switch/issues/62) is the read-only peer checkpoint within [5](https://github.com/Gaebabi/codex-switch/issues/5) and [6](https://github.com/Gaebabi/codex-switch/issues/6). Source1.38.0 adds the connection provider and a controlled native launch. Ordinary Linux account/handoff UI remains gated until normal lifecycle and authenticated acceptance pass. No supported Linux download is established by this checkpoint.

[Actual program before/after, original official-client confirmations and native16-check receipts](../evidence/linux-desktop-peer/README.md) bind codefc324761 to successful six-job CI on actual x64/ARM64 plus Windows/Mac. Final reviewed-head promotion is owned by PR63.

## Connection identity

The configured home and its `ipc` directory must be same-user0700 without symbolic links; the existing `ipc.sock` must be a same-user0600 native socket. Descriptor-relative traversal validates ancestors and anchors the connection to the opened directory. Socket identity is checked across connection. No directory, permission or stale socket is repaired or deleted during connection.

Before any application frame, `SO_PEERCRED` supplies the kernel UID/PID and `SO_PEERPIDFD` supplies a descriptor bound to the connected peer. The provider checks pidfd identity/liveness, original process start time, all process UIDs, absence of a tracer, and the retained root-owned exact native image. The approved Debian package/version/architecture and `app.asar` SHA256 are checked independently; a client-supplied version is insufficient. The peer is revalidated for stream reads/writes and refuses after the original process exits. Kernels without `SO_PEERPIDFD` cannot establish this capability; no numeric-PID-only fallback is used. [Linux Unix socket credentials](https://man7.org/linux/man-pages/man7/unix.7.html) / [process start time](https://man7.org/linux/man-pages/man5/proc_pid_stat.5.html).

Packaged entry arguments are restricted: native child/renderer roles, scripts, debugger flags and sandbox bypass flags are not admitted. Root-managed resources and same-user kernel identity are the trust boundary; this does not protect a user whose account, root, kernel or verified upstream application is already compromised.

## Native startup metadata

The actual pinned Chromium/Owl application normalizes its command name and clears its original environment region after startup. An empty `/proc/<pid>/environ` cannot establish the selected home or absence of loader/Node/Electron injection.

`LinuxDesktopLaunch` therefore opens the approved native executable and resources, launches through its retained execution descriptor with a small explicit desktop-session environment, and records the original PID/start/image/home in memory. No Node/Electron/loader overrides, API keys or access tokens are inherited. The observed canonical command name is admitted only with that original process receipt. A pre-existing peer without a receipt refuses: its erased environment cannot prove startup provenance, and no partial environment deny-list substitutes for a controlled launch. Merely finding an official executable never grants this missing provenance. A forwarded launch into an unrelated existing instance cannot substitute its PID. Receipts are not persisted or fabricated after an application restart.

The controlled launch does not terminate an existing client, turn off Chromium's sandbox, migrate credentials, or rewrite Codex policy. Normal intended-client close, relaunch and thread-open remain the next lifecycle checkpoint in6; a verification process receiving SIGTERM is diagnostic cleanup only.

## Native verification

Build `tests/linux-desktop/CodexSwitch.LinuxDesktop.Tests.csproj`, then on an explicitly disposable normal-user native worker:

```text
CODEX_SWITCH_DISPOSABLE_RUNNER=true python3 scripts/verify-linux-desktop.py --assembly <absolute-tests>/CodexSwitch.LinuxDesktop.Tests.dll --architecture X64 --report <fresh-private-directory>
```

Use `Arm64` only on actual ARM64. The harness owns a non-activating file-socket D-Bus, HOME/XDG paths and Xvfb display. Only public source/test/app/client inputs enter networkless containers. The dedicated desktop test container permits user-namespace creation using `seccomp=unconfined`; Chromium's own sandbox stays enabled. This applies only to disposable CI containers, which receive no personal files, Git credentials, host bus, signing or account secrets; the application never requests such a setting on a user's desktop.

Native contracts cover safe socket traversal/modes/collisions and byte preservation, same-user forged listeners receiving no initialization bytes, exact resource checksum refusal, arguments/environment restrictions and cancellation. The positive path uses the actual installed official sandboxed client, proves a framed initialize response, strips a real parent Node preload without executing its marker, captures only that original PID's visible native window, and refuses writes after its exit. A source-app own-XID capture separately establishes the utility UI. Metadata/hash/source receipts and visual inspection are required before publishing images.

No account, inference, chat rename/mutation, normal shutdown, active identity, account switch or Continue is exercised. These remain explicit acceptance work when an authorized disposable Linux account environment is available. Parent5/6/7 stay open.
