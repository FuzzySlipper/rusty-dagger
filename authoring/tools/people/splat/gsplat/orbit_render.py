"""Blender: render a rigged GLB in its bind pose from known cameras, for splat training.

  blender -b --factory-startup --python orbit_render.py -- IN.glb OUT_DIR

Writes OUT_DIR/images/NNN.png (RGBA, flat albedo: Workbench FLAT + texture, so
lighting comes later from SplatRelight), OUT_DIR/cameras.npz (OpenCV-convention
world-to-camera matrices and intrinsics in glTF Y-up space, the rig's own
space) and OUT_DIR/init_points.npz (surface samples with texture colours).
"""
import argparse
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Matrix, Vector

ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('src')
ap.add_argument('out')
ap.add_argument('--size', type=int, default=640)
ap.add_argument('--camera-plan', help='JSON list of elevation/azimuth/distance_scale rows; preserves order')
args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
src, out = args.src, args.out
SIZE = args.size
FOV = 35.0
ELEVATIONS = [-15, 5, 25, 45, 70]
AZ_STEP = 12
os.makedirs(out + '/images', exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'):  # importer's bone-shape helper
        bpy.data.objects.remove(o)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
dg = bpy.context.evaluated_depsgraph_get()

# Blender (Z-up) <-> glTF (Y-up): b = (x, -z, y), g = (x, z, -y).
B2G = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], np.float64)
G2B = B2G.T

# Surface samples for initialisation: vertices plus face centres, textured.
pts, cols = [], []
for o in meshes:
    m = o.evaluated_get(dg).to_mesh()
    img = None
    for slot in o.material_slots:
        for n in (slot.material.node_tree.nodes if slot.material and slot.material.use_nodes else []):
            if n.type == 'TEX_IMAGE' and n.image:
                img = n.image; break
    px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4) if img else None
    uv = m.uv_layers.active.data if m.uv_layers.active else None
    mw = np.array(o.matrix_world)
    for poly in m.polygons:
        loops = list(poly.loop_indices)
        corners = [m.vertices[m.loops[li].vertex_index].co for li in loops]
        samples = [(np.mean([c[:] for c in corners], 0), np.mean([uv[li].uv[:] for li in loops], 0) if uv else None)]
        samples += [(np.array(m.vertices[m.loops[li].vertex_index].co[:]), uv[li].uv[:] if uv else None) for li in loops[:1]]
        for p, t in samples:
            pw = (mw @ np.append(p, 1.0))[:3]
            pts.append(B2G @ pw)
            if px is not None and t is not None:
                x = int(np.clip(t[0], 0, 1) * (px.shape[1] - 1)); y = int(np.clip(t[1], 0, 1) * (px.shape[0] - 1))
                cols.append(px[y, x, :3])
            else:
                cols.append((0.4, 0.35, 0.3))
pts = np.array(pts, np.float32); cols = np.clip(np.array(cols, np.float32), 0, 1)
lo, hi = pts.min(0), pts.max(0)
centre = (lo + hi) / 2
radius = float(np.linalg.norm(hi - lo) / 2)
dist = radius / math.sin(math.radians(FOV / 2)) * 1.08
spacing = float(np.cbrt(np.prod(np.maximum(hi - lo, 1e-3)) / len(pts)))
np.savez_compressed(out + '/init_points.npz', points=pts, colors=cols, spacing=spacing)
print('INIT', len(pts), 'bounds', lo.round(3), hi.round(3), 'dist', round(dist, 3), flush=True)

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.show_shadows = False; sc.display.render_aa = '16'
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = SIZE; sc.render.resolution_percentage = 100
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
f_px = (SIZE / 2) / math.tan(math.radians(FOV / 2))
cd = bpy.data.cameras.new('c'); cd.sensor_fit = 'HORIZONTAL'; cd.sensor_width = 36.0; cd.lens = f_px * 36.0 / SIZE
cd.clip_start = 0.01; cd.clip_end = 50
cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam

viewmats, rows = [], []
views = [(el, az) for el in ELEVATIONS for az in range(0, 360, AZ_STEP)] + [(88, 0), (88, 180)]
plan = (json.load(open(args.camera_plan)) if args.camera_plan else
        [dict(elevation=el, azimuth=az, distance_scale=1.0) for el, az in views])
for i, row in enumerate(plan):
    el, az = row['elevation'], row['azimuth']
    distance = dist * row.get('distance_scale', 1.0)
    e, a = math.radians(el), math.radians(az)
    pos = centre + distance * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
    fwd = centre - pos; fwd /= np.linalg.norm(fwd)
    up = np.array([0.0, 1.0, 0.0]) if abs(fwd[1]) < 0.99 else np.array([0.0, 0.0, -1.0])
    right = np.cross(fwd, up); right /= np.linalg.norm(right)
    down = np.cross(fwd, right)
    R = np.stack([right, down, fwd], 1)            # camera-to-world, OpenCV axes
    M = np.eye(4); M[:3, :3] = R.T; M[:3, 3] = -R.T @ pos
    viewmats.append(M)
    # Blender camera looks down -Z with +Y up.
    rb = np.stack([G2B @ right, G2B @ (-down), G2B @ (-fwd)], 1)
    mw = Matrix.Identity(4)
    for r in range(3):
        for c in range(3):
            mw[r][c] = rb[r, c]
        mw[r][3] = (G2B @ pos)[r]
    cam.matrix_world = mw
    sc.render.filepath = f'{out}/images/{i:03d}.png'
    bpy.ops.render.render(write_still=True)
    rows.append(dict(row, index=i))
Ks = np.tile(np.array([[f_px, 0, SIZE / 2], [0, f_px, SIZE / 2], [0, 0, 1]], np.float32), (len(plan), 1, 1))
np.savez_compressed(out + '/cameras.npz', viewmats=np.array(viewmats, np.float32), Ks=Ks)
json.dump({'size': SIZE, 'fov_deg': FOV, 'centre': centre.tolist(), 'distance': dist, 'views': rows},
          open(out + '/cameras.json', 'w'), indent=1)
print('RENDERED', len(plan), flush=True)
