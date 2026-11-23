#!/usr/bin/env python3
"""Remesh a Meshy task's model to a light triangle mesh, then rig it, by API.

    meshy_rig.py MESH_TASK_DIR OUT_DIR [--polycount 40000] [--height 1.78]

MESH_TASK_DIR holds task.json from meshy_multiview.py. Meshy's rigging refuses models over
380,000 triangles, so the model is first remeshed (its own Remesh API, by task id). Writes
remesh.glb, rigged.glb / rigged.fbx, the walking and running clips Meshy returns, and both
task records. MESHY_TOKEN comes from the environment or asset-pipeline's `.api.env` (API_ENV overrides).
Moved from asset-pipeline tools/splat-scaffold on 2026-11-23 for static-npc's walker rig (npc-468).
"""
import json
import os
import sys
import time
import urllib.request
from pathlib import Path

API = "https://api.meshy.ai/openapi/v1"
a = sys.argv[1:]
if len(a) < 2 or "-h" in a:
    sys.exit(__doc__)
src, out = Path(a[0]), Path(a[1])
opt = lambda n, d: type(d)(a[a.index(n) + 1]) if n in a else d


def token():
    if os.environ.get("MESHY_TOKEN"):
        return os.environ["MESHY_TOKEN"]
    env = Path(os.environ.get("API_ENV") or Path.home() / "dev/asset-pipeline/.api.env")   # asset-pipeline's git-ignored .api.env
    for line in env.read_text().splitlines():
        if line.startswith("MESHY_TOKEN="):
            return line.split("=", 1)[1].strip().strip("\"'")
    sys.exit("MESHY_TOKEN not set")


def call(url, body=None):
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body else None, method="POST" if body else "GET",
                                 headers={"Authorization": f"Bearer {token()}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        sys.exit(f"meshy {e.code}: {e.read().decode()[:500]}")


def wait(kind, tid):
    t0, last = time.time(), None
    while True:
        task = call(f"{API}/{kind}/{tid}")
        if (task["status"], task.get("progress")) != last:
            last = (task["status"], task.get("progress"))
            print(f"  {kind} {int(time.time() - t0)}s {task['status']} {task.get('progress')}%", flush=True)
        if task["status"] in ("SUCCEEDED", "FAILED", "CANCELED"):
            (out / f"{kind}-task.json").write_text(json.dumps(task, indent=1))
            if task["status"] != "SUCCEEDED":
                sys.exit(f"meshy {kind} {task['status']}: {task.get('task_error')}")
            return task
        time.sleep(8)


def fetch(url, path):
    with urllib.request.urlopen(url, timeout=600) as r:
        path.write_bytes(r.read())


def urls(obj, prefix=""):
    """Every downloadable URL in a task result, keyed by its path in the record."""
    if isinstance(obj, dict):
        for k, v in obj.items():
            yield from urls(v, f"{prefix}{k}.")
    elif isinstance(obj, str) and obj.startswith("http"):
        yield prefix.rstrip("."), obj


out.mkdir(parents=True, exist_ok=True)
mesh_id = json.loads((src / "task.json").read_text())["id"]
rid = call(f"{API}/remesh", {"input_task_id": mesh_id, "target_formats": ["glb"], "topology": "triangle",
                             "target_polycount": opt("--polycount", 40000)})["result"]
print("remesh task", rid, flush=True)
remesh = wait("remesh", rid)
fetch(remesh["model_urls"]["glb"], out / "remesh.glb")
gid = call(f"{API}/rigging", {"input_task_id": rid, "height_meters": opt("--height", 1.78)})["result"]
print("rigging task", gid, flush=True)
rig = wait("rigging", gid)
for key, url in urls(rig.get("result", rig)):
    ext = url.split("?")[0].rsplit(".", 1)[-1]
    if ext in ("glb", "fbx"):
        name = key.replace("_url", "").replace("basic_animations.", "").replace("rigged_character_", "rigged.").replace(".", "-")
        fetch(url, out / f"{name}.{ext}")
        print("  got", f"{name}.{ext}")
print("done", out)
