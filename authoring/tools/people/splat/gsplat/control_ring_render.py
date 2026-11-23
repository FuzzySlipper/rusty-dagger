"""Blender: one camera ring around a rigged GLB as a VACE depth control video.

  blender -b --factory-startup --python control_ring_render.py -- IN.glb OUT_DIR ELEVATION [FRAMES] [W] [H]

Writes OUT_DIR/depth/NNN.png (near = white, background black, one fixed
near/far for the whole ring), OUT_DIR/albedo/NNN.png (flat texture, the same
cameras, for comparison) and OUT_DIR/cameras.npz in the convention
train_rat_splat.py reads (OpenCV world-to-camera in glTF Y-up space).
Frame i is camera i at azimuth 360*i/FRAMES degrees; azimuth 0 looks at +Z.
"""
import bpy, json, math, os, sys
import numpy as np
from mathutils import Matrix

a = sys.argv[sys.argv.index('--') + 1:]
src, out, elev = a[0], a[1], float(a[2])
frames = int(a[3]) if len(a) > 3 else 81
W = int(a[4]) if len(a) > 4 else 832
H = int(a[5]) if len(a) > 5 else 480
VFOV = 30.0
for d in ('depth', 'albedo'):
    os.makedirs(f'{out}/{d}', exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'):
        bpy.data.objects.remove(o)
B2G = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], np.float64)
G2B = B2G.T
dg = bpy.context.evaluated_depsgraph_get()
pts = np.array([B2G @ np.array((o.matrix_world @ v.co)[:]) for o in bpy.context.scene.objects if o.type == 'MESH'
                for v in o.evaluated_get(dg).to_mesh().vertices])
lo, hi = pts.min(0), pts.max(0)
centre = (lo + hi) / 2
radius = float(np.linalg.norm(hi - lo) / 2)
f_px = (H / 2) / math.tan(math.radians(VFOV / 2))
hfov = 2 * math.atan((W / 2) / f_px)
dist = radius / math.sin(min(hfov, math.radians(VFOV)) / 2) * 0.95
near, far = dist - radius * 1.05, dist + radius * 1.05

sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
sc.render.image_settings.file_format = 'PNG'
sc.view_settings.view_transform = 'Standard'
cd = bpy.data.cameras.new('c'); cd.sensor_fit = 'VERTICAL'; cd.sensor_height = 24.0; cd.lens = f_px * 24.0 / H
cd.clip_start, cd.clip_end = 0.01, 50
cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam

# Depth as an emission shader (camera View Z mapped near=1 .. far=0), which
# avoids the compositor API that changed in Blender 5.
depth_mat = bpy.data.materials.new('depth'); depth_mat.use_nodes = True
nt = depth_mat.node_tree; nt.nodes.clear()
camdata = nt.nodes.new('ShaderNodeCameraData')
rng = nt.nodes.new('ShaderNodeMapRange')
rng.inputs['From Min'].default_value = near; rng.inputs['From Max'].default_value = far
rng.inputs['To Min'].default_value = 1.0; rng.inputs['To Max'].default_value = 0.0
emit = nt.nodes.new('ShaderNodeEmission'); outn = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(camdata.outputs['View Z Depth'], rng.inputs['Value'])
nt.links.new(rng.outputs['Result'], emit.inputs['Color'])
nt.links.new(emit.outputs['Emission'], outn.inputs['Surface'])
meshes = [o for o in sc.objects if o.type == 'MESH']
original = {o.name: [slot.material for slot in o.material_slots] for o in meshes}
world = bpy.data.worlds.new('black'); world.color = (0, 0, 0); sc.world = world

viewmats = []
poses = []
for i in range(frames):
    e, az = math.radians(elev), math.radians(360.0 * i / frames)
    pos = centre + dist * np.array([math.cos(e) * math.sin(az), math.sin(e), math.cos(e) * math.cos(az)])
    fwd = centre - pos; fwd /= np.linalg.norm(fwd)
    right = np.cross(fwd, [0.0, 1.0, 0.0]); right /= np.linalg.norm(right)
    down = np.cross(fwd, right)
    R = np.stack([right, down, fwd], 1)
    M = np.eye(4); M[:3, :3] = R.T; M[:3, 3] = -R.T @ pos
    viewmats.append(M)
    rb = np.stack([G2B @ right, G2B @ (-down), G2B @ (-fwd)], 1)
    mw = Matrix.Identity(4)
    for r in range(3):
        for c in range(3):
            mw[r][c] = rb[r, c]
        mw[r][3] = (G2B @ pos)[r]
    poses.append(mw)

sc.render.engine = 'BLENDER_EEVEE'
sc.render.film_transparent = False
sc.render.image_settings.color_mode = 'BW'
for o in meshes:
    for slot in o.material_slots:
        slot.material = depth_mat
for i, mw in enumerate(poses):
    cam.matrix_world = mw
    sc.render.filepath = f'{out}/depth/{i:03d}.png'
    bpy.ops.render.render(write_still=True)

# Flat albedo through the same cameras, for comparison.
for o in meshes:
    for slot, mat in zip(o.material_slots, original[o.name]):
        slot.material = mat
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'TEXTURE'; sc.display.render_aa = '16'
sc.render.film_transparent = True
sc.render.image_settings.color_mode = 'RGBA'
for i, mw in enumerate(poses):
    cam.matrix_world = mw
    sc.render.filepath = f'{out}/albedo/{i:03d}.png'
    bpy.ops.render.render(write_still=True)

Ks = np.tile(np.array([[f_px, 0, W / 2], [0, f_px, H / 2], [0, 0, 1]], np.float32), (frames, 1, 1))
np.savez_compressed(f'{out}/cameras.npz', viewmats=np.array(viewmats, np.float32), Ks=Ks)
json.dump({'elevation': elev, 'frames': frames, 'size': [W, H], 'vfov_deg': VFOV, 'distance': dist,
           'near': near, 'far': far, 'centre': centre.tolist()}, open(f'{out}/ring.json', 'w'), indent=1)
print('RING', elev, frames, W, H, 'dist', round(dist, 3), flush=True)
