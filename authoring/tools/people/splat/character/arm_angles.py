#!/usr/bin/env python3
"""arm_angles.py RIG.npz LANDMARKS.json CLIP.glb T0 PERIOD [--n 32] [--abduct DEG --forearm DEG] [--skeleton TS] [--json OUT]

How far each upper arm hangs from vertical (shoulder joint to elbow joint), in degrees, split into its sideways part (out from
the body, in the body's frontal plane: what an A-pose is) and its forward part (the walk's swing):
  bake   the painted pose, from the landmarks (her frame: x his left, y up, z front)
  rest   the rig's re-posed rest (the canonical A-pose the clips are authored against)
  clip   over one cycle of CLIP (N phases from T0), posed as pose_splat_preview.py poses it (the same bones, the page's
         --abduct / --forearm offset when given), heading undone; min, max and the value at each phase
For a dagger character's evidence (lead, 2026-11-15: arms at the sides, to judge the shoulders coming down out of the A-pose)."""
import argparse, json, os, sys
import numpy as np
from scipy.spatial.transform import Rotation as Rot
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '../gsplat'))
from humanoid_splat_rig import load_skeleton          # noqa: E402
from pose_splat_preview import load_clip, sample, fk  # noqa: E402


def split(v, fwd, lat):
    """Degrees from straight down: total, sideways (+ out from the body, - across it; lat points out on this side), forward (+ in front)."""
    down = np.array([0.0, -1.0, 0.0]); v = v / np.linalg.norm(v)
    tot = np.degrees(np.arccos(np.clip(v @ down, -1, 1)))
    side = np.degrees(np.arctan2(v @ lat, -v[1])); fw = np.degrees(np.arctan2(v @ fwd, -v[1]))
    return round(float(tot), 1), round(float(side), 1), round(float(fw), 1)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('rig'); ap.add_argument('landmarks'); ap.add_argument('clip')
    ap.add_argument('t0', type=float); ap.add_argument('period', type=float); ap.add_argument('--n', type=int, default=32)
    ap.add_argument('--abduct', type=float, default=0); ap.add_argument('--forearm', type=float, default=0)
    ap.add_argument('--skeleton', default=None); ap.add_argument('--json')
    a = ap.parse_args()
    names, parents, _, can_r = load_skeleton(a.skeleton) if a.skeleton else load_skeleton()
    r = np.load(a.rig); T = r['joints']; L = json.load(open(a.landmarks))['joints']
    out = {'bake': {}, 'rest': {}, 'clip': {}}
    for s in 'lr':   # the bake, her frame: front +z, his left +x
        out['bake'][s] = split(np.array(L['lowerarm_' + s]) - np.array(L['upperarm_' + s]), np.array([0, 0, 1.0]), np.array([1.0 if s == 'l' else -1.0, 0, 0]))
    # SDK frame (to_sdk: sdk x = her z, sdk z = -her x): front +x, his left -z
    fwd0, lat0 = np.array([1.0, 0, 0]), np.array([0, 0, -1.0])
    B = fk(parents, can_r, T); clip = load_clip(a.clip); per = {s: [] for s in 'lr'}
    sgn = {'l': 1.0, 'r': -1.0}
    for s in 'lr':   # the joints are local translations: world positions from the rest's forward kinematics
        out['rest'][s] = split(B[names.index('lowerarm_' + s)][:3, 3] - B[names.index('upperarm_' + s)][:3, 3], fwd0, sgn[s] * lat0)
    for k in range(a.n):
        t = a.t0 + k * a.period / a.n
        rots = can_r.copy(); trans = T.copy()
        for i, bn in enumerate(names):
            if (bn, 'rotation') in clip:
                rots[i] = sample(clip[(bn, 'rotation')], t)
            tr = clip.get((bn, 'translation'))
            if i > 0 and tr is not None and np.ptp(tr[1], 0).max() > 1e-5:
                trans[i] = sample(tr, t)
                if bn == 'pelvis':
                    trans[i][[0, 2]] = T[i][[0, 2]]
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
        turn = Wp[1][:3, :3] @ B[1][:3, :3].T          # the pelvis's turn against the rest: the body's own frame
        fwd, lat = turn @ fwd0, turn @ lat0
        for s in 'lr':
            per[s].append(split(Wp[names.index('lowerarm_' + s)][:3, 3] - Wp[names.index('upperarm_' + s)][:3, 3], fwd, sgn[s] * lat))
    for s in 'lr':
        v = np.array(per[s])
        out['clip'][s] = {'sideways_min': float(v[:, 1].min()), 'sideways_max': float(v[:, 1].max()), 'forward_min': float(v[:, 2].min()),
                          'forward_max': float(v[:, 2].max()), 'total_min': float(v[:, 0].min()), 'total_max': float(v[:, 0].max()), 'per_phase': per[s]}
    out['offset'] = {'abduct': a.abduct, 'forearm': a.forearm}; out['clip_file'] = os.path.basename(a.clip)
    if a.json:
        json.dump(out, open(a.json, 'w'), indent=1)
    print(json.dumps({k: v for k, v in out.items() if k != 'clip'}),
          json.dumps({s: {k: v for k, v in out['clip'][s].items() if k != 'per_phase'} for s in 'lr'}))


if __name__ == '__main__':
    main()
