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
GALLERY_OUT = REPO / "content" / "projects" / "encounter-gallery.project.json"
GALLERY_NAV_OUT = REPO / "content" / "projects" / "encounter-gallery.navgrid.json"

SCHEMA_VERSION = 24
PROJECT_ID = "privateers-hold"
SCENE_ID = "scene/privateers-hold"
MESH_ASSET = "mesh/privateers-hold"

# Player controller tuning (fall settle + ledge climb assist, opt-in). The
# collision authority is the dungeon static mesh's trimesh policy (see
# build_scene); there is no voxel proxy anymore.
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


def build_assets(catalog: dict, static_mesh: dict, billboard_manifest: dict, enemy_manifest: dict, combat_manifest: dict) -> list:
    """StoredAssetDefinition[] from imported catalog entries + the static-mesh artifact.

    Catalog entry -> embedded asset: id, catalog {version, hash, sourcePath,
    label, dependencies(ids only)}, plus the typed payload key (material /
    staticMesh). texture/ entries additionally get their catalog hash and
    sourcePath stamped from the texture manifest so the studio adapter can
    resolve exact content-addressed PNG render resources. Billboard sprite
    textures (RDB flats) are appended as texture/billboard-A-R entries with
    transparent PNGs from the billboard manifest; enemy directional atlases
    as texture/enemy-<id>-atlas entries with per-orientation frame rects.
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
            print(
                f"warning: texture manifest is missing {png_name} for {entry['id']}; "
                "stamping the on-disk bytes (run scripts/regenerate.sh to refresh the manifest)",
                file=sys.stderr,
            )
        # PNG dimensions are needed for the renderer's retained-texture budget;
        # read the IHDR directly (fixed offsets 16..24) — no decoder required.
        png = (TEXTURES / png_name).read_bytes()
        if png[:8] != b"\x89PNG\r\n\x1a\n":
            raise SystemExit(f"{png_name} is not a PNG")
        width = int.from_bytes(png[16:20], "big")
        height = int.from_bytes(png[20:24], "big")
        actual = "sha256:" + hashlib.sha256(png).hexdigest()
        if stamped and actual != stamped["sha256"]:
            # Hand edits are legitimate; surface the drift and publish the
            # actual bytes' identity rather than hard-stopping generation.
            print(f"warning: {png_name} drifted from the manifest; stamping actual hash", file=sys.stderr)
        content_hash = actual
        return {
            "width": width,
            "height": height,
            "filter": "nearest",
            "wrap": "repeat",
            "sourcePath": f"content/textures/{png_name}",
            "contentHash": content_hash,
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

    # Billboard sprite textures (RDB flat billboards). One texture asset per
    # unique (archive, record) with a transparent PNG (index 0 = transparent).
    # Multi-frame records (torch flames, animated lights) carry a multi-frame
    # spriteAtlas with per-frame UV rects from the billboard manifest;
    # single-frame records use the default full-rect atlas.
    for tex in billboard_manifest.get("billboards", []):
        slug = f"billboard-{tex['archive']}-{tex['record']}"
        frame_count = tex.get("frameCount", 1)
        atlas_frames = tex.get("frames", [{"frame": 0, "uvMin": [0, 0], "uvMax": [1, 1]}])
        # Packed PNG dims are explicit in the manifest; fall back to the
        # frame-dims times frame-count derivation for older manifests.
        tex_width = tex.get("atlasWidth", tex["width"] * frame_count)
        tex_height = tex.get("atlasHeight", tex["height"])
        assets.append({
            "id": f"texture/{slug}",
            "catalog": {
                "version": 1,
                "hash": tex["sha256"].removeprefix("sha256:"),
                "sourcePath": f"content/textures/{tex['path']}",
                "label": slug,
                "dependencies": [],
            },
            "texture": {
                "width": tex_width,
                "height": tex_height,
                "filter": "nearest",
                "wrap": "clamp",
                "alphaCutout": True,
                "spriteAtlas": {
                    "frames": atlas_frames,
                },
            },
        })

    # Enemy directional sprite atlases (6595). One texture asset per unique
    # enemy mobile id; the importer-packed, Engine-bounded PNG holds the
    # orientation/animation frames (mirrored sides baked) and the manifest
    # carries per-frame UV rects, per-frame world sizes (the Engine resizes
    # the quad on frame change), and source DFU sizes as provenance.
    for enemy in enemy_manifest.get("enemies", []):
        slug = f"enemy-{enemy['mobileId']}-atlas"
        assets.append({
            "id": f"texture/{slug}",
            "catalog": {
                "version": 1,
                "hash": enemy["sha256"].removeprefix("sha256:"),
                "sourcePath": f"content/textures/{enemy['path']}",
                "label": slug,
                "dependencies": [],
            },
            "texture": {
                "width": enemy["width"],
                "height": enemy["height"],
                "filter": "nearest",
                "wrap": "clamp",
                "alphaCutout": True,
                "spriteAtlas": {
                    "frames": [
                        {"frame": f["frame"], "uvMin": f["uvMin"], "uvMax": f["uvMax"], **({"size": f["size"]} if "size" in f else {})}
                        for f in enemy["frames"]
                    ],
                },
            },
        })
        corpse = enemy.get("corpse")
        if isinstance(corpse, dict):
            corpse_slug = f"enemy-{enemy['mobileId']}-corpse"
            assets.append({
                "id": f"texture/{corpse_slug}",
                "catalog": {
                    "version": 1,
                    "hash": corpse["sha256"].removeprefix("sha256:"),
                    "sourcePath": f"content/textures/{corpse['path']}",
                    "label": corpse_slug,
                    "dependencies": [],
                },
                "texture": {
                    "width": corpse["width"],
                    "height": corpse["height"],
                    "filter": "nearest",
                    "wrap": "clamp",
                    "alphaCutout": True,
                    "spriteAtlas": {
                        "frames": [{"frame": 0, "uvMin": [0, 0], "uvMax": [1, 1]}],
                    },
                },
            })

    # Clone-first combat art is generated by dagger-import from classic CIF
    # and TEXTURE.380 records. The semantic catalog remains in
    # combat-manifest.json; project assets only publish exact texture bytes and
    # atlas UVs through the ordinary Engine facade.
    combat_textures = []
    weapon = combat_manifest.get("weapon")
    if isinstance(weapon, dict):
        combat_textures.append(weapon)
    combat_textures.extend(combat_manifest.get("effects", []))
    for combat in combat_textures:
        png_path = TEXTURES / combat["path"]
        png = png_path.read_bytes()
        actual = "sha256:" + hashlib.sha256(png).hexdigest()
        if actual != combat["sha256"]:
            # Hand edits are legitimate; surface the drift and stamp the hash
            # of the bytes actually being published.
            print(f"warning: {combat['path']} drifted from combat-manifest.json; stamping actual hash", file=sys.stderr)
        assets.append({
            "id": combat["textureAssetId"],
            "catalog": {
                "version": 1,
                "hash": actual.removeprefix("sha256:"),
                "sourcePath": f"content/textures/{combat['path']}",
                "label": combat["id"],
                "dependencies": [],
            },
            "texture": {
                "width": combat["width"],
                "height": combat["height"],
                "filter": "nearest",
                "wrap": "clamp",
                "alphaCutout": True,
                "spriteAtlas": {
                    "frames": [
                        {"frame": frame["frame"], "uvMin": frame["uvMin"], "uvMax": frame["uvMax"]}
                        for frame in combat["frames"]
                    ],
                },
            },
        })

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


def build_scene(static_mesh: dict, enemy_manifest: dict, billboard_manifest: dict) -> dict:
    """One scene: the dungeon mesh entity + a player-camera entity.

    Collision authority is the dungeon static mesh itself (rusty-engine task
    6516): the artifact's `collision.kind == "trimesh"` makes the full inline
    triangle payload — floors, walls, ceilings, ramps — one trimesh collider.
    There is no hidden `gameplayProxy` voxel environment anymore; the legacy
    rasterizer (and its wall/underside limitations) is retired. dagger-runtime
    registers the collider on admission; the kinematic sweep blocks on real
    geometry with no controller changes.

    The player controller opts into fall/step-up semantics: constant-speed
    downward settle after every action plus a bounded ledge climb assist, so
    support, landing, and stair descent are observable through authoritative
    readback.
    """
    bounds = static_mesh["payload"]["bounds"]
    mn, mx = bounds["min"], bounds["max"]

    dungeon_entity = {
        "id": 2,
        "name": "privateers-hold-dungeon",
        "translation": [0.0, 0.0, 0.0],
        "collision": {"enabled": True, "staticCollider": True},
        "renderable": {"asset": MESH_ASSET, "visible": True},
        "bounds": {"min": mn, "max": mx},
    }
    # Scene metadata sidecar from the mesh-json import run (markers, lights,
    # billboards in glTF world space). The mesh.scene.json is the complete one
    # (the GLB run's scene.json is a lighter subset).
    scene_meta_path = REPO / "content" / "privateers-hold.mesh.scene.json"
    if not scene_meta_path.exists():
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

    # Billboard sprite entities (RDB flat billboards: torches, furniture,
    # markers). One SpriteInstanceDescriptor-shaped node per flat, cylindrical
    # (Y-facing) billboard with a transparent texture. Positions are glTF world
    # space from dagger-import.
    billboard_entities = []
    # Manifest entries are keyed by (archive, record); the exporter may name
    # the PNG file after a hand-authored nickname instead of the numeric slug.
    billboard_sizes = {(t['archive'], t['record']): t for t in billboard_manifest.get('billboards', [])}
    if scene_meta and scene_meta.get("billboards"):
        for index, b in enumerate(scene_meta["billboards"]):
            slug = f"billboard-{b['textureArchive']}-{b['textureRecord']}"
            tex = billboard_sizes.get((b['textureArchive'], b['textureRecord']), {})
            # DFU GetScaledBillboardSize world dims, center-anchored quad.
            billboard_entities.append({
                "id": 1000 + index,
                "name": f"{slug}-{index}",
                "translation": [float(v) for v in b["position"]],
                "sprite": {
                    "asset": f"texture/{slug}",
                    "billboard": "cylindrical",
                    "pivot": [0.5, 0.5],
                    "size": tex.get("worldSize", [1.0, 1.0]),
                    "sizeMode": "world",
                    "shading": "lit",
                    "depth": "default",
                    "visible": True,
                },
            })

    # Enemy directional sprite entities (6595). One sprite per RDB enemy flat;
    # the runtime driver steps the 8-orientation frame from camera bearing.
    # Import normalizes visible frame pixels to one height and bottom-center
    # pivot per enemy. The quad therefore has one fixed size for every frame.
    enemy_entities = []
    enemy_entries = {e["mobileId"]: e for e in enemy_manifest.get("enemies", [])}
    if scene_meta and scene_meta.get("enemies"):
        for index, e in enumerate(scene_meta["enemies"]):
            enemy_entry = enemy_entries.get(e["mobileId"])
            frames = enemy_entry and enemy_entry["frames"]
            if not frames:
                continue  # atlas decode failed at import; warning already emitted
            slug = f"enemy-{e['mobileId']}-atlas"
            normalized_size = next(
                enemy["normalizedSize"]
                for enemy in enemy_manifest.get("enemies", [])
                if enemy["mobileId"] == e["mobileId"]
            )
            enemy_entities.append({
                "id": 2000 + index,
                "name": f"enemy-{e['name'].lower()}-{index}",
                "translation": [float(v) for v in e["position"]],
                "sprite": {
                    "asset": f"texture/{slug}",
                    "frame": 0,
                    "pivot": [0.5, 0.0],
                    "size": normalized_size,
                    "billboard": "cylindrical",
                    "sizeMode": "world",
                    "shading": "lit",
                    "depth": "default",
                    "visible": True,
                },
            })
            corpse = enemy_entry.get("corpse")
            if isinstance(corpse, dict):
                enemy_entities.append({
                    "id": 100000 + 2000 + index,
                    "name": f"corpse-for-{2000 + index}",
                    "translation": [float(v) for v in e["position"]],
                    "sprite": {
                        "asset": f"texture/enemy-{e['mobileId']}-corpse",
                        "frame": 0,
                        "pivot": [0.5, 0.0],
                        "size": corpse["worldSize"],
                        "billboard": "cylindrical",
                        "sizeMode": "world",
                        "shading": "lit",
                        "depth": "default",
                        "visible": False,
                    },
                })

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
        "entities": [player_entity, dungeon_entity] + light_entities + billboard_entities + enemy_entities,
    }


def build_project() -> dict:
    catalog = load_json(IMPORTED / "privateers-hold.catalog.json")
    static_mesh = load_json(IMPORTED / "privateers-hold.static-mesh.json")
    billboard_manifest_path = TEXTURES / "billboard-manifest.json"
    billboard_manifest = load_json(billboard_manifest_path) if billboard_manifest_path.exists() else {}
    enemy_manifest_path = TEXTURES / "enemy-manifest.json"
    enemy_manifest = load_json(enemy_manifest_path) if enemy_manifest_path.exists() else {}
    combat_manifest_path = TEXTURES / "combat-manifest.json"
    combat_manifest = load_json(combat_manifest_path) if combat_manifest_path.exists() else {}
    return {
        "schemaVersion": SCHEMA_VERSION,
        "projectId": PROJECT_ID,
        "name": "Privateer's Hold",
        "entryScene": SCENE_ID,
        "assets": build_assets(catalog, static_mesh, billboard_manifest, enemy_manifest, combat_manifest),
        "itemDefinitions": [],
        "scenes": [build_scene(static_mesh, enemy_manifest, billboard_manifest)],
    }


def build_encounter_gallery(project: dict, enemy_manifest: dict) -> tuple[dict, dict]:
    """Small product scene for inspecting real directional enemy presentation.

    It uses the same generated Daggerfall atlases, Rust runtime, animation
    authority, Engine facade, and browser application host as Privateer's Hold.
    Only the authored environment is intentionally simple.
    """
    mobile_ids = [0, 15, 1, 0, 15]
    enemy_entries = {e["mobileId"]: e for e in enemy_manifest.get("enemies", [])}
    required_assets = {f"texture/enemy-{mobile_id}-atlas" for mobile_id in mobile_ids}
    required_assets.update(
        f"texture/enemy-{mobile_id}-corpse"
        for mobile_id in mobile_ids
        if isinstance(enemy_entries[mobile_id].get("corpse"), dict)
    )
    required_assets.update(
        asset["id"]
        for asset in project["assets"]
        if asset["id"].startswith(("texture/weapon-", "texture/effect-"))
    )
    default_material = next(asset for asset in project["assets"] if asset["id"] == "material/default")
    assets = [default_material] + [asset for asset in project["assets"] if asset["id"] in required_assets]
    gallery_floor = {
        "id": MESH_ASSET,
        "catalog": {
            "version": 1,
            "hash": hashlib.sha256(b"rusty-dagger-encounter-gallery-floor-v2").hexdigest(),
            "sourcePath": None,
            "label": "encounter-gallery-floor",
            "dependencies": [{"id": "material/default", "version": {"req": "exact", "value": 1}, "hash": default_material["catalog"]["hash"]}],
        },
        "staticMesh": {
            "asset": MESH_ASSET,
            "payload": {
                "layout": {"vertexCount": 4, "indexCount": 6, "indexWidth": "u32", "attributes": [{"name": "position", "components": 3, "kind": "f32"}, {"name": "normal", "components": 3, "kind": "f32"}, {"name": "uv", "components": 2, "kind": "f32"}]},
                "groups": [{"materialSlot": 0, "start": 0, "count": 6}],
                "bounds": {"min": [-32.0, 0.0, -32.0], "max": [32.0, 0.0, 32.0]},
                "source": {"kind": "inline", "positions": [-32.0, 0.0, -32.0, 32.0, 0.0, -32.0, 32.0, 0.0, 32.0, -32.0, 0.0, 32.0], "normals": [0.0, 1.0, 0.0] * 4, "uvs": [0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0], "indices": [0, 2, 1, 0, 3, 2]},
                "provenance": "generated",
            },
            "materialSlots": [{"slot": 0, "material": "material/default"}],
            "collision": {"kind": "trimesh"},
        },
    }
    assets.append(gallery_floor)
    primitives = [
        {"id": 11, "name": "gallery-back-wall", "translation": [0.0, 2.5, -12.0], "scale": [16.5, 5.0, 0.25], "primitive": {"geometry": "cube", "color": [0.26, 0.29, 0.34, 1.0]}},
        {"id": 12, "name": "gallery-left-wall", "translation": [-8.0, 2.5, -2.0], "scale": [0.25, 5.0, 20.0], "primitive": {"geometry": "cube", "color": [0.22, 0.25, 0.3, 1.0]}},
        {"id": 13, "name": "gallery-right-wall", "translation": [8.0, 2.5, -2.0], "scale": [0.25, 5.0, 20.0], "primitive": {"geometry": "cube", "color": [0.22, 0.25, 0.3, 1.0]}},
    ]
    player = {
        "id": 1,
        "name": "player",
        "translation": [0.0, 0.35, 4.0],
        "collision": {"enabled": True, "staticCollider": False},
        "kinematic": {"halfExtents": [0.25, 0.25, 0.25], "velocity": [0.0, 0.0, 0.0]},
        "playerController": {
            "moveSpeedUnitsPerSecond": 4.0,
            "moveStepSeconds": 0.1,
            "lookDegreesPerUnit": 12.0,
            "initialYawDegrees": 0.0,
            "initialPitchDegrees": 0.0,
            "fallSpeedUnitsPerSecond": PLAYER_FALL_SPEED,
            "stepUpUnits": PLAYER_STEP_UP,
            "bindings": {"moveForward": "KeyW", "moveBackward": "KeyS", "moveLeft": "KeyA", "moveRight": "KeyD", "mouseLook": "pointer", "primaryFire": "Mouse0"},
        },
    }
    enemies = []
    for index, mobile_id in enumerate(mobile_ids):
        enemy_entry = enemy_entries[mobile_id]
        size = enemy_entry["normalizedSize"]
        handle = 2000 + index
        position = [-5.0 + index * 2.5, 0.0, -7.75]
        enemies.append({
            "id": handle,
            "name": f"gallery-enemy-{mobile_id}-{index}",
            "translation": position,
            "sprite": {"asset": f"texture/enemy-{mobile_id}-atlas", "frame": 0, "pivot": [0.5, 0.0], "size": size, "billboard": "cylindrical", "sizeMode": "world", "shading": "lit", "depth": "default", "visible": True},
        })
        corpse = enemy_entry.get("corpse")
        if isinstance(corpse, dict):
            enemies.append({
                "id": 100000 + handle,
                "name": f"corpse-for-{handle}",
                "translation": position,
                "sprite": {"asset": f"texture/enemy-{mobile_id}-corpse", "frame": 0, "pivot": [0.5, 0.0], "size": corpse["worldSize"], "billboard": "cylindrical", "sizeMode": "world", "shading": "lit", "depth": "default", "visible": False},
            })
    scene = {
        "id": "scene/encounter-gallery",
        "name": "Directional Sprite Encounter Gallery",
        "entities": [player, {"id": 10, "name": "gallery-floor", "translation": [0.0, 0.0, 0.0], "collision": {"enabled": True, "staticCollider": True}, "bounds": {"min": [-32.0, 0.0, -32.0], "max": [32.0, 0.0, 32.0]}, "renderable": {"asset": MESH_ASSET, "visible": True}}] + primitives + enemies,
    }
    gallery = {"schemaVersion": SCHEMA_VERSION, "projectId": "rusty-dagger-encounter-gallery", "name": "Encounter Gallery", "entryScene": scene["id"], "assets": assets, "itemDefinitions": [], "scenes": [scene]}
    navgrid = {"cellSize": 0.5, "cells": [[x, -16, 0, 0.0] for x in range(-14, 15)]}
    return gallery, navgrid


def main() -> None:
    mode = sys.argv[1] if len(sys.argv) > 1 else "--write"
    project = build_project()
    enemy_manifest = load_json(TEXTURES / "enemy-manifest.json")
    gallery, gallery_navgrid = build_encounter_gallery(project, enemy_manifest)
    # Compact separators: deterministic regenerated artifact, and the studio
    # adapter rejects project docs over 8 MiB (the multi-level collision
    # proxy pushes the pretty-printed form past that bound).
    text = json.dumps(project, separators=(",", ":")) + "\n"
    gallery_text = json.dumps(gallery, separators=(",", ":")) + "\n"
    gallery_nav_text = json.dumps(gallery_navgrid, separators=(",", ":")) + "\n"
    if mode == "--check":
        stale = [path for path, expected in [(OUT, text), (GALLERY_OUT, gallery_text), (GALLERY_NAV_OUT, gallery_nav_text)] if not path.exists() or path.read_text() != expected]
        if stale:
            raise SystemExit(f"{', '.join(map(str, stale))} stale; run scripts/generate-project.py --write")
        print(f"project documents up to date ({len(project['assets'])} dungeon assets, {len(gallery['assets'])} gallery assets)")
        return
    if mode != "--write":
        raise SystemExit(__doc__)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text)
    GALLERY_OUT.write_text(gallery_text)
    GALLERY_NAV_OUT.write_text(gallery_nav_text)
    digest = hashlib.sha256(text.encode()).hexdigest()[:16]
    print(f"wrote {OUT} ({len(text)} bytes, sha256:{digest}, assets={len(project['assets'])})")
    print(f"wrote {GALLERY_OUT} ({len(gallery_text)} bytes, assets={len(gallery['assets'])})")


if __name__ == "__main__":
    main()
