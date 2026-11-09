#!/usr/bin/env python3
"""Take a BuzzahBuddy user guide screenshot of the simulator's current screen, with optional arrows.

Usage: [DEVICE=iPhone] python3 guide_shot.py <name> [--arrow TARGET [from=above] [len=80] [at=0.5] [label="Text"]] ...

TARGET is an AutomationId, an exact accessibility label, tab:<Title>, or x,y,w,h in points. Join targets
with + to point at a group, such as PauseResumeButton+StopSessionButton.
from=above|below|left|right (default: the side with the most room), len= is the arrow length in points,
at= is where along that edge it lands (0 to 1).

Writes tmp/user-guide/images/<name>.png. The raw shot stays in assets/screenshots/.
"""
import argparse
import json
import math
import re
import shutil
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
SIM = REPO / ".claude/skills/ios-screenshots/scripts/sim.sh"
TOKENS = next(p / ".claude/skills/bluebuzzah-design-system/tokens.json" for p in REPO.parents
              if (p / ".claude/skills/bluebuzzah-design-system/tokens.json").exists())  # the workspace, also from a worktree
FONT = REPO / "BuzzahBuddy/Resources/Fonts/Lexend-Medium.ttf"
OUT = REPO / "tmp/user-guide/images"

# In points. Screenshots are 2x or 3x; guide pages show them far smaller, so the weights run heavy.
STROKE, HALO = 4.5, 9
HEAD, HEAD_ANGLE = 15, math.radians(42)
GAP = 7  # between the tip and the target, so the arrow never covers it
LENGTH = 80
LABEL_SIZE, LABEL_PAD_X, LABEL_PAD_Y = 19, 14, 9
LABEL_GAP = 5  # between the tail and its label
RADIUS, BORDER = 8, 2  # radius-sm, border-control
MARGIN = 16  # space-4


def room(rect, screen):
    x, y, w, h = rect
    return {"above": y, "below": screen[1] - y - h, "left": x, "right": screen[0] - x - w}


def arrow_line(rect, side, length, at, screen):
    """Tail and tip of an arrow reaching `rect` from `side`, in points."""
    x, y, w, h = rect
    tip, step = {
        "above": ((x + w * at, y - GAP), (0, -1)),
        "below": ((x + w * at, y + h + GAP), (0, 1)),
        "left": ((x - GAP, y + h * at), (-1, 0)),
        "right": ((x + w + GAP, y + h * at), (1, 0)),
    }[side]
    tail = (tip[0] + step[0] * length, tip[1] + step[1] * length)
    if not (0 <= tail[0] <= screen[0] and 0 <= tail[1] <= screen[1]):
        raise SystemExit(f"No room for a {length} pt arrow from {side} of {rect}. Change from= or len=.")
    return tail, tip


def label_box(tail, side, width, height, screen):
    """Label rectangle past the tail, kept inside the screen margin."""
    x, y = tail
    left, top = {
        "above": (x - width / 2, y - LABEL_GAP - height),
        "below": (x - width / 2, y + LABEL_GAP),
        "left": (x - LABEL_GAP - width, y - height / 2),
        "right": (x + LABEL_GAP, y - height / 2),
    }[side]
    left = min(max(left, MARGIN), screen[0] - MARGIN - width)
    if top < 0 or top + height > screen[1]:
        raise SystemExit(f"No room for a label at {tail}. Change from= or len=.")
    return left, top, width, height


def overlap(a, b):
    return a[0] < b[0] + b[2] and b[0] < a[0] + a[2] and a[1] < b[1] + b[3] and b[1] < a[1] + a[3]


def union(rects):
    x0, y0 = min(r[0] for r in rects), min(r[1] for r in rects)
    x1, y1 = max(r[0] + r[2] for r in rects), max(r[1] + r[3] for r in rects)
    return x0, y0, x1 - x0, y1 - y0


def idb(udid, *args):
    return json.loads(subprocess.check_output(["idb", "ui", *args, "--udid", udid, "--json"]))


def frame(e):
    f = e["frame"]
    return f["x"], f["y"], f["width"], f["height"]


def find(target, elements, udid, screen):
    if re.fullmatch(r"[\d.]+(,[\d.]+){3}", target):
        return tuple(map(float, target.split(",")))
    if target.startswith("tab:"):  # describe-all hides tab bar items, so probe the bar as sim.sh tab does
        for x in range(30, int(screen[0]) - 20, 30):
            try:
                e = idb(udid, "describe-point", str(x), str(int(screen[1]) - 52))
            except subprocess.CalledProcessError:  # nothing at this point
                continue
            if e.get("AXLabel") == target[4:]:
                return frame(e)
        raise SystemExit(f"No tab named {target[4:]}")
    for e in elements:
        if target in (e.get("AXUniqueId"), e.get("AXLabel")):
            return frame(e)
    raise SystemExit(f"{target} isn't on screen. Try: sim.sh ids")


def text_width(text, scale):
    size = str(LABEL_SIZE * scale)
    return int(subprocess.check_output(["magick", "-font", str(FONT), "-pointsize", size, f"label:{text}", "-format", "%w", "info:"]))


def draw(tail, tip, box, label, scale, color):
    pt = lambda p: f"{p[0] * scale:.1f},{p[1] * scale:.1f}"
    angle = math.atan2(tip[1] - tail[1], tip[0] - tail[0])
    barbs = [(tip[0] - HEAD * math.cos(angle + a), tip[1] - HEAD * math.sin(angle + a)) for a in (HEAD_ANGLE, -HEAD_ANGLE)]
    paths = f"path 'M {pt(tail)} L {pt(tip)}' path 'M {pt(barbs[0])} L {pt(tip)} L {pt(barbs[1])}'"
    mvg = [
        f"fill none stroke '{c}' stroke-width {w * scale} stroke-linecap round stroke-linejoin round {paths}"
        for c, w in ((color["surface"], HALO), (color["accent"], STROKE))
    ]
    if box:
        x, y, w, h = box
        text = label.replace("\\", "\\\\").replace("'", "\\'")
        mvg += [
            f"fill '{color['surface-overlay']}' stroke '{color['accent']}' stroke-width {BORDER * scale} "
            f"roundrectangle {pt((x, y))} {pt((x + w, y + h))} {RADIUS * scale},{RADIUS * scale}",
            f"fill '{color['ink']}' stroke none font '{FONT}' font-size {LABEL_SIZE * scale} "
            f"text {pt((x + LABEL_PAD_X, y + LABEL_PAD_Y + LABEL_SIZE * 0.98))} '{text}'",
        ]
    return " ".join(mvg)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("name")
    ap.add_argument("--arrow", nargs="+", action="append", default=[], metavar="ARG")
    args = ap.parse_args()

    tokens = json.loads(TOKENS.read_text())["color"]["tokens"]
    color = {t["name"]: t["value"]["dark"] for t in tokens if isinstance(t["value"], dict)}  # dark only, like the shots
    run_sim = lambda *a: subprocess.check_output(["bash", str(SIM), *a], text=True).strip()
    udid = run_sim("udid")
    elements = idb(udid, "describe-all")
    screen = frame(elements[0])[2:]
    raw = Path(run_sim("shot", f"guide-{args.name}"))
    scale = int(subprocess.check_output(["magick", "identify", "-format", "%w", str(raw)])) / screen[0]

    mvg, boxes = [], []
    for target, *options in args.arrow:
        o = dict(p.split("=", 1) for p in options)
        if unknown := set(o) - {"from", "len", "at", "label"}:
            raise SystemExit(f"Unknown arrow option: {', '.join(unknown)}")
        rect = union([find(t, elements, udid, screen) for t in target.split("+")])
        if rect[0] < 0 or rect[1] < 0 or rect[0] + rect[2] > screen[0] or rect[1] + rect[3] > screen[1]:
            raise SystemExit(f"{target} is off screen at {rect}. Scroll it into view.")
        spaces = room(rect, screen)
        side = o.get("from") or max(spaces, key=spaces.get)
        tail, tip = arrow_line(rect, side, float(o.get("len", LENGTH)), float(o.get("at", 0.5)), screen)
        box = None
        if "label" in o:
            size = (text_width(o["label"], scale) / scale + 2 * LABEL_PAD_X, LABEL_SIZE * 1.25 + 2 * LABEL_PAD_Y)
            box = label_box(tail, side, *size, screen)
            if any(overlap(box, other) for other in boxes):
                raise SystemExit(f"The label for {target} overlaps another label. Vary len= or at=.")
            boxes.append(box)
        print(f"{target}: frame {rect}, from {side}")
        mvg.append(draw(tail, tip, box, o.get("label"), scale, color))

    OUT.mkdir(parents=True, exist_ok=True)
    out = OUT / f"{args.name}.png"
    if mvg:
        subprocess.check_call(["magick", str(raw), "-draw", " ".join(mvg), str(out)])
    else:
        shutil.copy(raw, out)
    print(out)


if __name__ == "__main__":
    main()
