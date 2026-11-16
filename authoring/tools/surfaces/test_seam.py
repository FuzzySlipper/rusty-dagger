#!/usr/bin/env python3
"""A surface's wrap edge is a seam only where it changes more than the surface does elsewhere, and crunching keeps a
seamless edge seamless.

    python3 -m unittest test_seam     # from authoring/tools/surfaces
"""
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

import reduce


def noise(size, seed=1, grain=24):
    """Colour noise that wraps on both axes, as a seamless generation does, its blotches about size/grain across."""
    rng = np.random.default_rng(seed)
    field = ndimage.gaussian_filter(rng.random((size, size, 3)), (size / grain, size / grain, 0), mode="wrap")
    field = (field - field.min()) / (field.max() - field.min())
    return Image.fromarray((40 + field * 170).astype(np.uint8))


class Seam(unittest.TestCase):
    def test_a_wrapping_surface_is_no_seam_and_wraps(self):
        image = noise(64)
        self.assertLess(max(reduce.seam(image, 1)), reduce.SEAM)
        self.assertEqual(reduce.wraps(image), (True, True))

    def test_a_surface_cut_off_at_its_edge_is_a_seam_across_that_axis_only(self):
        pixels = np.asarray(noise(64)).copy()
        pixels[:, :32] = np.asarray(noise(64, seed=2))[:, :32]  # its left half from another surface
        image = Image.fromarray(pixels)
        self.assertGreaterEqual(max(reduce.seam(image, 1)), reduce.SEAM)
        self.assertEqual(reduce.wraps(image), (False, True))

    def test_a_crunch_wraps_where_the_generation_was_seamless(self):
        classic = noise(32, seed=3)
        with tempfile.TemporaryDirectory() as work:
            raw = Path(work) / "raw.png"
            noise(384, seed=4, grain=128).save(raw)  # fine grain, which a clamped resize and sharpening break at the edge
            seamless = reduce.crunch(raw, classic, "enable")
        self.assertEqual(seamless.size, (64, 64))
        self.assertLess(max(max(reduce.seam(seamless, axis)) for axis in (0, 1)), reduce.SEAM)


class Winter(unittest.TestCase):
    def test_the_classic_winters_snow_lies_over_the_refreshed_summer_texel_for_texel(self):
        summer = np.full((4, 4, 3), 60, np.uint8)
        winter = summer.copy()
        winter[0, 1] = 250  # one texel of snow
        refreshed = Image.fromarray(np.full((8, 8, 3), 100, np.uint8))
        out = np.asarray(reduce.winter(refreshed, Image.fromarray(summer), Image.fromarray(winter)))
        self.assertTrue((out[0:2, 2:4] == 250).all())
        self.assertEqual((out == 250).all(-1).sum(), 4)
        self.assertTrue((out[2:, :] == 100).all())
