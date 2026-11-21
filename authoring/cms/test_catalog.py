import json
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from catalog import Catalog
from library import Library
from server import Workspace


class FixtureLibrary(Library):
    @property
    def mobiles(self):
        return self.npcs.parent / "mobiles"

    @property
    def flora(self):
        return self.npcs.parent / "flora"


class CatalogTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.library = FixtureLibrary(self.root / "npc", self.root / "pipeline")
        self.path = self.root / "links.json"
        self.workspace = Workspace(self.library, self.path, self.root / "merges.json")
        self.write(self.library.npcs / "manifest.json", {
            "entries": [{"id": "npc-381", "name": "Villager", "kind": "Wandering townsfolk"}],
            "aliases": {"381-A": "npc-381", "npc-382": "npc-381", "npc-383": "npc-381"},
        })
        self.write(self.library.mobiles / "manifest.json", {"mobiles": [
            {"directory": "mobile-128", "name": "Mage", "sourceArchive": 486, "mobileId": 128, "variant": "base"},
            {"directory": "mobile-128-female", "name": "Mage", "sourceArchive": 485, "mobileId": 128, "variant": "female"},
        ]})
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

    def test_links_variants_without_confusing_the_female_mobile(self):
        catalog = Catalog(self.library, {})
        self.assertEqual(catalog.get("npc-381")["pipelineIds"], ["381-A"])
        self.assertEqual(catalog.get("mobile-128")["pipelineIds"], ["128"])
        self.assertEqual(catalog.get("mobile-128-female")["pipelineIds"], [])

    def test_files_on_disk_are_not_a_pipeline_approval(self):
        catalog = Catalog(self.library, {})
        self.assertIsNone(catalog.get("mobile-128")["concept"])
        self.assertFalse(catalog.get("mobile-128")["conceptChoices"][0]["approved"])
        self.assertTrue(catalog.get("npc-381")["concept"]["approved"])

    def test_saves_explicit_choice_from_an_attached_pipeline(self):
        choice = {"pipeline": "381-A", "path": "01-concept/approved/front.png"}
        with self.assertRaisesRegex(ValueError, "linked pipeline"):
            self.workspace.update_links("mobile-128", {"concept": choice})
        self.workspace.update_links("mobile-128", {"addPipeline": "381-A", "concept": choice})
        self.assertEqual(self.workspace.catalog().get("mobile-128")["explicitConcept"], choice)
        self.workspace.update_links("mobile-128", {"removePipeline": "381-A"})
        self.assertIsNone(self.workspace.catalog().get("mobile-128")["explicitConcept"])

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
