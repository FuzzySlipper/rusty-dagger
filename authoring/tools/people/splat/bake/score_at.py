#!/usr/bin/env python3
"""score_at.py FIT DATA SIZE...: a round's held-out subject PSNR (train_loop_splat.py's measure) rendered at other sizes (GPU lab).

FIT is the round's output (splat.npz, poses.npz with the refined held-out cameras), DATA its loop data (views.json, images/).
For each SIZE the held-out cameras' intrinsics are scaled by SIZE/W, the splat is rendered on the data's grey and compared with
the held-out frame and its matte resized to SIZE (area filter), over the union of matte and render alpha as the trainer does.
At the data's own size this reproduces score.json's heldout subject_psnr (up to the colour clamp)."""
import json, math, os, sys
import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image
from gsplat import rasterization

fit, data, sizes = sys.argv[1], sys.argv[2], [int(v) for v in sys.argv[3:]]
meta = json.load(open(data + '/views.json'))
files = sorted(os.listdir(data + '/images'), key=lambda f: (len(f), f))
held = [i for i, v in enumerate(meta['views']) if v['role'] == 'heldout']
grey = torch.tensor(np.array(meta['views'][0].get('background', [122, 122, 122])) / 255, dtype=torch.float32, device='cuda')
s = {k: torch.from_numpy(v).float().cuda() for k, v in np.load(fit + '/splat.npz').items()}
poses = np.load(fit + '/poses.npz')
out = {}
for size in sizes:
    vals = []
    for i in held:
        im = Image.open(f'{data}/images/{files[i]}').convert('RGBA')
        W = im.size[0]
        t = torch.from_numpy(np.asarray(im.resize((size, size), Image.BOX), np.float32) / 255).cuda().permute(2, 0, 1)
        target, m = t[:3] * t[3:] + grey[:, None, None] * (1 - t[3:]), t[3:]
        K = torch.from_numpy(poses['Ks'][i]).float().cuda().clone()
        K[:2] *= size / W
        view = torch.from_numpy(poses['viewmats'][i]).float().cuda()
        rgb, alpha, _ = rasterization(s['means'], F.normalize(s['quats'], dim=-1), s['scales'].exp(), torch.sigmoid(s['opacities'].reshape(-1)),
                                      s['rgb'], view[None], K[None], size, size, sh_degree=None, packed=False,
                                      near_plane=0.01, far_plane=50.0, backgrounds=grey[None])
        r, a = rgb[0].permute(2, 0, 1), alpha[0].permute(2, 0, 1)
        union = ((m + a) > 0.1).float()
        e = (((r - target) ** 2) * union).sum() / (union.sum() * 3).clamp_min(1)
        vals.append(-10 * math.log10(float(e) + 1e-12))
    out[size] = round(float(np.mean(vals)), 3)
print(json.dumps({'fit': fit, 'heldout_views': len(held), 'heldout_subject_psnr': out}))
