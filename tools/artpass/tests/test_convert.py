import unittest

from PIL import Image

from tools.artpass import convert, palette


def solid(size, rgba):
    return Image.new("RGBA", size, rgba)


def pixels(img):
    """Flat list of RGBA tuples. Avoids Image.getdata(), deprecated in Pillow 12."""
    px = img.load()
    return [px[x, y] for y in range(img.height) for x in range(img.width)]


class PosterizeTests(unittest.TestCase):
    def setUp(self):
        pal = palette.load_palette()
        self.colors = [pal["black"], pal["orange"], pal["yellow"]]
        self.edges = [0.20, 0.55]

    def test_transparent_pixels_stay_transparent(self):
        out = convert.posterize_image(solid((2, 2), (200, 200, 200, 0)), self.colors, self.edges)
        self.assertEqual((0, 0, 0, 0), out.getpixel((0, 0)))

    def test_dark_pixel_takes_first_ramp_colour(self):
        out = convert.posterize_image(solid((1, 1), (10, 10, 10, 255)), self.colors, self.edges)
        self.assertEqual(self.colors[0] + (255,), out.getpixel((0, 0)))

    def test_bright_pixel_takes_last_ramp_colour(self):
        out = convert.posterize_image(solid((1, 1), (250, 250, 250, 255)), self.colors, self.edges)
        self.assertEqual(self.colors[2] + (255,), out.getpixel((0, 0)))

    def test_alpha_is_forced_binary(self):
        img = Image.new("RGBA", (2, 1))
        img.putpixel((0, 0), (200, 200, 200, 127))  # below threshold
        img.putpixel((1, 0), (200, 200, 200, 128))  # at threshold
        out = convert.posterize_image(img, self.colors, self.edges)
        self.assertEqual(0, out.getpixel((0, 0))[3])
        self.assertEqual(255, out.getpixel((1, 0))[3])

    def test_output_only_uses_ramp_colours(self):
        img = Image.new("RGBA", (16, 16))
        for y in range(16):
            for x in range(16):
                img.putpixel((x, y), (x * 16, y * 16, (x + y) * 8, 255))
        out = convert.posterize_image(img, self.colors, self.edges)
        used = {p[:3] for p in pixels(out) if p[3]}
        self.assertTrue(used.issubset(set(self.colors)), used)

    def test_is_idempotent(self):
        img = Image.new("RGBA", (8, 8))
        for y in range(8):
            for x in range(8):
                img.putpixel((x, y), (x * 32, y * 32, 128, 255))
        once = convert.posterize_image(img, self.colors, self.edges)
        twice = convert.posterize_image(once, self.colors, self.edges)
        self.assertEqual(pixels(once), pixels(twice))


class HueSplitTests(unittest.TestCase):
    """Luminance alone cannot separate a miner's yellow hard hat from their skin -
    across the six variants they overlap, and on 'veteran' the hat is darker than the
    skin. One band may therefore be split by hue."""

    def setUp(self):
        pal = palette.load_palette()
        self.colors = [pal["black"], pal["brown"], pal["lightred"], pal["yellow"], pal["white"]]
        self.edges = [0.10, 0.30, 0.55, 0.84]
        self.split = {"band": 3, "hue_below": 0.085, "color": pal["lightred"]}

    def _one(self, rgb):
        img = Image.new("RGBA", (1, 1), rgb + (255,))
        out = convert.posterize_image(img, self.colors, self.edges, hue_split=self.split)
        return out.getpixel((0, 0))[:3]

    def test_skin_in_the_split_band_becomes_lightred(self):
        pal = palette.load_palette()
        for skin in [(0xD2, 0x9C, 0x84), (0xEE, 0xAD, 0x92), (0xDD, 0xA4, 0x93)]:
            self.assertEqual(pal["lightred"], self._one(skin), f"{skin} should read as skin")

    def test_hardhat_in_the_split_band_stays_yellow(self):
        pal = palette.load_palette()
        for hat in [(0xE6, 0xAE, 0x42), (0xF4, 0xBE, 0x27), (0xEA, 0x9C, 0x1C)]:
            self.assertEqual(pal["yellow"], self._one(hat), f"{hat} should read as hard hat")

    def test_other_bands_are_untouched_by_the_split(self):
        pal = palette.load_palette()
        self.assertEqual(pal["white"], self._one((0xDE, 0xDB, 0xD1)))   # shirt
        self.assertEqual(pal["black"], self._one((0x1A, 0x12, 0x16)))   # outline

    def test_no_split_leaves_the_band_alone(self):
        pal = palette.load_palette()
        img = Image.new("RGBA", (1, 1), (0xD2, 0x9C, 0x84, 255))
        out = convert.posterize_image(img, self.colors, self.edges)
        self.assertEqual(pal["yellow"], out.getpixel((0, 0))[:3])


class RegionTests(unittest.TestCase):
    """The terrain atlas packs five different terrains into one sheet - lava, rock,
    pit, shallow water and deep water. A single ramp turns lethal deep water into
    ordinary brown rock, so the sheet is converted in horizontal bands."""

    def setUp(self):
        pal = palette.load_palette()
        self.regions = [
            {"y": [0, 2], "colors": [pal["black"], pal["red"], pal["yellow"]],
             "edges": [0.10, 0.50], "hue_split": None},
            {"y": [2, 4], "colors": [pal["black"], pal["blue"], pal["cyan"]],
             "edges": [0.10, 0.50], "hue_split": None},
        ]

    def test_each_band_uses_its_own_ramp(self):
        pal = palette.load_palette()
        img = Image.new("RGBA", (1, 4), (255, 255, 255, 255))
        out = convert.posterize_regions(img, self.regions)
        self.assertEqual(pal["yellow"], out.getpixel((0, 0))[:3])
        self.assertEqual(pal["yellow"], out.getpixel((0, 1))[:3])
        self.assertEqual(pal["cyan"], out.getpixel((0, 2))[:3])
        self.assertEqual(pal["cyan"], out.getpixel((0, 3))[:3])

    def test_transparency_survives_regions(self):
        img = Image.new("RGBA", (1, 4), (255, 255, 255, 0))
        out = convert.posterize_regions(img, self.regions)
        self.assertEqual((0, 0, 0, 0), out.getpixel((0, 0)))

    def test_atlas_regions_tile_the_whole_sheet(self):
        """Every row of the real atlas must be covered exactly once."""
        ramps = palette.load_ramps()
        regions = ramps["region_overrides"]["tiles/combined_terrain_atlas.png"]
        covered = []
        for r in regions:
            covered.extend(range(r["y"][0], r["y"][1]))
        self.assertEqual(list(range(640)), sorted(covered))

    def test_deep_and_shallow_water_use_different_ramps(self):
        """Deep water is lethal; it must not look like shallow water."""
        ramps = palette.load_ramps()
        regions = ramps["region_overrides"]["tiles/combined_terrain_atlas.png"]
        by_row = {r["y"][0]: r["ramp"] for r in regions}
        self.assertNotEqual(by_row[384], by_row[512])


if __name__ == "__main__":
    unittest.main()
