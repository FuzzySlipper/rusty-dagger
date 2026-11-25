"""Unit and HTTP integration tests for daggerfall.people CMS integration."""

import base64
import json
from pathlib import Path
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
from threading import Thread
from unittest import TestCase
from urllib.error import HTTPError
from urllib.request import Request as UrlRequest, urlopen

from concepts import ConceptStore
from library import REPO, Library
from packs import ValidationError
from people_pack import PeoplePackStore
from reviews import ReviewStore
from server import Workspace, make_server

PNG_1X1 = base64.b64encode(
    base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
).decode("ascii")


class PeoplePackTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.library = Library(self.root / "local/art/reference/npc", self.root / "pipeline", repo=self.root)
        self._write(self.library.npcs / "manifest.json", {
            "entries": [
                {"id": "npc-010", "name": "King Gothryd", "kind": "Named NPC", "image": "gothryd.png"},
                {"id": "npc-020", "name": "Banker Sprite", "kind": "Townsperson", "image": "banker.png"},
                {"id": "npc-021", "name": "Sentinel Banker", "kind": "Townsperson", "image": "sentinel.png"},
            ],
            "aliases": {
                "334-19": "npc-010",
                "183-5": "npc-020",
                "357-1": "npc-021",
            },
        })
        for img in ("gothryd.png", "banker.png", "sentinel.png"):
            p = self.library.npcs / "art" / img
            p.parent.mkdir(parents=True, exist_ok=True)
            p.touch()

        self._write(self.library.base_payload, {
            "factions": {
                "factions": [
                    {"id": 364, "name": "King Gothryd", "type": 4, "typeName": "Individual", "region": 17, "parent": 359},
                    {"id": 510, "name": "Merchants", "type": 5, "typeName": "Subgroup", "region": -1, "parent": 0},
                ],
            },
            "buildingNames": {
                "regionNames": [f"Region {i}" for i in range(25)],
            },
        })
        # Give region 17 and 20 real names.
        base = json.loads(self.library.base_payload.read_text(encoding="utf-8"))
        base["buildingNames"]["regionNames"][17] = "Daggerfall"
        base["buildingNames"]["regionNames"][20] = "Sentinel"
        self._write(self.library.base_payload, base)

        pack = self.library.people_pack
        self._write(pack / "payload.json", {
            "castFile": "cast/individuals.json",
            "spawnersFile": "spawners.json",
            "blocksFolder": "blocks",
        })
        self._write(pack / "cast/individuals.json", {
            "individuals": [
                {
                    "id": "king-gothryd",
                    "factionId": 364,
                    "name": "King Gothryd",
                    "race": "breton",
                    "gender": "Male",
                    "visual": {"archive": 334, "record": 19},
                    "homeBlock": "S0000160.RDB",
                    "homeSlotId": "d:p04",
                    "interaction": "full-dialogue",
                    "harm": "protected",
                },
            ],
        })
        self._write(pack / "spawners.json", {
            "spawners": [
                {
                    "id": "banker",
                    "role": "bank teller",
                    "interaction": "full-dialogue",
                    "harm": "law-protected-civilian",
                    "deriveRegionalFaction": True,
                    "visualMode": "pool",
                    "defaultPool": [{"archive": 183, "record": 5, "gender": "Male", "weight": 31}],
                    "regionalPools": {},
                },
                {
                    "id": "royal-guard",
                    "role": "guard",
                    "interaction": "full-dialogue",
                    "harm": "combatant",
                    "deriveRegionalFaction": False,
                    "visualMode": "pool",
                    "defaultPool": [{"archive": 357, "record": 1, "gender": "Male", "weight": 10}],
                    "regionalPools": {},
                },
            ],
        })
        self._write(pack / "blocks/rmb-interiors.json", {
            "kind": "rmb-interior",
            "blocks": {
                "BANK00.RMB": {
                    "0": {
                        "buildingType": 3,
                        "buildingFactionId": 510,
                        "slots": [
                            {
                                "slotId": "b:0:p00",
                                "spawner": "banker",
                                "position": [1.0, 0.0, 2.0],
                                "interaction": "full-dialogue",
                                "factionId": 510,
                                "deriveRegionalFaction": True,
                                "visual": {"archive": 183, "record": 5, "gender": "Male"},
                                "buildingType": 3,
                                "buildingFactionId": 510,
                            },
                        ],
                    },
                },
            },
        })
        self._write(pack / "blocks/rmb-exteriors.json", {
            "kind": "rmb-exterior",
            "blocks": {
                "GATE00.RMB": [
                    {
                        "slotId": "e:p00",
                        "spawner": "royal-guard",
                        "position": [5.0, 0.0, 5.0],
                        "interaction": "full-dialogue",
                        "deriveRegionalFaction": False,
                        "visual": {"archive": 357, "record": 1, "gender": "Male"},
                    },
                ],
            },
        })
        self._write(pack / "blocks/rdb-dungeons.json", {
            "kind": "rdb-dungeon",
            "blocks": {
                "S0000160.RDB": [
                    {
                        "slotId": "d:p04",
                        "castId": "king-gothryd",
                        "position": [11.725, 33.325, -23.075],
                        "interaction": "full-dialogue",
                        "factionId": 364,
                        "visual": {"archive": 334, "record": 19, "gender": "Male"},
                    },
                ],
            },
        })

        self.workspace = Workspace(
            self.library,
            self.root / "links.json",
            self.root / "merges.json",
            ReviewStore(self.root / "reviews.sqlite3"),
            ConceptStore(self.root / "concepts"),
        )

    def _write(self, path: Path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def test_bidirectional_art_data_cross_linking_and_concept_art_status(self):
        # Before approving concept art, king-gothryd is flagged as needing concept art.
        cast_list = self.workspace.people_pack.list_cast(self.workspace.catalog(), self.workspace.concept_store)
        self.assertEqual(len(cast_list), 1)
        gothryd = cast_list[0]
        self.assertEqual(gothryd["art"]["npcId"], "npc-010")
        self.assertEqual(gothryd["factionName"], "King Gothryd")
        self.assertTrue(gothryd["needsConceptArt"])
        self.assertEqual(gothryd["usage"]["slotCount"], 1)

        # Approve a front concept on npc-010 -> needsConceptArt flips to False.
        cand = self.workspace.concept_action("npc-010", "candidates", {
            "batch": "b1", "name": "front", "filename": "front.png", "image": PNG_1X1,
            "details": {"prompt": "Royal portrait", "seed": 7},
        })
        self.workspace.concept_action("npc-010", "approve", {"candidate": cand["id"], "view": "front"})
        gothryd_after = self.workspace.people_pack.get_cast("king-gothryd", self.workspace.catalog(), self.workspace.concept_store)
        self.assertFalse(gothryd_after["needsConceptArt"])
        self.assertIsNotNone(gothryd_after["art"]["conceptUrl"])

        # Reverse link on People character detail page shows cast + spawners + slot counts.
        npc10 = self.workspace.character("npc-010")
        self.assertEqual([c["id"] for c in npc10["peoplePack"]["cast"]], ["king-gothryd"])
        self.assertEqual(npc10["peoplePack"]["slotCount"], 1)

        npc20 = self.workspace.character("npc-020")
        self.assertEqual(npc20["peoplePack"]["cast"], [])
        self.assertEqual([s["spawnerId"] for s in npc20["peoplePack"]["spawners"]], ["banker"])
        self.assertEqual(npc20["peoplePack"]["slotCount"], 1)

    def test_cast_edit_updates_only_individuals_json_and_validates(self):
        blocks_before = (self.library.people_pack / "blocks/rdb-dungeons.json").read_text(encoding="utf-8")
        updated = self.workspace.people_pack.update_cast(
            "king-gothryd",
            {"name": "King Gothryd of Daggerfall", "race": None, "harm": "law-protected-civilian"},
            self.workspace.catalog(),
            self.workspace.concept_store,
        )
        self.assertEqual(updated["name"], "King Gothryd of Daggerfall")
        self.assertIsNone(updated["race"])
        self.assertEqual(updated["harm"], "law-protected-civilian")
        # Block file untouched.
        self.assertEqual((self.library.people_pack / "blocks/rdb-dungeons.json").read_text(encoding="utf-8"), blocks_before)

        # Invalid enum rejected before write.
        with self.assertRaises(ValidationError) as ctx:
            self.workspace.people_pack.update_cast(
                "king-gothryd", {"harm": "invincible"}, self.workspace.catalog(), self.workspace.concept_store
            )
        self.assertEqual(ctx.exception.issues[0]["field"], "individuals[0].harm")

    def test_spawner_regional_pools_and_validation(self):
        updated = self.workspace.people_pack.update_spawner(
            "banker",
            {
                "defaultPool": [
                    {"archive": 183, "record": 5, "gender": "Male", "weight": 20},
                    {"archive": 357, "record": 1, "gender": "Female", "weight": 10},
                ],
                "regionalPools": {
                    "20": [{"archive": 357, "record": 1, "gender": "Male", "weight": 5}],
                },
            },
            self.workspace.catalog(),
            self.workspace.concept_store,
        )
        self.assertEqual(len(updated["defaultPool"]), 2)
        self.assertEqual(len(updated["regionalPools"]), 1)
        self.assertEqual(updated["regionalPools"][0]["regionKey"], "20")
        self.assertEqual(updated["regionalPools"][0]["regionName"], "Sentinel")
        self.assertEqual(updated["regionalPools"][0]["entries"][0]["art"]["npcId"], "npc-021")

        # Empty defaultPool when visualMode == "pool" is rejected (matches C# DaggerfallPeopleContentTests).
        with self.assertRaises(ValidationError):
            self.workspace.people_pack.update_spawner(
                "banker", {"defaultPool": []}, self.workspace.catalog(), self.workspace.concept_store
            )
        # Non-positive weight is rejected.
        with self.assertRaises(ValidationError):
            self.workspace.people_pack.update_spawner(
                "banker",
                {"defaultPool": [{"archive": 183, "record": 5, "gender": "Male", "weight": 0}]},
                self.workspace.catalog(),
                self.workspace.concept_store,
            )

    def test_block_slot_filtering_by_spawner_cast_building_and_inherited_harm(self):
        all_slots = self.workspace.people_pack.query_blocks(self.workspace.catalog(), self.workspace.concept_store)
        self.assertEqual(all_slots["total"], 3)

        # Inherited harm filter: combatant matches royal-guard slot even though slot JSON has no harm field.
        combatants = self.workspace.people_pack.query_blocks(
            self.workspace.catalog(), self.workspace.concept_store, harm="combatant"
        )
        self.assertEqual(combatants["total"], 1)
        self.assertEqual(combatants["items"][0]["spawner"], "royal-guard")

        # Protected matches king-gothryd cast slot.
        protected = self.workspace.people_pack.query_blocks(
            self.workspace.catalog(), self.workspace.concept_store, harm="protected"
        )
        self.assertEqual(protected["total"], 1)
        self.assertEqual(protected["items"][0]["castId"], "king-gothryd")

        # Building type + deriveRegional filter.
        banks = self.workspace.people_pack.query_blocks(
            self.workspace.catalog(), self.workspace.concept_store, building_type=3, derive_regional=True
        )
        self.assertEqual(banks["total"], 1)
        self.assertEqual(banks["items"][0]["buildingTypeName"], "Bank")
        self.assertEqual(banks["items"][0]["factionName"], "Merchants")

    def test_http_routes_and_people_cli_round_trip(self):
        server = make_server("127.0.0.1", 0, self.workspace)
        server.RequestHandlerClass.log_message = lambda *_args: None
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        origin = f"http://127.0.0.1:{server.server_port}"

        def call(method, path, body=None):
            data = None if body is None else json.dumps(body).encode("utf-8")
            headers = {"Content-Type": "application/json"} if body is not None else {}
            try:
                with urlopen(UrlRequest(origin + path, data=data, headers=headers, method=method), timeout=5) as r:
                    return r.status, json.loads(r.read().decode("utf-8"))
            except HTTPError as err:
                try:
                    return err.code, json.loads(err.read().decode("utf-8"))
                finally:
                    err.close()

        status, val = call("GET", "/api/people-pack/validate")
        self.assertEqual(status, 200)
        self.assertTrue(val["valid"])

        status, visuals = call("GET", "/api/people-pack/visuals?q=banker")
        self.assertEqual(status, 200)
        self.assertGreaterEqual(len(visuals), 1)

        status, err = call("PUT", "/api/people-pack/cast/king-gothryd", {"gender": "Invalid"})
        self.assertEqual(status, 400)
        self.assertTrue(err["issues"])

        # CLI smoke check.
        cli_out = subprocess.check_output(
            [sys.executable, str(REPO / "authoring/cms/people_cli.py"), "--url", origin, "validate"], text=True
        )
        self.assertTrue(json.loads(cli_out)["valid"])

        cli_edit = subprocess.check_output(
            [sys.executable, str(REPO / "authoring/cms/people_cli.py"), "--url", origin,
             "edit-cast", "king-gothryd", "--name", "Gothryd II"], text=True
        )
        self.assertEqual(json.loads(cli_edit)["name"], "Gothryd II")

    def test_committed_daggerfall_people_pack_validates_cleanly(self):
        real_lib = Library(REPO / "local/art/reference/npc", REPO.parent / "asset-pipeline", repo=REPO)
        store = PeoplePackStore(real_lib, self.workspace.cache, self.workspace.packs)
        report = store.validate_all()
        self.assertTrue(report["valid"], f"Unexpected pack validation issues: {report['issues']}")
        self.assertEqual(report["counts"]["cast"], 28)
        self.assertEqual(report["counts"]["spawners"], 30)
        self.assertGreaterEqual(report["counts"]["archetypes"], 6)
        self.assertGreater(report["counts"]["slots"], 14000)

    def test_archetypes_validation_and_round_trip_edit(self):
        pack = self.library.people_pack
        self._write(pack / "payload.json", {
            "castFile": "cast/individuals.json",
            "spawnersFile": "spawners.json",
            "archetypesFile": "archetypes.json",
            "blocksFolder": "blocks",
        })
        archetypes = [
            {
                "id": aid,
                "health": {"min": 20, "max": 30},
                "fatigue": 50,
                "magicka": 0,
                "armor": 0,
                "level": 1,
                "baseAttribute": 50,
                "attributes": {},
                "skills": {},
                "defaultHarm": "law-protected-civilian",
                "defaultInteraction": "full-dialogue",
                "walks": True,
                "loadout": [],
            }
            for aid in ("civilian", "static-npc", "clutter-person", "questor", "guard", "combatant", "merchant")
        ]
        self._write(pack / "archetypes.json", {"archetypes": archetypes})
        self.workspace.cache.invalidate(("people-pack-index", pack, self.library.base_payload))

        report = self.workspace.people_pack.validate_all()
        self.assertTrue(report["valid"], report["issues"])
        self.assertEqual(report["counts"]["archetypes"], 7)

        updated = self.workspace.people_pack.update_archetype("merchant", {"health": {"min": 24, "max": 34}})
        self.assertEqual(updated["health"], {"min": 24, "max": 34})

        with self.assertRaises(ValidationError):
            self.workspace.people_pack.update_archetype("merchant", {"health": {"min": 40, "max": 10}})

        with self.assertRaises(ValidationError):
            self.workspace.people_pack.update_spawner(
                "banker", {"archetype": "missing-archetype"},
                self.workspace.catalog(), self.workspace.concept_store,
            )
