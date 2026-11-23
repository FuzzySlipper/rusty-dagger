#!/usr/bin/env python3
"""Pose a humanoid_splat_rig.py character with PINOC clip frames, offline, for gsplat review renders.

  python3 pose_splat_preview.py OUT-rig.npz OUT_DIR NAME=CLIP.glb@SECONDS [NAME=CLIP.glb@SECONDS ...]
      [--skeleton .../skeleton-data-441.ts] [--rest NAME] [--abduct DEG] [--forearm DEG]

Each NAME becomes OUT_DIR/NAME/splat.npz in the source splat's own frame (Y up,
front +Z, height about 1, centred as the input was), so loop_review.py can
render the poses from the same cameras as the static budgets. --rest NAME adds
the re-posed rest (canonical A-pose) as one more row.

Posing follows the SDK's rules as read from its source: canonical rest
rotations, the character's own joint translations, clip rotations for every
bone, clip translations only where a channel varies (pelvis, face), the root
pinned to its bind translation but keeping the clip's yaw (the PINOC clips
carry a constant quarter or half turn there, which is how they face), and the
pelvis held at its rest X/Z (root motion off). The pose's heading (the
pelvis's yaw against its rest) is then undone, so every pose faces the way
the rest does and fixed review cameras compare
like with like. Skinning is linear blend
skinning of each Gaussian's four bones, with its rotation turned by the
blended bone rotation. This is a check of the rig and weights, not of the
SDK's renderer: the browser captures remain the real result.
--abduct DEG turns both upper arms DEG further out about the pelvis's forward
axis, as the page's per-character shoulder abduction (layers.js) does, and
--forearm DEG the forearms a further DEG out at the elbow.
"""
import argparse
import json
import os
import struct
import sys

import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from humanoid_splat_rig import DEFAULT_SKELETON, load_skeleton  # noqa: E402


def load_clip(path):
    data = open(path, 'rb').read()
    n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n]); binary = data[20 + n + 8:]

    def accessor(i):
        acc = doc['accessors'][i]; view = doc['bufferViews'][acc['bufferView']]
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[acc['type']]
        dtype = {5126: np.float32, 5122: np.int16, 5123: np.uint16}[acc['componentType']]
        x = np.frombuffer(binary, dtype, count=acc['count'] * width,
                          offset=view.get('byteOffset', 0) + acc.get('byteOffset', 0)).reshape(-1, width).astype(np.float64)
        return np.maximum(x / 32767, -1) if acc.get('normalized') else x
    anim = doc['animations'][0]; out = {}
    for ch in anim['channels']:
        sampler = anim['samplers'][ch['sampler']]
        out[(doc['nodes'][ch['target']['node']]['name'], ch['target']['path'])] = (accessor(sampler['input'])[:, 0], accessor(sampler['output']))
    return out


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


def fk(parents, rots, trans):
    n = len(parents); W = np.zeros((n, 4, 4))
    for i in range(n):
        L = np.eye(4); L[:3, :3] = Rot.from_quat(rots[i]).as_matrix(); L[:3, 3] = trans[i]
        W[i] = L if parents[i] < 0 else W[parents[i]] @ L
    return W


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('rig'); ap.add_argument('out'); ap.add_argument('poses', nargs='+')
    ap.add_argument('--skeleton', default=DEFAULT_SKELETON); ap.add_argument('--rest')
    ap.add_argument('--abduct', type=float, default=0, help='upper arms this many degrees further out (as layers.js)')
    ap.add_argument('--forearm', type=float, default=0, help='forearms this many degrees further out at the elbow')
    a = ap.parse_args()
    names, parents, _, can_r = load_skeleton(a.skeleton)
    r = np.load(a.rig)
    T = r['joints']; bones = r['bones']; w = r['weights']
    B = fk(parents, can_r, T); Binv = np.linalg.inv(B)
    scale, offset, to_sdk = float(r['scale']), r['offset'], r['to_sdk']
    to_her = Rot.from_matrix(to_sdk.T)

    def write(name, M):
        means = np.einsum('nij,nj->ni', M[:, :3, :3], r['rest_means']) + M[:, :3, 3]
        U, _, Vt = np.linalg.svd(M[:, :3, :3])
        q = (to_her * Rot.from_matrix(U @ Vt) * Rot.from_quat(r['quats'][:, [1, 2, 3, 0]])).as_quat()[:, [3, 0, 1, 2]]
        os.makedirs(os.path.join(a.out, name), exist_ok=True)
        np.savez(os.path.join(a.out, name, 'splat.npz'), means=((means - offset) / scale) @ to_sdk, scales=r['scales'] - np.log(scale),
                 quats=q, opacities=r['opacities'], rgb=r['rgb'])
        print('wrote', name)

    if a.rest:
        write(a.rest, np.tile(np.eye(4), (len(bones), 1, 1)))
    clips = {}
    for spec in a.poses:
        name, rest = spec.split('=', 1); path, t = rest.rsplit('@', 1)
        clip = clips.setdefault(path, load_clip(path))
        rots = can_r.copy(); trans = T.copy()
        for i, bn in enumerate(names):
            if (bn, 'rotation') in clip:                    # the root keeps the clip's yaw (how it faces)
                rots[i] = sample(clip[(bn, 'rotation')], float(t))
            tr = clip.get((bn, 'translation'))
            if i > 0 and tr is not None and np.ptp(tr[1], 0).max() > 1e-5:   # root pinned to bind
                trans[i] = sample(tr, float(t))
                if bn == 'pelvis':
                    trans[i][[0, 2]] = T[i][[0, 2]]           # root motion off: pelvis X/Z held at bind
        for deg, side_bones in ((a.abduct, ('upperarm_l', 'upperarm_r')), (a.forearm, ('lowerarm_l', 'lowerarm_r'))):
            if not deg:
                continue
            W0 = fk(parents, rots, trans); pel = names.index('pelvis')
            fwd = W0[pel][:3, :3] @ B[pel][:3, :3].T @ np.array([1.0, 0.0, 0.0]); fwd /= np.linalg.norm(fwd)
            for bn, sign in zip(side_bones, (1, -1)):
                i = names.index(bn); P = W0[parents[i]][:3, :3]
                local = P.T @ Rot.from_rotvec(fwd * np.radians(sign * deg)).as_matrix() @ P
                rots[i] = (Rot.from_matrix(local) * Rot.from_quat(rots[i])).as_quat()
        Wp = fk(parents, rots, trans)
        # Undo the pose's overall heading (the pelvis's yaw against its rest) so it faces the rest's way.
        turn = Wp[1][:3, :3] @ B[1][:3, :3].T
        heading = turn @ np.array([1.0, 0.0, 0.0])
        yaw = np.arctan2(-heading[2], heading[0])
        unturn = np.eye(4); unturn[:3, :3] = Rot.from_euler('y', -yaw).as_matrix()
        S = unturn @ Wp @ Binv
        write(name, np.einsum('nk,nkij->nij', w, S[bones]))


if __name__ == '__main__':
    main()
