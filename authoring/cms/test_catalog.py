import json
from collections import Counter
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from catalog import ASSETS, CATEGORIES, Catalog, category_of, model_category
from concepts import ConceptStore
from library import MODEL_CATEGORIES, Library, read_json
from server import Workspace


SCENERY = """id,category,kind,archive,record,climate,directory,width,height,frames,exterior_blocks,exterior_placements,interior_blocks,interior_placements,dungeon_blocks,dungeon_placements,scattered,light
nature-500-12,nature,,500,12,rainforest,nature/texture-500,40,80,1,0,0,0,0,0,0,1,0
nature-213-2,nature,plant,213,2,,nature/texture-213,30,40,1,63,136,7,7,0,0,0,0
prop-210-3,prop,light,210,3,,props/texture-210,20,50,4,1,2,3,4,0,0,0,1
creature-201-2,creature,animal,201,2,,creatures/texture-201,60,40,1,225,2309,0,0,0,0,0,0
"""
SURFACES = """archive,record,set,climate,width,height,frames,exterior_blocks,interior_blocks,dungeon_blocks,flat_planes,standing_planes,span_u,span_v
2,1,set-02,temperate,64,64,1,3,0,0,0,0,0,0
2,2,set-02,temperate,64,64,1,0,0,0,0,0,0,0
"""


class CatalogTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.library = Library(self.root / "local/art/reference/npc", self.root / "pipeline", repo=self.root)
        self.path = self.root / "links.json"
        self.workspace = Workspace(self.library, self.path, self.root / "merges.json")
        self.write(self.library.npcs / "manifest.json", {
            "entries": [{"id": "npc-381", "name": "Villager", "kind": "Wandering townsfolk"},
                        {"id": "npc-468", "name": "Mage, Healer (male)", "kind": "Enemy class",
                         "aliases": ["mobile-486", "npc-486", "mobile-128", "mobile-132"]},
                        {"id": "npc-467", "name": "Mage, Healer (female)", "kind": "Enemy class",
                         "aliases": ["mobile-485", "npc-485", "mobile-128-female", "mobile-132-female"]}],
            "aliases": {"381-A": "npc-381", "npc-382": "npc-381", "npc-383": "npc-381", "mobile-128": "npc-468",
                        "mobile-132": "npc-468", "mobile-128-female": "npc-467", "mobile-132-female": "npc-467"},
        })
        self.write(self.library.mobiles / "manifest.json", {"mobiles": [
            {"directory": "mobile-4", "name": "GrizzlyBear", "sourceArchive": 259, "mobileId": 4, "variant": "base"},
            {"directory": "mobile-128", "name": "Mage", "sourceArchive": 486, "mobileId": 128, "variant": "base"},
            {"directory": "mobile-128-female", "name": "Mage", "sourceArchive": 485, "mobileId": 128, "variant": "female"},
        ]})
        self.write(self.library.flora / "catalogue.json", {"500-12": {
            "key": "500-12", "climate": "Rainforest", "form": "palm", "height": 4.2, "triangles": 900, "category": "Trees"}})
        self.write(self.library.model_notes, {"444": {"status": "accepted", "note": "Battlements"},
                                              "41000": {"status": "accepted", "note": "Bed"},
                                              "60511": {"status": "accepted", "note": "Procedural rock"}})
        self.write(self.library.art_payload, {"artReplacements": {
            "models": {"41000": {"triangles": 300}},
            "splatPeople": {"clips": ["walk", "gentle", "idle"], "walks": ["walk"], "skirtWalks": ["gentle"], "idles": ["idle", "idle-hips"]},
            "peopleRig": {"figure": "w453", "walk": "walk", "idle": "idle", "clips": ["attention", "idle", "walk"]}}})
        self.write(self.library.classic_media, {"audio": [
            {"clip": "ambient.bird1", "mediaId": "audio.ambient.bird1", "sourceRecordOrdinal": 437, "sampleRate": 11025},
            {"clip": "swing", "mediaId": "audio.melee.dagger.swing", "sourceRecordOrdinal": 106, "sampleRate": 11025},
            {"clip": "sound.165", "mediaId": "audio.source.165", "sourceRecordOrdinal": 165, "sampleRate": 11025}]})
        self.library.music_sources.parent.mkdir(parents=True, exist_ok=True)
        self.library.music_sources.write_text("dungeon-01\trenders/04.wav\ntown-01\trenders/town.wav\n")
        self.library.scenery.mkdir(parents=True, exist_ok=True)
        (self.library.scenery / "usage.csv").write_text(SCENERY)
        self.library.surfaces.mkdir(parents=True, exist_ok=True)
        (self.library.surfaces / "usage.csv").write_text(SURFACES)
        self.pipeline("128", "empty")
        self.pipeline("381-A", "approved")

    def write(self, path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))

    def pipeline(self, id, state):
        root = self.library.characters / id
        path = "01-concept/approved/front.png"
        self.write(root / "character.json", {"id": id, "stages": {
            "01-concept": {"state": state, "outputs": {"front": {"path": path}} if state == "approved" else {}},
        }})
        front = root / path
        front.parent.mkdir(parents=True)
        front.touch()

    def overview(self):
        return Catalog(self.library, {}).overview()

    def ids(self, category):
        return [row["id"] for row in self.overview()[category]]

    def test_every_listed_record_sits_in_the_category_its_id_names_once(self):
        overview = self.overview()
        ids = [row["id"] for category in ASSETS for row in overview[category]]
        self.assertEqual([id for id, count in Counter(ids).items() if count > 1], [])
        for category in ASSETS:
            for row in overview[category]:
                self.assertEqual((row["id"], category_of(row["id"])), (row["id"], category))
        self.assertEqual(overview["categories"], CATEGORIES)

    def test_records_sort_by_what_they_show_not_how_classic_drew_them(self):
        # The placed plants join the nature sets, the placed animals join the monsters, and the flora review's record
        # stands for its nature set record rather than the export's.
        self.assertEqual(self.ids("nature"), ["nature-500-12", "nature-213-2", "nature-m60511"])
        self.assertEqual(
            self.ids("creatures"),
            ["creature-004", "creature-202", "creature-203", "creature-204", "creature-205", "creature-206", "creature-207", "creature-208"],
        )
        self.assertEqual(self.ids("props"), ["prop-210-3", "prop-m41000"])
        self.assertEqual(self.ids("architecture"), ["arch-m444"])
        nature = {row["id"]: row for row in self.overview()["nature"]}
        self.assertEqual((nature["nature-500-12"]["kind"], nature["nature-213-2"]["kind"]), ("tree", "plant"))
        catalog = Catalog(self.library, {})
        camel = catalog.get("creature-204")
        self.assertEqual((camel["name"], camel["kind"]), ("Camel", "Animal"))
        self.assertEqual(camel["game"]["placementCount"], 2309)
        self.assertEqual(catalog.resolve("creature-201-2"), "creature-204")

    def test_human_enemy_classes_are_people_found_by_their_mobile_ids(self):
        catalog = Catalog(self.library, {})
        self.assertNotIn("mobile-128", catalog.subjects)
        self.assertEqual((catalog.get("mobile-132")["id"], catalog.get("mobile-128-female")["id"]), ("npc-468", "npc-467"))
        self.assertEqual(catalog.get("mobile-4")["id"], "creature-004")
        self.assertEqual(catalog.get("creature-004")["category"], "creatures")

    def test_textures_animations_and_audio_list_what_the_game_draws_and_plays(self):
        self.assertEqual(self.ids("textures"), ["texture-002-1"])
        animations = {row["id"]: row["kind"] for row in self.overview()["animations"]}
        self.assertEqual(animations, {"anim-walk": "walk", "anim-gentle": "walk", "anim-idle": "idle", "anim-idle-hips": "idle",
                                      "anim-rig-attention": "gesture", "anim-rig-idle": "idle", "anim-rig-walk": "walk"})
        audio = {row["id"]: row["kind"] for row in self.overview()["audio"]}
        self.assertEqual(audio, {"sound-437": "ambient", "sound-106": "effect", "sound-165": "", "music-dungeon-01": "music",
                                 "music-town-01": "music"})

    def test_model_bands_sort_every_remodelled_model(self):
        bands = read_json(MODEL_CATEGORIES, {})["bands"]
        self.assertEqual([model_category(id, bands) for id in (444, 750, 41003, 41320, 41705, 41830, 60513, 62323, 74226, 9999)],
                         [("architecture", "battlement"), ("nature", "bush"), ("props", "furniture"), ("props", "dungeon"),
                          ("nature", ""), ("props", "container"), ("nature", "rock"), ("props", "statue"),
                          ("props", "dungeon"), ("architecture", "")])
        self.assertTrue(all(band["category"] in ("nature", "props", "architecture") for band in bands))

    def test_links_variants_without_confusing_the_female_set(self):
        catalog = Catalog(self.library, {})
        self.assertEqual(catalog.get("npc-381")["pipelineIds"], ["381-A"])
        self.assertEqual(catalog.get("npc-468")["pipelineIds"], ["128"])
        self.assertEqual(catalog.get("npc-467")["pipelineIds"], [])

    def test_files_on_disk_are_not_a_pipeline_approval(self):
        catalog = Catalog(self.library, {})
        self.assertIsNone(catalog.get("npc-468")["concept"])
        self.assertFalse(catalog.get("npc-468")["conceptChoices"][0]["approved"])
        self.assertTrue(catalog.get("npc-381")["concept"]["approved"])

    def test_concept_stage_keeps_the_classic_reference_after_approval(self):
        manifest = self.library.npcs / "manifest.json"
        chart = json.loads(manifest.read_text())
        chart["entries"][0]["image"] = "villager.png"
        self.write(manifest, chart)
        image = self.library.npcs / "art/villager.png"
        image.parent.mkdir()
        image.touch()
        self.workspace.concept_store = ConceptStore(self.root / "concepts")
        candidate = self.workspace.concept_action("npc-381", "candidates", {
            "name": "Front", "batch": "Outfit", "filename": "front.png",
            "image": "iVBORw0KGgo=", "details": {"prompt": "A tunic", "seed": 1},
        })

        stage = self.workspace.concept_stage("381-A")
        approved = self.workspace.concept_action("npc-381", "approve", {"candidate": candidate["id"]})
        for response in (stage, approved):
            self.assertEqual(response["classic"], "/media/npc/art/villager.png")
            self.assertEqual(response["classicUrl"], "/media/npc/npc-381/index.html")

    def test_saves_explicit_choice_from_an_attached_pipeline(self):
        choice = {"pipeline": "381-A", "path": "01-concept/approved/front.png"}
        with self.assertRaisesRegex(ValueError, "linked pipeline"):
            self.workspace.update_links("creature-004", {"concept": choice})
        self.workspace.update_links("mobile-4", {"addPipeline": "381-A", "concept": choice})
        self.assertEqual(self.workspace.catalog().get("creature-004")["explicitConcept"], choice)
        self.assertIn("creature-004", json.loads(self.path.read_text()))
        self.workspace.update_links("creature-004", {"removePipeline": "381-A"})
        self.assertIsNone(self.workspace.catalog().get("creature-004")["explicitConcept"])

    def test_merged_choices_require_review_when_they_disagree(self):
        path = "01-concept/approved/front.png"
        self.write(self.path, {
            "npc-382": {"pipelines": ["128"], "concept": {"pipeline": "128", "path": path}},
            "npc-383": {"pipelines": ["381-A"], "concept": {"pipeline": "381-A", "path": path}},
        })
        merged = self.workspace.catalog().get("npc-382")
        self.assertEqual(merged["conflicts"], ["concept"])
        self.assertIsNone(merged["concept"])
        result = self.workspace.update_links("npc-381", {"concept": {"pipeline": "128", "path": path}})
        self.assertEqual(result["concept"]["pipeline"], "128")
        self.assertEqual(result["conflicts"], [])
