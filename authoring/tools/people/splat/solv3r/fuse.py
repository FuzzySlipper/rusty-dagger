#!/usr/bin/env python3
"""Fuse foreground samples supported by other Solv3R views, with optional splat seeds."""
import argparse
import json
from pathlib import Path
from time import perf_counter

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation

COUNT = 48_000


def load_views(inputs, names, size):
    colors, masks = [], []
    for name in names:
        with Image.open(inputs / "images" / name) as image:
            colors.append(np.asarray(image.convert("RGB").resize(size, Image.Resampling.LANCZOS)))
        with Image.open(inputs / "masks" / name) as mask:
            masks.append(np.asarray(mask.convert("L").resize(size, Image.Resampling.NEAREST)) > 127)
    return np.stack(colors), np.stack(masks)


def camera_space(points, pose):
    return (points - pose[:3, 3]) @ pose[:3, :3]


def unit(v):
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-8)


def project(camera, intrinsics):
    image = camera @ intrinsics.T
    return image[:, :2] / np.maximum(image[:, 2:3], 1e-8)


def common_frame(points, mask, poses, base_count=None, up_from="cameras"):
    base_count = len(points) if base_count is None else base_count
    foreground = points[:base_count][mask[:base_count]]
    center = np.median(foreground, axis=0)
    up = unit(-np.median(poses[:base_count, :3, 1], axis=0))
    if up_from == "figure":
        # A standing figure's long axis, signed by the cameras' up. The median camera up leans on paths that rise on one side and dip
        # on the other (about 30 degrees on a +70/-60 cycle, 2026-11-14); on a flat orbit the two agree within a degree.
        sample = foreground[:: max(1, len(foreground) // 200000)] - center
        axis = np.linalg.eigh(np.cov(sample.T))[1][:, -1]
        up = unit(axis * (np.sign(axis @ up) or 1.0))
    front = poses[0, :3, 3] - center
    front = unit(front - up * (front @ up))
    right = unit(np.cross(up, front))
    basis = np.stack([right, up, front])
    height = np.diff(np.quantile((foreground - center) @ up, [0.01, 0.99])).item()
    transformed = poses.copy()
    transformed[:, :3, :3] = basis @ poses[:, :3, :3]
    transformed[:, :3, 3] = (poses[:, :3, 3] - center) @ basis.T / height
    return (points - center) @ basis.T / height, transformed


def supported_samples(points, poses, intrinsics, masks, confidence, rgb, tolerance,
                      reference_count=None, reference_limit=None, sample_limit=None, added_tolerance=None,
                      pixel_scale=1.0):
    count, h, w = masks.shape
    reference_count = count if reference_count is None else reference_count
    outside = [distance_transform_edt(~mask) > 5 * pixel_scale for mask in masks[:reference_count]]
    z_maps = [camera_space(p, pose)[..., 2].copy() for p, pose in zip(points[:reference_count], poses)]
    look_dirs = unit(poses[:count, :3, 3])
    kept, colors, quality, reports = [], [], [], []
    for i in range(count):
        agreement = added_tolerance if added_tolerance is not None and i >= reference_count else tolerance
        rows, cols = np.nonzero(masks[i])
        foreground_count = len(rows)
        if sample_limit and foreground_count > sample_limit:
            selected = np.random.default_rng(7 + i).choice(foreground_count, sample_limit, replace=False)
            rows, cols = rows[selected], cols[selected]
        p = points[i, rows, cols]
        conf = confidence[i, rows, cols]
        camera = camera_space(p, poses[i])
        own_error = np.linalg.norm(project(camera, intrinsics[i]) - np.stack([cols, rows], axis=1), axis=1)
        sampled_count = len(p)
        # Only locally consistent samples can receive votes from other views.
        eligible = (camera[:, 2] > 0) & (own_error < 8 * pixel_scale) & (conf >= np.quantile(conf, 0.1))
        rows, cols, p, conf = rows[eligible], cols[eligible], p[eligible], conf[eligible]
        votes = np.zeros(len(p), dtype=np.int16)
        conflicts = np.zeros_like(votes)
        similarity = look_dirs[:reference_count] @ look_dirs[i]
        references = [j for j in range(reference_count) if j != i and -0.35 < similarity[j] < 0.993]
        if reference_limit and len(references) > reference_limit:
            references = sorted(references, key=lambda j: similarity[j], reverse=True)[:reference_limit]
        for j in references:
            camera_j = camera_space(p, poses[j])
            xy = project(camera_j, intrinsics[j])
            x, y = np.rint(xy).T
            valid = (camera_j[:, 2] > 0) & (x >= 0) & (x < w) & (y >= 0) & (y < h)
            # Rays outside the camera can project far beyond the integer range.
            x = np.where(valid, x, 0).astype(np.int32)
            y = np.where(valid, y, 0).astype(np.int32)
            delta = camera_j[:, 2] - z_maps[j][y, x]
            votes += valid & masks[j][y, x] & (np.abs(delta) <= agreement)
            conflicts += valid & (outside[j][y, x] | (masks[j][y, x] & (delta < -2 * agreement)))
        minimum_votes = 2 if added_tolerance is not None and i >= reference_count else 1
        accept = (votes >= minimum_votes) & (votes >= conflicts)
        kept.append(p[accept])
        colors.append(rgb[i, rows[accept], cols[accept]] / 255.0)
        quality.append((np.log1p(conf[accept]) * (votes[accept] + 1) / (conflicts[accept] + 1)).astype(np.float32))
        reports.append({
            "foreground": foreground_count,
            "sampled": sampled_count,
            "supported": int(accept.sum()),
            "own_reprojection_median_px": float(np.median(own_error)),
            "own_reprojection_p90_px": float(np.quantile(own_error, 0.9)),
        })
    return np.concatenate(kept), np.concatenate(colors), np.concatenate(quality), reports


def merge_voxels(points, colors, quality, cell):
    grid = np.floor(points / cell).astype(np.int32)
    unique, assignment = np.unique(grid, axis=0, return_inverse=True)
    weights = np.bincount(assignment, weights=quality)
    values = np.column_stack([points, colors])
    merged = np.stack(
        [np.bincount(assignment, weights=column * quality) / weights for column in values.T],
        axis=1,
    )
    return merged[:, :3], merged[:, 3:], len(unique)


def seed_cloud(points, colors):
    rng = np.random.default_rng(7)
    n = len(points)
    # If fewer distinct samples survive, split them over their local tangent
    # planes to reach the count without repeating coincident centers.
    indices = rng.choice(n, min(n, COUNT), replace=False)
    if len(indices) < COUNT:
        indices = np.concatenate([indices, rng.choice(n, COUNT - n, replace=True)])
    centers = points[indices].copy()
    distances, nearby = cKDTree(points).query(centers, k=min(12, n), workers=2)
    offsets = points[nearby] - centers[:, None]
    covariance = np.einsum("nki,nkj->nij", offsets, offsets) / offsets.shape[1]
    _, axes = np.linalg.eigh(covariance)
    axis_x, axis_z = axes[:, :, 2], axes[:, :, 0]
    frame = np.stack([axis_x, np.cross(axis_z, axis_x), axis_z], axis=-1)
    quats = Rotation.from_matrix(frame).as_quat(scalar_first=True)
    tangent = (0.8 * distances[:, -1]).clip(0.0018, 0.011)
    if n < COUNT:
        jitter = rng.normal(0, 0.22, (COUNT - n, 2)) * tangent[n:, None]
        centers[n:] += np.einsum("nij,nj->ni", frame[n:, :, :2], jitter)
    thickness = (tangent * 0.26).clip(0.0006, 0.0026)
    return {
        "means": centers.astype(np.float32),
        "scales": np.log(np.stack([tangent, tangent, thickness], axis=1)).astype(np.float32),
        "quats": quats.astype(np.float32),
        "opacities": np.full(COUNT, np.log(0.80 / 0.20), dtype=np.float32),
        "rgb": colors[indices].astype(np.float32),
    }


def save_cloud(path, points, colors):
    vertices = np.empty(len(points), dtype=[("xyz", "<f4", 3), ("rgb", "u1", 3)])
    vertices["xyz"] = points
    vertices["rgb"] = np.rint(colors.clip(0, 1) * 255).astype(np.uint8)
    fields = "".join(f"property float {axis}\n" for axis in "xyz")
    fields += "".join(f"property uchar {channel}\n" for channel in ("red", "green", "blue"))
    with path.open("wb") as stream:
        stream.write(
            f"ply\nformat binary_little_endian 1.0\nelement vertex {len(points)}\n{fields}end_header\n".encode()
        )
        stream.write(vertices.tobytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("solve", type=Path)
    parser.add_argument("inputs", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--tolerance", type=float, default=0.025, help="depth agreement as a fraction of subject height")
    parser.add_argument("--appearance-size", type=int, help="long side of the RGB bake, defaulting to the solve size")
    parser.add_argument("--cloud-only", action="store_true", help="write the colored cloud and cameras without splat seeds")
    parser.add_argument("--reference-limit", type=int, help="check each view against this many nearby camera directions")
    parser.add_argument("--sample-limit", type=int, help="sample at most this many foreground pixels per view")
    parser.add_argument("--support-views", type=int, help="use only this many first views to support added samples")
    parser.add_argument("--added-tolerance", type=float, help="depth tolerance for added views, requiring two supporting views")
    parser.add_argument("--pixel-scale", type=float, help="override pixel margins, which grow above 504px")
    parser.add_argument("--up", choices=["cameras", "figure"], default="cameras",
                        help="the common frame's up: the median camera up (default), or a standing figure's long axis - use figure for sweeps")
    args = parser.parse_args()
    started = perf_counter()
    args.out.mkdir(parents=True, exist_ok=True)
    with np.load(args.solve) as solve:
        names, points, confidence = solve["names"], solve["points"], solve["confidence"]
        poses, intrinsics = solve["c2w"], solve["K"]
        base_count = int(solve["base_count"]) if "base_count" in solve else len(points)
        # Registered crops can add samples without voting against the wide cloud.
        anchor_support_only = "anchor_support_only" in solve and bool(solve["anchor_support_only"])
        reference_count = base_count if anchor_support_only else None
        if args.support_views:
            reference_count = min(args.support_views, len(points))
    n, h, w = confidence.shape
    pixel_scale = args.pixel_scale if args.pixel_scale is not None else max(1.0, max(h, w) / 504)
    rgb, masks = load_views(args.inputs, names, (w, h))
    points, poses = common_frame(points, masks[:n] & np.isfinite(points).all(axis=-1), poses, base_count, args.up)
    valid = masks[:n] & np.isfinite(points).all(axis=-1) & np.isfinite(confidence)
    raw, color, quality, reports = supported_samples(
        points, poses, intrinsics, valid, confidence, rgb, args.tolerance, reference_count,
        args.reference_limit, args.sample_limit,
        args.added_tolerance, pixel_scale,
    )
    summary = {
        "solve": args.solve.name,
        "tolerance": args.tolerance,
        "reference_limit": args.reference_limit,
        "sample_limit": args.sample_limit,
        "support_views": reference_count,
        "added_tolerance": args.added_tolerance,
        "pixel_scale": pixel_scale,
        "views": {str(name): report for name, report in zip(names, reports)},
        "camera_yaw_deg": np.degrees(np.unwrap(np.arctan2(poses[:base_count, 0, 3], poses[:base_count, 2, 3]))).round(1).tolist(),
        "supported_samples": len(raw),
    }
    minimum = 1 if args.cloud_only else 12
    if len(raw) < minimum:
        (args.out / "fusion.json").write_text(json.dumps(summary, indent=2))
        parser.exit(1, "Too few supported points for the requested output. See fusion.json.\n")
    cloud, cloud_rgb, count = merge_voxels(raw, color, quality, 0.003)
    summary["fused_points"] = count
    save_cloud(args.out / "cleaned-cloud.ply", cloud, cloud_rgb)
    if args.cloud_only:
        np.savez(args.out / "cameras.npz", names=names, train_count=n, c2w=poses, K=intrinsics)
        summary["fusion_seconds"] = perf_counter() - started
        (args.out / "fusion.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(f"{count:,} fused points in {summary['fusion_seconds']:.2f} s", flush=True)
        return
    seeds = seed_cloud(cloud, cloud_rgb)
    np.savez_compressed(args.out / "seed.npz", **seeds)
    if args.appearance_size:
        scale = args.appearance_size / max(h, w)
        bake_w, bake_h = round(w * scale), round(h * scale)
        rgb, masks = load_views(args.inputs, names, (bake_w, bake_h))
        # Preserve the same rays when resizing from the solver's pixel grid.
        intrinsics = intrinsics.copy()
        intrinsics[:, 0] *= bake_w / w
        intrinsics[:, 1] *= bake_h / h
    summary["appearance_size"] = list(rgb.shape[1:3][::-1])
    np.savez_compressed(
        args.out / "observations.npz", names=names, train_count=n, rgb=rgb,
        masks=masks, viewmats=np.linalg.inv(poses).astype(np.float32), Ks=intrinsics,
    )
    (args.out / "fusion.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps({key: val for key, val in summary.items() if key != "views"}))


if __name__ == "__main__":
    main()
