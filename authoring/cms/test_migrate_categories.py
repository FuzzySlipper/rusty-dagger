import json
import sqlite3
from contextlib import closing
from unittest import TestCase

import migrate_categories
from catalog import Catalog
from concepts import ConceptStore
from reviews import ReviewStore
import test_catalog


class MigrationTests(TestCase):
    def setUp(self):
        fixture = test_catalog.CatalogTests("test_links_variants_without_confusing_the_female_set")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.root, self.library = fixture.root, fixture.library
        self.catalog = Catalog(self.library, {})
        self.reviews = self.root / "reviews.sqlite3"
        ReviewStore(self.reviews)

    def insert(self, *rows):
        with closing(sqlite3.connect(self.reviews)) as db, db:
            db.executemany("INSERT INTO reviews(category, id, marked, rating, note) VALUES (?, ?, ?, ?, ?)", rows)

    def rows(self):
        with closing(sqlite3.connect(self.reviews)) as db:
            return sorted(db.execute("SELECT category, id, marked, rating, note FROM reviews"))

    def test_reviews_move_to_their_records_categories_with_every_note_kept(self):
        self.insert(("npcs", "npc-381", 1, "good", "Tunic"), ("flora", "500-12", 0, "bad", "Leaf density"),
                    ("mobiles", "mobile-4", 0, None, "Bear"), ("mobiles", "mobile-128", 1, "maybe", "Robe"),
                    ("mobiles", "mobile-132", 0, None, "Healer robe"), ("pipeline-stages", "381/01-concept", 0, "good", ""))
        migrate_categories.migrate_reviews(self.catalog, self.reviews)
        # Mage and Healer draw one sprite set: the Healer's review keeps its old id, which the record reads as its own.
        self.assertEqual(self.rows(), [
            ("creatures", "creature-004", 0, None, "Bear"), ("nature", "nature-500-12", 0, "bad", "Leaf density"),
            ("people", "mobile-132", 0, None, "Healer robe"), ("people", "npc-381", 1, "good", "Tunic"),
            ("people", "npc-468", 1, "maybe", "Robe"), ("pipeline-stages", "381/01-concept", 0, "good", "")])
        self.assertEqual(self.catalog.resolve("mobile-132"), "npc-468")

    def test_a_review_without_a_new_home_stops_the_move_before_anything_changes(self):
        self.insert(("npcs", "npc-381", 0, None, "Tunic"), ("mobiles", "mobile-999", 0, None, "Unknown"))
        before = self.rows()
        with self.assertRaisesRegex(SystemExit, "mobiles/mobile-999"):
            migrate_categories.migrate_reviews(self.catalog, self.reviews)
        self.assertEqual(self.rows(), before)

    def test_concept_art_and_links_filed_under_a_mobile_move_to_its_record(self):
        store = ConceptStore(self.root / "concepts")
        candidate = store.submit("mobile-128", {"name": "Front", "batch": "Robe", "filename": "front.png",
                                                "image": "iVBORw0KGgo=", "details": {"prompt": "A robe", "seed": 1}})
        store.approve("mobile-128", (), candidate["id"])
        store.submit("npc-381", {"name": "Front", "batch": "Tunic", "filename": "front.png", "image": "iVBORw0KGgo=", "details": {"prompt": "A robe", "seed": 1}})
        links = self.root / "links.json"
        links.write_text(json.dumps({"mobile-4": {"pipelines": ["128"]}, "npc-382": {"pipelines": ["381-A"]}}))

        self.assertEqual(migrate_categories.migrate_concepts(self.catalog, store.database), {"mobile-128": "npc-468"})
        self.assertEqual(migrate_categories.migrate_links(self.catalog, links), {"mobile-4": "creature-004"})
        self.assertEqual(sorted(store.subjects()), ["npc-381", "npc-468"])
        self.assertEqual(store.stage("npc-468")["approved"]["id"], candidate["id"])
        self.assertEqual(json.loads(links.read_text()), {"creature-004": {"pipelines": ["128"]}, "npc-382": {"pipelines": ["381-A"]}})
