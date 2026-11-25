"""Unit tests for the World Blocks CMS backend."""

from http import HTTPStatus
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parent))

from concepts import ConceptStore
from library import Library
from reviews import ReviewStore
from server import Workspace, make_server


def _write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


class WorldBlocksTests(unittest.TestCase):
    def _build_workspace(self, tmp: Path, *, with_staging: bool = True):
        npcs = tmp / "npcs"
        npcs.mkdir()
        _write_json(
            npcs / "manifest.json",
            {
                "aliases": {"184-2": "npc-001"},
                "entries": [
                    {
                        "id": "npc-001",
                        "name": "Test Alchemist",
                        "kind": "shopkeeper",
                        "source": {"archive": 184, "record": 2},
                        "image": "npc-001.png",
                    }
                ],
            },
        )
        (npcs / "npc-001.png").write_bytes(b"\x89PNG\r\n\x1a\n")

        people_pack = tmp / "people-pack"
        _write_json(
            people_pack / "payload.json",
            {"castFile": "cast/individuals.json", "spawnersFile": "spawners.json", "blocksFolder": "blocks"},
        )
        _write_json(people_pack / "cast/individuals.json", {"individuals": []})
        _write_json(
            people_pack / "spawners.json",
            {
                "spawners": [
                    {
                        "id": "alchemist",
                        "name": "Alchemist",
                        "harm": "law-protected-civilian",
                        "interaction": "full-dialogue",
                        "visualMode": "classic-slot",
                        "defaultPool": [],
                        "regionalPools": {},
                    }
                ]
            },
        )
        _write_json(
            people_pack / "blocks/rmb-interiors.json",
            {
                "kind": "rmb-interior",
                "blocks": {
                    "ALCHAL00.RMB": {
                        "0": {
                            "buildingType": 0,
                            "buildingFactionId": 0,
                            "slots": [
                                {
                                    "slotId": "person/0",
                                    "spawner": "alchemist",
                                    "position": [-0.2, 0, 4.4],
                                    "interaction": "full-dialogue",
                                    "factionId": 0,
                                    "visual": {"archive": 184, "record": 2, "gender": "Male"},
                                }
                            ],
                        }
                    }
                },
            },
        )

        world_blocks = tmp / "world-blocks"
        world_shared = tmp / "world-shared"
        if with_staging:
            _write_json(
                world_blocks / "blocks.json",
                {
                    "meshPublication": "worldrpg/imports/shared",
                    "counts": {"rmbExterior": 1, "rmbInterior": 1, "rdb": 1},
                    "blocks": [
                        {
                            "key": "rmb/alchal00-rmb/exterior",
                            "kind": "rmbExterior",
                            "sourceKey": "ALCHAL00.RMB",
                            "buildingIndex": None,
                            "document": "rmb/alchal00-rmb/exterior.json",
                            "spatial": "rmb/alchal00-rmb/exterior.spatial",
                        },
                        {
                            "key": "rmb/alchal00-rmb/interior-0",
                            "kind": "rmbInterior",
                            "sourceKey": "ALCHAL00.RMB",
                            "buildingIndex": 0,
                            "document": "rmb/alchal00-rmb/interior-0.json",
                            "spatial": "rmb/alchal00-rmb/interior-0.spatial",
                        },
                        {
                            "key": "rdb/b0000000-rdb",
                            "kind": "rdb",
                            "sourceKey": "B0000000.RDB",
                            "buildingIndex": None,
                            "document": "rdb/b0000000-rdb.json",
                            "spatial": "rdb/b0000000-rdb.spatial",
                        },
                    ],
                    "unplacedRmbBlocks": [],
                },
            )
            _write_json(
                world_blocks / "rmb/alchal00-rmb/exterior.json",
                {
                    "key": "rmb/alchal00-rmb/exterior",
                    "kind": "rmbExterior",
                    "sourceKey": "ALCHAL00.RMB",
                    "sourceOrdinal": 0,
                    "buildingIndex": None,
                    "bounds": {"minimum": {"x": 0, "y": 0, "z": -102.4}, "maximum": {"x": 102.4, "y": 10, "z": 0}},
                    "models": [
                        {
                            "id": "model/alchal00-rmb/0/0",
                            "modelId": "140",
                            "meshArtifactId": "geometry/mesh-140",
                            "position": {"x": 10, "y": 0, "z": -20},
                            "rotation": {"x": 0, "y": 0.7071068, "z": 0, "w": 0.7071068},
                            "buildingIndex": 0,
                        }
                    ],
                    "buildings": [
                        {"index": 0, "buildingType": 0, "factionId": 0, "quality": 5, "nameSeed": 0, "hasInterior": True}
                    ],
                    "ground": [{"x": 0, "y": 0, "textureRecord": 2, "rotated": False, "flipped": False}],
                    "doors": [],
                    "transitionDoors": [],
                    "gates": [],
                    "staticNpcs": [],
                    "billboards": [],
                    "lights": [],
                },
            )
            _write_json(
                world_blocks / "rmb/alchal00-rmb/interior-0.json",
                {
                    "key": "rmb/alchal00-rmb/interior-0",
                    "kind": "rmbInterior",
                    "sourceKey": "ALCHAL00.RMB",
                    "sourceOrdinal": 0,
                    "buildingIndex": 0,
                    "bounds": {"minimum": {"x": -4, "y": 0, "z": -6}, "maximum": {"x": 4, "y": 6, "z": 6}},
                    "models": [],
                    "interiorBuilding": {"index": 0, "buildingType": 0, "factionId": 0, "quality": 5, "nameSeed": 0, "hasInterior": True},
                    "staticNpcs": [
                        {
                            "id": "person/0",
                            "position": {"x": -0.2, "y": 0, "z": 4.4},
                            "billboardArchive": 184,
                            "billboardRecord": 2,
                            "factionId": 0,
                            "race": None,
                            "gender": "Male",
                            "sourceOffset": 100,
                        }
                    ],
                },
            )
            _write_json(
                world_blocks / "rdb/b0000000-rdb.json",
                {
                    "key": "rdb/b0000000-rdb",
                    "kind": "rdb",
                    "sourceKey": "B0000000.RDB",
                    "sourceOrdinal": 0,
                    "buildingIndex": None,
                    "bounds": {"minimum": {"x": 0, "y": 0, "z": -51.2}, "maximum": {"x": 51.2, "y": 20, "z": 0}},
                    "models": [],
                },
            )
            (world_shared / "geometry").mkdir(parents=True, exist_ok=True)
            (world_shared / "geometry/mesh-140.rstatmsh").write_bytes(b"RSTATMSH\x00\x00\x00\x00")
            (world_shared / "media/dungeon/terrain").mkdir(parents=True, exist_ok=True)
            (world_shared / "media/dungeon/terrain/texture-3-2.png").write_bytes(b"\x89PNG\r\n\x1a\n")
            world_classic = tmp / "world-classic-shared"
            (world_classic / "geometry").mkdir(parents=True, exist_ok=True)
            (world_classic / "geometry/mesh-41105.rstatmsh").write_bytes(b"RSTATMSH\x01\x00\x00\x00")
        else:
            world_classic = tmp / "world-classic-shared"

        lib = Library(
            npcs,
            tmp / "pipeline",
            repo=tmp,
            people_pack_root=people_pack,
            world_blocks_root=world_blocks,
            world_shared_root=world_shared,
            world_classic_shared_root=world_classic,
        )
        links = tmp / "links.json"
        links.write_text("{}", encoding="utf-8")
        merges = tmp / "merges.json"
        merges.write_text("{}", encoding="utf-8")
        return Workspace(lib, links, merges, ReviewStore(tmp / "reviews.sqlite3"), ConceptStore(tmp / "concepts"))

    def test_missing_staging_degrades_gracefully(self):
        with tempfile.TemporaryDirectory() as td:
            ws = self._build_workspace(Path(td), with_staging=False)
            res = ws.world_blocks.query_blocks()
            self.assertFalse(res["available"])
            self.assertEqual(res["total"], 0)
            self.assertIn("not found", res["reason"].lower())
            self.assertIsNone(ws.world_blocks.resolve_source("ALCHAL00.RMB"))

    def test_query_detail_people_join_and_http_mounts(self):
        with tempfile.TemporaryDirectory() as td:
            ws = self._build_workspace(Path(td), with_staging=True)
            server = make_server("127.0.0.1", 0, ws)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = f"http://127.0.0.1:{server.server_port}"
            try:
                # Query all blocks
                with urllib.request.urlopen(f"{base}/api/world-blocks") as resp:
                    self.assertEqual(resp.status, HTTPStatus.OK)
                    data = json.loads(resp.read())
                self.assertTrue(data["available"])
                self.assertEqual(data["total"], 3)
                self.assertEqual(data["counts"]["rmbExterior"], 1)

                # Filter by kind & hasPeople
                with urllib.request.urlopen(f"{base}/api/world-blocks?kind=rmbInterior&hasPeople=1") as resp:
                    filtered = json.loads(resp.read())
                self.assertEqual(filtered["total"], 1)
                self.assertEqual(filtered["items"][0]["key"], "rmb/alchal00-rmb/interior-0")
                self.assertEqual(filtered["items"][0]["peopleSlotCount"], 1)

                # Detail on exterior links building 0 -> interior-0
                with urllib.request.urlopen(f"{base}/api/world-blocks/detail?key=rmb/alchal00-rmb/exterior") as resp:
                    ext = json.loads(resp.read())
                self.assertEqual(ext["block"]["buildings"][0]["buildingTypeName"], "Alchemist")
                self.assertEqual(ext["block"]["buildings"][0]["interiorKey"], "rmb/alchal00-rmb/interior-0")
                self.assertEqual(ext["terrainArchive"], 3)

                # Detail on interior joins peopleSlots and resolves staticNpc visual -> npc-001
                with urllib.request.urlopen(f"{base}/api/world-blocks/detail?key=rmb/alchal00-rmb/interior-0") as resp:
                    interior = json.loads(resp.read())
                self.assertEqual(interior["parentExteriorKey"], "rmb/alchal00-rmb/exterior")
                self.assertEqual(len(interior["peopleSlots"]), 1)
                self.assertEqual(interior["peopleSlots"][0]["spawner"], "alchemist")
                self.assertEqual(interior["peopleSlots"][0]["art"]["npcId"], "npc-001")
                self.assertEqual(interior["block"]["staticNpcs"][0]["npcId"], "npc-001")

                # Resolve source -> canonical key
                with urllib.request.urlopen(f"{base}/api/world-blocks/resolve?source=ALCHAL00.RMB&buildingIndex=0") as resp:
                    resolved = json.loads(resp.read())
                self.assertEqual(resolved["key"], "rmb/alchal00-rmb/interior-0")

                # Static mounts serve .rstatmsh (including classic-art fallback) and terrain PNG
                with urllib.request.urlopen(f"{base}/media/world-geometry/mesh-140.rstatmsh") as resp:
                    self.assertTrue(resp.read().startswith(b"RSTATMSH"))
                with urllib.request.urlopen(f"{base}/media/world-geometry/mesh-41105.rstatmsh") as resp:
                    self.assertTrue(resp.read().startswith(b"RSTATMSH"))
                with urllib.request.urlopen(f"{base}/media/world-terrain/texture-3-2.png") as resp:
                    self.assertTrue(resp.read().startswith(b"\x89PNG"))
            finally:
                server.shutdown()
                server.server_close()


if __name__ == "__main__":
    unittest.main()
