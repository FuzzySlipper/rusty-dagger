"""Build the hanging roots, chains, cage and lanterns as rigged GLBs that swing when an actor walks through them (#10865).

    blender -b -P authoring/tools/hanging/hanging.py -- [ARCHIVE-RECORD ...]

Each piece stands in for one placed sprite through the art pack's `meshes` (promote.py's WORLD_MESHES, `--area effects
--key A-R`). It is built at its sprite's world size, its origin at the sprite's foot and its anchor at the sprite's top;
the game draws a hanging mesh at its sprite's height from the floor its sprite stands on, so it hangs from the same point
as the sprite, however wide it is. Finals go to local/art/generated/effects/hanging-A-R.glb.

The rig is what the game reads to hang it (DaggerfallHangingRig):
- a joint named `anchor...` follows the placed pose: the point the piece hangs from;
- every other joint with exactly one child (not counting `light`) is a swinging body, a capsule from it to that child;
- a joint with no child is an end, never a body;
- a lantern's `light` joint is where its light sits.
Bodies hang one below another from their anchor; a two-strand chain has two strands below one anchor.

- **Roots** (213-7..10, 100-20, 100-21) are their refreshed art on two crossed planes, bending along five bodies.
- **Chains** (211-4..7) are iron links, each rigid on its own body, gauged and coloured from the refreshed sprite; 211-5
  and 211-6 are two strands from a bar.
- **The cage** (211-34) hangs on a short chain: iron bars, bands and a floor.
- **Lanterns** (210-22/24/25/26) are a chain of small links, skinned to bodies about 0.35 m long, and a modelled
  lantern whose glass wears the refreshed sprite's glass and glows.
"""
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

REPOSITORY = Path(__file__).resolve().parents[3]
PACK = REPOSITORY / "content/worldrpg/packs/daggerfall.art-replacements"
OUT = REPOSITORY / "local/art/generated/effects"

# The import's classic billboards, whose world size (width, height) in metres each piece is built at, as the blocks place it.
DUNGEON_MANIFEST = REPOSITORY / "local/build/import/staging/worldrpg/imports/shared/media/dungeon/manifest.json"
# What each piece hangs as: the game's `hangs`, which each GLB states (its scene's extras) for promotion to carry.
ROOTS = [(213, 7), (213, 8), (213, 9), (213, 10), (100, 20), (100, 21)]
CHAINS = [(211, 4), (211, 5), (211, 6), (211, 7)]
LANTERNS = [(210, 22), (210, 24), (210, 25), (210, 26)]
CAGE = (211, 34)
HANGS = {**{key: "root" for key in ROOTS}, **{key: "chain" for key in CHAINS}, **{key: "lantern" for key in LANTERNS}, CAGE: "cage"}
# The anchor hangs the first body this far below the hang point, so its capsule clears what it hangs from.
DROP = .08
# A root bends along this many bodies.
ROOT_BODIES = 5
# A lantern's chain bends every this many metres.
LANTERN_SEGMENT = .35


def sizes():
    """Each classic billboard's world size, (width, height) in metres, by its texture archive and record."""
    manifest = json.loads(DUNGEON_MANIFEST.read_text())
    return {(b["textureArchive"], b["textureRecord"]): (b["worldSize"]["x"], b["worldSize"]["y"]) for b in manifest["billboards"]}


SIZES = sizes()


def refreshed(key):
    """The refreshed sprite of a record: its first frame as an RGBA array (top row first) and where the classic frame lies in it."""
    payload = json.loads((PACK / "payload.json").read_text())
    entry = next(e for e in payload["artReplacements"]["scenery"] if (e["replaces"]["archive"], e["replaces"]["record"]) == key)
    image = bpy.data.images.load(str(PACK.parent.parent.parent / entry["path"]))
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
    frame = entry["frames"][0]
    x, y, fw, fh = int(frame["x"]), int(frame["y"]), int(frame["width"]), int(frame["height"])
    return image, pixels[y:y + fh, x:x + fw], (x / w, y / h, fw / w, fh / h), entry["classic"]


def palette(pixels):
    """The median colour of a sprite's opaque pixels, as sRGB, and its darker and lighter quarter."""
    rgb = pixels[pixels[..., 3] > .5][:, :3]
    lum = rgb @ np.array([.299, .587, .114])
    order = np.argsort(lum)
    return np.median(rgb, axis=0), rgb[order[len(order) // 4]], rgb[order[3 * len(order) // 4]]


def linear(c):
    return [((v + .055) / 1.055) ** 2.4 if v > .04045 else v / 12.92 for v in c[:3]] + [1]


def material(name, colour, metallic=.7, roughness=.55, emission=None, texture=None, alpha=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = linear(colour)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = roughness
    if texture is not None:
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = texture
        t.interpolation = "Closest"
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
        if alpha:
            m.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
        if emission is not None:
            m.node_tree.links.new(t.outputs["Color"], b.inputs["Emission Color"])
            b.inputs["Emission Strength"].default_value = emission
    elif emission is not None:
        b.inputs["Emission Color"].default_value = linear(colour)
        b.inputs["Emission Strength"].default_value = emission
    m.use_backface_culling = False
    return m


class Rig:
    """Joints top down: each a name, its head and its parent; a bone runs from its head to its first child's head."""

    def __init__(self):
        self.joints = []

    def add(self, name, head, parent=None):
        self.joints.append((name, Vector(head), parent))
        return name

    def build(self, name):
        arm = bpy.data.objects.new(name + "-rig", bpy.data.armatures.new(name + "-rig"))
        bpy.context.scene.collection.objects.link(arm)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="EDIT")
        heads = {n: h for n, h, _ in self.joints}
        children = {n: [c for c, _, p in self.joints if p == n] for n, _, _ in self.joints}
        bones = {}
        for n, h, p in self.joints:
            bone = arm.data.edit_bones.new(n)
            kids = [c for c in children[n] if c != "light"]
            bone.head = h
            # A bone points at its body's child, so its +Y runs down the body; an end or a light points straight down.
            bone.tail = heads[kids[0]] if len(kids) == 1 and (heads[kids[0]] - h).length > 1e-4 else h + Vector((0, 0, -.05))
            if p is not None:
                bone.parent = bones[p]
            bones[n] = bone
        bpy.ops.object.mode_set(mode="OBJECT")
        # The Engine admits a skin only through its animated import, which a GLB takes by having a clip: one that holds rest.
        arm.animation_data_create().action = bpy.data.actions.new("rest")
        for frame in (1, 2):
            arm.pose.bones[self.joints[0][0]].keyframe_insert("location", frame=frame)
        return arm


def skin(obj, arm, weights):
    """weights: per vertex index, {joint: weight}."""
    groups = {}
    for i, joints in enumerate(weights):
        for joint, w in joints.items():
            if w <= 0:
                continue
            if joint not in groups:
                groups[joint] = obj.vertex_groups.new(name=joint)
            groups[joint].add([i], w, "REPLACE")
    obj.modifiers.new("rig", "ARMATURE").object = arm
    obj.parent = arm


def export(key):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"hanging-{key[0]}-{key[1]}.glb"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_animations=True,
                              export_skins=True, export_yup=True)
    patch(path, key)
    print("wrote", path)


def patch(path, key):
    """Blender writes a texture's alpha as a blend; a root's art is cut out at half alpha, as the sprites are. The scene
    states what the piece hangs as, which promotion carries into the art pack."""
    data = path.read_bytes()
    n = int.from_bytes(data[12:16], "little")
    document = json.loads(data[20:20 + n])
    rest = data[20 + n:]
    for m in document.get("materials", []):
        m["doubleSided"] = True
        if m.get("alphaMode") == "BLEND":
            m["alphaMode"], m["alphaCutoff"] = "MASK", .5
    document["scenes"][document.get("scene", 0)].setdefault("extras", {})["hangs"] = HANGS[key]
    text = json.dumps(document, separators=(",", ":")).encode()
    text += b" " * ((4 - len(text) % 4) % 4)
    body = len(text).to_bytes(4, "little") + b"JSON" + text + rest
    path.write_bytes(data[:8] + (12 + len(body)).to_bytes(4, "little") + body)


def new_object(name, bm, materials):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    obj = bpy.data.objects.new(name, mesh)
    for m in materials:
        obj.data.materials.append(m)
    bpy.context.scene.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return obj


def tube(bm, path, wire, sides, closed):
    """A round tube along a path of points; returns its vertices in path order."""
    made = []
    rings = []
    for k, p in enumerate(path):
        nxt = path[(k + 1) % len(path)] if closed or k + 1 < len(path) else p + (p - path[k - 1])
        prv = path[k - 1] if closed or k > 0 else p - (path[1] - p)
        t = (nxt - prv).normalized()
        side = t.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = t.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(t).normalized()
        ring = [bm.verts.new(p + (side * math.cos(2 * math.pi * j / sides) + up * math.sin(2 * math.pi * j / sides)) * wire / 2) for j in range(sides)]
        rings.append(ring)
        made += ring
    for k in range(len(rings) if closed else len(rings) - 1):
        a, c = rings[k], rings[(k + 1) % len(rings)]
        for j in range(sides):
            bm.faces.new([a[j], c[j], c[(j + 1) % sides], a[(j + 1) % sides]])
    return made


def link(bm, top, width, height, wire, turned):
    """An oval link whose inside top is at `top`, in the xz plane or turned a quarter about the vertical."""
    centre = top - Vector((0, 0, height / 2 - wire / 2))
    path = []
    for k in range(14):
        a = 2 * math.pi * k / 14
        dx, dz = (width - wire) / 2 * math.cos(a), (height - wire) / 2 * math.sin(a)
        path.append(centre + (Vector((0, dx, dz)) if turned else Vector((dx, 0, dz))))
    return tube(bm, path, wire, 6, True)


def ring(bm, top, diameter, wire, turned=False):
    return link(bm, top, diameter, diameter, wire, turned)


# --- roots -----------------------------------------------------------------------------------------------------------

def root(key):
    """The refreshed art on two crossed planes, the classic frame's part of it across the sprite's size, bending along
    ROOT_BODIES bodies: each vertex weighs on the body at its height, blending into the next over the lower half."""
    w, h = SIZES[key]
    image, _, (fx, fy, fw, fh), classic = refreshed(key)
    # UVs of the classic frame inside the atlas (v up from the bottom).
    u0, u1 = fx + classic["x"] * fw, fx + (classic["x"] + classic["width"]) * fw
    v1 = 1 - (fy + classic["y"] * fh)
    v0 = 1 - (fy + (classic["y"] + classic["height"]) * fh)
    rows = 16
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    for turn in (0, math.pi / 2):
        cos, sin = math.cos(turn), math.sin(turn)
        grid = [[bm.verts.new(((u - .5) * w * cos, (u - .5) * w * sin, h * (1 - r / rows))) for u in (0, 1)] for r in range(rows + 1)]
        for r in range(rows):
            face = bm.faces.new([grid[r][0], grid[r + 1][0], grid[r + 1][1], grid[r][1]])
            for loop, (u, t) in zip(face.loops, ((0, r), (0, r + 1), (1, r + 1), (1, r))):
                loop[uv].uv = (u0 + u * (u1 - u0), v1 - t / rows * (v1 - v0))
    order = list(bm.verts)
    obj = new_object("root", bm, [material("root", (1, 1, 1), 0, .95, texture=image, alpha=True)])
    rig = Rig()
    rig.add("anchor", (0, 0, h))
    tops = [h - DROP - (h - DROP) * k / ROOT_BODIES for k in range(ROOT_BODIES)]
    names = [rig.add(f"root{k}", (0, 0, z), "anchor" if k == 0 else f"root{k - 1}") for k, z in enumerate(tops)]
    rig.add("end", (0, 0, 0), names[-1])
    arm = rig.build("root")
    span = (h - DROP) / ROOT_BODIES

    def weigh(z):
        if z >= tops[0]:
            return {"anchor": 1.0}
        k = min(ROOT_BODIES - 1, int((tops[0] - z) / span))
        t = (tops[0] - z) / span - k
        nxt = .5 * max(0.0, (t - .5) * 2) if k + 1 < ROOT_BODIES else 0.0
        return {names[k]: 1 - nxt, **({names[k + 1]: nxt} if nxt else {})}

    skin(obj, arm, [weigh(v.co.z) for v in order])
    export(key)


# --- chains ------------------------------------------------------------------------------------------------------------

def strand(bm, rig, owners, x, top, length, gauge, end, parent, prefix, ring_width=0.0):
    """Links from `top` down `length` metres at x, then the end piece (a ring, a cuff, or a ring `ring_width` across
    lying in the sprite's plane); each link rigid on a body of its own."""
    lw, lh, wire = gauge
    pitch = lh - 2 * wire
    end_size = ring_width if end == "wide" else {"ring": lw * 1.35, "cuff": lw * 1.1}[end]
    count = max(2, round((length - end_size) / pitch))
    pitch = (length - end_size) / count
    previous = parent
    for n in range(count):
        z = top - n * pitch
        name = rig.add(f"{prefix}{n:02d}", (x, 0, z), previous)
        for v in link(bm, Vector((x, 0, z)), lw, lh, wire, n % 2 == 1):
            owners[v] = name
        previous = name
    z = top - count * pitch
    name = rig.add(f"{prefix}end", (x, 0, z), previous)
    made = ring(bm, Vector((x, 0, z)), end_size, wire * (1.8 if end == "cuff" else 1.0), count % 2 == 1 and end != "wide")
    for v in made:
        owners[v] = name
    rig.add(f"end.{prefix}", (x, 0, z - end_size), name)


def chain(key):
    w, h = SIZES[key]
    _, pixels, _, _ = refreshed(key)
    mid, dark, light = palette(pixels)
    bm = bmesh.new()
    owners = {}
    rig = Rig()
    rig.add("anchor", (0, 0, h))
    top = h - DROP
    if key in ((211, 4), (211, 7)):
        gauge = (w * .66, w * .82, w * .15)
        strand(bm, rig, owners, 0, top, top, gauge, "wide", "anchor", "a", ring_width=w)
    else:
        # Two strands from a bar bent across the top, on the anchor.
        gauge = (w * .32, w * .4, w * .07)
        x = w / 2 - gauge[0] * .68
        bar = [Vector((-x, 0, top - .02)), Vector((-x, 0, h - .03)), Vector((x, 0, h - .03)), Vector((x, 0, top - .02))]
        for v in tube(bm, bar, gauge[2] * 1.2, 6, False):
            owners[v] = "anchor"
        left, right = (top, top) if key == (211, 5) else (top, top * .62)
        strand(bm, rig, owners, -x, top - gauge[2], left - gauge[2], gauge, "ring", "anchor", "a")
        strand(bm, rig, owners, x, top - gauge[2], right - gauge[2], gauge, "ring" if key == (211, 5) else "cuff", "anchor", "b")
    order = list(bm.verts)
    obj = new_object("chain", bm, [material("iron", mid, .8, .45)])
    arm = rig.build("chain")
    skin(obj, arm, [{owners[v]: 1.0} for v in order])
    export(key)


# --- the cage ------------------------------------------------------------------------------------------------------------

def cage(key):
    """A short chain from a hook, then the cage: a dome of bars to a collar, bands at its shoulder, middle and foot, and
    a floor plate; the bands and floor take the sprite's rust."""
    w, h = SIZES[key]
    _, pixels, _, _ = refreshed(key)
    mid, dark, light = palette(pixels)
    rgb = pixels[pixels[..., 3] > .5][:, :3]
    rust = np.median(rgb[(rgb[:, 0] > rgb[:, 2] * 1.4) & (rgb[:, 0] > .25)], axis=0) if ((rgb[:, 0] > rgb[:, 2] * 1.4) & (rgb[:, 0] > .25)).sum() > 20 else np.array([.45, .2, .1])
    bm = bmesh.new()
    owners = {}
    rig = Rig()
    rig.add("anchor", (0, 0, h))
    radius = w / 2 - .03
    body = h * .62
    chain_len = h - body - DROP
    strand_top = h - DROP
    gauge = (.11, .14, .025)
    pitch = gauge[1] - 2 * gauge[2]
    count = max(2, round(chain_len / pitch))
    pitch = chain_len / count
    previous = "anchor"
    for n in range(count):
        z = strand_top - n * pitch
        name = rig.add(f"c{n:02d}", (0, 0, z), previous)
        for v in link(bm, Vector((0, 0, z)), gauge[0], gauge[1], gauge[2], n % 2 == 1):
            owners[v] = name
        previous = name
    top = strand_top - count * pitch
    rig.add("cage", (0, 0, top), previous)
    rig.add("end.cage", (0, 0, top - body), "cage")
    rust_faces = []
    made = ring(bm, Vector((0, 0, top)), .14, .03)            # the collar ring the chain holds
    shoulder, foot = top - body * .28, top - body + .06
    bars = 10
    for k in range(bars):
        a = 2 * math.pi * k / bars
        out = Vector((math.cos(a), math.sin(a), 0))
        # The dome curves out from the collar to the shoulder, then the bar runs down to the foot, drawing in a little.
        path = [out * (.06 + (radius - .06) * math.sin(s * math.pi / 2)) + Vector((0, 0, shoulder + (top - .12 - shoulder) * math.cos(s * math.pi / 2)))
                for s in np.linspace(0, 1, 6)]
        path += [out * radius * (1 - .08 * s) + Vector((0, 0, shoulder - (shoulder - foot) * s)) for s in np.linspace(0, 1, 5)[1:]]
        made += tube(bm, path, .035, 5, False)
    for z, r in ((shoulder, radius * 1.0), (shoulder - (shoulder - foot) / 2, radius * .97), (foot, radius * .92)):
        before = set(bm.faces)
        made += tube(bm, [Vector((r * math.cos(2 * math.pi * k / 24), r * math.sin(2 * math.pi * k / 24), z)) for k in range(24)], .06, 5, True)
        rust_faces += [f for f in bm.faces if f not in before]
    floor = bmesh.ops.create_circle(bm, cap_ends=True, segments=16, radius=radius * .9)["verts"]
    before = set(bm.faces)
    for v in floor:
        v.co.z = foot - .03
    made += floor
    rust_faces += [f for f in bm.faces if f not in before]
    for v in made:
        owners[v] = "cage"
    for f in rust_faces:
        f.material_index = 1
    order = list(bm.verts)
    obj = new_object("cage", bm, [material("iron", dark, .8, .5), material("rust", rust, .3, .8)])
    arm = rig.build("cage")
    skin(obj, arm, [{owners[v]: 1.0} for v in order])
    export(key)


# --- lanterns -------------------------------------------------------------------------------------------------------------

def lantern(key):
    """A chain of small links skinned to bodies LANTERN_SEGMENT apart, then the lantern where the sprite paints it:
    its glass wears the sprite's glass, lit, inside an iron cap, straps and foot."""
    w, h = SIZES[key]
    image, pixels, (fx, fy, fw, fh), classic = refreshed(key)
    mid, dark, light = palette(pixels)
    alpha = pixels[..., 3] > .5
    widths = alpha.sum(axis=1)
    rows = np.nonzero(widths > widths.max() * .45)[0]
    # The sprite's lantern: its rows as fractions of the frame, top down.
    body_top, body_bottom = rows.min() / len(widths), (rows.max() + 1) / len(widths)
    # The cap stands above the glass's widest rows by a fifth of their height.
    lantern_top = h * (1 - body_top) + .2 * h * (body_bottom - body_top)
    lantern_bottom = max(0.0, h * (1 - body_bottom) - .02)
    body = lantern_top - lantern_bottom
    width = min(w * .92, widths.max() / pixels.shape[1] * w)
    oval = key in ((210, 22), (210, 26))
    bm = bmesh.new()
    owners = {}
    rig = Rig()
    rig.add("anchor", (0, 0, h))
    top = h - DROP
    chain_len = top - lantern_top
    segments = max(1, round(chain_len / LANTERN_SEGMENT))
    seg = chain_len / segments
    names = [rig.add(f"s{k:02d}", (0, 0, top - k * seg), "anchor" if k == 0 else f"s{k - 1:02d}") for k in range(segments)]
    gauge = (width * .32, width * .44, width * .07) if oval else (width * .2, width * .3, width * .05)
    pitch = gauge[1] - 2 * gauge[2]
    count = max(1, int(chain_len / pitch))
    for n in range(count):
        z = top - n * pitch
        for v in link(bm, Vector((0, 0, z)), gauge[0], gauge[1], gauge[2], n % 2 == 1):
            owners[v] = names[min(segments - 1, int((top - z) / seg))]
    rig.add("lantern", (0, 0, lantern_top), names[-1])
    rig.add("end.lantern", (0, 0, lantern_bottom), "lantern")
    glass_z0, glass_z1 = lantern_bottom + body * .18, lantern_top - body * .25
    rig.add("light", (0, 0, (glass_z0 + glass_z1) / 2), "lantern")
    made = ring(bm, Vector((0, 0, lantern_top)), gauge[0] * 1.2, gauge[2], False)
    sides = 12 if oval else 6
    r = width / 2

    def profile(t):
        return r * (.55 + .45 * math.sin(math.pi * t)) if oval else r * .92

    # Glass: a lathe of `sides` columns, UV wrapped once round and spanning the sprite's lantern rows.
    uv = bm.loops.layers.uv.new()
    u0 = fx + (classic["x"]) * fw
    u1 = fx + (classic["x"] + classic["width"]) * fw
    v_top = 1 - (fy + (classic["y"] + body_top * classic["height"]) * fh)
    v_bottom = 1 - (fy + (classic["y"] + body_bottom * classic["height"]) * fh)
    levels = 8
    grid = []
    for i in range(levels + 1):
        t = i / levels
        z = glass_z1 - (glass_z1 - glass_z0) * t
        grid.append([bm.verts.new((profile(t) * math.cos(2 * math.pi * j / sides + math.pi / sides), profile(t) * math.sin(2 * math.pi * j / sides + math.pi / sides), z)) for j in range(sides)])
    glass_faces = []
    for i in range(levels):
        for j in range(sides):
            face = bm.faces.new([grid[i][j], grid[i + 1][j], grid[i + 1][(j + 1) % sides], grid[i][(j + 1) % sides]])
            # Each face shows the middle band of the sprite's lantern, as its front does.
            for loop, (a, b) in zip(face.loops, ((0, i), (0, i + 1), (1, i + 1), (1, i))):
                loop[uv].uv = (u0 + (.3 + .4 * a) * (u1 - u0), v_top - (v_top - v_bottom) * (.25 + .5 * b / levels))
            glass_faces.append(face)
    made += [v for row in grid for v in row]
    # The iron: a pointed cap, straps down the glass, and a foot.
    apex = bm.verts.new((0, 0, lantern_top - .01))
    rim = [bm.verts.new((profile(0) * 1.12 * math.cos(2 * math.pi * j / sides + math.pi / sides), profile(0) * 1.12 * math.sin(2 * math.pi * j / sides + math.pi / sides), glass_z1)) for j in range(sides)]
    for j in range(sides):
        bm.faces.new([apex, rim[j], rim[(j + 1) % sides]])
    made += [apex] + rim
    for j in range(0, sides, 1 if not oval else 2):
        a = 2 * math.pi * j / sides + math.pi / sides
        path = [Vector((profile(t) * 1.04 * math.cos(a), profile(t) * 1.04 * math.sin(a), glass_z1 - (glass_z1 - glass_z0) * t)) for t in np.linspace(0, 1, 6)]
        made += tube(bm, path, max(.012, width * .045), 4, False)
    tip = bm.verts.new((0, 0, lantern_bottom))
    foot = [bm.verts.new((profile(1) * 1.1 * math.cos(2 * math.pi * j / sides + math.pi / sides), profile(1) * 1.1 * math.sin(2 * math.pi * j / sides + math.pi / sides), glass_z0)) for j in range(sides)]
    for j in range(sides):
        bm.faces.new([tip, foot[(j + 1) % sides], foot[j]])
    made += [tip] + foot
    for v in made:
        owners[v] = "lantern"
    glass_set = set(glass_faces)
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if f not in glass_set])
    for f in bm.faces:
        f.material_index = 1 if f in glass_set else 0
    order = list(bm.verts)
    obj = new_object("lantern", bm, [material("iron", dark, .75, .5), material("glass", (1, 1, 1), 0, .3, emission=1.5, texture=image)])
    arm = rig.build("lantern")
    skin(obj, arm, [{owners[v]: 1.0} for v in order])
    export(key)


def build(key):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if key not in HANGS:
        raise SystemExit(f"{key[0]}-{key[1]} is not a hanging sprite this builds.")
    if key in ROOTS:
        root(key)
    elif key in CHAINS:
        chain(key)
    elif key in LANTERNS:
        lantern(key)
    else:
        cage(key)


args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for key in [tuple(int(p) for p in a.split("-")) for a in args] or list(HANGS):
    build(key)
