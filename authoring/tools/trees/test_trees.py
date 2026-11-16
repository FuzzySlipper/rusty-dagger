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


if __name__ == "__main__":
    unittest.main()
