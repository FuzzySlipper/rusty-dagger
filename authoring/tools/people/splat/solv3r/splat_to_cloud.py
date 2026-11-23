#!/usr/bin/env python3
"""Sample a coloured surface point cloud from a trained splat (gsplat, GPU lab).

  python splat_to_cloud.py SPLAT.npz OUT.ply [--size 1024] [--views 24] [--cell 0.0012]
      [--elevations -60 -30 0 30 60] [--min-alpha 0.97] [--max-step 0.004] [--min-facing 0]
      [--min-views 0 --agree 0.002]

Renders colour and expected depth from rings of known cameras, unprojects the
opaque pixels and merges them on a CELL-sized grid, averaging position and
colour. A pixel is dropped when its depth differs from a neighbour's by more
than MAX_STEP, which removes mixed samples along silhouettes. MIN_FACING drops
surface seen at a grazing angle, where blended depth is least reliable: it is
the least cosine between the depth map's normal and the viewing ray. With
MIN_VIEWS, a merged point survives only if that many views place their surface
within AGREE of it, and no more views than that see past it. Its colour then
becomes the mean of what those agreeing views show. This removes inner layers
and floaters and evens out colour that changes with direction. Every view comes
from the same splat, so the samples agree by construction. Units are the
splat's (subject height about 1), Y up. Writes binary PLY with xyz and RGB.
The report's `depth_disagreement` is the median distance between a view's kept
samples and the next view's depth map where both see the same surface (and
`_p90` its 90th percentile), a measure of how firm the splat's surface is.
"""
import argparse
import json
import math
from pathlib import Path

from gsplat import rasterization
import numpy as np
import torch
import torch.nn.functional as F


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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("splat", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--report-only", action="store_true", help="measure without writing the PLY")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--views", type=int, default=24, help="views per elevation ring")
    parser.add_argument("--elevations", type=float, nargs="+", default=[-60, -30, 0, 30, 60])
    parser.add_argument("--cell", type=float, default=0.0012)
    parser.add_argument("--min-alpha", type=float, default=0.97)
    parser.add_argument("--max-step", type=float, default=0.004)
    parser.add_argument("--min-facing", type=float, default=0.0)
    parser.add_argument("--min-views", type=int, default=0)
    parser.add_argument("--agree", type=float, default=0.002)
    parser.add_argument("--distance", type=float, default=2.7)
    parser.add_argument("--fov", type=float, default=26)
    args = parser.parse_args()

    with np.load(args.splat) as src:
        splat = {key: torch.from_numpy(src[key]).float().cuda() for key in src.files}
    focal = (args.size / 2) / math.tan(math.radians(args.fov / 2))
    intrinsics = torch.tensor([[focal, 0, args.size / 2], [0, focal, args.size / 2], [0, 0, 1]], device="cuda")
    rows, cols = torch.meshgrid(torch.arange(args.size, device="cuda"), torch.arange(args.size, device="cuda"), indexing="ij")
    rays = torch.stack([(cols + 0.5 - args.size / 2) / focal, (rows + 0.5 - args.size / 2) / focal, torch.ones_like(cols) * 1.0], -1)
    cameras = [(360 * i / args.views + 7.5 * ring, elevation) for ring, elevation in enumerate(args.elevations) for i in range(args.views)]
    cameras += [(0, 89), (0, -89)]
    points, colors, samples, previous, gaps, maps = [], [], 0, None, [], []
    with torch.no_grad():
        for azimuth, elevation in cameras:
            view = torch.from_numpy(look_at(azimuth, elevation, args.distance)).cuda()
            image, alpha, _ = rasterization(
                splat["means"], splat["quats"], splat["scales"].exp(), splat["opacities"].sigmoid(), splat["rgb"],
                view[None], intrinsics[None], args.size, args.size, sh_degree=None, packed=False,
                near_plane=0.01, far_plane=50.0, render_mode="RGB+ED",
            )
            depth, alpha = image[0, ..., 3], alpha[0, ..., 0]
            if args.min_views and not args.report_only:
                shown = (image[0, ..., :3] / alpha[..., None].clamp_min(0.5)).clamp(0, 1)
                maps.append((view, depth.clone(), alpha >= args.min_alpha, (shown * 255).byte()))
            near = -F.max_pool2d(-depth[None, None], 3, 1, 1)[0, 0]
            far = F.max_pool2d(depth[None, None], 3, 1, 1)[0, 0]
            keep = (alpha >= args.min_alpha) & (far - near <= args.max_step)
            if args.min_facing > 0:
                surface = rays * depth[..., None]
                across = F.pad(surface[:, 2:] - surface[:, :-2], (0, 0, 1, 1))
                down = F.pad(surface[2:] - surface[:-2], (0, 0, 0, 0, 1, 1))
                normal = F.normalize(torch.linalg.cross(across, down), dim=-1)
                keep &= (normal * F.normalize(surface, dim=-1)).sum(-1).abs() >= args.min_facing
            camera = rays[keep] * depth[keep][:, None]
            points.append((camera - view[:3, 3]) @ view[:3, :3])
            if previous is not None:
                # The previous view's samples, seen from this camera, against this view's depth map.
                seen = previous @ view[:3, :3].T + view[:3, 3]
                xy = (seen[:, :2] / seen[:, 2:3] * focal + args.size / 2).long()
                inside = (xy >= 0).all(1) & (xy < args.size).all(1)
                here = depth[xy[inside, 1], xy[inside, 0]]
                gap = (seen[inside, 2] - here).abs()[alpha[xy[inside, 1], xy[inside, 0]] >= args.min_alpha]
                gap = gap[gap < 0.02]
                if len(gap) > 1000:
                    gaps.append((float(gap.median()), float(gap.quantile(0.9))))
            previous = points[-1][::7]
            # Undo the blend with the black background for the kept, nearly opaque pixels.
            colors.append(image[0, ..., :3][keep] / alpha[keep][:, None])
            samples += int(keep.sum())
    vertices = []
    if not args.report_only:
        with torch.no_grad():
            points, colors = torch.cat(points), torch.cat(colors).clamp(0, 1)
            grid = torch.floor(points / args.cell).long()
            grid -= grid.min(0).values
            span = grid.max(0).values + 1
            keys = (grid[:, 0] * span[1] + grid[:, 1]) * span[2] + grid[:, 2]
            _, assignment, counts = torch.unique(keys, return_inverse=True, return_counts=True)
            merged = torch.zeros(len(counts), 6, device="cuda").index_add_(0, assignment, torch.cat([points, colors], 1))
            merged /= counts[:, None]
            merged_count = len(merged)
            if args.min_views:
                xyz = merged[:, :3]
                agree = torch.zeros(len(xyz), device="cuda")
                through = torch.zeros(len(xyz), device="cuda")
                shown = torch.zeros(len(xyz), 3, device="cuda")
                for view, depth, opaque, pixels in maps:
                    seen = xyz @ view[:3, :3].T + view[:3, 3]
                    xy = (seen[:, :2] / seen[:, 2:3] * focal + args.size / 2).long()
                    inside = (xy >= 0).all(1) & (xy < args.size).all(1)
                    x, y = xy[:, 0].clamp(0, args.size - 1), xy[:, 1].clamp(0, args.size - 1)
                    inside &= opaque[y, x]
                    offset = seen[:, 2] - depth[y, x]
                    on = inside & (offset.abs() <= args.agree)
                    agree += on
                    through += inside & (offset < -args.agree)
                    shown += pixels[y, x].float() / 255 * on[:, None]
                kept = (agree >= args.min_views) & (through <= agree)
                merged = torch.cat([xyz[kept], shown[kept] / agree[kept, None]], 1)
        vertices = np.empty(len(merged), dtype=[("xyz", "<f4", 3), ("rgb", "u1", 3)])
        vertices["xyz"] = merged[:, :3].cpu().numpy()
        vertices["rgb"] = (merged[:, 3:] * 255 + 0.5).byte().cpu().numpy()
        header = "ply\nformat binary_little_endian 1.0\n" + f"element vertex {len(vertices)}\n"
        header += "".join(f"property float {axis}\n" for axis in "xyz")
        header += "".join(f"property uchar {channel}\n" for channel in ("red", "green", "blue")) + "end_header\n"
        args.out.write_bytes(header.encode() + vertices.tobytes())
    report = {"splat": str(args.splat), "views": len(cameras), "size": args.size, "samples": samples,
              "points": len(vertices), "cell": args.cell, "min_alpha": args.min_alpha, "max_step": args.max_step,
              "min_facing": args.min_facing, "min_views": args.min_views, "agree": args.agree,
              "merged_before_agreement": merged_count if vertices is not None and len(vertices) else None,
              "depth_disagreement": round(float(np.median([g[0] for g in gaps])), 5),
              "depth_disagreement_p90": round(float(np.median([g[1] for g in gaps])), 5)}
    if not args.report_only:
        args.out.with_suffix(".json").write_text(json.dumps(report, indent=1) + "\n")
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
