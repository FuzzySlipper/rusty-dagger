"""dagger-char run ID 09-game --step mocap: a mocap clip (a Mixamo FBX, unedited) on the character's Mixamo-rig GLB, shown in the
character page frame by frame (lead for Patch, 2026-11-15: "we need to see these with natural motion and a mocap source").

The page plays only 441-skeleton clips on the .vsplat, so the Mixamo GLB is posed offline, as the GLB checks pose it
(splat_glb_pose_check.py: plain glTF skinning by joint name, rotation always, translation only where it varies), and each held
frame is written as a static splat (centres and rotations posed, in the .vsplat's own frame) that the page shows as a layer over
an empty body, in the rest clip: the same renderer, cameras and finish as the standard captures.

  clip      fbx_clip_dump.mjs reads the FBX (three.js FBXLoader; ASCII or binary) at 30 fps; written as a clip GLB in metres:
            every mixamorig bone's local rotation per frame, the Hips' world rotation and position (Y up, the file's cm / 100)
  fit       splat_glb_clip_fit.py ... walk=CLIP:leg - the only fitting: k from the leg ratio (his thigh + shin against the clip
            skeleton's), A keeping the lowest toe at its rest height; the Hips translation becomes (k x, A + k y, k z). Nothing else
            is changed: no arm offset, no rotation rewritten
  frames    posed per sampled frame -> 09-game/wip/mocap/frames/fNNN.vsplat (+ bind.vsplat, the GLB's bind pose)
  known     (1) bone directions of the posed GLB against the FBX's own at every frame (the GLB rig keeps Mixamo's bone axes, so the
            same local rotations must give the same directions; positions differ by his proportions, reported after scaling by k);
            (2) bind.vsplat shown by this path against the page's own render of the body at rest (pixel difference)
Captures and sheets are made by mocap_captures()."""
import json, os, struct, subprocess, sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as Rot
import dagger_char as dc
import rig as R

HERE = Path(__file__).parent
sys.path.insert(0, str(dc.REPO / 'authoring/tools/people/splat/gsplat'))
from splat_glb_pose_check import read, load_clip, sample, varies, trs   # noqa: E402
from make_skinned_vsplat import write_vsplat                              # noqa: E402
SOURCES = {'gvrm-walking': lambda cid: dc.REPO / 'external/gaussian-vrm/assets/Walking.fbx',
           'patch-walking': lambda cid: dc.char_dir(cid) / '09-game/wip/clips/mixamo-walking-patch-20261115.fbx',
           'as35-walking': lambda cid: dc.char_dir(cid) / '09-game/wip/clips/mixamo-walking-armspace35-20261115.fbx'}
LABEL = {'gvrm-walking': 'Mixamo Walking from external/gaussian-vrm (unedited)', 'patch-walking': 'Mixamo Walking, downloaded by Patch 2026-11-15',
         'as35-walking': 'Mixamo Walking (the same animation), Character Arm-Space 35 instead of the default 50, downloaded 2026-11-15'}


def sh(cmd, **kw):
    return subprocess.run([str(c) for c in cmd], text=True, capture_output=True, **kw)


def clip_glb(dump, out):
    """The dumped FBX clip as a glTF clip: one node per bone (rest TRS in metres), rotation channels for every bone, the Hips' world
    rotation and translation on the (parentless) Hips."""
    d = json.load(open(dump)); B = d['bones']; n = d['frames']; t = np.arange(n, dtype=np.float32) / d['fps']
    nodes = []
    for i, b in enumerate(B):
        nd = {'name': b['name'], 'translation': [v / 100 for v in b['rest_t']], 'rotation': b['rest_q']}
        ch = [j for j, c in enumerate(B) if c['parent'] == i]
        if ch:
            nd['children'] = ch
        nodes.append(nd)
    blobs = [t.tobytes()]; accessors = [{'bufferView': 0, 'componentType': 5126, 'count': n, 'type': 'SCALAR', 'min': [float(t[0])], 'max': [float(t[-1])]}]
    channels, samplers = [], []

    def add(arr, typ):
        blobs.append(np.asarray(arr, np.float32).tobytes())
        accessors.append({'bufferView': len(blobs) - 1, 'componentType': 5126, 'count': n, 'type': typ}); return len(accessors) - 1
    for i, b in enumerate(B):
        q = np.array([s[i]['world_q' if i == 0 else 'local'] for s in d['samples']])
        for k in range(1, n):   # one hemisphere, so interpolation does not flip
            if q[k] @ q[k - 1] < 0:
                q[k] = -q[k]
        samplers.append({'input': 0, 'output': add(q, 'VEC4'), 'interpolation': 'LINEAR'}); channels.append({'sampler': len(samplers) - 1, 'target': {'node': i, 'path': 'rotation'}})
        if i == 0:
            p = np.array([s[0]['world_p'] for s in d['samples']]) / 100
            samplers.append({'input': 0, 'output': add(p, 'VEC3'), 'interpolation': 'LINEAR'}); channels.append({'sampler': len(samplers) - 1, 'target': {'node': 0, 'path': 'translation'}})
    views, off, binary = [], 0, b''
    for blob in blobs:
        views.append({'buffer': 0, 'byteOffset': off, 'byteLength': len(blob)}); binary += blob; off += len(blob)
        pad = (-len(binary)) % 4; binary += b'\0' * pad; off += pad
    doc = {'asset': {'version': '2.0', 'generator': 'dagger-char mocap.py (from ' + os.path.basename(d['source']) + ')'}, 'scene': 0,
           'scenes': [{'nodes': [0]}], 'nodes': nodes, 'buffers': [{'byteLength': len(binary)}], 'bufferViews': views, 'accessors': accessors,
           'animations': [{'name': 'clip', 'channels': channels, 'samplers': samplers}]}
    js = json.dumps(doc).encode(); js += b' ' * ((-len(js)) % 4)
    with open(out, 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binary)))
        f.write(struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(binary), 0x004E4942) + binary)
    return d


def pose_glb(glb, clip, t, k, A):
    """splat_glb_pose_check.pose with the hips fit: posed centres, each Gaussian's blended 3x3, and the joints' world matrices."""
    doc, accessor = read(glb); nodes = doc['nodes']; skin = doc['skins'][0]
    parent = {c: i for i, nd in enumerate(nodes) for c in nd.get('children', [])}
    tracks, wrapper, _ = clip; top = next(j for j in skin['joints'] if parent.get(j) not in skin['joints'])
    local = {}
    for i, nd in enumerate(nodes):
        m = trs(nd); name = nd.get('name')
        if i in skin['joints']:
            r = tracks.get((name, 'rotation')); tr = tracks.get((name, 'translation'))
            rot = sample(r, t) if r else nd.get('rotation', [0, 0, 0, 1])
            pos = np.array(sample(tr, t)) if tr is not None and varies(tr) else np.array(nd.get('translation', [0, 0, 0]), float)
            if i == top and tr is not None and varies(tr):
                pos = np.array([k * pos[0], A + k * pos[1], k * pos[2]])
            m = trs({'rotation': list(rot), 'translation': list(pos)})
        local[i] = m
    world = {}

    def w(i):
        if i not in world:
            world[i] = local[i] if i not in parent else w(parent[i]) @ local[i]
        return world[i]
    ibm = accessor(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    palette = np.array([w(j) @ ibm[q] for q, j in enumerate(skin['joints'])])
    body = next(i for i, nd in enumerate(nodes) if nd.get('skin') is not None)
    prim = doc['meshes'][nodes[body]['mesh']]['primitives'][0]['attributes']
    P = accessor(prim['POSITION']); J = accessor(prim['JOINTS_0']).astype(int); W = accessor(prim['WEIGHTS_0']); W = W / W.sum(1, keepdims=True)
    blend = np.einsum('nk,nkij->nij', W, palette[J])
    return np.einsum('nij,nj->ni', blend[:, :3, :3], P) + blend[:, :3, 3], blend[:, :3, :3], {nodes[j]['name']: w(j) for j in skin['joints']}, P


def kabsch(a, b):
    """Rotation R and translation t with R a + t ~ b."""
    ca, cb = a.mean(0), b.mean(0); U, _, Vt = np.linalg.svd((a - ca).T @ (b - cb)); D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(Vt.T @ U.T))
    Rm = Vt.T @ D @ U.T; return Rm, cb - Rm @ ca


def write_static(path, r, means_v, rot_v):
    """A static splat in the .vsplat's frame: the rig's Gaussians with posed centres and rotations (the rig's bones and weights kept,
    so the page links it to the armature; at rest it shows the stored centres)."""
    from layer_rig import pack
    q = (Rot.from_matrix(rot_v) * Rot.from_quat(r['quats'][:, [1, 2, 3, 0]])).as_quat()[:, [3, 0, 1, 2]]
    stored, infl, palette = pack(r['bones'], r['weights'])
    return write_vsplat(str(path), means_v, r['scales'], q, r['opacities'], r['rgb'], stored, infl, palette, r['joints'])


def mocap(cid, tier='48k', source='gvrm-walking'):
    d = dc.char_dir(cid); w = d / '09-game/wip'; mo = w / 'mocap'; fr = mo / f'frames-{source}'; fr.mkdir(parents=True, exist_ok=True)
    glb = d / f'09-game/game/mixamo/{cid}-{tier}.glb'; r = np.load(w / f'rig/{cid}-{tier}-rig.npz')
    dump = mo / f'{source}.json'
    if not dump.exists():
        o = sh(['node', HERE / 'fbx_clip_dump.mjs', SOURCES[source](cid), dump])
        if o.returncode:
            raise SystemExit(o.stderr[-800:])
    cg = w / f'clips/{source}.glb'; D = clip_glb(dump, cg)
    fit = mo / f'fit-{source}.json'
    o = sh(['python3', '-I', dc.REPO / 'authoring/tools/people/splat/gsplat/splat_glb_clip_fit.py', glb, fit, f'walk={cg}:leg'])
    if o.returncode:
        raise SystemExit(o.stderr[-800:])
    F = json.load(open(fit))['clips']['walk']; k, A = F['k'], F['A']
    clip = load_clip(str(cg))
    # The GLB's bind against the .vsplat rest: the rigid map between their frames (same Gaussians, same order).
    _, _, _, P = pose_glb(glb, clip, 0, k, A)
    Rc, tc = kabsch(P, r['rest_means']); resid = float(np.abs(P @ Rc.T + tc - r['rest_means']).max())
    write_static(fr / 'bind.vsplat', r, r['rest_means'], np.tile(np.eye(3), (len(P), 1, 1)))
    # Frames through the retarget (Retarget: X Bot's change from its own bind, applied to our bind / T-equivalent). k stays the leg
    # ratio; A is refitted on the retargeted pose the same way (the lower toe joint at its rest height, median over the cycle).
    rt = Retarget(glb); names = [b['name'] for b in D['bones']]; S0 = D['samples']
    aq = lambda S: {n: S[names.index(n)]['world_q'] for n in rt.names.values()}   # noqa: E731
    hp = lambda S, A_: (lambda h: [k * h[0], A_ + k * h[1], k * h[2]])(np.array(S[0]['world_p']) / 100)   # noqa: E731
    toe_rest = min(rt.bind[j][1, 3] for j in rt.joints if rt.names[j].endswith('ToeBase'))
    low = [min(J[n][1][1] for n in J if n.endswith('ToeBase')) for J in (rt.pose(aq(S), hp(S, 0.0))[2] for S in S0[:-1])]
    A = float(toe_rest - np.median(low))
    pairs = [('LeftArm', 'LeftForeArm'), ('LeftForeArm', 'LeftHand'), ('RightArm', 'RightForeArm'), ('RightForeArm', 'RightHand'),
             ('LeftUpLeg', 'LeftLeg'), ('LeftLeg', 'LeftFoot'), ('RightUpLeg', 'RightLeg'), ('RightLeg', 'RightFoot')]
    dirs = {}; head = []
    for f in range(D['frames']):
        S = S0[f]; means, blend, J = rt.pose(aq(S), hp(S, A))
        U, _, Vt = np.linalg.svd(blend); rot = U @ Vt
        write_static(fr / f'f{f:03d}.vsplat', r, means @ Rc.T + tc, Rc @ rot @ Rc.T)
        np.save(fr / f'f{f:03d}-means.npy', means @ Rc.T + tc)   # for hem_on_frames
        np.save(fr / f'f{f:03d}-joints.npy', np.array([J[n][1] for n in sorted(J)]))
        for a_, b_ in pairs:
            na, nb = 'mixamorig:' + a_, 'mixamorig:' + b_
            g = J[nb][1] - J[na][1]; x = np.array(S[names.index(nb)]['world_p']) - np.array(S[names.index(na)]['world_p'])
            dirs[a_] = max(dirs.get(a_, 0.0), float(np.degrees(np.arccos(np.clip(g @ x / np.linalg.norm(g) / np.linalg.norm(x), -1, 1)))))
        # The head's tilt from upright: ours (its rest-upright direction turned by the head's geometry rotation) against X Bot's own
        # (its bind-upright direction turned by its change from bind).
        hj = next(j for j in rt.joints if rt.names[j] == 'mixamorig:Head'); up = np.array([0, 1.0, 0])
        ours = J['mixamorig:Head'][0] @ rt.bind[hj][:3, :3].T @ up
        xbot = Rot.from_quat(S[names.index('mixamorig:Head')]['world_q']).as_matrix() @ rt.xb[hj].T @ up
        head.append((float(np.degrees(np.arccos(np.clip(ours[1], -1, 1)))), float(np.degrees(np.arccos(np.clip(xbot[1], -1, 1))))))
    # Known case: X Bot's bind pose fed as the frame -> our rest for trunk, neck, head, legs and feet; the arms level in a T.
    bindq = {rt.names[j]: Rot.from_matrix(rt.xb[j]).as_quat() for j in rt.joints}
    _, _, JB = rt.pose(bindq, rt.bind[rt.order[0]][:3, 3])
    rest_dev = max(float(np.degrees((Rot.from_matrix(JB[rt.names[j]][0]).inv() * Rot.from_matrix(rt.bind[j][:3, :3])).magnitude()))
                   for j in rt.joints if not any(a_ in rt.names[j] for a_ in ARM))
    tdir = {}
    for a_, b_ in pairs[:4]:
        ja = next(j for j in rt.joints if rt.names[j] == 'mixamorig:' + a_); g = JB['mixamorig:' + b_][1] - JB['mixamorig:' + a_][1]; x = rt.xb[ja][:, 1]
        tdir[a_] = round(float(np.degrees(np.arccos(np.clip(g @ x / np.linalg.norm(g), -1, 1)))), 3)
    known = {'glb_to_vsplat_frame_residual_m': resid, 'retarget': 'D = R_anim_world . R_bind_world^-1 (X Bot, legacy/experiments/animations/mixamo/X Bot.fbx bind); posed = D . R_bind(ours), arm chain D . swing . R_bind(ours)',
             'xbot_bind_frame': {'trunk_legs_feet_head_max_deg_from_our_rest': round(rest_dev, 4), 'arm_bone_vs_xbot_T_deg': tdir},
             'limb_direction_max_deg_vs_xbot': {k_: round(v, 3) for k_, v in dirs.items()},
             'head_tilt_from_upright_deg_ours_vs_xbot_max_diff': round(max(abs(a_ - b_) for a_, b_ in head), 4),
             'head_tilt_from_upright_deg_range': [round(min(h[0] for h in head), 2), round(max(h[0] for h in head), 2)],
             'frames': D['frames'], 'fps': D['fps'],
             'fit': {'k': k, 'A': round(A, 4), 'how': 'k from the leg ratio (his thigh + shin against the clip skeleton, splat_glb_clip_fit.py); A keeps the lower toe joint at its rest height (median over the cycle, on the retargeted pose)',
                     'applied_to': '(k x, A + k y, k z) on mixamorig:Hips'}}
    json.dump(known, open(mo / f'known-{source}.json', 'w'), indent=1)
    print(json.dumps(known))
    return known


def page_setup(cid, source='gvrm-walking', tier='48k'):
    """Serve the frames as layer modes 'mocap:fNNN' (and 'mocap:bind') over an empty body, on a profile dagger-ID-mocap."""
    import shutil
    import armsdown
    d = dc.char_dir(cid); mo = d / '09-game/wip/mocap'; r = np.load(d / f'09-game/wip/rig/{cid}-{tier}-rig.npz')
    wd = R.page_files(cid, tier); (wd / f'mocap-{source}').mkdir(exist_ok=True)
    frames = sorted((mo / f'frames-{source}').glob('*.vsplat'))
    for f in frames:
        shutil.copy2(f, wd / f'mocap-{source}' / f.name)
    from layer_rig import pack
    stored, infl, palette = pack(r['bones'][:1], r['weights'][:1])
    write_vsplat(str(wd / 'mocap-empty.vsplat'), r['rest_means'][:1], r['scales'][:1], r['quats'][:1], np.full(1, -20.0), r['rgb'][:1], stored, infl, palette, r['joints'])
    cj = json.load(open(wd / 'character.json'))
    cj['layers'] = {'mocap': {'label': LABEL[source], 'modes': {f.stem: f'mocap-{source}/{f.name}' for f in frames}}}
    (wd / 'character.json').write_text(json.dumps(cj, indent=1))
    return armsdown.profile_q(cid, 'mocap', '&body=mocap-empty.vsplat', f'Mocap evidence: posed {source} frames as layer mocap:fNNN over an empty body (mocap.py).')


LAYER_SHOTS = """// Mocap frames as layers over an empty body, rest clip, held: $SHOTS (label = view-mode).
const shots = $SHOTS;
const out = [];
await browser({op: 'fill', selector: '#hold', value: '1'});
await browser({op: 'fill', selector: '#clip', value: 'rest'});
await browser({op: 'fill', selector: '#fx', value: 'native'});
for (const [view, mode] of shots) {
  await browser({op: 'fill', selector: '#view', value: view});
  await browser({op: 'fill', selector: '#layer', value: 'mocap:' + mode});
  await browser({op: 'fill', selector: '#tag', value: `${view}-${mode}`});
  await browser({op: 'click', selector: '#apply'});
  await sleep($SETTLE);
  out.push([`${view}-${mode}`, (await capture({label: `${view}-${mode}`})).path]);
}
const page = await browser({op: 'inspect'});
checkpoint('frames', {frames: out, console: (page.console || []).map((m) => m.text).filter((t) => /^(seen|applied) /.test(t))});
"""


def shots_script(shots, settle=700):
    return LAYER_SHOTS.replace('$SHOTS', json.dumps(shots)).replace('$SETTLE', str(settle))


def known_page(cid, source='gvrm-walking'):
    """Known case (2): the GLB's bind pose through this path (layer mocap:bind) against the page's own render of the body at rest."""
    import armsdown
    from PIL import Image, ImageChops
    d = dc.char_dir(cid); e = d / f'09-game/evidence/mocap/{source}'; e.mkdir(parents=True, exist_ok=True)
    pid = page_setup(cid, source)
    armsdown.capture(e, pid, {'known': shots_script([['front', 'bind'], ['side', 'bind'], ['back', 'bind']])})
    std = e / 'known-standard'; std.mkdir(exist_ok=True)
    armsdown.capture(std, f'dagger-{cid}', {'rest': armsdown.IDLE.replace("'idledown'", "'rest'").replace('idle-', 'rest-')})
    res = {}
    for v in ('front', 'side', 'back'):
        a = np.asarray(Image.open(e / f'known/{v}-bind.png').convert('RGB'), float); b = np.asarray(Image.open(std / f'rest/rest-{v}.png').convert('RGB'), float)
        dif = np.abs(a - b).max(2); res[v] = {'mean_abs': round(float(np.abs(a - b).mean()), 3), 'pixels_over_8': int((dif > 8).sum()), 'max': int(dif.max())}
        Image.fromarray(np.concatenate([a, b, np.clip(dif[..., None].repeat(3, 2) * 8, 0, 255)], 1).astype(np.uint8)).save(e / f'known-{v}-path-vs-page.png')
    json.dump(res, open(e / 'known-page.json', 'w'), indent=1); print(json.dumps(res)); return res


def arm_phases(dump):
    """Per frame, the left upper arm's forward and sideways angle from vertical (body frame from the shoulders and toes), and the
    frames where it is furthest forward, furthest back and straight down."""
    D = json.load(open(dump)); names = [b['name'] for b in D['bones']]; up = np.array([0, 1.0, 0]); per = {'l': [], 'r': []}
    for S in D['samples']:
        P = lambda n: np.array(S[names.index('mixamorig:' + n)]['world_p'])   # noqa: E731
        lat = P('LeftArm') - P('RightArm'); lat[1] = 0; lat /= np.linalg.norm(lat)
        fw = P('LeftToeBase') - P('LeftFoot') + P('RightToeBase') - P('RightFoot'); fw[1] = 0; fw /= np.linalg.norm(fw)
        for s, side, out in (('l', 'Left', lat), ('r', 'Right', -lat)):
            v = P(side + 'ForeArm') - P(side + 'Arm'); v /= np.linalg.norm(v)
            per[s].append((round(float(np.degrees(np.arctan2(v @ out, -v @ up))), 1), round(float(np.degrees(np.arctan2(v @ fw, -v @ up))), 1)))
    n = D['frames'] - 1   # the last sample repeats the first
    fwd = [p[1] for p in per['l'][:n]]
    # arm-down: the frame where the upper arm is nearest vertical overall (sideways and forward together), and not one of the other
    # two (a walk whose arm never swings past vertical has its furthest-forward frame there too)
    ks = {'arm-forward': int(np.argmax(fwd)), 'arm-back': int(np.argmin(fwd))}
    tot = [np.hypot(*per['l'][i]) if i not in ks.values() else 1e9 for i in range(n)]
    ks['arm-down'] = int(np.argmin(tot))
    return per, ks, n


def mocap_captures(cid, source='gvrm-walking', tier='48k'):
    import armsdown
    from PIL import Image, ImageDraw
    d = dc.char_dir(cid); mo = d / '09-game/wip/mocap'; e = d / f'09-game/evidence/mocap/{source}'; e.mkdir(parents=True, exist_ok=True)
    per, ks, n = arm_phases(mo / f'{source}.json'); pid = page_setup(cid, source)
    ph8 = [round(i * n / 8) for i in range(8)]
    shots = {'cycle': [[v, f'f{f:03d}'] for v in ('front', 'quarter', 'side', 'back') for f in ph8],
             'shoulders': [[v, f'f{f:03d}'] for v in ('front', 'back') for f in ks.values()]}
    vid = {f'vid-{p}': [['quarter', f'f{i % n:03d}'] for i in range(p * 24, min(3 * n, (p + 1) * 24))] for p in range((3 * n + 23) // 24)}
    out = armsdown.capture(e, pid, {k: shots_script(v, 600) for k, v in shots.items()})
    for k, v in vid.items():
        (e / 'scripts' / f'{k}.js').write_text(shots_script(v, 450).replace("`${view}-${mode}`", "'v' + String(out.length).padStart(3, '0')"))
    import shutil, subprocess as sp
    env = dict(os.environ, CAPTURE_SID=str(e / '.capture-sid')); shutil.rmtree(e / 'vid', ignore_errors=True); (e / 'vid').mkdir()
    frames = []
    for k in vid:
        rr = sp.run(['bash', str(R.CAP / 'run.sh'), f'scripts/{k}.js', k], cwd=e, env=env, text=True, capture_output=True); out[k] = rr.stdout.strip().splitlines()[-1:]
        for f in sorted((e / k).glob('v*.png')):
            frames.append(f)
    for i, f in enumerate(frames):
        shutil.copy2(f, e / f'vid/f{i:03d}.png')
    mp4 = e / f'{cid}-{source}-quarter.mp4'
    sh(['ffmpeg', '-loglevel', 'error', '-y', '-framerate', '30', '-pattern_type', 'glob', '-i', e / 'vid/f*.png', '-c:v', 'libx264', '-crf', '16', '-pix_fmt', 'yuv420p',
        '-vf', 'crop=1000:1080:460:0', mp4])
    nf = sh(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of', 'csv=p=0', mp4]).stdout.strip()
    out['video'] = {'frames_captured': len(frames), 'frames_encoded': nf, 'expected': 3 * n, 'cycles': 3, 'fps': 30}
    # Sheets: phases as columns, views as rows (labels view-fNNN -> files cycle/view-fNNN.png).
    rr = sh(['python3', R.SHEET, e / 'cycle-sheet.png', ','.join(f'f{f:03d}' for f in ph8), 'front,quarter,side,back', e / 'cycle/{row}-{col}.png'])
    out['cycle-sheet'] = {'rc': rr.returncode, 'touching_edge': json.load(open(e / 'cycle-sheet-tiles.json'))['touching_edge']}
    for ph, f in ks.items():
        for v in ('front', 'back'):
            shutil.copy2(e / f'shoulders/{v}-f{f:03d}.png', e / f'shoulders/{v}-{ph}.png')
    armsdown.shoulders(e, list(ks))
    # The same shoulder against the standard walk (PINOC Walking, page offset 10/15) at its own arm-forward / back / down phases.
    tk = dc.load(cid)['stages']['09-game']['checks']['arms_down']['shoulder_phases']; std = d / '09-game/evidence/mocap/standard'; std.mkdir(exist_ok=True)
    R.page_files(cid, tier)
    armsdown.capture(std, f'dagger-{cid}', {'shoulders': armsdown.SHOULDERS.replace('$TIMES', json.dumps(tk)).replace("'walkdown'", "'walk'")})
    page_setup(cid, source)
    rows = []
    for ph in ks:
        row = []
        for view, cx in (('front', 960 + 150), ('back', 960 - 150)):   # his left shoulder
            for name, folder in (('standard walk (PINOC, offset 10/15)', std / 'shoulders'), (f'mocap {source}', e / 'shoulders')):
                im = Image.open(folder / f'{view}-{ph}.png').convert('RGB')
                t = im.crop((cx - 140, 324 - 140, cx + 140, 324 + 140)).resize((700, 700), Image.LANCZOS)
                ImageDraw.Draw(t).text((8, 6), f'{ph}, {view}: {name}', fill=(255, 255, 255)); row.append(t)
        rows.append(row)
    sheet = Image.new('RGB', (706 * 4 - 6, 706 * len(rows) - 6), (255, 255, 255))
    for i, row in enumerate(rows):
        for j, t in enumerate(row):
            sheet.paste(t, (706 * j, 706 * i))
    sheet.save(e / 'shoulder-compare-2.5x.png')
    # Hem check on the posed frames (hem_check.py's measures; its posing step replaced by these frames).
    hem = hem_on_frames(cid, mo, ph8, tier, source)
    rec = {'when': dc.NOW(), 'source': LABEL[source], 'file': str(SOURCES[source](cid).relative_to(dc.REPO)), 'file_bytes': SOURCES[source](cid).stat().st_size, 'file_sha256': dc.file_hash(SOURCES[source](cid)), 'folder': f'09-game/evidence/mocap/{source}', 'profile': pid,
           'known': json.load(open(mo / f'known-{source}.json')), 'known_page': json.load(open(e / 'known-page.json')),
           'upper_arm_sideways_deg': {s: [min(p[0] for p in per[s][:n]), max(p[0] for p in per[s][:n])] for s in 'lr'},
           'upper_arm_forward_deg': {s: [min(p[1] for p in per[s][:n]), max(p[1] for p in per[s][:n])] for s in 'lr'},
           'phases': {'held': ph8, 'shoulders': ks}, 'captures': out, 'hem_check': hem}
    dc.patch(cid, lambda m: m['stages']['09-game']['checks'].__setitem__('mocap_' + source.replace('-', '_'), rec))
    print(json.dumps({k: v for k, v in rec.items() if k != 'captures'}, indent=1)[:3000]); print(json.dumps(out)[:1500])


def hem_on_frames(cid, mo, frames, tier='48k', source='gvrm-walking'):
    """hem_check.py on the posed mocap frames: a stand-in for its posing step (ASSET_CPU_REPO/gsplat/pose_splat_preview.py) copies
    the frames, converted to the source splat's frame, into the folders it reads."""
    import shutil, tempfile
    d = dc.char_dir(cid); w = d / '09-game/wip'; r = np.load(w / f'rig/{cid}-{tier}-rig.npz'); L = json.load(open(w / 'rig/landmarks.json'))
    sys.path.insert(0, str(dc.REPO / 'tests/splats'))
    tmp = Path(tempfile.mkdtemp(prefix='mocap-hem-')); (tmp / 'creatures').mkdir()
    for k, f in enumerate(frames):
        v = np.load(mo / f'frames-{source}/f{f:03d}-means.npy')
        np.savez(tmp / f'p{k}.npz', means=((v - r['offset']) / float(r['scale'])) @ r['to_sdk'])
    (tmp / 'gsplat/pose_splat_preview.py').write_text(
        "import sys, os, shutil\nout = sys.argv[2]\nfor a in sys.argv[3:]:\n    if '=' in a and '@' in a:\n        n = a.split('=')[0]; os.makedirs(os.path.join(out, n), exist_ok=True)\n"
        f"        shutil.copy({str(tmp)!r} + '/' + n + '.npz', os.path.join(out, n, 'splat.npz'))\n")
    o = sh(['python3', w / 'rig/hem_check.py', w / f'rig/{cid}-{tier}-rig.npz', w / f'fit{tier}/splat.npz', 'none.glb', 0, 1, len(frames), '--waist', L['skirt']['waist'],
            '--hem', L['skirt']['hem'], '--skeleton', w / 'rig/skeleton-data-441.ts'], env=dict(os.environ, ASSET_CPU_REPO=str(tmp)))
    shutil.rmtree(tmp, ignore_errors=True)
    return [l.strip() for l in o.stdout.splitlines() if l.startswith('  ') and ('per phase' in l or 'torn' in l or 'hands' in l)] or [o.stderr[-400:]]



# Retarget (lead, 2026-11-15): a clip authored on X Bot cannot be played by copying X Bot's joint rotations onto a skeleton whose bind
# frames differ (the Mixamo-rig GLB's are taken from his own joints: neck 26 deg off X Bot's, arms bound at ~37 deg out against
# X Bot's T). Each joint's motion is taken as a change from X Bot's own bind, D = R_anim_world . inverse(R_bind_world) (both X Bot's),
# and applied to our bind: posed = D . R_bind(ours) for trunk, neck, head, legs and feet (same upright stance); for the arm chain
# (shoulder, arm, forearm, hand) to our T-equivalent first: each joint swung from our bind the shortest way until its bone
# direction (+Y) equals X Bot's bind bone direction, no twist added. Our joint offsets (bone lengths) are kept.
XBOT = dc.AP / 'legacy/experiments/animations/mixamo/X Bot.fbx'
ARM = ('Shoulder', 'Arm', 'ForeArm', 'Hand')


def xbot_bind():
    out = dc.REPO / 'local/art/generated/people/dagger-characters/batches/xbot-bind.json'
    if not out.exists():
        out.parent.mkdir(parents=True, exist_ok=True)
        o = sh(['node', HERE / 'fbx_bind_dump.mjs', XBOT, out])
        if o.returncode:
            raise SystemExit(o.stderr[-600:])
    return {k: np.array(v).reshape(4, 4).T for k, v in json.load(open(out)).items()}


def swing(u, v):
    """The shortest rotation taking unit u onto unit v."""
    u = u / np.linalg.norm(u); v = v / np.linalg.norm(v); ax = np.cross(u, v); s = np.linalg.norm(ax); c = float(u @ v)
    if s < 1e-9:
        return np.eye(3)
    return Rot.from_rotvec(ax / s * np.arctan2(s, c)).as_matrix()


class Retarget:
    def __init__(self, glb):
        doc, acc = read(str(glb)); nodes = doc['nodes']; skin = doc['skins'][0]
        self.parent = {c: i for i, nd in enumerate(nodes) for c in nd.get('children', [])}
        self.joints = skin['joints']; self.names = {j: nodes[j]['name'] for j in self.joints}
        self.ibm = acc(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
        self.bind = {j: np.linalg.inv(self.ibm[q]) for q, j in enumerate(self.joints)}
        for j in self.bind:
            self.bind[j][:3, :3] /= np.linalg.norm(self.bind[j][:3, :3], axis=0)
        body = next(i for i, nd in enumerate(nodes) if nd.get('skin') is not None)
        prim = doc['meshes'][nodes[body]['mesh']]['primitives'][0]['attributes']
        self.P = acc(prim['POSITION']); self.J = acc(prim['JOINTS_0']).astype(int); W = acc(prim['WEIGHTS_0']); self.W = W / W.sum(1, keepdims=True)
        self.order = sorted(self.joints, key=lambda j: self.depth(j))
        X = xbot_bind(); self.xb = {}
        self.ref = {}   # our bind rotation, or its T-equivalent for the arm chain
        for j in self.joints:
            n = self.names[j]; xb = X[n][:3, :3] / np.linalg.norm(X[n][:3, :3], axis=0); self.xb[j] = xb; Rb = self.bind[j][:3, :3]
            arm = any(n.split(':')[1] in (s + a for s in ('Left', 'Right')) for a in ARM)
            self.ref[j] = swing(Rb[:, 1], xb[:, 1]) @ Rb if arm else Rb

    def depth(self, j):
        k = 0
        while j in self.parent and self.parent[j] in self.joints:
            j = self.parent[j]; k += 1
        return k

    def pose(self, anim_q, hips_p):
        """anim_q: X Bot's world rotation (xyzw) per joint name this frame; hips_p: the fitted Hips position. -> centres, blends, joints."""
        Rw, pw = {}, {}
        for j in self.order:
            n = self.names[j]; D = Rot.from_quat(anim_q[n]).as_matrix() @ self.xb[j].T; Rw[j] = D @ self.ref[j]
            p = self.parent.get(j)
            if p in self.joints:   # the parent's geometry turns by Rw[p] . Rb[p]^T: carry our bind offset with it
                pw[j] = pw[p] + Rw[p] @ self.bind[p][:3, :3].T @ (self.bind[j][:3, 3] - self.bind[p][:3, 3])
            else:
                pw[j] = np.asarray(hips_p, float)
        M = {}
        for j in self.joints:
            m = np.eye(4); m[:3, :3] = Rw[j]; m[:3, 3] = pw[j]; M[j] = m
        palette = np.array([M[j] @ self.ibm[q] for q, j in enumerate(self.joints)])
        blend = np.einsum('nk,nkij->nij', self.W, palette[self.J])
        return np.einsum('nij,nj->ni', blend[:, :3, :3], self.P) + blend[:, :3, 3], blend[:, :3, :3], {self.names[j]: (Rw[j], pw[j]) for j in self.joints}


def stick_compare(cid, source='patch-walking', frame=None):
    """Known case (3): one held frame, X Bot's own skeleton (sticks, from the FBX) beside ours (sticks of the retargeted joints) and the
    page's capture of the same frame, front and side."""
    import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
    from PIL import Image
    d = dc.char_dir(cid); mo = d / '09-game/wip/mocap'; e = d / f'09-game/evidence/mocap/{source}'
    D = json.load(open(mo / f'{source}.json')); names = [b['name'] for b in D['bones']]
    rec = dc.load(cid)['stages']['09-game']['checks'].get('mocap_' + source.replace('-', '_'), {})
    f = frame if frame is not None else rec.get('phases', {}).get('held', [0, 0, 7])[2]
    Jn = sorted(n for n in names if n in {'mixamorig:' + x for x in ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head') + tuple(
        s + b for s in ('Left', 'Right') for b in ('Shoulder', 'Arm', 'ForeArm', 'Hand', 'UpLeg', 'Leg', 'Foot', 'ToeBase'))})
    ours = dict(zip(Jn, np.load(mo / f'frames-{source}/f{f:03d}-joints.npy')))
    xb = {n: np.array(D['samples'][f][names.index(n)]['world_p']) / 100 for n in Jn}
    bonesl = [(n, D['bones'][D['bones'][names.index(n)]['parent']]['name']) for n in Jn if D['bones'][names.index(n)]['parent'] >= 0]
    fig, ax = plt.subplots(1, 4, figsize=(20, 8), dpi=80)
    for a_, (pts, ttl, i) in zip(ax, ((xb, 'X Bot (the FBX), front', 0), (ours, f'{cid} retargeted, front', 0), (xb, 'X Bot, side', 2), (ours, f'{cid}, side', 2))):
        for c, p in bonesl:
            if c in pts and p in pts:
                a_.plot([pts[c][i], pts[p][i]], [pts[c][1], pts[p][1]], '-o', ms=3, color='tab:red' if 'Left' in c else 'tab:blue' if 'Right' in c else 'k')
        a_.set_aspect('equal'); a_.set_title(ttl); a_.set_ylim(-0.05, 1.9); a_.grid(alpha=.3)
    plt.suptitle(f'Frame {f} of {source}: X Bot against {cid} (sticks; his left red)'); plt.tight_layout(); plt.savefig(e / f'known-sticks-f{f:03d}.png')
    caps = [Image.open(e / f'cycle/{v}-f{f:03d}.png').convert('RGB').crop((560, 0, 1360, 1080)) for v in ('front', 'side') if (e / f'cycle/{v}-f{f:03d}.png').exists()]
    if caps:
        W = Image.new('RGB', (800 * len(caps), 1080)); [W.paste(c, (800 * i, 0)) for i, c in enumerate(caps)]; W.save(e / f'known-page-f{f:03d}.png')
    return e / f'known-sticks-f{f:03d}.png'
