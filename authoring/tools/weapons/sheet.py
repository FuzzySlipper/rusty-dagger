#!/usr/bin/env python3
"""Turnaround sheet of weapon models: each GLB standing upright, tip up, from eight directions, one row each.

  python3 authoring/tools/weapons/sheet.py OUT.png LABEL=MODEL.glb [LABEL=MODEL.glb ...] [--views H3_VIEWS_DIR]
      [--tile 256] [--grey] [--arrow ARROW.glb]

The models are in the held-look frame (length along glTF -Z, +Y one edge, broad face toward ±X); the sheet turns
them upright and starts each row square on the broad face, turning the camera 45° at a time. With --views, the
first row is the H3 turnaround's eight chosen frames (front.png, front-right.png ... front-left.png). Rendered by
Blender's Workbench in flat texture colour, orthographic, so silhouettes and painted colour compare directly.
With --arrow, each model is a bow: its row shows rest, half and full draw from the side and from behind the string,
with the arrow on the `nock` joint.
"""
import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

DIRS = ['front', 'front-right', 'right', 'back-right', 'back', 'back-left', 'left', 'front-left']

RENDER = r'''
import bpy, json, math, sys
from mathutils import Vector
glb, out, tile, grey = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
pivot = bpy.data.objects.new('pivot', None)
bpy.context.scene.collection.objects.link(pivot)
for o in list(bpy.context.scene.objects):   # every root: a rigged model's meshes hang from its armature
    if o.parent is None and o is not pivot:
        o.parent = pivot
# glTF -Z (the length) imports as Blender +Y; stand it on +Z, the broad face (Blender X) toward the camera on -Y.
pivot.rotation_euler = (math.radians(90), 0, math.radians(90))
bpy.context.view_layer.update()
points = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector([min(p[i] for p in points) for i in range(3)])
hi = Vector([max(p[i] for p in points) for i in range(3)])
centre, size = (lo + hi) / 2, max(hi - lo) * 1.08
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'FLAT'
scene.display.shading.color_type = 'SINGLE' if grey == '1' else 'TEXTURE'
scene.display.shading.single_color = (0.7, 0.7, 0.7)
scene.display.shading.show_cavity = grey == '1'
scene.render.resolution_x = scene.render.resolution_y = int(tile)
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.22, 0.23, 0.25)
camera = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
camera.data.type = 'ORTHO'
camera.data.ortho_scale = size
scene.collection.objects.link(camera)
scene.camera = camera
for i, az in enumerate(range(0, 360, 45)):
    a = math.radians(az)
    camera.location = centre + Vector((math.sin(a), -math.cos(a), 0)) * size * 3
    camera.rotation_euler = (Vector(centre) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = f'{out}/{i}.png'
    bpy.ops.render.render(write_still=True)
'''


DRAW = r'''
import bpy, math, sys
from mathutils import Matrix, Vector
glb, arrow, out, tile = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
before = set(bpy.context.scene.objects)
bpy.ops.import_scene.gltf(filepath=arrow)
shaft = [o for o in bpy.context.scene.objects if o not in before]
roots = [o for o in shaft if o.parent is None]
action = bpy.data.actions['draw']
rig.animation_data.action = action
rig.animation_data.action_slot = action.slots[0]
for track in rig.animation_data.nla_tracks:
    track.mute = True
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'FLAT'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = scene.render.resolution_y = int(tile)
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.22, 0.23, 0.25)
camera = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
camera.data.type = 'ORTHO'
scene.collection.objects.link(camera)
scene.camera = camera
size = max(o.dimensions.y for o in meshes) * 1.2
camera.data.ortho_scale = size
for i, frame in enumerate((1, 13, 25)):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    bone = rig.pose.bones['nock']
    # The rest the arrow lies on, from the joint's direction when it was built: it points from the string to the rest.
    head = rig.matrix_world @ bone.head
    aim = (Vector((math.copysign(0.016, -head.z), head.y, 0)) - head).normalized()
    for root in roots:
        root.rotation_mode = 'QUATERNION'
        root.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(aim)
        root.location = head
    bpy.context.view_layer.update()
    for j, (eye, up) in enumerate((((-1, 0, 0), 'Y'), ((0, 0, 1), 'Y'))):
        camera.location = Vector((0, 0, 0.1)) + Vector(eye) * size * 3
        camera.rotation_euler = (-Vector(eye)).to_track_quat('-Z', up).to_euler()
        scene.render.filepath = f'{out}/{j * 3 + i}.png'
        bpy.ops.render.render(write_still=True)
'''


def row(images, label, tile):
    strip = Image.new('RGB', (tile * len(images), tile + 22), (30, 32, 36))
    ImageDraw.Draw(strip).text((6, 4), label, fill=(255, 220, 120))
    for i, image in enumerate(images):
        image = image.convert('RGB')
        image.thumbnail((tile, tile))
        strip.paste(image, (i * tile + (tile - image.width) // 2, 22 + (tile - image.height) // 2))
    return strip


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('out', type=Path)
    parser.add_argument('models', nargs='+')
    parser.add_argument('--views', type=Path)
    parser.add_argument('--tile', type=int, default=256)
    parser.add_argument('--grey', action='store_true')
    parser.add_argument('--arrow', type=Path)
    args = parser.parse_args()
    rows = []
    if args.views:
        rows.append(row([Image.open(args.views / f'{d}.png') for d in DIRS], f'H3 {args.views}', args.tile))
    with tempfile.TemporaryDirectory() as work:
        script = Path(work) / 'render.py'
        script.write_text(DRAW if args.arrow else RENDER)
        for k, model in enumerate(args.models):
            label, glb = model.split('=', 1)
            out = Path(work) / str(k)
            out.mkdir()
            if args.arrow:
                subprocess.run(['blender', '-b', '--factory-startup', '--python', str(script), '--', str(Path(glb).resolve()),
                                str(args.arrow.resolve()), str(out), str(args.tile)], check=True, capture_output=True)
                rows.append(row([Image.open(out / f'{i}.png') for i in range(6)], f'{label}: rest, half, full; side, then behind', args.tile))
                continue
            for grey in (['0', '1'] if args.grey else ['0']):
                subprocess.run(['blender', '-b', '--factory-startup', '--python', str(script), '--',
                                str(Path(glb).resolve()), str(out), str(args.tile), grey],
                               check=True, capture_output=True)
                rows.append(row([Image.open(out / f'{i}.png') for i in range(8)],
                                label + (' (grey)' if grey == '1' else ''), args.tile))
    sheet = Image.new('RGB', (max(r.width for r in rows), sum(r.height for r in rows)), (30, 32, 36))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(args.out)
    print(args.out)


if __name__ == '__main__':
    main()
