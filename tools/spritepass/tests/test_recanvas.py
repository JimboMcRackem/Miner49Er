import unittest

from PIL import Image

from tools.spritepass import recanvas


def sprite(canvas, box, colour=(200, 40, 40, 255)):
    """A canvas with an opaque rectangle at `box` = (x0, y0, x1, y1)."""
    im = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    px = im.load()
    for y in range(box[1], box[3]):
        for x in range(box[0], box[2]):
            px[x, y] = colour
    return im


class RecanvasTests(unittest.TestCase):
    def test_padding_keeps_content_the_same_pixel_size(self):
        """Padding must not rescale - the goat is drawn at the same size in both
        canvases, so its content has to survive untouched."""
        src = sprite(60, (25, 20, 35, 40))
        out = recanvas.resize_canvas(src, 68)
        self.assertEqual((68, 68), out.size)
        sb, ob = src.getbbox(), out.getbbox()
        self.assertEqual(sb[2] - sb[0], ob[2] - ob[0])
        self.assertEqual(sb[3] - sb[1], ob[3] - ob[1])

    def test_padding_keeps_content_centred(self):
        src = sprite(60, (25, 25, 35, 35))
        out = recanvas.resize_canvas(src, 68)
        bb = out.getbbox()
        self.assertEqual(68 - bb[2], bb[0])
        self.assertEqual(68 - bb[3], bb[1])

    def test_cropping_keeps_content_the_same_pixel_size(self):
        src = sprite(52, (20, 20, 32, 30))
        out = recanvas.resize_canvas(src, 48)
        self.assertEqual((48, 48), out.size)
        sb, ob = src.getbbox(), out.getbbox()
        self.assertEqual(sb[2] - sb[0], ob[2] - ob[0])
        self.assertEqual(sb[3] - sb[1], ob[3] - ob[1])

    def test_cropping_that_would_clip_content_raises(self):
        """Silently clipping a sprite would be worse than failing loudly."""
        src = sprite(52, (0, 0, 52, 52))
        with self.assertRaises(ValueError):
            recanvas.resize_canvas(src, 48)

    def test_no_change_when_already_the_target_size(self):
        src = sprite(48, (10, 10, 20, 20))
        out = recanvas.resize_canvas(src, 48)
        self.assertEqual(list(src.getdata()), list(out.getdata()))

    def test_pixels_are_never_recoloured(self):
        src = sprite(60, (25, 20, 35, 40), colour=(1, 2, 3, 255))
        out = recanvas.resize_canvas(src, 68)
        px = out.load()
        seen = {px[x, y] for y in range(68) for x in range(68) if px[x, y][3]}
        self.assertEqual({(1, 2, 3, 255)}, seen)

    def test_is_idempotent(self):
        src = sprite(60, (25, 20, 35, 40))
        once = recanvas.resize_canvas(src, 68)
        twice = recanvas.resize_canvas(once, 68)
        self.assertEqual(list(once.getdata()), list(twice.getdata()))


if __name__ == "__main__":
    unittest.main()
