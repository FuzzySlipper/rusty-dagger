from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from reviews import ReviewStore, Reviews


class ReviewTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.file = Path(directory.name) / "reviews.sqlite3"
        self.store = ReviewStore(self.file)
        self.revision = "1"
        self.aliases = {}
        self.reviews = Reviews(self.store, self.aliases,
                               lambda category, key: self.revision if category == "pipeline-assets" else None)

    def test_keys_distinguish_categories_and_preserve_unset_fields(self):
        self.reviews.patch("flora", "500-12", {"note": "Leaf density", "rating": "bad"})
        self.reviews.patch("flora", "500-12", {"marked": True})
        self.reviews.patch("npcs", "500-12", {"note": "Other record", "marked": True})
        self.assertEqual(self.store.clear_marks("flora"), 1)
        flora = self.reviews.item("flora", "500-12")
        self.assertEqual((flora["note"], flora["rating"], flora["marked"]), ("Leaf density", "bad", False))
        self.assertTrue(self.reviews.item("npcs", "500-12")["marked"])

    def test_marks_notes_and_ratings_survive_reopen(self):
        self.reviews.patch("mobiles", "mobile-128", {"marked": True, "rating": "good", "note": "Approved robe"})
        store = ReviewStore(self.file)
        self.assertEqual(store.rows("mobiles")[0]["note"], "Approved robe")
        self.assertEqual(store.rows("mobiles")[0]["rating"], "good")

    def test_changed_media_needs_review_but_keeps_notes_and_marks(self):
        key = "381/01-concept/front.png"
        self.reviews.patch("pipeline-assets", key, {"marked": True, "rating": "good", "note": "Looks good"})
        self.revision = "2"
        row = self.reviews.item("pipeline-assets", key)
        self.assertIsNone(row["rating"])
        self.assertEqual(row["previousRating"], "good")
        self.assertEqual(row["note"], "Looks good")
        self.assertTrue(row["marked"])

    def test_replacing_media_before_a_reviewer_clicks_cannot_accept_the_new_file(self):
        key = "381/01-concept/front.png"
        opened = self.revision
        self.revision = "2"
        with self.assertRaisesRegex(ValueError, "review target changed"):
            self.reviews.patch("pipeline-assets", key, {"rating": "good", "expectedRevision": opened})
        self.assertIsNone(self.reviews.item("pipeline-assets", key)["rating"])

    def test_npc_merge_keeps_old_notes_and_includes_the_keeper_in_marked_records(self):
        self.reviews.patch("npcs", "npc-332", {"marked": True, "note": "Keeper should retain this"})
        self.aliases["npc-332"] = "npc-193"
        row, = self.reviews.list("npcs")
        self.assertEqual(row["id"], "npc-193")
        self.assertEqual(row["related"][0]["note"], "Keeper should retain this")
        self.assertTrue(row["marked"])
        self.reviews.patch("npcs", "npc-193", {"marked": False})
        self.assertFalse(self.reviews.item("npcs", "npc-332")["marked"])
        self.assertTrue(self.reviews.item("npcs", "npc-193")["related"])
