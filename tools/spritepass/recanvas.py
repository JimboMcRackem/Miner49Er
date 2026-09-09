"""Unify canvas sizes within a sprite family.

Four families were authored on two canvas sizes at once, so their frames render at two
different ratios - the goat's base sprites sit on a 60px canvas while its 36 walk frames
use 68px, which makes the idle goat render about 12% larger than the walking one.

This pads or crops the canvas. It never rescales: in these families the artwork is the
same pixel size in both canvases, so the fix is to change the space around it, not the
sprite. Cropping that would clip opaque pixels raises instead of silently losing art.

    python -m tools.spritepass.recanvas --report
    python -m tools.spritepass.recanvas --apply
"""
import glob
import os
import sys

from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))


def resize_canvas(im, target):
    """Centre `im` on a `target` x `target` canvas by padding or cropping."""
    im = im.convert("RGBA")
    w, h = im.size
    if (w, h) == (target, target):
        return im.copy()

    if target < w or target < h:
        bb = im.getbbox()
        if bb:
            dx, dy = (w - target) // 2, (h - target) // 2
            if bb[0] < dx or bb[1] < dy or bb[2] > dx + target or bb[3] > dy + target:
                raise ValueError(
                    f"cropping {w}x{h} to {target} would clip opaque pixels at {bb}")

    out = Image.new("RGBA", (target, target), (0, 0, 0, 0))
    out.paste(im, ((target - w) // 2, (target - h) // 2))
    return out


# Families authored on two canvas sizes. Target is the size the majority of frames -
# and therefore the animation the player sees most - already uses.
JOBS = [
    {"name": "goat",  "pattern": "assets/monsters/goat/**/*.png",  "target": 68},
    {"name": "slime", "pattern": "assets/monsters/slime/**/*.png", "target": 48},
]


def run(apply_changes):
    changed = 0
    for job in JOBS:
        paths = sorted(glob.glob(os.path.join(ROOT, job["pattern"]), recursive=True))
        odd = []
        for p in paths:
            im = Image.open(p)
            if im.size[0] != job["target"] or im.size[1] != job["target"]:
                odd.append((p, im.size))
        print(f"\n{job['name']}: target {job['target']}px, "
              f"{len(paths) - len(odd)} already correct, {len(odd)} to change")
        for p, size in odd:
            rel = os.path.relpath(p, ROOT).replace(os.sep, "/")
            print(f"  {rel}  {size[0]}x{size[1]} -> {job['target']}")
            if apply_changes:
                resize_canvas(Image.open(p), job["target"]).save(p)
                changed += 1

    print(f"\nwrote {changed} file(s)" if apply_changes else "\n(report only - pass --apply to write)")
    return 0


def main():
    return run("--apply" in sys.argv)


if __name__ == "__main__":
    raise SystemExit(main())
