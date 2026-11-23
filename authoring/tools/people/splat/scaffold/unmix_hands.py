#!/usr/bin/env python3
"""Split Gaussians bound partly to a hand and partly to the body (legs, hips, spine).

    unmix_hands.py SPLAT.npz BINDING.npz SCAFFOLD.glb OUT_DIR [--min 0.05]

A character whose hands hang close to a hem (villager 383-A, 2026-11-16) gets Gaussians between hand and
cloth whose weights mix a hand bone with a thigh. In a walk they stretch from the swinging hand to the leg
as streaks. Each Gaussian with at least --min of its weight on the arm chain (shoulder, arm, forearm, hand
and fingers) and at least --min on the rest follows the side that holds more of its weight: the other side's
weights are dropped and the rest renormalised. Only Gaussians with some hand weight are touched.
--drop-hand-coloured (default on) then removes those sent to the body whose colour is nearer the hands' median colour than
the median of the body Gaussians round them (within --near, in the splat's units): hand pieces that would sit on the hem.
--chain forearm touches Gaussians with weight on a forearm or hand (not only a hand), and --by-colour picks each one's side by
colour instead of weight: the arm when its colour is nearer the median of the arm-led Gaussians round it (within --near) than
that of the body-led ones round it; nothing is dropped. For long sleeves that hang beside the thighs (villager 392's bell
sleeves were bound half to the forearm and half to a thigh and stretched between them in a walk, Patch 2026-11-18).
OUT_DIR gets splat.npz (a copy) and binding.npz, and unmix.json with the counts.
"""
import argparse, json, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from retarget_clip import read_glb


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('splat', type=Path); p.add_argument('binding', type=Path); p.add_argument('scaffold', type=Path); p.add_argument('out', type=Path)
    p.add_argument('--min', type=float, default=0.05)
    p.add_argument('--chain', choices=('hand', 'forearm'), default='hand'); p.add_argument('--by-colour', action='store_true')
    p.add_argument('--keep-hand-coloured', action='store_true'); p.add_argument('--near', type=float, default=0.04)
    a = p.parse_args()
    doc, _ = read_glb(a.scaffold)
    names = [doc['nodes'][j]['name'] for j in doc['skins'][0]['joints']]
    arm = np.array([any(s in n for s in ('Shoulder', 'Arm', 'Hand')) for n in names])
    hand = np.array([('Hand' in n) or (a.chain == 'forearm' and 'ForeArm' in n) for n in names])
    b = dict(np.load(a.binding)); bones = b['bones'].astype(np.int64); w = b['weights'].astype(np.float32).copy()
    on_arm = arm[bones] & (w > 0); on_hand = hand[bones] & (w > 0)
    aw = (w * on_arm).sum(1); hw = (w * on_hand).sum(1); rest = w.sum(1) - aw
    mixed = (hw >= a.min) & (aw >= a.min) & (rest >= a.min)
    to_arm = mixed & (aw >= rest)
    if a.by_colour:
        from scipy.spatial import cKDTree
        sp0 = np.load(a.splat); rgb = np.clip(sp0['rgb'].astype(np.float64), 0, 1); x = sp0['means']
        armled = ~mixed & (aw > 0.9); bodyled = ~mixed & (aw < 0.05)
        ta, tb = cKDTree(x[armled]), cKDTree(x[bodyled]); ra, rb = rgb[armled], rgb[bodyled]
        to_arm = np.zeros(len(w), bool)
        for i in np.where(mixed)[0]:
            na, nb = ta.query_ball_point(x[i], a.near), tb.query_ball_point(x[i], a.near)
            if not na: continue
            if not nb: to_arm[i] = True; continue
            to_arm[i] = np.linalg.norm(rgb[i] - np.median(ra[na], 0)) < np.linalg.norm(rgb[i] - np.median(rb[nb], 0))
    w[to_arm] *= on_arm[to_arm]; w[mixed & ~to_arm] *= ~on_arm[mixed & ~to_arm]
    w[mixed] /= w[mixed].sum(1, keepdims=True)
    a.out.mkdir(parents=True, exist_ok=True)
    sp = dict(np.load(a.splat)); keep = np.ones(len(w), bool); body = mixed & ~to_arm
    if not a.keep_hand_coloured and not a.by_colour and body.any():
        from scipy.spatial import cKDTree
        rgb = np.clip(sp['rgb'].astype(np.float64), 0, 1); x = sp['means']
        hand_med = np.median(rgb[(hw > 0.5) & ~mixed], 0); plain = ~mixed & (aw < 0.05)
        tree = cKDTree(x[plain]); pr = rgb[plain]
        for i in np.where(body)[0]:
            nb = tree.query_ball_point(x[i], a.near)
            if len(nb) < 5:
                keep[i] = False; continue
            if np.linalg.norm(rgb[i] - hand_med) < np.linalg.norm(rgb[i] - np.median(pr[nb], 0)):
                keep[i] = False
    b['weights'] = w.astype(b['weights'].dtype)
    np.savez(a.out / 'splat.npz', **{k: v[keep] if len(v) == len(keep) else v for k, v in sp.items()})
    np.savez(a.out / 'binding.npz', **{k: v[keep] if len(v) == len(keep) else v for k, v in b.items()})
    rep = {'gaussians': int(len(w)), 'mixed': int(mixed.sum()), 'to_arm': int(to_arm.sum()), 'to_body': int(body.sum()),
           'dropped_hand_coloured': int((~keep).sum()), 'kept': int(keep.sum()), 'min': a.min}
    (a.out / 'unmix.json').write_text(json.dumps(rep) + '\n'); print(json.dumps(rep))


if __name__ == '__main__':
    main()
