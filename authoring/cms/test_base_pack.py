"""Tests for split daggerfall.base manifest loading and per-section cache invalidation."""

import json
import os
from pathlib import Path
import tempfile
import unittest

from base_pack import read_section, section_path, sections_stamp
from cache import FileCache
from library import BASE_PAYLOAD


class BasePackTests(unittest.TestCase):
    def test_real_repo_base_pack_is_split_manifest_and_reads_sections(self):
        self.assertTrue(BASE_PAYLOAD.is_file())
        self.assertLess(BASE_PAYLOAD.stat().st_size, 5000)
        cache = FileCache()
        factions = read_section(BASE_PAYLOAD, "factions", {}, cache)
        self.assertGreater(len(factions.get("factions", [])), 300)
        creature_specs = read_section(BASE_PAYLOAD, "actorSpecsCreatures", {}, cache)
        self.assertEqual(len(creature_specs.get("specs", [])), 43)
        enemy_specs = read_section(BASE_PAYLOAD, "actorSpecsEnemies", {}, cache)
        self.assertEqual(len(enemy_specs.get("specs", [])), 20)
        building_names = read_section(BASE_PAYLOAD, "buildingNames", {}, cache)
        self.assertEqual(len(building_names.get("regionNames", [])), 62)
        vocab = read_section(BASE_PAYLOAD, "vocabulary", {}, cache)
        self.assertIn("strength", vocab.get("attributes", []))
        path, is_split = section_path(BASE_PAYLOAD, "actorSpecsCreatures", cache)
        self.assertTrue(is_split)
        self.assertEqual(path.name, "creatures.json")

    def test_per_section_stamp_ignores_unrelated_section_edits(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "world").mkdir()
            (root / "text").mkdir()
            manifest = root / "payload.json"
            factions_file = root / "world/factions.json"
            text_file = root / "text/text.json"
            manifest.write_text(json.dumps({
                "ruleset": "daggerfall",
                "sections": {"factions": "world/factions.json", "text": "text/text.json"},
            }), encoding="utf-8")
            factions_file.write_text(json.dumps({"factions": [{"id": 1, "name": "One"}]}), encoding="utf-8")
            text_file.write_text(json.dumps({"records": []}), encoding="utf-8")

            cache = FileCache()
            stamp_before = sections_stamp(manifest, ("factions",), cache)

            # Mutating unrelated text section must not change the factions stamp
            text_file.write_text(json.dumps({"records": [{"k": "v"}]}), encoding="utf-8")
            os.utime(text_file, (text_file.stat().st_atime + 10, text_file.stat().st_mtime + 10))
            self.assertEqual(stamp_before, sections_stamp(manifest, ("factions",), cache))

            # Mutating factions file changes the stamp and returns updated data
            factions_file.write_text(json.dumps({"factions": [{"id": 2, "name": "Two"}]}), encoding="utf-8")
            os.utime(factions_file, (factions_file.stat().st_atime + 10, factions_file.stat().st_mtime + 10))
            self.assertNotEqual(stamp_before, sections_stamp(manifest, ("factions",), cache))
            self.assertEqual(read_section(manifest, "factions", {}, cache)["factions"][0]["id"], 2)

    def test_inline_legacy_payload_still_reads(self):
        with tempfile.TemporaryDirectory() as tmp:
            manifest = Path(tmp) / "payload.json"
            manifest.write_text(json.dumps({
                "ruleset": "daggerfall",
                "mobiles": {"mobiles": [{"donorId": 0, "identity": "rat"}]},
            }), encoding="utf-8")
            cache = FileCache()
            self.assertEqual(read_section(manifest, "mobiles", {}, cache)["mobiles"][0]["identity"], "rat")
            path, is_split = section_path(manifest, "mobiles", cache)
            self.assertFalse(is_split)
            self.assertEqual(path, manifest)


if __name__ == "__main__":
    unittest.main()
