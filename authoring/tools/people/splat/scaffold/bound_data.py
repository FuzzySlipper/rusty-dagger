#!/usr/bin/env python3
"""What a shell-bound bake needs from a scaffold: its start points and its distance grid.

    bound_data.py BOUND.bin RIGGED.glb TRANSFORM.json OUT_DIR [--voxel 0.003] [--margin 0.12]
                  [--loose-bones LeftHand,RightHand --loose-factor 2.4]

BOUND.bin is bind_colours.py --frames output for the same mesh and transform (points on the
scaffold with colours from the pictures). Writes, in the bake's frame:
  OUT_DIR/init_points.npz  points, colors, spacing: the trainer's start (in place of the solved cloud)
  OUT_DIR/shell.npz        dist [z, y, x] (unsigned distance to the scaffold surface, clipped at 0.25),
                           origin, voxel: for train_loop_splat.py --shell
The grid covers the scaffold plus --margin, at --voxel (both in the bake's units).

--loose-bones names bones whose surface (theirs and their descendants') gives the bake more room:
distances to it are divided by --loose-factor in the grid, so with a 2.5 cm reach and a factor of
2.4 a Gaussian may stand 6 cm off a hand. A mesher's hand, or a donor hand, is not where the
painted fingers are; held at the body's reach they bake soft. Give export_bound.py the same two
options, so those Gaussians bind to the hand and not to cloth that happens to be nearer.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).resolve().parent))
from score_outline import load_glb, surface_points  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("bound"); ap.add_argument("glb"); ap.add_argument("transform"); ap.add_argument("out", type=Path)
ap.add_argument("--voxel", type=float, default=0.003); ap.add_argument("--margin", type=float, default=0.12)
ap.add_argument("--loose-bones"); ap.add_argument("--loose-factor", type=float, default=2.4)
a = ap.parse_args()
a.out.mkdir(parents=True, exist_ok=True)
T = json.loads(Path(a.transform).read_text())
s, R, t = T["scale"], np.array(T["rotation"]), np.array(T["translation"])
buf = np.fromfile(a.bound, dtype=[("p", "<f4", 3), ("r", "<f4"), ("n", "i1", 4), ("c", "u1", 4), ("b", "u1", 4), ("w", "u1", 4)])
pts = (s * buf["p"].astype(float) @ R.T + t).astype(np.float32)
spacing = float(buf["r"][0] * np.sqrt(np.pi) / 2 * s)
np.savez_compressed(a.out / "init_points.npz", points=pts, colors=(buf["c"][:, :3] / 255).astype(np.float32), spacing=spacing)

v, f = load_glb(a.glb)
rng = np.random.default_rng(0)
place = lambda q: s * q @ R.T + t
surf = place(surface_points(v, f, 4_000_000, rng))
lo = surf.min(0) - a.margin
dims = np.ceil((surf.max(0) + a.margin - lo) / a.voxel).astype(int) + 1


def field(points):
    occ = np.zeros(dims[::-1], bool)                               # [z, y, x]
    ijk = np.round((points - lo) / a.voxel).astype(int)
    occ[ijk[:, 2], ijk[:, 1], ijk[:, 0]] = True
    return ndimage.distance_transform_edt(~occ) * a.voxel


if a.loose_bones:
    from transplant_hands import led_by
    lv, lf, led = led_by(a.glb, a.loose_bones.split(","))
    dist = np.minimum(field(place(surface_points(lv, lf[~led], 4_000_000, rng))), field(place(surface_points(lv, lf[led], 1_000_000, rng))) / a.loose_factor)
else:
    dist = field(surf)
dist = dist.clip(max=0.25).astype(np.float16)
np.savez_compressed(a.out / "shell.npz", dist=dist, origin=lo.astype(np.float32), voxel=np.float32(a.voxel))
print(json.dumps({"points": len(pts), "spacing": round(spacing, 5), "grid_zyx": list(map(int, dist.shape)), "voxel": a.voxel,
                  "units_per_metre": round(s, 4), "voxel_mm": round(a.voxel / s * 1000, 2)}))
