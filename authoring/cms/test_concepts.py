import base64
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from concepts import ConceptStore


class ConceptTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.store = ConceptStore(self.root)

    def submit(self, name, subject="npc-381", seed=18446744073709551615):
        return self.store.submit(subject, {
            "name": name, "batch": "Outfit study", "filename": "sample.png",
            "image": base64.b64encode(b"\x89PNG\r\n\x1a\n" + name.encode()).decode(),
            "details": {"prompt": "A teal robe.\nDark leather boots.", "seed": seed,
                        "model": "Local model", "settings": {"steps": 25}},
        })

    def test_approval_replaces_the_pointer_and_keeps_both_originals(self):
        first = self.submit("Front A")
        second = self.submit("Front B", seed=4)
        self.store.approve("npc-381", (), first["id"])
        self.store.approve("npc-381", (), second["id"])
        reopened = ConceptStore(self.root).stage("npc-381")
        self.assertEqual(reopened["approved"]["id"], second["id"])
        self.assertEqual(reopened["approved"]["details"]["seed"], "4")
        self.assertEqual(reopened["candidates"][0]["details"]["seed"], "18446744073709551615")
        self.assertEqual(reopened["approved"]["details"]["settings"], {"steps": 25})
        self.assertEqual(len(reopened["candidates"]), 2)
        self.assertEqual(len(list(self.store.images.iterdir())), 2)

    def test_legacy_approvals_migrate_to_front_with_original_date_and_details(self):
        front = self.submit("Front A")
        date = "2026-11-21T10:20:00+00:00"
        with self.store.connection() as db:
            db.execute("DROP TABLE approvals")
            db.execute("""
                CREATE TABLE approvals (
                    subject TEXT PRIMARY KEY, candidate TEXT NOT NULL REFERENCES candidates(id),
                    approved_at TEXT NOT NULL
                )
                """)
            db.execute("INSERT INTO approvals VALUES (?, ?, ?)", ("npc-381", front["id"], date))
        stage = ConceptStore(self.root).stage("npc-381")
        self.assertEqual(stage["approvals"]["front"], front | {"approvedAt": date})
        self.assertEqual(stage["approved"], stage["approvals"]["front"])
        self.assertIsNone(stage["approvals"]["back"])
        self.assertEqual(ConceptStore(self.root).stage("npc-381"), stage)

    def test_replacing_back_keeps_front_and_preserves_both_views_metadata(self):
        front = self.submit("Front A")
        first_back = self.submit("Back A", seed=2)
        second_back = self.submit("Back B", seed=3)
        self.store.approve("npc-381", (), front["id"])
        self.store.approve("npc-381", (), first_back["id"], "back")
        self.store.approve("npc-381", (), second_back["id"], "back")
        stage = ConceptStore(self.root).stage("npc-381")
        self.assertEqual(stage["approved"]["id"], front["id"])
        self.assertEqual(stage["approvals"]["front"]["details"], front["details"])
        self.assertEqual(stage["approvals"]["back"]["id"], second_back["id"])
        self.assertEqual(stage["approvals"]["back"]["details"], second_back["details"])
        self.assertEqual(len(stage["candidates"]), 3)

    def test_resolving_a_merged_front_conflict_keeps_the_inherited_back(self):
        first = self.submit("Front A", subject="npc-382")
        second = self.submit("Front B", subject="npc-383")
        back = self.submit("Back A", subject="npc-382")
        self.store.approve("npc-382", (), first["id"])
        self.store.approve("npc-383", (), second["id"])
        self.store.approve("npc-382", (), back["id"], "back")
        aliases = ("npc-382", "npc-383")
        stage = self.store.stage("npc-381", aliases)
        self.assertEqual(stage["conflicts"], ["front"])
        self.assertEqual(stage["approvals"]["back"]["id"], back["id"])
        resolved = self.store.approve("npc-381", aliases, first["id"])
        self.assertEqual(resolved["conflicts"], [])
        self.assertEqual(resolved["approved"]["id"], first["id"])
        self.assertEqual(resolved["approvals"]["back"]["id"], back["id"])

    def test_retry_keeps_original_and_cannot_change_approved_provenance(self):
        row = self.submit("Front A")
        self.store.approve("npc-381", (), row["id"])
        self.assertEqual(self.submit("Front A")["id"], row["id"])
        with self.assertRaisesRegex(ValueError, "different art or generation details"):
            self.submit("Front A", seed=8)
        self.assertEqual(self.store.stage("npc-381")["approved"]["id"], row["id"])

    def test_approval_cannot_choose_a_different_characters_candidate(self):
        row = self.submit("Front A", subject="npc-382")
        with self.assertRaisesRegex(ValueError, "from this character"):
            self.store.approve("npc-381", (), row["id"])

    def test_merged_npcs_with_two_selections_require_a_new_choice(self):
        first = self.submit("Front A", subject="npc-382")
        second = self.submit("Front B", subject="npc-383")
        self.store.approve("npc-382", (), first["id"])
        self.store.approve("npc-383", (), second["id"])
        stage = self.store.stage("npc-381", ("npc-382", "npc-383"))
        self.assertIsNone(stage["approved"])
        self.assertTrue(stage["conflict"])
        self.store.approve("npc-381", ("npc-382", "npc-383"), first["id"])
        self.assertEqual(self.store.stage("npc-381", ("npc-382", "npc-383"))["approved"]["id"], first["id"])

    def test_two_view_approvals_table_migrates_to_four_views(self):
        front = self.submit("Front A")
        back = self.submit("Back A", seed=2)
        left = self.submit("Left A", seed=3)
        right = self.submit("Right A", seed=4)
        date = "2026-11-24T10:20:00+00:00"
        with self.store.connection() as db:
            db.execute("DROP TABLE approvals")
            db.execute("""
                CREATE TABLE approvals (
                    subject TEXT NOT NULL, view TEXT NOT NULL CHECK(view IN ('front', 'back')),
                    candidate TEXT NOT NULL REFERENCES candidates(id), approved_at TEXT NOT NULL,
                    PRIMARY KEY (subject, view)
                )
                """)
            db.execute("INSERT INTO approvals VALUES (?, ?, ?, ?)", ("npc-381", "front", front["id"], date))
            db.execute("INSERT INTO approvals VALUES (?, ?, ?, ?)", ("npc-381", "back", back["id"], date))

        reopened = ConceptStore(self.root)
        reopened.approve("npc-381", (), left["id"], "left")
        reopened.approve("npc-381", (), right["id"], "right")
        stage = reopened.stage("npc-381")
        self.assertEqual(stage["approvals"]["front"]["id"], front["id"])
        self.assertEqual(stage["approvals"]["left"]["id"], left["id"])
        self.assertEqual(stage["approvals"]["right"]["id"], right["id"])
        self.assertEqual(stage["approvals"]["back"]["id"], back["id"])

    def test_submit_with_approve_marks_views_atomically_and_rejects_invalid_view(self):
        created = self.store.submit("npc-381", {
            "name": "Left profile",
            "batch": "Turnaround",
            "filename": "left.png",
            "image": base64.b64encode(b"\x89PNG\r\n\x1a\nleft").decode(),
            "details": {"prompt": "Left side profile.", "seed": 77},
            "approve": "left",
        })
        self.assertEqual(created["approvedViews"], ["left"])
        stage = self.store.stage("npc-381")
        self.assertEqual(stage["approvals"]["left"]["id"], created["id"])
        self.assertIsNone(stage["approvals"]["front"])

        with self.assertRaisesRegex(ValueError, "front, left, right, back"):
            self.store.submit("npc-381", {
                "name": "Bad view",
                "batch": "Turnaround",
                "filename": "bad.png",
                "image": base64.b64encode(b"\x89PNG\r\n\x1a\nbad").decode(),
                "details": {"prompt": "Invalid view test.", "seed": 88},
                "approve": "top",
            })
        # Failed validation must not persist candidate or image
        self.assertEqual(len(self.store.stage("npc-381")["candidates"]), 1)
        self.assertEqual(len(list(self.store.images.iterdir())), 1)
