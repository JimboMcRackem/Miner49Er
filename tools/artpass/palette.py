"""Loading and colour maths for the C64 art pass.

The 16-colour palette and every ramp live in JSON under art/. This module is the
only place that parses them; convert.py, verify.py and gen_palette_cs.py all go
through here so there is exactly one interpretation of the art direction.
"""
import fnmatch
import json
import os

ART_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "art")
PALETTE_PATH = os.path.normpath(os.path.join(ART_DIR, "palette.json"))


def load_palette(path=PALETTE_PATH):
    """name -> (r, g, b). Values are the C64 hardware palette; nothing else is legal."""
    with open(path, encoding="utf-8") as fh:
        raw = json.load(fh)["colors"]
    return {name: _hex_to_rgb(h) for name, h in raw.items()}


def _hex_to_rgb(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def luminance(rgb):
    """Rec.601 luma, normalised to 0.0-1.0.

    Absolute (not per-sprite-stretched) on purpose: fixed thresholds are what stop
    animation frames shimmering as each frame's brightness range shifts.
    """
    r, g, b = rgb
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0


RAMPS_PATH = os.path.normpath(os.path.join(ART_DIR, "ramps.json"))


def load_ramps(path=RAMPS_PATH):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def band_index(lum01, edges):
    """Which ramp slot a luminance falls in. A value exactly on an edge takes the
    upper band, so bands are [lo, hi)."""
    i = 0
    for e in edges:
        if lum01 >= e:
            i += 1
        else:
            break
    return i


def is_excluded(relpath, ramps):
    """True for art deliberately left unconverted (marketing art, redrawn in M6)."""
    return relpath.replace("\\", "/") in set(ramps.get("exclude", []))


def ramp_for(relpath, ramps):
    """Resolve an asset path (relative to assets/, forward slashes) to a ramp name.
    Per-file overrides beat globs; the first matching glob wins."""
    relpath = relpath.replace("\\", "/")
    override = ramps.get("overrides", {}).get(relpath)
    if override:
        return override
    for rule in ramps["assign"]:
        if fnmatch.fnmatch(relpath, rule["glob"]):
            return rule["ramp"]
    raise KeyError(f"no ramp assigned for {relpath}")


def ramp_colors(name, ramps, pal):
    """(list[(r,g,b)], list[float]) for a named family ramp."""
    spec = ramps["families"][name]
    return [pal[c] for c in spec["colors"]], spec["edges"]
