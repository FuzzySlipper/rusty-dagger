"""Builds the small cart a driver sits on (authoring/docs/mounts.md).

usage: blender -b --factory-startup --python-exit-code 1 --python authoring/tools/mounts/cart.py -- OUT.glb

OUT holds a two-wheeled cart of planks, on its origin's ground with the seat over the origin and its shafts along -Z, and
OUT-retro.glb beside it the same with a smaller texture. It is built from boxes and cylinders, in the art's wood colours.
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

out_path = Path(sys.argv[sys.argv.index("--") + 1])
SIZE = 256
RETRO_SIZE = 64
# Wood from dark to light, and the iron of the tyres.
WOOD = ((.2, .12, .06), (.3, .18, .09), (.4, .26, .13), (.5, .33, .18))
IRON = ((.12, .12, .13), (.2, .2, .22))


def texture(name, ramp, grain_along, size):
    """A plank's colours: streaks along one axis, flat texels drawn nearest."""
    random = np.random.default_rng(sum(name.encode()))
    streak = random.random((size, 1)).repeat(size, axis=1) * .7 + random.random((size, size)) * .3
    steps = np.array(ramp)[np.clip((streak * len(ramp)).astype(int), 0, len(ramp) - 1)]
    pixels = np.concatenate([steps, np.ones((size, size, 1))], axis=2)
    image = bpy.data.images.new(name, size, size)
    image.pixels.foreach_set(pixels.ravel().astype(np.float32))
    image.pack()
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    node.interpolation = "Closest"
    material.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = .9
    return material


def box(bm, centre, size, slot=0, turn=None):
    made = bmesh.ops.create_cube(bm, size=1)["verts"]
    matrix = Matrix.Translation(centre) @ (turn or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
    bmesh.ops.transform(bm, matrix=matrix, verts=made)
    for face in {f for v in made for f in v.link_faces}:
        face.material_index = slot


def wheel(bm, x):
    made = bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=.55, radius2=.55, depth=.07)["verts"]
    bmesh.ops.transform(bm, matrix=Matrix.Translation((x, 0, .55)) @ Matrix.Rotation(math.pi / 2, 4, "Y"), verts=made)
    for face in {f for v in made for f in v.link_faces}:
        face.material_index = 1


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bm = bmesh.new()
    # The bed, its sides and front board, the seat plank, the shafts running forward to the horse, and the axle.
    for plank in range(5):
        box(bm, (-.48 + plank * .24, .25, .85), (.236, 1.5, .05))
    box(bm, (-.62, .25, 1.02), (.05, 1.5, .3))
    box(bm, (.62, .25, 1.02), (.05, 1.5, .3))
    box(bm, (0, 1.0, 1.04), (1.28, .05, .34))
    box(bm, (0, .3, 1.32), (1.2, .3, .05))
    box(bm, (0, -.5, 1.02), (1.28, .05, .3))
    for x in (-.42, .42):
        box(bm, (x, 1.95, .95), (.07, 2.1, .07), turn=Matrix.Rotation(math.radians(3), 4, "X"))
    box(bm, (0, 0, .55), (1.4, .08, .08))
    wheel(bm, -.76)
    wheel(bm, .76)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("cart")
    uv = bm.loops.layers.uv.new()
    for face in bm.faces:
        normal = face.normal
        for loop in face.loops:
            co = loop.vert.co
            loop[uv].uv = (co.x, co.y) if abs(normal.z) > .6 else ((co.y, co.z) if abs(normal.x) > .6 else (co.x, co.z))
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("cart", mesh)
    bpy.context.scene.collection.objects.link(obj)
    # The seat over the origin.
    mesh.transform(Matrix.Translation((0, -.3, 0)))
    for path, size in ((out_path, SIZE), (out_path.with_name(f"{out_path.stem}-retro.glb"), RETRO_SIZE)):
        mesh.materials.clear()
        mesh.materials.append(texture(f"wood{size}", WOOD, 0, size))
        mesh.materials.append(texture(f"iron{size}", IRON, 0, size))
        path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", export_image_format="AUTO", export_yup=True, export_tangents=False)
    print("BUILT", out_path)


main()
