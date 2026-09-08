"""Before/after contact sheets, so a human can actually judge the conversion."""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
CELL = 112
PAD = 16


def sheet(rel_paths, out_path, src_root="art/source", dst_root="assets", cols=10):
    rel_paths = [p for p in rel_paths
                 if os.path.exists(os.path.join(ROOT, src_root, p))
                 and os.path.exists(os.path.join(ROOT, dst_root, p))]
    if not rel_paths:
        print("nothing to draw", file=sys.stderr)
        return

    rows = (len(rel_paths) + cols - 1) // cols
    w = cols * CELL
    h = rows * (2 * CELL + PAD + 12)
    img = Image.new("RGBA", (w, h), (28, 28, 34, 255))
    d = ImageDraw.Draw(img)

    for i, rel in enumerate(rel_paths):
        cx, cy = (i % cols) * CELL, (i // cols) * (2 * CELL + PAD + 12)
        for row, root in ((0, src_root), (1, dst_root)):
            im = Image.open(os.path.join(ROOT, root, rel)).convert("RGBA")
            s = min(CELL / im.width, CELL / im.height)
            im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.NEAREST)
            y = cy + 12 + row * CELL
            img.alpha_composite(im, (cx + (CELL - im.width) // 2, y + (CELL - im.height) // 2))
        d.text((cx + 2, cy + 1), os.path.basename(rel)[:17], fill=(215, 215, 225, 255))

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    img.convert("RGB").save(out_path)
    print(f"wrote {out_path}  ({len(rel_paths)} pairs, top row = original)")


def main():
    import glob
    groups = {
        "objects": "assets/objects/*.png",
        "tiles": "assets/tiles/**/*.png",
        "monsters": "assets/monsters/*/*.png",
        "miners": "assets/miners/*/idle/*.png",
    }
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "art/review"
    for name, pattern in groups.items():
        rels = sorted(
            os.path.relpath(p, os.path.join(ROOT, "assets")).replace("\\", "/")
            for p in glob.glob(os.path.join(ROOT, pattern), recursive=True))
        sheet(rels, os.path.join(out_dir, f"{name}.png"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
