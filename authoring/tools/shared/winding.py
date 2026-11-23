"""Inside-out models: faces wound against the way they face, over every model the game ships or builds (#10997).

The renderer culls back faces, a triangle's front being the side its corners run anticlockwise on (glTF's rule, and the
static meshes'). A face wound the wrong way hides its front and shows what lies behind it, so a prop turns inside out.
Two measures per model:
- normals: the share of its faces whose winding disagrees with the normals it carries (their corners' mean normal points
  behind them). Most of a model's faces disagreeing is a model wound against its own normals, as a mirrored or reversed
  build leaves it; a few disagree in many a sound model, where smooth normals round a sharp edge.
- seen back: the share of the faces a viewer outside the model sees that show their back. Sight lines come from all
  round, from level with its foot to overhead (from every side for a piece that hangs), each aimed at a point within
  its bounds, and a face counts as seen back where twice as many lines meet its back as its front.
A refreshed model fails on either; classic ARCH3D geometry on its normals alone, since a room or a dungeon is seen from
inside and shows its back to every viewer outside it. A model whose materials are all double sided draws both sides, so
only its normals fail it.

Scans the art pack's GLBs and static meshes under CONTENT (default content/worldrpg) and, when present, the classic
ARCH3D meshes the import built under IMPORT (default local/build/import), or just the files named. Prints one line per
flagged model (or every model with --all) and exits 1 when any model EXCEPTIONS doesn't name is flagged.

--fix turns, in place, each face of the named GLBs that viewers see from behind (as seen back counts it), and its
normals with it where they are its own and face away from it: a generated mesh's scattered flipped faces. The promote
step refuses a mesh this check flags (promote.py's inside_out), so fix the final, or the generator that built it.

usage: winding.py [--content DIR] [--import DIR] [--all] [--json FILE] [FILE ...]
       winding.py --fix FILE.glb ...
"""
import argparse
import json
import os
import struct
import sys
import zlib
from fnmatch import fnmatch
from multiprocessing import Pool
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "hedges"))
import hedges  # noqa: E402

NORMALS_LIMIT = 0.5    # share of faces wound against their own normals that flags a model
SEEN_BACK_LIMIT = 0.05  # share of a refreshed model's seen faces that show a viewer their back
RAYS = 10000           # sight lines per refreshed model checked
FIX_RAYS = 40000       # sight lines per model fixed, so that most faces are seen

# Models drawn deliberately with faces the measures count, by path pattern (relative to the scanned root), and why.
MODELS = "packs/daggerfall.art-replacements/media/models"
SHELLS = ("the classic's own faces in a shell stack (hedges.py), open where the classic is open (its back, ends and "
          "bottom), so its inner layers show their backs through the openings, as the classic's faces do")
EXCEPTIONS = {
    **{f"{MODELS}/{model}.rstatmsh": SHELLS for model in hedges.MODELS},
    **{f"{MODELS}/{model}.rstatmsh": ("the lid's inner staves stand just outside its gable ends and seams, facing in; "
                                      "culled, the lid's outer faces behind them draw the chest whole")
       for model in (41811, 41812, 41813, 41821)},
    f"{MODELS}/41214.rstatmsh": ("the handcart, turned by --fix: what still counts is its bed's underside, seen only from "
                                 "beneath the cart, and edge slivers seen about as often from either side"),
}

COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    """A GLB's document, its binary chunk (writable) and where that chunk starts in the file."""
    data = bytearray(path.read_bytes())
    magic, _, length = struct.unpack_from("<4sII", data)
    if magic != b"glTF":
        raise ValueError(f"{path} is not a GLB")
    at, doc, binary = 12, None, (0, 0)
    while at < length:
        size, kind = struct.unpack_from("<I4s", data, at)
        if kind == b"JSON":
            doc = json.loads(data[at + 8:at + 8 + size])
        elif kind == b"BIN\x00":
            binary = (at + 8, size)
        at += 8 + size
    return doc, data, binary


def accessor_view(doc, data, binary, index):
    """An accessor's rows as a strided view into the file's bytes, writable."""
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    dtype = np.dtype(COMPONENTS[a["componentType"]])
    width = WIDTHS[a["type"]]
    start = binary[0] + view.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = view.get("byteStride", 0) or width * dtype.itemsize
    return np.ndarray((a["count"], width), dtype, data, start, (stride, dtype.itemsize))


def glb_primitives(doc, data, binary):
    """Each triangle primitive of a GLB in world space: positions, normals (or None), faces, double sided, and its index
    and normal accessors; and whether its scene hangs (hanging.py's pieces, seen from below too)."""

    def local(node):
        if "matrix" in node:
            return np.array(node["matrix"], float).reshape(4, 4).T
        x, y, z, w = node.get("rotation", (0, 0, 0, 1))
        rotation = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                             [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                             [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        matrix = np.eye(4)
        matrix[:3, :3] = rotation * np.array(node.get("scale", (1, 1, 1)))
        matrix[:3, 3] = node.get("translation", (0, 0, 0))
        return matrix

    out = []

    def visit(index, parent):
        node = doc["nodes"][index]
        world = parent @ local(node)
        if "mesh" in node:
            linear = world[:3, :3]
            mirrored = np.linalg.det(linear) < 0
            for primitive in doc["meshes"][node["mesh"]]["primitives"]:
                if primitive.get("mode", 4) != 4:
                    continue
                attributes = primitive["attributes"]
                positions = accessor_view(doc, data, binary, attributes["POSITION"]).astype(float) @ linear.T + world[:3, 3]
                normals = None
                if "NORMAL" in attributes:
                    normals = accessor_view(doc, data, binary, attributes["NORMAL"]).astype(float) @ np.linalg.inv(linear)
                faces = (accessor_view(doc, data, binary, primitive["indices"]).astype(np.int64).reshape(-1, 3) if "indices" in primitive
                         else np.arange(len(positions)).reshape(-1, 3))
                if mirrored:
                    faces = faces[:, ::-1]
                material = doc.get("materials", [])[primitive["material"]] if "material" in primitive else {}
                out.append({"positions": positions, "normals": normals, "faces": faces,
                            "double": bool(material.get("doubleSided", False)), "mirrored": mirrored,
                            "indices": primitive.get("indices"), "normal": attributes.get("NORMAL")})
        for child in node.get("children", []):
            visit(child, world)

    scene = doc.get("scenes", [{"nodes": range(len(doc.get("nodes", [])))}])[doc.get("scene", 0)]
    for root in scene["nodes"]:
        visit(root, np.eye(4))
    return out, "hangs" in scene.get("extras", {})


def static_mesh(path):
    """An Engine static mesh (RSTATMSH) as one primitive, drawn one sided."""
    data = path.read_bytes()
    if data[:8] != b"RSTATMSH":
        raise ValueError(f"{path} is not a static mesh")
    (size,) = struct.unpack_from("<I", data, 8)
    payload = json.loads(data[12:12 + size])["payload"]
    resource = data[12 + size:]
    layout, source = payload["layout"], payload["source"]
    vertices, indices = layout["vertexCount"], layout["indexCount"]
    positions = np.frombuffer(resource, np.float32, vertices * 3, source["positionsByteOffset"]).reshape(-1, 3)
    normals = np.frombuffer(resource, np.float32, vertices * 3, source["normalsByteOffset"]).reshape(-1, 3)
    faces = np.frombuffer(resource, np.uint32, indices, source["indicesByteOffset"]).reshape(-1, 3)
    return [{"positions": positions.astype(float), "normals": normals.astype(float), "faces": faces.astype(np.int64), "double": False}]


def load(path):
    if path.suffix == ".glb":
        return glb_primitives(*read_glb(path))
    return static_mesh(path), False


def sight(corners, unit, rng, rays, hangs):
    """For each face, how many sight lines from outside meet it first from its front and from its back."""
    flat = corners.reshape(-1, 3)
    low, high = flat.min(axis=0), flat.max(axis=0)
    centre, radius = (low + high) / 2, max(np.linalg.norm(high - low) / 2, 1e-6)
    azimuth = rng.uniform(0, 2 * np.pi, rays)
    elevation = np.arcsin(rng.uniform(-1 if hangs else 0, 1, rays))
    toward = low + rng.uniform(0, 1, (rays, 3)) * (high - low)
    origin = centre + 2 * radius * np.stack([np.cos(elevation) * np.sin(azimuth), np.sin(elevation), np.cos(elevation) * np.cos(azimuth)], axis=1)
    if not hangs:
        origin[:, 1] = np.maximum(origin[:, 1], low[1])
    direction = toward - origin
    direction /= np.linalg.norm(direction, axis=1, keepdims=True)
    a, edge1, edge2 = corners[:, 0], corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]
    votes = np.zeros((len(corners), 2), int)
    for lines in range(0, rays, 4000):
        o, d = origin[lines:lines + 4000], direction[lines:lines + 4000]
        nearest, first = np.full(len(o), np.inf), np.full(len(o), -1)
        for chunk in range(0, len(corners), 512):
            e1, e2, v0 = edge1[chunk:chunk + 512], edge2[chunk:chunk + 512], a[chunk:chunk + 512]
            p = np.cross(d[:, None], e2[None])
            det = (e1[None] * p).sum(-1)
            ok = np.abs(det) > 1e-18
            inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
            s = o[:, None] - v0[None]
            u = (s * p).sum(-1) * inv
            q = np.cross(s, e1[None])
            v = (d[:, None] * q).sum(-1) * inv
            t = np.where(ok & (u >= 0) & (u <= 1) & (v >= 0) & (u + v <= 1), (e2[None] * q).sum(-1) * inv, np.inf)
            t[t <= 0] = np.inf
            # A face drawn both ways is two faces back to back: at a tie the one facing the viewer is what they see.
            t += ((d @ unit[chunk:chunk + 512].T) > 0) * radius * 1e-5
            best = t.argmin(axis=1)
            distance = t[np.arange(len(o)), best]
            closer = distance < nearest
            nearest[closer], first[closer] = distance[closer], chunk + best[closer]
        seen = first >= 0
        back = (unit[first[seen]] * d[seen]).sum(axis=1) > 0
        np.add.at(votes, (first[seen], back.astype(int)), 1)
    return votes


def backward(votes):
    """The faces most who see them see from behind: twice as many sight lines meet their back as their front. A thin sheet
    seen about as often from either side shows its back to half its viewers whichever way it turns, so it isn't counted."""
    return votes[:, 1] > 2 * votes[:, 0]


def faces_of(parts):
    """Every part's faces in one array, with their corners, unit normals and carried normals' sum; degenerate faces
    marked (False in the returned mask) and left out of the measures."""
    offsets = np.cumsum([0] + [len(part["positions"]) for part in parts])
    positions = np.concatenate([part["positions"] for part in parts])
    faces = np.concatenate([part["faces"] + offset for part, offset in zip(parts, offsets)])
    corners = positions[faces]
    cross = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    area = np.linalg.norm(cross, axis=1)
    sound = area > 1e-12
    unit = cross / np.maximum(area, 1e-300)[:, None]
    carried = None
    if all(part["normals"] is not None for part in parts):
        carried = np.concatenate([part["normals"] for part in parts])[faces].sum(axis=1)
    return corners, unit, carried, sound


def measure(parts, rng, hangs, seen=True):
    """The model's triangles, the share wound against their normals (None without normals) and, if `seen`, the share
    seen back (else None)."""
    corners, unit, carried, sound = faces_of(parts)
    corners, unit = corners[sound], unit[sound]
    if not len(corners):
        return 0, None, None
    flipped = None
    if carried is not None:
        carried = carried[sound]
        has = np.linalg.norm(carried, axis=1) > 1e-6
        flipped = float(((unit * carried).sum(axis=1) < 0)[has].mean()) if has.any() else None
    if not seen:
        return len(corners), flipped, None
    votes = sight(corners, unit, rng, RAYS, hangs)
    shown = votes.sum(axis=1) > 0
    seen_back = float(backward(votes)[shown].mean()) if shown.any() else 0.0
    return len(corners), flipped, seen_back


def exception(name):
    return next((why for pattern, why in EXCEPTIONS.items() if fnmatch(name, pattern)), None)


def scan(model):
    """One model's measures and verdict: model is (path, name as EXCEPTIONS names it, whether it is refreshed art)."""
    path, name, refreshed = model
    parts, hangs = load(path)
    double = bool(parts) and all(part["double"] for part in parts)
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    triangles, flipped, seen_back = measure(parts, rng, hangs, seen=refreshed) if parts else (0, None, None)
    flagged = (flipped or 0) > NORMALS_LIMIT or (not double and (seen_back or 0) > SEEN_BACK_LIMIT)
    return {"model": name, "triangles": triangles, "normals": flipped, "seenBack": seen_back, "doubleSided": double,
            "flagged": flagged, "exception": exception(name) if flagged else None}


def describe(entry):
    normals = "-" if entry["normals"] is None else f"{entry['normals']:.1%}"
    seen = "-" if entry["seenBack"] is None else f"{entry['seenBack']:.1%}"
    return (f"triangles {entry['triangles']}, normals {normals}, seen back {seen}"
            f"{', double sided' if entry['doubleSided'] else ''}")


def fix(path):
    """Turns, in place, each face of a GLB seen back (see the module's --fix)."""
    doc, data, binary = read_glb(path)
    parts, hangs = glb_primitives(doc, data, binary)
    corners, unit, _, sound = faces_of(parts)
    votes = sight(corners, unit, np.random.default_rng(zlib.crc32(path.name.encode())), FIX_RAYS, hangs)
    turn = sound & backward(votes)
    start, turned, normals_turned = 0, 0, 0
    for part in parts:
        count = len(part["faces"])
        mine = np.flatnonzero(turn[start:start + count])
        start += count
        if not len(mine):
            continue
        if part["indices"] is None:
            raise SystemExit(f"{path}: a primitive without indices has faces to turn; index it first")
        indices = accessor_view(doc, data, binary, part["indices"]).reshape(-1, 3)
        indices[mine, 1], indices[mine, 2] = indices[mine, 2].copy(), indices[mine, 1].copy()
        turned += len(mine)
        if part["normal"] is None:
            continue
        # A normal turns with its face where only turned faces use its vertex and it faced away from the turned face.
        faces = indices.astype(np.int64)
        used = np.zeros(len(part["positions"]), bool)
        used[np.delete(faces, mine, axis=0).reshape(-1)] = True
        normals = accessor_view(doc, data, binary, part["normal"])
        sign = -1 if part["mirrored"] else 1
        for face in mine:
            corner = part["positions"][faces[face]]
            winding = sign * np.cross(corner[1] - corner[0], corner[2] - corner[0])
            for vertex in faces[face]:
                if not used[vertex] and (part["normals"][vertex] @ winding) < 0:
                    normals[vertex] *= -1
                    normals_turned += 1
                    used[vertex] = True   # turned once
    path.write_bytes(bytes(data))
    print(f"{path}: turned {turned} of {int(sound.sum())} faces and {normals_turned} normals")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("files", nargs="*", type=Path)
    parser.add_argument("--content", type=Path, default=Path("content/worldrpg"))
    parser.add_argument("--import", dest="imported", type=Path, default=Path("local/build/import"))
    parser.add_argument("--all", action="store_true", help="print every model, not just the flagged ones")
    parser.add_argument("--json", type=Path, help="write every model's measures here")
    parser.add_argument("--fix", action="store_true", help="turn the named GLBs' faces seen from behind, in place")
    args = parser.parse_args()

    if args.fix:
        for path in args.files:
            fix(path)
        return 0
    if args.files:
        models = [(path, str(path), True) for path in args.files]
    else:
        models = [(path, str(path.relative_to(args.content)), True)
                  for path in sorted(args.content.rglob("*")) if path.suffix in (".glb", ".rstatmsh")]
        classic = args.imported / "staging/worldrpg/imports/shared/geometry"
        if classic.exists():
            models += [(path, "import/" + str(path.relative_to(classic)), False) for path in sorted(classic.rglob("*.rstatmsh"))]
        else:
            print(f"no classic import at {classic}: refreshed models only", file=sys.stderr)

    with Pool(min(8, os.cpu_count() or 1)) as pool:
        report = pool.map(scan, models, chunksize=16)
    failed = excused = 0
    for entry in report:
        failed += bool(entry["flagged"] and not entry["exception"])
        excused += bool(entry["exception"])
        if entry["flagged"] or args.all:
            state = "exception: " + entry["exception"] if entry["exception"] else "FLAGGED" if entry["flagged"] else "ok"
            print(f"{entry['model']}  {describe(entry)}  {state}")
    if args.json:
        args.json.write_text(json.dumps(report, indent=1))
    print(f"{len(report)} models: {failed} inside out, {excused} excepted")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
