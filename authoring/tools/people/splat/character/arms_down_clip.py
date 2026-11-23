#!/usr/bin/env python3
"""arms_down_clip.py IN.glb OUT.glb RIG.npz [--target DEG] [--skeleton TS]: a copy of a splat-skeleton (441-bone) clip with the
upper arms brought in to the sides: at every upper-arm keyframe the arm is turned about the body's forward axis (as the page's
shoulder abduction turns it, layers.js) until its sideways angle from vertical is --target degrees (default 4, out from the body);
the forward swing, the forearms, hands and every other channel are left as authored. Same keyframes, same file size: only the
upper arms' rotation values are rewritten.

Why (lead for Patch, 2026-11-15): PINOC's Walking holds the upper arms about 17-19 deg out through half the cycle and the page
adds 10 deg more, so the captures never show the shoulders coming down out of the A-pose; the PINOC library has no other plain
forward walk. The joint translations come from RIG.npz (the angle barely depends on the character)."""
import argparse, json, os, struct, sys
import numpy as np
from scipy.spatial.transform import Rotation as Rot
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '../gsplat'))
from humanoid_splat_rig import load_skeleton          # noqa: E402
from pose_splat_preview import load_clip, sample, fk  # noqa: E402


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('inp'); ap.add_argument('out'); ap.add_argument('rig')
    ap.add_argument('--target', type=float, default=4.0); ap.add_argument('--skeleton')
    a = ap.parse_args()
    names, parents, _, can_r = load_skeleton(a.skeleton) if a.skeleton else load_skeleton()
    T = np.load(a.rig)['joints']; B = fk(parents, can_r, T); clip = load_clip(a.inp)
    data = bytearray(open(a.inp, 'rb').read()); n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n]); bin0 = 20 + n + 8
    anim = doc['animations'][0]; fwd0, lat0 = np.array([1.0, 0, 0]), np.array([0, 0, -1.0]); sgn = {'l': 1.0, 'r': -1.0}

    def pose(t, override):
        rots = can_r.copy(); trans = T.copy()
        for i, bn in enumerate(names):
            if (bn, 'rotation') in clip:
                rots[i] = sample(clip[(bn, 'rotation')], t)
            tr = clip.get((bn, 'translation'))
            if i > 0 and tr is not None and np.ptp(tr[1], 0).max() > 1e-5:
                trans[i] = sample(tr, t)
                if bn == 'pelvis':
                    trans[i][[0, 2]] = T[i][[0, 2]]
        for i, q in override.items():
            rots[i] = q
        return rots, trans

    def side(W, s):
        turn = W[1][:3, :3] @ B[1][:3, :3].T; lat = sgn[s] * (turn @ lat0)
        v = W[names.index('lowerarm_' + s)][:3, 3] - W[names.index('upperarm_' + s)][:3, 3]; v /= np.linalg.norm(v)
        return np.degrees(np.arctan2(v @ lat, -v[1])), turn @ fwd0

    report = {}
    for s in 'lr':
        bi = names.index('upperarm_' + s)
        ch = next(c for c in anim['channels'] if doc['nodes'][c['target']['node']]['name'] == 'upperarm_' + s and c['target']['path'] == 'rotation')
        times, vals = clip[('upperarm_' + s, 'rotation')]; new = vals.copy(); before = []; after = []
        for k, t in enumerate(times):
            q = vals[k] / np.linalg.norm(vals[k])
            for it in range(4):
                rots, trans = pose(float(t), {bi: q}); W = fk(parents, rots, trans); th, fwd = side(W, s)
                if it == 0:
                    before.append(th)
                d = a.target - th
                if abs(d) < 0.05:
                    break
                P = W[parents[bi]][:3, :3]
                local = P.T @ Rot.from_rotvec(fwd * np.radians(sgn[s] * d)).as_matrix() @ P
                q = (Rot.from_matrix(local) * Rot.from_quat(q)).as_quat()
            rots, trans = pose(float(t), {bi: q}); after.append(side(fk(parents, rots, trans), s)[0]); new[k] = q
        acc = doc['accessors'][anim['samplers'][ch['sampler']]['output']]; view = doc['bufferViews'][acc['bufferView']]
        off = bin0 + view.get('byteOffset', 0) + acc.get('byteOffset', 0)
        if acc['componentType'] == 5126:
            data[off:off + new.size * 4] = new.astype('<f4').tobytes()
        elif acc['componentType'] == 5122 and acc.get('normalized'):
            data[off:off + new.size * 2] = np.round(np.clip(new, -1, 1) * 32767).astype('<i2').tobytes()
        else:
            raise SystemExit(f'upperarm_{s}: unsupported component type {acc["componentType"]}')
        report[s] = {'keys': len(times), 'sideways_before': [round(float(min(before)), 1), round(float(max(before)), 1)],
                     'sideways_after': [round(float(min(after)), 1), round(float(max(after)), 1)]}
    open(a.out, 'wb').write(bytes(data))
    print(json.dumps({'out': a.out, 'target_deg': a.target, 'arms': report}))


if __name__ == '__main__':
    main()
