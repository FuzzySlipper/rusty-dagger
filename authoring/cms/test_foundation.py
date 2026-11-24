"""Unit tests for the CMS backend foundation: Library, FileCache, PackStore, Router, and HTTP wiring."""

from http import HTTPStatus
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Thread
from unittest import TestCase
from urllib.error import HTTPError
from urllib.request import Request as UrlRequest, urlopen

from cache import FileCache, dir_stamp, file_stamp
from concepts import ConceptStore
from library import Library
from packs import PackStore, ValidationError, ValidationIssue, atomic_write_json, format_json
from reviews import ReviewStore
from router import ConflictError, NotFoundError, Router
from server import Workspace, make_server


class LibraryTests(TestCase):
    def test_repo_rooted_library_derives_every_pack_and_reference_path_from_repo(self):
        with TemporaryDirectory() as tmp:
            repo = Path(tmp)
            library = Library(repo / "local/art/reference/npc", repo / "pipeline", repo=repo)
            self.assertEqual(library.base_payload, repo / "content/worldrpg/packs/daggerfall.base/payload.json")
            self.assertEqual(library.people_pack, repo / "content/worldrpg/packs/daggerfall.people")
            self.assertEqual(library.art_payload, repo / "content/worldrpg/packs/daggerfall.art-replacements/payload.json")
            self.assertEqual(library.mobiles, repo / "local/art/reference/daggerfall-mobile-idle-unmirrored-r01")
            self.assertEqual(library.flora, repo / "local/art/review/flora")
            self.assertEqual(library.scenery, repo / "local/art/reference/scenery")
            self.assertEqual(library.surfaces, repo / "local/art/reference/surfaces")
            self.assertEqual(library.model_notes, repo / "authoring/ledger/models-notes.json")
            self.assertEqual(library.music_sources, repo / "authoring/tools/music/sources.tsv")
            self.assertEqual(library.classic_media,
                             repo / "local/build/import/staging/worldrpg/imports/shared/media/classic/manifest.json")
            self.assertEqual(library.npc_chart_script,
                             repo / "src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceChart.js")
            self.assertEqual(library.merge_script, repo / "scripts/apply-npc-merges.py")


class CacheTests(TestCase):
    def test_file_cache_reuses_unchanged_entry_and_reloads_when_file_changes(self):
        with TemporaryDirectory() as tmp:
            cache = FileCache()
            target = Path(tmp) / "sample.json"
            target.write_text('{"value": 1}\n', encoding="utf-8")

            loads = 0

            def load():
                nonlocal loads
                loads += 1
                return json.loads(target.read_text(encoding="utf-8"))

            first = cache.get("sample", file_stamp(target), load)
            second = cache.get("sample", file_stamp(target), load)
            self.assertIs(first, second)
            self.assertEqual(loads, 1)

            # Change file size + mtime to guarantee a different stamp even on coarse filesystems.
            target.write_text('{"value": 200}\n', encoding="utf-8")
            stat = target.stat()
            os.utime(target, ns=(stat.st_atime_ns, stat.st_mtime_ns + 1_000_000))
            third = cache.get("sample", file_stamp(target), load)
            self.assertEqual(third, {"value": 200})
            self.assertEqual(loads, 2)

    def test_json_csv_and_dir_stamp_helpers(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            cache = FileCache()
            self.assertEqual(cache.json(root / "missing.json", {"ok": True}), {"ok": True})
            self.assertEqual(cache.csv(root / "missing.csv"), ())

            csv_file = root / "rows.csv"
            csv_file.write_text("id,name\n1,Alpha\n", encoding="utf-8")
            self.assertEqual(cache.csv(csv_file), ({"id": "1", "name": "Alpha"},))

            (root / "a").mkdir()
            (root / "a/character.json").write_text('{"id":"a"}', encoding="utf-8")
            stamp_a = dir_stamp(root, "*/character.json")
            self.assertEqual(len(stamp_a), 1)
            (root / "b").mkdir()
            (root / "b/character.json").write_text('{"id":"b"}', encoding="utf-8")
            stamp_b = dir_stamp(root, "*/character.json")
            self.assertNotEqual(stamp_a, stamp_b)


class PackStoreTests(TestCase):
    def test_atomic_write_json_is_deterministic_utf8_and_preserves_key_order(self):
        with TemporaryDirectory() as tmp:
            target = Path(tmp) / "cast/individuals.json"
            payload = {"id": "lord-k-avar", "name": "Lord K'avar", "gender": "Male"}
            atomic_write_json(target, payload)
            raw = target.read_text(encoding="utf-8")
            self.assertEqual(raw, format_json(payload))
            self.assertIn("Lord K'avar", raw)
            self.assertTrue(raw.endswith("\n"))
            self.assertLess(raw.index('"id"'), raw.index('"name"'))
            self.assertLess(raw.index('"name"'), raw.index('"gender"'))
            # No leftover temp files beside target.
            self.assertEqual(list(target.parent.iterdir()), [target])

    def test_pack_store_rejects_invalid_update_without_touching_disk(self):
        with TemporaryDirectory() as tmp:
            target = Path(tmp) / "spawners.json"
            original = {"spawners": [{"id": "banker", "weight": 10}]}
            atomic_write_json(target, original)
            before = target.read_text(encoding="utf-8")
            store = PackStore()

            def validate(doc):
                if doc["spawners"][0]["weight"] <= 0:
                    raise ValidationError("Invalid spawner pool.", [ValidationIssue("spawners[0].weight", "Must be positive.")])

            with self.assertRaises(ValidationError) as ctx:
                store.update_json(
                    target,
                    lambda doc: {"spawners": [{"id": "banker", "weight": 0}]},
                    validator=validate,
                )
            self.assertEqual(ctx.exception.issues, [{"field": "spawners[0].weight", "message": "Must be positive."}])
            self.assertEqual(target.read_text(encoding="utf-8"), before)

            # Valid update writes atomically and invalidates cache.
            self.assertEqual(store.read_json(target), original)
            result = store.update_json(
                target,
                lambda doc: ({"spawners": [{"id": "banker", "weight": 25}]}, {"saved": 25}),
                validator=validate,
            )
            self.assertEqual(result, {"saved": 25})
            self.assertEqual(store.read_json(target)["spawners"][0]["weight"], 25)


class RouterTests(TestCase):
    def test_supports_all_methods_named_params_path_params_and_errors(self):
        router = Router()
        router.get("/api/items/{id}", lambda req: {"id": req.params["id"], "q": req.arg("q")})
        router.put("/api/items/{id}", lambda req: {"updated": req.params["id"], "body": req.json()})
        router.delete("/api/items/{id}", lambda req: {"deleted": req.params["id"]})
        router.get("/api/files/{category}/{rest:path}", lambda req: req.params)

        def fail_validation(_req):
            raise ValidationError("Bad payload.", [ValidationIssue("weight", "Must be > 0.")])

        router.post("/api/validate", fail_validation)
        router.post("/api/conflict", lambda _req: (_ for _ in ()).throw(ConflictError("Already exists.")))
        router.get("/api/missing", lambda _req: (_ for _ in ()).throw(NotFoundError("Gone.")))

        res = router.dispatch("GET", "/api/items/sword%201?q=steel")
        self.assertEqual(res.status, HTTPStatus.OK)
        self.assertEqual(res.data, {"id": "sword 1", "q": "steel"})

        res = router.dispatch("PUT", "/api/items/sword-1", b'{"damage": 12}')
        self.assertEqual(res.status, HTTPStatus.OK)
        self.assertEqual(res.data, {"updated": "sword-1", "body": {"damage": 12}})

        res = router.dispatch("DELETE", "/api/items/sword-1")
        self.assertEqual(res.status, HTTPStatus.OK)
        self.assertEqual(res.data, {"deleted": "sword-1"})

        # Both raw slashes and URL-encoded slashes work for :path parameters.
        self.assertEqual(
            router.dispatch("GET", "/api/files/pipeline-assets/452/01-concept/front.png").data,
            {"category": "pipeline-assets", "rest": "452/01-concept/front.png"},
        )
        self.assertEqual(
            router.dispatch("GET", "/api/files/pipeline-assets/452%2F01-concept%2Ffront.png").data,
            {"category": "pipeline-assets", "rest": "452/01-concept/front.png"},
        )

        # Structured validation, conflict, not-found, 405, and static fallback behavior.
        val = router.dispatch("POST", "/api/validate", b"{}")
        self.assertEqual(val.status, HTTPStatus.BAD_REQUEST)
        self.assertEqual(val.data, {"error": "Bad payload.", "issues": [{"field": "weight", "message": "Must be > 0."}]})

        conflict = router.dispatch("POST", "/api/conflict", b"{}")
        self.assertEqual(conflict.status, HTTPStatus.CONFLICT)

        missing = router.dispatch("GET", "/api/missing")
        self.assertEqual(missing.status, HTTPStatus.NOT_FOUND)

        method_not_allowed = router.dispatch("POST", "/api/items/sword-1", b"{}")
        self.assertEqual(method_not_allowed.status, HTTPStatus.METHOD_NOT_ALLOWED)
        self.assertIn("GET", method_not_allowed.headers["Allow"])
        self.assertIn("PUT", method_not_allowed.headers["Allow"])

        bad_json = router.dispatch("PUT", "/api/items/sword-1", b"{broken")
        self.assertEqual(bad_json.status, HTTPStatus.BAD_REQUEST)

        self.assertEqual(router.dispatch("GET", "/api/unknown").status, HTTPStatus.NOT_FOUND)
        self.assertIsNone(router.dispatch("GET", "/index.html"))


class ServerIntegrationTests(TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.library = Library(self.root / "local/art/reference/npc", self.root / "pipeline", repo=self.root)
        self._write(self.library.npcs / "manifest.json", {
            "entries": [{"id": "npc-381", "name": "Villager", "kind": "Wandering townsfolk"}],
            "aliases": {"381-A": "npc-381"},
        })
        self._write(self.library.characters / "381-A/character.json", {
            "id": "381-A", "name": "Villager A", "stages": {"01-concept": {"state": "approved"}},
        })
        self.workspace = Workspace(
            self.library,
            self.root / "links.json",
            self.root / "merges.json",
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

    def _write(self, path: Path, data):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data), encoding="utf-8")

    def _request(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body).encode("utf-8")
        headers = {"Content-Type": "application/json"} if body is not None else {}
        req = UrlRequest(self.origin + path, data=data, headers=headers, method=method)
        try:
            with urlopen(req, timeout=5) as resp:
                return resp.status, json.loads(resp.read().decode("utf-8"))
        except HTTPError as error:
            try:
                return error.code, json.loads(error.read().decode("utf-8"))
            finally:
                error.close()

    def test_existing_endpoints_and_catalog_caching_round_trip(self):
        status, health = self._request("GET", "/health")
        self.assertEqual((status, health), (200, {"service": "Rusty Dagger Authoring"}))

        status, catalog_a = self._request("GET", "/api/catalog")
        self.assertEqual(status, 200)
        self.assertEqual([row["id"] for row in catalog_a["people"]], ["npc-381"])

        # Second call hits cached Catalog instance while files are unchanged.
        first_catalog = self.workspace.catalog()
        self.assertIs(first_catalog, self.workspace.catalog())

        # Character detail + 404.
        status, character = self._request("GET", "/api/characters/npc-381")
        self.assertEqual(status, 200)
        self.assertEqual(character["id"], "npc-381")
        self.assertEqual(self._request("GET", "/api/characters/npc-missing")[0], 404)

        # Reviews with slash in ID + empty-body POST clear-marks.
        status, saved = self._request("POST", "/api/reviews/pipeline-assets/381-A/01-concept/front.png",
                                      {"marked": True, "note": "Check silhouette"})
        self.assertEqual(status, 200)
        self.assertTrue(saved["marked"])

        status, fetched = self._request("GET", "/api/reviews/pipeline-assets/381-A%2F01-concept%2Ffront.png")
        self.assertEqual(status, 200)
        self.assertEqual(fetched["note"], "Check silhouette")

        status, cleared = self._request("POST", "/api/marks/pipeline-assets/clear")
        self.assertEqual((status, cleared), (200, {"cleared": 1}))

        # Stage review and pipeline lookup.
        status, stage = self._request("GET", "/api/stage-review/381-A/01-concept")
        self.assertEqual(status, 200)
        self.assertEqual(stage["pipeline"], "381-A")

        status, pipeline = self._request("GET", "/api/pipelines/381-A")
        self.assertEqual(status, 200)
        self.assertEqual(pipeline["id"], "381-A")

        # External manifest edit invalidates CatalogStore cache automatically.
        manifest = self.library.npcs / "manifest.json"
        self._write(manifest, {
            "entries": [
                {"id": "npc-381", "name": "Villager", "kind": "Wandering townsfolk"},
                {"id": "npc-382", "name": "Merchant", "kind": "Townsperson"},
            ],
            "aliases": {"381-A": "npc-381"},
        })
        stat = manifest.stat()
        os.utime(manifest, ns=(stat.st_atime_ns, stat.st_mtime_ns + 1_000_000))
        self.assertIsNot(first_catalog, self.workspace.catalog())
        status, catalog_b = self._request("GET", "/api/catalog")
        self.assertEqual([row["id"] for row in catalog_b["people"]], ["npc-381", "npc-382"])
