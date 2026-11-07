# Codex Switch1.40.2 native window gallery

These are actual application windows from source2d9940ab3a324fdaabe0aeb6d48b94117c9e1501, verified in native [Windows CI](https://github.com/Gaebabi/codex-switch/actions/runs/38924644870) and the [ordinary Linux desktop session](https://github.com/Gaebabi/codex-switch/actions/runs/38924645195). [Source/runtime/image hashes and presentation receipt](gallery.json).

![Actual Windows WPF program window](windows.png)

Windows uses the real native WPF program with explicitly fictional demo accounts. It does not capture an owner account or personal desktop. Resources are unavailable in this isolated UI probe.

![Actual installed Linux program with original Xfce titlebar](linux.png)

Linux uses the ordinary per-user installed app with an empty account home, actual Xfce/xfwm4 titlebar and real GNOME Keyring. This current-source native x64 session passes53 checks, including automatic initialization after keyring unlock without app input. It is isolated Docker userspace, not a booted VM/GNOME/KDE/Wayland/ARM64 desktop or authenticated active-chat continuation claim. [Startup before/after and separate owner-authenticated15/16-check scope](../../evidence/automatic-startup-recovery/README.md).

Both PNGs preserve original window pixels at1:1 on a centered1000×800 solid `#18202b` canvas. Original titlebars and controls are retained; no desktop/cursor pixels or PNG metadata are included. The exports were visually inspected. The publication's final source/merged tree/native package identities belong to its Release verification receipt; these images retain their exact capture source above.

**MacBook real-device screenshot: reserved for the maintainer.** Add `macbook.png` with its actual original titlebar, app/OS/architecture/source information, privacy inspection and image hash before embedding it. Do not substitute another OS, mockup or native CI frame for the owner's MacBook image.
