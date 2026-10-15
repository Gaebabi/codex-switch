# Owner-operated Linux desktop acceptance

[Issue76](https://github.com/Gaebabi/codex-switch/issues/76), within #5/#6/#7. Source1.40.0/code d0893e5a, original approved upstream26.1007.71326, actual native x64 Debian13/Xfce/X11 userspace on the owner's Docker Desktop. [Interactive recipe and isolation](../../development/LINUX_INTERACTIVE_DESKTOP.md).

![Actual installed application before the Linux title ownership correction; all private content removed](before.png)

The owner directly created a password-protected native default keyring and authenticated one account in Linux Firefox. No Windows credentials, host browser profile or host home was mounted or copied. A bounded private read-only probe through the production vault/client/protocol/usage assemblies passed [eight semantic checks](one-account.json): existing CADL1 encryption, one registered identity, matching active credentials, actual upstream file policy, native `account/read`, authenticated `account/rateLimits/read` parsed by the production usage model, original-key reopening and unchanged active identity. The receipt exports no identity or upstream payload; it is local acceptance evidence, not an authenticated public-CI test.

Only **one** account is available. Cross-account switching, original chat settings and one eligible automatic Continue remain **unverified** and do not close #6. The owner reported a disabled Add button during an unfinished second login; ordinary Cancel restored Add while preserving the original account. This is the login concurrency gate, not evidence of a second registered account.

The initial3GiB container limit produced six kernel OOM kills of original client children. Stopping only an empty owned secondary smoke desktop and raising this desktop's limit to4GiB stopped additional OOM kills; no client flag, sandbox, credential policy or data format was relaxed. This is a verification environment capacity correction, not an application crash fix. Production app initialization succeeded after the owner provisioned the previously absent native default collection and normally reopened the utility. No root cause for an application retry defect is claimed.

The screenshot includes only the original application and its own native WM frame. All account/chat rows, status text and device/path pixels were replaced with solid pixels before export. Metadata is absent. Raw frames stay private. The duplicated native/app captions are visible in this baseline; after confirmation is pending.

Docker/Xvfb userspace is not a booted desktop VM, PAM/login-manager, full GNOME/KDE, native GPU, XWayland/pure Wayland, ARM64 or accessibility/scaling acceptance. Complete unsigned package previews and native CI remain separate from supported binary Release promotion.
