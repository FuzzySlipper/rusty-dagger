"""blender_path.py: a Blender guide for an H3 orbit - a still, abstract figure and an explicit camera path.

Copied 2026-11-15 from live-evidence/h3-orbit-control-20261114-r01/tools/blender_path.py (the orbit-control run's tool, unchanged
there) with one addition: FIT may be a rigged scaffold GLB (authoring/tools/people/splat/scaffold, a character's own clay mesh) instead of a joint
fit. The mesh then IS the still figure: imported in its rest pose, stood on z = 0 at the origin, scaled to --mesh-height metres
(default 1.72, the capsule figure's height, so the depth pass's fixed range still fits). Everything else is as before.

  blender -b --factory-startup -t 2 --python blender_path.py -- FIT.json OUT_DIR --path eyelevel|riseddip|twocycle|gc [--gc-az 0|45|90|135] [--up 45 --down 30] [--light flat|world] [--fov DEG (0 = orthographic)] [--size 1088] [--frames 73] [--pass clay|depth|shaded|texture] [--sun right|left] [--rig key|even|keyfill|camkey [--dome --top | --key --fill --back --dome]] [--cams CAMS.json --only 0,18,...] [--target-frac 0.5] [--zoom 1]

The figure: capsule limbs, a sphere head and an elongated torso built from FIT.json's joints (tools/guide.py fit: joints fitted to the
character's front keyframe, metres, x = his left, y up, z = his front), standing still at the world origin with his feet on y = 0.
It never moves; only the camera does. Camera: orthographic (scale fixed so the figure fills the frame as the keyframes do), looking
at the figure's mid height, on a path of FRAMES frames:
  azimuth  = -5 deg x frame (frame 18 shows his right side, 36 his back, as our FL2VA orbits),
  eyelevel = elevation 0 throughout;
  riseddip = elevation +45 x sin(pi f / 36) for f <= 36 (up over him, back to eye level at his back), then -30 x sin(pi (f-36)/36)
             (down below him, back to eye level at the front), so frames 0, 36 and 72 are at eye level and match the keyframes;
             --up/--down change the 45/30. Paths are defined over 73 frames; with --frames N the same path runs slower (frame f -> f x 72/(N-1));
  twocycle = +UP x sin(pi f/18) when positive, DOWN x sin(...) when negative: up, down, up, down, zero at front, sides, back.
Pass per frame: clay/NNN.png (Workbench, flat lighting, single light-grey colour on flat grey 128, no outline) - the pass the REF2VA
route reads as its reference video. cameras.json gives each
frame's azimuth, elevation, camera location and orthographic scale (Blender units = metres).
"""
import bpy, json, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
fit, out = argv[0], argv[1]
opt = dict(zip(argv[2::2], argv[3::2]))
PATH = opt.get('--path', 'eyelevel'); SIZE = int(opt.get('--size', 1088)); N = int(opt.get('--frames', 73))
MESH = fit.lower().endswith('.glb')
J = {} if MESH else {k: Vector((v[0], -v[2], v[1])) for k, v in json.load(open(fit))['joints'].items()}   # to Blender: x left, -z->y (front = -y), y->z up

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
mat = bpy.data.materials.new('clay'); mat.diffuse_color = (0.67, 0.67, 0.67, 1)


def capsule(a, b, r0, r1, name):
    d = b - a; L = d.length
    bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=r0, radius2=r1, depth=L, location=(a + b) / 2)
    o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = d.to_track_quat('Z', 'Y'); o.name = name
    for p, r in ((a, r0), (b, r1)):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=r, location=p)
    return o


def sphere(p, r, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=r, location=p); bpy.context.object.scale = scale


if MESH:   # the character's own scaffold as the still figure (glTF import: Y up becomes Z up, his front is -y, his left +x)
    bpy.ops.import_scene.gltf(filepath=fit)
    ms = [o for o in sc.objects if o.type == 'MESH' and len(o.vertex_groups)]   # the skinned body; the importer also makes a bone-shape mesh
    for o in ms:
        w = o.matrix_world.copy(); o.modifiers.clear(); o.parent = None; o.matrix_world = w; o.data.materials.clear()
    for o in [o for o in sc.objects if o not in ms]:
        bpy.data.objects.remove(o)
    co = [o.matrix_world @ v.co for o in ms for v in o.data.vertices]
    lo = Vector((min(p.x for p in co), min(p.y for p in co), min(p.z for p in co))); hi = Vector((max(p.x for p in co), max(p.y for p in co), max(p.z for p in co)))
    k = float(dict(zip(argv[2::2], argv[3::2])).get('--mesh-height', 1.72)) / (hi.z - lo.z)
    from mathutils import Matrix
    M = Matrix.Scale(k, 4) @ Matrix.Translation(Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
    for o in ms:
        o.matrix_world = M @ o.matrix_world
    bpy.context.view_layer.update()
else:
    pel = (J['rhip'] + J['lhip']) / 2; chest = (J['rsho'] + J['lsho']) / 2 - Vector((0, 0, 0.12))
    capsule(pel + Vector((0, 0, -0.02)), chest, 0.13, 0.155, 'torso')
    capsule(J['rsho'] + Vector((0.03, 0, 0)), J['lsho'] - Vector((0.03, 0, 0)), 0.055, 0.055, 'shoulders')
    head = Vector((0, 0, 1.62)) + Vector((0, 0, J['neck'].z - 1.46))
    capsule(J['neck'], head - Vector((0, 0, 0.04)), 0.05, 0.045, 'neck')
    sphere(head, 0.098, (0.95, 1.05, 1.15))
    for s in 'rl':
        capsule(J[s + 'sho'], J[s + 'elb'], 0.05, 0.04, s + 'upperarm')
        capsule(J[s + 'elb'], J[s + 'wri'], 0.04, 0.032, s + 'forearm')
        capsule(J[s + 'wri'], J[s + 'wri'] + Vector((0.015 * (1 if s == 'l' else -1), -0.015, -0.10)), 0.032, 0.02, s + 'hand')
        capsule(J[s + 'hip'], J[s + 'kne'], 0.085, 0.06, s + 'thigh')
        capsule(J[s + 'kne'], J[s + 'ank'], 0.06, 0.04, s + 'shin')
        yaw = 0.18 * (1 if s == 'l' else -1)
        capsule(J[s + 'ank'] + Vector((0, 0, -0.03)), J[s + 'ank'] + Vector((math.sin(yaw) * 0.19, -0.19 * math.cos(yaw), -0.075)), 0.045, 0.03, s + 'foot')
for o in bpy.context.scene.objects:
    if o.type == 'MESH':
        o.data.materials.append(mat); bpy.context.view_layer.objects.active = o; bpy.ops.object.shade_smooth()

zs = [v.z for o in sc.objects if o.type == 'MESH' for v in (o.matrix_world @ p.co for p in o.data.vertices)]
z0, z1 = min(zs), max(zs); H = z1 - z0; TF = float(opt.get('--target-frac', 0.5)); ZOOM = float(opt.get('--zoom', 1.0))
target = Vector((0, 0, z0 + H * TF))   # --target-frac: the aim point as a fraction of the figure's height (0.78 = head and shoulders)
cam_data = bpy.data.cameras.new('cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = H / (1898 / 2176) / ZOOM   # figure fills 1898/2176 as the keys; --zoom N frames 1/N of that
cam_data.clip_start = 0.01; cam_data.clip_end = 20
cam = bpy.data.objects.new('cam', cam_data); sc.collection.objects.link(cam); sc.camera = cam
DIST = 5.0
FOV = float(opt.get('--fov', 0))   # 0 = orthographic; else a perspective camera with this vertical/horizontal FOV (square frame)
if FOV:
    cam_data.type = 'PERSP'; cam_data.sensor_fit = 'HORIZONTAL'; cam_data.angle = math.radians(FOV)
    DIST = (cam_data.ortho_scale / 2) / math.tan(math.radians(FOV) / 2)   # the figure fills the frame at the target's distance
sc.render.resolution_x = sc.render.resolution_y = SIZE; sc.render.film_transparent = False
sc.render.engine = 'BLENDER_WORKBENCH'
LIGHT = opt.get('--light', 'flat')
sh = sc.display.shading; sh.light = 'FLAT' if LIGHT == 'flat' else 'STUDIO'
if LIGHT == 'world':   # studio light fixed in world space: the clay is lit from one world direction whatever the camera does
    sh.use_world_space_lighting = True; sh.studiolight_rotate_z = math.radians(45)
sh.color_type = 'MATERIAL'; sh.show_object_outline = False; sh.background_type = 'VIEWPORT'
sh.background_color = (0.2158, 0.2158, 0.2158)   # linear 0.2158 = sRGB 128
sc.view_settings.view_transform = 'Standard'
PASS = opt.get('--pass', 'clay')   # clay | depth | shaded (one world-fixed soft sun in front of him, to his right, above head height; weak
# ambient; transparent film - flatten over grey 128 afterwards) | texture (solver ground truth: a coloured solid noise texture, emission, on grey 128)
if PASS == 'shaded':
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 32; sc.cycles.use_denoising = False; sc.render.film_transparent = True
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.06, 0.06, 0.06, 1)
    RIG = opt.get('--rig', 'key')   # key: one sun (--sun side); even: a bright dome (world) + a soft overhead sun, no side preference
    if RIG == 'even':
        w.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1); w.node_tree.nodes['Background'].inputs[1].default_value = float(opt.get('--dome', 0.55))
    sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.0 if RIG == 'key' else float(opt.get('--top', 1.2)); sun.angle = math.radians(15 if RIG == 'key' else 40)
    so = bpy.data.objects.new('sun', sun); sc.collection.objects.link(so)
    so.rotation_mode = 'QUATERNION'; SIDE = 1.0 if opt.get('--sun', 'right') == 'right' else -1.0   # --sun right|left: the side of HIM the light stands on (Blender +x = his left)
    so.rotation_quaternion = (Vector((SIDE, 1.0, -1.2)) if RIG == 'key' else Vector((0.0, 0.0, -1.0))).to_track_quat('-Z', 'Y')
    if RIG == 'keyfill':   # Patch 2026-11-14: soft key at his front left above head height + fill at his front right + a fill from behind
        w.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1); w.node_tree.nodes['Background'].inputs[1].default_value = float(opt.get('--dome', 0.1))
        sun.energy = float(opt.get('--key', 2.4)); sun.angle = math.radians(30)
        so.rotation_quaternion = Vector((-1.0, 1.0, -1.2)).to_track_quat('-Z', 'Y')   # from +x (his left), -y (front), above
        for nm, e, d in (('fill', float(opt.get('--fill', 1.0)), (1.0, 1.0, -0.5)), ('backfill', float(opt.get('--back', 0.8)), (0.0, -1.0, -0.6))):
            L = bpy.data.lights.new(nm, 'SUN'); L.energy = e; L.angle = math.radians(45)
            o = bpy.data.objects.new(nm, L); sc.collection.objects.link(o); o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector(d).to_track_quat('-Z', 'Y')   # light travels from (-SIDE x, -y his front, +z above) toward him
    for ob in sc.objects:
        if ob.type == 'MESH':
            ob.data.materials.clear(); ob.data.materials.append(mat)
if PASS == 'texture':
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 16; sc.cycles.use_denoising = False
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.2158, 0.2158, 0.2158, 1)
    tm = bpy.data.materials.new('tex'); tm.use_nodes = True; nt = tm.node_tree; nt.nodes.clear()
    tc = nt.nodes.new('ShaderNodeTexCoord'); nz = nt.nodes.new('ShaderNodeTexNoise'); vo = nt.nodes.new('ShaderNodeTexVoronoi')
    mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; em = nt.nodes.new('ShaderNodeEmission'); o = nt.nodes.new('ShaderNodeOutputMaterial')
    nz.inputs['Scale'].default_value = 9.0; nz.inputs['Detail'].default_value = 6.0; vo.inputs['Scale'].default_value = 25.0
    nt.links.new(tc.outputs['Object'], nz.inputs['Vector']); nt.links.new(tc.outputs['Object'], vo.inputs['Vector'])
    nt.links.new(nz.outputs['Color'], mx.inputs[6]); nt.links.new(vo.outputs['Color'], mx.inputs[7]); mx.inputs['Factor'].default_value = 0.35
    nt.links.new(mx.outputs[2], em.inputs['Color']); nt.links.new(em.outputs[0], o.inputs[0])
    for ob in sc.objects:
        if ob.type == 'MESH':
            ob.data.materials.clear(); ob.data.materials.append(tm)
            ob.matrix_parent_inverse.identity()
if PASS == 'depth':   # guide.py's depth convention: near bright, ONE fixed mapping for every frame, black background. Value =
    # 40 + 215 x (d + R) / 2R over d = DIST - view depth (metres in front of the target's vertical axis, +near), R = 0.45 m; Cycles
    # emission of that value, Raw view so pixels carry it exactly.
    R = 0.45; sc.render.engine = 'CYCLES'; sc.cycles.samples = 16; sc.cycles.use_denoising = False; sc.view_settings.view_transform = 'Raw'
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0, 0, 0, 1)
    dm = bpy.data.materials.new('depth'); dm.use_nodes = True; nt = dm.node_tree; nt.nodes.clear()
    cd = nt.nodes.new('ShaderNodeCameraData'); mr = nt.nodes.new('ShaderNodeMapRange'); em = nt.nodes.new('ShaderNodeEmission'); o = nt.nodes.new('ShaderNodeOutputMaterial')
    mr.clamp = True; mr.inputs['From Min'].default_value = DIST + R; mr.inputs['From Max'].default_value = DIST - R
    mr.inputs['To Min'].default_value = 40 / 255; mr.inputs['To Max'].default_value = 1.0
    nt.links.new(cd.outputs['View Z Depth'], mr.inputs['Value']); nt.links.new(mr.outputs['Result'], em.inputs['Color']); nt.links.new(em.outputs[0], o.inputs[0])
    for ob in sc.objects:
        if ob.type == 'MESH':
            ob.data.materials.clear(); ob.data.materials.append(dm)


GCAZ = float(opt.get('--gc-az', 0))   # path gc: the azimuth of the great circle's vertical plane
UPA = float(opt.get('--up', 45)); DNA = float(opt.get('--down', 30))   # riseddip/twocycle amplitudes, degrees above / below
CAMS = json.load(open(opt['--cams'])) if '--cams' in opt else None   # [{frame, azimuth_deg, elevation_deg}]: render at given (e.g. solved) cameras
ONLY = [int(x) for x in opt['--only'].split(',')] if '--only' in opt else None


def elevation(f):
    if CAMS:
        return CAMS[f]['elevation_deg']
    f = f * 72 / (N - 1)   # the path is defined over 73 frames; longer clips run the same path slower
    if PATH == 'eyelevel':
        return 0.0
    if PATH == 'twocycle':   # up, down, up, down: zero at the front, both sides and the back
        v = math.sin(math.pi * f / 18); return UPA * v if v > 0 else DNA * v
    return UPA * math.sin(math.pi * f / 36) if f <= 36 else -DNA * math.sin(math.pi * (f - 36) / 36)


for d in (PASS,):
    os.makedirs(f'{out}/{d}', exist_ok=True)
cams = []
for f in range(N):
    if ONLY is not None and f not in ONLY:
        continue
    az = math.radians(CAMS[f]['azimuth_deg'] if CAMS else -5 * f * 72 / (N - 1)); el = math.radians(elevation(f))
    UPV = None
    LEAD = float(opt.get('--lead', 0))   # path gc with --lead DEG: first DEG along the level orbit from the front (Patch's rule via the lead,
    # 2026-11-14: H3 opens on a painted view, so a circle that does not pass the front starts there), then the full circle; GCAZ must equal LEAD
    if PATH == 'gc' and LEAD and (LEAD + 360.0) * f / (N - 1) < LEAD:
        az = math.radians(-(LEAD + 360.0) * f / (N - 1)); el = 0.0; UPV = None
        d = Vector((math.sin(az), -math.cos(az), 0.0))
    elif PATH == 'gc':   # great circle through both poles, in the vertical plane at azimuth GCAZ (0 = front-back, 90 = side-side)
        th = (math.radians((LEAD + 360.0) * f / (N - 1) - LEAD) if LEAD else 2 * math.pi * f / (N - 1)); A = math.radians(-GCAZ)   # same azimuth sign as the orbits (-90 = his right side)
        hvec = Vector((math.sin(A), -math.cos(A), 0.0)); zvec = Vector((0.0, 0.0, 1.0))
        dvec = math.cos(th) * hvec + math.sin(th) * zvec              # camera direction from the target
        tvec = -math.sin(th) * hvec + math.cos(th) * zvec             # ring tangent (the "ring up"): world up at frame 0
        # roll relative to the ring up: 0 below 60 deg from level going up, turning smoothly by 180 deg over each pole (60..120 and
        # 240..300 deg along the ring) so that away from the poles the figure is upright, and the image never jumps.
        def ss(x): x = min(max(x, 0.0), 1.0); return x * x * (3 - 2 * x)
        deg = math.degrees(th) % 360
        phi = math.pi * (ss((deg - 60) / 60) + ss((deg - 240) / 60))
        fwd = -dvec
        UPV = tvec * math.cos(phi) + fwd.cross(tvec) * math.sin(phi)
        az = math.atan2(dvec.x, -dvec.y); el = math.asin(max(-1, min(1, dvec.z)))
        d = dvec
    # azimuth 0 = in front of him (-y in Blender), turning so that frame 18 sees his right side (-x)
    if UPV is None:
        d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    cam.location = target + d * DIST
    if UPV is None:
        cam.rotation_mode = 'QUATERNION'; cam.rotation_quaternion = (target - cam.location).to_track_quat('-Z', 'Y')
    else:   # explicit up: camera -Z looks at the target, +Y along UPV
        from mathutils import Matrix
        zc = -(target - cam.location).normalized(); yc = (UPV - UPV.dot(zc) * zc).normalized(); xc = yc.cross(zc)
        cam.rotation_mode = 'QUATERNION'; cam.rotation_quaternion = Matrix((xc, yc, zc)).transposed().to_quaternion()
    if PASS == 'shaded' and opt.get('--rig') == 'camkey':   # known case for a light that turns with the camera: the key keeps its place
        # relative to the CAMERA (from the picture's upper right, in front), as a painting's light does when it is copied view by view
        so.rotation_quaternion = cam.rotation_quaternion @ Vector((-0.6, -0.7, -1.0)).normalized().to_track_quat('-Z', 'Y')
    sc.frame_set(f); sc.render.filepath = f'{out}/{PASS}/{f:03d}.png'
    bpy.ops.render.render(write_still=True)
    R_ = cam.rotation_quaternion.to_matrix()
    cams.append({'fov_deg': FOV or None, 'frame': f, 'azimuth_deg': round(math.degrees(az), 4) if PATH == 'gc' else (CAMS[f]['azimuth_deg'] if CAMS else round(-5 * f * 72 / (N - 1), 4)),
                 'elevation_deg': round(math.degrees(el), 3) if PATH == 'gc' else round(elevation(f), 3), 'right': [round(x, 6) for x in R_.col[0]], 'up': [round(x, 6) for x in R_.col[1]],
                 'location': [round(x, 5) for x in cam.location], 'ortho_scale': cam_data.ortho_scale})
json.dump({'path': PATH, 'size': SIZE, 'frames': N, 'distance': DIST, 'target': list(target), 'figure_height_m': H, 'cameras': cams},
          open(f'{out}/cameras.json', 'w'), indent=1)
print('rendered', N)
