#!/usr/bin/env python3
"""Make BuzzahBuddy's static fonts from the design system's variable woff2 files.

MAUI loads TTF/OTF only and can't pick a weight from a variable font, so each weight
the app uses becomes its own TTF. Needs fontTools and brotli, e.g. in a throwaway venv:

  python3 -m venv /tmp/bbfonts && /tmp/bbfonts/bin/pip install fonttools brotli
  /tmp/bbfonts/bin/python scripts/gen_fonts.py [--ds PATH]
"""
import argparse
import shutil
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "BuzzahBuddy/Resources/Fonts"

# (source woff2, weight, output file). Bold Text uses the heavier entries (Typography.ApplyFontWeights).
FONTS = [
    ("lexend-latin-wght-normal.woff2", 500, "Lexend-Medium.ttf"),
    ("lexend-latin-wght-normal.woff2", 600, "Lexend-SemiBold.ttf"),
    ("lexend-latin-wght-normal.woff2", 700, "Lexend-Bold.ttf"),
    ("atkinson-hyperlegible-next-latin-wght-normal.woff2", 400, "AtkinsonHyperlegibleNext-Regular.ttf"),
    ("atkinson-hyperlegible-next-latin-wght-normal.woff2", 700, "AtkinsonHyperlegibleNext-Bold.ttf"),
    ("atkinson-hyperlegible-next-latin-wght-normal.woff2", 800, "AtkinsonHyperlegibleNext-ExtraBold.ttf"),
    ("atkinson-hyperlegible-mono-latin-wght-normal.woff2", 400, "AtkinsonHyperlegibleMono-Regular.ttf"),
    ("atkinson-hyperlegible-mono-latin-wght-normal.woff2", 700, "AtkinsonHyperlegibleMono-Bold.ttf"),
]


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--ds", type=Path, default=REPO / "../../.claude/skills/bluebuzzah-design-system")
    fonts_dir = parser.parse_args().ds.resolve() / "fonts"
    OUT.mkdir(parents=True, exist_ok=True)
    names = set()
    for src, weight, out in FONTS:
        font = TTFont(fonts_dir / src)
        static = instancer.instantiateVariableFont(font, {"wght": weight}, updateFontNames=True)
        static.flavor = None
        static.save(OUT / out)
        ps = static["name"].getDebugName(6)
        gsub = static["GSUB"].table.FeatureList.FeatureRecord if "GSUB" in static else []
        tnum = any(f.FeatureTag == "tnum" for f in gsub)
        print(f"{out}: PostScript {ps}, tnum {'yes' if tnum else 'no'}")
        names.add(ps)
    assert len(names) == len(FONTS), "PostScript names collide; iOS would load the wrong weight"
    licenses = OUT / "licenses"
    licenses.mkdir(exist_ok=True)
    for lic in fonts_dir.glob("OFL-*.txt"):
        shutil.copy(lic, licenses / lic.name)
        print(f"copied {lic.name}")


if __name__ == "__main__":
    main()
