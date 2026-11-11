"""Builds the first-person arms with their clips from the old game's MedievalFPArms and FPP melee clips
(run under Blender 5 by authoring/tools/fp-arms/build.sh; authoring/tools/fp-arms/README.md).

usage: blender -b --factory-startup --python authoring/tools/fp-arms/build.py -- SOURCES DEFINITION OUT_DIR

SOURCES is local/inputs/fp-melee (authoring/tools/fp-arms/copy-sources.sh). DEFINITION is the held definition, which names every clip
the game plays: the rig's own clip and each family's idle, power hold and attack clips, by their FBX file stems. Writes into OUT_DIR:

- each of the definition's armSets: its model, the arms (mesh, skeleton and a colour map painted by paint.py and drawn
  nearest; claws() grows the claw arms on the skeleton instead, with a paint of their own) with every clip the definition
  names, and its retroModel, the same at most RETRO_TEXTURE, which the heldRetro style draws. The clips are the model's own, not clip packs: the Engine shares an opened model's clip packs between
  everything that opens it, so a second set of arms on the same model (a weapon swapped, another site's viewmodel) could
  not associate its packs again.
- measure.json: what the definition is fitted with, measured on the clips: where the right upper arm sits from the eye
  at the start of the rig's clip (arms.shoulder), where the dagger family's weapon bone sits in the hand (rig.palm),
  each family's look grip and hold at that palm, and each attack clip's frames with the weapon's speed per frame.

Each clip is baked as the clip's own camera bone sees it: every frame, the whole body is moved so the camera bone sits
at the origin looking down the view (+Y here, -Z once exported Y-up), so the clip carries only what moves in view and
the Engine places the model with its origin at the eye. The clips' skeleton (the full UE4 mannequin, with root, camera
and weapon bones) differs from the arms' (no root, camera_mount and weapon_mount), so each frame is copied bone by
bone in world space onto the arms' own skeleton, whose rest pose is the clips'.
"""
import json
import math
import struct
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.append(str(Path(__file__).parent))
import paint as pixel_paint

sources, definition_path, out = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])
definition = json.loads(definition_path.read_text())
pack_root = sources / "MedievalFPArms"
# Each arm set's mesh file, the stem and colour of its textures in the pack, and how much its colour map is lifted before
# the paint reads it: the chainmail's is made for metal shading and is nearly black.
SOURCES = {
    "male-leather": ("FPArms_Male-Leather-Armor_LPR_LOD0", "FPArms_Male-Leather-Armor", "Medium_COL.png", 1),
    "female-leather": ("FPArms_Female-Leather-Armor_LOD0_LPR", "FPArms_Female-Leather-Armor", "Medium_COL.png", 1),
    "male-cloth": ("FPArms_Male-Cloth-LOD0_LPR", "FPArms_Male-Medieval-Cloth", "Medium_COL.png", 1),
    "female-cloth": ("FPArms_Female-Medieval-Cloth_LOD0_LPR", "FPArms_Female-Medieval-Cloth", "Medium_COL.png", 1),
    "male-chainmail": ("FPArms_Male-Chainmail_LPR_LOD0", "FPArms_Male-Chainmail", "COL.png", 2.5),
}
# The painted arms' size, whose texels draw as big as the weapons' and the classic sprites', and the size of the pack's maps
# the paint reads.
TEXTURE = 256
PAINT_SOURCE = 1024
FUR_TEXTURE = 64
# How far a face may follow the pack's `sleeve` bone and stay: more, and it belongs to the cloth sets' loose left cuff.
FLAP_WEIGHT = .25
# The legs' rings and their small plain textures: nothing of the pack's art goes into them.
LEG_SIDES = 10
LEG_TEXTURE = 32
# The most a retro model (heldRetro) draws, unfiltered, as Daggerfall draws its world; the painted arms already fit.
RETRO_TEXTURE = 256
# The arm sets grown on the skeleton (claws()) rather than taken from the pack, each with its fur from dark to light, and
# the file whose skeleton they grow on. The first is in the colours of the classic WEAPON11 frames, the boar's in its hide's
# on the refreshed paper doll.
CLAW_FUR = {
    "claws": ((.16, .1, .06), (.23, .15, .07), (.34, .22, .11), (.4, .25, .15), (.51, .31, .19), (.55, .34, .22)),
    "claws-boar": ((.08, .06, .06), (.14, .1, .09), (.22, .15, .13), (.3, .2, .17), (.38, .25, .21), (.45, .31, .26)),
}
CLAW_SKELETON = "male-leather"
# Their texture, at about the weapons' 2 mm cells, and the colours they share.
CLAW_TEXTURE = 512
MOTTLE = ((.07, .07, .07), (.12, .12, .12))
SKIN = ((.2, .14, .15), (.33, .2, .22), (.45, .28, .31), (.54, .34, .38))
NAIL = ((.07, .07, .07), (.12, .12, .12), (.23, .23, .23), (.31, .31, .31))
FINGERS = ("index", "middle", "ring", "pinky", "thumb")
# How far the clips' hands stay open on the claw arms: each joint's curl in degrees, knuckle first, and the knuckle's spread.
CLAW_CURL = (8, 28, 22)
CLAW_SPREAD = 9
# The clips' camera bone looks along its X with its Y up and its Z to the right; the baked view looks along +Y with +Z
# up and +X to the right, which the Y-up export turns into the Engine's camera space (-Z ahead, +Y up, +X right).
VIEW = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
# The pack's own clips have no camera bone: their camera_mount looks along its -Y with its X up and its Z to the right.
PACK_EYE = Matrix(((0, -1, 0, 0), (1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))
# Blender world (view baked as above) into the Engine's camera space.
TO_CAMERA = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))
# The pack's weapon bones carry the blade along +Z with its edge along +Y; a look's length runs along -Z with its up
# side +Y, so its axes in the bone's are X and Z reversed.
LOOK_IN_BONE = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, -1)))


# The bow's clips are made here (bow_clip): the pack has none. In the view: cm, +X right, +Y ahead, +Z up.
BOW_AIM = Vector((-5, 54, -14))
BOW_AIM_FORWARD = Vector((0, 1, 0))
BOW_AIM_UP = Vector((.21, 0, 1))
BOW_REST = Vector((-25, 38, -36))
BOW_REST_FORWARD = Vector((.42, .85, -.2))
BOW_REST_UP = Vector((.22, .2, .9))
BOW_RIGHT_REST = Vector((24, 22, -62))
# How far the string sits from the grip at rest, about the bows' mean; how far from the grip a full draw takes it, which
# stops the nock just below the view and short of the camera's near plane, so the string runs out of the view rather than
# ending in it; how far from wrist to string; and how far the hand follows through on the release.
BOW_BRACE = 13
BOW_FULL_DRAW = 37
BOW_HOOK_REACH = 9
# How far below the arrow the hand grips the bow, so the arrow lies over the hand and the arm under it.
BOW_HAND_DROP = 5
BOW_HOOK_RISE = Vector((-.15, 0, .7))
BOW_FOLLOW = 7
BOW_SWAY = .25
BOW_GRIP = (62, 70, 45)
BOW_HOOK = (50, 75, 50)
BOW_OPEN = (10, 15, 10)


def bow_idle(t):
    return 0, 0, 0, t, 0


def bow_draw(t):
    return smooth(t / .45), smooth((t - .35) / .65), smooth(t / .4), 0, 0


def bow_hold(t):
    return 1, 1, 1, t, 0


def bow_loose(t):
    return 1 - smooth((t - .3) / .7), 1, 1 - smooth((t - .25) / .5), 0, min(t / .06, 1)


# The rider's hands on the reins (cm in the view, the right hand's; the left mirrors it), how far forward a fist points, and
# how far they drift with the horse's stride.
RIDE_CLIP = "RIDE_Idle"
RIDE_HAND = Vector((9, 34, -48))
RIDE_POINT = Vector((-.22, 1, -.12))
RIDE_SWAY = .5


# Each bow clip's frames at 60 a second, and what it does at each moment from 0 to 1: how far the bow is raised to the aim,
# how far the string is drawn, how far the right hand has come to the string, where in its sway, and how far let go.
BOW_CLIPS = {"BOW_Idle": (120, bow_idle), "BOW_Draw": (60, bow_draw), "BOW_Hold": (120, bow_hold), "BOW_Loose": (60, bow_loose)}


def clip_file(stem):
    found = [p for p in sources.rglob(f"{stem}.fbx")]
    if len(found) != 1:
        raise SystemExit(f"clip '{stem}': {len(found)} files named {stem}.fbx under {sources}")
    return found[0]


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    return [o for o in bpy.data.objects if o not in before]


def remove(objects):
    for obj in objects:
        bpy.data.objects.remove(obj, do_unlink=True)


def pixels_of(image):
    """`image` as an array of rows from the top, its channels as they are stored."""
    pixels = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(pixels)
    return pixels.reshape(image.size[1], image.size[0], 4)[::-1]


def pack_map(path, channels=slice(0, 1), lift=1):
    """A map of the pack at PAINT_SOURCE, its values averaged: Blender's own scale would alias the cloth's weave into blotches."""
    image = bpy.data.images.load(str(path))
    image.colorspace_settings.name = "Non-Color"
    values = pixel_paint.area(pixels_of(image)[..., channels], PAINT_SOURCE)
    bpy.data.images.remove(image)
    return np.minimum(values * lift, 1)


def painted_image(name, colours, alpha=None):
    """An image of `colours`, rows from the top, sRGB 0-1."""
    size = colours.shape[0]
    image = bpy.data.images.new(name, size, size, alpha=alpha is not None)
    rgba = np.ones((size, size, 4), dtype=np.float32)
    rgba[..., :3] = colours
    if alpha is not None:
        rgba[..., 3] = alpha
    image.pixels.foreach_set(rgba[::-1].ravel())
    image.update()
    image.pack()
    return image


def baked_normals(meshes, normal_map):
    """The meshes' object-space normals with the pack's normal map on them, as colours, black where no mesh covers."""
    target = bpy.data.images.new("baked-normals", PAINT_SOURCE, PAINT_SOURCE, float_buffer=True, alpha=False)
    target.colorspace_settings.name = "Non-Color"
    normals = bpy.data.images.load(str(normal_map))
    normals.colorspace_settings.name = "Non-Color"
    added = []
    for obj in meshes:
        for slot in obj.material_slots:
            nodes, links = slot.material.node_tree.nodes, slot.material.node_tree.links
            bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
            source, mapped, baked = nodes.new("ShaderNodeTexImage"), nodes.new("ShaderNodeNormalMap"), nodes.new("ShaderNodeTexImage")
            source.image, baked.image = normals, target
            links.new(source.outputs["Color"], mapped.inputs["Color"])
            links.new(mapped.outputs["Normal"], bsdf.inputs["Normal"])
            nodes.active = baked
            added.append((nodes, (source, mapped, baked)))
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.render.bake.margin = 6
    scene.render.bake.normal_space = "OBJECT"
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.bake(type="NORMAL")
    for nodes, created in added:
        for node in created:
            nodes.remove(node)
    values = pixels_of(target)[..., :3].copy()
    bpy.data.images.remove(target)
    bpy.data.images.remove(normals)
    return values


def draw(material, image):
    """Gives `material` the flat painted `image`, drawn nearest."""
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    colour = nodes.new("ShaderNodeTexImage")
    colour.label = "colour"
    colour.image = image
    colour.interpolation = "Closest"
    links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = .85
    bsdf.inputs["Metallic"].default_value = 0
    return colour


def paint_body(meshes, stem, colour, lift):
    """Paints the arms from the pack's maps."""
    textures = pack_root / "Textures"
    image = painted_image(f"{stem}-paint", pixel_paint.paint(
        pack_map(textures / f"{stem}_{colour}", slice(0, 3), lift), pack_map(textures / f"{stem}_AO.png"), pack_map(textures / f"{stem}_RGS.png"),
        pack_map(textures / f"{stem}_MET.png"), baked_normals(meshes, textures / f"{stem}_NRM.png"), TEXTURE))
    for obj in meshes:
        for slot in obj.material_slots:
            draw(slot.material, image)


def paint_fur(obj, stem):
    """The cloth sets' frayed cuffs, their alpha cut hard; a Greater Than node is how glTF's alpha mask exports."""
    cut = pixel_paint.area(pack_map(pack_root / f"Textures/{stem}_Fur_COL.tga", slice(0, 4)), FUR_TEXTURE)
    image = painted_image(f"{stem}-fur", pixel_paint.cloth(cut[..., :3]), cut[..., 3] > .5)
    for slot in obj.material_slots:
        nodes, links = slot.material.node_tree.nodes, slot.material.node_tree.links
        colour = draw(slot.material, image)
        cut = nodes.new("ShaderNodeMath")
        cut.operation = "GREATER_THAN"
        cut.inputs[1].default_value = .5
        links.new(colour.outputs["Alpha"], cut.inputs[0])
        links.new(cut.outputs[0], next(n for n in nodes if n.type == "BSDF_PRINCIPLED").inputs["Alpha"])


def retro():
    """Turns the arms' materials into the retro arms': at most RETRO_TEXTURE, drawn nearest."""
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            colour = next(n for n in slot.material.node_tree.nodes if n.label == "colour")
            if colour.image.size[0] > RETRO_TEXTURE:
                colour.image.scale(RETRO_TEXTURE, RETRO_TEXTURE)
            colour.interpolation = "Closest"


def cut_flap(meshes):
    """Cuts the loose cuff that hangs from the `sleeve` bone: the torn sleeve stays, but this piece breaks the arm's outline."""
    for obj in meshes:
        group = obj.vertex_groups.get("sleeve")
        if group is None:
            continue
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        weights = mesh.verts.layers.deform.verify()
        flap = [face for face in mesh.faces if max(vert[weights].get(group.index, 0) for vert in face.verts) > FLAP_WEIGHT]
        bmesh.ops.delete(mesh, geom=flap, context="FACES")
        mesh.to_mesh(obj.data)
        mesh.free()


def arms(name):
    """The arm set `name`, painted, as one mesh; the pack's own unarmed clip is dropped."""
    mesh_file, stem, colour, lift = SOURCES[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 60
    imported = import_fbx(pack_root / f"Meshes/{mesh_file}.fbx")
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    rig = next(o for o in imported if o.type == "ARMATURE")
    meshes = [obj for obj in imported if obj.type == "MESH"]
    for obj in meshes:
        obj.data.uv_layers[0].name = "UVMap"
    cut_flap(meshes)
    fur = [obj for obj in meshes if "_Fur" in obj.name]
    paint_body([obj for obj in meshes if obj not in fur], stem, colour, lift)
    for obj in fur:
        paint_fur(obj, stem)
    legs(rig, meshes)
    return rig


def plain(name, colour):
    """A rough material of `colour` (sRGB 0-1) on a small noisy texture, drawn nearest, labelled for retro()."""
    random = np.random.default_rng(sum(name.encode()))
    grain = 1 + random.normal(0, 0.09, (LEG_TEXTURE, LEG_TEXTURE, 1))
    return nearest(name, np.clip(np.array(colour) * grain, 0, 1))


def nearest(name, colours):
    """A rough material drawing `colours`, a square of sRGB 0-1 whose first row is the bottom, nearest, labelled for retro()."""
    size = colours.shape[0]
    pixels = np.concatenate([colours, np.ones((size, size, 1))], axis=2)
    image = bpy.data.images.new(name, size, size)
    image.pixels.foreach_set(pixels.ravel().astype(np.float32))
    image.pack()
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    node = nodes.new("ShaderNodeTexImage")
    node.label = "colour"
    node.image = image
    node.interpolation = "Closest"
    material.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return material


def legs(rig, meshes, cloth=(.32, .28, .23), boots=(.3, .19, .11)):
    """
    Grows a low leg and boot on each of the skeleton's legs and joins them to the arms' `meshes`, as the Engine draws one
    mesh for the arms: the pack has arms only, and a kick needs something to kick with. Rings of LEG_SIDES run down each
    leg's bones, every ring on the bones given, so nothing here comes from the pack.
    """
    mesh = bpy.data.meshes.new("legs")
    leg = bpy.data.objects.new("legs", mesh)
    bpy.context.scene.collection.objects.link(leg)
    leg.matrix_world = rig.matrix_world
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    deform = bm.verts.layers.deform.verify()
    groups = {}
    for side in "lr":
        bones = {n: rig.data.bones[f"{n}_{side}"] for n in ("thigh", "calf", "foot", "ball")}
        for b in bones.values():
            groups[b.name] = leg.vertex_groups.new(name=b.name).index
        thigh, calf, foot, ball = (bones[n] for n in ("thigh", "calf", "foot", "ball"))
        toe = ball.head_local + (ball.tail_local - ball.head_local).normalized() * 9
        # (centre, half width, half depth, the bones it follows, boot?): cm in the skeleton's space.
        rings = [(thigh.head_local, 9, 9.5, [thigh], False), (thigh.head_local.lerp(calf.head_local, .5), 7.5, 8, [thigh], False),
                 (calf.head_local, 6, 6.5, [thigh, calf], False), (calf.head_local.lerp(foot.head_local, .35), 6, 6.5, [calf], False),
                 (calf.head_local.lerp(foot.head_local, .55), 6.5, 7, [calf], True), (calf.head_local.lerp(foot.head_local, .9), 5.5, 6, [calf], True),
                 (foot.head_local, 5.5, 7, [calf, foot], True), (foot.head_local.lerp(ball.head_local, .5), 5.5, 5, [foot], True),
                 (ball.head_local, 6, 3.8, [foot, ball], True), (toe, 4.2, 2.4, [ball], True)]
        previous = None
        for at, (centre, wide, deep, follow, boot) in enumerate(rings):
            ahead = (rings[min(at + 1, len(rings) - 1)][0] - rings[max(at - 1, 0)][0]).normalized()
            across = ahead.cross(Vector((0, 1, 0)) if abs(ahead.y) < .9 else Vector((1, 0, 0))).normalized()
            up = across.cross(ahead)
            ring = []
            for step in range(LEG_SIDES):
                turn = 2 * math.pi * step / LEG_SIDES
                vert = bm.verts.new(centre + across * math.cos(turn) * wide + up * math.sin(turn) * deep)
                for b in follow:
                    vert[deform][groups[b.name]] = 1 / len(follow)
                ring.append(vert)
            if previous:
                for step in range(LEG_SIDES):
                    face = bm.faces.new((previous[step], previous[(step + 1) % LEG_SIDES], ring[(step + 1) % LEG_SIDES], ring[step]))
                    face.material_index = 1 if boot else 0
                    for loop, u, v in zip(face.loops, (step, step + 1, step + 1, step), (at - 1, at - 1, at, at)):
                        loop[uv].uv = (u / LEG_SIDES, v / len(rings))
            previous = ring
        bm.faces.new(reversed(previous)).material_index = 1
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(plain("legs-cloth", cloth))
    mesh.materials.append(plain("legs-boot", boots))
    bpy.ops.object.select_all(action="DESELECT")
    leg.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = max(meshes, key=lambda obj: len(obj.data.vertices))
    bpy.ops.object.join()


def head(rig, bone):
    return rig.data.bones[bone].head_local.copy()


def digit(rig, side, finger):
    """A digit's joints from the knuckle, and its tip past the last joint."""
    joints = [head(rig, f"{finger}_0{i}_{side}") for i in (1, 2, 3)]
    return [*joints, joints[2] + (joints[2] - joints[1]) * .9]


def curling(rig, side, finger):
    """The way a digit curls at rest: the fingers together, toward the palm, and the thumb its own way."""
    bend = Vector()
    for name in ("thumb",) if finger == "thumb" else FINGERS[:4]:
        joints = digit(rig, side, name)
        bend += (joints[2] - joints[1]).normalized() - (joints[1] - joints[0]).normalized()
    return bend.normalized()


def tube(bm, points, radii, sides, each=None):
    """Closed rings of `radii` down `points`; each(vert, ring) marks a vertex of ring number `ring`."""
    first = previous = None
    for at, (centre, radius) in enumerate(zip(points, radii)):
        ahead = (points[min(at + 1, len(points) - 1)] - points[max(at - 1, 0)]).normalized()
        across = ahead.cross(Vector((0, 1, 0)) if abs(ahead.y) < .9 else Vector((1, 0, 0))).normalized()
        up = across.cross(ahead)
        ring = []
        for step in range(sides):
            turn = 2 * math.pi * step / sides
            vert = bm.verts.new(centre + (across * math.cos(turn) + up * math.sin(turn)) * radius)
            if each:
                each(vert, at)
            ring.append(vert)
        if previous:
            for step in range(sides):
                bm.faces.new((previous[step], previous[(step + 1) % sides], ring[(step + 1) % sides], ring[step]))
        first = first or ring
        previous = ring
    bm.faces.new(reversed(first))
    bm.faces.new(previous)


def arm_bones(rig, side):
    """The arm's bones from shoulder to hand, each with the stretch of the arm it carries."""
    shoulder, elbow, twist, wrist = (head(rig, f"{n}_{side}") for n in ("upperarm", "lowerarm", "lowerarm_twist_01", "hand"))
    return [(f"upperarm_{side}", shoulder, elbow), (f"lowerarm_{side}", elbow, twist), (f"lowerarm_twist_01_{side}", twist, wrist),
            (f"hand_{side}", wrist, head(rig, f"middle_01_{side}"))]


def arm_weights(bones, at):
    """Which of the arm's `bones` carry the point `at`: the nearest, shared with its neighbour near the joint between them."""
    def along(a, b):
        return min(max((at - a).dot(b - a) / (b - a).length_squared, 0), 1)
    nearest_at = min(range(len(bones)), key=lambda i: (at - bones[i][1].lerp(bones[i][2], along(*bones[i][1:]))).length)
    name, a, b = bones[nearest_at]
    t = along(a, b)
    if t < .25 and nearest_at > 0:
        share = .5 + 2 * t
        return {name: share, bones[nearest_at - 1][0]: 1 - share}
    if t > .75 and nearest_at + 1 < len(bones):
        share = .5 + 2 * (1 - t)
        return {name: share, bones[nearest_at + 1][0]: 1 - share}
    return {name: 1}


def lattice_noise(points, cell, seed):
    """Smooth noise from 0 to 1 at `points` (N, 3), its features about `cell` across on each axis."""
    grid = np.random.default_rng(seed).random((64, 64, 64))
    scaled = points / cell
    low = np.floor(scaled).astype(int)
    t = scaled - low
    t = t * t * (3 - 2 * t)
    total = np.zeros(len(points))
    for corner in range(8):
        offset = np.array([corner & 1, corner >> 1 & 1, corner >> 2 & 1])
        at = (low + offset) % 64
        share = np.prod(np.where(offset, t, 1 - t), axis=1)
        total += share * grid[at[:, 0], at[:, 1], at[:, 2]]
    return total


def ramp(colours, value):
    """`colours`' nearest step for `value` (N) from 0 to 1."""
    table = np.array(colours)
    return table[np.clip((value * len(table)).astype(int), 0, len(table) - 1)]


def paint(mesh, surface, forearm, fur_ramp):
    """
    Paints the claw arms' colours by what each texel is, the fur in `fur_ramp`: `surface` holds each vertex's kind, 0 fur, 1 bare finger and 2 nail.
    Fur streaks run down the arm (`forearm`, a rotation into a frame along it) and mottles black as the classic frames do.
    """
    size = CLAW_TEXTURE
    where = np.zeros((size, size, 3))
    kind = np.full((size, size), -1.0)
    mesh.calc_loop_triangles()
    uvs = mesh.uv_layers.active.data
    for triangle in mesh.loop_triangles:
        corners = np.array([uvs[i].uv for i in triangle.loops]) * size
        low = np.maximum(np.floor(corners.min(axis=0)).astype(int) - 1, 0)
        high = np.minimum(np.ceil(corners.max(axis=0)).astype(int) + 1, size - 1)
        xs, ys = np.meshgrid(np.arange(low[0], high[0] + 1), np.arange(low[1], high[1] + 1))
        a, b, c = corners
        area = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
        if abs(area) < 1e-9:
            continue
        px, py = xs + .5, ys + .5
        wb = ((px - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (py - a[1])) / area
        wc = ((b[0] - a[0]) * (py - a[1]) - (px - a[0]) * (b[1] - a[1])) / area
        wa = 1 - wb - wc
        inside = (wa >= -.02) & (wb >= -.02) & (wc >= -.02)
        verts = triangle.vertices
        points = [np.array(mesh.vertices[v].co) for v in verts]
        kinds = [surface[v] for v in verts]
        rows, columns = ys[inside], xs[inside]
        where[rows, columns] = wa[inside, None] * points[0] + wb[inside, None] * points[1] + wc[inside, None] * points[2]
        kind[rows, columns] = wa[inside] * kinds[0] + wb[inside] * kinds[1] + wc[inside] * kinds[2]
    filled = kind >= 0
    points = where[filled].copy()
    # Both arms read alike: the left's x folded onto the right's.
    points[:, 0] = -np.abs(points[:, 0])
    down = points @ np.array(forearm).T
    streak = lattice_noise(down, np.array((.55, .55, 3.2)), 1)
    tone = lattice_noise(down, np.array((5, 5, 9)), 2)
    patch = lattice_noise(down, np.array((1.5, 1.5, 3.2)), 3)
    fur = np.clip(streak * .75 + tone * .55 - .27, 0, .999)
    colours = ramp(fur_ramp, fur)
    mottled = patch + (streak - .5) * .5 > .7
    colours[mottled] = ramp(MOTTLE, streak[mottled])
    kinds = kind[filled]
    bare = (kinds >= .5) & (kinds < 1.5)
    colours[bare] = ramp(SKIN, np.clip(tone[bare] * .6 + streak[bare] * .5, 0, .999))
    nail = kinds >= 1.5
    colours[nail] = ramp(NAIL, np.clip(streak[nail] * .9 - .1, 0, .999))
    image = np.zeros((size, size, 3))
    image[filled] = colours
    # Cells beside an island take its colour, so the nearest sample at its edge never reads the empty sheet.
    for _ in range(6):
        grown = filled.copy()
        for axis, shift in ((0, 1), (0, -1), (1, 1), (1, -1)):
            near = np.roll(filled, shift, axis) & ~grown
            image[near] = np.roll(image, shift, axis)[near]
            grown |= near
        filled = grown
    return image


def claws(name):
    """
    A beast's arms, the set `name`, grown on the skeleton as legs() grows a leg, so nothing here comes from the pack's art: an arm and
    palm fused from tubes down the bones, a finger and curved nail on each digit and a shag of fur shards, with legs that
    match. The skeleton is an arm set's, so every clip plays on it as it plays on the rest.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 60
    imported = import_fbx(pack_root / f"Meshes/{SOURCES[CLAW_SKELETON][0]}.fbx")
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    rig = next(o for o in imported if o.type == "ARMATURE")
    remove([o for o in imported if o.type == "MESH"])

    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = rig
    bm = bmesh.new()
    for side in "lr":
        bones = arm_bones(rig, side)
        shoulder, elbow, wrist = bones[0][1], bones[0][2], bones[3][1]
        tube(bm, [shoulder.lerp(elbow, .35), shoulder.lerp(elbow, .7), elbow, elbow.lerp(wrist, .3), elbow.lerp(wrist, .6), elbow.lerp(wrist, .88), wrist],
             [7, 6.6, 5.8, 6.2, 5.2, 4.2, 3.9], 10)
        for finger in FINGERS[:4]:
            knuckle = head(rig, f"{finger}_01_{side}")
            tube(bm, [wrist, wrist.lerp(knuckle, .5), knuckle], [3.4, 3, 1.9], 8)
        tube(bm, [wrist, head(rig, f"thumb_01_{side}")], [2.6, 1.9], 8)
    bm.to_mesh(mesh)
    bm.free()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    remesh = obj.modifiers.new("fuse", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = .45
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth = obj.modifiers.new("smooth", "SMOOTH")
    smooth.iterations = 6
    smooth.factor = .6
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    decimate = obj.modifiers.new("low", "DECIMATE")
    decimate.ratio = 3000 / len(mesh.polygons)
    bpy.ops.object.modifier_apply(modifier=decimate.name)

    groups = {}

    def weigh(vert, weights):
        for bone, share in weights.items():
            if bone not in groups:
                groups[bone] = obj.vertex_groups.new(name=bone).index
            vert[deform][groups[bone]] = share

    bm = bmesh.new()
    bm.from_mesh(mesh)
    deform = bm.verts.layers.deform.verify()
    kind = bm.verts.layers.float.new("kind")
    bm.normal_update()
    arm = list(bm.verts)
    random = np.random.default_rng(11)
    for vert in arm:
        weigh(vert, arm_weights(arm_bones(rig, "l" if vert.co.x > 0 else "r"), vert.co))
        vert[kind] = 0.0
    # The shag: a three-sided shard on a random spot of the arm, lying back toward the hand, sharing the spot's weights.
    faces = list(bm.faces)
    shards = set()
    for face in (faces[i] for i in random.integers(0, len(faces), 900)):
        side = "l" if face.calc_center_median().x > 0 else "r"
        bones = arm_bones(rig, side)
        centre = face.calc_center_median()
        down = (bones[3][1] - bones[1][1]).normalized() if (centre - bones[1][1]).dot(bones[3][1] - bones[1][1]) > 0 else (bones[0][2] - bones[0][1]).normalized()
        out = face.normal
        lie = (down * .95 + out * .3).normalized()
        across = out.cross(down).normalized()
        wide = random.uniform(.4, .7)
        tip = bm.verts.new(centre + lie * random.uniform(1.2, 2.4))
        base = [bm.verts.new(centre + across * wide * math.cos(turn) + down.cross(across) * wide * math.sin(turn) - out * .4)
                for turn in (0, 2.1, 4.2)]
        source = face.verts[0]
        for vert in (tip, *base):
            vert[deform] = source[deform]
            vert[kind] = 0.0
        for i in range(3):
            shards.add(bm.faces.new((base[i], base[(i + 1) % 3], tip)))
    for side in "lr":
        hand = f"hand_{side}"
        for finger in FINGERS:
            bend = curling(rig, side, finger)
            joints = digit(rig, side, finger)
            names = [f"{finger}_0{i}_{side}" for i in (1, 2, 3)]
            base = joints[0] - (joints[1] - joints[0]).normalized() * 1.2
            # Each ring (where it sits, how thick, what carries it, what it is): fur to the first knuckle, bare beyond.
            rings = [(base, 1.05, {hand: 1}, 0), (joints[0], 1.15, {hand: .5, names[0]: .5}, 0),
                     (joints[0].lerp(joints[1], .5), 1, {names[0]: 1}, 0), (joints[1], 1.05, {names[0]: .5, names[1]: .5}, 1),
                     (joints[1].lerp(joints[2], .5), .9, {names[1]: 1}, 1), (joints[2], .95, {names[1]: .5, names[2]: .5}, 1),
                     (joints[2].lerp(joints[3], .5), .85, {names[2]: 1}, 1), (joints[3], .7, {names[2]: 1}, 1)]

            def mark(vert, ring, rings=rings):
                weigh(vert, rings[ring][2])
                vert[kind] = float(rings[ring][3])

            tube(bm, [r[0] for r in rings], [r[1] for r in rings], 8, mark)
            ahead = (joints[3] - joints[2]).normalized()
            length = 5.5 if finger == "thumb" else 7
            steps = [i / 5 for i in range(6)]

            def nail(vert, ring, last=names[2]):
                weigh(vert, {last: 1})
                vert[kind] = 1.2 if ring == 0 else 2.0

            tube(bm, [joints[3] - ahead + ahead * length * t + bend * length * .45 * t * t for t in steps],
                 [.8 * (1 - t) ** .8 + .03 for t in steps], 6, nail)
    surface = [0.0] * len(bm.verts)
    bm.verts.index_update()
    for vert in bm.verts:
        surface[vert.index] = vert[kind]
    bm.verts.layers.float.remove(kind)
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for face in bm.faces:
        face.smooth = face not in shards
    bm.to_mesh(mesh)
    bm.free()
    obj.modifiers.new("rig", "ARMATURE").object = rig

    mesh.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    wrist, elbow = head(rig, "hand_r"), head(rig, "lowerarm_r")
    down = (wrist - elbow).normalized()
    across = down.cross(Vector((0, 1, 0))).normalized()
    fur = CLAW_FUR[name]
    mesh.materials.append(nearest(name, paint(mesh, surface, Matrix((across, across.cross(down), down)), fur)))
    legs(rig, [obj], fur[3], fur[1])
    return rig


def hand_shape(rig, side, curls, spread=0):
    """One hand's digits, by bone: their rest, each joint curled by `curls`, knuckle first, each knuckle spread from the middle finger by `spread`."""
    basis = {}
    middle = head(rig, f"middle_01_{side}")
    for finger in FINGERS:
        bend = curling(rig, side, finger)
        joints = digit(rig, side, finger)
        for joint, curl in enumerate(curls):
            bone = rig.data.bones[f"{finger}_0{joint + 1}_{side}"]
            ahead = (joints[joint + 1] - joints[joint]).normalized()
            local = bone.matrix_local.to_3x3().inverted()
            turn = Matrix.Rotation(math.radians(curl), 4, local @ ahead.cross(bend).normalized())
            if joint == 0 and finger != "middle":
                outward = joints[0] - middle
                turn = turn @ Matrix.Rotation(math.radians(spread), 4, local @ ahead.cross(outward).normalized())
            basis[bone.name] = turn
    return basis


def open_hand(rig):
    """The claw arms' fingers, held by CLAW_CURL and CLAW_SPREAD whatever a clip's hands do."""
    return hand_shape(rig, "l", CLAW_CURL, CLAW_SPREAD) | hand_shape(rig, "r", CLAW_CURL, CLAW_SPREAD)


def frame_of(forward, up):
    """A rotation whose columns are a right-handed frame: `forward`, `up` squared against it, and their cross."""
    forward = forward.normalized()
    up = (up - forward * up.dot(forward)).normalized()
    return Matrix((forward, up, forward.cross(up))).transposed()


def hand_rest(rig, side):
    """A hand's frame at rest (along the hand, toward its thumb), and its palm against the wrist, all in the arms' space."""
    wrist, knuckle, thumb = (head(rig, f"{n}_{side}") for n in ("hand", "middle_01", "thumb_01"))
    frame = frame_of(knuckle - wrist, thumb - wrist)
    return frame, (knuckle - wrist) * .55 + curling(rig, side, "index") * 2.6


def solve_arm(rig, base, side, wrist, turn, pole, shape):
    """
    Where `base`, a clip's frame, puts one arm to reach `wrist` with the hand turned to `turn` (the hand's frame in the
    arms' space), the elbow bent toward `pole`, and the digits in `shape`: the bones of the arm, from the upper arm down.
    """
    names = {n: f"{n}_{side}" for n in ("upperarm", "lowerarm", "hand", "lowerarm_twist_01", "upperarm_twist_01")}
    bones = rig.data.bones
    shoulder = base[names["upperarm"]].to_translation()
    upper = (bones[names["lowerarm"]].head_local - bones[names["upperarm"]].head_local).length
    lower = (bones[names["hand"]].head_local - bones[names["lowerarm"]].head_local).length
    reach = wrist - shoulder
    distance = min(reach.length, (upper + lower) * .995)
    along = reach.normalized()
    wrist = shoulder + along * distance
    # The elbow, in the plane of the reach and the pole.
    near = (distance ** 2 + upper ** 2 - lower ** 2) / (2 * distance)
    out = (pole - along * pole.dot(along)).normalized()
    elbow = shoulder + along * near + out * math.sqrt(max(upper ** 2 - near ** 2, 0))

    def placed(at, rotation):
        return Matrix.Translation(at) @ rotation.to_4x4()

    def swung(name, child, start, end):
        was = (base[child].to_translation() - base[name].to_translation()).normalized()
        return was.rotation_difference((end - start).normalized()).to_matrix() @ base[name].to_3x3().normalized()

    upper_turn = swung(names["upperarm"], names["lowerarm"], shoulder, elbow)
    forearm = swung(names["lowerarm"], names["hand"], elbow, wrist)
    posed = {names["upperarm"]: placed(shoulder, upper_turn), names["lowerarm"]: placed(elbow, forearm)}
    rest = {n: bones[names[n]].matrix_local.to_3x3() for n in names}
    # The hand: `turn` in place of its frame at rest, and its roll shared with the forearm's twist joint.
    hand = turn @ hand_rest(rig, side)[0].inverted() @ rest["hand"]
    posed[names["hand"]] = placed(wrist, hand)
    axis = (wrist - elbow).normalized()
    fore_moved = forearm @ rest["lowerarm"].inverted()
    drift = (fore_moved.inverted() @ hand @ rest["hand"].inverted()).to_quaternion()
    roll = 2 * math.atan2(Vector(drift[1:]).dot(fore_moved.inverted() @ axis), drift.w)
    posed[names["lowerarm_twist_01"]] = placed(elbow.lerp(wrist, .5), Matrix.Rotation(roll / 2, 3, axis) @ fore_moved @ rest["lowerarm_twist_01"])
    posed[names["upperarm_twist_01"]] = placed(shoulder, upper_turn @ rest["upperarm"].inverted() @ rest["upperarm_twist_01"])
    for finger in FINGERS:
        parent = names["hand"]
        for joint in (1, 2, 3):
            name = f"{finger}_0{joint}_{side}"
            local = bones[parent].matrix_local.inverted() @ bones[name].matrix_local
            posed[name] = posed[parent] @ local @ shape.get(name, Matrix.Identity(4))
            parent = name
    return posed


def smooth(t):
    t = min(max(t, 0), 1)
    return t * t * (3 - 2 * t)


def bow_pose(fired):
    """Where the bow's grip is and how it is turned (cm and a frame in the view), `fired` of the way from rest to the aim."""
    at = BOW_REST.lerp(BOW_AIM, fired)
    forward = BOW_REST_FORWARD.lerp(BOW_AIM_FORWARD, fired)
    up = BOW_REST_UP.lerp(BOW_AIM_UP, fired)
    return at, frame_of(forward, up)


def bow_clip(rig, base, stem):
    """
    A clip the pack has no bow for, made on the skeleton from `base`, a frame of another clip: the left hand carries the
    bow from where it rests to the aim, and the right draws the string to the eye. Returns the clip's samples, as bake() does.
    """
    length, shape = BOW_CLIPS[stem]
    bones = [b for b in rig.data.bones if b.name in base]
    left_hand, hook_hand, open_hand_r = hand_shape(rig, "l", BOW_GRIP), hand_shape(rig, "r", BOW_HOOK), hand_shape(rig, "r", BOW_OPEN)
    rest_frame, palm = hand_rest(rig, "l")
    unrig = rig.matrix_world.inverted()
    start = {name: unrig @ m for name, m in base.items() if name in rig.data.bones}
    frames, samples = [], []
    for frame in range(length + 1):
        fired, pull, hand_in, sway, loosed = shape(frame / length)
        grip, turn = bow_pose(fired)
        grip = grip + Vector((math.sin(sway * 2 * math.pi), 0, math.sin(sway * 4 * math.pi) * .7)) * BOW_SWAY
        left = solve_arm(rig, start, "l", grip - turn.col[1] * BOW_HAND_DROP - turn @ rest_frame.inverted() @ palm, turn, Vector((-.6, -.2, -.8)), left_hand)
        nock = grip - turn.col[0] * (BOW_BRACE + (BOW_FULL_DRAW - BOW_BRACE) * pull)
        # The hand comes up to the string from below, so that at the aim all of it is out of view but its fingers.
        right_turn = frame_of(turn.col[0] + BOW_HOOK_RISE, Vector((.2, -.5, 1)))
        hook = nock - right_turn.col[0] * BOW_HOOK_REACH - turn.col[0] * loosed * BOW_FOLLOW
        wrist = BOW_RIGHT_REST.lerp(hook, smooth(hand_in))
        right = solve_arm(rig, start, "r", wrist, right_turn, Vector((.6, -.7, -.5)), mixed(open_hand_r, hook_hand, smooth(hand_in) * (1 - smooth(loosed))))
        posed = start | left | right
        frames.append(pose_basis(bones, posed))
        samples.append({name: rig.matrix_world @ m for name, m in posed.items()} | {"weapon_r": rig.matrix_world @ posed["hand_r"], "weapon_l": rig.matrix_world @ posed["hand_l"]})
    keyed(rig, stem, bones, frames)
    return samples


def bow_seat(sample):
    """Where the aimed bow sits on the off hand's joint in the Engine's terms: cm, degrees for HeldFrames.Turn, and scale."""
    to_gltf = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
    hand = to_gltf @ sample["hand_l"]
    at, turn = bow_pose(1)
    forward, up = turn.col[0], turn.col[1]
    # The bow's own axes: +X the archer's left, +Y its belly, ahead, and -Z its upper limb.
    model = Matrix((-forward.cross(up), forward, -up)).transposed().to_4x4()
    model.translation = at / 100
    seat = hand.inverted() @ to_gltf @ model
    rotation = seat.to_3x3().normalized()
    x, y, z = rotation.col[0], rotation.col[1], rotation.col[2]
    degrees_of = [math.degrees(a) for a in (math.asin(max(-1, min(1, -z.y))), math.atan2(z.x, z.z), math.atan2(x.y, y.y))]
    return {"at": rounded(seat.to_translation()), "turn": [round(d, 2) for d in degrees_of], "scale": round(seat.to_scale().x, 3)}


def ride_clip(rig, base, stem, seconds=2):
    """The hands on the reins, made as the bow's clips are: a loop of both fists held low ahead, drifting a little."""
    bones = [b for b in rig.data.bones if b.name in base]
    unrig = rig.matrix_world.inverted()
    start = {name: unrig @ m for name, m in base.items() if name in rig.data.bones}
    frames, samples = [], []
    length = seconds * 60
    for frame in range(length + 1):
        drift = math.sin(frame / length * 2 * math.pi) * RIDE_SWAY
        posed = dict(start)
        for side, mirror in (("r", 1), ("l", -1)):
            wrist = Vector((RIDE_HAND.x * mirror, RIDE_HAND.y - drift, RIDE_HAND.z + drift * mirror))
            point = Vector((RIDE_POINT.x * mirror, RIDE_POINT.y, RIDE_POINT.z))
            posed |= solve_arm(rig, start, side, wrist, frame_of(point, Vector((0, .2, 1))), Vector((.7 * mirror, -.4, -.6)), hand_shape(rig, side, BOW_GRIP))
        frames.append(pose_basis(bones, posed))
        samples.append({name: rig.matrix_world @ m for name, m in posed.items()} | {"weapon_r": rig.matrix_world @ posed["hand_r"], "weapon_l": rig.matrix_world @ posed["hand_l"]})
    keyed(rig, stem, bones, frames)
    return samples


def mixed(a, b, share):
    """Two poses by bone, `share` of the way from `a` to `b`."""
    return {name: a[name].to_quaternion().slerp(b[name].to_quaternion(), share).to_matrix().to_4x4() for name in a}



def world(rig, bone):
    return rig.matrix_world @ rig.pose.bones[bone].matrix


def unscaled(matrix):
    return Matrix.LocRotScale(matrix.translation, matrix.to_quaternion(), None)


def swapped(name):
    """The bone on the other side of the body, or the bone itself on its middle."""
    return name[:-1] + {"l": "r", "r": "l"}[name[-1]] if name.endswith(("_l", "_r")) else name


def bake(rig, stem, hands=None):
    """
    Bakes clip `stem` onto the arms as an action of that name, as its camera bone sees it; returns its samples. A stem
    ending in MIRRORED is the clip the other way round, for a pack with one punch and two fists. The pack's own clips have
    no camera bone: they are made for the arms, whose camera_mount is the eye. `hands` holds bones to a pose of their own
    through the clip: the claw arms' open fingers, whatever a clip's hand grips.
    """
    mirrored = stem.endswith(MIRRORED)
    imported = import_fbx(clip_file(stem.removesuffix(MIRRORED)))
    source = next(o for o in imported if o.type == "ARMATURE")
    eye = "camera" if "camera" in source.pose.bones else "camera_mount"
    held = {side: f"weapon_{side}" if f"weapon_{side}" in source.pose.bones else f"hand_{side}" for side in "lr"}
    clip = source.animation_data.action
    first, last = (int(f) for f in clip.frame_range)
    bones = [b for b in rig.data.bones if b.name in source.pose.bones]
    # The two skeletons share their rest pose's joints but not their bones' axes, so each bone takes the clip's turn
    # from its own rest: its rest frame as the clip's bone at rest sees it.
    rest = {b.name: (source.matrix_world @ source.data.bones[b.name].matrix_local).inverted() @ rig.matrix_world @ b.matrix_local for b in bones}
    # The IK helper bones (ik_*) skin nothing and are placed apart in each file.
    if eye == "camera":
        worst = max(((rig.matrix_world @ b.matrix_local).translation
                     - (source.matrix_world @ source.data.bones[b.name].matrix_local).translation).length for b in bones if not b.name.startswith("ik_"))
        if worst > 0.01:
            raise SystemExit(f"clip '{stem}': its rest pose's joints lie up to {worst * 100:.1f} cm from the arms'")
    else:
        # The pack's own clips are the arms' own skeleton in another rest: each bone is simply where the clip puts it.
        rest = {b.name: Matrix.Identity(4) for b in bones}
    unrig = rig.matrix_world.inverted()
    # The view's left-right flip in the arms' space, and how each bone's own frame turns against its other side's at rest.
    flip = unrig @ Matrix.Diagonal((-1, 1, 1, 1)) @ rig.matrix_world
    body = Matrix.Diagonal((-1, 1, 1, 1)) if abs(rig.data.bones["clavicle_l"].head_local.x) > abs(rig.data.bones["clavicle_l"].head_local.y) else Matrix.Diagonal((1, -1, 1, 1))
    turned = {b.name: (body @ rig.data.bones[swapped(b.name)].matrix_local).inverted() @ b.matrix_local for b in bones}
    frames, samples = [], []
    for frame in range(first, last + 1):
        bpy.context.scene.frame_set(frame)
        view = (VIEW if eye == "camera" else VIEW @ PACK_EYE) @ unscaled(world(source, eye)).inverted()
        posed = {b.name: unrig @ view @ world(source, b.name) @ rest[b.name] for b in bones}
        if mirrored:
            posed = {b.name: flip @ posed[swapped(b.name)] @ turned[b.name] for b in bones}
        frames.append(pose_basis(bones, posed) | (hands or {}))
        samples.append({name: rig.matrix_world @ m for name, m in posed.items()} | {"weapon_r": view @ world(source, held["r"]), "weapon_l": view @ world(source, held["l"])})
    remove(imported)
    bpy.data.actions.remove(clip)
    keyed(rig, stem, bones, frames)
    return samples


def pose_basis(bones, posed):
    """Each of `bones`' pose against its rest, from where `posed` puts it in the arms' space."""
    basis = {}
    for b in bones:
        if b.parent is None or b.parent.name not in posed:
            basis[b.name] = b.matrix_local.inverted() @ posed[b.name]
        else:
            local = b.parent.matrix_local.inverted() @ b.matrix_local
            basis[b.name] = local.inverted() @ posed[b.parent.name].inverted() @ posed[b.name]
    return basis


def keyed(rig, stem, bones, frames):
    """`frames`, each bone's pose by name, as an action of the arms named `stem`."""
    action = bpy.data.actions.new(stem)
    rig.animation_data_create()
    rig.animation_data.action = action
    for b in bones:
        pb = rig.pose.bones[b.name]
        pb.rotation_mode = "QUATERNION"
        path = f'pose.bones["{b.name}"]'
        tracks = [("location", 3, lambda m: m.to_translation()), ("rotation_quaternion", 4, lambda m: m.to_quaternion()),
                  ("scale", 3, lambda m: m.to_scale())]
        for prop, size, read in tracks:
            values = [read(f[b.name]) for f in frames]
            if prop == "rotation_quaternion":
                for i in range(1, len(values)):
                    if values[i].dot(values[i - 1]) < 0:
                        values[i] = -values[i]
            for index in range(size):
                curve = action.fcurve_ensure_for_datablock(rig, f"{path}.{prop}", index=index)
                curve.keyframe_points.add(len(values))
                curve.keyframe_points.foreach_set("co", [c for i, v in enumerate(values) for c in (i, v[index])])
                curve.update()
    rig.animation_data.action = None
    action.use_fake_user = True


def export(path, keep):
    """Exports the arms with only the actions named in `keep`."""
    for action in list(bpy.data.actions):
        if action.name not in keep:
            bpy.data.actions.remove(action)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", export_animation_mode="ACTIONS",
                              export_image_format="AUTO", export_yup=True, export_frame_range=False,
                              export_force_sampling=True)
    data = path.read_bytes()
    names = [a["name"] for a in json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]]).get("animations", [])]
    if sorted(names) != sorted(keep):
        raise SystemExit(f"{path}: exported clips {names}, expected {sorted(keep)}")


def camera(v):
    return TO_CAMERA @ v


def hand(sample):
    """The right hand's canonical frame (camera space, as the Engine measures it from the knuckles) and its joint."""
    wrist = camera(sample["hand_r"].translation)
    y = (camera(sample["middle_01_r"].translation) - wrist).normalized()
    across = camera(sample["pinky_01_r"].translation) - camera(sample["index_01_r"].translation)
    x = (across - y * across.dot(y)).normalized()
    return Matrix((x, y, x.cross(y))).transposed(), wrist


def weapon(sample):
    """Where the clip's weapon bone sits in the hand's canonical frame, and its axes there."""
    canonical, wrist = hand(sample)
    bone = sample["weapon_r"]
    return canonical.transposed() @ (camera(bone.translation) - wrist), canonical.transposed() @ TO_CAMERA @ bone.to_3x3().normalized()


def degrees(turn):
    """Degrees about X, Y and Z that HeldFrames.Turn turns back into a rotation matrix (System.Numerics yaw-pitch-roll)."""
    cx, cy, cz = turn.col[0], turn.col[1], turn.col[2]
    return [round(math.degrees(a), 2) for a in (math.asin(max(-1, min(1, -cz.y))), math.atan2(cz.x, cz.z), math.atan2(cx.y, cy.y))]


# The stem that names a clip played the other way round.
MIRRORED = ".mirrored"

# The commit's share of the time after the hit, which is close to this in every weapon class's tuning. A cast's commit
# is the moment the spell leaves, so nearly all of its time after the hit is recovery.
COMMIT_SHARE = 0.3
CAST_COMMIT_SHARE = 0.06


def marks(tips, speeds, commit_share=COMMIT_SHARE):
    """
    Suggested marks from a point 30 cm along the blade, per frame (camera space): the strike where the swing meets what
    is in front, the frame of its fastest burst (the frames around its top speed at a third of it or more) whose point
    lies nearest the centre of the view; the end where the clip has come back (the point stays within 3 cm of where the clip leaves it); and the
    recovery commit_share of the way between, so the clip plays after the hit at one pace over the rules' commit and
    recovery.
    """
    peak = max(range(len(speeds)), key=lambda i: speeds[i])
    first, last_fast = peak, peak
    while first > 0 and speeds[first - 1] >= speeds[peak] / 3:
        first -= 1
    while last_fast < len(speeds) - 1 and speeds[last_fast + 1] >= speeds[peak] / 3:
        last_fast += 1
    strike = min(range(first, last_fast + 1), key=lambda i: tips[i].angle(Vector((0, 0, -1))))
    last = len(tips) - 1
    end = next(i for i in range(strike, last + 1) if all((tips[j] - tips[-1]).length < 0.03 for j in range(i, last + 1)))
    return {"start": 0, "strike": round(strike / last, 3), "recover": round((strike + (end - strike) * commit_share) / last, 3), "end": round(end / last, 3)}


def kicked(frames, suggested):
    """A kick's marks: its strike where a foot reaches furthest ahead of the eye, the rest as `suggested`."""
    reach = [max(-camera(s[f"ball_{side}"].translation).z for side in "lr") for s in frames]
    strike = round(reach.index(max(reach)) / (len(frames) - 1), 3)
    end = suggested["end"]
    return {"start": 0, "strike": strike, "recover": round(strike + (end - strike) * COMMIT_SHARE, 3), "end": end}


def rounded(v, places=4):
    return [round(c, places) for c in v]


rig_clip = definition["rig"]["clip"]
families = definition["families"]
shield = definition["shield"]


def guard_stems(guard):
    return [guard[part]["clip"] for part in ("raise", "hold", "lower")] if guard else []


stems = [rig_clip, *guard_stems(shield["guard"]), *(stem for family in families.values() for stem in (
    family["idle"]["clip"], family["powerHold"]["clip"], *guard_stems(family.get("guard")), *(clip["clip"] for clip in family["strikes"].values())))]
# Every set's joints are the clips' (bake refuses others), so the samples of any set measure them all.
for name, arm_set in definition["armSets"].items():
    rig = claws(name) if name in CLAW_FUR else arms(name)
    hands = open_hand(rig) if name in CLAW_FUR else None
    samples = {stem: bake(rig, stem, hands) for stem in dict.fromkeys(stems) if stem not in BOW_CLIPS and stem != RIDE_CLIP}
    samples |= {stem: bow_clip(rig, samples[rig_clip][0], stem) for stem in stems if stem in BOW_CLIPS}
    samples[RIDE_CLIP] = ride_clip(rig, samples[rig_clip][0], RIDE_CLIP)
    export(out / arm_set["model"], list(samples))
    retro()
    export(out / arm_set["retroModel"], list(samples))

measure = {"shoulder": rounded(camera(samples[rig_clip][0]["upperarm_r"].translation)), "families": {}}
palm = None
for name, family in families.items():
    # The family's weapon bone in the hand at the start of its idle; the dagger family's is the rig's palm.
    at, axes = weapon(samples[family["idle"]["clip"]][0])
    if palm is None:
        palm = at
    look = axes @ LOOK_IN_BONE
    strikes = {}
    for action, clip in family["strikes"].items():
        # A cast is the left hand's, and a punch its fist's: the hit is the hand thrown forward, not a blade.
        cast = action == "cast"
        fist = "hand_l" if cast or clip["clip"].endswith(MIRRORED) else "hand_r" if family.get("class") == "hand-to-hand" else None
        tips = [camera(s[fist].translation) if fist else camera(s["weapon_r"].translation + s["weapon_r"].to_3x3().normalized().col[2] * 0.3) for s in samples[clip["clip"]]]
        speeds = [round((tips[i + 1] - tips[i]).length * 60, 2) for i in range(len(tips) - 1)]
        suggested = marks(tips, speeds, CAST_COMMIT_SHARE if cast else COMMIT_SHARE)
        if action == "kick":
            suggested = kicked(samples[clip["clip"]], suggested)
        strikes[action] = {"clip": clip["clip"], "frames": len(tips), "seconds": round((len(tips) - 1) / 60, 4),
                           "marks": suggested, "tipSpeed": speeds}
    measure["families"][name] = {
        "idleSeconds": round((len(samples[family["idle"]["clip"]]) - 1) / 60, 4),
        "powerHoldSeconds": round((len(samples[family["powerHold"]["clip"]]) - 1) / 60, 4),
        "grip": rounded(look.transposed() @ (palm - at)),
        "hold": degrees(look.transposed()),
        "strikes": strikes,
    }
measure["palm"] = rounded(palm)
measure["bowSeat"] = bow_seat(samples["BOW_Hold"][0])
# Where the clips carry a shield: the weapon_l bone in hand_l's own frame, at the height of the raised guard.
raised = samples[shield["guard"]["hold"]["clip"]][0]
carried = raised["hand_l"].inverted() @ raised["weapon_l"]
measure["shieldSeat"] = {"at": rounded(carried.translation), "turn": rounded(carried.to_euler("XYZ"))}
measure["guardSeconds"] = {stem: round((len(samples[stem]) - 1) / 60, 4) for stem in dict.fromkeys(
    [*guard_stems(shield["guard"]), *(stem for family in families.values() for stem in guard_stems(family.get("guard")))])}
(out / "measure.json").write_text(json.dumps(measure, indent=1))
print("BUILT", out)
