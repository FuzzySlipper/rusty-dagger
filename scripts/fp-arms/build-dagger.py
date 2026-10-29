"""Builds the first-person dagger (item 113: gold spiked crossguard, green stones, broad leaf blade) as a GLB.

Run under Blender. The look's frame follows Rusty.Engine.FirstPerson: the origin at the grip's centre, the length
along -Z with the tip forward, and +Y (one edge) as its up side. Proportions are read from the WEAPON02 frames,
taking the gauntlet's fist as about 9 cm across.

usage: blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- OUT_GLB
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

out = Path(sys.argv[sys.argv.index("--") + 1])

# Blender is Z-up and the export turns it Y-up: Blender +Y becomes glTF -Z (the length), +Z becomes glTF +Y (up).
GRIP = 0.095            # grip length, centred on the origin
GUARD = 0.052           # guard centre along the length
BLADE = (0.064, 0.30)   # blade base and tip
BLADE_WIDTH = 0.054
BLADE_THICK = 0.0065
SPIKES = [(-78, 0.060), (-52, 0.072), (-26, 0.060), (26, 0.060), (52, 0.072), (78, 0.060)]  # (degrees off the length, length)


def material(name, colour, metallic, roughness):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return m


def mesh_object(name, bm, mat):
    data = bpy.data.meshes.new(name)
    bm.to_mesh(data)
    bm.free()
    data.materials.append(mat)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    for poly in data.polygons:
        poly.use_smooth = True
    return obj


def blade_width(t):
    """A leaf: broad shoulders that swell a little before narrowing to the point."""
    if t < 0.35:
        return BLADE_WIDTH * (0.86 + 0.14 * math.sin(t / 0.35 * math.pi / 2))
    return BLADE_WIDTH * math.cos((t - 0.35) / 0.65 * math.pi / 2) ** 0.8


def blade(mat):
    """Lozenge section with a shallow fuller either side of the ridge."""
    bm = bmesh.new()
    rows, steps = [], 24
    for i in range(steps + 1):
        t = i / steps
        y = BLADE[0] + (BLADE[1] - BLADE[0]) * t
        w = max(blade_width(t), 0.0005) / 2
        th = BLADE_THICK / 2 * (1 - 0.7 * t)
        fuller = th * (0.55 if t < 0.6 else 1.0)
        section = [(0, w), (th * 0.6, w * 0.55), (fuller, w * 0.15), (th, 0), (fuller, -w * 0.15), (th * 0.6, -w * 0.55),
                   (0, -w), (-th * 0.6, -w * 0.55), (-fuller, -w * 0.15), (-th, 0), (-fuller, w * 0.15), (-th * 0.6, w * 0.55)]
        rows.append([bm.verts.new((x, y, z)) for x, z in section])
    n = len(rows[0])
    for a, b in zip(rows, rows[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bm.faces.new(list(reversed(rows[0])))
    tip = bm.verts.new((0, BLADE[1] + 0.012, 0))
    for k in range(n):
        bm.faces.new((rows[-1][k], rows[-1][(k + 1) % n], tip))
    obj = mesh_object("blade", bm, mat)
    obj.data.polygons.foreach_set("use_smooth", [False] * len(obj.data.polygons))
    return obj


def cone(name, base, direction, length, radius, mat, segments=8):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=0.0004, depth=length)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, length / 2))
    turn = Vector((0, 0, 1)).rotation_difference(direction.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation(base) @ turn, verts=bm.verts)
    return mesh_object(name, bm, mat)


def sphere(name, centre, radius, mat, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(centre) @ Matrix.Diagonal((*scale, 1)), verts=bm.verts)
    return mesh_object(name, bm, mat)


def cylinder(name, start, end, radius, mat, segments=12):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius, depth=(end - start).length)
    turn = Vector((0, 0, 1)).rotation_difference(end - start).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((start + end) / 2) @ turn, verts=bm.verts)
    return mesh_object(name, bm, mat)


bpy.ops.wm.read_factory_settings(use_empty=True)
gold = material("gold", (0.85, 0.58, 0.22), 1.0, 0.32)
steel = material("steel", (0.78, 0.79, 0.82), 1.0, 0.28)
stone = material("stone", (0.04, 0.42, 0.16), 0.0, 0.08)
wrap = material("grip", (0.16, 0.09, 0.05), 0.0, 0.7)

blade(steel)
cylinder("grip", Vector((0, -GRIP / 2, 0)), Vector((0, GRIP / 2, 0)), 0.0125, wrap)
for k in range(5):  # gold bands over the wrap
    y = -GRIP / 2 + GRIP * (k + 0.5) / 5
    cylinder(f"band{k}", Vector((0, y - 0.002, 0)), Vector((0, y + 0.002, 0)), 0.0138, gold)
sphere("pommel", Vector((0, -GRIP / 2 - 0.012, 0)), 0.017, gold, (1, 0.9, 1))
sphere("guard", Vector((0, GUARD, 0)), 0.026, gold, (0.75, 0.42, 1.3))
for side in (1, -1):  # a stone set in each face of the guard
    sphere(f"stone{side}", Vector((0.016 * side, GUARD, 0)), 0.008, stone, (0.6, 1, 1.15))
for k, (angle, length) in enumerate(SPIKES):  # the crown fans forward and out across the edges
    a = math.radians(angle)
    direction = Vector((0, math.cos(a), math.sin(a)))
    base = Vector((0, GUARD, 0.022 * math.sin(a)))
    for lean in (-0.18, 0.18):  # a pair, splayed slightly off the blade's plane
        cone(f"spike{k}{'ab'[lean > 0]}", base + Vector((lean * 0.04, 0, 0)), direction + Vector((lean, 0, 0)), length, 0.0065, gold)

out.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB", export_yup=True)
print("BUILT", out)
