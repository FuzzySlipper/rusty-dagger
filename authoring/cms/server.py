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
from tempfile import NamedTemporaryFile, TemporaryDirectory
from threading import Lock
from urllib.parse import parse_qs, unquote, urlsplit

from catalog import Catalog, assigned
from concepts import MEDIA, ConceptStore
from library import CMS, DEFAULT_CONCEPTS, DEFAULT_MERGES, DEFAULT_NPCS, DEFAULT_REVIEWS, REPO, Library, read_json
from pipelines import STAGES, stage_row
from review_media import revision, stage_assets, stage_info
from reviews import ReviewStore, Reviews

WEB = CMS / "web"


class Workspace:
    def __init__(self, library, links, merges, review_store=None, concept_store=None):
        self.library = library
        self.links = links
        self.merges = merges
        self.review_store = review_store
        self.concept_store = concept_store
        self.link_lock = Lock()

    def catalog(self):
        return Catalog(self.library, read_json(self.links, {}))

    def concept_subject(self, catalog, key):
        id = key if key in catalog.npcs or key in catalog.mobiles else catalog.aliases.get(key, key)
        row = catalog.npcs.get(id) or catalog.mobiles.get(id)
        if not row:
            return None
        aliases = tuple(old for old, target in catalog.aliases.items()
                        if target == id and old.startswith("npc-") and old != id)
        return id, row["name"], aliases

    def character(self, key):
        catalog = self.catalog()
        subject = catalog.get(key)
        if subject and self.concept_store:
            id, _, aliases = self.concept_subject(catalog, subject["id"])
            stage = self.concept_store.stage(id, aliases)
            subject["conceptStage"] = {"count": len(stage["candidates"]), "approved": stage["approved"],
                                      "conflict": stage["conflict"]}
        return subject

    def concept_stage(self, key):
        subject = self.concept_subject(self.catalog(), key)
        if subject:
            id, name, aliases = subject
            return self.concept_store.stage(id, aliases) | {"name": name}
        return None

    def concept_action(self, key, action, body):
        subject = self.concept_subject(self.catalog(), key)
        if not subject:
            raise ValueError("Unknown character.")
        id, name, aliases = subject
        if action == "candidates":
            return self.concept_store.submit(id, body)
        if not isinstance(body, dict):
            raise ValueError("Choose one candidate to approve.")
        return self.concept_store.approve(id, aliases, body.get("candidate"), body.get("view", "front")) | {"name": name}

    def reviews(self):
        aliases = read_json(self.library.npcs / "manifest.json", {}).get("aliases", {})
        return Reviews(self.review_store, aliases, lambda category, id: revision(self.library, category, id))

    def overview(self):
        catalog = self.catalog()
        concepts = {}
        if self.concept_store:
            for key in self.concept_store.subjects():
                subject = self.concept_subject(catalog, key)
                if subject:
                    id, name, aliases = subject
                    if id not in concepts:
                        stage = self.concept_store.stage(id, aliases)
                        concepts[id] = {"id": id, "name": name, "count": len(stage["candidates"]),
                                        "approved": stage["approved"]["name"] if stage["approved"] else None}
        return catalog.overview() | {"reviews": self.reviews().list(), "conceptStages": list(concepts.values())}

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
        if not isinstance(body, dict):
            raise ValueError("A link update needs a JSON object.")
        with self.link_lock:
            catalog = self.catalog()
            subject = catalog.get(key)
            if not subject:
                raise ValueError("Unknown character.")
            identity = subject["id"]
            current = dict(catalog.links.get(identity, {}))
            if "addPipeline" in body:
                pipeline = body["addPipeline"]
                if not isinstance(pipeline, str) or pipeline not in catalog.pipelines:
                    raise ValueError("Choose an existing character pipeline.")
                current["pipelines"] = list(dict.fromkeys([*current.get("pipelines", []), pipeline]))
            if "removePipeline" in body:
                current["pipelines"] = [id for id in current.get("pipelines", []) if id != body["removePipeline"]]
            allowed = set(subject["automaticPipelines"]) | set(current.get("pipelines", []))
            for role in ("concept", "final"):
                if role in body:
                    choice = body[role]
                    if choice is None:
                        current.pop(role, None)
                        continue
                    if not isinstance(choice, dict) or not isinstance(choice.get("pipeline"), str) or choice["pipeline"] not in allowed:
                        raise ValueError("Choose an asset from a linked pipeline.")
                    pipeline = catalog.pipelines[choice["pipeline"]]
                    choices = pipeline["concepts"] if role == "concept" else [pipeline["final"]] if pipeline["final"] else []
                    if not assigned(choice, choices):
                        raise ValueError(f"The selected {role} is no longer available.")
                    current[role] = {"pipeline": choice["pipeline"], "path": choice["path"]}
                elif current.get(role, {}).get("pipeline") not in allowed:
                    current.pop(role, None)
            links = {}
            for id, link in catalog.raw_links.items():
                if id == identity:
                    continue
                if catalog.aliases.get(id) != identity:
                    links[id] = link
                    continue
                unresolved = {role: link[role] for role in catalog.conflicts.get(identity, [])
                              if role not in body and role in link and link[role]["pipeline"] in allowed}
                if unresolved:
                    links[id] = unresolved
            if any(current.values()):
                links[identity] = current
            self.links.parent.mkdir(parents=True, exist_ok=True)
            with NamedTemporaryFile(mode="w", encoding="utf-8", dir=self.links.parent, delete=False) as file:
                json.dump(links, file, indent=2, sort_keys=True)
                file.write("\n")
            Path(file.name).replace(self.links)
            return self.character(identity)

    def merge_npcs(self, candidate):
        with TemporaryDirectory(prefix="dagger-authoring-merge-") as directory:
            file = Path(directory) / "npc-merge-candidate.json"
            file.write_text(json.dumps(candidate), encoding="utf-8")
            process = subprocess.run(
                ["python3", str(REPO / "scripts/apply-npc-merges.py"), str(file),
                 "--output", str(self.library.npcs), "--merges", str(self.merges)],
                cwd=REPO, capture_output=True, text=True,
            )
        if process.returncode:
            raise ValueError((process.stderr or process.stdout).strip() or "The NPC chart rebuild failed.")
        return {"message": "NPC merge applied."}


class Handler(SimpleHTTPRequestHandler):
    workspace: Workspace

    def end_headers(self):
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def send_data(self, data, content_type, status=HTTPStatus.OK):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def json_response(self, data, status=HTTPStatus.OK):
        self.send_data(json.dumps(data).encode(), "application/json; charset=utf-8", status)

    def translate_path(self, path):
        path = unquote(urlsplit(path).path)
        mounts = self.workspace.library.mounts | {MEDIA: self.workspace.concept_store.images}
        for mount, root in mounts.items():
            if path.startswith(mount):
                file = (root / path.removeprefix(mount)).resolve()
                return str(file if file.is_relative_to(root.resolve()) else WEB / "missing")
        file = (WEB / path.lstrip("/")).resolve()
        return str(file if file.is_relative_to(WEB) else WEB / "missing")

    def review_key(self, path, prefix):
        category, sep, id = path.removeprefix(prefix).partition("/")
        if not sep or not category or not id:
            raise ValueError("Provide a category and record ID.")
        return unquote(category), unquote(id)

    def do_GET(self):
        url = urlsplit(self.path)
        path = url.path
        try:
            if path == "/health":
                self.json_response({"service": "Rusty Dagger Authoring"})
            elif path == "/api/catalog":
                self.json_response(self.workspace.overview())
            elif path == "/api/reviews":
                fields = parse_qs(url.query)
                select = lambda key: fields.get(key, [None])[0]
                rows = self.workspace.reviews().list(select("category"))
                self.json_response([row for row in rows
                                    if (select("marked") != "1" or row["marked"])
                                    and (not select("rating") or row["rating"] == select("rating"))
                                    and (not select("prefix") or row["id"].startswith(select("prefix")))])
            elif path.startswith("/api/reviews/"):
                self.json_response(self.workspace.reviews().item(*self.review_key(path, "/api/reviews/")))
            elif path.startswith("/api/stage-review/"):
                review = self.workspace.stage_review(*self.review_key(path, "/api/stage-review/"))
                self.json_response(review or {"error": "Stage not found."},
                                   HTTPStatus.OK if review else HTTPStatus.NOT_FOUND)
            elif path.startswith("/api/concepts/"):
                stage = self.workspace.concept_stage(unquote(path.removeprefix("/api/concepts/")))
                self.json_response(stage or {"error": "Character not found."},
                                   HTTPStatus.OK if stage else HTTPStatus.NOT_FOUND)
            elif path.startswith("/api/characters/"):
                subject = self.workspace.character(unquote(path.removeprefix("/api/characters/")))
                self.json_response(subject or {"error": "Character not found."},
                                   HTTPStatus.OK if subject else HTTPStatus.NOT_FOUND)
            elif path.startswith("/api/pipelines/"):
                record = self.workspace.catalog().pipelines.get(unquote(path.removeprefix("/api/pipelines/")))
                self.json_response(record or {"error": "Pipeline not found."},
                                   HTTPStatus.OK if record else HTTPStatus.NOT_FOUND)
            elif path in ("/media/npc/", "/media/npc/index.html"):
                page = (self.workspace.library.npcs / "index.html").read_text()
                page = page.replace("<body>", '<body data-merge-endpoint="/api/npcs/merge">', 1)
                page = page.replace("<header>", '<header><nav><a href="/#npcs">Authoring workspace</a></nav>', 1)
                self.send_data(page.encode(), "text/html; charset=utf-8")
            elif path == "/media/npc/filter.js":
                script = REPO / "src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceChart.js"
                self.send_data(script.read_bytes(), "application/javascript; charset=utf-8")
            else:
                super().do_GET()
        except FileNotFoundError:
            self.send_error(HTTPStatus.NOT_FOUND)
        except (OSError, ValueError, sqlite3.Error) as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)

    def do_POST(self):
        path = urlsplit(self.path).path
        try:
            if path.startswith("/api/marks/") and path.endswith("/clear"):
                category = unquote(path.removeprefix("/api/marks/").removesuffix("/clear"))
                if not category:
                    raise ValueError("Provide a category.")
                self.json_response({"cleared": self.workspace.review_store.clear_marks(category)})
                return
            if path.startswith("/api/concepts/") and path.endswith(("/candidates", "/approve")):
                subject, operation = self.review_key(path, "/api/concepts/")
                action = lambda body: self.workspace.concept_action(subject, operation, body)
            elif path.startswith("/api/reviews/"):
                category, id = self.review_key(path, "/api/reviews/")
                action = lambda body: self.workspace.reviews().patch(category, id, body)
            elif path == "/api/npcs/merge":
                action = self.workspace.merge_npcs
            elif path.startswith("/api/characters/") and path.endswith("/links"):
                key = unquote(path.removeprefix("/api/characters/").removesuffix("/links"))
                action = lambda body: self.workspace.update_links(key, body)
            else:
                self.json_response({"error": "Unknown action."}, HTTPStatus.NOT_FOUND)
                return
            body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            self.json_response(action(body))
        except (KeyError, ValueError) as error:
            self.json_response({"error": str(error)}, HTTPStatus.BAD_REQUEST)
        except (OSError, sqlite3.Error) as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8784)
    parser.add_argument("--asset-pipeline", type=Path, default=os.getenv("ASSET_PIPELINE", REPO.parent / "asset-pipeline"))
    parser.add_argument("--npc-root", type=Path, default=DEFAULT_NPCS)
    parser.add_argument("--npc-merges", type=Path, default=DEFAULT_MERGES)
    parser.add_argument("--links", type=Path, default=CMS / "links.json")
    parser.add_argument("--reviews", type=Path, default=DEFAULT_REVIEWS)
    parser.add_argument("--concept-root", type=Path, default=DEFAULT_CONCEPTS)
    parser.add_argument("--characters", type=Path, default=os.getenv("DAGGER_CHAR_ROOT"),
                        help="character stage root, such as authoring/characters after moving the assets")
    args = parser.parse_args()
    character_root = args.characters or REPO / "authoring/characters"
    library = Library(args.npc_root.resolve(), args.asset_pipeline.resolve(),
                      character_root.resolve() if args.characters or character_root.is_dir() else None)
    Handler.workspace = Workspace(library, args.links.resolve(), args.npc_merges.resolve(),
                                  ReviewStore(args.reviews.resolve()), ConceptStore(args.concept_root.resolve()))
    with ThreadingHTTPServer((args.host, args.port), Handler) as server:
        print(f"Rusty Dagger Authoring at http://{args.host}:{server.server_port}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
