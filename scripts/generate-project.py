#!/usr/bin/env python3
"""Generate a studio-openable project doc for rusty-dagger from the engine
import artifacts (content/imported/) produced by rusty-asset-import.

Design decision (task 6518): hand-roll a small generator instead of consuming
@rusty-engine-demo/project-content. That package is TS, demo-flavored (loading-
bay-specific generators), and its value here would be the schema definitions
(which we mirror minimally below) plus a write-file step. Our artifacts are
already fully-formed catalog entries from rusty-asset-import; this script just
publishes them into the StoredProjectContent (schemaVersion 24) shape studio
expects, following the demo projects as the reference. If this grows past a
few asset kinds, re-evaluate consuming an engine-owned generator.

Usage: python3 scripts/generate-project.py [--write|--check]
  --write (default): write content/projects/privateers-hold.project.json
  --check: fail if the committed project doc is stale (for CI/verify hooks)
"""
import json, sys, hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
IMPORTED = REPO / "content" / "imported"
OUT = REPO / "content" / "projects" / "privateers-hold.project.json"

SCHEMA_VERSION = 24
PROJECT_ID = "privateers-hold"
SCENE_ID = "scene/privateers-hold"
MESH_ASSET = "mesh/privateers-hold"


def load_json(p: Path):
    return json.loads(p.read_text())


def build_assets(catalog: dict, static_mesh: dict) -> list:
    """StoredAssetDefinition[] from imported catalog entries + the static-mesh artifact.

    Catalog entry -> embedded asset: id, catalog {version, hash, sourcePath,
    label, dependencies(ids only)}, plus the typed payload key (material /
    staticMesh).
    """
    assets = []
    mesh_hash_by_id = {}
    for entry in catalog["entries"]:
        mesh_hash_by_id[entry["id"]] = (entry["version"], entry["hash"])

    def catalog_block(entry):
        deps = [
            {"id": d["id"], "version": d["version"], "hash": d["hash"]}
            for d in entry.get("dependencies", [])
        ]
        return {
            "version": 1,
            "hash": entry["hash"],
            "sourcePath": entry.get("sourcePath"),
            "label": entry.get("label"),
            "dependencies": deps,
        }

    for entry in catalog["entries"]:
        asset = {"id": entry["id"], "catalog": catalog_block(entry)}
        if entry["id"] == MESH_ASSET:
            asset["staticMesh"] = static_mesh
        elif "material" in entry:
            asset["material"] = entry["material"]
        else:
            raise SystemExit(f"unknown catalog entry shape: {entry['id']}")
        assets.append(asset)
    return assets


def build_scene(static_mesh: dict) -> dict:
    """One scene: the dungeon mesh entity + a player-camera entity, over a
    hidden gameplayProxy voxel environment.

    Spawn comes from the extracted start marker (content/privateers-hold.scene.json,
    written by dagger-import) when available; falls back to block center.
    """
    bounds = static_mesh["payload"]["bounds"]
    mn, mx = bounds["min"], bounds["max"]

    # Ground plane of solid voxels just under the dungeon floor (1m voxels).
    # Material slots are 1-based into the project's material palette (slot 0 =
    # empty in the engine voxel convention); slot 1 = our first material.
    x0, x1 = int(mn[0] // 1), int(mx[0] // 1) + 1
    z0, z1 = int(mn[2] // 1), int(mx[2] // 1) + 1
    solid = [
        {"address": [x, -1, z], "materialSlot": 1}
        for x in range(x0, x1 + 1)
        for z in range(z0, z1 + 1)
    ]

    voxel_environment = {
        "kind": "material",
        "voxelSize": 1.0,
        "chunkSize": 16,
        "materialVoxels": solid,
        "gameplayProxy": True,
    }

    dungeon_entity = {
        "id": 2,
        "name": "privateers-hold-dungeon",
        "translation": [0.0, 0.0, 0.0],
        "collision": {"enabled": True, "staticCollider": True},
        "renderable": {"asset": MESH_ASSET, "visible": True},
        "bounds": {"min": mn, "max": mx},
    }
    scene_meta_path = REPO / "content" / "privateers-hold.scene.json"
    spawn = [25.6, 1.6, -25.6]
    scene_meta = None
    if scene_meta_path.exists():
        scene_meta = load_json(scene_meta_path)
        if scene_meta.get("startMarker"):
            spawn = [float(v) for v in scene_meta["startMarker"]]

    # Point lights from the RDB light objects (DFU: white, intensity 0.8,
    # range = radius*0.025*3 — computed in dagger-import and stored here).
    light_entities = []
    next_id = 100
    if scene_meta and scene_meta.get("lights"):
        for entry in scene_meta["lights"]:
            light_entities.append({
                "id": next_id,
                "name": f"dungeon-light-{next_id}",
                "translation": entry["position"],
                "light": {
                    "kind": "point",
                    "color": [1.0, 1.0, 1.0],
                    "intensity": 0.8,
                    "enabled": True,
                    "range": entry["range"],
                    "decay": 2.0,
                    "shadows": False,
                },
            })
            next_id += 1

    player_entity = {
        "id": 1,
        "name": "player",
        "translation": spawn,
        "collision": {"enabled": True, "staticCollider": False},
        "renderable": {"asset": MESH_ASSET, "visible": False},
        "kinematic": {"halfExtents": [0.25, 0.25, 0.25], "velocity": [0.0, 0.0, 0.0]},
        "playerController": {
            "moveSpeedUnitsPerSecond": 4.0,
            "moveStepSeconds": 0.1,
            "lookDegreesPerUnit": 12.0,
            "initialYawDegrees": 180.0,
            "initialPitchDegrees": 0.0,
            "bindings": {
                "moveForward": "KeyW",
                "moveBackward": "KeyS",
                "moveLeft": "KeyA",
                "moveRight": "KeyD",
                "mouseLook": "pointer",
                "primaryFire": "Mouse0",
            },
        },
    }
    return {
        "id": SCENE_ID,
        "name": "Privateer's Hold",
        "voxelEnvironment": voxel_environment,
        "entities": [player_entity, dungeon_entity] + light_entities,
    }


def build_project() -> dict:
    catalog = load_json(IMPORTED / "privateers-hold.catalog.json")
    static_mesh = load_json(IMPORTED / "privateers-hold.static-mesh.json")
    return {
        "schemaVersion": SCHEMA_VERSION,
        "projectId": PROJECT_ID,
        "name": "Privateer's Hold",
        "entryScene": SCENE_ID,
        "assets": build_assets(catalog, static_mesh),
        "itemDefinitions": [],
        "scenes": [build_scene(static_mesh)],
    }


def main() -> None:
    mode = sys.argv[1] if len(sys.argv) > 1 else "--write"
    project = build_project()
    text = json.dumps(project, indent=2) + "\n"
    if mode == "--check":
        actual = OUT.read_text() if OUT.exists() else ""
        if actual != text:
            raise SystemExit(f"{OUT} is stale; run scripts/generate-project.py --write")
        print(f"{OUT} up to date ({len(project['assets'])} assets)")
        return
    if mode != "--write":
        raise SystemExit(__doc__)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text)
    digest = hashlib.sha256(text.encode()).hexdigest()[:16]
    print(f"wrote {OUT} ({len(text)} bytes, sha256:{digest}, assets={len(project['assets'])})")


if __name__ == "__main__":
    main()
