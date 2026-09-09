import unittest

from PIL import Image

from tools.spritepass import posterize


def img_from(pixels, size):
    im = Image.new("RGBA", size, (0, 0, 0, 0))
    px = im.load()
    for (x, y), v in pixels.items():
        px[x, y] = v
    return im


def colors_of(im):
    px = im.load()
    return {px[x, y][:3] for y in range(im.height) for x in range(im.width) if px[x, y][3]}


class MapImageTests(unittest.TestCase):
    def setUp(self):
        self.pal = [(0, 0, 0), (120, 0, 0), (255, 255, 255)]

    def test_maps_each_pixel_to_the_nearest_palette_colour(self):
        im = img_from({(0, 0): (250, 250, 250, 255), (1, 0): (10, 2, 3, 255)}, (2, 1))
        out = posterize.map_image(im, self.pal)
        self.assertEqual((255, 255, 255), out.getpixel((0, 0))[:3])
        self.assertEqual((0, 0, 0), out.getpixel((1, 0))[:3])

    def test_transparent_pixels_stay_transparent(self):
        im = img_from({(0, 0): (200, 200, 200, 0)}, (1, 1))
        self.assertEqual((0, 0, 0, 0), posterize.map_image(im, self.pal).getpixel((0, 0)))

    def test_alpha_is_forced_binary(self):
        im = img_from({(0, 0): (200, 0, 0, 127), (1, 0): (200, 0, 0, 128)}, (2, 1))
        out = posterize.map_image(im, self.pal)
        self.assertEqual(0, out.getpixel((0, 0))[3])
        self.assertEqual(255, out.getpixel((1, 0))[3])

    def test_output_only_uses_palette_colours(self):
        im = Image.new("RGBA", (16, 16))
        for y in range(16):
            for x in range(16):
                im.putpixel((x, y), (x * 16, y * 16, (x * y) % 256, 255))
        self.assertTrue(colors_of(posterize.map_image(im, self.pal)) <= set(self.pal))

    def test_is_idempotent(self):
        im = Image.new("RGBA", (8, 8))
        for y in range(8):
            for x in range(8):
                im.putpixel((x, y), (x * 30, y * 30, 90, 255))
        once = posterize.map_image(im, self.pal)
        twice = posterize.map_image(once, self.pal)
        self.assertEqual(list(once.getdata()), list(twice.getdata()))

    def test_a_shared_palette_maps_a_colour_the_same_way_in_every_frame(self):
        """Per-frame palettes would make an animation shimmer; this is the guard."""
        a = img_from({(0, 0): (200, 10, 10, 255)}, (1, 1))
        b = img_from({(0, 0): (200, 10, 10, 255)}, (1, 1))
        self.assertEqual(posterize.map_image(a, self.pal).getpixel((0, 0)),
                         posterize.map_image(b, self.pal).getpixel((0, 0)))


class PaletteTests(unittest.TestCase):
    def test_build_palette_returns_at_most_n_colours(self):
        im = Image.new("RGBA", (32, 32))
        for y in range(32):
            for x in range(32):
                im.putpixel((x, y), (x * 8 % 256, y * 8 % 256, 128, 255))
        pal = posterize.build_palette([im], 12)
        self.assertLessEqual(len(pal), 12)
        self.assertGreater(len(pal), 0)

    def test_build_palette_ignores_transparent_pixels(self):
        im = Image.new("RGBA", (4, 4), (255, 0, 255, 0))  # all transparent magenta
        im.putpixel((0, 0), (10, 20, 30, 255))
        pal = posterize.build_palette([im], 8)
        self.assertEqual([(10, 20, 30)], pal)

    def test_exact_palette_lists_distinct_opaque_colours(self):
        im = img_from({(0, 0): (1, 2, 3, 255), (1, 0): (1, 2, 3, 255),
                       (2, 0): (9, 9, 9, 255), (3, 0): (5, 5, 5, 0)}, (4, 1))
        self.assertEqual({(1, 2, 3), (9, 9, 9)}, set(posterize.exact_palette([im])))

    def test_common_palette_keeps_the_most_used_colours(self):
        # 6 px of A, 3 of B, 1 of C -> top 2 is [A, B]
        im = Image.new("RGBA", (10, 1), (0, 0, 0, 0))
        for x in range(6):
            im.putpixel((x, 0), (10, 10, 10, 255))
        for x in range(6, 9):
            im.putpixel((x, 0), (20, 20, 20, 255))
        im.putpixel((9, 0), (30, 30, 30, 255))
        self.assertEqual([(10, 10, 10), (20, 20, 20)], posterize.common_palette([im], 2))

    def test_common_palette_is_deterministic_on_ties(self):
        im = Image.new("RGBA", (2, 1))
        im.putpixel((0, 0), (9, 9, 9, 255))
        im.putpixel((1, 0), (1, 1, 1, 255))
        self.assertEqual(posterize.common_palette([im], 2), posterize.common_palette([im], 2))

    def test_build_palette_is_deterministic(self):
        im = Image.new("RGBA", (16, 16))
        for y in range(16):
            for x in range(16):
                im.putpixel((x, y), (x * 16, y * 16, 60, 255))
        self.assertEqual(posterize.build_palette([im], 8), posterize.build_palette([im], 8))


class ProfileTests(unittest.TestCase):
    def test_singleton_fraction_flags_a_gradient(self):
        grad = Image.new("RGBA", (20, 20))
        for y in range(20):
            for x in range(20):
                grad.putpixel((x, y), (x * 12 % 256, y * 12 % 256, (x + y) * 6 % 256, 255))
        flat = Image.new("RGBA", (20, 20), (10, 20, 30, 255))
        self.assertGreater(posterize.profile(grad)["singleton"], 0.5)
        self.assertLess(posterize.profile(flat)["singleton"], 0.5)


if __name__ == "__main__":
    unittest.main()
