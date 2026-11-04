"""Builds the first-person arms with their clips from the old game's MedievalFPArms and FPP melee clips
(run under Blender 5 by scripts/fp-arms/build.sh; scripts/fp-arms/README.md).

usage: blender -b --factory-startup --python scripts/fp-arms/build.py -- SOURCES DEFINITION OUT_DIR

SOURCES is local/fp-melee (scripts/fp-arms/copy-sources.sh). DEFINITION is the held definition, which names every clip
the game plays: the rig's own clip and each family's idle, power hold and attack clips, by their FBX file stems. Writes into OUT_DIR:

- first-person/arms.glb: the male leather armour arms (mesh, skeleton, colour and normal maps) with every clip the
  definition names, and first-person/arms-retro.glb, the same with its colour map alone, small and drawn nearest, which
  the heldRetro style draws. The clips are the model's own, not clip packs: the Engine shares an opened model's clip packs
  between everything that opens it, so a second set of arms on the same model (a weapon swapped, another site's
  viewmodel) could not associate its packs again.
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

import bpy
from mathutils import Matrix, Quaternion, Vector

sources, definition_path, out = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])
definition = json.loads(definition_path.read_text())
pack_root = sources / "MedievalFPArms"
ARMS = pack_root / "Meshes/FPArms_Male-Leather-Armor_LPR_LOD0.fbx"
COLOUR = pack_root / "Textures/FPArms_Male-Leather-Armor_Medium_COL.png"
NORMAL = pack_root / "Textures/FPArms_Male-Leather-Armor_NRM.png"
TEXTURE = 1024
# The retro arms (heldRetro) draw the colour map alone, this small and unfiltered, as Daggerfall draws its world.
RETRO_TEXTURE = 256
# The clips' camera bone looks along its X with its Y up and its Z to the right; the baked view looks along +Y with +Z
# up and +X to the right, which the Y-up export turns into the Engine's camera space (-Z ahead, +Y up, +X right).
VIEW = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
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


def load(path, size, colour=True):
    """An image scaled down to at most `size` a side; its colour space is set first, as setting it reloads the file."""
    image = bpy.data.images.load(str(path))
    if not colour:
        image.colorspace_settings.name = "Non-Color"
    if max(image.size) > size:
        image.scale(size, size)
    return image


def texture(material):
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    colour = nodes.new("ShaderNodeTexImage")
    colour.label = "colour"
    colour.image = load(COLOUR, TEXTURE)
    links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Metallic"].default_value = 0.0
    normal = nodes.new("ShaderNodeTexImage")
    normal.label = "normal"
    normal.image = load(NORMAL, TEXTURE, colour=False)
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


def arms():
    """The arms with their textures; the pack's own unarmed clip is dropped."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 60
    imported = import_fbx(ARMS)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    rig = next(o for o in imported if o.type == "ARMATURE")
    for obj in imported:
        if obj.type == "MESH":
            for slot in obj.material_slots:
                texture(slot.material)
    return rig


def world(rig, bone):
    return rig.matrix_world @ rig.pose.bones[bone].matrix


def unscaled(matrix):
    return Matrix.LocRotScale(matrix.translation, matrix.to_quaternion(), None)


def bake(rig, stem):
    """Bakes clip `stem` onto the arms as an action of that name, as its camera bone sees it; returns its samples."""
    imported = import_fbx(clip_file(stem))
    source = next(o for o in imported if o.type == "ARMATURE")
    clip = source.animation_data.action
    first, last = (int(f) for f in clip.frame_range)
    bones = [b for b in rig.data.bones if b.name in source.pose.bones]
    # The two skeletons share their rest pose's joints but not their bones' axes, so each bone takes the clip's turn
    # from its own rest: its rest frame as the clip's bone at rest sees it.
    rest = {b.name: (source.matrix_world @ source.data.bones[b.name].matrix_local).inverted() @ rig.matrix_world @ b.matrix_local for b in bones}
    # The IK helper bones (ik_*) skin nothing and are placed apart in each file.
    worst = max(((rig.matrix_world @ b.matrix_local).translation
                 - (source.matrix_world @ source.data.bones[b.name].matrix_local).translation).length for b in bones if not b.name.startswith("ik_"))
    if worst > 0.01:
        raise SystemExit(f"clip '{stem}': its rest pose's joints lie up to {worst * 100:.1f} cm from the arms'")
    unrig = rig.matrix_world.inverted()
    frames, samples = [], []
    for frame in range(first, last + 1):
        bpy.context.scene.frame_set(frame)
        view = VIEW @ unscaled(world(source, "camera")).inverted()
        posed = {b.name: unrig @ view @ world(source, b.name) @ rest[b.name] for b in bones}
        basis = {}
        for b in bones:
            if b.parent is None or b.parent.name not in posed:
                basis[b.name] = b.matrix_local.inverted() @ posed[b.name]
            else:
                local = b.parent.matrix_local.inverted() @ b.matrix_local
                basis[b.name] = local.inverted() @ posed[b.parent.name].inverted() @ posed[b.name]
        frames.append(basis)
        samples.append({name: rig.matrix_world @ m for name, m in posed.items()} | {"weapon_r": view @ world(source, "weapon_r")})
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


def rounded(v, places=4):
    return [round(c, places) for c in v]


rig_clip = definition["rig"]["clip"]
families = definition["families"]
stems = [rig_clip, *(stem for family in families.values() for stem in (family["idle"]["clip"], family["powerHold"]["clip"], *(clip["clip"] for clip in family["strikes"].values())))]
rig = arms()
samples = {stem: bake(rig, stem) for stem in dict.fromkeys(stems)}
export(out / "first-person/arms.glb", list(samples))
retro()
export(out / "first-person/arms-retro.glb", list(samples))

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
        # A cast is the left hand's: its hit is the palm thrown forward, not a blade.
        cast = action == "cast"
        tips = [camera(s["hand_l"].translation) if cast else camera(s["weapon_r"].translation + s["weapon_r"].to_3x3().normalized().col[2] * 0.3) for s in samples[clip["clip"]]]
        speeds = [round((tips[i + 1] - tips[i]).length * 60, 2) for i in range(len(tips) - 1)]
        strikes[action] = {"clip": clip["clip"], "frames": len(tips), "seconds": round((len(tips) - 1) / 60, 4),
                           "marks": marks(tips, speeds, CAST_COMMIT_SHARE if cast else COMMIT_SHARE), "tipSpeed": speeds}
    measure["families"][name] = {
        "idleSeconds": round((len(samples[family["idle"]["clip"]]) - 1) / 60, 4),
        "powerHoldSeconds": round((len(samples[family["powerHold"]["clip"]]) - 1) / 60, 4),
        "grip": rounded(look.transposed() @ (palm - at)),
        "hold": degrees(look.transposed()),
        "strikes": strikes,
    }
measure["palm"] = rounded(palm)
(out / "measure.json").write_text(json.dumps(measure, indent=1))
print("BUILT", out)
