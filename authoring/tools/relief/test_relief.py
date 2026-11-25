#!/usr/bin/env python3
"""A building relief recipe makes a texture's outlines, mortar and openings the floor and raises its shapes over a bevel to
their face, tiling as the texture does; shingles rise course by course (#10859).

    python3 -m unittest test_relief     # from authoring/tools/relief
"""
import unittest

import numpy as np

import interior_relief
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

    def test_framed_art_raises_outer_frame_and_keeps_canvas_below_extrude_cutoff(self):
        rgb = np.full((64, 80, 3), 0.5)
        h, _, kind, depth, extrude = interior_relief.interior_height(
            rgb, interior_relief.role_of(348, 0), db=np.full((64, 80), 0.5))
        self.assertEqual(kind, "framed-art")
        self.assertGreater(depth, 0)
        self.assertGreater(extrude, 0)
        self.assertGreater(h[3, 40], 0.70, "frame moulding stands proud")
        self.assertLess(h[32, 40], 0.35, "painting canvas stays flush below default extrude cutoff")

    def test_timber_plaster_raises_dark_beams_above_light_plaster(self):
        rgb = np.full((64, 64, 3), 0.78)
        rgb[:, 0:12] = np.array([0.24, 0.16, 0.10])
        rgb[0:12, :] = np.array([0.24, 0.16, 0.10])
        h, _, _, _, extrude = interior_relief.interior_height(
            rgb, interior_relief.role_of(360, 0), db=np.full((64, 64), 0.6))
        self.assertGreater(extrude, 0)
        self.assertGreater(h[6, 36], 0.70, "dark timber beam stands proud")
        self.assertLess(h[36, 36], 0.35, "light plaster panel stays flush below default extrude cutoff")

    def test_niche_shelf_recesses_box_while_surrounding_masonry_stands_proud(self):
        rgb = stones()
        role = interior_relief.role_of(337, 0) | {"recessBox": [16, 16, 48, 48]}
        h, _, _, _, extrude = interior_relief.interior_height(rgb, role, db=np.full((64, 64), 0.7))
        self.assertGreater(extrude, 0)
        self.assertLess(h[32, 32], 0.32, "recessed niche stays below default extrude cutoff")
        self.assertGreater(h[8, 8], 0.60, "surrounding masonry stands proud")

    def test_flat_role_stays_flush_with_zero_extrude_and_no_exterior_depth(self):
        rgb = np.full((32, 32, 3), 0.5)
        h, _, kind, depth, extrude = interior_relief.interior_height(rgb, interior_relief.role_of(57, 0))
        self.assertEqual(kind, "flat")
        self.assertIsNone(depth)
        self.assertEqual(extrude, 0.0)
        self.assertTrue(np.allclose(h, 0.28))


if __name__ == "__main__":
    unittest.main()
