"""Clay views of a rigged scaffold GLB, for a look before anything is baked onto it (Blender, Workbench engine).

    blender -b -P scaffold_views.py -- SCAFFOLD.glb OUT_DIR [--frames 1,8,16,24] [--views front,side,back,quarter] [--size 1088]

Writes OUT_DIR/VIEW-fNNN.png: orthographic, the figure filling the frame, matcap clay with cavity shading. With a GLB that carries
an animation (a rigger's walking_glb.glb), --frames picks frames of it; without one, frame 1 is the rest pose.
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
src, out = a[0], Path(a[1]); opt = dict(zip(a[2::2], a[3::2]))
frames = [int(x) for x in opt.get('--frames', '1').split(',')]; views = opt.get('--views', 'front,side,back,quarter').split(',')
size = int(opt.get('--size', 1088)); out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH']
for o in meshes:
    for p in o.data.polygons:
        p.use_smooth = True
sc.render.engine = 'BLENDER_WORKBENCH'; sh = sc.display.shading
sh.light = 'MATCAP'; sh.studio_light = 'clay_brown.exr'; sh.color_type = 'SINGLE'; sh.single_color = (0.8, 0.8, 0.8); sh.show_cavity = True
sc.render.resolution_x = sc.render.resolution_y = size; sc.render.film_transparent = False
sc.world = bpy.data.worlds.new('w'); sc.world.color = (0.05, 0.05, 0.05)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
AZ = {'front': 0, 'quarter': 35, 'side': 90, 'back-quarter': 145, 'back': 180}
for f in frames:
    sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get(); pts = []
    for o in meshes:
        e = o.evaluated_get(dg); m = e.to_mesh(); pts += [e.matrix_world @ v.co for v in list(m.vertices)[::7]]; e.to_mesh_clear()
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; cam.data.ortho_scale = max(hi.z - lo.z, hi.x - lo.x, hi.y - lo.y) * 1.08
    for v in views:
        t = math.radians(AZ[v]); d = Vector((math.sin(t), -math.cos(t), 0)) * 10   # the figure faces -Y after glTF import
        cam.location = c + d; cam.rotation_euler = (math.radians(90), 0, t)
        sc.render.filepath = str(out / f'{v}-f{f:03d}.png'); bpy.ops.render.render(write_still=True)
print('SCAFFOLD_VIEWS', len(frames) * len(views))
