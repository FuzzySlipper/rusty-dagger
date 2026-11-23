#!/usr/bin/env python3
"""Turn a fused Solv3R solve into round 0 of the splat repaint loop.

  python3 loop_data.py CLOUD_DIR INPUTS OUT [--size 768] [--grid 504] [--heldout 14] [--init 60000]
      [--min-overlap 0.5] [--overlap-size 768]

CLOUD_DIR comes from `fuse.py --cloud-only` (cameras.npz, cleaned-cloud.ply).
INPUTS holds images/ and masks/ under the names in cameras.npz. OUT uses the
layout that authoring/tools/people/splat/gsplat/train_loop_splat.py reads: RGBA images,
cameras.npz, init_points.npz and views.json. The solved training views become
anchors, whose poses the trainer refines. The first HELDOUT localized reserved
views become held-out views. The subject is moved so its bounding-box centre
sits at the origin, where the loop's camera rings look. Each view records the
IoU between the projected cloud and its subject mask. A training view under
MIN_OVERLAP has no usable camera and gets the role `dropped`. The overlap is
measured on an OVERLAP_SIZE grid (or SIZE, if smaller) whatever SIZE is: the
INIT projected points, dilated by 2 pixels, cover a dense matte at 768 but
only about half of it at 2176, where every view then scored about 0.5 and
good views were dropped.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import binary_dilation
from scipy.spatial import cKDTree


def read_cloud(path):
    data = Path(path).read_bytes()
    end = data.index(b"end_header\n") + len(b"end_header\n")
    count = int(next(line for line in data[:end].splitlines() if line.startswith(b"element vertex")).split()[-1])
    vertices = np.frombuffer(data, dtype=[("xyz", "<f4", 3), ("rgb", "u1", 3)], count=count, offset=end)
    return vertices["xyz"].astype(np.float32), vertices["rgb"].astype(np.float32) / 255


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cloud", type=Path)
    parser.add_argument("inputs", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--size", type=int, default=768)
    parser.add_argument("--grid", type=int, default=504, help="solver grid that the intrinsics refer to")
    parser.add_argument("--heldout", type=int, default=14)
    parser.add_argument("--init", type=int, default=60000)
    parser.add_argument("--min-overlap", type=float, default=0.5)
    parser.add_argument("--overlap-size", type=int, default=768,
                        help="grid of the cloud/mask overlap test (capped at SIZE), so it does not depend on SIZE")
    args = parser.parse_args()
    (args.out / "images").mkdir(parents=True, exist_ok=True)

    cameras = np.load(args.cloud / "cameras.npz")
    names, train_count = cameras["names"].tolist(), int(cameras["train_count"])
    keep = train_count + args.heldout
    poses, intrinsics = cameras["c2w"][:keep].astype(np.float64), cameras["K"][:keep].astype(np.float64)
    points, colors = read_cloud(args.cloud / "cleaned-cloud.ply")
    low, high = np.quantile(points, [0.005, 0.995], axis=0)
    center = (low + high) / 2
    points = points - center
    poses[:, :3, 3] -= center
    intrinsics[:, :2] *= args.size / args.grid
    viewmats = np.linalg.inv(poses)
    rng = np.random.default_rng(7)
    chosen = rng.choice(len(points), min(args.init, len(points)), replace=False)

    check = min(args.size, args.overlap_size)
    rows, overlaps, background = [], [], None
    for index, name in enumerate(names[:keep]):
        rgb = Image.open(args.inputs / "images" / name).convert("RGB").resize((args.size, args.size), Image.LANCZOS)
        mask = Image.open(args.inputs / "masks" / name).convert("L").resize((args.size, args.size), Image.BILINEAR)
        rgb.putalpha(mask)
        rgb.save(args.out / "images" / f"{index:03d}.png")
        pixels = np.asarray(rgb)
        if background is None:
            corners = np.concatenate([pixels[:8, :8, :3], pixels[:8, -8:, :3], pixels[-8:, :8, :3], pixels[-8:, -8:, :3]])
            background = np.median(corners.reshape(-1, 3), axis=0).round().astype(int).tolist()
        # The cloud should land on the subject in every view if the frames agree.
        camera = points[chosen] @ viewmats[index][:3, :3].T + viewmats[index][:3, 3]
        k = intrinsics[index].copy()
        k[:2] *= check / args.size
        image = camera @ k.T
        xy = np.rint(image[:, :2] / np.maximum(image[:, 2:3], 1e-6)).astype(int)
        inside = (xy >= 0).all(1) & (xy < check).all(1) & (camera[:, 2] > 0)
        covered = np.zeros((check, check), bool)
        covered[xy[inside, 1], xy[inside, 0]] = True
        subject = np.asarray(mask.resize((check, check), Image.BILINEAR)) > 127
        covered = binary_dilation(covered, iterations=2)
        overlaps.append(float((covered & subject).sum() / max((covered | subject).sum(), 1)))
        role = "heldout" if index >= train_count else "anchor" if overlaps[-1] >= args.min_overlap else "dropped"
        rows.append({"index": index, "name": name, "role": role, "cloud_iou": round(overlaps[-1], 3),
                     "background": background})

    spacing = float(np.median(cKDTree(points[chosen]).query(points[chosen], k=2)[0][:, 1]))
    np.savez_compressed(args.out / "init_points.npz", points=points[chosen], colors=colors[chosen], spacing=spacing)
    np.savez_compressed(args.out / "cameras.npz", viewmats=viewmats.astype(np.float32), Ks=intrinsics.astype(np.float32))
    report = {
        "source_cloud": str(args.cloud), "size": args.size, "overlap_size": check, "center_shift": center.tolist(),
        "extent": (high - low).tolist(), "init_spacing": spacing,
        "anchors": sum(row["role"] == "anchor" for row in rows),
        "dropped": [row["name"] for row in rows if row["role"] == "dropped"],
        "weak_heldout": [row["name"] for row in rows if row["role"] == "heldout" and row["cloud_iou"] < args.min_overlap],
        "views": rows,
    }
    (args.out / "views.json").write_text(json.dumps(report, indent=1) + "\n")
    print(json.dumps({key: report[key] for key in ("extent", "init_spacing", "anchors", "dropped", "weak_heldout")}))


if __name__ == "__main__":
    main()
