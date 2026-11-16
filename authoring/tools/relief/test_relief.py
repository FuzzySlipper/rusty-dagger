#!/usr/bin/env python3
"""A building relief recipe makes a texture's outlines, mortar and openings the floor and raises its shapes over a bevel to
their face, tiling as the texture does; shingles rise course by course (#10859).

    python3 -m unittest test_relief     # from authoring/tools/relief
"""
import unittest

import numpy as np

import relief


def stones():
    """Light stones 16 texels square in dark mortar a texel wide, and one stone a black opening, as a window's glass."""
    rng = np.random.default_rng(1)
    rgb = .55 + rng.random((64, 64, 3)) * .05
    rgb[::16, :] = rgb[:, ::16] = .08
    rgb[17:32, 17:32] = 0
    return rgb


class Recipe(unittest.TestCase):
    def test_mortar_and_openings_are_the_floor_and_a_stone_rises_over_its_bevel_to_its_face(self):
        # Every colour region at one level, so only the outlines and the bevel shape the height.
        h, _ = relief.recipe_height(stones(), relief.RECIPES["roles"]["stone"] | {"levels": [1, 1, 1]})
        self.assertTrue((h[::16, :] == 0).all() and (h[:, ::16] == 0).all(), "mortar is the floor")
        self.assertTrue((h[18:31, 18:31] == 0).all(), "an opening is the floor")
        self.assertEqual(h[40, 40], 1)
        self.assertLess(h[33, 40], h[40, 40], "a stone's edge is lower than its face")
        # The texture tiles, so a stone at its wrap edge rises from the mortar beyond the edge as one inside does.
        self.assertAlmostEqual(h[40, 63], h[40, 47])
        self.assertAlmostEqual(h[40, 63], h[40, 49])

    def test_shingles_rise_from_the_course_line_above_to_their_own_lower_edge(self):
        rgb = np.full((64, 32, 3), .5)
        rgb[::16] = .15
        h, _ = relief.recipe_height(rgb, relief.RECIPES["roles"]["shingles"])
        column = h[:, 8]
        self.assertEqual(column[16], 0)
        self.assertTrue(np.all(np.diff(column[18:31]) > 0), "a course rises toward its lower edge")


if __name__ == "__main__":
    unittest.main()
