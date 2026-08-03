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
TEXTURES = REPO / "content" / "textures"
OUT = REPO / "content" / "projects" / "privateers-hold.project.json"

SCHEMA_VERSION = 24
PROJECT_ID = "privateers-hold"
SCENE_ID = "scene/privateers-hold"
MESH_ASSET = "mesh/privateers-hold"

# Collision stopgap tuning (shared with scripts/find-route.py — keep in sync).
# Until upstream trimesh collision lands (rusty-engine task 6516), the hidden
# gameplayProxy voxel environment is the collision authority. See build_scene.
PROXY_CELL = 0.5            # voxel edge length (m)
PROXY_NORMAL_THRESHOLD = 0.5  # up-facing filter: ny/|n| > 0.5 (floors + ramps)
PROXY_CLUSTER_TOL = 0.3     # merge surface heights closer than this per column
PLAYER_FALL_SPEED = 12.0    # controller settle speed (m/s), opt-in
PLAYER_STEP_UP = 0.75       # controller ledge climb assist (m), opt-in


def load_json(p: Path):
    return json.loads(p.read_text())


def load_texture_manifest() -> dict:
    """sha256 manifest emitted by dagger-import --texture-dir (content/textures).

    Maps PNG file name -> {"sha256", "byteLength"}. Missing/empty manifest means
    the untextured fallback chain was run; texture catalog entries then keep
    their importer placeholder hash and no sourcePath, and the adapter simply
    projects no texture resources.
    """
    manifest = TEXTURES / "manifest.json"
    if not manifest.exists():
        return {}
    data = load_json(manifest)
    return {t["path"]: t for t in data.get("textures", [])}


def build_assets(catalog: dict, static_mesh: dict) -> list:
    """StoredAssetDefinition[] from imported catalog entries + the static-mesh artifact.

    Catalog entry -> embedded asset: id, catalog {version, hash, sourcePath,
    label, dependencies(ids only)}, plus the typed payload key (material /
    staticMesh). texture/ entries additionally get their catalog hash and
    sourcePath stamped from the texture manifest so the studio adapter can
    resolve exact content-addressed PNG render resources.
    """
    texture_manifest = load_texture_manifest()

    def texture_block(entry):
        """Texture asset payload: decoded PNG geometry + content identity.

        The importer creates bare texture/ catalog entries; the exact bytes
        live at content/textures/<slug>.png and their hash is authoritative.
        """
        slug = entry["id"].split("/", 1)[1]
        png_name = f"{slug}.png"
        stamped = texture_manifest.get(png_name)
        if not stamped:
            raise SystemExit(
                f"texture manifest is missing {png_name} for {entry['id']}; "
                "run scripts/regenerate.sh (dagger-import --texture-dir)"
            )
        # PNG dimensions are needed for the renderer's retained-texture budget;
        # read the IHDR directly (fixed offsets 16..24) — no decoder required.
        png = (TEXTURES / png_name).read_bytes()
        if png[:8] != b"\x89PNG\r\n\x1a\n":
            raise SystemExit(f"{png_name} is not a PNG")
        width = int.from_bytes(png[16:20], "big")
        height = int.from_bytes(png[20:24], "big")
        actual = "sha256:" + hashlib.sha256(png).hexdigest()
        if actual != stamped["sha256"]:
            raise SystemExit(f"{png_name} hash drifted from the manifest; regenerate textures")
        return {
            "width": width,
            "height": height,
            "filter": "nearest",
            "wrap": "repeat",
            "sourcePath": f"content/textures/{png_name}",
            "contentHash": stamped["sha256"],
        }

    assets = []

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
        elif entry["id"].startswith("texture/"):
            texture = texture_block(entry)
            asset["texture"] = {
                "width": texture["width"],
                "height": texture["height"],
                "filter": texture["filter"],
                "wrap": texture["wrap"],
            }
            # Stamp exact content identity for the adapter/host resource path.
            asset["catalog"]["hash"] = texture["contentHash"].removeprefix("sha256:")
            asset["catalog"]["sourcePath"] = texture["sourcePath"]
        elif "material" in entry:
            asset["material"] = entry["material"]
        else:
            raise SystemExit(f"unknown catalog entry shape: {entry['id']}")
        assets.append(asset)

    # Re-stamp material -> texture dependency hashes so the catalog lock stays
    # consistent with the real PNG content hashes stamped above (the importer
    # only knows placeholder texture hashes).
    real_tex_hash = {
        a["id"]: a["catalog"]["hash"] for a in assets if a["id"].startswith("texture/")
    }
    for a in assets:
        style = a.get("material", {}).get("style", {})
        for dep in a["catalog"]["dependencies"]:
            if dep["id"] in real_tex_hash:
                dep["hash"] = real_tex_hash[dep["id"]]
        tex_ref = style.get("texture")
        if isinstance(tex_ref, dict) and tex_ref.get("id") in real_tex_hash:
            tex_ref["hash"] = real_tex_hash[tex_ref["id"]]
    return assets


def build_scene(static_mesh: dict) -> dict:
    """One scene: the dungeon mesh entity + a player-camera entity, over a
    hidden gameplayProxy voxel environment.

    Collision stopgap until upstream trimesh lands (rusty-engine task 6516,
    swap path: replace this rasterizer with the engine collision owner):

    - Every up-facing mesh triangle (ny/|n| > PROXY_NORMAL_THRESHOLD, so floors
      and walkable ramps) is rasterized into the 0.5m columns its XZ bounding
      box touches, recording the triangle centroid height.
    - Heights within one column are clustered (PROXY_CLUSTER_TOL), and each
      cluster becomes ONE solid voxel whose top face sits at the cluster
      height rounded to the nearest cell boundary (max 0.25m error). Columns
      therefore keep EVERY walkable level (spawn plateau at 38.4 AND the
      levels beneath it), not just the topmost surface — this is what lets
      the start-marker layer and the descending border route both have
      authoritative support (review finding R6520-1).
    - Where the mesh has no up-facing surface there is no voxel: walking out
      of the dungeon through a gap means falling, and the walkthrough's
      negative probes prove support disappears when the proxy is displaced.

    Known limitations (documented, accepted for the stopgap): vertical wall
    faces contribute no voxels, so wall solidity is incidental (a wall only
    blocks where its top edge or an adjacent floor voxel intersects the
    mover), and a raised solid (e.g. a staircase flank) is represented only
    by its top surface, so its underside is hollow from below. The verified
    traversal route (content/projects/privateers-hold.route.json) is checked
    against these voxels, so the accepted route's floor collision is real.

    The player controller opts into fall/step-up semantics (parsed by the
    loading-bay runtime): constant-speed downward settle after every action
    plus a bounded ledge climb assist, so support, landing, and stair descent
    are observable through authoritative readback.
    """
    import math

    bounds = static_mesh["payload"]["bounds"]
    mn, mx = bounds["min"], bounds["max"]

    # Rasterize up-facing triangles from the inline mesh payload into columns.
    positions = static_mesh["payload"]["source"]["positions"]
    indices = static_mesh["payload"]["source"]["indices"]
    CELL = PROXY_CELL
    columns: dict = {}
    for t in range(0, len(indices), 3):
        a, b, c = indices[t], indices[t + 1], indices[t + 2]
        ax, ay, az = positions[a * 3], positions[a * 3 + 1], positions[a * 3 + 2]
        bx, by, bz = positions[b * 3], positions[b * 3 + 1], positions[b * 3 + 2]
        cx, cy, cz = positions[c * 3], positions[c * 3 + 1], positions[c * 3 + 2]
        ux, uy, uz = bx - ax, by - ay, bz - az
        vx, vy, vz = cx - ax, cy - ay, cz - az
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        ln = (nx * nx + ny * ny + nz * nz) ** 0.5 or 1.0
        if ny / ln <= PROXY_NORMAL_THRESHOLD:
            continue
        fy = (ay + by + cy) / 3.0
        ix0 = math.floor(min(ax, bx, cx) / CELL)
        ix1 = math.floor(max(ax, bx, cx) / CELL)
        iz0 = math.floor(min(az, bz, cz) / CELL)
        iz1 = math.floor(max(az, bz, cz) / CELL)
        for ix in range(ix0, ix1 + 1):
            for iz in range(iz0, iz1 + 1):
                columns.setdefault((ix, iz), []).append(fy)

    # Cluster each column's surface heights, then emit one voxel per cluster.
    # The voxel at address y occupies [y*CELL, (y+1)*CELL]; round the top face
    # to the cell boundary nearest the surface (y+1 = round(fy/CELL)) so a
    # walkable plateau's tops stay level instead of stair-stepping.
    addresses = set()
    for (ix, iz), heights in columns.items():
        heights.sort()
        clusters: list = []
        for h in heights:
            if clusters and h - clusters[-1][-1] <= PROXY_CLUSTER_TOL:
                clusters[-1].append(h)
            else:
                clusters.append([h])
        for group in clusters:
            fy = sum(group) / len(group)
            addresses.add((ix, round(fy / CELL) - 1, iz))

    solid = [
        {"address": [ix, iy, iz], "materialSlot": 1}
        for (ix, iy, iz) in sorted(addresses)
    ]

    voxel_environment = {
        "kind": "material",
        "voxelSize": CELL,
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
            "fallSpeedUnitsPerSecond": PLAYER_FALL_SPEED,
            "stepUpUnits": PLAYER_STEP_UP,
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
    # Compact separators: deterministic regenerated artifact, and the studio
    # adapter rejects project docs over 8 MiB (the multi-level collision
    # proxy pushes the pretty-printed form past that bound).
    text = json.dumps(project, separators=(",", ":")) + "\n"
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
