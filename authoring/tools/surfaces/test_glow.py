#!/usr/bin/env python3
"""A light's emission map holds its flame or glass and leaves the metal dark.

    python3 -m unittest test_glow     # from authoring/tools/surfaces
"""
import unittest

import numpy as np
from PIL import Image

import reduce

FLAME, GLASS, IRON = (250, 190, 80), (60, 120, 240), (40, 40, 40)


def sprite():
    pixels = np.zeros((12, 12, 4), np.uint8)
    pixels[..., :3], pixels[..., 3] = IRON, 255
    pixels[1:6, 1:6, :3] = FLAME   # the flame
    pixels[8, 8:10, :3] = FLAME    # a highlight on the iron, far smaller than the flame
    pixels[10, 0:6, :3] = GLASS
    return Image.fromarray(pixels, "RGBA")


class Glow(unittest.TestCase):
    def test_a_warm_light_keeps_its_flame_in_its_own_colour_and_loses_the_highlight(self):
        glow = np.asarray(reduce.glow(sprite()))
        self.assertEqual((glow[..., 3] > 0).sum(), 25)
        self.assertEqual(tuple(glow[3, 3, :3]), FLAME)
        self.assertEqual(glow[8, 8, 3], 0)

    def test_a_cool_light_takes_its_glass(self):
        self.assertEqual((np.asarray(reduce.glow(sprite(), cool=True))[..., 3] > 0).sum(), 6)
