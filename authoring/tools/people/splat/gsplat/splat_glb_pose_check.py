#!/usr/bin/env python3
"""Pose two skinned splat GLBs (vsplat_to_glb.py) with their own clips and measure how far apart they land.

  python3 -I splat_glb_pose_check.py A.glb CLIP_A.glb B.glb CLIP_B.glb [--times 0,0.25,...] [--png OUT.png]

Use: the same character written on two rigs (viggle441 and mixamo), each posed
with the same motion exported for that rig (PINOC's splat-skeleton-glb and
mixamo-glb of one library clip). Times are fractions of each clip's length.

Posing is plain glTF skinning by node name: the clip sets each joint's local
rotation, and its translation only where that channel varies (the Engine's
TranslationChannels = VaryingOnly; constant clip translations would replace
the character's bone lengths). A clip whose skeleton hangs under a non-joint
wrapper node (PINOC's mixamo-glb puts the rig under a +90 deg X 'Armature'
for Blender's Z-up) has that wrapper folded into its top joint's channels.
Each Gaussian's centre is moved by the weight-blended joint matrices.
--npz saves both posed clouds per phase (B also as aligned), in each GLB's
own frame with root motion and no hips fit (k = 1, A = 0): the offline
reference an engine's posing of the same files and clips should match.

Each phase also reports both GLBs' unaligned heading (degrees from +Z toward
+X, from the mean ankle-to-toe direction), and the report gives each cloud's
unaligned travel, so a wrong facing is not hidden by the alignment.

The two posed clouds are then rigidly aligned (Kabsch over all Gaussians, so
heading, root offset and pelvis height drop out) and the per-Gaussian
distance is reported overall and by dominant joint of A. This is a check
that a rig animates like the other, not of the Engine's renderer.
"""
import argparse
import json
import struct

import numpy as np
from scipy.spatial.transform import Rotation as Rot


def read(path):
    data = open(path, 'rb').read()
    n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n]); binary = data[20 + n + 8:]

    def accessor(i):
        a = doc['accessors'][i]; v = doc['bufferViews'][a['bufferView']]
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        dtype = {5126: np.float32, 5121: np.uint8, 5123: np.uint16, 5122: np.int16}[a['componentType']]
        stride = v.get('byteStride') or np.dtype(dtype).itemsize * width
        start = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        rows = np.lib.stride_tricks.as_strided(
            np.frombuffer(binary, np.uint8, offset=start, count=stride * (a['count'] - 1) + np.dtype(dtype).itemsize * width),
            (a['count'], np.dtype(dtype).itemsize * width), (stride, 1))
        x = np.ascontiguousarray(rows).view(dtype).reshape(a['count'], width).astype(np.float64)
        if a.get('normalized'):
            x = x / np.iinfo(dtype).max if dtype != np.float32 else x
        return x
    return doc, accessor


def trs(n):
    m = np.eye(4)
    m[:3, :3] = Rot.from_quat(n.get('rotation', [0, 0, 0, 1])).as_matrix() * np.array(n.get('scale', [1, 1, 1]))
    m[:3, 3] = n.get('translation', [0, 0, 0])
    return m


def load_clip(path):
    doc, accessor = read(path)
    nodes = doc['nodes']
    parent = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    joints = set(doc['skins'][0]['joints']) if doc.get('skins') else {i for i, n in enumerate(nodes)}
    anim = doc['animations'][0]; tracks = {}
    for ch in anim['channels']:
        s = anim['samplers'][ch['sampler']]
        tracks[(nodes[ch['target']['node']]['name'], ch['target']['path'])] = (accessor(s['input'])[:, 0], accessor(s['output']))
    # A wrapper: the world matrix of the non-joint ancestors above each top joint.
    wrapper = {}
    for j in joints:
        if parent.get(j) not in joints:
            m = np.eye(4); p = parent.get(j)
            while p is not None:
                m = trs(nodes[p]) @ m; p = parent.get(p)
            wrapper[nodes[j]['name']] = m
    length = max(t[0][-1] for t in tracks.values())
    return tracks, wrapper, length


def sample(track, t):
    times, values = track
    if len(times) == 1:
        return values[0]
    i = int(np.clip(np.searchsorted(times, t) - 1, 0, len(times) - 2))
    f = float(np.clip((t - times[i]) / (times[i + 1] - times[i]), 0, 1))
    if values.shape[1] == 4:
        a, b = values[i], values[i + 1] * (1 if values[i] @ values[i + 1] >= 0 else -1)
        q = (1 - f) * a + f * b
        return q / np.linalg.norm(q)
    return (1 - f) * values[i] + f * values[i + 1]


def varies(track):
    return len(track[0]) > 1 and np.ptp(track[1], axis=0).max() > 1e-6


def pose(glb, clip, t):
    doc, accessor = read(glb)
    nodes = doc['nodes']; skin = doc['skins'][0]
    parent = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    tracks, wrapper, _ = clip
    local = {}
    for i, n in enumerate(nodes):
        m = trs(n); name = n.get('name')
        if i in skin['joints']:
            r = tracks.get((name, 'rotation')); tr = tracks.get((name, 'translation'))
            rot = sample(r, t) if r else n.get('rotation', [0, 0, 0, 1])
            pos = sample(tr, t) if tr is not None and varies(tr) else n.get('translation', [0, 0, 0])
            m = trs({'rotation': rot, 'translation': pos})
            if name in wrapper and not np.allclose(wrapper[name], np.eye(4)):
                m = wrapper[name] @ m
                if tr is None or not varies(tr):
                    m[:3, 3] = n.get('translation', [0, 0, 0])
        local[i] = m
    world = {}

    def w(i):
        if i not in world:
            world[i] = local[i] if i not in parent else w(parent[i]) @ local[i]
        return world[i]
    ibm = accessor(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    palette = np.array([w(j) @ ibm[k] for k, j in enumerate(skin['joints'])])
    body = next(i for i, n in enumerate(nodes) if n.get('skin') is not None)
    prim = doc['meshes'][nodes[body]['mesh']]['primitives'][0]['attributes']
    P = accessor(prim['POSITION']); J = accessor(prim['JOINTS_0']).astype(int); W = accessor(prim['WEIGHTS_0'])
    W = W / W.sum(1, keepdims=True)
    blend = np.einsum('nk,nkij->nij', W, palette[J])
    names = [nodes[j]['name'] for j in skin['joints']]
    pose.joints = {nodes[j]['name']: w(j) for j in skin['joints']}
    return np.einsum('nij,nj->ni', blend[:, :3, :3], P) + blend[:, :3, 3], np.array(names)[J[:, 0]]


FEET = {'viggle441': (('foot_l', 'ball_l'), ('foot_r', 'ball_r')),
        'mixamo': (('mixamorig:LeftFoot', 'mixamorig:LeftToeBase'), ('mixamorig:RightFoot', 'mixamorig:RightToeBase'))}


def heading(joints):
    """Unaligned facing in degrees from +Z toward +X: the mean ankle-to-toe direction of both feet, in XZ."""
    rig = 'mixamo' if 'mixamorig:LeftFoot' in joints else 'viggle441'
    d = sum(joints[toe][:3, 3] - joints[ankle][:3, 3] for ankle, toe in FEET[rig])
    return float(np.degrees(np.arctan2(d[0], d[2])))


def kabsch(a, b):
    ca, cb = a.mean(0), b.mean(0)
    u, _, vt = np.linalg.svd((b - cb).T @ (a - ca))
    d = np.sign(np.linalg.det(u @ vt))
    r = u @ np.diag([1, 1, d]) @ vt
    return (b - cb) @ r + ca


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('a'); ap.add_argument('clip_a'); ap.add_argument('b'); ap.add_argument('clip_b')
    ap.add_argument('--times', default='0,0.125,0.25,0.375,0.5,0.625,0.75,0.875')
    ap.add_argument('--png')
    ap.add_argument('--npz', help='save the posed centres (A and B, B unaligned and aligned) per phase, as a reference to match')
    a = ap.parse_args()
    ca, cb = load_clip(a.clip_a), load_clip(a.clip_b)
    report = {'clip_a_seconds': ca[2], 'clip_b_seconds': cb[2], 'phases': []}
    centre = {}
    shots = []
    for f in map(float, a.times.split(',')):
        pa, region = pose(a.a, ca, f * ca[2]); heading_a = heading(pose.joints)
        pb_raw, _ = pose(a.b, cb, f * cb[2]); heading_b = heading(pose.joints)
        centre.setdefault('a', []).append(pa.mean(0)); centre.setdefault('b', []).append(pb_raw.mean(0))
        pb = kabsch(pa, pb_raw)
        d = np.linalg.norm(pa - pb, axis=1)
        parts = {r: round(float(np.median(d[region == r])) * 100, 2) for r in np.unique(region)}
        report['phases'].append({'phase': f, 'heading_a_deg': round(heading_a, 1), 'heading_b_deg': round(heading_b, 1),
                                 'median_cm': round(float(np.median(d)) * 100, 2),
                                 'p95_cm': round(float(np.percentile(d, 95)) * 100, 2), 'median_cm_by_joint': parts})
        shots.append((f, pa, pb, pb_raw))
    for k in 'ab':   # unaligned travel of the cloud's centre over the sampled phases, in each GLB's own frame
        c = np.array(centre[k]); report[f'travel_{k}_xz_m'] = [round(float(x), 3) for x in (c[-1] - c[0])[[0, 2]]]
    print(json.dumps(report, indent=1))
    if a.npz:
        np.savez_compressed(a.npz, phases=np.array([f for f, *_ in shots]),
                            seconds_a=np.array([f * ca[2] for f, *_ in shots]), seconds_b=np.array([f * cb[2] for f, *_ in shots]),
                            a=np.array([x[1] for x in shots], np.float32), b=np.array([x[3] for x in shots], np.float32),
                            b_aligned_to_a=np.array([x[2] for x in shots], np.float32))
    if a.png:
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
        fig, axes = plt.subplots(2, len(shots), figsize=(2.4 * len(shots), 9), dpi=120)
        for k, (f, pa, pb, _) in enumerate(shots):
            # The posed clouds share A's frame: X against Y, then Z against Y.
            for row, (u, label) in enumerate(((0, 'x-y'), (2, 'z-y'))):
                ax = axes[row, k]
                ax.scatter(pa[::3, u], pa[::3, 1], s=0.2, c='tab:blue', alpha=0.4, linewidths=0)
                ax.scatter(pb[::3, u], pb[::3, 1], s=0.2, c='tab:red', alpha=0.4, linewidths=0)
                ax.set_aspect('equal'); ax.axis('off'); ax.set_title(f'{f:.3f} {label}', fontsize=8)
        fig.suptitle('blue: A posed by clip A   red: B posed by clip B, rigidly aligned to A', fontsize=9)
        fig.savefig(a.png, bbox_inches='tight')


if __name__ == '__main__':
    main()
