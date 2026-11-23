#!/usr/bin/env python3
"""Fit a clip's hips translation to a skinned splat GLB's legs (k, A), and measure foot sliding, offline.

  python3 -I splat_glb_clip_fit.py CHARACTER.glb OUT.json NAME=CLIP.glb[:leg] [NAME=CLIP.glb ...] [--contact 0.01]

The GLB counterpart of clip_fit.py, for vsplat_to_glb.py's output on any rig
(--rig mixamo; the 441 rig's fits come from clip_fit.py). Clips pose the GLB by
node name, rotation always and translation only where the channel varies
(the Engine's TranslationChannels = VaryingOnly), with a clip's wrapper node
folded into its top joint. The top joint's (Hips') sampled translation p, in
that folded Y-up frame, becomes (k p.x, A + k p.y, k p.z): the Engine's
TranslationFit { Scale = (k, k, k), Offset = (0, A, 0) }, as the web page
applies to the 441 pelvis.

k is chosen in 0.5-1.2 for the least planted-foot drift with root motion on
(median end drift over whole-cycle stances plus a quarter of the largest; a grid at 0.02 steps, then a bounded refine
within 0.02 of the best, since the cost has more than one minimum on softened clips), or
with `:leg` after the clip by the leg ratio (the character's thigh plus shin
length against the clip skeleton's). A then keeps
the lowest toe at its rest height through the cycle (median over frames).
A foot is planted while its toe joint is within --contact of its rest height.
OUT.json holds k, A and the numbers per clip, for the character's product data.
"""
import argparse
import json
import os
import sys

import numpy as np
from scipy.optimize import minimize_scalar

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from splat_glb_pose_check import load_clip, read, sample, trs, varies  # noqa: E402

LEGS = {  # top joint, then per side: thigh, shin, ankle, toe joint
    'mixamo': ('mixamorig:Hips', {s: tuple(f'mixamorig:{side}{b}' for b in ('UpLeg', 'Leg', 'Foot', 'ToeBase'))
                                  for s, side in (('l', 'Left'), ('r', 'Right'))}),
}


class Rig:
    def __init__(self, glb, clip_path):
        doc, _ = read(glb)
        self.nodes = doc['nodes']
        self.rig = doc['asset'].get('extras', {}).get('rig', 'mixamo')
        self.top, self.legs = LEGS[self.rig]
        self.parent = {c: i for i, n in enumerate(self.nodes) for c in n.get('children', [])}
        self.joints = set(doc['skins'][0]['joints'])
        self.index = {n.get('name'): i for i, n in enumerate(self.nodes)}
        self.tracks, self.wrapper, self.length = load_clip(clip_path)
        self.clip_doc, _ = read(clip_path)
        self.times = np.arange(0, self.length + 1e-6, 1 / 30)
        # The joints the feet hang from, top first, and their clip-posed locals per frame (top translation aside).
        chain = set()
        for s in 'lr':
            for name in self.legs[s]:
                i = self.index[name]
                while i in self.joints:
                    chain.add(i); i = self.parent.get(i, -1)
        self.chain = sorted(chain, key=self.depth)
        self.local = {}
        for t in self.times:
            for i in self.chain:
                n = self.nodes[i]; name = n['name']
                r = self.tracks.get((name, 'rotation')); tr = self.tracks.get((name, 'translation'))
                rot = sample(r, t) if r else n.get('rotation', [0, 0, 0, 1])
                pos = sample(tr, t) if tr is not None and varies(tr) else n.get('translation', [0, 0, 0])
                m = trs({'rotation': rot, 'translation': pos})
                if name == self.top:
                    w = self.wrapper.get(name, np.eye(4))
                    m = w @ m
                    self.local[(t, 'p')] = m[:3, 3].copy()
                self.local[(t, i)] = m
        self.above = np.eye(4)                                       # non-joint ancestors of the top joint
        i = self.parent.get(self.index[self.top])
        while i is not None:
            self.above = trs(self.nodes[i]) @ self.above; i = self.parent.get(i)
        rest = self.world_rest()
        self.rest_h = {s: rest[self.index[self.legs[s][3]]][1, 3] for s in 'lr'}

    def depth(self, i):
        d = 0
        while i in self.parent:
            i = self.parent[i]; d += 1
        return d

    def world_rest(self):
        W = {}
        for i in sorted(self.joints, key=self.depth):
            m = trs(self.nodes[i])
            W[i] = (self.above if self.nodes[i]['name'] == self.top else W[self.parent[i]]) @ m
        return W

    def leg_length(self, nodes):
        names = {n.get('name'): n for n in nodes}
        return np.mean([sum(np.linalg.norm(names[self.legs[s][b]].get('translation', [0, 0, 0])) for b in (1, 2))
                        for s in 'lr'])

    def pose(self, t, k, A):
        W = {}
        for i in self.chain:
            m = self.local[(t, i)].copy()
            if self.nodes[i]['name'] == self.top:
                p = self.local[(t, 'p')]
                m[:3, 3] = [k * p[0], A + k * p[1], k * p[2]]
                W[i] = self.above @ m
            else:
                W[i] = W[self.parent[i]] @ m
        return W

    def ground(self, k):
        lows = []
        for t in self.times[::2]:
            W = self.pose(t, k, 0.0)
            lows.append(min(W[self.index[self.legs[s][3]]][1, 3] - self.rest_h[s] for s in 'lr'))
        return -float(np.median(lows))

    def measure(self, k, A, contact):
        toe = {s: self.index[self.legs[s][3]] for s in 'lr'}; top = self.index[self.top]
        P = {s: [] for s in 'lr'}; hips = []
        for t in self.times:
            W = self.pose(t, k, A)
            for s in 'lr':
                P[s].append(W[toe[s]][:3, 3])
            hips.append(W[top][:3, 3])
        hips = np.array(hips); ts = self.times
        travel = hips[-1, [0, 2]] - hips[0, [0, 2]]
        out = {'seconds': float(ts[-1]), 'speed_m_s': float(np.linalg.norm(travel) / ts[-1]),
               'travel_xz_m': [round(float(x), 3) for x in travel],
               'hips_height_m': [float(hips[:, 1].min()), float(hips[:, 1].max())]}
        phases = []
        for s in 'lr':
            p = np.array(P[s]); h = p[:, 1] - self.rest_h[s]
            out[f'toe_{s}_lowest_m'] = float(h.min())
            on = h < contact; i = 0
            while i < len(on):
                if not on[i]:
                    i += 1; continue
                j = i
                while j + 1 < len(on) and on[j + 1]:
                    j += 1
                if j - i >= 2:
                    d = np.linalg.norm(p[i:j + 1][:, [0, 2]] - p[i][[0, 2]], axis=1)
                    phases.append({'foot': s, 'from_s': round(float(ts[i]), 2), 'to_s': round(float(ts[j]), 2),
                                   'drift_end_cm': round(100 * float(d[-1]), 1), 'drift_max_cm': round(100 * float(d.max()), 1)})
                i = j + 1
        inner = [q for q in phases if q['from_s'] > 0.05 and q['to_s'] < ts[-1] - 0.05]
        out['stances'] = phases
        if inner:
            out['drift_end_cm_median'] = float(np.median([q['drift_end_cm'] for q in inner]))
            out['drift_end_cm_max'] = float(max(q['drift_end_cm'] for q in inner))
            out['drift_max_cm_median'] = float(np.median([q['drift_max_cm'] for q in inner]))
        return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('glb'); ap.add_argument('out'); ap.add_argument('clips', nargs='+')
    ap.add_argument('--contact', type=float, default=0.01)
    a = ap.parse_args()
    report = {'character': a.glb, 'contact_m': a.contact, 'translation_fit': '(k x, A + k y, k z) on the top joint, wrapper folded', 'clips': {}}
    for spec in a.clips:
        name, path = spec.split('=', 1)
        leg_mode = path.endswith(':leg')
        path = path[:-4] if leg_mode else path
        rig = Rig(a.glb, path)
        k_leg = float(rig.leg_length(rig.nodes) / rig.leg_length(rig.clip_doc['nodes']))
        if leg_mode:
            k = k_leg
        else:
            def cost(kk):
                m = rig.measure(kk, rig.ground(kk), a.contact)
                return m.get('drift_end_cm_median', 1e3) + 0.25 * m.get('drift_end_cm_max', 1e3)
            # a grid first: on softened clips (gentle_glb.py) a bounded search over the whole range settled in a wrong
            # minimum (k 0.9-0.96, toe drift 10-23 cm; rusty-dagger #10978), where the grid finds k 0.55-0.70 at 1-3 cm
            grid = np.round(np.arange(0.5, 1.2001, 0.02), 4)
            k0 = float(grid[int(np.argmin([cost(kk) for kk in grid]))])
            k = float(minimize_scalar(cost, bounds=(max(0.5, k0 - 0.02), min(1.2, k0 + 0.02)), method='bounded', options={'xatol': 0.0005}).x)
        A = rig.ground(k)
        fitted = rig.measure(k, A, a.contact)
        unscaled = rig.measure(1.0, rig.ground(1.0), a.contact)
        report['clips'][name] = {'path': os.path.basename(path), 'k': round(k, 4), 'A': round(A, 4),
                                 'method': 'leg ratio' if leg_mode else 'least planted-toe drift',
                                 'k_leg_ratio': round(k_leg, 4), 'fitted': fitted, 'unscaled_k1': unscaled}
        short = lambda m: {x: m.get(x) for x in ('speed_m_s', 'drift_end_cm_median', 'drift_end_cm_max', 'drift_max_cm_median', 'toe_l_lowest_m', 'toe_r_lowest_m')}  # noqa: E731
        print(name, json.dumps({'k': round(k, 4), 'A': round(A, 4), 'k_leg': round(k_leg, 4), 'fitted': short(fitted), 'k1': short(unscaled)}))
    json.dump(report, open(a.out, 'w'), indent=1)


if __name__ == '__main__':
    main()
