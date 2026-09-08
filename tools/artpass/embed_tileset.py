"""Re-embed the converted terrain atlas into assets/tiles/combined_terrain.tres.

The TileSet does NOT reference a PNG. Godot baked the 160x640 atlas into the
resource as a raw RGBA8 PackedByteArray, so converting combined_terrain_atlas.png
alone changes nothing on screen - the terrain the game actually draws lives in
these bytes. Terrain is most of the screen, so missing this would have left the
single most visible surface off-palette.

The atlas PNG and the embedded bytes are pixel-identical, so this simply rewrites
the byte array from the converted PNG.
"""
import os
import re
import sys

from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
TRES_PATH = os.path.join(ROOT, "assets", "tiles", "combined_terrain.tres")
ATLAS_PATH = os.path.join(ROOT, "assets", "tiles", "combined_terrain_atlas.png")

_ARRAY_RE = re.compile(r"PackedByteArray\((.*?)\)", re.S)


def read_embedded(tres_text):
    """The embedded atlas as a flat list of RGBA bytes."""
    m = _ARRAY_RE.search(tres_text)
    if not m:
        raise ValueError("no PackedByteArray found in the TileSet")
    return [int(x) for x in m.group(1).replace("\n", "").split(",") if x.strip()]


def atlas_bytes(path=ATLAS_PATH):
    img = Image.open(path).convert("RGBA")
    px = img.load()
    out = []
    for y in range(img.height):
        for x in range(img.width):
            out.extend(px[x, y])
    return out, img.size


def embed(tres_path=TRES_PATH, atlas_path=ATLAS_PATH):
    """Rewrite the TileSet's byte array from the converted atlas PNG."""
    with open(tres_path, encoding="utf-8") as fh:
        text = fh.read()

    existing = read_embedded(text)
    new_bytes, (w, h) = atlas_bytes(atlas_path)
    if len(new_bytes) != len(existing):
        raise ValueError(
            f"atlas is {w}x{h} ({len(new_bytes)} bytes) but the TileSet holds "
            f"{len(existing)} bytes - refusing to write a mismatched image")

    replacement = "PackedByteArray(" + ", ".join(str(b) for b in new_bytes) + ")"
    out = _ARRAY_RE.sub(lambda _m: replacement, text, count=1)
    with open(tres_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(out)
    return len(new_bytes)


def main():
    n = embed()
    print(f"embedded {n} bytes into {os.path.relpath(TRES_PATH, ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
