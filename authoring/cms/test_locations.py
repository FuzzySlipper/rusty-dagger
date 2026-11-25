"""Unit tests for the Locations / Towns & Dungeons CMS backend and batch world-blocks fetch."""

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


class LocationsTests(unittest.TestCase):
    def _build_workspace(self, tmp: Path, *, with_staging: bool = True):
        npcs = tmp / "npcs"
        npcs.mkdir()
        _write_json(npcs / "manifest.json", {"aliases": {}, "entries": []})

        base_dir = tmp / "content/worldrpg/packs/daggerfall.base"
        _write_json(
            base_dir / "payload.json",
            {"regions": [{"index": 17, "name": "Daggerfall"}, {"index": 20, "name": "Sentinel"}]},
        )

        people_pack = tmp / "people-pack"
        _write_json(
            people_pack / "payload.json",
            {"castFile": "cast/individuals.json", "spawnersFile": "spawners.json", "blocksFolder": "blocks"},
        )
        _write_json(people_pack / "cast/individuals.json", {"individuals": []})
        _write_json(people_pack / "spawners.json", {"spawners": []})

        world_blocks = tmp / "world-blocks"
        world_shared = tmp / "world-shared"
        imported_payload = tmp / "daggerfall.imported.json"

        if with_staging:
            _write_json(
                world_blocks / "blocks.json",
                {
                    "meshPublication": "worldrpg/imports/shared",
                    "counts": {"rmbExterior": 2, "rmbInterior": 1, "rdb": 2},
                    "blocks": [
                        {
                            "key": "rmb/wall00-rmb/exterior",
                            "kind": "rmbExterior",
                            "sourceKey": "WALL00.RMB",
                            "buildingIndex": None,
                            "document": "rmb/wall00-rmb/exterior.json",
                            "spatial": None,
                        },
                        {
                            "key": "rmb/alchal00-rmb/exterior",
                            "kind": "rmbExterior",
                            "sourceKey": "ALCHAL00.RMB",
                            "buildingIndex": None,
                            "document": "rmb/alchal00-rmb/exterior.json",
                            "spatial": None,
                        },
                        {
                            "key": "rmb/alchal00-rmb/interior-0",
                            "kind": "rmbInterior",
                            "sourceKey": "ALCHAL00.RMB",
                            "buildingIndex": 0,
                            "document": "rmb/alchal00-rmb/interior-0.json",
                            "spatial": None,
                        },
                        {
                            "key": "rdb/n0000001-rdb",
                            "kind": "rdb",
                            "sourceKey": "N0000001.RDB",
                            "buildingIndex": None,
                            "document": "rdb/n0000001-rdb.json",
                            "spatial": None,
                        },
                        {
                            "key": "rdb/b0000000-rdb",
                            "kind": "rdb",
                            "sourceKey": "B0000000.RDB",
                            "buildingIndex": None,
                            "document": "rdb/b0000000-rdb.json",
                            "spatial": None,
                        },
                    ],
                    "unplacedRmbBlocks": [],
                },
            )
            for key, kind, src in (
                ("rmb/wall00-rmb/exterior", "rmbExterior", "WALL00.RMB"),
                ("rmb/alchal00-rmb/exterior", "rmbExterior", "ALCHAL00.RMB"),
                ("rmb/alchal00-rmb/interior-0", "rmbInterior", "ALCHAL00.RMB"),
                ("rdb/n0000001-rdb", "rdb", "N0000001.RDB"),
                ("rdb/b0000000-rdb", "rdb", "B0000000.RDB"),
            ):
                _write_json(
                    world_blocks / f"{key}.json",
                    {
                        "key": key,
                        "kind": kind,
                        "sourceKey": src,
                        "sourceOrdinal": 0,
                        "buildingIndex": 0 if kind == "rmbInterior" else None,
                        "bounds": {"minimum": {"x": 0, "y": 0, "z": -102.4}, "maximum": {"x": 102.4, "y": 10, "z": 0}},
                        "models": [],
                    },
                )
            _write_json(
                imported_payload,
                {
                    "locations": {
                        "locations": [
                            {
                                "region": 17,
                                "index": 1231,
                                "name": "Daggerfall",
                                "locationType": 0,
                                "climate": 231,
                                "dungeonType": -1,
                                "discovered": True,
                                "exterior": {
                                    "locationId": 50000,
                                    "mapPixelX": 100,
                                    "mapPixelY": 150,
                                    "width": 2,
                                    "height": 1,
                                    "blocks": [
                                        {"sourceName": "WALL00.RMB", "x": 0, "y": 0},
                                        {"sourceName": "ALCHAL00.RMB", "x": 1, "y": 0},
                                    ],
                                    "buildingTypes": [0, 15],
                                },
                            },
                            {
                                "region": 17,
                                "index": 179,
                                "name": "Privateer's Hold",
                                "locationType": 4,
                                "climate": 231,
                                "dungeonType": 2,
                                "discovered": False,
                                "exterior": {
                                    "locationId": 50049,
                                    "mapPixelX": 109,
                                    "mapPixelY": 158,
                                    "width": 1,
                                    "height": 1,
                                    "blocks": [{"sourceName": "WALL00.RMB", "x": 0, "y": 0}],
                                    "buildingTypes": [],
                                },
                            },
                        ],
                        "dungeons": [
                            {
                                "region": 17,
                                "index": 179,
                                "name": "Privateer's Hold",
                                "dungeonLocationId": 50050,
                                "blocks": ["N0000001.RDB", "B0000000.RDB"],
                                "blockPlacements": [
                                    {"sourceKey": "N0000001.RDB", "x": 0, "z": 0, "start": True},
                                    {"sourceKey": "B0000000.RDB", "x": 1, "z": 0, "start": False},
                                ],
                                "textureTable": {"archives": [22], "remaps": []},
                            }
                        ],
                    }
                },
            )

        lib = Library(
            npcs,
            tmp / "pipeline",
            repo=tmp,
            people_pack_root=people_pack,
            world_blocks_root=world_blocks,
            world_shared_root=world_shared,
            world_classic_shared_root=tmp / "world-classic-shared",
            imported_payload_path=imported_payload,
        )
        links = tmp / "links.json"
        links.write_text("{}", encoding="utf-8")
        merges = tmp / "merges.json"
        merges.write_text("{}", encoding="utf-8")
        return Workspace(lib, links, merges, ReviewStore(tmp / "reviews.sqlite3"), ConceptStore(tmp / "concepts"))

    def test_missing_imported_payload_degrades_gracefully(self):
        with tempfile.TemporaryDirectory() as td:
            ws = self._build_workspace(Path(td), with_staging=False)
            res = ws.locations.query_locations()
            self.assertFalse(res["available"])
            self.assertEqual(res["total"], 0)
            self.assertIn("not found", res["reason"].lower())
            self.assertIsNone(ws.locations.location_detail(17, 1231))

    def test_locations_query_detail_and_batch_blocks(self):
        with tempfile.TemporaryDirectory() as td:
            ws = self._build_workspace(Path(td), with_staging=True)
            server = make_server("127.0.0.1", 0, ws)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = f"http://127.0.0.1:{server.server_port}"
            try:
                # Default list sorts largest town first
                with urllib.request.urlopen(f"{base}/api/locations") as resp:
                    self.assertEqual(resp.status, HTTPStatus.OK)
                    data = json.loads(resp.read())
                self.assertTrue(data["available"])
                self.assertEqual(data["total"], 2)
                self.assertEqual(data["items"][0]["name"], "Daggerfall")
                self.assertEqual(data["items"][0]["regionName"], "Daggerfall")

                # Multi-block filter
                with urllib.request.urlopen(f"{base}/api/locations?multiBlock=1") as resp:
                    multi = json.loads(resp.read())
                self.assertEqual(multi["total"], 1)
                self.assertEqual(multi["items"][0]["name"], "Daggerfall")

                # Dungeon filter
                with urllib.request.urlopen(f"{base}/api/locations?hasDungeon=1") as resp:
                    dungs = json.loads(resp.read())
                self.assertEqual(dungs["total"], 1)
                self.assertEqual(dungs["items"][0]["name"], "Privateer's Hold")

                # Detail for Daggerfall
                with urllib.request.urlopen(f"{base}/api/locations/detail?region=17&index=1231") as resp:
                    df = json.loads(resp.read())
                self.assertEqual(df["exterior"]["width"], 2)
                self.assertEqual(len(df["exterior"]["blocks"]), 2)
                self.assertEqual(df["exterior"]["blocks"][1]["blockKey"], "rmb/alchal00-rmb/exterior")
                self.assertEqual(df["exterior"]["blocks"][1]["interiorCount"], 1)

                # Detail for Privateer's Hold includes dungeon placements
                with urllib.request.urlopen(f"{base}/api/locations/detail?region=17&index=179") as resp:
                    ph = json.loads(resp.read())
                self.assertIsNotNone(ph["dungeon"])
                self.assertEqual(ph["dungeon"]["blockCount"], 2)
                self.assertTrue(ph["dungeon"]["placements"][0]["start"])

                # Batch block fetch returns both exterior blocks in 1 POST
                req = urllib.request.Request(
                    f"{base}/api/world-blocks/batch",
                    data=json.dumps({"keys": df["exterior"]["uniqueBlockKeys"]}).encode("utf-8"),
                    headers={"Content-Type": "application/json"},
                    method="POST",
                )
                with urllib.request.urlopen(req) as resp:
                    batch = json.loads(resp.read())
                self.assertTrue(batch["available"])
                self.assertEqual(set(batch["blocks"].keys()), {"rmb/wall00-rmb/exterior", "rmb/alchal00-rmb/exterior"})
                self.assertEqual(batch["missing"], [])
            finally:
                server.shutdown()
                server.server_close()


if __name__ == "__main__":
    unittest.main()
