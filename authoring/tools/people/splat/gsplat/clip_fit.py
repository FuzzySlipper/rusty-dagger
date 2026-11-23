#!/usr/bin/env python3
"""Fit PINOC clips' pelvis translation to a rigged character's legs, and measure foot sliding, offline.

  python3 clip_fit.py OUT-rig.npz OUT.json NAME=CLIP.glb [NAME=CLIP.glb ...] [--skeleton ...] [--contact 0.01] [--leg-ratio]

The clips' pelvis translation is absolute and authored for the canonical
skeleton's legs. A character with other legs (humanoid_splat_rig.py --height)
then floats or sinks, and with root motion on its planted feet slide, because
the body travels the canonical stride while its own legs cover a different
one. This scales the clip's pelvis translation, X, Y and Z alike, by the k
in 0.6-1.2 that leaves the least planted-foot drift (median over stances), or
with --leg-ratio by k = (his thigh + shin) / (the canonical thigh + shin),
which is reported either way. For mobile-30 the fitted k (0.84 on Walking) is
lower than the leg ratio (0.91): the stride also depends on the hip width,
foot length and how bent the stance leg is, not just leg length. It adds a height A so
that the lowest foot stands at its rest height through the cycle (median over
frames). The page applies pelvis = (k x, A + k y, k z) in a pose modifier,
and root motion carries the scaled X/Z.

Sliding: with that pelvis and root motion on (the pelvis translation applied
in world space, as the engine's 'apply' mode does), a foot is planted while
its ball joint is within --contact metres of its rest height. For each planted
stretch it reports the ball's horizontal drift from where it landed (end and
largest, in cm) and the same for the clip on the canonical skeleton at k = 1
(the clip's own sliding). OUT.json holds k, A and the numbers per clip.
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from humanoid_splat_rig import DEFAULT_SKELETON, load_skeleton  # noqa: E402
from pose_splat_preview import fk, load_clip, sample  # noqa: E402


def frames(clip):
    times = clip[('pelvis', 'translation')][0]
    return np.arange(0, times[-1] + 1e-6, 1 / 30)


def pose_world(names, parents, can_r, T, clip, t, k, A):
    rots = can_r.copy(); trans = T.copy()
    for i, bn in enumerate(names):
        if (bn, 'rotation') in clip:
            rots[i] = sample(clip[(bn, 'rotation')], t)
    p = sample(clip[('pelvis', 'translation')], t)
    trans[names.index('pelvis')] = [k * p[0], A + k * p[1], k * p[2]]
    return fk(parents, rots, trans)


def stances(height, contact):
    on = height < contact; out = []; i = 0
    while i < len(on):
        if on[i]:
            j = i
            while j + 1 < len(on) and on[j + 1]:
                j += 1
            if j - i >= 2:
                out.append((i, j))
            i = j + 1
        else:
            i += 1
    return out


def measure(names, parents, can_r, T, clip, k, A, rest_h, contact):
    bl = {s: names.index('ball_' + s) for s in 'lr'}; ft = {s: names.index('foot_' + s) for s in 'lr'}
    ts = frames(clip)
    P = {s: [] for s in 'lr'}; F = {s: [] for s in 'lr'}; pel = []
    for t in ts:
        W = pose_world(names, parents, can_r, T, clip, float(t), k, A)
        for s in 'lr':
            P[s].append(W[bl[s]][:3, 3]); F[s].append(W[ft[s]][:3, 3])
        pel.append(W[names.index('pelvis')][:3, 3])
    P = {s: np.array(v) for s, v in P.items()}; F = {s: np.array(v) for s, v in F.items()}; pel = np.array(pel)
    out = {'seconds': float(ts[-1]), 'frames': len(ts)}
    travel = pel[-1, [0, 2]] - pel[0, [0, 2]]
    out['speed_m_s'] = float(np.linalg.norm(travel) / ts[-1])
    out['pelvis_height_m'] = [float(pel[:, 1].min()), float(pel[:, 1].max())]
    phases = []
    for s in 'lr':
        h = P[s][:, 1] - rest_h['ball_' + s]
        hf = F[s][:, 1] - rest_h['foot_' + s]
        out[f'ball_{s}_lowest_m'] = float(h.min()); out[f'foot_{s}_lowest_m'] = float(hf.min())
        for i, j in stances(h, contact):
            xz = P[s][i:j + 1][:, [0, 2]] - P[s][i][[0, 2]]
            d = np.linalg.norm(xz, axis=1)
            phases.append({'foot': s, 'from_s': round(float(ts[i]), 2), 'to_s': round(float(ts[j]), 2),
                           'drift_end_cm': round(100 * float(d[-1]), 1), 'drift_max_cm': round(100 * float(d.max()), 1),
                           'sink_cm': round(100 * float(min(0, h[i:j + 1].min())), 1)})
    # Whole cycles only: drop stances touching the clip's ends.
    inner = [p for p in phases if p['from_s'] > 0.05 and p['to_s'] < ts[-1] - 0.05]
    out['stances'] = phases
    if inner:
        out['drift_end_cm_median'] = float(np.median([p['drift_end_cm'] for p in inner]))
        out['drift_end_cm_max'] = float(max(p['drift_end_cm'] for p in inner))
        out['drift_max_cm_median'] = float(np.median([p['drift_max_cm'] for p in inner]))
        out['stance_s_median'] = float(np.median([p['to_s'] - p['from_s'] for p in inner]))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('rig'); ap.add_argument('out'); ap.add_argument('clips', nargs='+')
    ap.add_argument('--skeleton', default=DEFAULT_SKELETON); ap.add_argument('--contact', type=float, default=0.01)
    ap.add_argument('--leg-ratio', action='store_true', help='scale by the leg-length ratio instead of fitting k')
    a = ap.parse_args()
    names, parents, can_t, can_r = load_skeleton(a.skeleton)
    T = np.load(a.rig)['joints']
    idx = {n: i for i, n in enumerate(names)}
    leg = lambda tr: np.mean([np.linalg.norm(tr[idx['calf_' + s]]) + np.linalg.norm(tr[idx['foot_' + s]]) for s in 'lr'])  # noqa: E731
    k_leg = float(leg(T) / leg(can_t))
    rest = fk(parents, can_r, T); rest_c = fk(parents, can_r, can_t)
    rest_h = {f'{b}_{s}': float(rest[idx[f'{b}_{s}']][1, 3]) for b in ('ball', 'foot') for s in 'lr'}
    rest_hc = {f'{b}_{s}': float(rest_c[idx[f'{b}_{s}']][1, 3]) for b in ('ball', 'foot') for s in 'lr'}
    report = {'rig': a.rig, 'k_leg_ratio': k_leg, 'his_leg_m': float(leg(T)), 'canonical_leg_m': float(leg(can_t)), 'contact_m': a.contact, 'clips': {}}
    for spec in a.clips:
        name, path = spec.split('=', 1)
        clip = load_clip(path)

        def ground(T_, k_, rest_h_, clip=clip):
            lows = []
            for t in frames(clip)[::2]:
                W = pose_world(names, parents, can_r, T_, clip, float(t), k_, 0.0)
                lows.append(min(W[idx['ball_' + s]][1, 3] - rest_h_['ball_' + s] for s in 'lr'))
            return -float(np.median(lows))
        # The pelvis rest height is in the rig's own pelvis translation; A replaces it.
        k = k_leg
        if not a.leg_ratio:
            from scipy.optimize import minimize_scalar

            def cost(kk):
                m = measure(names, parents, can_r, T, clip, kk, ground(T, kk, rest_h), rest_h, a.contact)
                return m.get('drift_end_cm_median', 1e3) + 0.25 * m.get('drift_end_cm_max', 1e3)
            k = float(minimize_scalar(cost, bounds=(0.6, 1.2), method='bounded', options={'xatol': 0.005}).x)
        A = ground(T, k, rest_h)
        A_c = ground(can_t, 1.0, rest_hc)
        his = measure(names, parents, can_r, T, clip, k, A, rest_h, a.contact)
        unscaled = measure(names, parents, can_r, T, clip, 1.0, ground(T, 1.0, rest_h), rest_h, a.contact)
        legratio = measure(names, parents, can_r, T, clip, k_leg, ground(T, k_leg, rest_h), rest_h, a.contact)
        own = measure(names, parents, can_r, can_t, clip, 1.0, A_c, rest_hc, a.contact)
        report['clips'][name] = {'path': path, 'k': k, 'A': A, 'his': his, 'his_leg_ratio': legratio, 'his_unscaled_root': unscaled, 'canonical': own}
        short = lambda m: {x: m.get(x) for x in ('speed_m_s', 'drift_end_cm_median', 'drift_end_cm_max', 'drift_max_cm_median', 'stance_s_median')}  # noqa: E731
        print(name, json.dumps({'k': round(k, 4), 'A': round(A, 4), 'his': short(his), 'leg_ratio': short(legratio), 'unscaled': short(unscaled), 'canonical': short(own)}))
    json.dump(report, open(a.out, 'w'), indent=1)
    print('leg ratio', round(k_leg, 4), '->', a.out)


if __name__ == '__main__':
    main()
