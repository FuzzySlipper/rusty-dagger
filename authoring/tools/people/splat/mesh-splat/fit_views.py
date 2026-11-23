#!/usr/bin/env python3
"""Find the camera that best lines each painted view up with a seed splat's silhouette.

  python fit_views.py SEED.npz VIEWS.json OUT.npz [--size 768] [--step 2.5] [--distance 6]
      [--elevations 0 4 8 12] [--points]

VIEWS.json lists {"name", "image", "azimuth": [LOW, HIGH]} with RGBA images
of one square size. For each view the script scans azimuth in its range and
the listed elevations, matches the rendered silhouette's bounding box to the
painting's by focal length and principal point, and keeps the camera with the
highest silhouette IoU. A row may give "turn": [LOW, HIGH] instead of
"azimuth": its range is then the previous row's fitted azimuth plus LOW to
HIGH, which follows the frames of an orbit video in order. Azimuth 0 looks at
the front from +Z, and positive azimuth moves the camera toward +X. OUT.npz
holds viewmats and Ks at the scan SIZE, with names, azimuths, elevations and
IoUs; the cameras are a starting point for the trainer's pose refinement.

The default silhouette is a gsplat render and needs the GPU lab. --points
draws the splat's centres as dots and closes the gaps between them instead,
which needs no GPU: use it for a dense surface seed such as mesh_seed.py's.
"""
import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image


def look_at(azimuth, elevation, distance):
    e, a = math.radians(elevation), math.radians(azimuth)
    eye = distance * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
    forward = -eye / distance
    right = np.cross(forward, [0.0, 1.0, 0.0])
    right /= np.linalg.norm(right)
    rotation = np.stack([right, np.cross(forward, right), forward])
    view = np.eye(4, dtype=np.float32)
    view[:3, :3] = rotation
    view[:3, 3] = -rotation @ eye
    return view


def box(mask):
    rows, cols = np.flatnonzero(mask.any(1)), np.flatnonzero(mask.any(0))
    return float(cols[0]), float(rows[0]), float(cols[-1]) + 1, float(rows[-1]) + 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("seed", type=Path)
    parser.add_argument("views", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--size", type=int, default=768)
    parser.add_argument("--step", type=float, default=2.5)
    parser.add_argument("--distance", type=float, default=6.0)
    parser.add_argument("--elevations", type=float, nargs="+", default=[0.0, 4.0, 8.0, 12.0])
    parser.add_argument("--points", action="store_true")
    args = parser.parse_args()
    size = args.size
    with np.load(args.seed) as src:
        splat = {key: src[key].astype(np.float32) for key in src.files}
    if args.points:
        from scipy.ndimage import maximum_filter, minimum_filter
    else:
        from gsplat import rasterization
        import torch
        splat = {key: torch.from_numpy(value).cuda() for key, value in splat.items()}

    def silhouette(view, camera):
        if args.points:
            seen = splat["means"] @ view[:3, :3].T + view[:3, 3]
            xy = np.rint(seen[:, :2] / seen[:, 2:3] * camera[0, 0] + camera[:2, 2]).astype(int)
            inside = (xy >= 0).all(1) & (xy < size).all(1)
            dots = np.zeros((size, size), bool)
            dots[xy[inside, 1], xy[inside, 0]] = True
            return minimum_filter(maximum_filter(dots, 7), 7)
        with torch.no_grad():
            _, alpha, _ = rasterization(
                splat["means"], splat["quats"], splat["scales"].exp(), splat["opacities"].sigmoid(), splat["rgb"],
                torch.from_numpy(view)[None].cuda(), torch.from_numpy(camera)[None].cuda(), size, size,
                sh_degree=None, packed=False, near_plane=0.01, far_plane=50.0,
            )
        return (alpha[0, ..., 0] > 0.5).cpu().numpy()

    start = 0.8 * size * args.distance
    views, cameras, report = [], [], []
    for row in json.loads(args.views.read_text()):
        image = Image.open(args.views.parent / row["image"]).convert("RGBA").resize((size, size), Image.LANCZOS)
        target = np.asarray(image)[..., 3] > 127
        tx0, ty0, tx1, ty1 = box(target)
        low, high = row["azimuth"] if "azimuth" in row else [report[-1]["azimuth"] + turn for turn in row["turn"]]
        best = (-1.0,)
        for azimuth in np.arange(low, high + 1e-6, args.step):
            for elevation in args.elevations:
                view = look_at(azimuth, elevation, args.distance)
                camera = np.array([[start, 0, size / 2], [0, start, size / 2], [0, 0, 1]], np.float32)
                x0, y0, x1, y1 = box(silhouette(view, camera))
                scale = (ty1 - ty0) / (y1 - y0)
                camera[0, 0] = camera[1, 1] = start * scale
                # After the focal change the box scales about the image centre; then move it onto the painting's.
                camera[0, 2] = size / 2 + (tx0 + tx1) / 2 - (size / 2 + ((x0 + x1) / 2 - size / 2) * scale)
                camera[1, 2] = size / 2 + (ty0 + ty1) / 2 - (size / 2 + ((y0 + y1) / 2 - size / 2) * scale)
                drawn = silhouette(view, camera)
                iou = float((drawn & target).sum() / (drawn | target).sum())
                if iou > best[0]:
                    best = (iou, float(azimuth), elevation, view, camera)
        views.append(best[3]); cameras.append(best[4])
        report.append({"name": row["name"], "azimuth": best[1], "elevation": best[2], "iou": round(best[0], 4)})
        print(report[-1], flush=True)
    np.savez(args.out, viewmats=np.array(views), Ks=np.array(cameras), names=[r["name"] for r in report],
             azimuths=[r["azimuth"] for r in report], elevations=[r["elevation"] for r in report],
             ious=[r["iou"] for r in report], size=size)


if __name__ == "__main__":
    main()
