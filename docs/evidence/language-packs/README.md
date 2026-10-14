# Language-pack application evidence

Related [issue #16](https://github.com/Gaebabi/codex-switch/issues/16). These lossless screenshots use actual Windows WPF application components with identical fictional accounts/resource inputs. They contain no private identity/chat/home/token pixels and are not evidence of live switching. Native Mac GUI/file-picker acceptance is separate from Mac compilation and cross-platform catalogue tests.

| Before: 1.27.6 | After: 1.28.0 source |
| --- | --- |
| ![Existing English application](../app-after.png) | ![New English application with extensible language menu](app-en.png) |

Confirmation screens: [한국어](app-ko.png), [日本語](app-ja.png), [简体中文](app-zh-CN.png), [繁體中文](app-zh-TW.png), [Español](app-es.png), [Français](app-fr.png), [Deutsch](app-de.png), [Português](app-pt-BR.png).

Minimum-window check: [German 500×360](app-de-compact.png). [Arabic partial local-pack fixture](app-rtl-fixture.png) verifies RTL layout and English fallback; it is not a complete Arabic catalogue or a native-account handoff test.

The renderer's process-local WPF off-screen switch permits capture when the host's display session is disconnected. The earlier source also produced an empty bitmap in that environment. Production graphics/settings are unchanged. A pixel-coverage check rejects blank output; no image is synthesized from repository screenshots or edited to disguise failures. [WPF switch source](https://source.dot.net/PresentationCore/MS/Internal/CoreAppContextSwitches.cs.html)

All builtin packs supply 338 structurally checked messages; linguistic/native-speaker review is separate. The tests use production pack parsing/install/restore code with disposable JSON and never touch a real Codex home/vault/session. The PR records exact-head CI and review.
