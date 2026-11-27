"""Unit and HTTP integration tests for the unified CMS Actors inspector (#11095)."""

import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Thread
from unittest import TestCase
from urllib.error import HTTPError
from urllib.request import Request as UrlRequest, urlopen

from concepts import ConceptStore
from library import REPO, Library
from reviews import ReviewStore
from server import Workspace, make_server


class ActorsCatalogTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.lib = Library(self.root / "local/art/reference/npc", self.root / "pipeline", repo=self.root)

        # Art manifests: 1 creature + 2 npc records (one enemy class mobile, one cast visual).
        self._write(self.lib.npcs / "manifest.json", {
            "entries": [
                {"id": "npc-010", "name": "King Gothryd", "kind": "Named NPC", "image": "gothryd.png"},
                {"id": "npc-099", "name": "Mage Enemy", "kind": "Enemy Class", "image": "mage.png"},
                {"id": "npc-100", "name": "Mage Enemy Female", "kind": "Enemy Class", "image": "mage-f.png"},
            ],
            "aliases": {
                "334-19": "npc-010",
                "mobile-128": "npc-099",
                "mobile-128-female": "npc-100",
            },
        })
        for img in ("gothryd.png", "mage.png", "mage-f.png"):
            p = self.lib.npcs / "art" / img
            p.parent.mkdir(parents=True, exist_ok=True)
            p.touch()

        self._write(self.lib.mobiles / "manifest.json", {
            "mobiles": [
                {"mobileId": 1, "name": "Imp", "directory": "001-imp", "sourceArchive": 256, "variant": "idle", "frames": [{"direction": "front", "file": "imp.png"}]},
            ],
        })
        (self.lib.mobiles / "imp.png").touch()

        base_dir = self.lib.base_payload.parent
        self._write(self.lib.base_payload, {
            "ruleset": "daggerfall",
            "sections": {
                "vocabulary": "core/vocabulary.json",
                "actorTemplateBases": "actors/templates/bases.json",
                "actorTemplateCreatures": "actors/templates/creatures.json",
                "actorTemplateClasses": "actors/templates/classes.json",
                "actorTemplateRoles": "actors/templates/roles.json",
                "actorSpecsCreatures": "actors/specs/creatures.json",
                "actorSpecsEnemies": "actors/specs/enemies.json",
                "actorSpecsTownsfolk": "actors/specs/townsfolk.json",
                "actorSpecsCast": "actors/specs/cast.json",
                "catalogs": "world/catalogs.json",
                "encounters": "actors/encounters.json",
                "enemySpells": "actors/enemy-spells.json",
                "magic": "magic/magic.json",
                "boardSeats": "world/board-seats.json",
                "factions": "world/factions.json",
                "buildingNames": "world/building-names.json",
            },
        })

        self._write(base_dir / "core/vocabulary.json", {
            "attributes": ["strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck"],
            "skills": ["long-blade", "dodging", "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism"],
        })
        self._write(base_dir / "actors/templates/bases.json", {
            "templates": [
                {
                    "id": "actor-base",
                    "attributes": {"strength": 50, "intelligence": 50, "willpower": 50, "agility": 50, "endurance": 50, "personality": 50, "speed": 50, "luck": 50},
                    "skills": {},
                    "health": {"minimum": 10, "maximum": 20},
                    "armor": 0,
                    "level": 1,
                    "scaling": "fixed",
                    "tactic": "legacy",
                    "spellPolicy": "none",
                    "spells": None,
                    "allowedGenders": ["male", "female"],
                },
                {"id": "humanoid", "extends": "actor-base", "canOpenDoors": True},
                {"id": "combat-class", "extends": "humanoid", "scaling": "career-leveled", "isCombatEncounter": True},
                {"id": "creature-base", "extends": "actor-base", "scaling": "level-scaled-skills", "isCombatEncounter": True},
            ],
        })
        self._write(base_dir / "actors/templates/creatures.json", {
            "templates": [
                {
                    "id": "imp",
                    "extends": "creature-base",
                    "health": {"minimum": 12, "maximum": 36},
                    "level": 2,
                    "armor": 6,
                    "castsMagic": True,
                    "spellPolicy": "fixed",
                    "spells": ["spell.007", "spell.010"],
                    "fixedRace": "imp",
                },
            ],
        })
        self._write(base_dir / "actors/templates/classes.json", {
            "templates": [
                {
                    "id": "mage",
                    "extends": "combat-class",
                    "careerRef": "class00",
                    "castsMagic": True,
                    "spellPolicy": "class-tiers",
                    "magicka": 65,
                    "defaultHarm": "law-protected-civilian",
                    "defaultInteraction": "full-dialogue",
                    "walks": False,
                },
            ],
        })
        self._write(base_dir / "actors/templates/roles.json", {
            "templates": [
                {
                    "id": "static-npc",
                    "extends": "humanoid",
                    "health": {"minimum": 20, "maximum": 30},
                    "fatigue": 50,
                    "magicka": 0,
                    "defaultHarm": "protected",
                    "defaultInteraction": "full-dialogue",
                    "walks": False,
                },
            ],
        })
        self._write(base_dir / "actors/specs/creatures.json", {
            "specs": [
                {
                    "id": "imp",
                    "family": "creature",
                    "template": "imp",
                    "published": True,
                    "displayName": "Imp",
                    "genders": [{"gender": "male", "weight": 1}, {"gender": "female", "weight": 1}],
                    "race": "imp",
                    "harm": "combatant",
                    "interaction": "none",
                    "team": "monster",
                    "appearances": {"maleTexture": 256, "femaleTexture": 256},
                    "classicBinding": {"mobileId": 1, "donorName": "Imp", "identity": "imp"},
                },
            ],
        })
        self._write(base_dir / "actors/specs/enemies.json", {
            "specs": [
                {
                    "id": "encounter-mage",
                    "family": "enemy",
                    "template": "mage",
                    "published": True,
                    "displayName": "Mage",
                    "genders": [{"gender": "male", "weight": 1}, {"gender": "female", "weight": 1}],
                    "race": None,
                    "harm": "combatant",
                    "interaction": "none",
                    "team": "knights-and-mages",
                    "appearances": {"maleTexture": 486, "femaleTexture": 485},
                    "classicBinding": {"mobileId": 128, "donorName": "Mage", "identity": "mage"},
                },
            ],
        })
        self._write(base_dir / "actors/specs/townsfolk.json", {
            "specs": [
                {
                    "id": "town-mage-guild-member",
                    "family": "town",
                    "template": "mage",
                    "published": False,
                    "displayName": "Mage Guild Member",
                    "role": "mage",
                    "genders": [{"gender": "male", "weight": 1}],
                    "harm": "law-protected-civilian",
                    "interaction": "full-dialogue",
                    "deriveRegionalFaction": False,
                    "visualMode": "pool",
                    "defaultPool": [{"archive": 334, "record": 19, "gender": "Male", "weight": 5}],
                    "regionalPools": {},
                },
            ],
        })
        self._write(base_dir / "actors/specs/cast.json", {
            "specs": [
                {
                    "id": "king-gothryd",
                    "family": "cast",
                    "template": "static-npc",
                    "published": False,
                    "displayName": "King Gothryd",
                    "genders": [{"gender": "male", "weight": 1}],
                    "race": "breton",
                    "factionId": 364,
                    "naming": "designated",
                    "harm": "protected",
                    "interaction": "full-dialogue",
                    "visual": {"archive": 334, "record": 19},
                    "home": {"block": "S0000160.RDB", "slotId": "d:p04"},
                },
            ],
        })
        self._write(base_dir / "world/catalogs.json", {
            "careers": [
                {"id": "class00", "name": "Mage", "attributeValues": [42, 60, 65, 45, 45, 50, 43, 50], "hitPointsPerLevel": 6},
            ],
            "races": [{"id": "breton"}, {"id": "redguard"}],
        })
        self._write(base_dir / "actors/encounters.json", {
            "tables": [["imp", "imp", "encounter-mage"]],
        })
        self._write(base_dir / "actors/enemy-spells.json", {
            "mobiles": {},
            "classTiers": [[7, 10], [22, 25]],
        })
        self._write(base_dir / "magic/magic.json", {
            "spells": [
                {"key": "spell.007", "identity": 7, "name": "Fireball"},
                {"key": "spell.010", "identity": 10, "name": "Shock"},
                {"key": "spell.022", "identity": 22, "name": "Ice Storm"},
                {"key": "spell.025", "identity": 25, "name": "Lightning"},
            ],
        })
        self._write(base_dir / "world/board-seats.json", {
            "seats": [{"id": "daggerfall", "officeFactionId": 201, "title": "King"}],
        })
        self._write(base_dir / "world/factions.json", {
            "factions": [
                {"id": 201, "name": "Court of Daggerfall", "type": 7, "region": 17, "parent": 0},
                {"id": 364, "name": "King Gothryd", "type": 4, "region": 17, "parent": 201},
            ],
        })
        self._write(base_dir / "world/building-names.json", {
            "regionNames": [f"Region {i}" for i in range(25)],
        })
        self._write(self.root / "content/worldrpg/packs/daggerfall.base/world/iliac-bay.board.json", {
            "seats": [{"id": "daggerfall", "name": "Kingdom of Daggerfall", "kind": "Crown", "holder": "king-gothryd", "home": "daggerfall"}],
        })
        self._write(self.lib.people_pack / "payload.json", {"blocksFolder": "blocks"})
        self._write(self.lib.people_pack / "blocks/rdb-dungeons.json", {
            "kind": "rdb-dungeon",
            "blocks": {
                "S0000160.RDB": [
                    {
                        "slotId": "d:p04",
                        "spawner": "king-gothryd",
                        "position": [1, 2, 3],
                        "interaction": "full-dialogue",
                        "factionId": 364,
                        "visual": {"archive": 334, "record": 19, "gender": "Male"},
                    },
                ],
            },
        })

        self.ws = Workspace(
            self.lib,
            self.root / "links.json",
            self.root / "merges.json",
            ReviewStore(self.root / "reviews.sqlite3"),
            ConceptStore(self.root / "concepts"),
        )

    def _write(self, path: Path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def test_template_inheritance_provenance_direct_children_and_reverse_refs(self):
        mage = self.ws.actors_catalog.get_template("mage", self.ws.catalog(), self.ws.concept_store)
        self.assertIsNotNone(mage)
        self.assertEqual([c["id"] for c in mage["chain"]], ["actor-base", "humanoid", "combat-class", "mage"])
        self.assertEqual(mage["depth"], 3)

        fields_by_name = {f["field"]: f for f in mage["fields"]}
        self.assertEqual(fields_by_name["scaling"]["definedBy"], "combat-class")
        self.assertEqual(fields_by_name["scaling"]["status"], "inherited")
        self.assertEqual(fields_by_name["spellPolicy"]["definedBy"], "mage")
        self.assertEqual(fields_by_name["spellPolicy"]["status"], "overridden")
        self.assertEqual(fields_by_name["hitPointsPerLevel"]["source" if "source" in fields_by_name["hitPointsPerLevel"] else "status"], "career-default")
        self.assertEqual(fields_by_name["hitPointsPerLevel"]["value"], 6)

        attrs = {r["stat"]: r for r in mage["attributeRows"]}
        self.assertEqual(attrs["intelligence"]["value"], 60)
        self.assertEqual(attrs["intelligence"]["status"], "career-default")

        # Shared mage template links to BOTH enemy and town spawners.
        spawner_ids = [s["id"] for s in mage["reverseRefs"]["spawners"]]
        self.assertEqual(spawner_ids, ["encounter-mage", "town-mage-guild-member"])
        self.assertEqual(mage["reverseRefs"]["encounterTables"], [{"tableIndex": 0, "slots": 1}])

        # Parent template lists direct children.
        humanoid = self.ws.actors_catalog.get_template("humanoid", self.ws.catalog(), self.ws.concept_store)
        self.assertEqual([c["id"] for c in humanoid["directChildren"]], ["combat-class", "static-npc"])

    def test_spec_reverse_refs_board_seat_art_links_and_preview(self):
        gothryd = self.ws.actors_catalog.get_spec("king-gothryd", self.ws.catalog(), self.ws.concept_store)
        self.assertEqual(gothryd["editorHref"], "#cast/king-gothryd")
        self.assertEqual(gothryd["reverseRefs"]["blockSlots"]["slotCount"], 1)
        self.assertEqual([s["seatId"] for s in gothryd["reverseRefs"]["boardSeats"]], ["daggerfall"])
        self.assertEqual(gothryd["reverseRefs"]["boardSeats"][0]["title"], "King")
        self.assertEqual(gothryd["artLinks"][0]["id"], "npc-010")

        # Bidirectional art -> actor records on both People (npc-###) and Creatures (creature-###).
        npc10 = self.ws.character("npc-010")
        self.assertIn("king-gothryd", [s["id"] for s in npc10["actorRecords"]["spawners"]])
        npc99 = self.ws.character("npc-099")
        self.assertEqual([s["id"] for s in npc99["actorRecords"]["spawners"]], ["encounter-mage"])
        self.assertEqual([t["id"] for t in npc99["actorRecords"]["templates"]], ["mage"])
        creature1 = self.ws.character("creature-001")
        self.assertEqual([s["id"] for s in creature1["actorRecords"]["spawners"]], ["imp"])
        self.assertEqual([t["id"] for t in creature1["actorRecords"]["templates"]], ["imp"])

        # Preview: level/gender/region resolves career-leveled HP, runtime class-tier spells (`level // 3`),
        # runtime scaled skills (`min(100, level * 5 + 30)`), caster magic schools = 80, and magicka = 10 * level + 100.
        p_lvl2 = self.ws.actors_catalog.preview(
            "spec", "encounter-mage", self.ws.catalog(), self.ws.concept_store,
            level=2, gender="male", region=17,
        )
        self.assertEqual([s["identity"] for s in p_lvl2["effective"]["spells"]], [7, 10])
        self.assertEqual(p_lvl2["effective"]["skills"]["long-blade"], 40)
        self.assertEqual(p_lvl2["effective"]["skills"]["destruction"], 80)
        self.assertEqual(p_lvl2["effective"]["magicka"], 120)

        p_lvl3 = self.ws.actors_catalog.preview(
            "spec", "encounter-mage", self.ws.catalog(), self.ws.concept_store,
            level=3, gender="female", region=20,
        )
        # Runtime tier switches at level 3 (3 // 3 == 1), NOT level 4.
        self.assertEqual([s["identity"] for s in p_lvl3["effective"]["spells"]], [22, 25])
        self.assertEqual(p_lvl3["effective"]["skills"]["long-blade"], 45)
        self.assertEqual(p_lvl3["effective"]["skills"]["destruction"], 80)
        self.assertEqual(p_lvl3["effective"]["magicka"], 130)

        p_lvl5 = self.ws.actors_catalog.preview(
            "spec", "encounter-mage", self.ws.catalog(), self.ws.concept_store,
            level=5, gender="female", region=20,
        )
        self.assertEqual(p_lvl5["inputs"]["gender"], "female")
        self.assertEqual(p_lvl5["effective"]["race"], "redguard")
        self.assertEqual(p_lvl5["effective"]["health"], {"minimum": 15, "maximum": 40})
        self.assertEqual([s["name"] for s in p_lvl5["effective"]["spells"]], ["Ice Storm", "Lightning"])
        self.assertEqual(p_lvl5["effective"]["skills"]["long-blade"], 55)
        self.assertEqual(p_lvl5["effective"]["skills"]["destruction"], 80)
        self.assertEqual(p_lvl5["effective"]["magicka"], 150)
        self.assertEqual(p_lvl5["effective"]["visual"]["textureArchive"], 485)
        self.assertEqual(p_lvl5["effective"]["visual"]["art"]["id"], "npc-100")

        # Scaled creature caster (imp, level 2): ordinary skills = 40, magic schools = 80, magicka = 120.
        p_imp = self.ws.actors_catalog.preview(
            "spec", "imp", self.ws.catalog(), self.ws.concept_store,
            level=2, gender="male", region=17,
        )
        self.assertEqual(p_imp["effective"]["skills"]["long-blade"], 40)
        self.assertEqual(p_imp["effective"]["skills"]["destruction"], 80)
        self.assertEqual(p_imp["effective"]["magicka"], 120)

        # Disallowed gender snaps with an explanatory note.
        p_cast = self.ws.actors_catalog.preview(
            "spec", "king-gothryd", self.ws.catalog(), self.ws.concept_store,
            level=1, gender="female", region=17,
        )
        self.assertEqual(p_cast["inputs"]["gender"], "male")
        self.assertTrue(p_cast["notes"])

    def test_http_routes_and_real_repo_actors_catalog(self):
        server = make_server("127.0.0.1", 0, self.ws)
        server.RequestHandlerClass.log_message = lambda *_args: None
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        origin = f"http://127.0.0.1:{server.server_port}"

        def get(path: str):
            try:
                with urlopen(UrlRequest(origin + path, method="GET"), timeout=5) as r:
                    return r.status, json.loads(r.read().decode("utf-8"))
            except HTTPError as err:
                try:
                    return err.code, json.loads(err.read().decode("utf-8"))
                finally:
                    err.close()

        st, ov = get("/api/actors/overview")
        self.assertEqual(st, 200)
        self.assertTrue(ov["available"])
        self.assertEqual(ov["counts"]["templates"], 7)
        self.assertEqual(ov["counts"]["specs"], 4)

        st, tpl = get("/api/actors/templates/mage")
        self.assertEqual(st, 200)
        self.assertEqual(tpl["id"], "mage")

        st, sp = get("/api/actors/specs/king-gothryd")
        self.assertEqual(st, 200)
        self.assertEqual(sp["id"], "king-gothryd")

        st, prev = get("/api/actors/preview?kind=spec&id=encounter-mage&level=4&gender=female&region=20")
        self.assertEqual(st, 200)
        self.assertEqual(prev["effective"]["race"], "redguard")

        st, _ = get("/api/actors/specs/does-not-exist")
        self.assertEqual(st, 404)

        # Verify real committed repository loads cleanly with all templates, specs, and board seats.
        real_lib = Library(REPO / "local/art/reference/npc", REPO.parent / "asset-pipeline", repo=REPO)
        real_ws = Workspace(real_lib, self.root / "r-links.json", self.root / "r-merges.json", ReviewStore(self.root / "r.sqlite3"), ConceptStore(self.root / "rc"))
        real_ov = real_ws.actors_catalog.overview(real_ws.catalog(), real_ws.concept_store)
        self.assertTrue(real_ov["available"])
        self.assertGreaterEqual(real_ov["counts"]["templates"], 70)
        self.assertEqual(real_ov["counts"]["byFamily"]["cast"], 43)
        self.assertEqual(real_ov["counts"]["byFamily"]["town"], 31)
        real_gothryd = real_ws.actors_catalog.get_spec("king-gothryd", real_ws.catalog(), real_ws.concept_store)
        self.assertEqual([s["seatId"] for s in real_gothryd["reverseRefs"]["boardSeats"]], ["daggerfall"])
        self.assertEqual(real_gothryd["familyId"], "fam:dynasty:daggerfall")
        self.assertEqual({m["id"] for m in real_gothryd["familyMembers"]}, {"queen-aubk-i", "mynisera"})
        real_courtier = real_ws.actors_catalog.get_spec("town-courtier", real_ws.catalog(), real_ws.concept_store)
        self.assertEqual(real_courtier["socialGrouping"], "noble-house")
        real_resident = real_ws.actors_catalog.get_spec("town-house-resident", real_ws.catalog(), real_ws.concept_store)
        self.assertEqual(real_resident["socialGrouping"], "building-household")
        real_mage = real_ws.actors_catalog.get_template("mage", real_ws.catalog(), real_ws.concept_store)
        mage_spawners = {s["id"] for s in real_mage["reverseRefs"]["spawners"]}
        self.assertIn("encounter-mage", mage_spawners)
        self.assertIn("town-mage-guild-member", mage_spawners)

        # Real committed encounter-mage preview matches runtime AtLevel / EnemySpells / MechanicsState:
        # level 2 -> tier 0 ([16, 20]), level 3 -> tier 1 ([22, 23, 31]), level 6 -> tier 2 ([6, 7, 22, 25, 31]),
        # level 5 -> ordinary skills 55, magic schools 80, magicka 150.
        rm2 = real_ws.actors_catalog.preview("spec", "encounter-mage", real_ws.catalog(), real_ws.concept_store, level=2)
        rm3 = real_ws.actors_catalog.preview("spec", "encounter-mage", real_ws.catalog(), real_ws.concept_store, level=3)
        rm5 = real_ws.actors_catalog.preview("spec", "encounter-mage", real_ws.catalog(), real_ws.concept_store, level=5)
        rm6 = real_ws.actors_catalog.preview("spec", "encounter-mage", real_ws.catalog(), real_ws.concept_store, level=6)
        self.assertEqual([s["identity"] for s in rm2["effective"]["spells"]], [16, 20])
        self.assertEqual([s["identity"] for s in rm3["effective"]["spells"]], [22, 23, 31])
        self.assertEqual([s["identity"] for s in rm6["effective"]["spells"]], [6, 7, 22, 25, 31])
        self.assertEqual(rm5["effective"]["skills"]["long-blade"], 55)
        self.assertEqual(rm5["effective"]["skills"]["destruction"], 80)
        self.assertEqual(rm5["effective"]["magicka"], 150)

        # Real committed scaled creature (rat at level 1) populates vocabulary skills with 1*5+30 = 35 and is not a caster.
        rat1 = real_ws.actors_catalog.preview("spec", "rat", real_ws.catalog(), real_ws.concept_store, level=1)
        self.assertEqual(rat1["effective"]["skills"]["hand-to-hand"], 35)
        self.assertEqual(rat1["effective"]["skills"]["destruction"], 35)
        self.assertEqual(rat1["effective"]["spells"], [])

        # Schedule profiles and timeOfDay / weather preview (#11100):
        self.assertEqual(len(real_ov["enums"]["schedules"]), 8)
        self.assertEqual(real_resident["schedule"], "street-walker")
        self.assertEqual(real_gothryd["schedule"], "always-stationed")

        # Street walker: outdoors midday sunny, sheltered in thunder, evening tavern/home at 20:30, home at 23:30.
        walker_day = real_ws.actors_catalog.preview(
            "spec", "town-house-resident", real_ws.catalog(), real_ws.concept_store,
            time_of_day="12:00", weather="sunny",
        )
        walker_storm = real_ws.actors_catalog.preview(
            "spec", "town-house-resident", real_ws.catalog(), real_ws.concept_store,
            time_of_day="12:00", weather="thunder",
        )
        walker_night = real_ws.actors_catalog.preview(
            "spec", "town-house-resident", real_ws.catalog(), real_ws.concept_store,
            time_of_day="23:30", weather="sunny",
        )
        self.assertEqual(walker_day["effective"]["schedule"]["placement"], "exterior-street")
        self.assertNotEqual(walker_storm["effective"]["schedule"]["placement"], "exterior-street")
        self.assertEqual(walker_night["effective"]["schedule"]["placement"], "sheltered-in-home")

        # City guard stays on duty at 23:30 in thunder; underworld-night is active outdoors at 23:30 in rain.
        guard_storm = real_ws.actors_catalog.preview(
            "spec", "town-blades-guard", real_ws.catalog(), real_ws.concept_store,
            time_of_day="23:30", weather="thunder",
        )
        self.assertEqual(guard_storm["effective"]["schedule"]["placement"], "exterior-street")
        uw_night = real_ws.actors_catalog.preview(
            "spec", "town-dark-brotherhood-member", real_ws.catalog(), real_ws.concept_store,
            time_of_day="23:30", weather="rain",
        )
        self.assertEqual(uw_night["effective"]["schedule"]["placement"], "exterior-street")

        # Shopkeeper (town-armorer, 09:00-19:00) is open at 12:00 and evening-tavern-eligible once closed at 20:30.
        shop_open = real_ws.actors_catalog.preview(
            "spec", "town-armorer", real_ws.catalog(), real_ws.concept_store,
            time_of_day="12:00", weather="sunny",
        )
        shop_evening = real_ws.actors_catalog.preview(
            "spec", "town-armorer", real_ws.catalog(), real_ws.concept_store,
            time_of_day="20:30", weather="sunny",
        )
        self.assertEqual(shop_open["effective"]["schedule"]["reason"], "open-hours")
        self.assertEqual(shop_open["effective"]["schedule"]["openHours"], "09:00–19:00")
        self.assertEqual(shop_evening["effective"]["schedule"]["reason"], "evening-tavern-eligible")

        # Opening hours derive live from DaggerfallBuildingEntryPolicy.cs (zero duplicate table):
        # mutating the canonical C# policy in an isolated repo immediately updates CMS preview.
        hours_repo = self.root / "hours-repo"
        pack_dir = hours_repo / "content/worldrpg/packs/daggerfall.base"
        (pack_dir / "actors/templates").mkdir(parents=True, exist_ok=True)
        (pack_dir / "actors/specs").mkdir(parents=True, exist_ok=True)
        (pack_dir / "world").mkdir(parents=True, exist_ok=True)
        (pack_dir / "payload.json").write_text(json.dumps({
            "ruleset": "daggerfall",
            "sections": {
                "actorTemplateBases": "actors/templates/bases.json",
                "actorSpecsTownsfolk": "actors/specs/townsfolk.json",
                "actorSchedules": "actors/schedules.json",
                "buildingNames": "world/building-names.json",
            },
        }), encoding="utf-8")
        (pack_dir / "actors/templates/bases.json").write_text(json.dumps({
            "templates": [{"id": "merchant", "health": {"minimum": 10, "maximum": 20}, "armor": 0}],
        }), encoding="utf-8")
        (pack_dir / "actors/specs/townsfolk.json").write_text(json.dumps({
            "specs": [{
                "id": "town-armorer",
                "family": "town",
                "template": "merchant",
                "displayName": "Armorer",
                "role": "merchant",
                "genders": [{"gender": "male", "weight": 1}],
                "schedule": "shopkeeper",
                "defaultPool": [],
            }],
        }), encoding="utf-8")
        (pack_dir / "actors/schedules.json").write_text(
            (REPO / "content/worldrpg/packs/daggerfall.base/actors/schedules.json").read_text(encoding="utf-8"),
            encoding="utf-8",
        )
        (pack_dir / "world/building-names.json").write_text(json.dumps({"regionNames": ["Daggerfall"]}), encoding="utf-8")
        policy_file = hours_repo / "src/WorldRpg.Rulesets.Daggerfall/Interiors/DaggerfallBuildingEntryPolicy.cs"
        policy_file.parent.mkdir(parents=True, exist_ok=True)
        policy_file.write_text(
            "private static readonly int[] OpenHours = [7, 8, 11];\n"
            "private static readonly int[] CloseHours = [22, 16, 15];\n",
            encoding="utf-8",
        )
        hours_lib = Library(self.lib.npcs, self.root / "pipeline", repo=hours_repo)
        hours_ws = Workspace(hours_lib, self.root / "h-links.json", self.root / "h-merges.json", ReviewStore(self.root / "h.sqlite3"), ConceptStore(self.root / "hc"))
        p_closed = hours_ws.actors_catalog.preview("spec", "town-armorer", hours_ws.catalog(), hours_ws.concept_store, time_of_day="10:00", weather="sunny")
        self.assertEqual(p_closed["effective"]["schedule"]["openHours"], "11:00–15:00")
        self.assertEqual(p_closed["effective"]["schedule"]["reason"], "after-hours")
        # Now edit the single canonical policy file (and bump mtime) -> CMS preview reflects new hours immediately.
        policy_file.write_text(
            "private static readonly int[] OpenHours = [7, 8, 8];\n"
            "private static readonly int[] CloseHours = [22, 16, 21];\n",
            encoding="utf-8",
        )
        st_now = policy_file.stat()
        os.utime(policy_file, (st_now.st_atime + 2, st_now.st_mtime + 2))
        p_open = hours_ws.actors_catalog.preview("spec", "town-armorer", hours_ws.catalog(), hours_ws.concept_store, time_of_day="10:00", weather="sunny")
        self.assertEqual(p_open["effective"]["schedule"]["openHours"], "08:00–21:00")
        self.assertEqual(p_open["effective"]["schedule"]["reason"], "open-hours")

        # Built CMS frontend bundle includes the unified Actors routes, reverse art links, and schedule preview controls.
        app_bundle = (REPO / "authoring/cms/web/dist/app.js").read_text(encoding="utf-8")
        self.assertIn("/api/actors/overview", app_bundle)
        self.assertIn("/api/actors/preview", app_bundle)
        self.assertIn("Unified actor records", app_bundle)
        self.assertIn("Family / Dynasty members", app_bundle)
        self.assertIn("Social grouping", app_bundle)
        self.assertIn("Time of day", app_bundle)
        self.assertIn("Schedule & weather", app_bundle)
