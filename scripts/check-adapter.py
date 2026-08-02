#!/usr/bin/env python3
"""Drive the local Rusty Dagger Studio adapter over stdio.

``RUSTY_STUDIO_ADAPTER`` remains an explicit escape hatch for diagnostics
against another adapter; normal checks always exercise the local binary.
"""
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
ADAPTER = os.environ.get(
    "RUSTY_STUDIO_ADAPTER",
    str(REPO / "target" / "debug" / "dagger-studio-adapter"),
)
ROOT = str(REPO)
PROJECT = "content/projects/privateers-hold.project.json"
PROTOCOL = 14

proc = subprocess.Popen(
    [ADAPTER], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True
)

def exchange(req):
    proc.stdin.write(json.dumps(req) + "\n")
    proc.stdin.flush()
    line = proc.stdout.readline()
    if not line:
        raise SystemExit("adapter closed pipe")
    return json.loads(line)

failures = []

desc = exchange({"type": "describe", "protocolVersion": PROTOCOL, "requestId": "describe-1"})
print("describe:", desc.get("type"), "protocol:", desc.get("protocolVersion"))
if desc.get("type") != "described":
    failures.append(f"describe failed: {json.dumps(desc)[:300]}")

opened = exchange({
    "type": "openProject",
    "protocolVersion": PROTOCOL,
    "requestId": "open-1",
    "root": ROOT,
    "projectFile": PROJECT,
})
print("openProject:", opened.get("type"))
print(json.dumps(opened, indent=1)[:1200])
if opened.get("type") != "projectOpened":
    failures.append(f"openProject failed: {json.dumps(opened)[:600]}")
else:
    ident = opened.get("project", {}).get("identity", {})
    print("project identity:", json.dumps({k: ident.get(k) for k in ("projectId","name","entryScene")})[:200])

if opened.get("type") == "projectOpened":
    read = exchange({"type": "readProject", "protocolVersion": PROTOCOL, "requestId": "read-1"})
    print("readProject:", read.get("type"))
    if read.get("type") != "projectRead":
        failures.append(f"readProject failed: {json.dumps(read)[:600]}")
    else:
        body = json.dumps(read)
        for needle in ("mesh/privateers-hold", "scene/privateers-hold", "player"):
            if needle not in body:
                failures.append(f"projectRead missing {needle}")
        print("read contains mesh/privateers-hold:", "mesh/privateers-hold" in body)

    close = exchange({"type": "closeProject", "protocolVersion": PROTOCOL, "requestId": "close-1"})
    print("closeProject:", close.get("type"))
    if close.get("type") != "projectClosed":
        failures.append(f"closeProject failed: {json.dumps(close)[:600]}")

    rejected = exchange({
        "type": "setSceneObjectTransform",
        "protocolVersion": PROTOCOL,
        "requestId": "mutation-1",
        "entityId": 2,
    })
    if rejected.get("type") != "rejected" or rejected.get("error", {}).get("code") != "unsupported_operation":
        failures.append(f"mutation did not fail closed: {json.dumps(rejected)[:600]}")

proc.terminate()
if failures:
    print("ADAPTER CHECK FAILED:")
    for f in failures:
        print(" -", f)
    sys.exit(1)
print("ADAPTER CHECK PASSED")
