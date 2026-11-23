#!/usr/bin/env python3
"""Render a bound bake as Gaussians in poses of its scaffold's rig (gsplat, GPU lab).

    render_posed.py SPLAT.npz BINDING.npz POSES.npz OUT_DIR [--size 1088] [--jpg] [--under UNDER.npz:UNDER_BINDING.npz]

BINDING.npz (export_bound.py --binding): bones [N,4] and weights [N,4] per Gaussian. POSES.npz:
`skin` [P, B, 4, 4], one skinning matrix per bone per pose in the bake's frame (row-major, rest =
identity), `viewmats` [V,4,4] and `Ks` [V,3,3] at `size`, and `names` [P]. Each Gaussian's centre
is moved by its blended matrix and its orientation by that matrix's rotation; sizes, colours and
opacities are the bake's. Writes OUT_DIR/POSE-vN.png (or .jpg, for clips of many frames) on the bake's grey.

--under draws a second posed splat (underlayer.py's coloured copy of the scaffold) as a BASE LAYER: it is rendered first and the
body is laid over it by the body's own coverage, whatever their depths, so it only shows where the body's splats do not cover
(a renderer would draw the scaffold mesh first, without letting it hide splats). Prints, per run, the share of the figure's
pixels (where the underlayer is solid) that the body covers less than half, and less than 0.9: the gaps the base layer fills.
"""
import argparse
import json
import os

import numpy as np
import torch
import torch.nn.functional as F
from gsplat import rasterization
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("splat"); ap.add_argument("binding"); ap.add_argument("poses"); ap.add_argument("out"); ap.add_argument("--size", type=int, default=1088)
ap.add_argument("--jpg", action="store_true"); ap.add_argument("--under", help="UNDER.npz:UNDER_BINDING.npz drawn as a base layer beneath the body")
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
ps = np.load(a.poses)
dev = "cuda"
skin = torch.from_numpy(ps["skin"]).float().to(dev)
view, K = torch.from_numpy(ps["viewmats"]).float().to(dev), torch.from_numpy(ps["Ks"]).float().to(dev)
scale = a.size / float(ps["size"])
K = K.clone(); K[:, :2] *= scale
bg = torch.full((1, 3), 122 / 255, device=dev)


def load(splat, binding):
    sp, bd = np.load(splat), np.load(binding)
    quats = F.normalize(torch.from_numpy(sp["quats"]).float().to(dev), dim=-1)
    w, x, y, z = quats.unbind(-1)
    R0 = torch.stack([1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                      2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)], -1).reshape(-1, 3, 3)
    return {"means": torch.from_numpy(sp["means"]).float().to(dev), "R0": R0, "scales": torch.exp(torch.from_numpy(sp["scales"]).float().to(dev)),
            "opac": torch.sigmoid(torch.from_numpy(sp["opacities"]).float().to(dev).reshape(-1)), "rgb": torch.from_numpy(sp["rgb"]).float().to(dev),
            "bones": torch.from_numpy(bd["bones"].astype(np.int64)).to(dev), "weights": torch.from_numpy(bd["weights"]).float().to(dev)}


def to_quat(R):
    m00, m11, m22 = R[:, 0, 0], R[:, 1, 1], R[:, 2, 2]
    qw = torch.sqrt((1 + m00 + m11 + m22).clamp(min=1e-8)) / 2
    qx = torch.sqrt((1 + m00 - m11 - m22).clamp(min=1e-8)) / 2 * torch.sign(R[:, 2, 1] - R[:, 1, 2] + 1e-12)
    qy = torch.sqrt((1 - m00 + m11 - m22).clamp(min=1e-8)) / 2 * torch.sign(R[:, 0, 2] - R[:, 2, 0] + 1e-12)
    qz = torch.sqrt((1 - m00 - m11 + m22).clamp(min=1e-8)) / 2 * torch.sign(R[:, 1, 0] - R[:, 0, 1] + 1e-12)
    return F.normalize(torch.stack([qw, qx, qy, qz], -1), dim=-1)


def posed(d, p):
    T = (skin[p][d["bones"]] * d["weights"][..., None, None]).sum(1)                 # [N, 4, 4]
    m = (T[:, :3, :3] @ d["means"][..., None])[..., 0] + T[:, :3, 3]
    U, _, Vh = torch.linalg.svd(T[:, :3, :3])
    return m, to_quat((U @ Vh) @ d["R0"])


def draw(d, m, q, v):
    """Premultiplied colour and coverage of one posed splat from one camera."""
    c, al, _ = rasterization(m, q, d["scales"], d["opac"], d["rgb"], view[v:v + 1], K[v:v + 1], a.size, a.size, packed=False, near_plane=0.01, far_plane=50.0)
    return c[0], al[0]


body = load(a.splat, a.binding)
under = load(*a.under.split(":")) if a.under else None
thin, thinner, solid = 0.0, 0.0, 0.0
with torch.no_grad():
    for p, name in enumerate(ps["names"]):
        m, q = posed(body, p)
        if under:
            um, uq = posed(under, p)
        for v in range(len(view)):
            c, al = draw(body, m, q, v)
            if under:
                uc, ual = draw(under, um, uq, v)
                base = uc + (1 - ual) * bg[0]
                inside = ual[..., 0] > 0.9
                solid += float(inside.sum()); thin += float((inside & (al[..., 0] < 0.5)).sum()); thinner += float((inside & (al[..., 0] < 0.9)).sum())
            else:
                base = bg[0]
            out = c + (1 - al) * base
            im = Image.fromarray(out.clamp(0, 1).mul(255).byte().cpu().numpy())
            im.save(f"{a.out}/{name}-v{v}.jpg", quality=95) if a.jpg else im.save(f"{a.out}/{name}-v{v}.png")
if under:
    print("COVER", json.dumps({"figure_pixels_body_covers_under_half": round(thin / max(solid, 1), 4), "under_0.9": round(thinner / max(solid, 1), 4)}))
print("wrote", len(ps["names"]) * len(view), "pictures to", a.out)
