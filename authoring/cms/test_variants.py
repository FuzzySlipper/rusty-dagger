"""Unit tests for Tier 3 World Block Variant recipes, model search, baking, and people-slot spatial editing."""

import json
from pathlib import Path
import sys
from tempfile import TemporaryDirectory
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))

from cache import FileCache
from library import Library
from packs import PackStore, ValidationError
from server import Workspace, create_router
from variants import WorldVariantsStore


def _write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


class WorldVariantsAndSlotEditTests(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory(prefix="cms-tier3-test-")
        self.root = Path(self.tmp.name)
        self.npcs = self.root / "npcs"
        self.npcs.mkdir()
        _write_json(self.npcs / "index.json", {"npcs": []})
        self.pipeline = self.root / "pipeline"
        self.pipeline.mkdir()
        self.links = self.root / "links.json"
        _write_json(self.links, {})
        self.merges = self.root / "merges.json"
        _write_json(self.merges, {"merges": []})

        # Minimal packs dir with daggerfall.quests + daggerfall.base
        self.packs_root = self.root / "content/worldrpg/packs"
        _write_json(
            self.packs_root / "daggerfall.base/payload.json",
            {
                "factions": {
                    "factions": [
                        {"id": 100, "name": "Town Guard", "type": 1},
                        {"id": 510, "name": "Some Individual", "type": 4},
                    ]
                }
            },
        )
        _write_json(
            self.packs_root / "daggerfall.quests/payload.json",
            {
                "worldVariants": {
                    "blocks": [
                        {
                            "block": "MARKAA01.RMB",
                            "variant": "closed",
                            "remove": ["model/markaa01-rmb/misc/1"],
                            "add": [
                                {
                                    "model": "41214",
                                    "position": {"x": 19.6, "y": 0.875, "z": -52.0},
                                    "yawDegrees": 0,
                                }
                            ],
                        }
                    ]
                }
            },
        )
        _write_json(
            self.packs_root / "daggerfall.quests/variants/rmb/markaa01-rmb/exterior-variant-closed.json",
            {
                "key": "rmb/markaa01-rmb/exterior-variant-closed",
                "baseDigest": "abc123def456",
                "models": [{"id": "m1"}, {"id": "m2"}],
            },
        )

        # Minimal daggerfall.people pack
        self.people_pack = self.packs_root / "daggerfall.people"
        _write_json(
            self.people_pack / "cast/individuals.json",
            {
                "individuals": [
                    {
                        "id": "lady-brisienna",
                        "factionId": 510,
                        "name": "Lady Brisienna",
                        "gender": "Female",
                        "visual": {"archive": 182, "record": 10},
                        "homeBlock": "ALCHAL00.RMB",
                        "homeSlotId": "b:0:p00",
                        "interaction": "full-dialogue",
                        "harm": "protected",
                    }
                ]
            },
        )
        _write_json(
            self.people_pack / "spawners.json",
            {
                "spawners": [
                    {
                        "id": "house-resident",
                        "role": "resident",
                        "interaction": "full-dialogue",
                        "harm": "law-protected-civilian",
                        "deriveRegionalFaction": True,
                        "visualMode": "pool",
                        "defaultPool": [{"archive": 184, "record": 20, "gender": "Male", "weight": 1}],
                    },
                    {
                        "id": "shopkeeper",
                        "role": "merchant",
                        "interaction": "full-dialogue",
                        "harm": "law-protected-civilian",
                        "deriveRegionalFaction": False,
                        "visualMode": "pool",
                        "defaultPool": [{"archive": 184, "record": 1, "gender": "Male", "weight": 1}],
                    },
                ]
            },
        )
        _write_json(
            self.people_pack / "blocks/rmb-interiors.json",
            {
                "kind": "rmb-interior",
                "blocks": {
                    "ALCHAL00.RMB": {
                        "0": {
                            "buildingType": 0,
                            "buildingFactionId": 100,
                            "slots": [
                                {
                                    "slotId": "b:0:p00",
                                    "spawner": "house-resident",
                                    "position": [-1.8, 0.0, 1.2],
                                    "interaction": "full-dialogue",
                                    "deriveRegionalFaction": True,
                                    "visual": {"archive": 184, "record": 20, "gender": "Male"},
                                }
                            ],
                        }
                    }
                },
            },
        )

        # Shared geometry for model picker search
        self.shared = self.root / "shared"
        (self.shared / "geometry").mkdir(parents=True)
        (self.shared / "geometry/mesh-41214.rstatmsh").write_bytes(b"stub")
        (self.shared / "geometry/mesh-41221.rstatmsh").write_bytes(b"stub")
        self.classic_shared = self.root / "classic-shared"
        (self.classic_shared / "geometry").mkdir(parents=True)
        (self.classic_shared / "geometry/mesh-500.rstatmsh").write_bytes(b"stub")

        self.library = Library(
            self.npcs,
            self.pipeline,
            repo=self.root,
            people_pack_root=self.people_pack,
            world_shared_root=self.shared,
            world_classic_shared_root=self.classic_shared,
            packs_dir=self.packs_root,
            arena2_dir=self.root / "missing-arena2",
        )
        self.workspace = Workspace(self.library, self.links, self.merges)
        self.router = create_router(self.workspace)

    def tearDown(self):
        self.tmp.cleanup()

    def _call(self, method: str, path: str, body=None):
        raw = json.dumps(body).encode("utf-8") if body is not None else b""
        resp = self.router.dispatch(method, path, raw)
        self.assertIsNotNone(resp)
        payload, _ = resp.encode()
        return resp.status, json.loads(payload.decode("utf-8"))

    def test_list_create_update_delete_variant(self):
        status, data = self._call("GET", "/api/world-variants?block=MARKAA01.RMB")
        self.assertEqual(status, 200)
        self.assertEqual(len(data["variants"]), 1)
        self.assertEqual(data["variants"][0]["variant"], "closed")
        self.assertTrue(data["variants"][0]["bakedExists"])
        self.assertEqual(data["variants"][0]["baseDigest"], "abc123def456")

        # Create a second variant on the same block
        status, saved = self._call(
            "PUT",
            "/api/world-variants",
            {
                "packId": "daggerfall.quests",
                "block": "MARKAA01.RMB",
                "variant": "festival",
                "remove": ["model/markaa01-rmb/misc/2"],
                "add": [
                    {"model": "41221", "position": {"x": 10.12345, "y": 0.5, "z": -20.25}, "yawDegrees": 90}
                ],
            },
        )
        self.assertEqual(status, 200)
        self.assertEqual(saved["recipe"]["variant"], "festival")
        self.assertEqual(saved["recipe"]["add"][0]["position"]["x"], 10.1235)
        self.assertEqual(len(saved["variants"]), 2)

        # Verify persisted to payload.json
        payload = json.loads((self.packs_root / "daggerfall.quests/payload.json").read_text(encoding="utf-8"))
        self.assertEqual(len(payload["worldVariants"]["blocks"]), 2)

        # Delete the new variant
        status, deleted = self._call(
            "DELETE",
            "/api/world-variants?packId=daggerfall.quests&block=MARKAA01.RMB&variant=festival",
        )
        self.assertEqual(status, 200)
        self.assertTrue(deleted["deleted"])
        self.assertEqual(len(deleted["variants"]), 1)

    def test_variant_validation_rejects_bad_inputs(self):
        # Empty remove & add
        status, err = self._call(
            "PUT",
            "/api/world-variants",
            {"packId": "daggerfall.quests", "block": "MARKAA01.RMB", "variant": "empty", "remove": [], "add": []},
        )
        self.assertEqual(status, 400)
        self.assertIn("remove at least one", err["error"].lower())

        # Non-RMB block
        status, err = self._call(
            "PUT",
            "/api/world-variants",
            {"packId": "daggerfall.quests", "block": "S0000001.RDB", "variant": "bad", "remove": ["m/1"], "add": []},
        )
        self.assertEqual(status, 400)
        self.assertIn(".rmb", err["error"].lower())

        # Invalid variant name
        status, err = self._call(
            "PUT",
            "/api/world-variants",
            {"packId": "daggerfall.quests", "block": "MARKAA01.RMB", "variant": "bad name!", "remove": ["m/1"], "add": []},
        )
        self.assertEqual(status, 400)

        # Duplicate remove
        status, err = self._call(
            "PUT",
            "/api/world-variants",
            {
                "packId": "daggerfall.quests",
                "block": "MARKAA01.RMB",
                "variant": "dup",
                "remove": ["model/a", "model/a"],
                "add": [],
            },
        )
        self.assertEqual(status, 400)

        # Non-integer ARCH3D model ID
        status, err = self._call(
            "PUT",
            "/api/world-variants",
            {
                "packId": "daggerfall.quests",
                "block": "MARKAA01.RMB",
                "variant": "badmodel",
                "remove": [],
                "add": [{"model": "barrel", "position": {"x": 0, "y": 0, "z": 0}, "yawDegrees": 0}],
            },
        )
        self.assertEqual(status, 400)

    def test_bake_reports_missing_arena2_and_runs_injected_runner(self):
        status, err = self._call("POST", "/api/world-variants/bake", {"packId": "daggerfall.quests"})
        self.assertEqual(status, 400)
        self.assertIn("arena2", err["error"].lower())

        calls = []

        def fake_runner(repo, arena2, pack_dir):
            calls.append((repo, arena2, pack_dir))
            return 0, "world-variant-bake: MARKAA01.RMB 'closed': 25 models, base 112233445566"

        self.workspace.world_variants = WorldVariantsStore(
            self.library, FileCache(), PackStore(), bake_runner=fake_runner,
        )
        status, res = self._call(
            "POST",
            "/api/world-variants/bake",
            {"packId": "daggerfall.quests", "block": "MARKAA01.RMB"},
        )
        self.assertEqual(status, 200)
        self.assertTrue(res["ok"])
        self.assertIn("25 models", res["output"])
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][2], (self.packs_root / "daggerfall.quests").resolve())

    def test_search_models_merges_shared_and_classic_geometry(self):
        status, res = self._call("GET", "/api/world-blocks/models?q=412")
        self.assertEqual(status, 200)
        ids = [row["modelId"] for row in res["items"]]
        self.assertEqual(ids, ["41214", "41221"])

        status, res_all = self._call("GET", "/api/world-blocks/models")
        self.assertEqual(status, 200)
        self.assertEqual(res_all["total"], 3)
        self.assertIn("500", [row["modelId"] for row in res_all["items"]])

    def test_update_people_block_slot_round_trip_and_validation(self):
        status, updated = self._call(
            "PUT",
            "/api/people-pack/blocks/slot",
            {
                "kind": "rmb-interior",
                "block": "ALCHAL00.RMB",
                "buildingIndex": 0,
                "slotId": "b:0:p00",
                "position": [2.25, 0.1, -3.5],
                "spawner": "shopkeeper",
                "interaction": "ambient-bark",
                "deriveRegionalFaction": False,
                "factionId": 100,
            },
        )
        self.assertEqual(status, 200)
        self.assertEqual(updated["position"], [2.25, 0.1, -3.5])
        self.assertEqual(updated["spawner"], "shopkeeper")
        self.assertIsNone(updated["castId"])
        self.assertEqual(updated["interaction"], "ambient-bark")
        self.assertFalse(updated["deriveRegionalFaction"])
        self.assertEqual(updated["factionId"], 100)

        # Persisted on disk
        doc = json.loads((self.people_pack / "blocks/rmb-interiors.json").read_text(encoding="utf-8"))
        saved_slot = doc["blocks"]["ALCHAL00.RMB"]["0"]["slots"][0]
        self.assertEqual(saved_slot["position"], [2.25, 0.1, -3.5])
        self.assertEqual(saved_slot["spawner"], "shopkeeper")

        # Reject Type-4 Individual faction on non-cast spawner slot
        status, err = self._call(
            "PUT",
            "/api/people-pack/blocks/slot",
            {
                "kind": "rmb-interior",
                "block": "ALCHAL00.RMB",
                "buildingIndex": 0,
                "slotId": "b:0:p00",
                "spawner": "shopkeeper",
                "factionId": 510,
            },
        )
        self.assertEqual(status, 400)
        self.assertIn("type-4", err["error"].lower())

        # Allow switching slot to cast individual with Type-4 faction
        status, cast_updated = self._call(
            "PUT",
            "/api/people-pack/blocks/slot",
            {
                "kind": "rmb-interior",
                "block": "ALCHAL00.RMB",
                "buildingIndex": 0,
                "slotId": "b:0:p00",
                "castId": "lady-brisienna",
                "factionId": 510,
            },
        )
        self.assertEqual(status, 200)
        self.assertEqual(cast_updated["castId"], "lady-brisienna")
        self.assertIsNone(cast_updated["spawner"])
        self.assertEqual(cast_updated["castName"], "Lady Brisienna")


if __name__ == "__main__":
    unittest.main()
