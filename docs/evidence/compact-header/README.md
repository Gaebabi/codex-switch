# Compact Linux header

The original1.42.3 images below retain the exact app-owned X11 window and direct-parent Xfwm4 frame, with fictional accounts and no pixel changes. [Original source/image hashes](before.json).

![English minimum-width baseline clips the automatic-switch caption](before-en-dark.png)

![Korean minimum-width comparison](before-ko-light.png)

At500×360, the native titlebar shares its remaining width with fixed actions. Equal star columns around the session counts waste space on the empty cancel side and constrain the automatic-switch label to a narrow remainder. The1.42.4 change uses content-sized cancel/automatic columns and the remaining space for counters, with smaller counter gaps. Minimum size,34px header height and account viewport remain unchanged. Native assertions require complete rendered caption/state height/width and no action/counter overlap.

Fresh actual x64/ARM64 confirmation in both languages/themes at500×360,600×420 and800×600 is pending. Mac/Windows regressions and protected final-head checks are separate gates. This does not establish supported Linux account handoff or authenticated continuation. [Issue115](https://github.com/Gaebabi/codex-switch/issues/115).
