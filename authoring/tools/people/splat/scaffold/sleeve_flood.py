#!/usr/bin/env python3
"""Bind a long hanging sleeve wholly to the arm it hangs from.

    sleeve_flood.py SPLAT.npz BINDING.npz SCAFFOLD.glb TRANSFORM.json OUT_DIR [--link 0.012] [--colour 0.12]

A bell sleeve that hangs beside the thigh gets bound partly or wholly to the leg, so in a walk it smears or tears between arm
and thigh (villager 392, Patch 2026-11-18). For each arm, this starts from the Gaussians the forearm and hand lead (weight over
0.5) below the elbow, and grows through Gaussians below the elbow within --link (metres) of one already reached whose colour is within --colour (RGB
distance) of that one's. Everything reached takes the bone
weights of the nearest seed. The skirt (another colour) and the bodice (above the elbow) stay as they are.
OUT_DIR gets splat.npz (a copy), binding.npz and sleeves.json with the counts.
"""
import argparse, json, sys
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from skirt_weights import load_rig


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('splat', type=Path); p.add_argument('binding', type=Path); p.add_argument('scaffold', type=Path); p.add_argument('transform', type=Path)
    p.add_argument('out', type=Path); p.add_argument('--link', type=float, help='metres (default 0.03 up to 50,000 Gaussians, else 0.015)'); p.add_argument('--colour', type=float, default=0.1)
    p.add_argument('--dark', type=float, default=0.12, help='then take dark Gaussians (mean RGB under this) up to --dark-steps links out: the sleeve\'s shadowed inside')
    p.add_argument('--dark-steps', type=int, default=2)
    p.add_argument('--recolour', type=float, default=0.08, help='round the hand (0.3 m, lowest 30%% of the forearm), give Gaussians (bound to the arm too) whose blue cast exceeds the forearm sleeve\'s by this (or saturation by 5 times this; not skin) the sleeve\'s colour at their own brightness (0 = off): 392\'s left sleeve end trained to cyan glitter, Patch 2026-11-18')
    p.add_argument('--mixed', type=float, default=0, help='also take every Gaussian below the elbow that the forearm or hand leads by more than this share (0 = off) and is lighter than --dark: sleeve cloth weighted half to the arm, half to the body (392 -r02 still smeared, Patch 2026-11-18)')
    a = p.parse_args()
    rig = load_rig(a.scaffold); names = list(rig['names']); T = json.loads(a.transform.read_text())
    sc, rot, tr = T['scale'], np.array(T['rotation']), np.array(T['translation'])
    J = {m: sc * np.asarray(rig['bind'][i], float) @ rot.T + tr for i, m in enumerate(names)}   # joints in the bake's frame
    sp = dict(np.load(a.splat)); b = dict(np.load(a.binding)); xs = sp['means'].astype(np.float64); rgb = np.clip(sp['rgb'].astype(np.float64), 0, 1)
    bo = b['bones'].astype(np.int64); w = b['weights'].astype(np.float32).copy(); n = len(xs); link = (a.link or (0.03 if n <= 50000 else 0.015)) * sc   # metres -> bake units
    up = 1 if J['Head'][1] > J['Hips'][1] else -1
    rep = {'gaussians': n}; total = np.zeros(n, bool); recol = {}
    lead_now = lambda side: (w * np.array([(f'{side}ForeArm' in m) or (f'{side}Hand' in m) for m in names])[bo]).sum(1)
    for side in ('Left', 'Right'):
        chain = np.array([(f'{side}ForeArm' in m) or (f'{side}Hand' in m) for m in names])
        lead = (w * chain[bo]).sum(1)
        below = up * xs[:, 1] < up * J[f'{side}ForeArm'][1]
        seed = (lead > 0.5) & below
        if seed.sum() < 20:
            rep[side] = {'recoloured': recol.get(side, 0), 'seeds': int(seed.sum())}; continue
        med = np.median(rgb[seed], 0); idx = np.where(below)[0]; tree = cKDTree(xs[idx]); reached = seed.copy(); front = np.where(seed)[0]
        while len(front):   # grow to touching Gaussians whose colour is close to the one that reached them (stops at the skirt's edge)
            new = []
            for i, nb in zip(front, tree.query_ball_point(xs[front], link)):
                if nb:
                    j = idx[nb]; j = j[~reached[j]]
                    new.append(j[np.linalg.norm(rgb[j] - rgb[i], axis=1) < a.colour])
            new = np.unique(np.concatenate(new)) if new else np.array([], int)
            new = new[~reached[new]]; reached[new] = True; front = new
        dark = below & (rgb.mean(1) < a.dark); front = np.where(reached)[0]
        for _ in range(a.dark_steps):   # the bell's inside, in shadow, is too dark for the colour step
            nb = [k for k in tree.query_ball_point(xs[front], link) if k]
            new = np.unique(idx[np.concatenate(nb)]) if nb else np.array([], int)
            new = new[dark[new] & ~reached[new]]; reached[new] = True; front = new
        if a.mixed: reached |= below & (lead > a.mixed) & (rgb.mean(1) >= a.dark)
        pure = below & (lead > 0.95)   # weights come from the nearest Gaussian the arm leads wholly: the seeds themselves can be
        add = reached & ~total & ~pure   # half thigh (392 -r02: ~190 per hand at 40-60% LeftUpLeg smeared in the walk, Patch 2026-11-18)
        if add.any() and pure.sum() >= 20:
            _, k = cKDTree(xs[pure]).query(xs[add]); src = np.where(pure)[0][k]
            bo[add] = bo[src]; w[add] = w[src]
        total |= reached
        if a.recolour:
            H, E = J[f'{side}Hand'], J[f'{side}ForeArm']; arm = lead_now(side) > 0.5; lo = H[1] + 0.3 * (E[1] - H[1])
            fore = arm & (up * xs[:, 1] < up * E[1]) & (up * xs[:, 1] > up * lo); ref = np.median(rgb[fore], 0)
            near = (np.linalg.norm(xs - H, axis=1) < 0.3 * sc) & (up * xs[:, 1] < up * lo)   # any bone: most of 392's were still thigh-led
            m_ = np.maximum(rgb.mean(1), 0.05); chroma = (rgb.max(1) - rgb.min(1)) / m_ - (ref.max() - ref.min()) / max(ref.mean(), 1e-3)   # glitter: far more saturated than the sleeve (grey skirt is less)
            skin = (rgb[:, 0] > rgb[:, 2] + 0.03) & (rgb[:, 0] >= rgb[:, 1])   # but leave the hand alone
            cast = near & ~skin & ((chroma > 5 * a.recolour) | ((rgb[:, 2] - rgb[:, 0]) > (ref[2] - ref[0]) + a.recolour))
            lum = rgb[cast].mean(1, keepdims=True); sp['rgb'][cast] = np.clip(ref * lum / max(ref.mean(), 1e-3), 0, 1).astype(sp['rgb'].dtype); recol[side] = int(cast.sum())
            mv = cast & ~arm
            if mv.any() and pure.sum() >= 20:
                _, k = cKDTree(xs[pure]).query(xs[mv]); src = np.where(pure)[0][k]; bo[mv] = bo[src]; w[mv] = w[src]
        rep[side] = {'recoloured': recol.get(side, 0), 'seeds': int(seed.sum()), 'pure': int(pure.sum()), 'rebound': int(add.sum()), 'colour': [round(float(c), 3) for c in med]}
    a.out.mkdir(parents=True, exist_ok=True)
    b['bones'] = bo.astype(b['bones'].dtype); b['weights'] = w.astype(b['weights'].dtype)
    np.savez(a.out / 'splat.npz', **sp); np.savez(a.out / 'binding.npz', **b)
    (a.out / 'sleeves.json').write_text(json.dumps(rep) + '\n'); print(json.dumps(rep))


if __name__ == '__main__':
    main()
