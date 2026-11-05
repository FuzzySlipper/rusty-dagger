"""Builds the first-person arms with their clips from the old game's MedievalFPArms and FPP melee clips
(run under Blender 5 by scripts/fp-arms/build.sh; scripts/fp-arms/README.md).

usage: blender -b --factory-startup --python scripts/fp-arms/build.py -- SOURCES DEFINITION OUT_DIR

SOURCES is local/fp-melee (scripts/fp-arms/copy-sources.sh). DEFINITION is the held definition, which names every clip
the game plays: the rig's own clip and each family's idle, power hold and attack clips, by their FBX file stems. Writes into OUT_DIR:

- each of the definition's armSets: its model, the arms (mesh, skeleton, colour and normal maps) with every clip the
  definition names, and its retroModel, the same with its colour map alone, small and drawn nearest, which the heldRetro
  style draws. The clips are the model's own, not clip packs: the Engine shares an opened model's clip packs between
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

sources, definition_path, out = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])
definition = json.loads(definition_path.read_text())
pack_root = sources / "MedievalFPArms"
# Each arm set's mesh file, the stem and colour of its textures in the pack, and how much its colour map is lifted: the
# chainmail's is painted for metal shading, which the arms don't use, and would draw nearly black.
SOURCES = {
    "male-leather": ("FPArms_Male-Leather-Armor_LPR_LOD0", "FPArms_Male-Leather-Armor", "Medium_COL.png", 1),
    "female-leather": ("FPArms_Female-Leather-Armor_LOD0_LPR", "FPArms_Female-Leather-Armor", "Medium_COL.png", 1),
    "male-cloth": ("FPArms_Male-Cloth-LOD0_LPR", "FPArms_Male-Medieval-Cloth", "Medium_COL.png", 1),
    "female-cloth": ("FPArms_Female-Medieval-Cloth_LOD0_LPR", "FPArms_Female-Medieval-Cloth", "Medium_COL.png", 1),
    "male-chainmail": ("FPArms_Male-Chainmail_LPR_LOD0", "FPArms_Male-Chainmail", "COL.png", 2.5),
}
TEXTURE = 1024
# The legs' rings and their small plain textures: nothing of the pack's art goes into them.
LEG_SIDES = 10
LEG_TEXTURE = 32
# The retro arms (heldRetro) draw the colour map alone, this small and unfiltered, as Daggerfall draws its world.
RETRO_TEXTURE = 256
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


def load(path, size, colour=True, lift=1):
    """
    An image scaled down to at most `size` a side, its colours multiplied by `lift`; its colour space is set first, as
    setting it reloads the file.
    """
    image = bpy.data.images.load(str(path))
    if not colour:
        image.colorspace_settings.name = "Non-Color"
    if max(image.size) > size:
        image.scale(size, size)
    if lift != 1:
        pixels = np.empty(len(image.pixels), dtype=np.float32)
        image.pixels.foreach_get(pixels)
        rgba = pixels.reshape(-1, 4)
        rgba[:, :3] = np.minimum(rgba[:, :3] * lift, 1)
        image.pixels.foreach_set(pixels)
        image.update()
    return image


def texture(material, colour_map, normal_map, lift=1):
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    colour = nodes.new("ShaderNodeTexImage")
    colour.label = "colour"
    colour.image = load(colour_map, TEXTURE, lift=lift)
    links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Metallic"].default_value = 0.0
    if normal_map is None:
        # Fur, which has no normal map: its alpha cuts its edges, a Greater Than node being how glTF's alpha mask exports.
        cut = nodes.new("ShaderNodeMath")
        cut.operation = "GREATER_THAN"
        cut.inputs[1].default_value = 0.5
        links.new(colour.outputs["Alpha"], cut.inputs[0])
        links.new(cut.outputs[0], bsdf.inputs["Alpha"])
        return
    normal = nodes.new("ShaderNodeTexImage")
    normal.label = "normal"
    normal.image = load(normal_map, TEXTURE, colour=False)
    mapped = nodes.new("ShaderNodeNormalMap")
    links.new(normal.outputs["Color"], mapped.inputs["Color"])
    links.new(mapped.outputs["Normal"], bsdf.inputs["Normal"])


def retro():
    """Turns the arms' materials into the retro arms': the colour map at RETRO_TEXTURE, drawn nearest, no normal map."""
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            nodes = slot.material.node_tree.nodes
            for node in [n for n in nodes if n.type == "NORMAL_MAP" or n.label == "normal"]:
                nodes.remove(node)
            colour = next(n for n in nodes if n.label == "colour")
            colour.image.scale(RETRO_TEXTURE, RETRO_TEXTURE)
            colour.interpolation = "Closest"


def arms(name):
    """The arm set `name` with its textures, as one mesh; the pack's own unarmed clip is dropped."""
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
        fur = "_Fur" in obj.name
        for slot in obj.material_slots:
            texture(slot.material, pack_root / f"Textures/{stem}_Fur_COL.tga" if fur else pack_root / f"Textures/{stem}_{colour}",
                    None if fur else pack_root / f"Textures/{stem}_NRM.png", lift)
    legs(rig, meshes)
    return rig


def plain(name, colour):
    """A rough material of `colour` (sRGB 0-1) on a small noisy texture, drawn nearest, labelled for retro()."""
    random = np.random.default_rng(sum(name.encode()))
    grain = 1 + random.normal(0, 0.09, (LEG_TEXTURE, LEG_TEXTURE, 1))
    pixels = np.concatenate([np.clip(np.array(colour) * grain, 0, 1), np.ones((LEG_TEXTURE, LEG_TEXTURE, 1))], axis=2)
    image = bpy.data.images.new(name, LEG_TEXTURE, LEG_TEXTURE)
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


def legs(rig, meshes):
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
    mesh.materials.append(plain("legs-cloth", (.32, .28, .23)))
    mesh.materials.append(plain("legs-boot", (.3, .19, .11)))
    bpy.ops.object.select_all(action="DESELECT")
    leg.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = max(meshes, key=lambda obj: len(obj.data.vertices))
    bpy.ops.object.join()


def world(rig, bone):
    return rig.matrix_world @ rig.pose.bones[bone].matrix


def unscaled(matrix):
    return Matrix.LocRotScale(matrix.translation, matrix.to_quaternion(), None)


def swapped(name):
    """The bone on the other side of the body, or the bone itself on its middle."""
    return name[:-1] + {"l": "r", "r": "l"}[name[-1]] if name.endswith(("_l", "_r")) else name


def bake(rig, stem):
    """
    Bakes clip `stem` onto the arms as an action of that name, as its camera bone sees it; returns its samples. A stem
    ending in MIRRORED is the clip the other way round, for a pack with one punch and two fists. The pack's own clips have
    no camera bone: they are made for the arms, whose camera_mount is the eye.
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
        basis = {}
        for b in bones:
            if b.parent is None or b.parent.name not in posed:
                basis[b.name] = b.matrix_local.inverted() @ posed[b.name]
            else:
                local = b.parent.matrix_local.inverted() @ b.matrix_local
                basis[b.name] = local.inverted() @ posed[b.parent.name].inverted() @ posed[b.name]
        frames.append(basis)
        samples.append({name: rig.matrix_world @ m for name, m in posed.items()} | {"weapon_r": view @ world(source, held["r"]), "weapon_l": view @ world(source, held["l"])})
    remove(imported)
    bpy.data.actions.remove(clip)

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
    return samples


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
    return [guard[phase]["clip"] for phase in ("raise", "hold", "lower")]


stems = [rig_clip, *guard_stems(shield["guard"]), *(stem for family in families.values() for stem in (
    family["idle"]["clip"], family["powerHold"]["clip"], *guard_stems(family["guard"]), *(clip["clip"] for clip in family["strikes"].values())))]
# Every set's joints are the clips' (bake refuses others), so the samples of any set measure them all.
for name, arm_set in definition["armSets"].items():
    rig = arms(name)
    samples = {stem: bake(rig, stem) for stem in dict.fromkeys(stems)}
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
# Where the clips carry a shield: the weapon_l bone in hand_l's own frame, at the height of the raised guard.
raised = samples[shield["guard"]["hold"]["clip"]][0]
carried = raised["hand_l"].inverted() @ raised["weapon_l"]
measure["shieldSeat"] = {"at": rounded(carried.translation), "turn": rounded(carried.to_euler("XYZ"))}
measure["guardSeconds"] = {stem: round((len(samples[stem]) - 1) / 60, 4) for stem in dict.fromkeys(
    [*guard_stems(shield["guard"]), *(stem for family in families.values() for stem in guard_stems(family["guard"]))])}
(out / "measure.json").write_text(json.dumps(measure, indent=1))
print("BUILT", out)
