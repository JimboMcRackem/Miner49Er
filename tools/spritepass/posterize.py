"""Reduce smooth-shaded sprites to banded pixel art.

Two sprites in the library were authored as continuous-tone renders rather than pixel
art: the water snake (every frame, ~700-840 colours, 97% of them used by two pixels or
fewer) and the slime's four base directional sprites (~348 colours, 98% singletons).
Beside sprites that carry 19-28 colours they read as a different medium.

The palette is built once per CREATURE and applied to every frame. Quantising each
frame on its own would give each frame its own palette, and the animation would shimmer
as colours drifted between frames - the snake's 40 frames already carry 20,079 distinct
colours between them, which is that problem in its raw form.

Usage:
    python -m tools.spritepass.posterize --report
    python -m tools.spritepass.posterize --apply
"""
import glob
import os
import sys

from PIL import Image

ALPHA_THRESHOLD = 128
ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))


def _opaque_colors(im):
    im = im.convert("RGBA")
    px = im.load()
    return [px[x, y][:3]
            for y in range(im.height)
            for x in range(im.width)
            if px[x, y][3] >= ALPHA_THRESHOLD]


def exact_palette(images):
    """Every distinct opaque colour, in first-seen order.

    Used to adopt a reference palette from frames that are already correct, rather than
    inventing one - the slime's 36 walk frames define what a slime looks like, so its
    four base sprites are mapped onto exactly that.
    """
    seen = {}
    for im in images:
        for c in _opaque_colors(im):
            seen.setdefault(c, None)
    return list(seen)


def common_palette(images, n):
    """The n most-used opaque colours across the given frames.

    The plain union of a 36-frame walk cycle carries 78 colours, most of them rare.
    Mapping onto all of them leaves the result speckled with barely-used tones - still
    technically the right palette, but not the right shading language. Keeping the
    colours that actually paint the sprite gives a tighter, more representative set.
    """
    counts = {}
    for im in images:
        for c in _opaque_colors(im):
            counts[c] = counts.get(c, 0) + 1
    ranked = sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))
    return [c for c, _ in ranked[:n]]


def build_palette(images, n):
    """One median-cut palette across all the given frames."""
    pix = []
    for im in images:
        pix.extend(_opaque_colors(im))
    if not pix:
        return []
    strip = Image.new("RGB", (len(pix), 1))
    sp = strip.load()
    for i, c in enumerate(pix):
        sp[i, 0] = c
    q = strip.quantize(colors=n, method=Image.MEDIANCUT, dither=Image.NONE)
    used = sorted({q.load()[i, 0] for i in range(len(pix))})
    pal = q.getpalette()
    return [tuple(pal[i * 3:i * 3 + 3]) for i in used]


def map_image(im, palette):
    """Snap every opaque pixel to its nearest palette colour; alpha becomes binary."""
    im = im.convert("RGBA")
    src = im.load()
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    dst = out.load()
    cache = {}
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = src[x, y]
            if a < ALPHA_THRESHOLD:
                continue
            key = (r, g, b)
            hit = cache.get(key)
            if hit is None:
                hit = min(palette, key=lambda p: (r - p[0]) ** 2 + (g - p[1]) ** 2 + (b - p[2]) ** 2)
                cache[key] = hit
            dst[x, y] = hit + (255,)
    return out


def profile(im):
    """Colour count, density and singleton fraction - the shading-language tells."""
    cols = {}
    for c in _opaque_colors(im):
        cols[c] = cols.get(c, 0) + 1
    n = sum(cols.values())
    if not n:
        return {"colors": 0, "px": 0, "cpp": 0.0, "singleton": 0.0}
    return {
        "colors": len(cols),
        "px": n,
        "cpp": len(cols) / n,
        "singleton": sum(1 for v in cols.values() if v <= 2) / len(cols),
    }


# --- what this pass targets -------------------------------------------------------
# Deliberately NOT the octopus (96 colours) or the prizes (88): despite high colour
# counts they sit at 20-24% singletons, which is banded pixel art. Their colour count
# is a function of sprite size, not of smooth shading.
JOBS = [
    {
        "name": "water_snake",
        "targets": "assets/monsters/water_snake/**/*.png",
        "reference": None,      # no clean frames exist; build a palette from all of them
        "colors": 20,
    },
    {
        "name": "slime (base sprites)",
        "targets": "assets/monsters/slime/*.png",
        "reference": "assets/monsters/slime/walk/*.png",  # already correct - adopt it
        "colors": 26,
    },
]


def _load(pattern):
    paths = sorted(glob.glob(os.path.join(ROOT, pattern), recursive=True))
    return paths, [Image.open(p) for p in paths]


def run(apply_changes):
    total = 0
    for job in JOBS:
        paths, images = _load(job["targets"])
        if not paths:
            print(f"!! no files matched {job['targets']}", file=sys.stderr)
            continue

        if job["reference"]:
            ref_paths, ref_images = _load(job["reference"])
            palette = common_palette(ref_images, job["colors"])
            src = f"top {len(palette)} colours from {len(ref_paths)} reference frames"
        else:
            palette = build_palette(images, job["colors"])
            src = f"median-cut across {len(paths)} frames"

        before = [profile(im) for im in images]
        mapped = [map_image(im, palette) for im in images]
        after = [profile(im) for im in mapped]

        print(f"\n{job['name']}  ({len(paths)} files)")
        print(f"  palette: {len(palette)} colours, {src}")
        print(f"  colours   {min(b['colors'] for b in before):5d}-{max(b['colors'] for b in before):<5d}"
              f" ->  {min(a['colors'] for a in after):d}-{max(a['colors'] for a in after):d}")
        print(f"  singleton {100*max(b['singleton'] for b in before):4.0f}%"
              f"        ->  {100*max(a['singleton'] for a in after):.0f}%")

        if apply_changes:
            for p, out in zip(paths, mapped):
                out.save(p)
            total += len(paths)

    if apply_changes:
        print(f"\nwrote {total} file(s)")
    else:
        print("\n(report only - pass --apply to write)")
    return 0


def main():
    return run("--apply" in sys.argv)


if __name__ == "__main__":
    raise SystemExit(main())
