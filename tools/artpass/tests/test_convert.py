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


if __name__ == "__main__":
    unittest.main()
