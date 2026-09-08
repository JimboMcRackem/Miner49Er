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


class GeneratorTests(unittest.TestCase):
    def test_generated_cs_is_current(self):
        from tools.artpass import gen_palette_cs
        expected = gen_palette_cs.render(palette.load_palette())
        with open(gen_palette_cs.OUT_PATH, encoding="utf-8") as fh:
            self.assertEqual(expected, fh.read(),
                             "C64Palette.g.cs is stale - run python -m tools.artpass.gen_palette_cs")


if __name__ == "__main__":
    unittest.main()
