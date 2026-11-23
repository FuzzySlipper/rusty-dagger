#!/usr/bin/env python3
"""A gentler copy of a clip: every joint's swing pulled toward its average pose over the clip.

    gentle_clip.py CLIP.json OUT.json [--amount 0.75]

CLIP.json is a clip as fbx_clip_dump.mjs writes it (bones with rest_t/rest_q, samples per frame with each bone's local and world
rotation and world position). Each bone's local rotation is slerped from its clip-average rotation toward the frame's by --amount
(1: unchanged, 0: frozen in the average pose), and the hips' height bob is scaled the same way; world rotations and positions are
then rebuilt down the hierarchy. The posture stays, the stride and arm swing shrink, and a looping clip still loops.

Why (Patch 2026-11-21): on skirt bones a long skirt "could work fine" with "a slightly less vigorous walk": the Mixamo walk's
full stride pushes the cloth sheet hardest. Long-skirted characters play this on the bench (bound.py playable,
local/art/generated/people/dagger-characters/batches/clips-skirt/). The Engine plays its own clips; there the same amount would apply to its walk.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot


def mean_rotation(qs):
    return Rot.from_quat(qs).mean()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('clip', type=Path); ap.add_argument('out', type=Path); ap.add_argument('--amount', type=float, default=0.75)
    a = ap.parse_args()
    c = json.loads(a.clip.read_text()); S = c['samples']; nb = len(c['bones']); nf = len(S)
    par = [b['parent'] for b in c['bones']]
    rest_t = np.array([b['rest_t'] for b in c['bones']], float)
    L = np.array([[s[j]['local'] for j in range(nb)] for s in S], float)          # frames x bones x xyzw
    out = []
    root_p = np.array([S[f][j]['world_p'] for f in range(nf) for j in range(nb) if par[j] < 0], float).reshape(nf, -1, 3)
    roots = [j for j in range(nb) if par[j] < 0]
    newL = np.zeros_like(L)
    for j in range(nb):
        m = mean_rotation(L[:, j])
        d = (m.inv() * Rot.from_quat(L[:, j])).as_rotvec() * a.amount
        newL[:, j] = (m * Rot.from_rotvec(d)).as_quat()
    mean_p = root_p.mean(0)
    for f in range(nf):
        wq = [None] * nb; wp = [None] * nb; fr = []
        for j in range(nb):   # parents come before children in fbx_clip_dump's order
            lq = Rot.from_quat(newL[f, j])
            if par[j] < 0:
                k = roots.index(j); p = root_p[f, k].copy(); p[1] = mean_p[k, 1] + a.amount * (p[1] - mean_p[k, 1])
                wq[j], wp[j] = lq, p
            else:
                pq = wq[par[j]]; wq[j] = pq * lq; wp[j] = wp[par[j]] + pq.apply(rest_t[j] * np.array(c.get('parent_scale', [1, 1, 1])))
            fr.append({'local': newL[f, j].tolist(), 'world_q': wq[j].as_quat().tolist(), 'world_p': [float(x) for x in wp[j]]})
        out.append(fr)
    c2 = dict(c); c2['samples'] = out; c2['gentle'] = {'from': str(a.clip), 'amount': a.amount}
    a.out.parent.mkdir(parents=True, exist_ok=True); a.out.write_text(json.dumps(c2))
    # check: at amount 1 the rebuild should match the source's world rotations
    err = max(float(np.abs(np.abs(np.sum(np.array(S[f][j]['world_q']) * np.array(out[f][j]['world_q']))) - 1)) for f in (0, nf // 2) for j in range(nb))
    print(json.dumps({'frames': nf, 'bones': nb, 'amount': a.amount, 'world_q_dot_gap_vs_source': round(err, 4)}))


if __name__ == '__main__':
    main()
