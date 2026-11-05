#!/usr/bin/env python3
"""Writes each weapon model's lightning: where an enchanted weapon's arcs crawl over it (authoring/docs/weapons.md).

usage: arcs.py [NAME ...]

Reads the committed models in content/worldrpg/first-person/weapons and writes NAME.arcs.json beside each. All of it works
on the model's metal, or all of the model when it has no metal slot; on a bow only the middle, which the draw barely bends,
and on a flail its ball, in the joint's own space, as the ball swings free. It holds:

- `paths`: arcs that walk the surface in short steps. A step that finds no surface has gone over an edge, and the arc
  carries on into the air as a tendril, as the classic enchanted frames' do. Each arc forks as it goes. A path is a polyline
  and its `group` the arc it belongs to, which the game shows together.
- `sparks`: short dashes in the air around the model, which the game shows during a swing.
- `hull`: the surface welded, its normals smoothed across edges, which the game draws grown behind the weapon as its outline.
- `joint`: the model's joint all of it rides, where it rides one.
"""
import json
import math
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[3]
MODELS = ROOT / "content/worldrpg/first-person/weapons"
SPEC = ROOT / "authoring/specs/weapons/weapons.json"
# Arcs per square metre of metal, and at least and at most this many.
ARCS_PER_AREA = 900
ARCS = (24, 96)
# A step along an arc, how far one bends at each step, its length in steps, and a fork's chance and length.
STEP = .008
WANDER = .8
TRUNK_STEPS = (9, 22)
FORK_CHANCE = .42
FORK_STEPS = (2, 6)
# How far from the surface a step still counts as on it, how far a tendril runs into the air, and how it curls.
ON_SURFACE = .006
TENDRIL_STEPS = (2, 6)
TENDRIL_WANDER = 1.1
# How high above the surface an arc runs.
LIFT = .003
# Sparks per arc, how far they fly from the surface, and how long a dash is.
SPARKS_PER_ARC = .6
SPARK_FLIGHT = (.015, .09)
SPARK_LENGTH = .007
# How far apart hull vertices can be and still weld, and how much of a bow's length from the grip it and the arcs cover.
WELD = .0005
# How thick a slice along the model's length is, how near its ends a vertex leans past them, and how far the lean outweighs the normal.
SLICE = .01
END = .02
LEAN = 2.5
BOW_MIDDLE = .24


def read_glb(path):
    data = path.read_bytes()
    length = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + length])
    return gltf, data[20 + length + 8:]


def accessor(gltf, blob, index):
    item = gltf["accessors"][index]
    view = gltf["bufferViews"][item["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[item["type"]]
    kind = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}[item["componentType"]]
    offset = view.get("byteOffset", 0) + item.get("byteOffset", 0)
    return np.frombuffer(blob, kind, item["count"] * width, offset).reshape(item["count"], width)


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], float).reshape(4, 4).T
    x, y, z, w = node.get("rotation", (0, 0, 0, 1))
    turn = np.array(((1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
                     (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
                     (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y))))
    matrix = np.eye(4)
    matrix[:3, :3] = turn * np.array(node.get("scale", (1, 1, 1)))
    matrix[:3, 3] = node.get("translation", (0, 0, 0))
    return matrix


def inverse_bind(gltf, blob, joint_name):
    """The inverse bind matrix of the skinned joint `joint_name`: from the model's space into the joint's."""
    skin = gltf["skins"][0]
    index = next(i for i, joint in enumerate(skin["joints"]) if gltf["nodes"][joint].get("name") == joint_name)
    item = gltf["accessors"][skin["inverseBindMatrices"]]
    view = gltf["bufferViews"][item["bufferView"]]
    offset = view.get("byteOffset", 0) + item.get("byteOffset", 0) + index * 64
    return np.frombuffer(blob, np.float32, 16, offset).reshape(4, 4).T.astype(float), index


def triangles(path, joint=None):
    """
    The model's triangles (N, 3, 3): its metal's, or all of them with no metal slot, in its own space; with `joint`, those
    the joint carries, in the joint's space.
    """
    gltf, blob = read_glb(path)
    bind, joint_index = inverse_bind(gltf, blob, joint) if joint else (None, None)
    names = [m.get("name") for m in gltf.get("materials", [])]
    found = {True: [], False: []}

    def walk(index, parent):
        node = gltf["nodes"][index]
        placed = parent @ node_matrix(node)
        if "mesh" in node:
            for primitive in gltf["meshes"][node["mesh"]]["primitives"]:
                points = accessor(gltf, blob, primitive["attributes"]["POSITION"]).astype(float)
                # A skinned mesh's vertices are already in the model's space.
                points = points if "skin" in node else points @ placed[:3, :3].T + placed[:3, 3]
                corners = accessor(gltf, blob, primitive["indices"]).reshape(-1, 3)
                metal = names[primitive.get("material", 0)] == "metal" if names else False
                if joint:
                    bones = accessor(gltf, blob, primitive["attributes"]["JOINTS_0"]).astype(int)
                    shares = accessor(gltf, blob, primitive["attributes"]["WEIGHTS_0"]).astype(float)
                    carried = (shares * (bones == joint_index)).sum(axis=1) > .5
                    corners = corners[carried[corners].all(axis=1)]
                    points = points @ bind[:3, :3].T + bind[:3, 3]
                found[metal].append(points[corners])
        for child in node.get("children", []):
            walk(child, placed)

    for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
        walk(root, np.eye(4))
    chosen = [part for part in found[True] if len(part)] or [part for part in found[False] if len(part)]
    return np.concatenate(chosen)


def outward(points, normals):
    """
    `normals` turned out from the model's length. A flat blade's normals lie across its thin side, so growing along them
    wouldn't widen its outline: each vertex also leans from the middle of its slice along the length, and past its ends.
    """
    slices = np.round(points[:, 2] / SLICE).astype(int)
    lean = np.zeros_like(points)
    for index in np.unique(slices):
        chosen = slices == index
        middle = (points[chosen].min(axis=0) + points[chosen].max(axis=0)) / 2
        lean[chosen, :2] = (points[chosen] - middle)[:, :2]
    lean /= np.maximum(np.linalg.norm(lean, axis=1, keepdims=True), 1e-9)
    ends = np.clip(1 - (np.minimum(points[:, 2] - points[:, 2].min(), points[:, 2].max() - points[:, 2]) / END), 0, 1)
    lean[:, 2] = ends * np.sign(points[:, 2] - points[:, 2].mean())
    leaned = normals + lean * LEAN
    return leaned / np.maximum(np.linalg.norm(leaned, axis=1, keepdims=True), 1e-9)


def hull(tris):
    """The triangles welded, as flat lists of points, outward normals and corner indices."""
    keys = np.round(tris.reshape(-1, 3) / WELD).astype(np.int64)
    _, first, inverse = np.unique(keys, axis=0, return_index=True, return_inverse=True)
    points = tris.reshape(-1, 3)[first]
    corners = inverse.reshape(-1, 3)
    corners = corners[(corners[:, 0] != corners[:, 1]) & (corners[:, 1] != corners[:, 2]) & (corners[:, 0] != corners[:, 2])]
    sides = np.cross(points[corners[:, 1]] - points[corners[:, 0]], points[corners[:, 2]] - points[corners[:, 0]])
    normals = np.zeros_like(points)
    for corner in range(3):
        np.add.at(normals, corners[:, corner], sides)
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
    normals = outward(points, normals)
    return {"points": [round(float(v), 4) for v in points.ravel()], "normals": [round(float(v), 3) for v in normals.ravel()],
            "corners": [int(v) for v in corners.ravel()]}


def nearest_on(tris, point):
    """The nearest point of `tris` to `point`, and that triangle's unit normal: its corners and its projection, clamped in."""
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    ab, ac = b - a, c - a
    normal = np.cross(ab, ac)
    area = np.maximum(np.sum(normal * normal, axis=1), 1e-20)
    off = point - a
    v = np.sum(np.cross(off, ac) * normal, axis=1) / area
    w = np.sum(np.cross(ab, off) * normal, axis=1) / area
    v, w = np.clip(v, 0, 1), np.clip(w, 0, 1)
    over = v + w > 1
    v[over], w[over] = v[over] / (v + w)[over], w[over] / (v + w)[over]
    near = a + ab * v[:, None] + ac * w[:, None]
    gap = np.sum((near - point) ** 2, axis=1)
    best = int(np.argmin(gap))
    return near[best], normal[best] / math.sqrt(area[best]), math.sqrt(gap[best])


def turned(direction, normal, angle):
    """`direction` turned `angle` about `normal`, kept in the surface."""
    flat = direction - normal * direction.dot(normal)
    flat /= max(np.linalg.norm(flat), 1e-9)
    return flat * math.cos(angle) + np.cross(normal, flat) * math.sin(angle)


def walk(tris, start, direction, steps, random, group, paths, fork=True):
    """One path from `start`: on the surface while there is one, and off the edge as a tendril when there isn't."""
    point, normal, _ = nearest_on(tris, start)
    path = [point + normal * LIFT]
    air = 0
    for step in range(steps):
        wander = TENDRIL_WANDER if air else WANDER
        direction = turned(direction, normal, random.normal(0, wander))
        ahead = path[-1] + direction * STEP
        if not air:
            near, seen, gap = nearest_on(tris, ahead)
            if gap > ON_SURFACE:
                air = int(random.integers(*TENDRIL_STEPS))
            else:
                normal = seen
                ahead = near + seen * LIFT
        path.append(ahead)
        if air:
            air -= 1
            if air == 0:
                break
        elif fork and random.random() < FORK_CHANCE:
            walk(tris, ahead, turned(direction, normal, random.choice((-1, 1)) * random.uniform(.6, 1.3)),
                 int(random.integers(*FORK_STEPS)), random, group, paths, fork=False)
    if len(path) > 1:
        paths.append({"group": group, "points": [[round(float(v), 4) for v in p] for p in path]})


def lightning(path, joint, middle):
    tris = triangles(path, joint)
    if middle:
        reach = np.abs(tris[..., 2]).max() * middle
        tris = tris[(np.abs(tris[..., 2]) < reach).all(axis=1)]
    sides = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    areas = np.linalg.norm(sides, axis=1) / 2
    random = np.random.default_rng(sum(path.stem.encode()))
    count = int(min(max(areas.sum() * ARCS_PER_AREA, ARCS[0]), ARCS[1]))
    spots = random.choice(len(tris), count, p=areas / areas.sum())
    paths = []
    for group, index in enumerate(spots):
        walk(tris, random.dirichlet((1, 1, 1)) @ tris[index], random.normal(size=3), int(random.integers(*TRUNK_STEPS)), random, group, paths)
    sparks = []
    for index in random.choice(len(tris), int(count * SPARKS_PER_ARC), p=areas / areas.sum()):
        away = random.normal(size=3)
        at = random.dirichlet((1, 1, 1)) @ tris[index] + away / np.linalg.norm(away) * random.uniform(*SPARK_FLIGHT)
        dash = random.normal(size=3)
        dash *= SPARK_LENGTH / np.linalg.norm(dash)
        sparks.append({"group": len(sparks), "points": [[round(float(v), 4) for v in p] for p in (at, at + dash)]})
    return {"paths": paths, "sparks": sparks, "hull": hull(tris)}


def main():
    spec = json.loads(SPEC.read_text())["weapons"]
    names = sys.argv[1:] or [p.stem for p in sorted(MODELS.glob("*.glb"))]
    for name in names:
        entry = spec.get(name, {}) if isinstance(spec, dict) else next((w for w in spec if w.get("name") == name), {})
        if entry.get("family") == "shield" or name == "arrow":
            continue
        joint = "ball" if name == "flail" else None
        found = lightning(MODELS / f"{name}.glb", joint, BOW_MIDDLE if "bow" in name else 0)
        if joint:
            found["joint"] = joint
        out = MODELS / f"{name}.arcs.json"
        out.write_text(json.dumps(found, separators=(",", ":")) + "\n")
        print(name, len({p["group"] for p in found["paths"]}), "arcs,", len(found["sparks"]), "sparks,", len(found["hull"]["corners"]) // 3, "hull tris,", out.stat().st_size // 1024, "KB")


if __name__ == "__main__":
    main()
