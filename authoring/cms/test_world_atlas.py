"""Unit tests for the CMS 2D World & Political Atlas backend."""

import json
from pathlib import Path
import sys
from tempfile import TemporaryDirectory
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))

from library import Library
from server import Workspace, create_router
from world_atlas import MAP_HEIGHT, MAP_WIDTH


def _write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


class WorldAtlasTests(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory(prefix="cms-atlas-test-")
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

        # Minimal daggerfall.base with regions, factions, diplomacy, and worldNames
        _write_json(
            self.root / "content/worldrpg/packs/daggerfall.base/payload.json",
            {
                "buildingNames": {"regionNames": ["Alik'r", "Dragontail", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "Daggerfall", "", "", "", "", "", "Wayrest", "", "", "Orsinium"]},
                "worldNames": {"names": [{"id": "high-rock", "name": "High Rock", "kind": "realm", "x": 300, "y": 62}]},
                "factions": {
                    "factions": [
                        {"id": 201, "name": "Daggerfall", "type": 7, "typeName": "Province", "region": 17, "parent": 0, "power": 80, "ruler": 1, "children": [364], "allies": [], "enemies": []},
                        {"id": 595, "name": "Court of Daggerfall", "type": 14, "typeName": "Courts", "region": 17, "parent": 201, "power": 10, "ruler": 0, "allies": [], "enemies": []},
                        {"id": 518, "name": "People of Daggerfall", "type": 15, "typeName": "Peoples", "region": 17, "parent": 201, "power": 10, "ruler": 0, "allies": [], "enemies": []},
                        {"id": 364, "name": "King Gothryd", "type": 4, "typeName": "Individual", "region": 17, "parent": 201, "power": 15, "ruler": 0, "allies": [207], "enemies": []},
                        {"id": 207, "name": "Wayrest", "type": 7, "typeName": "Province", "region": 23, "parent": 0, "power": 80, "ruler": 1, "children": [390], "allies": [], "enemies": []},
                        {"id": 390, "name": "King Eadwyre", "type": 4, "typeName": "Individual", "region": 23, "parent": 207, "power": 15, "ruler": 0, "allies": [], "enemies": [357]},
                        {"id": 357, "name": "Gortwog", "type": 4, "typeName": "Individual", "region": 26, "parent": 0, "power": 30, "ruler": 0, "allies": [], "enemies": []},
                    ]
                },
            },
        )

        # Minimal daggerfall.people pack linking cast + regional pool
        self.people_pack = self.root / "content/worldrpg/packs/daggerfall.people"
        _write_json(
            self.people_pack / "cast/individuals.json",
            {
                "individuals": [
                    {
                        "id": "king-gothryd",
                        "factionId": 364,
                        "name": "King Gothryd",
                        "gender": "Male",
                        "visual": {"archive": 182, "record": 1},
                        "homeBlock": "PALAAA00.RMB",
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
                        "id": "town-guard",
                        "role": "guard",
                        "interaction": "ambient-bark",
                        "harm": "combatant",
                        "deriveRegionalFaction": True,
                        "visualMode": "pool",
                        "defaultPool": [{"archive": 184, "record": 1, "gender": "Male", "weight": 1}],
                        "regionalPools": {"17": [{"archive": 184, "record": 2, "gender": "Male", "weight": 1}]},
                    }
                ]
            },
        )
        (self.people_pack / "blocks").mkdir(parents=True, exist_ok=True)

        # Minimal imported payload with politic/climate RLE rows & 2 locations
        self.imported = self.root / "imported.json"
        _write_json(
            self.imported,
            {
                "politic": {
                    "values": [
                        {"value": 64, "region": -1, "disposition": "ocean"},
                        {"value": 145, "region": 17, "disposition": "region"},
                        {"value": 151, "region": 23, "disposition": "region"},
                    ],
                    "rows": [
                        {"y": 10, "runs": [{"count": 100, "value": 64}, {"count": 40, "value": 145}, {"count": 60, "value": 151}, {"count": 801, "value": 64}]},
                        {"y": 11, "runs": [{"count": 100, "value": 64}, {"count": 40, "value": 145}, {"count": 60, "value": 151}, {"count": 801, "value": 64}]},
                    ],
                },
                "climate": {
                    "values": [
                        {"value": 223, "name": "Ocean", "climateBase": "swamp", "people": "Breton"},
                        {"value": 231, "name": "Woodlands", "climateBase": "temperate", "people": "Breton"},
                    ],
                    "rows": [
                        {"y": 10, "runs": [{"count": 100, "value": 223}, {"count": 100, "value": 231}, {"count": 801, "value": 223}]},
                        {"y": 11, "runs": [{"count": 100, "value": 223}, {"count": 100, "value": 231}, {"count": 801, "value": 223}]},
                    ],
                },
                "locations": {
                    "locations": [
                        {"region": 17, "index": 0, "name": "Daggerfall", "locationType": 0, "climate": 231, "exterior": {"mapPixelX": 120, "mapPixelY": 10, "width": 2, "height": 2, "blocks": []}},
                        {"region": 23, "index": 1, "name": "Wayrest", "locationType": 0, "climate": 231, "exterior": {"mapPixelX": 170, "mapPixelY": 11, "width": 2, "height": 2, "blocks": []}},
                    ],
                    "dungeons": [],
                },
            },
        )

        # Minimal atlas manifest & classic map stub
        self.atlas_dir = self.root / "atlas"
        _write_json(
            self.atlas_dir / "atlas.json",
            {"tileSize": 256, "worldWidth": 1000, "worldHeight": 500, "levels": [{"level": 0, "width": 1000, "height": 500, "columns": 4, "rows": 2}]},
        )
        self.maps_dir = self.root / "maps"
        self.maps_dir.mkdir()
        (self.maps_dir / "map-trav0i00.png").write_bytes(b"\x89PNG\r\n")
        (self.maps_dir / "map-fmap0i17.png").write_bytes(b"\x89PNG\r\n")

        self.library = Library(
            self.npcs,
            self.pipeline,
            repo=self.root,
            people_pack_root=self.people_pack,
            imported_payload_path=self.imported,
            atlas_dir=self.atlas_dir,
            maps_dir=self.maps_dir,
        )
        self.workspace = Workspace(self.library, self.links, self.merges)
        self.router = create_router(self.workspace)

    def tearDown(self):
        self.tmp.cleanup()

    def _get(self, path: str):
        resp = self.router.dispatch("GET", path, b"")
        self.assertIsNotNone(resp)
        payload, _ = resp.encode()
        return resp.status, json.loads(payload.decode("utf-8"))

    def test_overview_joins_regions_factions_diplomacy_and_people_pack(self):
        status, data = self._get("/api/world-atlas/overview")
        self.assertEqual(status, 200)
        self.assertTrue(data["available"])
        self.assertTrue(data["atlas"]["available"])
        self.assertTrue(data["classicMaps"]["available"])
        self.assertEqual(len(data["classicMaps"]["sheets"]), 1)

        by_id = {r["id"]: r for r in data["regions"]}
        df = by_id[17]
        self.assertEqual(df["name"], "Daggerfall")
        self.assertEqual(df["pixelCount"], 80)
        self.assertEqual(df["bounds"], {"minX": 100, "minY": 10, "maxX": 139, "maxY": 11})
        self.assertEqual(df["province"]["rulerTitle"], "King")
        self.assertEqual(df["province"]["regentName"], "King Gothryd")
        self.assertEqual(df["court"]["name"], "Court of Daggerfall")
        self.assertEqual(df["people"]["name"], "People of Daggerfall")
        self.assertEqual(df["dominantClimate"], "Woodlands")
        self.assertEqual(df["locations"]["cities"], 1)
        self.assertEqual(df["peoplePack"]["regionalSpawners"], ["town-guard"])
        self.assertEqual(df["peoplePack"]["cast"][0]["id"], "king-gothryd")
        # King Gothryd (17) is allied with Wayrest (23) -> bidirectional allyRegions
        self.assertIn(23, df["diplomacy"]["allyRegions"])
        self.assertIn(17, by_id[23]["diplomacy"]["allyRegions"])
        # King Eadwyre (23) is enemies with Gortwog (26)
        self.assertIn(26, by_id[23]["diplomacy"]["enemyRegions"])
        self.assertIn(23, by_id[26]["diplomacy"]["enemyRegions"])

    def test_masks_and_pins_endpoints_and_region_filter(self):
        status, masks = self._get("/api/world-atlas/masks")
        self.assertEqual(status, 200)
        self.assertEqual(masks["width"], MAP_WIDTH)
        self.assertEqual(masks["height"], MAP_HEIGHT)
        total_pixels = sum(masks["politicRuns"][1::2])
        self.assertEqual(total_pixels, MAP_WIDTH * MAP_HEIGHT)

        status, pins = self._get("/api/world-atlas/pins")
        self.assertEqual(status, 200)
        self.assertEqual(pins["total"], 2)

        status, reg_pins = self._get("/api/world-atlas/pins?region=17")
        self.assertEqual(status, 200)
        self.assertEqual(reg_pins["total"], 1)
        self.assertEqual(reg_pins["pins"][0][6], "Daggerfall")

    def test_missing_imported_payload_degrades_gracefully(self):
        missing_lib = Library(
            self.npcs,
            self.pipeline,
            repo=self.root,
            people_pack_root=self.people_pack,
            imported_payload_path=self.root / "nonexistent.json",
            atlas_dir=self.root / "no-atlas",
            maps_dir=self.root / "no-maps",
        )
        ws = Workspace(missing_lib, self.links, self.merges)
        router = create_router(ws)
        resp = router.dispatch("GET", "/api/world-atlas/overview", b"")
        payload, _ = resp.encode()
        data = json.loads(payload.decode("utf-8"))
        self.assertEqual(resp.status, 200)
        self.assertFalse(data["available"])
        self.assertFalse(data["atlas"]["available"])


if __name__ == "__main__":
    unittest.main()
