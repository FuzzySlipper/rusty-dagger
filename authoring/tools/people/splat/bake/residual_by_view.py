#!/usr/bin/env python3
"""residual_by_view.py FIT DATA [SIZE]: where a round disagrees with its frames, view by view and body band (GPU lab).

For every view of DATA (anchors and held-out), the round's splat is rendered at the round's refined camera (FIT/poses.npz) as score_at.py
does, and compared with the frame inside the frame's matte: mean absolute error (0-255) and the mean signed luminance error (render minus
frame, so a + means the fit is brighter than this view saw) for the whole subject and for bands of the matte's height: torso 20-45%,
forearm rows 44-54%, legs 74-92%, each split into picture-left and picture-right halves about the matte's column centre. A light fixed in
the room should leave small residuals with no view-dependent band; a light that turns with the camera leaves a signed error that changes
sign between opposite views (the fit averages the two). Prints JSON lines, one per view: index, role, and the band values."""
import json, os, sys
import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image
from gsplat import rasterization

fit, data = sys.argv[1], sys.argv[2]; size = int(sys.argv[3]) if len(sys.argv) > 3 else 768
meta = json.load(open(data + '/views.json')); files = sorted(os.listdir(data + '/images'), key=lambda f: (len(f), f))
grey = torch.tensor(np.array(meta['views'][0].get('background', [122, 122, 122])) / 255, dtype=torch.float32, device='cuda')
s = {k: torch.from_numpy(v).float().cuda() for k, v in np.load(fit + '/splat.npz').items()}
poses = np.load(fit + '/poses.npz'); lw = torch.tensor([0.2126, 0.7152, 0.0722], device='cuda')[:, None, None]
for i, v in enumerate(meta['views']):
    if v['role'] == 'dropped': continue
    im = Image.open(f'{data}/images/{files[i]}').convert('RGBA'); W = im.size[0]
    t = torch.from_numpy(np.asarray(im.resize((size, size), Image.BOX), np.float32) / 255).cuda().permute(2, 0, 1)
    target, m = t[:3] * t[3:] + grey[:, None, None] * (1 - t[3:]), (t[3] > 0.5)
    K = torch.from_numpy(poses['Ks'][i]).float().cuda().clone(); K[:2] *= size / W
    view = torch.from_numpy(poses['viewmats'][i]).float().cuda()
    rgb, alpha, _ = rasterization(s['means'], F.normalize(s['quats'], dim=-1), s['scales'].exp(), torch.sigmoid(s['opacities'].reshape(-1)),
                                  s['rgb'], view[None], K[None], size, size, sh_degree=None, packed=False, near_plane=0.01, far_plane=50.0, backgrounds=grey[None])
    r = rgb[0].permute(2, 0, 1)
    ae = ((r - target).abs().mean(0) * 255); se = (((r - target) * lw).sum(0) * 255)
    ys = torch.nonzero(m.any(1)).flatten(); top, bot = int(ys.min()), int(ys.max()); H = bot - top
    xc = int(torch.nonzero(m.any(0)).flatten().float().mean())
    row = {'index': i, 'role': v['role'], 'all_mae': round(float(ae[m].mean()), 2), 'all_signed': round(float(se[m].mean()), 2)}
    for name, (f0, f1) in (('torso', (0.20, 0.45)), ('forearm', (0.44, 0.54)), ('legs', (0.74, 0.92))):
        y0, y1 = top + int(f0 * H), top + int(f1 * H)
        for side, sl in (('L', slice(0, xc)), ('R', slice(xc, size))):
            mm = torch.zeros_like(m); mm[y0:y1, sl] = m[y0:y1, sl]
            row[f'{name}_{side}_mae'] = round(float(ae[mm].mean()), 2) if mm.sum() > 50 else None
            row[f'{name}_{side}_signed'] = round(float(se[mm].mean()), 2) if mm.sum() > 50 else None
    print(json.dumps(row))
