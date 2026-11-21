#!/usr/bin/env python3
"""Serve the Rusty Dagger authoring workspace on the LAN."""

import argparse
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
from tempfile import NamedTemporaryFile, TemporaryDirectory
from threading import Lock
from urllib.parse import unquote, urlsplit

from catalog import Catalog, assigned
from library import CMS, DEFAULT_MERGES, DEFAULT_NPCS, REPO, Library, read_json

WEB = CMS / "web"


class Workspace:
    def __init__(self, library, links, merges):
        self.library = library
        self.links = links
        self.merges = merges
        self.link_lock = Lock()

    def catalog(self):
        return Catalog(self.library, read_json(self.links, {}))

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
            return self.catalog().get(identity)

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
        for mount, root in self.workspace.library.mounts.items():
            if path.startswith(mount):
                file = (root / path.removeprefix(mount)).resolve()
                return str(file if file.is_relative_to(root.resolve()) else WEB / "missing")
        file = (WEB / path.lstrip("/")).resolve()
        return str(file if file.is_relative_to(WEB) else WEB / "missing")

    def do_GET(self):
        path = urlsplit(self.path).path
        try:
            if path == "/health":
                self.json_response({"service": "Rusty Dagger Authoring"})
            elif path == "/api/catalog":
                self.json_response(self.workspace.catalog().overview())
            elif path.startswith("/api/characters/"):
                subject = self.workspace.catalog().get(unquote(path.removeprefix("/api/characters/")))
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
        except (OSError, ValueError) as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)

    def do_POST(self):
        path = urlsplit(self.path).path
        try:
            if path == "/api/npcs/merge":
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
        except OSError as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8784)
    parser.add_argument("--asset-pipeline", type=Path, default=os.getenv("ASSET_PIPELINE", REPO.parent / "asset-pipeline"))
    parser.add_argument("--npc-root", type=Path, default=DEFAULT_NPCS)
    parser.add_argument("--npc-merges", type=Path, default=DEFAULT_MERGES)
    parser.add_argument("--links", type=Path, default=CMS / "links.json")
    args = parser.parse_args()
    Handler.workspace = Workspace(Library(args.npc_root.resolve(), args.asset_pipeline.resolve()),
                                  args.links.resolve(), args.npc_merges.resolve())
    with ThreadingHTTPServer((args.host, args.port), Handler) as server:
        print(f"Rusty Dagger Authoring at http://{args.host}:{server.server_port}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
