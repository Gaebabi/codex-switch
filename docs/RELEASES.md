# Binary releases

Current version: **1.27.6**. [GitHub Release](https://github.com/Gaebabi/codex-switch/releases/tag/v1.27.6) contains self-contained Windows x64, Mac Apple Silicon and Mac Intel ZIPs plus SHA256SUMS.txt. Users do not need a source build or .NET installation; Codex desktop/CLI is required.

Extract the entire Windows folder and run `CodexSwitch.exe`. On Mac, extract the complete ZIP, run `Install.command` and open the installed `Codex Switch.app`. Mac cross-published packages are unsigned; installation applies local ad-hoc signing. They are not Apple-notarized and can require app-specific first-launch approval. [Mac setup](MACOS.md)

The release includes the latest responsive UI, white-filled scalable icon, compact titlebar account/refresh controls, centered activity counters, asynchronous quota/history/resource/drive displays and shared Windows/Mac account-switch core. Switching backs up the current login, restarts Codex and reopens the same local chats, automatically sending one **Continue** to eligible idle continuations. [Handoff details](SWITCHING.md)

Windows isolated checks and disposable SQLite/WAL checks are distinct from actual account switching. Mac cross-publish/architecture/ZIP checks do not establish native login, switching/resumption or Intel-device acceptance. [Remaining acceptance](development/BACKLOG.md)

Packages include complete runtimes, MIT and asset notices; exclude credentials, vaults, private diagnostics, source fixtures and recovery archives. SHA256SUMS.txt covers the three ZIPs. Source commit/tag and uploaded GitHub digests must match the published build. Publication is manual; automatic updates and CI release publication are not configured.
