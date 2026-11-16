#!/usr/bin/env python3
"""Build the hedges' remodels (art pack `models`): the classic's own faces as a stack of shells, so the ground cover's
effect gives them depth.

Each classic model's faces, from the import's published mesh in its own frame, are drawn again in LAYERS layers, the
outermost on the classic's faces and each lower one inset a step deeper, down to the stack's core DEPTH inside. A layer
draws its classic texture's relief (`groundCover.relief`, whose alpha is each texel's height) cut where the texel falls short
of it, the core all of it, so leaves stand out of the hedge in pixels and the remodel stays inside the classic.

- The inset is mitred: every face moves in by the layer's depth along its own normal, and each corner by the offset that
  keeps all its faces' planes, so the faces stay joined at every edge and a box shrinks to a smaller box.
- Each corner's uv is its face's own uv mapping at the inset point, so a texel's column stands square to the face.
- A layer is a material of its own, named for its classic texture `texture-A-R` with `"shell": {"layer": K, "layers": N}`
  in its extras: model-mesh writes it as the slot that draws that texture's shell layer K of N, layer 0 the opaque core.

Writes local/art/generated/models/ID.glb for each model; promote them with `promote.py --area models --key ID`.

    hedges.py [ID ...]
"""
import json, struct, sys
from pathlib import Path

import numpy as np

REPOSITORY = Path(__file__).resolve().parents[3]
CLASSIC = REPOSITORY / "local/build/import/staging/worldrpg/imports/shared/geometry"
FINALS = REPOSITORY / "local/art/generated/models"

LAYERS = 9
# How deep the stack reaches into the hedge, in metres: its leaves about eight texels of 33-0.
DEPTH = .12
# The hedges, which all wear 33-0. The low walls 517, 518 and 520 that #10844 grouped with them stay classic.
MODELS = [744, 745, 746, 747, 748, 749, 750, 751, 752, 753, 754, 905, 735, 736, 737]
# Points nearer than this are one corner: the classic stores each face's corners apart.
WELD = 1e-4


def read_classic(model):
    """The classic's triangles: corners, their uvs and each triangle's classic texture, from its published static mesh."""
    data = (CLASSIC / f"mesh-{model}.rstatmsh").read_bytes()
    if data[:8] != b"RSTATMSH":
        raise SystemExit(f"{model}: not a static mesh")
    length = struct.unpack_from("<I", data, 8)[0]
    header = json.loads(data[12:12 + length])
    payload, base = header["payload"], 12 + length
    source, count = payload["source"], payload["layout"]["vertexCount"]

    def floats(offset, components):
        return np.frombuffer(data, "<f4", count * components, base + offset).reshape(count, components).astype(np.float64)

    positions, uvs = floats(source["positionsByteOffset"], 3), floats(source["uvsByteOffset"], 2)
    indices = np.frombuffer(data, "<u4", payload["layout"]["indexCount"], base + source["indicesByteOffset"]).reshape(-1, 3)
    materials = {slot["slot"]: slot["material"] for slot in header["materialSlots"]}
    texture = np.empty(len(indices), object)
    for group in payload["groups"]:
        texture[group["start"] // 3:(group["start"] + group["count"]) // 3] = materials[group["materialSlot"]].removeprefix("material/")
    return positions[indices], uvs[indices], texture


def offsets(corners, normals):
    """
    Each corner's mitred inset per metre of depth: the offset x with n·x = -1 for every face plane through it, and, where
    the corner stands on an open edge (the classic leaves its bottom and some ends open), c·x = 0 for the opening's plane,
    so a sloping face's inset never slides out past it. The classics are prisms along their axes, so an opening's plane is
    the axis plane nearest the open edge's outward direction: the ground under an open bottom, the end of an open end.
    """
    points = corners.reshape(-1, 3)
    welded, ids = np.unique(np.round(points / WELD).astype(np.int64), axis=0, return_inverse=True)
    ids = ids.reshape(-1)
    openings = open_edges(corners, normals)
    result = np.zeros((len(welded), 3))
    for corner in range(len(welded)):
        at = points[ids == corner][0]
        # The faces through the corner: those it is a corner of, and those whose edge it lies on (a T-junction).
        planes = []
        for (a, b, c), n in zip(corners, normals):
            if abs(np.dot(at - a, n)) > WELD * 10:
                continue
            u, v = b - a, c - a
            d = at - a
            m = np.array([[u @ u, u @ v], [u @ v, v @ v]])
            s, t = np.linalg.solve(m, [d @ u, d @ v])
            if s >= -1e-4 and t >= -1e-4 and s + t <= 1 + 1e-4 and not any(n @ p > .999 for p in planes):
                planes.append(n)
        planes = np.array(planes)
        if (planes @ planes.T < -.9).any():
            raise SystemExit(f"a corner at {at.round(3)} lies on faces facing each other, so it has no inside to move into")
        caps = np.eye(3)[sorted({int(np.argmax(np.abs(out))) for a, b, out in openings if on_segment(at, a, b)})]
        rows = np.vstack([planes, caps])
        result[corner] = -np.linalg.lstsq(rows, np.r_[np.ones(len(planes)), np.zeros(len(caps))], rcond=None)[0]
    return result[ids].reshape(corners.shape)


def on_segment(point, a, b):
    along = b - a
    s = np.clip((point - a) @ along / (along @ along), 0, 1)
    return np.linalg.norm(a + s * along - point) < WELD * 10


def open_edges(corners, normals):
    """Every triangle edge no other triangle's edge runs along, with its outward direction in its face's plane."""
    edges = [(tri[i], tri[(i + 1) % 3], tri[(i + 2) % 3], n) for tri, n in zip(corners, normals) for i in range(3)]
    found = []
    for index, (a, b, opposite, n) in enumerate(edges):
        along = (b - a) / np.linalg.norm(b - a)
        covered = False
        for other, (c, d, _, _) in enumerate(edges):
            if other == index or not (on_line(c, a, along) and on_line(d, a, along)):
                continue
            s, t = sorted(((c - a) @ along, (d - a) @ along))
            if t > 1e-4 and s < np.linalg.norm(b - a) - 1e-4:
                covered = True
                break
        if not covered:
            out = np.cross(along, n)
            found.append((a, b, out if out @ (a - opposite) > 0 else -out))
    return found


def on_line(point, origin, along):
    offset = point - origin
    return np.linalg.norm(offset - (offset @ along) * along) < WELD * 10


def layered(model):
    """The stack: per layer, its classic texture, its layer and its triangles' corners, normals and uvs."""
    corners, uvs, textures = read_classic(model)
    edges = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    area = np.linalg.norm(edges, axis=1)
    # The classic fills T-junctions with zero-area triangles, which draw nothing.
    keep = area > 1e-6
    corners, uvs, textures, normals = corners[keep], uvs[keep], textures[keep], edges[keep] / area[keep, None]
    inset = offsets(corners, normals)
    parts = []
    for texture in sorted(set(textures)):
        own = textures == texture
        for layer in range(LAYERS):
            moved = corners[own] + inset[own] * DEPTH * (1 - layer / (LAYERS - 1))
            parts.append((texture, layer, moved, normals[own], face_uvs(corners[own], uvs[own], moved)))
    return parts


def face_uvs(corners, uvs, points):
    """Each point's uv by its own triangle's uv mapping, taken at the point's place in the triangle's plane."""
    out = np.empty(points.shape[:2] + (2,))
    for i, ((a, b, c), (ua, ub, uc)) in enumerate(zip(corners, uvs)):
        basis = np.stack([b - a, c - a], 1)
        for j, p in enumerate(points[i]):
            s, t = np.linalg.lstsq(basis, p - a, rcond=None)[0]
            out[i, j] = ua + s * (ub - ua) + t * (uc - ua)
    return out


def glb(parts):
    """A binary glTF of one mesh, a primitive per part's material, which names its classic texture and states its layer, each
    triangle's corners apart."""
    blob, views, accessors, primitives, materials = bytearray(), [], [], [], []

    def add(array, target, component=5126, kind="VEC3"):
        while len(blob) % 4:
            blob.append(0)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": array.nbytes, "target": target})
        blob.extend(array.tobytes())
        accessor = {"bufferView": len(views) - 1, "componentType": component, "count": len(array), "type": kind}
        if kind == "VEC3":
            accessor |= {"min": array.min(0).tolist(), "max": array.max(0).tolist()}
        accessors.append(accessor)
        return len(accessors) - 1

    for texture, layer, corners, normals, uvs in parts:
        count = len(corners) * 3
        position = add(corners.reshape(-1, 3).astype("<f4"), 34962)
        normal = add(np.repeat(normals, 3, 0).astype("<f4"), 34962)
        uv = add(uvs.reshape(-1, 2).astype("<f4"), 34962, kind="VEC2")
        index = add(np.arange(count, dtype="<u4"), 34963, 5125, "SCALAR")
        materials.append({"name": texture, "extras": {"shell": {"layer": layer, "layers": LAYERS}},
                          "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1]}})
        primitives.append({"attributes": {"POSITION": position, "NORMAL": normal, "TEXCOORD_0": uv}, "indices": index,
                           "material": len(materials) - 1})
    document = {"asset": {"version": "2.0", "generator": "authoring/tools/hedges/hedges.py"}, "scene": 0, "scenes": [{"nodes": [0]}],
                "nodes": [{"mesh": 0}], "meshes": [{"primitives": primitives}], "materials": materials,
                "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": len(blob)}]}
    text = json.dumps(document, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    while len(blob) % 4:
        blob.append(0)
    return (struct.pack("<III", 0x46546C67, 2, 28 + len(text) + len(blob)) + struct.pack("<II", len(text), 0x4E4F534A) + text
            + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob))


def main():
    FINALS.mkdir(parents=True, exist_ok=True)
    for model in [int(arg) for arg in sys.argv[1:]] or MODELS:
        parts = layered(model)
        (FINALS / f"{model}.glb").write_bytes(glb(parts))
        print(f"{model}: {sum(len(part[2]) for part in parts)} triangles in {len(parts)} layers")


if __name__ == "__main__":
    main()
