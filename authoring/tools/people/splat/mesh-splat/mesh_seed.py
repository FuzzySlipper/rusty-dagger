#!/usr/bin/env python3
"""Sample a triangle mesh into surface discs that seed a pinned splat.

  python3 mesh_seed.py MESH.ply OUT_DIR [--count 400000] [--axes yzx]

MESH.ply is a binary PLY of float vertices and triangle faces, as JoVox and
TRELLIS.2 write. AXES names the mesh axis that becomes each of X, Y and Z in
the loop's frame (Y up, front toward +Z); a JoVox mesh is Z up with its front
on +X, so `yzx`. The figure is centred on its bounding box and scaled to
height 1. OUT_DIR gets init_points.npz (points, normals, quats, colors, spacing) for
train_loop_splat.py --pin, and seed/splat.npz, clay-shaded discs that
render_rings.py, loop_review.py and fit_views.py can draw.
"""
import argparse
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation


def read_mesh(path):
    data = Path(path).read_bytes()
    end = data.index(b"end_header\n") + len(b"end_header\n")
    header = data[:end].decode().splitlines()
    vertices = int(next(line for line in header if line.startswith("element vertex")).split()[-1])
    faces = int(next(line for line in header if line.startswith("element face")).split()[-1])
    if sum(line.startswith("property float") for line in header) != 3:
        raise SystemExit("expected vertices with exactly x, y and z floats")
    points = np.frombuffer(data, "<f4", vertices * 3, end).reshape(-1, 3)
    triangles = np.frombuffer(data, np.dtype([("n", "u1"), ("i", "<i4", 3)]), faces, end + vertices * 12)
    if (triangles["n"] != 3).any():
        raise SystemExit("expected triangles only")
    return points.astype(np.float64), triangles["i"]


def disc_quats(normals):
    helper = np.where(np.abs(normals[:, 1:2]) < 0.9, [[0.0, 1.0, 0.0]], [[1.0, 0.0, 0.0]])
    first = np.cross(helper, normals)
    first /= np.linalg.norm(first, axis=1, keepdims=True)
    frame = np.stack([first, np.cross(normals, first), normals], axis=-1)
    return Rotation.from_matrix(frame).as_quat(scalar_first=True).astype(np.float32)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mesh", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--count", type=int, default=400000)
    parser.add_argument("--axes", default="yzx")
    args = parser.parse_args()
    (args.out / "seed").mkdir(parents=True, exist_ok=True)

    points, triangles = read_mesh(args.mesh)
    points = points[:, ["xyz".index(axis) for axis in args.axes]]
    low, high = points.min(0), points.max(0)
    points = (points - (low + high) / 2) / (high[1] - low[1])
    a, b, c = (points[triangles[:, k]] for k in range(3))
    cross = np.cross(b - a, c - a)
    area = np.linalg.norm(cross, axis=1) / 2
    rng = np.random.default_rng(7)
    chosen = rng.choice(len(triangles), args.count, p=area / area.sum())
    u, v = rng.random((2, args.count))
    flip = u + v > 1
    u[flip], v[flip] = 1 - u[flip], 1 - v[flip]
    samples = a[chosen] + u[:, None] * (b[chosen] - a[chosen]) + v[:, None] * (c[chosen] - a[chosen])
    normals = cross[chosen] / np.maximum(2 * area[chosen, None], 1e-12)
    spacing = float(np.sqrt(area.sum() / args.count))
    clay = 0.30 + 0.55 * np.abs(normals @ np.array([0.35, 0.45, 0.82]))
    np.savez_compressed(args.out / "init_points.npz", points=samples.astype(np.float32), normals=normals.astype(np.float32),
                        quats=disc_quats(normals), colors=np.full((args.count, 3), 0.5, np.float32), spacing=spacing)
    radius = np.log(0.9 * spacing)
    np.savez_compressed(
        args.out / "seed" / "splat.npz", means=samples.astype(np.float32), quats=disc_quats(normals),
        scales=np.tile(np.array([radius, radius, np.log(1e-4)], np.float32), (args.count, 1)),
        opacities=np.full(args.count, np.log(0.95 / 0.05), np.float32),
        rgb=np.repeat(clay[:, None], 3, axis=1).astype(np.float32),
    )
    print(f"{len(triangles)} triangles, area {area.sum():.3f}, {args.count} discs, spacing {spacing:.5f}, "
          f"extent {np.ptp(samples, axis=0).round(3).tolist()}")


if __name__ == "__main__":
    main()
