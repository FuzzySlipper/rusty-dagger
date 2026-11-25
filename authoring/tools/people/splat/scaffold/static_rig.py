#!/usr/bin/env python3
"""A scaffold for a static NPC: a clay mesh with a short bone chain instead of an auto-rig, for the bound route.

    static_rig.py MESH.glb OUT_SCAFFOLD.glb [--height 1.78] [--tris 50000] [--report OUT.json] [--stick FIT.json]
                  [--view-yaw DEG | --view-yaw auto --front FRONT-CUT.png --back BACK-CUT.png]

A figure that never walks (Patch 2026-11-22: "they'll just be standing there and doing little idle twitches and maybe turning
their head and blinking") needs no limbs, and an auto-rigger cannot find limbs in a closed robe anyway. So the mesher's clay
(Meshy, untextured, front +z) is decimated to about --tris triangles in Blender, stood on y = 0 at the origin at --height metres,
and given the joints the bound tools look for: Hips, Spine, Spine1, Spine2, Neck, Head, head_end, headfront (a chain up the
middle, for breathing, a small sway and head turns) and LeftHand, RightHand where the hands are (bound_data.py gives the bake
more room round them; here they stay still). Weights by position:
  - hands: within 9 cm of a hand joint, that hand, and anything forward of the body above them (a staff's head) when a staff
    stands down the middle in front (021; not for a hunched figure, whose chin is there: 017);
  - a staff or anything else thin and forward of the body below the hands: Hips, so it stays rigid;
  - the head: above the neck, Head (the whole hood, its point and its forward lip too); blended down to Neck over 4 cm;
  - parts more than 17 cm to the side of the head's middle at shoulder height (a cloak's shoulder points): Spine2;
  - everything else blends linearly between the two chain joints at its height (Hips below the hips).
The hands are found as the most forward surface between 0.62 and 0.80 of the height, each side of the middle (--hands front,
the default); --hands sides takes the outermost surface between 0.40 and 0.56 of it, for arms hanging at the sides (023).
--stick writes the stick figure guided_clip.sh's guide step takes (FIT=FIT.json), scaled to a 1.72 m figure as stick_fit.py
writes it: neck at Neck, shoulders between Neck and Spine2 17 cm to each side, wrists at the hands, elbows at the sides 12 cm
below the lower of shoulder and wrist, hips 9 cm to each side at Hips, straight legs under the robe. 021's hand-written stick
(2026-11-22) had the same layout.
A head far forward of the body's middle (a hunched figure, 017) gets its Neck and Head 11 cm behind the face's front, so a
head turn turns the face in place instead of swinging it sideways away from the chin. --head-axis X,Z sets that axis from the
bake's own face (017's Meshy face sat 11 cm to the side of the baked one); static-npc's npc.json "head_axis" passes it.
--view-yaw: the paintings' view, in degrees round the figure from the mesh's front (+z) towards her right (-x). A painting
drawn at an angle (017, the crone, three-quarter from her right front) gets a mesher's body that faces +z all the same, so the
scaffold stays square and only the stick is turned by this much, so that the orbit guide opens on the painting's view.
"auto" takes the turn, within 60 degrees either way, whose mesh outline best matches both cut paintings (back at +180); the
report records it as view_yaw, and the score step (bound_run.sh YAW=) starts its fit from it.
Written for 021, the static NPC pilot (2026-11-22); --view-yaw for 017 (2026-11-22).
"""
import argparse
import json
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from eye_mask import acc, read_glb, write_glb   # noqa: E402

BLENDER = """
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]; src, out, tris = a[0], a[1], int(a[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
n = len(ob.data.polygons)
m = ob.modifiers.new('d', 'DECIMATE'); m.ratio = min(1.0, tris / max(n, 1))
bpy.ops.object.modifier_apply(modifier='d')
bpy.ops.object.shade_smooth()
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True, export_normals=True, export_materials='NONE',
                          export_texcoords=False, export_yup=True)
print('DECIMATED', n, len(ob.data.polygons))
"""


def view_yaw(P, front, back, H=200):
    """The turn (degrees) whose outlines of P best match the front cut-out at it and the back cut-out (if any) at it + 180."""
    from PIL import Image
    from scipy.ndimage import binary_dilation, binary_fill_holes

    def sil(path):
        m = np.asarray(Image.open(path).convert('RGBA'))[..., 3] > 128
        ys, xs = np.nonzero(m); m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        w = max(1, int(m.shape[1] * H / m.shape[0]))
        return np.asarray(Image.fromarray(m.astype(np.uint8) * 255).resize((w, H))) > 128

    def proj(deg):
        t = np.radians(deg); x = np.cos(t) * P[:, 0] + np.sin(t) * P[:, 2]; y = P[:, 1]; k = (H - 1) / (y.max() - y.min())
        xi = ((x - x.min()) * k).astype(int); yi = ((y.max() - y) * k).astype(int)
        m = np.zeros((H, xi.max() + 1), bool); m[yi, xi] = True
        return binary_fill_holes(binary_dilation(m, iterations=2))

    def iou(a, b):
        w = max(a.shape[1], b.shape[1]); A = np.zeros((H, w), bool); B = A.copy(); A[:, :a.shape[1]] = a; B[:, :b.shape[1]] = b
        return (A & B).sum() / (A | B).sum()
    F = sil(front); B = sil(back) if back else None   # no back painting (023): the front outline alone
    scores = {d: (iou(proj(d), F) + iou(proj(d + 180), B)) / 2 if B is not None else iou(proj(d), F) for d in range(-60, 61, 5)}
    best = max(scores, key=scores.get)
    return best, round(float(scores[best]), 3), round(float(scores[0]), 3)


def turn_stick(fit, deg):
    t = np.radians(deg); c, s_ = np.cos(t), np.sin(t)
    for k, (x, y, z) in fit['joints'].items():
        fit['joints'][k] = [round(c * x + s_ * z, 3), y, round(-s_ * x + c * z, 3)]
    fit['source'] += f'; turned {deg} degrees to the paintings\' view (--view-yaw)'
    return fit


def stick(J, height, mesh, P=None, prop=None, elbows=None):
    """stick_fit.py's joints for a figure with no limbs, from the short chain (1.72 m, front +z)."""
    s = 1.72 / height
    p = lambda v: [round(float(x), 3) for x in v]
    sho_y = (J['Neck'][1] + J['Spine2'][1]) / 2 * s
    out = {'neck': p(J['Neck'] * s)}
    for side, sx, hand in (('r', -1, 'RightHand'), ('l', 1, 'LeftHand')):
        w = J[hand] * s
        out[side + 'sho'] = p([0.17 * sx, sho_y, 0.0])
        if elbows and hand in elbows:   # --elbows: read off the mesh (npc-026's akimbo and staff arms)
            out[side + 'elb'] = p(np.asarray(elbows[hand]) * s)
        elif P is not None:   # --body-axis (npc-026): the elbow halfway between shoulder and wrist, pushed 10 cm out and 5 cm down
            sh = np.array([0.17 * sx, sho_y, 0.0])
            out[side + 'elb'] = p((sh + w) / 2 + [0.10 * sx, -0.05, 0.0])
        elif w[1] < sho_y - 0.35:   # a hanging arm (023): the elbow halfway down it, a little out
            out[side + 'elb'] = p([(0.17 * sx + w[0]) / 2 + 0.02 * sx, (sho_y + w[1]) / 2, w[2] * 0.5])
        else:
            out[side + 'elb'] = p([0.20 * sx, min(sho_y, w[1]) - 0.12, w[2] * 0.2])
        out[side + 'wri'] = p(w)
        hy = J['Hips'][1] * s
        out[side + 'hip'] = p([0.09 * sx, hy, 0.0])
        out[side + 'kne'] = p([0.10 * sx, hy * 0.53, 0.0])
        out[side + 'ank'] = p([0.10 * sx, 0.08, 0.0])
        if P is not None:   # --body-axis: ankles over the feet (a wide stance), knees between hip and ankle
            q = (P[:, 1] < 0.08) & (sx * P[:, 0] > 0.02)
            if prop is not None and np.sign(prop[0]) == sx:
                q &= sx * P[:, 0] < sx * prop[0] - 0.08          # not the staff's foot
            if q.sum() > 20:
                ft = P[q].mean(0) * s
                out[side + 'ank'] = p([ft[0], 0.08, ft[2]])
                out[side + 'kne'] = p([(0.09 * sx + ft[0]) / 2, hy * 0.53, ft[2] / 2])
    return {'joints': out, 'source': f'static_rig.py --stick from {mesh}: the short chain\'s Neck, Spine2, Hips and hands, the limbs laid out as for 021'}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('mesh'); ap.add_argument('out')
    ap.add_argument('--height', type=float, default=1.78); ap.add_argument('--tris', type=int, default=50000)
    ap.add_argument('--report')
    ap.add_argument('--stick', help='write the stick-figure joints for guided_clip.sh FIT= here')
    ap.add_argument('--view-yaw', default='0', help='degrees the paintings are turned from the front, or auto (with --front, --back)')
    ap.add_argument('--front'); ap.add_argument('--back')
    ap.add_argument('--hands', default='front', help="front (held before the body: 021, 017), sides (hanging at the thighs: 023) or "
                    "'X,Y,Z;X,Y,Z' metres, left then right")
    ap.add_argument('--head-axis', help='X,Z metres: the Neck/Head turning axis, 11 cm behind the baked face (017: -0.02,0.26)')
    ap.add_argument('--wide', type=float, default=0.17, help='metres from the head\'s middle beyond which the shoulder zone goes to Spine2 '
                    '(a cloak\'s shoulder points); wider for big hair, which should turn with the head (npc-026: 0.30)')
    ap.add_argument('--elbows', help="'X,Y,Z;X,Y,Z' metres, left then right: the stick's elbows (default: laid out from shoulder and wrist)")
    ap.add_argument('--body-axis', action='store_true', help='centre on the body (median at 0.25-0.55 of the height) and take the '
                    'height from the crown, not the bounding box: a prop held to one side and above the head (npc-026)')
    ap.add_argument('--prop-hand', choices=['LeftHand', 'RightHand'], help='a staff held to that side: everything beyond the hand\'s '
                    'inner edge on that side goes wholly to that hand, so it stays rigid (npc-026)')
    a = ap.parse_args()
    with tempfile.TemporaryDirectory() as td:
        script = Path(td) / 'dec.py'; script.write_text(BLENDER); tmp = Path(td) / 'dec.glb'
        r = subprocess.run(['blender', '-b', '--factory-startup', '--python', str(script), '--', a.mesh, str(tmp), str(a.tris)],
                           capture_output=True, text=True)
        if not tmp.exists():
            raise SystemExit(f'blender decimate failed\n{r.stdout[-1500:]}\n{r.stderr[-1500:]}')
        dec = next((ln for ln in r.stdout.splitlines() if ln.startswith('DECIMATED')), '')
        g, b = read_glb(tmp)
    pr = g['meshes'][0]['primitives'][0]
    P = acc(g, b, pr['attributes']['POSITION']).astype(np.float64); N = acc(g, b, pr['attributes']['NORMAL']).astype(np.float64)
    I = acc(g, b, pr['indices']).astype(np.uint32).ravel()
    node = next(n for n in g['nodes'] if 'mesh' in n)
    if any(k in node for k in ('rotation', 'scale', 'matrix')):   # bake the node transform in (Blender's export is usually identity)
        from scipy.spatial.transform import Rotation as Rot
        S = np.diag(node.get('scale', [1, 1, 1])); Rm = Rot.from_quat(node.get('rotation', [0, 0, 0, 1])).as_matrix()
        P = P @ (Rm @ S).T; N = N @ Rm.T
    lo, hi = P.min(0), P.max(0); k = a.height / (hi[1] - lo[1])
    P = (P - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * k
    H = a.height
    if a.body_axis:   # npc-026: her staff stands beside her and its bat rises above her head; the box would shrink and shift her
        mid = (P[:, 1] > 0.25 * H) & (P[:, 1] < 0.55 * H)
        P[:, [0, 2]] -= np.median(P[mid][:, [0, 2]], 0)
        crown = np.percentile(P[np.hypot(P[:, 0], P[:, 2]) < 0.15, 1], 99.5)
        P *= H / crown; k *= H / crown
    y = P[:, 1]
    hands = {}
    if a.hands == 'front':   # the hands held in front (021's staff, 017's claw): the most forward surface in the hands' band, each side
        band = (y > 0.62 * H) & (y < 0.80 * H)
        for side, sg in (('LeftHand', 1), ('RightHand', -1)):
            q = band & (np.sign(P[:, 0]) == sg) & (np.abs(P[:, 0]) > 0.01)
            zf = np.percentile(P[q, 2], 99); s = q & (P[:, 2] > zf - 0.06)
            hands[side] = P[s].mean(0) - [0, 0, 0.03]
    elif a.hands == 'sides':   # arms hanging at the sides (023): the outermost surface at the thighs, each side, 3 cm in
        band = (y > 0.40 * H) & (y < 0.56 * H)
        for side, sg in (('LeftHand', 1), ('RightHand', -1)):
            q = band & (sg * P[:, 0] > 0.05)
            xo = np.percentile(sg * P[q, 0], 99); s = q & (sg * P[:, 0] > xo - 0.04)
            hands[side] = P[s].mean(0) - [0.03 * sg, 0, 0]
    else:   # X,Y,Z;X,Y,Z metres (left, then right), from scaffold_views.py
        for side, v in zip(('LeftHand', 'RightHand'), a.hands.split(';')):
            hands[side] = np.array([float(t) for t in v.split(',')])
    hy = float(np.mean([v[1] for v in hands.values()]))
    chain = {'Hips': 0.47, 'Spine': 0.56, 'Spine1': 0.64, 'Spine2': 0.72, 'Neck': 0.81, 'Head': 0.86}
    J = {n: np.array([0.0, f * H, 0.0]) for n, f in chain.items()}
    head_top = float(np.percentile(y[(np.hypot(P[:, 0], P[:, 2]) < 0.12)], 99.5))
    J['head_end'] = np.array([0.0, min(head_top, 0.95 * H), 0.0]); J['headfront'] = J['Head'] + [0, 0, 0.10]
    J.update(hands)
    # the head's turning axis: a hunched head sits far forward of the body's middle (017: face 35 cm out), and a turn about the
    # middle swings the face sideways while the Neck-led chin follows less of it, tearing the face. Neck, Head and their ends move
    # to 11 cm behind the face's front when that is more than 8 cm forward of the middle; an upright head (021) stays put.
    h9 = np.abs(y - 0.9 * H) < 0.02 * H; hx = (P[h9, 0].min() + P[h9, 0].max()) / 2   # the head's middle, as hc below
    fb = (y > J['Neck'][1]) & (y < J['Head'][1] + 0.08) & (np.abs(P[:, 0] - hx) < 0.12)   # not a staff's top beside the head
    zf = np.percentile(P[fb, 2], 95); fr = fb & (P[:, 2] > zf - 0.03)
    axis = np.array([P[fr, 0].mean(), zf - 0.11])
    if a.head_axis:                                       # measured on the bake's face (the mesher's face can sit to one side)
        axis = np.array([float(v) for v in a.head_axis.split(',')])
    if axis[1] > 0.08:
        for n in ('Neck', 'Head', 'head_end', 'headfront'):
            J[n] = J[n] + [axis[0], 0, axis[1]]
    names = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'head_end', 'headfront', 'LeftHand', 'RightHand']
    parent = {'Hips': None, 'Spine': 'Hips', 'Spine1': 'Spine', 'Spine2': 'Spine1', 'Neck': 'Spine2', 'Head': 'Neck',
              'head_end': 'Head', 'headfront': 'Head', 'LeftHand': 'Spine2', 'RightHand': 'Spine2'}
    ix = {n: i for i, n in enumerate(names)}
    # weights
    Wt = np.zeros((len(P), len(names)))
    rad = np.hypot(P[:, 0], P[:, 2])
    seq = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head']; ys = np.array([J[n][1] for n in seq])
    t = np.clip(np.interp(y, ys, np.arange(len(seq))), 0, len(seq) - 1); i0 = np.floor(t).astype(int); i1 = np.minimum(i0 + 1, len(seq) - 1); f = t - i0
    for kk in range(len(P)):
        Wt[kk, ix[seq[i0[kk]]]] += 1 - f[kk]; Wt[kk, ix[seq[i1[kk]]]] += f[kk]
    # the head's own axis: the middle of the figure at 0.9 of its height (a hood hangs forward of the body's axis)
    hs = np.abs(y - 0.9 * H) < 0.02 * H; hc = np.array([(P[hs, 0].min() + P[hs, 0].max()) / 2, (P[hs, 2].min() + P[hs, 2].max()) / 2])
    wide = (y > J['Neck'][1] - 0.06) & (np.abs(P[:, 0] - hc[0]) > a.wide)   # shoulder points / cloak at the shoulders, beside the head
    Wt[wide] = 0; Wt[wide, ix['Spine2']] = 1
    hd = (y > J['Neck'][1]) & ~wide                        # the head and the whole hood
    fh = np.clip((y - J['Neck'][1]) / 0.04, 0, 1)
    Wt[hd] = 0; Wt[hd, ix['Head']] = fh[hd]; Wt[hd, ix['Neck']] = 1 - fh[hd]
    staff = (y < hy - 0.05) & (np.abs(P[:, 0]) < 0.04) & (P[:, 2] > 0.12)
    Wt[staff] = 0; Wt[staff, ix['Hips']] = 1
    # the staff's head just above the hands goes with them (not the hood's front rim higher up)
    knob = (y >= hy - 0.05) & (y <= hy + 0.22) & (np.abs(P[:, 0]) < 0.10) & (P[:, 2] > 0.18)
    # only for a staff standing down the middle in front (021: 1,020 vertices over 1.19 m); without one the zone above the
    # hands is the chin and chest of a hunched figure (017: her lower face went to the hands and sheared in the look clip)
    if staff.sum() < 100 or np.ptp(y[staff]) < 0.5:
        knob[:] = False
    for side in ('LeftHand', 'RightHand'):
        near = (np.linalg.norm(P - J[side], axis=1) < 0.09) | knob
        if side == 'LeftHand':
            near &= P[:, 0] >= 0
        else:
            near &= P[:, 0] < 0
        Wt[near] = 0; Wt[near, ix[side]] = 1
    if a.prop_hand:   # a staff to one side (npc-026): beyond the hand's inner edge, the hand's alone, so it does not bend
        sg = 1 if a.prop_hand == 'LeftHand' else -1
        prop = sg * P[:, 0] > sg * J[a.prop_hand][0] - 0.06
        Wt[prop] = 0; Wt[prop, ix[a.prop_hand]] = 1
    top = np.argsort(-Wt, 1)[:, :4]; tw = np.take_along_axis(Wt, top, 1); tw /= tw.sum(1, keepdims=True)
    # write the glTF: mesh, armature, skin
    out = {'asset': {'version': '2.0', 'generator': 'static_rig.py',
                     'extras': {'static_rig': {'source': a.mesh, 'height_m': a.height, 'decimate': dec, 'joints': {n: J[n].round(4).tolist() for n in names}}}},
           'scene': 0, 'scenes': [{'nodes': [0, 1]}], 'nodes': [], 'meshes': [], 'skins': [], 'accessors': [], 'bufferViews': [], 'buffers': []}
    blob = bytearray()

    def put(arr, ctype, typ, target=None):
        arr = np.ascontiguousarray(arr); blob.extend(b'\0' * (-len(blob) % 4)); off = len(blob); blob.extend(arr.tobytes())
        bv = {'buffer': 0, 'byteOffset': off, 'byteLength': arr.nbytes}
        if target:
            bv['target'] = target
        out['bufferViews'].append(bv)
        acd = {'bufferView': len(out['bufferViews']) - 1, 'componentType': ctype, 'count': len(arr), 'type': typ}
        if typ == 'VEC3' and ctype == 5126:
            acd['min'] = arr.min(0).tolist(); acd['max'] = arr.max(0).tolist()
        out['accessors'].append(acd); return len(out['accessors']) - 1
    a_pos = put(P.astype(np.float32), 5126, 'VEC3', 34962)
    nn = N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
    a_nrm = put(nn.astype(np.float32), 5126, 'VEC3', 34962)
    a_j = put(top.astype(np.uint8), 5121, 'VEC4', 34962); a_w = put(tw.astype(np.float32), 5126, 'VEC4', 34962)
    a_i = put(I, 5125, 'SCALAR', 34963)
    out['meshes'].append({'name': 'scaffold', 'primitives': [{'attributes': {'POSITION': a_pos, 'NORMAL': a_nrm, 'JOINTS_0': a_j, 'WEIGHTS_0': a_w}, 'indices': a_i}]})
    out['nodes'].append({'name': 'scaffold', 'mesh': 0, 'skin': 0})
    out['nodes'].append({'name': 'Armature', 'children': [2]})
    base = 2
    for i, n in enumerate(names):
        p = parent[n]; tr = J[n] - (J[p] if p else 0)
        out['nodes'].append({'name': n, 'translation': np.asarray(tr, float).round(6).tolist()})
    for i, n in enumerate(names):
        ch = [base + names.index(c) for c in names if parent[c] == n]
        if ch:
            out['nodes'][base + i]['children'] = ch
    ibm = np.stack([np.eye(4) for _ in names]).astype(np.float32)
    for i, n in enumerate(names):
        ibm[i][:3, 3] = -J[n]
    a_ibm = put(np.ascontiguousarray(ibm.transpose(0, 2, 1)).reshape(len(names), 16), 5126, 'MAT4')
    out['skins'].append({'joints': [base + i for i in range(len(names))], 'inverseBindMatrices': a_ibm, 'skeleton': base})
    out['buffers'].append({'byteLength': len(blob)})
    write_glb(a.out, out, blob)
    rep = {'out': a.out, 'decimate': dec, 'vertices': len(P), 'triangles': len(I) // 3, 'height_m': a.height, 'joints': len(names),
           'joint_places': {n: J[n].round(3).tolist() for n in names},
           'dominant': {n: int((top[:, 0] == ix[n]).sum()) for n in names}}
    # the head zone's binding (017, 2026-11-22: the knob rule gave her chin to the hands and her face sheared in the look clip):
    # the share of vertices between the Neck joint and the head top led by Neck or the head's joints; anything else tears the face
    hz = (P[:, 1] > J['Neck'][1]) & (np.abs(P[:, 0] - J['Head'][0]) < 0.15)
    rep['head_zone_on_head'] = round(float(np.isin(top[hz, 0], [ix[n] for n in ('Neck', 'Head', 'head_end', 'headfront')]).mean()), 4) if hz.any() else None
    if a.view_yaw == 'auto':
        if not a.front:
            raise SystemExit('static_rig.py: --view-yaw auto needs --front (and --back, when there is a back painting)')
        yaw, best, square = view_yaw(P, a.front, a.back)
        rep['view_yaw'] = yaw; rep['view_yaw_iou'] = {'best': best, 'square': square}
    else:
        rep['view_yaw'] = float(a.view_yaw)
    if a.report:
        Path(a.report).write_text(json.dumps(rep, indent=1) + '\n')
    if a.stick:
        Path(a.stick).write_text(json.dumps(turn_stick(stick(J, a.height, a.mesh, P if a.body_axis else None,
                                                             J[a.prop_hand] if a.prop_hand else None,
                                                             {h: [float(t) for t in v.split(',')] for h, v in zip(('LeftHand', 'RightHand'), a.elbows.split(';'))} if a.elbows else None),
                                                       rep['view_yaw']), indent=1) + '\n')
    print(json.dumps(rep))


if __name__ == '__main__':
    main()
