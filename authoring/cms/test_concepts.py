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
