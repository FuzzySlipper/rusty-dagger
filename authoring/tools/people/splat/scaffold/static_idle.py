#!/usr/bin/env python3
"""Procedural idle clips for a static NPC's short rig (static_rig.py), written as clip_compare.py prep poses.

    static_idle.py PREP_DIR SCAFFOLD.glb TRANSFORM.json [--clip idle --clip look] [--fps 30] [--turn 20] [--turn-back 15]

A figure that never walks has no legs to retarget a Mixamo clip onto (retarget_clip.py needs UpLeg..ToeBase), so its clips
are made here, straight onto the chain Hips, Spine, Spine1, Spine2, Neck, Head (head_end, headfront follow Head):

  idle  8 s: two breaths (Spine1/Spine2 pitch 0.8 deg each, the Neck giving half of it back so the head stays level), a slow
        weight shift (the whole figure rolls 0.6 deg about the floor under the hips, so the hem and a staff's foot stay put,
        ), the head drifting 3 deg in yaw and 1 deg in pitch, and one small head tilt at 5.5 s
  look  10 s: the same breathing and sway under a look round - the head turns --turn degrees to his left (Neck 40%, Head 60%),
        holds, turns --turn-back degrees to his right, holds and comes back

LeftHand and RightHand move with Hips, not with the spine: hands clasped on a staff (021) keep the staff head, weighted to
the hands, on the shaft, weighted to Hips. Every motion repeats in the clip's length and the last frame is the first, so the
clips loop. Joint rotations are in the rest frame's axes (the figure faces +z, his left is +x) about each joint's rest place.

PREP_DIR is a clip_compare.py prep folder run with --bound figures on this scaffold (prep with no --clip): each figure gets
poses-TAG-CLIP.npz (skin, joints, viewmats, Ks, size, names) as prep writes them, and prep.json's clips gains the clip, so
render_posed.py, clip_compare.py videos and export_workbench.py take them as they take a retargeted clip.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, str(Path(__file__).parent))
from clip_compare import SIZE, cameras   # noqa: E402
from retarget_clip import load_rig        # noqa: E402

CHAIN = ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head')
LENGTH = {'idle': 8.0, 'look': 10.0}


def keys(t, points):
    """Ease between (time, value) keys with smootherstep, holding the ends."""
    ts, vs = (np.array(x, float) for x in zip(*points))
    i = np.clip(np.searchsorted(ts, t, side='right') - 1, 0, len(ts) - 2)
    u = np.clip((t - ts[i]) / (ts[i + 1] - ts[i]), 0, 1)
    return vs[i] + (vs[i + 1] - vs[i]) * u * u * u * (u * (u * 6 - 15) + 10)


def motion(clip, t, a):
    """Per frame: {joint: (pitch, yaw, roll) degrees}."""
    L = LENGTH[clip]
    w = 2 * np.pi * t / L
    breath = np.sin(2 * w)                                   # two breaths per clip
    sway = np.sin(w)
    rot = {j: np.zeros((len(t), 3)) for j in CHAIN}
    rot['Spine1'][:, 0] = -0.8 * breath
    rot['Spine2'][:, 0] = -0.8 * breath
    rot['Neck'][:, 0] = 0.8 * breath
    rot['Hips'][:, 2] = 0.6 * sway
    rot['Spine'][:, 2] = -0.25 * sway                         # the chest leans back a little over the shifted weight
    rot['Head'][:, 2] = -0.3 * sway
    if clip == 'idle':
        rot['Head'][:, 1] += 3.0 * np.sin(w + 1.0)
        rot['Head'][:, 0] += 1.0 * np.sin(3 * w + 0.4)
        rot['Head'][:, 2] += keys(t, [(0, 0), (5.3, 0), (5.7, 2.5), (6.6, 2.5), (7.3, 0), (L, 0)])
    else:
        yaw = keys(t, [(0, 0), (0.6, 0), (2.2, a.turn), (4.4, a.turn), (6.4, -a.turn_back), (8.4, -a.turn_back), (9.8, 0), (L, 0)])
        rot['Neck'][:, 1] += 0.4 * yaw
        rot['Head'][:, 1] += 0.6 * yaw
        rot['Head'][:, 0] += keys(t, [(0, 0), (2.2, 2.0), (4.4, 2.0), (6.4, 1.0), (8.4, 1.0), (9.8, 0), (L, 0)])   # glances down a touch
    return rot


def about(pivot, R):
    m = np.eye(4)
    m[:3, :3] = R
    m[:3, 3] = pivot - R @ pivot
    return m


def poses(rig, clip, a):
    names, parents, pb = rig['names'], rig['parents'], rig['bind']
    L = LENGTH[clip]
    nf = int(round(L * a.fps)) + 1
    t = np.arange(nf) / a.fps
    rot = motion(clip, t, a)
    idx = {n: i for i, n in enumerate(names)}
    missing = [j for j in CHAIN if j not in idx]
    if missing:
        raise SystemExit(f'static_idle.py: the rig has no {missing} (static_rig.py makes Hips..Head)')
    order = []                                                # parents before children
    seen = set()
    while len(order) < len(names):
        for j in range(len(names)):
            if j not in seen and (parents[j] < 0 or parents[j] in seen):
                order.append(j)
                seen.add(j)
    D = np.tile(np.eye(4), (nf, len(names), 1, 1))
    hips = idx['Hips']
    floor = np.array([pb[hips, 0], rig['ground'], pb[hips, 2]])
    for f in range(nf):
        for j in order:
            n = names[j]
            R = Rot.from_euler('xyz', np.radians(rot[n][f])).as_matrix() if n in rot else np.eye(3)
            if j == hips:
                D[f, j] = about(floor, R)
            elif n in ('LeftHand', 'RightHand'):
                D[f, j] = D[f, hips]
            else:
                D[f, j] = (D[f, parents[j]] if parents[j] >= 0 else np.eye(4)) @ about(pb[j], R)
    joints = np.einsum('fbij,bj->fbi', D[..., :3, :3], pb) + D[..., :3, 3]
    return D, joints


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('prep', type=Path)
    ap.add_argument('scaffold', type=Path)
    ap.add_argument('transform', type=Path)
    ap.add_argument('--clip', action='append', choices=sorted(LENGTH))
    ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--turn', type=float, default=20.0, help='degrees the look turns to his left')
    ap.add_argument('--turn-back', type=float, default=15.0, help='degrees it then turns to his right')
    a = ap.parse_args()
    report = json.loads((a.prep / 'prep.json').read_text())
    scale = report['metres_per_bake_unit'] * json.loads(a.transform.read_text())['scale']
    rig = load_rig(str(a.scaffold), scale)
    hips = rig['bind'][rig['names'].index('Hips')]
    for clip in a.clip or ['idle', 'look']:
        rec = {'source': f'static_idle.py {clip}', 'fps': a.fps}
        for tag, fig in report['figures'].items():
            rig['ground'] = fig['ground']
            skin, joints = poses(rig, clip, a)
            vm, Ks = cameras(hips, fig['ground'], report['views'])
            np.savez_compressed(a.prep / f'poses-{tag}-{clip}.npz', skin=skin.astype(np.float32), viewmats=vm, Ks=Ks, size=SIZE,
                                names=np.array([f'{clip}-f{f:03d}' for f in range(len(skin))]), joints=joints.astype(np.float32))
            rec['frames'] = len(skin)
        report['clips'][clip] = rec
        print(clip, json.dumps(rec))
    (a.prep / 'prep.json').write_text(json.dumps(report, indent=1))


if __name__ == '__main__':
    main()
