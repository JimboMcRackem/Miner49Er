import os
import unittest

from tools.artpass import verify


class AssetComplianceTests(unittest.TestCase):
    """The permanent guard from the spec: no asset may ship an off-palette pixel."""

    def test_every_asset_is_in_palette_with_binary_alpha(self):
        problems = verify.check_tree()
        self.assertEqual([], problems[:20], f"{len(problems)} violation(s)")

    def test_exclusion_list_has_not_grown(self):
        """Exclusions are a deliberate M6 deferral, not a place to hide failures."""
        from tools.artpass import palette
        self.assertEqual(
            {"Icon.png", "Icon_Old.png", "Splash.png"},
            set(palette.load_ramps().get("exclude", [])))

    def test_asset_and_source_trees_have_the_same_files(self):
        def rels(root):
            return {
                os.path.relpath(os.path.join(d, n), root).replace("\\", "/")
                for d, _dirs, files in os.walk(root) for n in files
                if n.lower().endswith(".png")
            }
        self.assertEqual(rels("art/source"), rels("assets"))


class TilesetEmbedTests(unittest.TestCase):
    """The TileSet bakes the terrain atlas in as raw bytes instead of referencing the
    PNG, so converting the PNG alone leaves the terrain on screen unchanged."""

    def test_embedded_tileset_matches_the_converted_atlas(self):
        from tools.artpass import embed_tileset
        with open(embed_tileset.TRES_PATH, encoding="utf-8") as fh:
            embedded = embed_tileset.read_embedded(fh.read())
        expected, _size = embed_tileset.atlas_bytes()
        # Report the first mismatch rather than diffing 409,600 ints, which is
        # slow enough to look like a hang.
        self.assertEqual(len(expected), len(embedded), "TileSet byte count differs")
        for i, (a, b) in enumerate(zip(expected, embedded)):
            if a != b:
                self.fail(f"combined_terrain.tres is stale at byte {i} "
                          f"(atlas={a}, tres={b}) - run python -m tools.artpass.embed_tileset")

    def test_embedded_tileset_is_in_palette(self):
        from tools.artpass import embed_tileset, palette
        legal = set(palette.load_palette().values())
        with open(embed_tileset.TRES_PATH, encoding="utf-8") as fh:
            raw = embed_tileset.read_embedded(fh.read())
        bad = {tuple(raw[i:i + 3]) for i in range(0, len(raw), 4)
               if raw[i + 3] == 255 and tuple(raw[i:i + 3]) not in legal}
        self.assertEqual(set(), bad, f"{len(bad)} off-palette colour(s) in the TileSet")


if __name__ == "__main__":
    unittest.main()
