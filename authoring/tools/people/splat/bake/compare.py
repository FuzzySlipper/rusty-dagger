#!/usr/bin/env python3
"""compare.py OUT.png LABEL=SPLAT.npz... [--tile 1080]: townsman 385's close-up sheet (GPU lab; loop_review.py's camera beside it).

Rows are splats in round 0's frame (Y up, front +Z); columns are fixed close-ups, the same cameras for every row (azimuth 0 on +Z,
distance 2.7, FOV in degrees): the face from the front, 35 degrees to each side from 5 below eye level, from 40 above; each hand;
the pouch and belt (his right hip); the case and diagonal strap (his left hip); the striped legs from the front, the +x side and
behind; the shoes. Targets measured on round 0 (hands, pouch, case: opaque skin- and leather-coloured Gaussians)."""
import argparse, sys
from pathlib import Path
import numpy as np
import torch
from PIL import Image, ImageDraw
from gsplat import rasterization
sys.path.insert(0, str(Path(__file__).resolve().parent))
from loop_review import camera

COLUMNS = [
    ('Face front', (0.0, 0.39, 0.06), 0, 3, 7),
    ('Face 35 to +x, 5 below', (0.0, 0.39, 0.06), 35, -5, 7),
    ('Face 35 to -x, 5 below', (0.0, 0.39, 0.06), -35, -5, 7),
    ('Face from 40 above', (0.0, 0.39, 0.04), 0, 40, 8),
    ('Hand +x', (0.225, 0.0, 0.02), 60, 10, 8),
    ('Hand -x', (-0.21, -0.02, 0.1), -60, 10, 8),
    ('Pouch and belt', (-0.11, 0.0, 0.08), -35, 8, 10),
    ('Case and strap', (0.12, 0.0, 0.0), 75, 8, 10),
    ('Legs front', (0.0, -0.32, 0.0), 0, 5, 15),
    ('Legs side +x', (0.0, -0.32, 0.0), 90, 5, 15),
    ('Legs back', (0.0, -0.32, 0.0), 180, 5, 15),
    ('Shoes', (0.0, -0.47, 0.0), 20, 12, 12),
]
p = argparse.ArgumentParser(); p.add_argument('out'); p.add_argument('rows', nargs='+'); p.add_argument('--tile', type=int, default=1080)
a = p.parse_args()
bg = torch.tensor([115, 115, 115], dtype=torch.float32, device='cuda') / 255
sheet = Image.new('RGB', (len(COLUMNS) * a.tile, len(a.rows) * a.tile)); draw = ImageDraw.Draw(sheet)
for r, spec in enumerate(a.rows):
    label, path = spec.split('=', 1)
    with np.load(path) as src:
        s = {k: torch.from_numpy(src[k]).float().cuda() for k in ('means', 'quats', 'scales', 'opacities', 'rgb')}
    for c, (name, target, az, el, fov) in enumerate(COLUMNS):
        view, k = camera(list(target), az, el, 2.7, fov, a.tile)
        with torch.no_grad():
            img, _, _ = rasterization(s['means'], torch.nn.functional.normalize(s['quats'], dim=-1), s['scales'].exp(),
                                      s['opacities'].reshape(-1).sigmoid(), s['rgb'], torch.from_numpy(view)[None].cuda(),
                                      torch.from_numpy(k)[None].cuda(), a.tile, a.tile, sh_degree=None, packed=False,
                                      near_plane=0.01, far_plane=50.0, backgrounds=bg[None])
        sheet.paste(Image.fromarray((img[0].clamp(0, 1) * 255).byte().cpu().numpy()), (c * a.tile, r * a.tile))
        draw.text((c * a.tile + 16, r * a.tile + 12), f'{label}: {name}', fill='white', font_size=30)
sheet.save(a.out); print('wrote', a.out, sheet.size)
