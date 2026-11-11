"""Builds the small cart a driver sits on (design: wiki/rusty-dagger/first-person-held-art).

usage: blender -b --factory-startup --python-exit-code 1 --python authoring/tools/mounts/cart.py -- OUT.glb

OUT holds a two-wheeled cart of planks, on its origin's ground with the seat over the origin and its shafts along -Z, with
the reins from the driver's hands to the horse's own, and OUT-retro.glb beside it the same with a smaller texture. It is built
from boxes and cylinders, in the art's wood colours.

The game draws the horse ahead of it, at `mount.cartHorse` in the held definition. The reins run from the driver's fists (FIST)
to the horse's own (REINS_END), overlapping them so the horse can move: moving the seat, the horse or RIDE_HAND in
scripts/fp-arms/build.py means moving FIST and REINS_END too.
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
LEATHER = ((.22, .13, .07), (.3, .18, .1))
# The driver's fists from the seat (`RIDE_HAND` in scripts/fp-arms/build.py on the seat's height, `mount.cartSeat` in the
# held definition), and where the horse's own reins end from the seat (horse.py's REINS_ at `mount.cartHorse`), a little
# past it so the two overlap as the horse moves.
FIST = Vector((.07, .43, 1.46))
REINS_END = Vector((.09, 3.0, 1.73))


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


def strap(bm, start, end):
    """A flat strap from `start` to `end`, sagging a little between them."""
    previous = None
    steps = 10
    for step in range(steps + 1):
        t = step / steps
        centre = start.lerp(end, t) + Vector((0, 0, -.1 * math.sin(t * math.pi)))
        ring = [bm.verts.new(centre + Vector(corner)) for corner in ((.012, 0, .006), (-.012, 0, .006), (-.012, 0, -.006), (.012, 0, -.006))]
        if previous:
            for corner in range(4):
                bm.faces.new((previous[corner], previous[(corner + 1) % 4], ring[(corner + 1) % 4], ring[corner])).material_index = 2
        previous = ring


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
    # Everything so far is placed from the cart's middle, and the seat is .3 ahead of it.
    bmesh.ops.translate(bm, vec=(0, -.3, 0), verts=bm.verts)
    for side in (-1, 1):
        strap(bm, Vector((FIST.x * side, FIST.y, FIST.z)), Vector((REINS_END.x * side, REINS_END.y, REINS_END.z)))
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
    # Clearing the materials sets every face back to the first, so each face's slot is kept to restore.
    slots = [polygon.material_index for polygon in mesh.polygons]
    for path, size in ((out_path, SIZE), (out_path.with_name(f"{out_path.stem}-retro.glb"), RETRO_SIZE)):
        mesh.materials.clear()
        mesh.materials.append(texture(f"wood{size}", WOOD, 0, size))
        mesh.materials.append(texture(f"iron{size}", IRON, 0, size))
        mesh.materials.append(texture(f"leather{size}", LEATHER, 0, size))
        mesh.polygons.foreach_set("material_index", slots)
        mesh.update()
        path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", export_image_format="AUTO", export_yup=True, export_tangents=False)
    print("BUILT", out_path)


main()
