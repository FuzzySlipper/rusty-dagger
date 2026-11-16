#!/usr/bin/env python3
"""A ground cover run rewrites its own section and keeps what relief.py made in it (#10859): the buildings' relief entries
and the shader their faces draw them with, untouched, and refuses to make a record's relief relief.py already makes.

    python3 -m unittest test_groundcover     # from authoring/tools/groundcover
"""
import unittest

import groundcover

MINE = f"{groundcover.MEDIA}/relief-102-1.png"
BUILDING = "worldrpg/packs/daggerfall.art-replacements/media/relief/relief-312-1.png"


def entry(path, **more):
    return {"path": path, "byteLength": 1, "sha256": "0" * 64, "width": 128, "height": 128, "tallest": 1} | more


class Section(unittest.TestCase):
    def setUp(self):
        self.previous = {"shader": {"path": "old"}, "grass": {"102": {}},
                         "relief": {"102-1": entry(MINE, kind="clods"), "312-1": entry(BUILDING, kind="plaster", depth=.02)},
                         "surfaceShader": {"path": "worldrpg/packs/daggerfall.art-replacements/media/relief/relief.wgsl"}}
        self.made = {"shader": {"path": "new"}, "grass": {}, "relief": {"102-1": entry(MINE, kind="stones")}}

    def test_a_run_replaces_its_own_entries_and_keeps_the_buildings_relief_and_their_shader_untouched(self):
        cover = groundcover.section(self.previous, self.made)
        self.assertEqual(cover["shader"], {"path": "new"})
        self.assertEqual(cover["relief"]["102-1"]["kind"], "stones")
        self.assertEqual(cover["relief"]["312-1"], self.previous["relief"]["312-1"])
        self.assertEqual(cover["surfaceShader"], self.previous["surfaceShader"])

    def test_a_dropped_ground_entry_goes_and_a_section_without_building_relief_gains_no_shader(self):
        cover = groundcover.section({"relief": {"102-3": entry(f"{groundcover.MEDIA}/relief-102-3.png")}}, self.made)
        self.assertEqual(set(cover["relief"]), {"102-1"})
        self.assertNotIn("surfaceShader", cover)

    def test_making_a_record_relief_py_makes_is_refused(self):
        self.made["relief"]["312-1"] = entry(f"{groundcover.MEDIA}/relief-312-1.png")
        with self.assertRaises(SystemExit):
            groundcover.section(self.previous, self.made)


if __name__ == "__main__":
    unittest.main()
