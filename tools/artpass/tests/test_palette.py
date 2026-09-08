import unittest

from tools.artpass import palette


class LoadPaletteTests(unittest.TestCase):
    def test_loads_sixteen_colors(self):
        p = palette.load_palette()
        self.assertEqual(16, len(p))

    def test_black_and_white_are_exact(self):
        p = palette.load_palette()
        self.assertEqual((0, 0, 0), p["black"])
        self.assertEqual((255, 255, 255), p["white"])

    def test_yellow_matches_pepto(self):
        p = palette.load_palette()
        self.assertEqual((0xED, 0xF1, 0x71), p["yellow"])


class LuminanceTests(unittest.TestCase):
    def test_black_is_zero(self):
        self.assertAlmostEqual(0.0, palette.luminance((0, 0, 0)), places=5)

    def test_white_is_one(self):
        self.assertAlmostEqual(1.0, palette.luminance((255, 255, 255)), places=5)

    def test_uses_rec601_weights(self):
        # 0.299*255/255 == 0.299 for pure red
        self.assertAlmostEqual(0.299, palette.luminance((255, 0, 0)), places=3)


class BandTests(unittest.TestCase):
    def test_below_first_edge_is_band_zero(self):
        self.assertEqual(0, palette.band_index(0.05, [0.10, 0.30, 0.55]))

    def test_above_last_edge_is_final_band(self):
        self.assertEqual(3, palette.band_index(0.99, [0.10, 0.30, 0.55]))

    def test_exactly_on_edge_takes_upper_band(self):
        self.assertEqual(1, palette.band_index(0.10, [0.10, 0.30, 0.55]))

    def test_middle_band(self):
        self.assertEqual(2, palette.band_index(0.40, [0.10, 0.30, 0.55]))


class RampTests(unittest.TestCase):
    def test_every_family_ramp_is_monotonic_in_luminance(self):
        pal = palette.load_palette()
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            lums = [palette.luminance(pal[c]) for c in spec["colors"]]
            self.assertEqual(sorted(lums), lums, f"{name} is not monotonic: {spec['colors']}")

    def test_every_team_ramp_is_monotonic_in_luminance(self):
        pal = palette.load_palette()
        for spec in palette.load_ramps()["teams"]:
            lums = [palette.luminance(pal[c]) for c in spec["colors"]]
            self.assertEqual(sorted(lums), lums, f"{spec['name']} is not monotonic")

    def test_edge_counts_match_colour_counts(self):
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            self.assertEqual(len(spec["colors"]) - 1, len(spec["edges"]), name)
        for spec in ramps["teams"]:
            self.assertEqual(len(spec["colors"]) - 1, len(spec["edges"]), spec["name"])

    def test_edges_are_ascending(self):
        ramps = palette.load_ramps()
        for name, spec in ramps["families"].items():
            self.assertEqual(sorted(spec["edges"]), spec["edges"], name)

    def test_all_ramp_tokens_resolve(self):
        pal = palette.load_palette()
        ramps = palette.load_ramps()
        for spec in list(ramps["families"].values()) + ramps["teams"]:
            for c in spec["colors"]:
                self.assertIn(c, pal)

    def test_eight_teams_with_unique_identity_colours(self):
        ramps = palette.load_ramps()
        self.assertEqual(8, len(ramps["teams"]))
        # A team's identity is the band it declares, not simply its brightest colour:
        # the mid band paints ~48% of a miner, the top band only a small highlight.
        identities = [t["colors"][t.get("identity", len(t["colors"]) - 1)]
                      for t in ramps["teams"]]
        self.assertEqual(8, len(set(identities)), identities)

    def test_no_team_ramp_uses_a_preserved_colour(self):
        """A team colour equal to the hard hat or skin would merge into the miner."""
        ramps = palette.load_ramps()
        reserved = set(ramps["tint_preserve"])
        for t in ramps["teams"]:
            for c in t["colors"]:
                self.assertNotIn(c, reserved, f"{t['name']} uses reserved {c}")

    def test_override_beats_glob(self):
        ramps = palette.load_ramps()
        self.assertEqual("stone", palette.ramp_for("objects/idol_odin.png", ramps))
        self.assertEqual("gold", palette.ramp_for("objects/idol_buddha.png", ramps))

    def test_first_matching_glob_wins(self):
        ramps = palette.load_ramps()
        self.assertEqual("flesh", palette.ramp_for("miners/burly/walk/e0.png", ramps))
        self.assertEqual("bone", palette.ramp_for("monsters/skeleton_human/walk/north_0.png", ramps))

    def test_flat_files_match_their_family_glob(self):
        # These sit directly in their family folder, not a subdirectory. fnmatch's '*'
        # spans '/', so 'monsters/skeleton_*' and 'miners/**' catch both shapes.
        ramps = palette.load_ramps()
        self.assertEqual("bone", palette.ramp_for("monsters/skeleton_bones_pile.png", ramps))
        self.assertEqual("flesh", palette.ramp_for("miners/miner_e.png", ramps))

    def test_unmatched_path_raises(self):
        ramps = palette.load_ramps()
        with self.assertRaises(KeyError):
            palette.ramp_for("nowhere/mystery.png", ramps)


class ExclusionTests(unittest.TestCase):
    def test_marketing_art_is_excluded(self):
        ramps = palette.load_ramps()
        self.assertTrue(palette.is_excluded("Splash.png", ramps))
        self.assertTrue(palette.is_excluded("Icon.png", ramps))

    def test_normal_assets_are_not_excluded(self):
        ramps = palette.load_ramps()
        self.assertFalse(palette.is_excluded("objects/chest.png", ramps))


class GeneratorTests(unittest.TestCase):
    def test_generated_palette_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render(palette.load_palette())
        with open(gen_palette_cs.OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Palette.g.cs is stale - run python -m tools.artpass.gen_palette_cs")

    def test_generated_ramps_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render_ramps(palette.load_ramps())
        with open(gen_palette_cs.RAMPS_OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Ramps.g.cs is stale - run python -m tools.artpass.gen_palette_cs")


if __name__ == "__main__":
    unittest.main()
