"""Unit and HTTP integration tests for in-CMS People curation (compare, merge, guarded retire, art-need filters)."""

import base64
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Thread
from unittest import TestCase
from urllib.error import HTTPError
from urllib.request import Request as UrlRequest, urlopen

from concepts import ConceptStore
from library import Library
from reviews import ReviewStore
from server import Workspace, make_server

PNG_1X1 = base64.b64encode(
    base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
).decode("ascii")


class PeopleCurationTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.library = Library(self.root / "local/art/reference/npc", self.root / "pipeline", repo=self.root)
        self._write(self.library.npcs / "manifest.json", {
            "entries": [
                {"id": "npc-010", "name": "King Gothryd", "kind": "Named NPC", "image": "a.png", "aliases": ["334-19"]},
                {"id": "npc-020", "name": "Banker Sprite", "kind": "Billboard", "image": "b.png", "aliases": ["183-5"]},
                {"id": "npc-099", "name": "Unused Duplicate", "kind": "Billboard", "image": "c.png", "aliases": ["184-99"]},
            ],
            "aliases": {
                "334-19": "npc-010",
                "183-5": "npc-020",
                "184-99": "npc-099",
            },
            "briefs": "briefs.json",
        })
        for img in ("a.png", "b.png", "c.png"):
            p = self.library.npcs / "art" / img
            p.parent.mkdir(parents=True, exist_ok=True)
            p.touch()

        self._write(self.library.base_payload, {
            "factions": {"factions": [{"id": 364, "name": "King Gothryd", "type": 4, "typeName": "Individual"}]},
            "buildingNames": {"regionNames": ["Daggerfall"]},
        })
        pack = self.library.people_pack
        self._write(pack / "payload.json", {"castFile": "cast/individuals.json", "spawnersFile": "spawners.json", "blocksFolder": "blocks"})
        self._write(pack / "cast/individuals.json", {
            "individuals": [{
                "id": "king-gothryd", "factionId": 364, "name": "King Gothryd", "race": "breton", "gender": "Male",
                "visual": {"archive": 334, "record": 19}, "homeBlock": "S0000160.RDB", "homeSlotId": "d:p04",
                "interaction": "full-dialogue", "harm": "protected",
            }],
        })
        self._write(pack / "spawners.json", {
            "spawners": [{
                "id": "banker", "role": "bank teller", "interaction": "full-dialogue", "harm": "law-protected-civilian",
                "deriveRegionalFaction": True, "visualMode": "pool",
                "defaultPool": [{"archive": 183, "record": 5, "gender": "Male", "weight": 10}],
                "regionalPools": {},
            }],
        })
        self._write(pack / "blocks/rdb-dungeons.json", {
            "kind": "rdb-dungeon",
            "blocks": {
                "S0000160.RDB": [{
                    "slotId": "d:p04", "castId": "king-gothryd", "position": [0, 0, 0],
                    "interaction": "full-dialogue", "factionId": 364,
                    "visual": {"archive": 334, "record": 19, "gender": "Male"},
                }],
            },
        })

        # Fake merge/retire script under scratch repo that updates merges.json and manifest.json cleanly.
        self.merges = self.root / "merges.json"
        self._write(self.merges, {"npc-011": "npc-010"})
        script = self.library.merge_script
        script.parent.mkdir(parents=True, exist_ok=True)
        script.write_text("""#!/usr/bin/env python3
import argparse, json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument("candidate", type=Path)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--merges", type=Path, required=True)
args = p.parse_args()
cand = json.loads(args.candidate.read_text())
ledger = json.loads(args.merges.read_text())
manifest_path = args.output / "manifest.json"
manifest = json.loads(manifest_path.read_text())

if "retire" in cand:
    for nid in cand["retire"]:
        ledger[nid] = None
        manifest["entries"] = [e for e in manifest["entries"] if e["id"] != nid]
        manifest["aliases"] = {k: v for k, v in manifest["aliases"].items() if v != nid}
elif "restore" in cand:
    for nid in cand["restore"]:
        ledger.pop(nid, None)
        manifest["entries"].append({"id": nid, "name": "Restored " + nid, "kind": "Billboard", "aliases": []})
else:
    keep, members = cand["keep"], cand["merge"]
    for nid in members:
        ledger[nid] = keep
        manifest["entries"] = [e for e in manifest["entries"] if e["id"] != nid]
        manifest["aliases"][nid] = keep
        for k, v in list(manifest["aliases"].items()):
            if v == nid:
                manifest["aliases"][k] = keep

args.merges.write_text(json.dumps(ledger, indent=2, sort_keys=True) + "\\n")
manifest_path.write_text(json.dumps(manifest, indent=2) + "\\n")
""", encoding="utf-8")

        self.workspace = Workspace(
            self.library,
            self.root / "links.json",
            self.merges,
            ReviewStore(self.root / "reviews.sqlite3"),
            ConceptStore(self.root / "concepts"),
        )
        self.server = make_server("127.0.0.1", 0, self.workspace)
        self.server.RequestHandlerClass.log_message = lambda *_args: None
        self.thread = Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.origin = f"http://127.0.0.1:{self.server.server_port}"

    def _write(self, path: Path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def _request(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body).encode("utf-8")
        headers = {"Content-Type": "application/json"} if body is not None else {}
        try:
            with urlopen(UrlRequest(self.origin + path, data=data, headers=headers, method=method), timeout=5) as r:
                return r.status, json.loads(r.read().decode("utf-8"))
        except HTTPError as err:
            try:
                return err.code, json.loads(err.read().decode("utf-8"))
            finally:
                err.close()

    def test_catalog_enriches_people_with_art_need_and_pack_usage_counts(self):
        cand = self.workspace.concept_action("npc-010", "candidates", {
            "batch": "b1", "name": "front", "filename": "front.png", "image": PNG_1X1,
            "details": {"prompt": "Royal portrait", "seed": 1},
        })
        self.workspace.concept_action("npc-010", "approve", {"candidate": cand["id"], "view": "front"})

        status, overview = self._request("GET", "/api/catalog")
        self.assertEqual(status, 200)
        by_id = {row["id"]: row["curation"] for row in overview["people"]}
        self.assertEqual(by_id["npc-010"]["castIds"], ["king-gothryd"])
        self.assertTrue(by_id["npc-010"]["hasApprovedConcept"])
        self.assertEqual(by_id["npc-020"]["spawnerIds"], ["banker"])
        self.assertFalse(by_id["npc-020"]["hasApprovedConcept"])
        self.assertEqual((by_id["npc-099"]["castCount"], by_id["npc-099"]["spawnerCount"], by_id["npc-099"]["slotCount"]), (0, 0, 0))

    def test_compare_and_merge_surfaces_concept_conflicts_and_updates_chart(self):
        for npc in ("npc-010", "npc-020"):
            cand = self.workspace.concept_action(npc, "candidates", {
                "batch": "b1", "name": "front", "filename": "front.png", "image": PNG_1X1,
                "details": {"prompt": f"Portrait {npc}", "seed": 1},
            })
            self.workspace.concept_action(npc, "approve", {"candidate": cand["id"], "view": "front"})

        status, compared = self._request("POST", "/api/people/compare", {"ids": ["npc-010", "npc-020"]})
        self.assertEqual(status, 200)
        self.assertEqual([item["id"] for item in compared["items"]], ["npc-010", "npc-020"])
        self.assertTrue(any("approved front concepts" in w for w in compared["warnings"]))

        status, merged = self._request("POST", "/api/people/merge", {"keep": "npc-020", "merge": ["npc-099"]})
        self.assertEqual(status, 200)
        self.assertEqual(merged["keep"], "npc-020")
        ledger = json.loads(self.merges.read_text(encoding="utf-8"))
        self.assertEqual(ledger["npc-099"], "npc-020")

    def test_retirement_guards_merged_keepers_active_references_and_allows_safe_or_forced_retire_and_restore(self):
        # 1. npc-010 is a merge keeper for npc-011 -> hard blocker (409 even with force=True).
        status, refs = self._request("GET", "/api/people/npc-010/references")
        self.assertEqual(status, 200)
        self.assertFalse(refs["canRetire"])
        self.assertEqual(refs["mergedMembers"], ["npc-011"])

        status, blocked = self._request("POST", "/api/people/npc-010/retire", {"force": True})
        self.assertEqual(status, 409)
        self.assertIn("merge keeper", blocked["error"])

        # 2. npc-020 is used by banker spawner -> requires confirmation (409 without force, 200 with force=True).
        status, refs20 = self._request("GET", "/api/people/npc-020/references")
        self.assertEqual(status, 200)
        self.assertTrue(refs20["canRetire"])
        self.assertTrue(refs20["requiresConfirmation"])
        self.assertTrue(any("banker" in w for w in refs20["warnings"]))

        status, guarded = self._request("POST", "/api/people/npc-020/retire", {"force": False})
        self.assertEqual(status, 409)
        self.assertIn("references", guarded)

        # 3. npc-099 has zero references -> retires cleanly without force, shows in /retired, and can restore.
        status, refs99 = self._request("GET", "/api/people/npc-099/references")
        self.assertEqual(status, 200)
        self.assertTrue(refs99["canRetire"])
        self.assertFalse(refs99["requiresConfirmation"])

        status, retired = self._request("POST", "/api/people/npc-099/retire", {})
        self.assertEqual(status, 200)
        self.assertEqual(retired["retired"], ["npc-099"])

        status, retired_list = self._request("GET", "/api/people/retired")
        self.assertEqual((status, retired_list), (200, {"retired": ["npc-099"]}))

        status, restored = self._request("POST", "/api/people/npc-099/restore", {})
        self.assertEqual(status, 200)
        self.assertEqual(restored["restored"], ["npc-099"])

    def test_batch_retire_guards_blockers_and_references_and_workspace_status_responds(self):
        # Batch with a merge keeper (npc-010) is hard-blocked even with force=True.
        status, report = self._request("POST", "/api/people/references-batch", {"ids": ["npc-010", "npc-099"]})
        self.assertEqual(status, 200)
        self.assertFalse(report["canRetireAll"])
        self.assertEqual([i["id"] for i in report["items"]], ["npc-010", "npc-099"])

        status, blocked = self._request("POST", "/api/people/retire-batch", {"ids": ["npc-010", "npc-099"], "force": True})
        self.assertEqual(status, 409)
        self.assertIn("npc-010", blocked["error"])

        # Batch with active reference (npc-020) + unused (npc-099) requires force=True.
        status, guarded = self._request("POST", "/api/people/retire-batch", {"ids": ["npc-020", "npc-099"], "force": False})
        self.assertEqual(status, 409)
        self.assertIn("npc-020", guarded["error"])

        # Forced batch retires both in one action.
        status, done = self._request("POST", "/api/people/retire-batch", {"ids": ["npc-020", "npc-099"], "force": True})
        self.assertEqual(status, 200)
        self.assertEqual(done["retired"], ["npc-020", "npc-099"])

        status, ws = self._request("GET", "/api/workspace/status")
        self.assertEqual(status, 200)
        self.assertIn("gitAvailable", ws)
        self.assertIn("modified", ws)

    def test_enriches_enemy_spawners_wandering_townsfolk_faction_cast_and_canonical_concept_preview(self):
        # Extend manifest with an enemy class, a wandering street townsfolk, and a faction-linked named cast record.
        manifest = json.loads((self.library.npcs / "manifest.json").read_text(encoding="utf-8"))
        manifest["entries"].extend([
            {"id": "npc-468", "name": "Mage, Healer (male)", "kind": "Enemy class", "image": "a.png", "aliases": ["mobile-128", "mobile-486"]},
            {"id": "npc-385", "name": "Breton male outfit 1", "kind": "Wandering townsfolk", "image": "b.png", "aliases": ["385", "mobile-385"]},
            {"id": "npc-076", "name": "Oswin Harrowgate", "kind": "Named NPC", "image": "c.png", "aliases": ["npc-faction-1000"]},
        ])
        manifest["aliases"].update({
            "mobile-128": "npc-468",
            "mobile-486": "npc-468",
            "385": "npc-385",
            "mobile-385": "npc-385",
            "npc-faction-1000": "npc-076",
        })
        self._write(self.library.npcs / "manifest.json", manifest)

        # Add split base sections for enemies, catalogs, and cast.
        self._write(self.library.base_payload, {
            "ruleset": "daggerfall",
            "sections": {
                "factions": "world/factions.json",
                "buildingNames": "world/building-names.json",
                "actorSpecsEnemies": "actors/specs/enemies.json",
                "actorSpecsCast": "actors/specs/cast.json",
                "catalogs": "world/catalogs.json",
            },
        })
        base_dir = self.library.base_payload.parent
        self._write(base_dir / "world/factions.json", {
            "factions": [
                {"id": 364, "name": "King Gothryd", "type": 4, "typeName": "Individual"},
                {"id": 1000, "name": "Oswin Harrowgate", "type": 4, "typeName": "Individual"},
            ],
        })
        self._write(base_dir / "world/building-names.json", {"regionNames": ["Daggerfall"]})
        self._write(base_dir / "actors/specs/enemies.json", {
            "specs": [{
                "id": "encounter-mage", "family": "enemy", "template": "mage",
                "appearances": {"maleTexture": 486, "femaleTexture": 485},
                "classicBinding": {"mobileId": 128, "donorName": "Mage", "affinity": "Human"},
            }],
        })
        self._write(base_dir / "actors/specs/cast.json", {
            "specs": [{
                "id": "oswin-harrowgate", "family": "cast", "template": "static-npc",
                "displayName": "Oswin Harrowgate", "factionId": 1000,
                "visual": {"archive": 334, "record": 15},
            }],
        })
        self._write(base_dir / "world/catalogs.json", {
            "races": [{"id": "breton", "townspeople": {"male": [385], "female": [453]}}],
        })

        # Approve a front concept for npc-076 in ConceptStore.
        cand = self.workspace.concept_action("npc-076", "candidates", {
            "batch": "b1", "name": "front", "filename": "front.png", "image": PNG_1X1,
            "details": {"prompt": "Guildmaster portrait", "seed": 1},
        })
        self.workspace.concept_action("npc-076", "approve", {"candidate": cand["id"], "view": "front"})

        status, overview = self._request("GET", "/api/catalog")
        self.assertEqual(status, 200)
        by_id = {row["id"]: row for row in overview["people"]}

        self.assertEqual(by_id["npc-468"]["curation"]["spawnerIds"], ["encounter-mage"])
        self.assertEqual(by_id["npc-468"]["curation"]["spawnerCount"], 1)
        self.assertIsNone(by_id["npc-468"]["conceptPreview"])

        self.assertEqual(by_id["npc-385"]["curation"]["spawnerIds"], ["street-breton-male"])
        self.assertEqual(by_id["npc-385"]["curation"]["spawnerCount"], 1)

        self.assertEqual(by_id["npc-076"]["curation"]["castIds"], ["oswin-harrowgate"])
        self.assertEqual(by_id["npc-076"]["curation"]["castCount"], 1)
        self.assertTrue(by_id["npc-076"]["curation"]["hasApprovedConcept"])
        self.assertEqual(by_id["npc-076"]["conceptPreview"], cand["url"])

        # Guarded retire warnings also recognize enemy, wandering, and faction-linked cast usage.
        for npc_id, expected_token in [
            ("npc-468", "encounter-mage"),
            ("npc-385", "street-breton-male"),
            ("npc-076", "oswin-harrowgate"),
        ]:
            st, refs = self._request("GET", f"/api/people/{npc_id}/references")
            self.assertEqual(st, 200)
            self.assertTrue(refs["requiresConfirmation"])
            self.assertTrue(any(expected_token in w for w in refs["warnings"]), refs["warnings"])
        _, refs76 = self._request("GET", "/api/people/npc-076/references")
        self.assertEqual(refs76["conceptPreview"], cand["url"])
