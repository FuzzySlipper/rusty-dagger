"""Unit tests for Tier 2 world appearance: climate swaps, dungeon presets, and art replacements."""

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
from world_appearance import ground_texture_archive, nature_texture_archive


def _write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


class WorldAppearanceTests(unittest.TestCase):
    def test_ground_and_nature_archives_match_daggerfall_climate_rules(self):
        # Woodlands (231): temperate ground 302 summer, 303 winter, 304 rain; nature 504 / 505
        self.assertEqual(ground_texture_archive(231, "summer"), 302)
        self.assertEqual(ground_texture_archive(231, "winter"), 303)
        self.assertEqual(ground_texture_archive(231, "rain"), 304)
        self.assertEqual(nature_texture_archive(231, "summer"), 504)
        self.assertEqual(nature_texture_archive(231, "winter"), 505)

        # Desert / Subtropical (224, 229): no winter snow on ground or nature
        self.assertEqual(ground_texture_archive(229, "summer"), 2)
        self.assertEqual(ground_texture_archive(229, "winter"), 2)
        self.assertEqual(ground_texture_archive(229, "rain"), 4)
        self.assertEqual(nature_texture_archive(229, "summer"), 501)
        self.assertEqual(nature_texture_archive(229, "winter"), 501)

        # Mountain (226): 102 / 103 / 104, nature 510 / 511
        self.assertEqual(ground_texture_archive(226, "summer"), 102)
        self.assertEqual(ground_texture_archive(226, "winter"), 103)
        self.assertEqual(nature_texture_archive(226, "winter"), 511)

        # Swamp (228): 402 / 403 / 404
        self.assertEqual(ground_texture_archive(228, "summer"), 402)
        self.assertEqual(ground_texture_archive(228, "winter"), 403)

    def test_appearance_endpoint_and_art_pack_mount(self):
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td)
            npcs = tmp / "npcs"
            npcs.mkdir()
            _write_json(npcs / "manifest.json", {"aliases": {}, "entries": []})

            imp_path = tmp / "daggerfall.imported.json"
            _write_json(
                imp_path,
                {
                    "climate": {
                        "values": [
                            {"value": 231, "name": "Woodlands", "climateBase": "temperate"},
                            {"value": 229, "name": "Subtropical", "climateBase": "desert"},
                        ],
                        "swaps": [
                            {"archive": 117, "climate": "temperate", "season": "summer", "firstRecord": 0, "lastRecord": None, "targetArchive": 317},
                            {"archive": 117, "climate": "desert", "season": "summer", "firstRecord": 0, "lastRecord": None, "targetArchive": 17},
                        ],
                    },
                    "locations": {
                        "locations": [{"region": 17, "index": 179, "dungeonType": 2}],
                        "dungeons": [
                            {
                                "region": 17,
                                "index": 179,
                                "textureTable": {"remaps": [{"archive": 119, "targetArchive": 22}]},
                            }
                        ],
                    },
                },
            )

            art_dir = tmp / "art-pack"
            _write_json(
                art_dir / "payload.json",
                {
                    "artReplacements": {
                        "surfaces": [
                            {
                                "replaces": {"archive": 302, "record": 2},
                                "path": "worldrpg/packs/daggerfall.art-replacements/media/surfaces/texture-302-2.png",
                            }
                        ],
                        "scenery": [
                            {
                                "replaces": {"archive": 210, "record": 13},
                                "path": "worldrpg/packs/daggerfall.art-replacements/media/scenery/texture-210-13.png",
                            }
                        ],
                        "models": {
                            "41000": {
                                "path": "worldrpg/packs/daggerfall.art-replacements/media/models/41000.rstatmsh",
                                "slots": [
                                    {
                                        "slot": 0,
                                        "texture": {"path": "worldrpg/packs/daggerfall.art-replacements/media/models/41000-0.png"},
                                    }
                                ],
                            }
                        },
                    }
                },
            )
            (art_dir / "media/surfaces").mkdir(parents=True, exist_ok=True)
            (art_dir / "media/surfaces/texture-302-2.png").write_bytes(b"\x89PNG\r\n\x1a\n")
            (art_dir / "media/models").mkdir(parents=True, exist_ok=True)
            (art_dir / "media/models/41000.rstatmsh").write_bytes(b"RSTATMSH\x00\x00\x00\x00")

            lib = Library(
                npcs,
                tmp / "pipeline",
                repo=tmp,
                imported_payload_path=imp_path,
                art_pack_dir=art_dir,
            )
            links = tmp / "links.json"
            links.write_text("{}", encoding="utf-8")
            merges = tmp / "merges.json"
            merges.write_text("{}", encoding="utf-8")
            ws = Workspace(lib, links, merges, ReviewStore(tmp / "reviews.sqlite3"), ConceptStore(tmp / "concepts"))

            server = make_server("127.0.0.1", 0, ws)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = f"http://127.0.0.1:{server.server_port}"
            try:
                with urllib.request.urlopen(f"{base}/api/world-blocks/appearance") as resp:
                    self.assertEqual(resp.status, HTTPStatus.OK)
                    app = json.loads(resp.read())

                self.assertEqual(len(app["climates"]), 2)
                self.assertEqual(app["swaps"]["desert"]["summer"]["117"], [[0, 255, 17]])
                self.assertEqual(app["dungeonPresets"][0]["name"], "Human Stronghold")
                self.assertEqual(app["dungeonPresets"][0]["remaps"]["119"], 22)
                ar = app["artReplacements"]
                self.assertTrue(ar["available"])
                self.assertEqual(ar["surfaces"]["302-2"], "/media/art-pack/media/surfaces/texture-302-2.png")
                self.assertEqual(ar["scenery"]["210-13"], "/media/art-pack/media/scenery/texture-210-13.png")
                self.assertEqual(ar["models"]["41000"]["meshUrl"], "/media/art-pack/media/models/41000.rstatmsh")
                self.assertEqual(
                    ar["models"]["41000"]["slots"]["0"]["textureUrl"],
                    "/media/art-pack/media/models/41000-0.png",
                )

                # Verify /media/art-pack/ static mount serves PNG & rstatmsh
                with urllib.request.urlopen(f"{base}/media/art-pack/media/surfaces/texture-302-2.png") as resp:
                    self.assertTrue(resp.read().startswith(b"\x89PNG"))
                with urllib.request.urlopen(f"{base}/media/art-pack/media/models/41000.rstatmsh") as resp:
                    self.assertTrue(resp.read().startswith(b"RSTATMSH"))
            finally:
                server.shutdown()
                server.server_close()


if __name__ == "__main__":
    unittest.main()
