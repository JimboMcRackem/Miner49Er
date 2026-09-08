"""Assert that every shipped asset obeys the palette contract.

This is the permanent guard: it is what stops cohesion decaying the next time a
feature phase adds art. convert.py runs it automatically over its own output.
"""
import os
import sys

from PIL import Image

from tools.artpass import palette

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS_ROOT = os.path.join(ROOT, "assets")


def check_image(path, legal):
    """Return a list of human-readable violations for one PNG."""
    problems = []
    img = Image.open(path).convert("RGBA")
    px = img.load()
    bad_colors = set()
    soft_alpha = 0
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a not in (0, 255):
                soft_alpha += 1
            elif a == 255 and (r, g, b) not in legal:
                bad_colors.add((r, g, b))

    if soft_alpha:
        problems.append(f"{path}: {soft_alpha} pixel(s) with non-binary alpha")
    for c in sorted(bad_colors):
        problems.append(f"{path}: off-palette colour #{c[0]:02X}{c[1]:02X}{c[2]:02X}")
    return problems


def check_tree(root=ASSETS_ROOT, pal=None, ramps=None):
    """Every asset must be in-palette, except art explicitly excluded in ramps.json."""
    legal = set((pal or palette.load_palette()).values())
    ramps = ramps or palette.load_ramps()
    problems = []
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            if not fn.lower().endswith(".png"):
                continue
            abs_path = os.path.join(dirpath, fn)
            rel = os.path.relpath(abs_path, root).replace("\\", "/")
            if palette.is_excluded(rel, ramps):
                continue
            problems.extend(check_image(abs_path, legal))
    return problems


def main():
    problems = check_tree()
    if problems:
        print(f"FAIL - {len(problems)} violation(s):", file=sys.stderr)
        for p in problems:
            print(f"  {p}", file=sys.stderr)
        return 1
    print("OK: every asset is in-palette with binary alpha")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
