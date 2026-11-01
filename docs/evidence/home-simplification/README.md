# Home simplification preview

The core account tanks, percentage display and direct Switch/Recover actions are preserved. Global actions move into one titlebar gear; Add account and Refresh remain beside it. Secondary metadata is quieter and missing subscription dates no longer add empty rows. [Behavior and limits](../../development/HOME_SIMPLIFICATION.md).

| Before | Local preview |
| --- | --- |
| ![Previous production WPF home with fictional profiles](before-ko-dark.png) | ![Simplified production WPF home with the same fictional profiles](after-ko-dark.png) |

![Settings menu rendered from the production WPF menu controls](settings-ko-dark.png)

These images are windowless renders of the actual maintained WPF controls with fictional account/resource data, not native application-window screenshots. The titlebar preview's DEMO marker is supplied only by the documentation renderer. The before source is the pre-change main tree; the after is a local uncommitted implementation. No real account, Codex restart, provider switch or installed package was exercised.

The UI harness passes54checks, including0/1/4/8accounts at two sizes in English/Korean and dark/light themes. Long names, warning actions, tank/number/metadata containment, the Settings path, persisted language/theme/automatic choices, busy restart refusal and vertical access to the last of eight accounts are covered. The standard preview renderer also exports600×400. Actual native popup placement, keyboard behavior and Mac/Linux runtime UI remain separate acceptance items. No commit, push, merge, installation or release is implied by this local preview.
