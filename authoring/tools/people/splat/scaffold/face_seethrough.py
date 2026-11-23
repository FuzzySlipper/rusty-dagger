#!/usr/bin/env python3
"""How much of a bound character's face can be seen through, from the front.

    face_seethrough.py SPLAT.npz TRANSFORM.json [--box -0.035,0.035,1.48,1.64] [--layer 0.04] [--px 0.002] [--json OUT]
                       [--yaw DEG] [--png MAP.png]

021's face read as a glassy veil (Patch 2026-11-22): a review found rays passing through the front face layer and landing
on the back of the hood 22-25 cm behind it, because the bound bake's 2.5 cm shell held the face Gaussians forward of the true
face (Meshy's hooded head was 4-7 cm too thick). This measures that, with no GPU: the splat (a bake-frame splat.npz, logit
opacities, log scales, wxyz quaternions) is taken into the scaffold's frame with the score step's transform.json (bake =
scale * R v + t), and each Gaussian's footprint is composited front to back in an orthographic view straight down -z,
--px metres a pixel, over --box (x0,x1,y0,y1 in scaffold metres: the face; 021's default, the eyes to the chin).

Per pixel: the first surface is where the composited alpha passes 0.05; T_front is the transmittance left after every
Gaussian within --layer metres behind it (a solid face: about 0; a veil over the back of the hood: large). Reports the mean,
p50 and p90 of T_front, the share of pixels above 0.1, and the total transmittance (light through to the background).
--yaw looks from DEG round to the figure's right instead of the front (the box is then in that view's x); --box whole takes
the whole figure (with --px 0.006 or so: a minute or two on the CPU); --png writes T_front as a map (black solid, white clear,
blue empty) for finding see-through patches anywhere on the body, as on 017's robe (2026-11-22).
"""
import argparse
import json

import numpy as np


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('splat'); ap.add_argument('transform')
    ap.add_argument('--box', default='-0.035,0.035,1.48,1.64'); ap.add_argument('--layer', type=float, default=0.04)
    ap.add_argument('--px', type=float, default=0.002); ap.add_argument('--json')
    ap.add_argument('--yaw', type=float, default=0.0); ap.add_argument('--png')
    a = ap.parse_args()
    z = np.load(a.splat); t = json.load(open(a.transform))
    s, R, tr = t['scale'], np.array(t['rotation']), np.array(t['translation'])
    mu = (z['means'] - tr) @ R / s                                       # scaffold frame, metres
    sc = np.exp(z['scales']) / s
    ty = np.radians(a.yaw); Ry = np.array([[np.cos(ty), 0, np.sin(ty)], [0, 1, 0], [-np.sin(ty), 0, np.cos(ty)]])
    mu = mu @ Ry.T; Rw = Ry @ R.T                                         # the camera DEG round to her right looks down -z
    op = 1 / (1 + np.exp(-z['opacities']))
    q = z['quats'] / np.linalg.norm(z['quats'], axis=1, keepdims=True)
    if a.box == 'whole':
        lo, hi = np.percentile(mu[op > 0.3], [0.2, 99.8], 0); x0, x1, y0, y1 = lo[0], hi[0], lo[1], hi[1]
    else:
        x0, x1, y0, y1 = (float(v) for v in a.box.split(','))
    m = 0.03
    near = (mu[:, 0] > x0 - m) & (mu[:, 0] < x1 + m) & (mu[:, 1] > y0 - m) & (mu[:, 1] < y1 + m) & (op > 0.004)
    mu, sc, op, q = mu[near], sc[near], op[near], q[near]
    w, x, y, zq = q.T
    Rg = np.stack([1 - 2 * (y * y + zq * zq), 2 * (x * y - w * zq), 2 * (x * zq + w * y),
                   2 * (x * y + w * zq), 1 - 2 * (x * x + zq * zq), 2 * (y * zq - w * x),
                   2 * (x * zq - w * y), 2 * (y * zq + w * x), 1 - 2 * (x * x + y * y)], 1).reshape(-1, 3, 3)
    Rs = Rw[None] @ Rg                                                     # Gaussian axes in the (turned) scaffold frame
    C = Rs @ (sc[:, :, None] ** 2 * np.transpose(Rs, (0, 2, 1)))
    C2 = C[:, :2, :2] + np.eye(2) * (0.3 * a.px) ** 2
    inv = np.linalg.inv(C2)
    nx, ny = int(round((x1 - x0) / a.px)), int(round((y1 - y0) / a.px))
    gx = x0 + (np.arange(nx) + 0.5) * a.px; gy = y0 + (np.arange(ny) + 0.5) * a.px
    order = np.argsort(-mu[:, 2])                                          # nearest the camera (+z) first
    T = np.ones((ny, nx)); first = np.full((ny, nx), np.nan); Tf = np.full((ny, nx), np.nan)
    rad = 3 * np.sqrt(np.maximum(C2[:, 0, 0], C2[:, 1, 1]))
    for i in order:
        ia, ib = np.searchsorted(gx, [mu[i, 0] - rad[i], mu[i, 0] + rad[i]])
        ja, jb = np.searchsorted(gy, [mu[i, 1] - rad[i], mu[i, 1] + rad[i]])
        if ia >= ib or ja >= jb:
            continue
        dx = gx[None, ia:ib] - mu[i, 0]; dy = gy[ja:jb, None] - mu[i, 1]
        g = np.exp(-0.5 * (inv[i, 0, 0] * dx * dx + 2 * inv[i, 0, 1] * dx * dy + inv[i, 1, 1] * dy * dy))
        al = np.minimum(0.99, op[i] * g)
        blk = (slice(ja, jb), slice(ia, ib))
        f = first[blk]; zi = mu[i, 2]
        # the layer closes once a Gaussian lies more than --layer behind the first surface: record T there, once
        closing = ~np.isnan(f) & np.isnan(Tf[blk]) & (zi < f - a.layer)
        Tf[blk] = np.where(closing, T[blk], Tf[blk])
        T[blk] *= 1 - al
        newly = np.isnan(f) & (T[blk] < 0.95)
        first[blk] = np.where(newly, zi, f)
    Tf = np.where(np.isnan(Tf), T, Tf)
    hit = ~np.isnan(first)
    v = Tf[hit]
    rep = {'splat': a.splat, 'gaussians_near_face': int(near.sum()), 'pixels': int(hit.sum()), 'layer_m': a.layer, 'box': a.box,
           'T_front_mean': round(float(v.mean()), 4), 'T_front_p50': round(float(np.median(v)), 4),
           'T_front_p90': round(float(np.percentile(v, 90)), 4), 'share_over_0.1': round(float((v > 0.1).mean()), 4),
           'T_total_mean': round(float(T[hit].mean()), 5), 'front_z_median': round(float(np.median(first[hit])), 4)}
    if a.png:
        from PIL import Image
        img = np.zeros((ny, nx, 3)); img[..., 2] = 0.35
        img[hit] = np.clip(Tf[hit], 0, 1)[:, None]
        Image.fromarray((img[::-1] * 255).astype(np.uint8)).save(a.png)
    print(json.dumps(rep))
    if a.json:
        open(a.json, 'w').write(json.dumps(rep, indent=1) + '\n')


if __name__ == '__main__':
    main()
