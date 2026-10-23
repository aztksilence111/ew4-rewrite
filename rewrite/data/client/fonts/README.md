# Offline UI Fonts

All ten font families are bundled as bounded WOFF2 subsets, without reducing
the combined character coverage or adding system font aliases.
AR PL UKai CN is the primary Chinese UI face. Latin letters and ordinary digits
use Liberation Serif Bold; combat digits use Liberation Sans Bold Italic.
These are visual substitutes, not identified original fonts. The two modified
Liberation subsets have renamed internal families under OFL.
LXGW WenKai Regular 1.530 and Noto Serif SC Regular 400 are fallbacks. Jigmo, Jigmo2 and Jigmo3 cover rare
Han characters; GNU Unifont and Unifont Upper 17.0.05 supplement other scripts.
`coverage.json` records each subset's exact Unicode ranges, sizes, SHA-256,
bootstrap role and upstream URL. Each decoded subset is at most 8 MiB.
UTF-8 is the text encoding, not a guarantee that every Unicode code point has ink.

`original-ui.js::TextRenderer.prepare` loads the coverage index and the primary
common subset. If it fails, the bundled WenKai, Noto and then Unifont common
subset are tried. Other subsets load on demand with serialized decoding.
Canvas text repaints after deferred fonts become available. A persistent status
names failed files and provides a retry; failed promises are not permanent.
`Ew4OriginalUI.prepareText(text)` can be awaited before inspecting rare glyphs.
`displayText` normalizes NFC and replaces
unsupported code points with bundled U+FFFD, with a console warning. City labels
use separate white fill and black outline. Imported FNT/PNG styling is ignored.

Combat damage uses `EW4 Combat` outlines with black stroke and white/red fill.
Geographic map artwork is excluded from UI font unification. Original map labels
retain animation artwork; the existing Chinese map labels retain their previous
WenKai/Noto chain through `mapFontFamily/prepareMapText`.

Licenses: UKai-LICENSE.txt (Arphic Public License), Liberation-LICENSE.txt,
LXGWWenKai-OFL.txt and NotoSerifSC-OFL.txt (SIL OFL 1.1), Jigmo-LICENSE.txt (CC0 1.0),
Unifont-LICENSE.txt (compiled font dual license; distributed under SIL OFL 1.1).
Keep these licenses when redistributing. Detailed author guidance is in
`rewrite/mod-kit/data/client/fonts/README.md`; build/audit tools are
`tools/build_bundled_fonts.py` and `tools/audit_bundled_fonts.py`.
