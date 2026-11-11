"""Rigs and paints the horse a rider sits on (design: wiki/rusty-dagger/first-person-held-art).

usage: blender -b --factory-startup --python-exit-code 1 --python authoring/tools/mounts/horse.py -- MESH.glb ICON.png OUT.glb

MESH is a generated horse (TRELLIS.2 from the refreshed inventory art). ICON is that art, whose colours the crunch repaint
keeps. OUT holds the horse standing on its origin's ground, nose along -Z, with the saddle over the origin: a skeleton found
from the mesh, a saddle draped on it, and the clips Stand, Walk, Trot and Gallop, whose loops are made here from each gait's
footfalls. OUT-retro.glb beside it is the same with a small texture, for the heldRetro style, and OUT-harness.glb and its
retro are the horse with no saddle, for a cart's shafts.

Before: generate MESH with asset-pipeline's `image-to-mesh-local` (TRELLIS.2) on the cutout
authoring/gen-raw/studies/items-icons-player-icons-bp-20261103/cutout/horse.png with `--mask-source alpha`, into the ignored
authoring/gen-raw/mounts/horse-generated.glb. After: `rusty asset check` on OUT, then `player.ride horse` in the game.

A gait is one row of GAITS: the loop's seconds, the share of it a foot is down, each leg's phase, how far the legs swing and
the body bobs. REINS_AHEAD and its neighbours place the reins' end where RIDE_HAND in scripts/fp-arms/build.py puts the hands,
so change them together.
"""
import json
import math
import struct
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

mesh_path, icon_path, out_path = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])
# The ears' height in metres, and the withers' as a share of it.
HEIGHT = 1.9
WITHERS = .78
FACES = 9000
TEXTURE = 1024
RETRO_TEXTURE = 256
PALETTE = 14
# How much the generated colours darken before the art's palette takes them: the viewmodel layer draws them bright.
SHADE = .62
# Where the rider's hands hold the reins from the seat, along and up from the ground, and how far apart.
REINS_AHEAD, REINS_HEIGHT, REINS_APART = .36, 1.72, .09
NECK_CLEAR = .13
# Clips: frames a second, and each gait's loop in seconds, the share of it a foot is down, and each leg's phase.
FPS = 30
LEGS = ("lf", "rf", "lh", "rh")
GAITS = {
    "Walk": (1.1, .64, {"lh": 0, "lf": .25, "rh": .5, "rf": .75}, 16, .012, 2),
    "Trot": (.72, .5, {"lh": 0, "rf": 0, "rh": .5, "lf": .5}, 20, .03, 2),
    "Gallop": (.56, .34, {"lh": 0, "rh": .13, "lf": .46, "rf": .6}, 34, .05, 1),
}


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    bpy.ops.import_scene.gltf(filepath=str(mesh_path))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def stand(obj):
    """Sets the horse on the ground at full size, nose along +Y (-Z once exported)."""
    data = obj.data
    points = np.array([v.co for v in data.vertices])
    top = points[points[:, 2] > np.percentile(points[:, 2], 97)]
    turn = math.pi if top[:, 1].mean() < points[:, 1].mean() else 0
    scale = HEIGHT / np.ptp(points[:, 2])
    placed = Matrix.Rotation(turn, 4, "Z") @ Matrix.Scale(scale, 4)
    data.transform(placed)
    points = np.array([v.co for v in data.vertices])
    centre = Vector(((points[:, 0].min() + points[:, 0].max()) / 2, 0, points[:, 2].min()))
    data.transform(Matrix.Translation(-centre))
    data.validate()
    data.update()


def lower(obj):
    decimate = obj.modifiers.new("low", "DECIMATE")
    decimate.ratio = min(1, FACES / len(obj.data.polygons))
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=decimate.name)


def column(points, start, heights):
    """A leg's centre at each of `heights`, going up from `start`: the verts near the last centre, at that height."""
    centre, found = np.array(start[:2]), []
    for height in heights:
        band = points[np.abs(points[:, 2] - height) < .035]
        near = band[np.linalg.norm(band[:, :2] - centre, axis=1) < .15]
        if len(near):
            centre = near[:, :2].mean(axis=0)
        found.append(Vector((centre[0], centre[1], height)))
    return found


def skeleton(obj):
    """The joints, found from the mesh: each foot's cluster and the leg's column above it, then the spine, neck and tail."""
    points = np.array([v.co for v in obj.data.vertices])
    withers = HEIGHT * WITHERS
    ears = points[points[:, 2] > HEIGHT * .96]
    nose_y = points[:, 1].max()
    feet = points[points[:, 2] < .1]
    mid_y = (feet[:, 1].min() + feet[:, 1].max()) / 2
    joints = {}
    for name, side, end in (("lf", -1, 1), ("rf", 1, 1), ("lh", -1, -1), ("rh", 1, -1)):
        foot = feet[(np.sign(feet[:, 0] - feet[:, 0].mean()) == side) & (np.sign(feet[:, 1] - mid_y) == end)]
        start = foot.mean(axis=0)
        shares = (.02, .13, .33, .58) if end > 0 else (.02, .13, .4, .62)
        joints[name] = column(points, start, [withers * share for share in shares])
    front_y = (joints["lf"][3].y + joints["rf"][3].y) / 2
    hind_y = (joints["lh"][3].y + joints["rh"][3].y) / 2
    hips = Vector((0, hind_y - .12, withers * .9))
    chest = Vector((0, front_y - .05, withers * .9))
    poll = Vector((0, ears[:, 1].mean(), HEIGHT * .9))
    root_neck = Vector((0, front_y + .1, withers * 1.0))
    neck = root_neck.lerp(poll, .5) + Vector((0, -.04, .05))
    nose = Vector((0, nose_y - .03, points[points[:, 1] > nose_y - .08][:, 2].mean()))
    tail_points = points[points[:, 1] < hips.y - .34]
    tail_top = Vector((0, tail_points[:, 1].max() - .02, withers * .86))
    tail_end = tail_points[np.argmin(tail_points[:, 2])]
    tail_mid = tail_top.lerp(Vector(tail_end), .4) + Vector((0, -.12, 0))
    return joints, hips, chest, root_neck, neck, poll, nose, tail_top, tail_mid, Vector(tail_end)


def rig(obj):
    joints, hips, chest, root_neck, neck, poll, nose, tail_top, tail_mid, tail_end = skeleton(obj)
    data = bpy.data.armatures.new("horse")
    armature = bpy.data.objects.new("horse", data)
    bpy.context.scene.collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None):
        made = data.edit_bones.new(name)
        made.head, made.tail = head, tail
        made.parent = data.edit_bones[parent] if parent else None
        return made

    bone("root", Vector((0, 0, 0)), Vector((0, .3, 0)))
    bone("hips", hips, hips.lerp(chest, .5), "root")
    bone("spine", hips.lerp(chest, .5), chest, "hips")
    bone("neck_1", root_neck, neck, "spine")
    bone("neck_2", neck, poll, "neck_1")
    bone("head", poll, nose, "neck_2")
    bone("tail_1", tail_top, tail_mid, "hips")
    bone("tail_2", tail_mid, tail_mid.lerp(tail_end, .5), "tail_1")
    bone("tail_3", tail_mid.lerp(tail_end, .5), tail_end, "tail_2")
    for name, (hoof, fetlock, knee, elbow) in joints.items():
        front = name.endswith("f")
        top = elbow + Vector((0, .1 if front else -.14, .3 if front else .34))
        bone(f"{name}_1", top, elbow, "spine" if front else "hips")
        bone(f"{name}_2", elbow, knee, f"{name}_1")
        bone(f"{name}_3", knee, fetlock, f"{name}_2")
        bone(f"{name}_4", fetlock, hoof, f"{name}_3")
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.parent_set(type="ARMATURE_NAME")
    weigh(obj, armature)
    return armature


def weigh(obj, armature):
    """
    Each vertex to its nearest bones, by the distance to each bone's length (the mesh is too rough for Blender's heat
    weights). A leg's bones reach only their own side and end of the body, so a leg never drags its neighbour.
    """
    points = np.array([v.co for v in obj.data.vertices])
    bones = [b for b in armature.data.bones if b.name != "root"]
    distances = np.empty((len(points), len(bones)))
    middle = (armature.data.bones["hips"].head_local.y + armature.data.bones["spine"].tail_local.y) / 2
    # The barrel between the legs moves with the spine alone.
    fore, hind = armature.data.bones["lf_2"].head_local.y, armature.data.bones["lh_2"].head_local.y
    belly = (points[:, 1] < fore - .12) & (points[:, 1] > hind + .16) & (points[:, 2] > HEIGHT * WITHERS * .5)
    for index, bone in enumerate(bones):
        head, tail = np.array(bone.head_local), np.array(bone.tail_local)
        span = tail - head
        along = np.clip((points - head) @ span / (span @ span), 0, 1)
        distances[:, index] = np.linalg.norm(points - (head + along[:, None] * span), axis=1)
        if bone.name[:2] in LEGS:
            left = bone.name[0] == "l"
            front = bone.name[1] == "f"
            distances[(points[:, 0] > .02 if left else points[:, 0] < -.02) | (points[:, 1] < middle if front else points[:, 1] > middle) | belly, index] = 1e6
        elif bone.name.startswith("tail"):
            distances[points[:, 1] > armature.data.bones["tail_1"].head_local.y + .06, index] = 1e6
    shares = (1 / (distances + .012)) ** 5
    keep = np.argsort(-shares, axis=1)[:, :3]
    groups = [obj.vertex_groups.get(b.name) or obj.vertex_groups.new(name=b.name) for b in bones]
    for vertex, chosen in enumerate(keep):
        total = shares[vertex, chosen].sum()
        for index in chosen:
            groups[index].add([vertex], float(shares[vertex, index] / total), "REPLACE")


def paint(obj):
    """The soft generated colours in the art's own palette, flat texels drawn nearest: the crunch."""
    material = obj.data.materials[0]
    nodes = material.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    source = next(link.from_node for link in material.node_tree.links if link.to_socket == bsdf.inputs["Base Color"])
    image = source.image
    image.scale(TEXTURE, TEXTURE)
    width = TEXTURE
    pixels = np.empty(width * width * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    colours = pixels.reshape(-1, 4)[:, :3]
    icon = bpy.data.images.load(str(icon_path))
    art = np.empty(len(icon.pixels), dtype=np.float32)
    icon.pixels.foreach_get(art)
    art = art.reshape(-1, 4)
    art = art[art[:, 3] > .9][:, :3]
    random = np.random.default_rng(3)
    palette = art[random.choice(len(art), PALETTE)]
    for _ in range(12):
        sample = art[random.choice(len(art), 20000)]
        nearest_of = np.argmin(((sample[:, None] - palette[None]) ** 2).sum(axis=2), axis=1)
        for index in range(PALETTE):
            if (nearest_of == index).any():
                palette[index] = sample[nearest_of == index].mean(axis=0)
    # An ordered dither, so a soft ramp breaks into cells rather than bands.
    bayer = np.array(((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))) / 16 - .5
    dither = np.tile(bayer, (width // 4, width // 4)).reshape(-1, 1) * .05
    shifted = np.clip(colours * SHADE + dither, 0, 1)
    chosen = np.empty(len(colours), dtype=int)
    for start in range(0, len(colours), 65536):
        chosen[start:start + 65536] = np.argmin(((shifted[start:start + 65536, None] - palette[None]) ** 2).sum(axis=2), axis=1)
    out = pixels.reshape(-1, 4)
    out[:, :3] = palette[chosen]
    out[:, 3] = 1
    image.pixels.foreach_set(pixels)
    image.update()
    image.pack()
    source.interpolation = "Closest"
    for node in list(nodes):
        if node.type in ("TEX_IMAGE", "NORMAL_MAP", "SEPARATE_COLOR", "GROUP") and node.name != source.name:
            nodes.remove(node)
    bsdf.inputs["Roughness"].default_value = .85
    bsdf.inputs["Metallic"].default_value = 0


def step(phase, stance):
    """A leg through its stride at `phase`: how far forward it swings (-1 to 1), and how far it folds lifting clear (0 to 1)."""
    if phase < stance:
        return 1 - 2 * phase / stance, 0
    lift = (phase - stance) / (1 - stance)
    return -math.cos(lift * math.pi), math.sin(lift * math.pi)


def turn_about(bone, axis, degrees):
    return Matrix.Rotation(math.radians(degrees), 4, bone.matrix_local.to_3x3().inverted() @ Vector(axis))


def pose_at(armature, gait, t):
    """Each bone's pose at `t` (0 to 1) through `gait`, by name; None is the horse standing and breathing."""
    bones = armature.data.bones
    if gait is None:
        breath = math.sin(t * 2 * math.pi)
        return {"spine": turn_about(bones["spine"], (1, 0, 0), breath * .4), "neck_1": turn_about(bones["neck_1"], (1, 0, 0), breath * 1.2),
                "head": turn_about(bones["head"], (1, 0, 0), -breath * 1.5), "tail_2": turn_about(bones["tail_2"], (0, 1, 0), breath * 3)}
    _, stance, phases, swing, bob, beats = GAITS[gait]
    basis = {}
    for leg in LEGS:
        forward, fold = step((t + phases[leg]) % 1, stance)
        front = leg.endswith("f")
        basis[f"{leg}_1"] = turn_about(bones[f"{leg}_1"], (1, 0, 0), forward * swing * .8)
        basis[f"{leg}_2"] = turn_about(bones[f"{leg}_2"], (1, 0, 0), forward * swing * .3 - (fold * 30 if front else -fold * 22))
        basis[f"{leg}_3"] = turn_about(bones[f"{leg}_3"], (1, 0, 0), (-fold * 62) if front else (-fold * 48))
        basis[f"{leg}_4"] = turn_about(bones[f"{leg}_4"], (1, 0, 0), -fold * 40)
    wave = math.sin(t * beats * 2 * math.pi)
    basis["root"] = Matrix.Translation((0, 0, 0))
    basis["hips"] = Matrix.Translation(bones["hips"].matrix_local.to_3x3().inverted() @ Vector((0, 0, wave * bob))) @ turn_about(bones["hips"], (1, 0, 0), wave * bob * 90)
    basis["spine"] = turn_about(bones["spine"], (1, 0, 0), -wave * bob * 60)
    basis["neck_1"] = turn_about(bones["neck_1"], (1, 0, 0), -wave * bob * 150)
    basis["neck_2"] = turn_about(bones["neck_2"], (1, 0, 0), wave * bob * 60)
    basis["head"] = turn_about(bones["head"], (1, 0, 0), wave * bob * 110)
    trail = 12 + bob * 300
    basis["tail_1"] = turn_about(bones["tail_1"], (1, 0, 0), trail + wave * 3)
    basis["tail_2"] = turn_about(bones["tail_2"], (1, 0, 0), wave * 5)
    return basis


def keyed(armature, name, gait, seconds):
    frames = round(seconds * FPS)
    action = bpy.data.actions.new(name)
    armature.animation_data_create()
    armature.animation_data.action = action
    for index in range(frames + 1):
        basis = pose_at(armature, gait, index / frames)
        for bone in armature.pose.bones:
            bone.rotation_mode = "QUATERNION"
            matrix = basis.get(bone.name, Matrix.Identity(4))
            bone.location, bone.rotation_quaternion = matrix.to_translation(), matrix.to_quaternion()
            bone.keyframe_insert("location", frame=index + 1)
            bone.keyframe_insert("rotation_quaternion", frame=index + 1)
    armature.animation_data.action = None
    action.use_fake_user = True


def flat(name, colour):
    """A matte material of `colour` (sRGB) on a small noisy texture, drawn nearest."""
    random = np.random.default_rng(sum(name.encode()))
    size = 64
    grain = 1 + random.normal(0, .1, (size, size, 1))
    pixels = np.concatenate([np.clip(np.array(colour) * grain, 0, 1), np.ones((size, size, 1))], axis=2)
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
    bsdf.inputs["Roughness"].default_value = .85
    return material


def saddle(obj, armature, seat_y):
    """
    A saddle on a blanket, draped over the back from the horse's own surface: the grid's heights are the highest vertices
    under each point, with a pommel and cantle raised on the seat.
    """
    points = np.array([v.co for v in obj.data.vertices])
    columns, rows = 19, 13
    half_width, half_length = .33, .34
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    deform = bm.verts.layers.deform.verify()
    group = obj.vertex_groups["spine"].index
    grid = []
    for row in range(rows):
        y = seat_y + (row / (rows - 1) - .5) * 2 * half_length
        line = []
        for column in range(columns):
            x = (column / (columns - 1) - .5) * 2 * half_width
            near = points[(np.abs(points[:, 0] - x) < .045) & (np.abs(points[:, 1] - y) < .06)]
            top = near[:, 2].max() if len(near) else HEIGHT * WITHERS - .3
            across = abs(x) / .15
            along = (y - seat_y) / half_length
            rise = max(0, 1 - across ** 2) * (.1 * max(0, -along - .35) / .65 * 2.2 + .07 * max(0, along - .4) / .6 * 2.4 + .02)
            vert = bm.verts.new((x, y, top + .012 + rise))
            vert[deform][group] = 1
            line.append(vert)
        grid.append(line)
    for row in range(rows - 1):
        for column in range(columns - 1):
            face = bm.faces.new((grid[row][column], grid[row][column + 1], grid[row + 1][column + 1], grid[row + 1][column]))
            leather = abs((column + .5) / (columns - 1) - .5) * 2 * half_width < .17 and abs((row + .5) / (rows - 1) - .5) * 2 < .8
            face.material_index = 1 if leather else 0
            for loop, (u, v) in zip(face.loops, ((column, row), (column + 1, row), (column + 1, row + 1), (column, row + 1))):
                loop[uv].uv = (u / (columns - 1), v / (rows - 1))
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("saddle")
    bm.to_mesh(mesh)
    bm.free()
    part = bpy.data.objects.new("saddle", mesh)
    bpy.context.scene.collection.objects.link(part)
    part.vertex_groups.new(name="spine")
    mesh.materials.append(flat("blanket", (.32, .09, .07)))
    mesh.materials.append(flat("leather", (.2, .12, .07)))
    for vert in mesh.vertices:
        part.vertex_groups[0].add([vert.index], 1, "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    part.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()


def reins(obj, armature, seat_y):
    """A strap from each side of the bit back to the rider's hand, sagging, that follows the head at one end and the back at the other."""
    head = armature.data.bones["head"]
    bit = head.head_local.lerp(head.tail_local, .78)
    spine, head_group = obj.vertex_groups["spine"].index, obj.vertex_groups["head"].index
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    deform = bm.verts.layers.deform.verify()
    for side in (-1, 1):
        start = bit + Vector((side * .075, 0, -.03))
        end = Vector((side * REINS_APART, seat_y + REINS_AHEAD, REINS_HEIGHT))
        previous = None
        steps = 10
        for step in range(steps + 1):
            t = step / steps
            # Round the neck rather than through it.
            centre = start.lerp(end, t) + Vector((side * NECK_CLEAR * math.sin(t * math.pi), 0, -.04 * math.sin(t * math.pi)))
            ring = []
            for corner in ((.012, .006), (-.012, .006), (-.012, -.006), (.012, -.006)):
                vert = bm.verts.new(centre + Vector((corner[0], 0, corner[1])))
                vert[deform][head_group] = 1 - t
                vert[deform][spine] = t
                ring.append(vert)
            if previous:
                for corner in range(4):
                    face = bm.faces.new((previous[corner], previous[(corner + 1) % 4], ring[(corner + 1) % 4], ring[corner]))
                    face.material_index = 0
                    for loop in face.loops:
                        loop[uv].uv = (.5, .5)
            previous = ring
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("reins")
    bm.to_mesh(mesh)
    bm.free()
    part = bpy.data.objects.new("reins", mesh)
    bpy.context.scene.collection.objects.link(part)
    for group in obj.vertex_groups:
        part.vertex_groups.new(name=group.name)
    mesh.materials.append(flat("strap", (.3, .18, .1)))
    bpy.ops.object.select_all(action="DESELECT")
    part.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()


def main():
    obj = load()
    stand(obj)
    lower(obj)
    armature = rig(obj)
    paint(obj)
    seat_y = float(armature.data.bones["spine"].tail_local.y - .3)
    reins(obj, armature, seat_y)
    keyed(armature, "Stand", None, 3)
    for gait, values in GAITS.items():
        keyed(armature, gait, gait, values[0])
    # The seat over the origin, so the rider's place is only a height.
    armature.location.y -= seat_y
    bpy.context.view_layer.update()
    harness = out_path.with_name(f"{out_path.stem}-harness.glb")
    select(obj, armature)
    export(harness)
    # The saddle is draped in the armature's own space, before the horse moves under the seat.
    armature.location.y += seat_y
    bpy.context.view_layer.update()
    saddle(obj, armature, seat_y)
    armature.location.y -= seat_y
    bpy.context.view_layer.update()
    select(obj, armature)
    export(out_path)
    obj.data.materials[0].node_tree.nodes["Image Texture"].image.scale(RETRO_TEXTURE, RETRO_TEXTURE)
    export(out_path.with_name(f"{out_path.stem}-retro.glb"))
    unsaddle(obj)
    select(obj, armature)
    export(harness.with_name(f"{harness.stem}-retro.glb"))
    print("BUILT", out_path, out_path.stat().st_size // 1024, "KB", len(obj.data.polygons), "faces, seat", round(seat_y, 3))


def select(obj, armature):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    armature.select_set(True)


def unsaddle(obj):
    """Takes the saddle back off, for the horse in a cart's shafts."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    slots = [index for index, material in enumerate(obj.data.materials) if material.name.startswith(("blanket", "leather"))]
    bmesh.ops.delete(bm, geom=[face for face in bm.faces if face.material_index in slots], context="FACES")
    bm.to_mesh(obj.data)
    bm.free()


def export(path):
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", export_animation_mode="ACTIONS", export_image_format="AUTO",
                              export_yup=True, export_frame_range=False, export_force_sampling=True, export_tangents=False, use_selection=True)
    data = path.read_bytes()
    names = sorted(a["name"] for a in json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]]).get("animations", []))
    if names != ["Gallop", "Stand", "Trot", "Walk"]:
        raise SystemExit(f"{path}: exported clips {names}")


main()
