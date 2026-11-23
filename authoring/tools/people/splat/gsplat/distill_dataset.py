#!/usr/bin/env python3
"""Render a splat from rings of known cameras into a train_loop_splat.py dataset, to refit it at a smaller budget.

  python distill_dataset.py SPLAT.npz OUT_DIR [--elevations -35,-15,0,15,35,60] [--per-ring 36]
      [--size 640] [--fov 30] [--distance 1.6] [--target 0,0,0] [--exact-every 12] [--arc FROM,TO]   (GPU lab: torch + gsplat)

For a splat with no photographs of its own, such as a garment layer cut out of
a dressed figure (authoring/tools/people/splat/mesh-splat/cloak_layer.py): its renders are the
training views. Each image is RGBA with straight (not premultiplied) colour,
so the trainer's random-background compositing sees the layer's true
silhouette, its open front and its inner face alike. Rings at several
elevations cover the outside, the hem from below and, through the open
front, the inside. Every --exact-every-th view is kept back with role
`exact` (scored, never trained). init_points.npz holds every source Gaussian
centre; cut it to a budget with budget_seed.py, then train with
--freeze-poses --ring 0 (the cameras are exact).

--arc FROM,TO spreads each ring's cameras over that range of azimuths (degrees,
0 = from +z, the front) instead of the full circle: close views of one side of
a part, such as a face (--target at the head, a short --distance).
"""
import argparse
import json
import math
import os
import sys

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image
from gsplat import rasterization

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from train_loop_splat import C0, ring_cameras  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('splat'); ap.add_argument('out')
    ap.add_argument('--elevations', default='-35,-15,0,15,35,60'); ap.add_argument('--per-ring', type=int, default=36)
    ap.add_argument('--size', type=int, default=640); ap.add_argument('--fov', type=float, default=30)
    ap.add_argument('--distance', type=float, default=1.6); ap.add_argument('--target', default='0,0,0')
    ap.add_argument('--exact-every', type=int, default=12)
    ap.add_argument('--arc', help='FROM,TO azimuths in degrees: an arc instead of full rings')
    a = ap.parse_args()
    os.makedirs(a.out + '/images', exist_ok=True)
    s = np.load(a.splat)
    dev = 'cuda'
    means = torch.from_numpy(s['means']).float().to(dev)
    quats = F.normalize(torch.from_numpy(s['quats']).float().to(dev), dim=-1)
    scales = torch.exp(torch.from_numpy(s['scales']).float().to(dev))
    opac = torch.sigmoid(torch.from_numpy(s['opacities'].reshape(-1)).float().to(dev))
    sh0 = ((torch.from_numpy(np.clip(s['rgb'], 0, 1)).float().to(dev) - 0.5) / C0)[:, None]
    target = np.array([float(v) for v in a.target.split(',')], np.float32)
    mats, Ks, views = [], [], []
    for el in [float(v) for v in a.elevations.split(',')]:
        if a.arc:   # a full ring of 360 one-degree steps, of which the arc's azimuths are kept
            lo, hi = [float(v) for v in a.arc.split(',')]
            vm, K = ring_cameras(360, el, a.distance, a.fov, a.size)
            keep = [int(round(z)) % 360 for z in np.linspace(lo, hi, a.per_ring)]
            vm, K = vm[keep], K[keep]
        else:
            vm, K = ring_cameras(a.per_ring, el, a.distance, a.fov, a.size)
        shift = np.eye(4, dtype=np.float32); shift[:3, 3] = -target          # look at TARGET instead of the origin
        mats += [m @ shift for m in vm]; Ks += list(K)
    zero = torch.zeros(1, 3, device=dev)
    for i, (vm, K) in enumerate(zip(mats, Ks)):
        out = rasterization(means, quats, scales, opac, sh0, torch.from_numpy(vm)[None].to(dev), torch.from_numpy(K)[None].to(dev),
                            a.size, a.size, sh_degree=0, packed=False, near_plane=0.01, far_plane=50.0, backgrounds=zero)
        rgb, alpha = out[0][0].clamp(0, 1), out[1][0].clamp(0, 1)
        straight = torch.where(alpha > 1e-3, rgb / alpha.clamp_min(1e-3), torch.zeros_like(rgb)).clamp(0, 1)
        img = torch.cat([straight, alpha], -1).mul(255).round().byte().cpu().numpy()
        name = f'{i:04d}.png'
        Image.fromarray(img, 'RGBA').save(f'{a.out}/images/{name}')
        role = 'exact' if a.exact_every and i % a.exact_every == a.exact_every // 2 else 'anchor'
        views.append({'name': name, 'role': role, 'background': [122, 122, 122]})
    np.savez_compressed(a.out + '/cameras.npz', viewmats=np.array(mats, np.float32), Ks=np.array(Ks, np.float32))
    m = s['means']
    spacing = float(np.median(np.exp(s['scales']).max(1)))
    np.savez_compressed(a.out + '/init_points.npz', points=m.astype(np.float32), colors=np.clip(s['rgb'], 0, 1).astype(np.float32), spacing=spacing)
    json.dump({'source': a.splat, 'size': [a.size, a.size], 'fov_deg': a.fov, 'distance': a.distance, 'target': target.tolist(),
               'elevations': a.elevations, **({'arc': a.arc} if a.arc else {}), 'views': views}, open(a.out + '/views.json', 'w'), indent=1)
    print('wrote', len(views), 'views,', sum(v['role'] == 'exact' for v in views), 'exact ->', a.out)


if __name__ == '__main__':
    main()
