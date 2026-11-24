#!/usr/bin/env python3
"""Serve the Rusty Dagger authoring workspace on the LAN."""

import argparse
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import sqlite3
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory
from urllib.parse import unquote, urlsplit

from cache import FileCache
import catalog as catalog_module
from catalog import CatalogStore
import concepts as concepts_module
from concepts import MEDIA, ConceptStore
from library import (
    CMS, DEFAULT_CHARACTERS, DEFAULT_CONCEPTS, DEFAULT_MERGES, DEFAULT_NPCS, DEFAULT_REVIEWS, REPO, Library,
)
from packs import PackStore
import people_curation as people_curation_module
import people_pack as people_pack_module
from people_pack import PeoplePackStore
from pipelines import STAGES, stage_row
from review_media import revision, stage_assets, stage_info
import reviews as reviews_module
from reviews import ReviewStore, Reviews
from router import Response, Router

WEB = CMS / "web"


class Workspace:
    """Composition root connecting the local library, caches, pack I/O, and domain stores."""

    def __init__(self, library, links, merges, review_store=None, concept_store=None, cache=None, packs=None):
        self.library = library
        self.links = links
        self.merges = merges
        self.review_store = review_store
        self.concept_store = concept_store
        self.cache = cache if cache is not None else FileCache()
        self.packs = packs if packs is not None else PackStore(self.cache)
        self.catalog_store = CatalogStore(library, links, self.cache, self.packs)
        self.people_pack = PeoplePackStore(library, self.cache, self.packs)

    def catalog(self):
        return self.catalog_store.catalog()

    def concept_subject(self, catalog, key):
        id = catalog.resolve(key)
        row = catalog.subjects.get(id)
        if not row:
            return None
        # Concept art filed under any earlier id of the character stays its own.
        aliases = tuple(old for old, target in catalog.aliases.items() if target == id and old != id)
        subject = catalog.summary(id, row)
        return {key: subject[key] for key in ("id", "name", "classic", "classicUrl")}, aliases

    def character(self, key):
        catalog = self.catalog()
        subject = catalog.get(key)
        if subject and self.concept_store:
            _, aliases = self.concept_subject(catalog, subject["id"])
            stage = self.concept_store.stage(subject["id"], aliases)
            subject["conceptStage"] = {"count": len(stage["candidates"]), "approved": stage["approved"],
                                      "conflict": stage["conflict"]}
        if subject and subject.get("category") == "people":
            subject["peoplePack"] = self.people_pack.usage_for_npc(subject["id"], catalog, self.concept_store)
        return subject

    def concept_stage(self, key):
        resolved = self.concept_subject(self.catalog(), key)
        if resolved:
            subject, aliases = resolved
            return self.concept_store.stage(subject["id"], aliases) | subject
        return None

    def concept_action(self, key, action, body):
        resolved = self.concept_subject(self.catalog(), key)
        if not resolved:
            raise ValueError("Unknown character.")
        subject, aliases = resolved
        id = subject["id"]
        if action == "candidates":
            return self.concept_store.submit(id, body)
        if not isinstance(body, dict):
            raise ValueError("Choose one candidate to approve.")
        return self.concept_store.approve(id, aliases, body.get("candidate"), body.get("view", "front")) | subject

    def reviews(self):
        return Reviews(self.review_store, self.catalog().aliases, lambda category, id: revision(self.library, category, id))

    def overview(self):
        catalog = self.catalog()
        concepts = {}
        if self.concept_store:
            for key in self.concept_store.subjects():
                resolved = self.concept_subject(catalog, key)
                if resolved:
                    subject, aliases = resolved
                    id = subject["id"]
                    if id not in concepts:
                        stage = self.concept_store.stage(id, aliases)
                        concepts[id] = {"id": id, "name": subject["name"], "count": len(stage["candidates"]),
                                        "approved": stage["approved"]["name"] if stage["approved"] else None}
        overview = self.catalog_store.overview()
        overview["people"] = people_curation_module.enrich_people_rows(
            overview["people"], catalog, self.concept_store, self.people_pack,
        )
        return overview | {
            "reviews": self.reviews().list(),
            "conceptStages": list(concepts.values()),
            "peoplePack": self.people_pack.summary_counts(catalog, self.concept_store),
        }

    def stage_review(self, pipeline, stage):
        info = stage_info(self.library, pipeline, stage)
        if not info:
            return None
        character, state = info
        return {
            "pipeline": pipeline, "character": character.get("name", pipeline),
            "stage": stage_row(stage, STAGES[stage], state) | {"revision": state.get("updated") or "unrecorded"},
            "assets": stage_assets(self.library, pipeline, stage, state),
        }

    def update_links(self, key, body):
        identity = self.catalog_store.update_links(key, body)
        return self.character(identity)

    def run_npc_ledger_action(self, candidate):
        with TemporaryDirectory(prefix="dagger-authoring-merge-") as directory:
            file = Path(directory) / "npc-merge-candidate.json"
            file.write_text(json.dumps(candidate), encoding="utf-8")
            process = subprocess.run(
                ["python3", str(self.library.merge_script), str(file),
                 "--output", str(self.library.npcs), "--merges", str(self.merges)],
                cwd=self.library.repo, capture_output=True, text=True,
            )
        if process.returncode:
            raise ValueError((process.stderr or process.stdout).strip() or "The NPC chart rebuild failed.")
        self.cache.invalidate()
        return {"message": "NPC chart updated."}

    def merge_npcs(self, candidate):
        self.run_npc_ledger_action(candidate)
        return {"message": "NPC merge applied."}


def create_router(workspace: Workspace) -> Router:
    """Assemble the CMS route table from each domain module."""
    router = Router()

    router.get("/health", lambda _req: {"service": "Rusty Dagger Authoring"})

    def npc_chart_html(_req):
        page = (workspace.library.npcs / "index.html").read_text(encoding="utf-8")
        page = page.replace("<body>", '<body data-merge-endpoint="/api/npcs/merge">', 1)
        page = page.replace("<header>", '<header><nav><a href="/#people">Authoring workspace</a></nav>', 1)
        return Response(page.encode("utf-8"), HTTPStatus.OK, "text/html; charset=utf-8")

    def npc_chart_script(_req):
        return Response(workspace.library.npc_chart_script.read_bytes(), HTTPStatus.OK, "application/javascript; charset=utf-8")

    router.get("/media/npc/", npc_chart_html)
    router.get("/media/npc/index.html", npc_chart_html)
    router.get("/media/npc/filter.js", npc_chart_script)

    catalog_module.register_routes(router, workspace)
    reviews_module.register_routes(router, workspace)
    concepts_module.register_routes(router, workspace)
    people_pack_module.register_routes(router, workspace)
    people_curation_module.register_routes(router, workspace)
    return router


class Handler(SimpleHTTPRequestHandler):
    workspace: Workspace
    router: Router

    def end_headers(self):
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def send_data(self, data, content_type, status=HTTPStatus.OK, headers=None):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        for name, value in (headers or {}).items():
            self.send_header(name, value)
        self.end_headers()
        self.wfile.write(data)

    def json_response(self, data, status=HTTPStatus.OK):
        self.send_data(json.dumps(data).encode("utf-8"), "application/json; charset=utf-8", status)

    def translate_path(self, path):
        path = unquote(urlsplit(path).path)
        mounts = dict(self.workspace.library.mounts)
        if self.workspace.concept_store is not None:
            mounts[MEDIA] = self.workspace.concept_store.images
        for mount, root in mounts.items():
            if path.startswith(mount):
                file = (root / path.removeprefix(mount)).resolve()
                return str(file if file.is_relative_to(root.resolve()) else WEB / "missing")
        file = (WEB / path.lstrip("/")).resolve()
        return str(file if file.is_relative_to(WEB) else WEB / "missing")

    def _read_body(self):
        raw_length = self.headers.get("Content-Length")
        if not raw_length:
            return b""
        try:
            length = int(raw_length)
        except ValueError:
            return b""
        return self.rfile.read(max(0, length))

    def _handle(self, method: str, allow_static: bool = False):
        try:
            body = None if method == "GET" else self._read_body()
            response = self.router.dispatch(method, self.path, body)
            if response is None and allow_static:
                super().do_GET()
                return
            if response is None:
                self.json_response({"error": "Not found."}, HTTPStatus.NOT_FOUND)
                return
            payload, content_type = response.encode()
            self.send_data(payload, content_type, response.status, response.headers)
        except FileNotFoundError:
            self.send_error(HTTPStatus.NOT_FOUND)
        except (OSError, sqlite3.Error) as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)

    def do_GET(self):
        self._handle("GET", allow_static=True)

    def do_POST(self):
        self._handle("POST")

    def do_PUT(self):
        self._handle("PUT")

    def do_DELETE(self):
        self._handle("DELETE")


def make_server(host: str, port: int, workspace: Workspace, router: Router | None = None) -> ThreadingHTTPServer:
    """Create a ThreadingHTTPServer bound to a specific workspace and router without global state collisions."""
    bound_router = router if router is not None else create_router(workspace)

    class BoundHandler(Handler):
        pass

    BoundHandler.workspace = workspace
    BoundHandler.router = bound_router
    return ThreadingHTTPServer((host, port), BoundHandler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8784)
    parser.add_argument("--repo", type=Path, default=REPO)
    parser.add_argument("--asset-pipeline", type=Path, default=os.getenv("ASSET_PIPELINE", REPO.parent / "asset-pipeline"))
    parser.add_argument("--npc-root", type=Path, default=DEFAULT_NPCS)
    parser.add_argument("--npc-merges", type=Path, default=DEFAULT_MERGES)
    parser.add_argument("--links", type=Path, default=CMS / "links.json")
    parser.add_argument("--reviews", type=Path, default=DEFAULT_REVIEWS)
    parser.add_argument("--concept-root", type=Path, default=DEFAULT_CONCEPTS)
    parser.add_argument("--characters", type=Path, default=os.getenv("DAGGER_CHAR_ROOT"),
                        help="override the repo-local character stage root")
    parser.add_argument("--people-pack", type=Path, default=None,
                        help="override the daggerfall.people content pack directory")
    args = parser.parse_args()
    character_root = args.characters or DEFAULT_CHARACTERS
    library = Library(
        args.npc_root.resolve(),
        args.asset_pipeline.resolve(),
        character_root.resolve() if args.characters or character_root.is_dir() else None,
        repo=args.repo.resolve(),
        people_pack_root=args.people_pack.resolve() if args.people_pack else None,
    )
    workspace = Workspace(
        library,
        args.links.resolve(),
        args.npc_merges.resolve(),
        ReviewStore(args.reviews.resolve()),
        ConceptStore(args.concept_root.resolve()),
    )
    with make_server(args.host, args.port, workspace) as server:
        print(f"Rusty Dagger Authoring at http://{args.host}:{server.server_port}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
