#!/usr/bin/env python3
"""Every area promotes through promote_area, so this holds its rules on a stand-in area in a scratch pack: a key replaces
its units' entries and files and keeps the rest byte for byte, a whole-area run refuses while the ledger accepts a final
the checkout lacks, a refused run changes nothing, and promoting by key and then the whole area equals one whole run.

    python3 -m unittest test_promote     # from authoring/tools/shared
"""
import json, shutil, tempfile, unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path

import numpy as np
from PIL import Image

import promote

AREA = "stand-in"
MEDIA = f"{promote.MEDIA}/{AREA}"


def units():
    """Each ledger key is a unit drawing its own final, but `pair-1` and `pair-2`, which share the final `pair`."""
    def build(key, final):
        return lambda: [("standIns", {"replaces": key, **promote.write(f"{MEDIA}/{key}.bin", final.path.read_bytes())})]
    notes = promote.ledger(AREA)
    found = {}
    for key in sorted(notes):
        final = promote.generated(AREA, key, ".bin")
        for unit in ([f"{key}-1", f"{key}-2"] if key == "pair" else [key]):
            found[unit] = promote.Unit([final], build(unit, final))
    return found


class Promotion(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        for name, value in (("CONTENT", self.root / "content"), ("PAYLOAD", self.root / "content/payload.json"),
                            ("REPLACED", self.root / "replaced-classic.json"), ("ART", self.root / "art"), ("AUTHORING", self.root / "authoring")):
            self.addCleanup(setattr, promote, name, getattr(promote, name))
            setattr(promote, name, value)
        self.addCleanup(promote.AREAS.pop, AREA, None)
        promote.AREAS[AREA] = promote.Area([AREA], {"standIns": lambda entry: entry["replaces"]}, units)
        (self.root / "authoring/ledger").mkdir(parents=True)
        (self.root / f"art/generated/{AREA}").mkdir(parents=True)
        (self.root / "content").mkdir()
        self.ledger({"a": "accepted", "b": "accepted", "pair": "accepted", "c": "rejected"})
        for key in ("a", "b", "pair", "c"):
            self.final(key, f"{key} first")
        self.run_promote()
        # Another area's section, which no run of this one touches.
        payload = json.loads(promote.PAYLOAD.read_text())
        payload["artReplacements"]["other"] = [{"path": f"{promote.MEDIA}/other/x.bin"}]
        promote.PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")

    def ledger(self, statuses):
        (self.root / f"authoring/ledger/{AREA}-notes.json").write_text(json.dumps({key: {"status": status} for key, status in statuses.items()}))

    def final(self, key, text):
        (self.root / f"art/generated/{AREA}/{key}.bin").write_text(text)

    def run_promote(self, *keys):
        with redirect_stdout(StringIO()) as out:
            promote.main(["--area", AREA, *(arg for key in keys for arg in ("--key", key))])
        return out.getvalue()

    def pack(self):
        """Every file of the scratch pack, by path, with its bytes."""
        return {path.relative_to(self.root).as_posix(): path.read_bytes() for path in sorted((self.root / "content").rglob("*")) if path.is_file()}

    def test_a_whole_run_promotes_every_unit_the_ledger_accepts(self):
        entries = json.loads(promote.PAYLOAD.read_text())["artReplacements"]["standIns"]
        self.assertEqual([entry["replaces"] for entry in entries], ["a", "b", "pair-1", "pair-2"])
        self.assertEqual((self.root / f"content/{MEDIA}/a.bin").read_text(), "a first")

    def test_a_key_replaces_its_unit_and_keeps_every_other_entry_and_file(self):
        before = self.pack()
        self.final("a", "a second")
        (self.root / f"art/generated/{AREA}/b.bin").unlink()
        self.run_promote("a")
        after = self.pack()
        changed = {path for path in before.keys() | after.keys() if before.get(path) != after.get(path)}
        self.assertEqual(changed, {"content/payload.json", f"content/{MEDIA}/a.bin"})
        self.assertEqual(after[f"content/{MEDIA}/a.bin"], b"a second")
        old, new = (json.loads(pack["content/payload.json"])["artReplacements"] for pack in (before, after))
        self.assertEqual([entry for entry in new["standIns"] if entry["replaces"] != "a"],
                         [entry for entry in old["standIns"] if entry["replaces"] != "a"])
        self.assertEqual(new["other"], old["other"])

    def test_units_that_share_a_final_promote_together(self):
        self.final("pair", "pair second")
        self.run_promote("pair-1")
        entries = {entry["replaces"]: entry for entry in json.loads(promote.PAYLOAD.read_text())["artReplacements"]["standIns"]}
        self.assertEqual(entries["pair-1"]["sha256"], entries["pair-2"]["sha256"])
        self.assertEqual((self.root / f"content/{MEDIA}/pair-2.bin").read_text(), "pair second")

    def test_a_whole_run_refuses_and_changes_nothing_while_a_final_the_ledger_accepts_is_missing(self):
        before = self.pack()
        self.final("a", "a second")
        (self.root / f"art/generated/{AREA}/b.bin").unlink()
        with self.assertRaises(SystemExit) as refusal:
            self.run_promote()
        self.assertIn("no final for b", str(refusal.exception))
        self.assertIn("--key", str(refusal.exception))
        self.assertEqual(self.pack(), before)

    def test_a_key_refuses_a_unit_the_ledger_does_not_accept_or_the_checkout_lacks(self):
        before = self.pack()
        (self.root / f"art/generated/{AREA}/b.bin").unlink()
        for key in ("b", "c"):
            with self.assertRaises(SystemExit):
                self.run_promote("a", key)
        with self.assertRaises(SystemExit):
            self.run_promote("nothing")
        self.assertEqual(self.pack(), before)

    def test_a_whole_run_names_and_removes_an_entry_the_ledger_no_longer_accepts(self):
        self.ledger({"a": "accepted", "b": "rejected", "pair": "accepted", "c": "rejected"})
        said = self.run_promote()
        self.assertIn("removes b", said)
        self.assertNotIn("b", [entry["replaces"] for entry in json.loads(promote.PAYLOAD.read_text())["artReplacements"]["standIns"]])
        self.assertFalse((self.root / f"content/{MEDIA}/b.bin").exists())

    def test_a_key_then_the_whole_area_equals_one_whole_run(self):
        self.final("a", "a second")
        self.final("b", "b second")
        self.run_promote("a")
        self.run_promote()
        by_key = self.pack()
        self.setUp()
        self.final("a", "a second")
        self.final("b", "b second")
        self.run_promote()
        self.assertEqual(self.pack(), by_key)


class WorldMesh(unittest.TestCase):
    def test_a_sprite_mesh_states_the_slots_that_carry_an_emissive_map_and_leaves_its_width_to_the_glb(self):
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root)
        self.addCleanup(setattr, promote, "STAGE", promote.STAGE)
        promote.STAGE = root / "stage"

        def glb(name, materials):
            text = json.dumps({"asset": {"version": "2.0"}, "materials": materials}).encode()
            text += b" " * (-len(text) % 4)
            (root / name).write_bytes(b"glTF" + (2).to_bytes(4, "little") + (20 + len(text)).to_bytes(4, "little")
                                      + len(text).to_bytes(4, "little") + b"JSON" + text)
            return promote.Final(None, name, root / name)
        lamp = promote.world_mesh_entry(glb("lamp.glb", [{"name": "post"}, {"name": "glass", "emissiveTexture": {"index": 0}}]), 210, 28)
        self.assertEqual(({"archive": 210, "record": 28}, [1]), (lamp["replaces"], lamp["glow"]))
        # The game draws a mesh at its sprite's width from the GLB's own width, as the Engine admits it.
        self.assertNotIn("footprint", lamp)
        self.assertEqual((root / "lamp.glb").read_bytes(), (promote.STAGE / lamp["path"]).read_bytes())
        self.assertNotIn("glow", promote.world_mesh_entry(glb("cart.glb", [{"name": "wood"}]), 205, 27))

    def test_a_sprite_mesh_with_a_normal_of_no_length_or_not_finite_is_refused_by_name(self):
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root)

        def glb(name, normals):
            binary = np.array(normals, np.float32).tobytes()
            gltf = {"asset": {"version": "2.0"}, "buffers": [{"byteLength": len(binary)}],
                    "bufferViews": [{"buffer": 0, "byteLength": len(binary)}],
                    "accessors": [{"bufferView": 0, "componentType": 5126, "count": len(normals), "type": "VEC3"}],
                    "meshes": [{"primitives": [{"attributes": {"NORMAL": 0}}]}]}
            text = json.dumps(gltf).encode()
            text += b" " * (-len(text) % 4)
            chunks = len(text).to_bytes(4, "little") + b"JSON" + text + len(binary).to_bytes(4, "little") + b"BIN\0" + binary
            (root / name).write_bytes(b"glTF" + (2).to_bytes(4, "little") + (12 + len(chunks)).to_bytes(4, "little") + chunks)
            return promote.Final(None, name, root / name)
        for normal in ([0, 0, 0], [0, float("nan"), 1], [0, .5, 0]):
            with self.assertRaisesRegex(SystemExit, r"rock\.glb: 1 of its normals are not of unit length or not finite"):
                promote.world_mesh_entry(glb("rock.glb", [[0, 1, 0], normal]), 213, 11)
        self.assertEqual(2, len(promote.glb_normals(*self.read(root / "rock.glb"))))

    @staticmethod
    def read(path):
        data = path.read_bytes()
        return json.loads(data[20:20 + int.from_bytes(data[12:16], "little")]), data


class Placements(unittest.TestCase):
    """A flat and a person state their own placement, taken from the original once and kept after."""
    CLASSIC = {"x": .25, "y": .5, "width": .5, "height": .25}

    def setUp(self):
        self.addCleanup(promote.BILLBOARDS.clear)
        promote.BILLBOARDS.update({
            (210, 3): {"worldSize": {"x": 2, "y": 3}, "pivot": {"x": .5, "y": .5}, "playback": {"framesPerSecond": 5}},
            (210, 4): {"worldSize": {"x": 1, "y": 2}, "pivot": {"x": .5, "y": 0}, "playback": None}})
        self.addCleanup(promote.KEPT.clear)

    def test_a_flat_takes_its_size_pivot_foot_and_rate_from_the_classic_frame_in_its_atlas_frame(self):
        placement = promote.flat_placement(210, 3, self.CLASSIC, 3)
        self.assertEqual({"size": {"width": 4, "height": 12}, "pivot": {"x": .5, "y": .375}, "foot": {"x": .5, "y": .25}, "framesPerSecond": 5}, placement)
        # A person has no rate, and stands on the middle of its atlas frame's bottom.
        self.assertEqual({"x": .5, "y": 0}, promote.flat_placement(210, 3, self.CLASSIC, 3, person=True)["foot"])
        self.assertNotIn("framesPerSecond", promote.flat_placement(210, 3, self.CLASSIC, 3, person=True))
        with self.assertRaisesRegex(SystemExit, "no playback"):
            promote.flat_placement(210, 4, self.CLASSIC, 2)

    def test_a_promotion_keeps_the_values_an_entry_has_and_a_recompute_takes_them_again(self):
        kept = {"size": {"width": 9, "height": 9}, "pivot": {"x": 0, "y": 0}, "foot": {"x": 0, "y": 0}, "from": {"size": "hand", "pivot": "original", "foot": "original"}}
        promote.KEPT["flat.210.4"] = kept
        names = ["size", "pivot", "foot"]
        self.assertIs(kept, promote.own("flat.210.4", names, lambda: promote.flat_placement(210, 4, self.CLASSIC, 1)))
        promote.RECOMPUTE.add("flat.210.4")
        self.addCleanup(promote.RECOMPUTE.clear)
        again = promote.own("flat.210.4", names, lambda: promote.flat_placement(210, 4, self.CLASSIC, 1))
        self.assertEqual(8, again["size"]["height"])   # the classic 2 m over the .25 of the frame it takes
        self.assertEqual({"size": "original", "pivot": "original", "foot": "original"}, again["from"])


class OwnArt(unittest.TestCase):
    """A worn entry keeps its dye mask with its classic images moved out of staging, and a worn mask or a window's glass
    is the entry's own: written from a final drawn for this art, kept without one, refused when missing or for other art."""
    HELM, CLOAK = "texture-247-record-032-frame-000", "texture-235-record-022-frame-000"

    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        catalogue = {"paperDoll": {"items": [
            {"archive": 247, "record": 32, "dyes": {"steel": "paper-doll.247.32", "daedric": "paper-doll.247.32.daedric"}, "maskMediaId": "paper-doll.247.32.mask"},
            {"archive": 235, "record": 22, "dyes": {"blue": "paper-doll.235.22"}}]}}
        (self.root / "imported.json").write_text(json.dumps(catalogue))
        for name, value in (("IMPORTED", self.root / "imported.json"), ("STAGED", self.root / "staging"), ("CLASSIC_ART", self.root / "classic-art"),
                            ("MASKS", self.root / "mask"), ("GLASS", self.root / "glass"), ("STAGE", self.root / "stage")):
            self.addCleanup(setattr, promote, name, getattr(promote, name))
            setattr(promote, name, value)
        for cache in (promote.paper_doll_items,):
            cache.cache_clear()
            self.addCleanup(cache.cache_clear)
        for kept in (promote.KEPT, promote.KEPT_MASK, promote.KEPT_GLASS):
            self.addCleanup(kept.clear)
        promote.MASKS.mkdir(), promote.GLASS.mkdir()
        pixels = np.zeros((40, 30, 4), np.uint8)
        pixels[5:35, 5:25] = (120, 120, 120, 255)
        self.item = self.root / f"{self.HELM}.png"
        Image.fromarray(pixels, "RGBA").save(self.item)

    def promote_helm(self):
        return promote.promote("worn", self.HELM, self.item, rect=(40, 10, 30, 40))

    def draw_mask(self, art):
        Image.fromarray(np.full((8, 12, 4), 255, np.uint8), "RGBA").save(promote.MASKS / f"{self.HELM}.png")
        (promote.MASKS / f"{self.HELM}.json").write_text(json.dumps({"rect": [38, 6, 3, 2], "item": promote.pixels(art)}))

    def test_a_worn_entry_keeps_its_dye_mask_with_the_classic_images_moved_out_of_staging(self):
        self.draw_mask(Image.open(self.item))
        self.assertIn("dyeMask", self.promote_helm())   # the catalogue names a daedric image apart from the steel one

    def test_a_masking_record_takes_the_mask_drawn_for_its_art_and_keeps_it_on_a_re_promote(self):
        self.draw_mask(Image.open(self.item))
        entry = self.promote_helm()
        self.assertEqual("refreshed.paper-doll.247.32.mask", entry["mask"]["mediaId"])
        self.assertEqual({"mask": {"x": 38, "y": 6, "width": 3, "height": 2}, "from": {"mask": "hand"}}, entry["placement"])
        # Without its final, a re-promote keeps the mask the payload holds.
        for path in promote.MASKS.iterdir():
            path.unlink()
        promote.KEPT["paper-doll.247.32"], promote.KEPT_MASK["paper-doll.247.32"] = entry["placement"], entry["mask"]
        kept = self.promote_helm()
        self.assertEqual((entry["placement"], entry["mask"]), (kept["placement"], kept["mask"]))

    def test_a_masking_record_without_its_own_mask_or_with_one_drawn_for_other_art_is_refused_by_name(self):
        with self.assertRaisesRegex(SystemExit, f"{self.HELM} replaces paper-doll.247.32"):
            self.promote_helm()
        self.draw_mask(Image.new("RGBA", (30, 40)))
        with self.assertRaisesRegex(SystemExit, "drawn for other art"):
            self.promote_helm()

    def test_a_window_takes_the_glass_cut_from_its_surface_and_keeps_it_only_with_that_surface(self):
        window = promote.WINDOW_GLASS.format(archive=9, record=3)
        (promote.CLASSIC_ART / window).parent.mkdir(parents=True)
        (promote.CLASSIC_ART / window).write_bytes(b"classic glass")   # moved out of staging by the import
        surface = Image.new("RGBA", (8, 16), (90, 60, 30, 255))
        entry = {"sha256": "this surface"}
        with self.assertRaisesRegex(SystemExit, "texture-009-3 is a window"):
            promote.window_glass("surfaces", "texture-009-3", 9, 3, surface, entry)
        Image.new("RGBA", (8, 16), (255, 255, 255, 255)).save(promote.GLASS / "texture-009-3.png")
        (promote.GLASS / "texture-009-3.json").write_text(json.dumps({"surface": promote.pixels(surface)}))
        glass = promote.window_glass("surfaces", "texture-009-3", 9, 3, surface, entry)["glass"]
        self.assertEqual((8, 16), (glass["width"], glass["height"]))
        with self.assertRaisesRegex(SystemExit, "cut from other art"):
            promote.window_glass("surfaces", "texture-009-3", 9, 3, Image.new("RGBA", (8, 16)), entry)
        # Without its final, the glass the payload holds is kept with the surface it was cut for, never with a redrawn one.
        for path in promote.GLASS.iterdir():
            path.unlink()
        promote.KEPT_GLASS[(9, 3)] = (glass, "this surface")
        self.assertEqual({"glass": glass}, promote.window_glass("surfaces", "texture-009-3", 9, 3, surface, entry))
        with self.assertRaisesRegex(SystemExit, "its earlier art"):
            promote.window_glass("surfaces", "texture-009-3", 9, 3, surface, {"sha256": "a redrawn surface"})
        # A record that is no window has no glass.
        self.assertEqual({}, promote.window_glass("surfaces", "texture-009-4", 9, 4, surface, entry))


if __name__ == "__main__":
    unittest.main()
