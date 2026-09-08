"""Posterise art/source/**.png into assets/**.png using authored family ramps.

Deterministic and idempotent: the same source plus the same ramps always yields the
same bytes. Reads only art/source, writes only assets - so ramp tuning is repeatable.
Posterising is lossy, which is exactly why the originals must be kept.
"""
import os
import sys

from PIL import Image

from tools.artpass import palette

ALPHA_THRESHOLD = 128

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC_ROOT = os.path.join(ROOT, "art", "source")
DST_ROOT = os.path.join(ROOT, "assets")


def posterize_image(img, colors, edges):
    """Map every opaque pixel's luminance into the ramp; force alpha binary."""
    img = img.convert("RGBA")
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src = img.load()
    dst = out.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = src[x, y]
            if a < ALPHA_THRESHOLD:
                continue
            i = palette.band_index(palette.luminance((r, g, b)), edges)
            cr, cg, cb = colors[i]
            dst[x, y] = (cr, cg, cb, 255)
    return out


def convert_tree(src_root=SRC_ROOT, dst_root=DST_ROOT, pal=None, ramps=None, verbose=True):
    """Convert every PNG under src_root into dst_root, mirroring paths."""
    pal = pal or palette.load_palette()
    ramps = ramps or palette.load_ramps()
    converted, skipped = 0, []

    for dirpath, _dirs, files in os.walk(src_root):
        for fn in files:
            if not fn.lower().endswith(".png"):
                continue
            abs_src = os.path.join(dirpath, fn)
            rel = os.path.relpath(abs_src, src_root).replace("\\", "/")
            if palette.is_excluded(rel, ramps):
                continue
            try:
                ramp_name = palette.ramp_for(rel, ramps)
            except KeyError:
                skipped.append(rel)
                continue

            colors, edges = palette.ramp_colors(ramp_name, ramps, pal)
            out = posterize_image(Image.open(abs_src), colors, edges)

            abs_dst = os.path.join(dst_root, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(abs_dst), exist_ok=True)
            out.save(abs_dst)
            converted += 1
            if verbose:
                print(f"  {rel} -> {ramp_name}")

    return converted, skipped


def main():
    converted, skipped = convert_tree(verbose="--quiet" not in sys.argv)
    print(f"\nconverted {converted} file(s)")
    if skipped:
        print(f"SKIPPED {len(skipped)} file(s) with no ramp assignment:", file=sys.stderr)
        for rel in skipped:
            print(f"  {rel}", file=sys.stderr)
        return 1

    from tools.artpass import verify
    violations = verify.check_tree(DST_ROOT)
    if violations:
        print(f"\nVERIFY FAILED - {len(violations)} violation(s):", file=sys.stderr)
        for v in violations[:20]:
            print(f"  {v}", file=sys.stderr)
        return 1
    print("verify: all output in-palette with binary alpha")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
