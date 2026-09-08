"""Loading and colour maths for the C64 art pass.

The 16-colour palette and every ramp live in JSON under art/. This module is the
only place that parses them; convert.py, verify.py and gen_palette_cs.py all go
through here so there is exactly one interpretation of the art direction.
"""
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
