# Codex Switch icon

The maintainer supplied the cloud/terminal silhouette and requested the center be changed to `>_<`. The built-in ImageGen edit produced `mark.png`, a transparent monochrome mark. Its accepted alpha silhouette is traced into `mark.path`/`mark.svg` with one-source-pixel contour tolerance. WPF/Avalonia titlebar and template icons draw this geometry directly at the display's DPI.

Windows ICO stores independent native vector renders at 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels. Mac PNG/ICNS start from a 1024px vector render. No 16px image is stretched into a taskbar or Retina icon. `--export-icons` and `scripts/make-mac-icons.py` reproduce the packaged icons; normal builds use the checked-in assets.

The packaged application icon fills the cloud body white beneath the original black perimeter and face. `scripts/vectorize-mark.py` exports the enclosing outer contour as `mark-body.path` alongside the unchanged even-odd mark. Icon export draws that body first, keeping everything outside it transparent. In-app titlebar and template glyphs continue to use the theme's foreground ink.

Edit prompt: "Preserve the thick rounded irregular cloud perimeter. Replace only the inner terminal symbols with a playful literal >_< face: two inward-pointing chevron squint eyes and a short underscore mouth centered below. Flat near-black strokes, actual transparent background, no tile, shadow, gradient or extra lettering."

This modified icon identifies an independent community utility. It does not indicate OpenAI sponsorship or endorsement. The application code's MIT grant does not grant rights to third-party trademarks.
