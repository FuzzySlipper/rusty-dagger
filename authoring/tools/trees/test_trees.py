#!/usr/bin/env python3
"""trees.py's checks: a prop mesh is written only with sound normals, and rocks whose sprites are painted with
see-through pockets are built whole: the winter rock 507-4 on its foot (#10871), 506-3 in its sprite's proportions (#10874).

    python3 test_trees.py
"""
import hashlib, json, shutil, tempfile, unittest
from pathlib import Path

import numpy as np

import trees


class Meshes(unittest.TestCase):
    def test_a_mesh_with_a_normal_not_of_unit_length_or_not_finite_is_refused_by_name(self):
        folder = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, folder)
        for normal in ([0, 0, 0], [0, float("nan"), 1], [0, .5, 0]):
            mesh = {"height": 1, "parts": [{"role": "stone", "normals": [0, 1, 0, *normal]}]}
            with self.assertRaisesRegex(SystemExit, r"rock\.prop-mesh\.json: 1 of its stone normals are not of unit length or not finite"):
                trees.write_mesh(folder / "rock.prop-mesh.json", mesh)
            self.assertFalse((folder / "rock.prop-mesh.json").exists())
        trees.write_mesh(folder / "rock.prop-mesh.json", {"height": 1, "parts": [{"role": "stone", "normals": [0, 1, 0, 1, 0, 0]}]})
        self.assertTrue((folder / "rock.prop-mesh.json").exists())

    def test_palm_trunks_keep_the_whole_stem_between_soil_and_crown(self):
        # 500-15 had unused vertices throughout its gap. Only indexed bark can bridge it.
        art = json.loads(trees.PAYLOAD.read_bytes())["artReplacements"]["trees"]
        for key, crown in (("500-12", .72), ("500-14", .72), ("500-15", .72),
                           ("501-11", .48), ("501-13", .72), ("501-16", .48)):
            entry = art[key]
            mesh = json.loads((trees.CONTENT / entry["mesh"]["path"]).read_bytes())
            bark = next(part for part in mesh["parts"] if part["role"] == "bark")
            heights = np.array(bark["positions"][1::3]) / mesh["height"]
            faces = heights[np.array(bark["indices"]).reshape(-1, 3)]
            covered = 0
            for low, high in sorted(zip(faces.min(1), faces.max(1))):
                self.assertLessEqual(low, covered + 1e-6, f"{key} bark gap from {covered} to {low}")
                covered = max(covered, high)
            self.assertGreater(covered, crown, f"{key} trunk ends below the crown")

    def test_tree_replacements_have_indexed_support_from_the_foot_into_the_leaves(self):
        art = json.loads(trees.PAYLOAD.read_bytes())["artReplacements"]["trees"]
        keys = ["500-3", "500-13", "500-16", "500-18", "500-21", "500-25", "500-27",
                "501-20", "501-27", "501-28", "501-30",
                "502-13", "502-16", "502-17", "502-24", "502-25", "503-5", "503-13", "503-23",
                *(f"504-{r}" for r in trees.woodland_trees.FORMS)]
        for key in keys:
            with self.subTest(key=key):
                entry = art[key]
                mesh = json.loads((trees.CONTENT / entry["mesh"]["path"]).read_bytes())
                spans = []
                for part in mesh["parts"]:
                    heights = np.array(part["positions"][1::3]) / mesh["height"]
                    triangles = heights[np.array(part["indices"]).reshape(-1, 3)]
                    if part["role"] == "leaves":
                        lowest_leaf = triangles.min()
                    else:
                        # The tree on stone is supported by the boulder as well as its roots.
                        spans.extend(zip(triangles.min(1), triangles.max(1)))
                supported = 0
                for low, high in sorted(spans):
                    self.assertLessEqual(low, supported + 1e-6)
                    supported = max(supported, high)
                self.assertGreater(supported, lowest_leaf)

    def test_matching_paintings_use_the_same_media_but_their_own_placement(self):
        art = json.loads(trees.PAYLOAD.read_bytes())["artReplacements"]
        flats = {f"{f['replaces']['archive']}-{f['replaces']['record']}": f for f in art["flats"]}
        for key, source in trees.SHARED_TREES.items():
            with self.subTest(key=key):
                instance = art["trees"][key]
                original = art["trees"][source]
                self.assertEqual({k: v for k, v in original.items() if k != "placement"},
                                 {k: v for k, v in instance.items() if k != "placement"})
                self.assertEqual(trees.shared_tree(original, flats[key], instance), instance)
                hand = {"placement": {"height": 7.25, "foot": .1, "from": {"height": "hand", "foot": "hand"}}}
                self.assertEqual(trees.shared_tree(original, flats[key], hand)["placement"], hand["placement"])

    def test_the_winter_rock_painted_with_pockets_is_built_whole_on_its_foot(self):
        flat = next(f for f in json.loads(trees.PAYLOAD.read_text())["artReplacements"]["flats"] if f["replaces"] == {"archive": 507, "record": 4})
        rgba = trees.frame(flat)
        species, variant = trees.FORMS[507][4]
        mesh, _ = trees.rocks.nature_rock(species, int(hashlib.sha256(rgba.tobytes()).hexdigest()[:8], 16), rgba.astype(np.uint8), variant)
        part = mesh["parts"][0]
        normals = np.array(part["normals"]).reshape(-1, 3)
        self.assertTrue(np.allclose(np.linalg.norm(normals, axis=1), 1, atol=1e-3))
        # It sinks into the ground at its foot, as every rock does, where it had stood a fifth of its height off it.
        self.assertLess(min(part["positions"][1::3]), 0)

    def test_a_rock_painted_with_a_pocket_stands_in_its_sprite_s_proportions(self):
        # 506-3's sprite is an arch: shaped round its opening, the rock stood squat at 55% of the sprite's height (#10874).
        flat = next(f for f in json.loads(trees.PAYLOAD.read_text())["artReplacements"]["flats"] if f["replaces"] == {"archive": 506, "record": 3})
        rgba = trees.frame(flat)
        species, variant = trees.FORMS[506][3]
        mesh, _ = trees.rocks.nature_rock(species, int(hashlib.sha256(rgba.tobytes()).hexdigest()[:8], 16), rgba.astype(np.uint8), variant)
        positions = np.array(mesh["parts"][0]["positions"]).reshape(-1, 3)
        solid = trees.rocks.outline(rgba.astype(np.uint8))
        self.assertAlmostEqual((positions[:, 0].max() - positions[:, 0].min()) / mesh["height"], solid.shape[1] / solid.shape[0], delta=.1)
        self.assertGreater(mesh["height"], .8)


class Placement(unittest.TestCase):
    """A tree's placement is its own once written: a rebuild keeps it however its values were set, and only --recompute measures again."""
    FLAT = {"placement": {"size": {"height": 4.0}}}
    RGBA = np.zeros((100, 10, 4))
    MEASURED = {"height": 90, "foot": 98}   # a sprite 100 rows tall drawing 90 of them, its foot on row 98

    def test_a_new_tree_measures_its_height_and_foot_at_its_flats_size_and_marks_them_art(self):
        self.assertEqual({"height": 3.6, "foot": .04, "from": {"height": "art", "foot": "art"}}, trees.placement(self.FLAT, self.RGBA, self.MEASURED, None))

    def test_a_rebuild_keeps_every_value_the_payload_holds_whatever_set_it_and_fills_only_a_missing_one(self):
        old = {"placement": {"height": 7.0, "foot": 1.0, "from": {"height": "art", "foot": "hand"}}}
        # The sprite and its flat have changed since: nothing moves.
        self.assertEqual({"height": 7.0, "foot": 1.0, "from": {"height": "art", "foot": "hand"}}, trees.placement(self.FLAT, self.RGBA, self.MEASURED, old))
        partial = {"placement": {"height": 7.0, "from": {"height": "original"}}}
        self.assertEqual({"height": 7.0, "foot": .04, "from": {"height": "original", "foot": "art"}}, trees.placement(self.FLAT, self.RGBA, self.MEASURED, partial))

    def test_recompute_measures_every_value_again(self):
        old = {"placement": {"height": 7.0, "foot": 1.0, "from": {"height": "art", "foot": "hand"}}}
        self.assertEqual({"height": 3.6, "foot": .04, "from": {"height": "art", "foot": "art"}}, trees.placement(self.FLAT, self.RGBA, self.MEASURED, old, recompute=True))


if __name__ == "__main__":
    unittest.main()
