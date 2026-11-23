#!/usr/bin/env python3
"""Render camera rings of a trained splat as repaint controls (gsplat, GPU lab).

  python render_rings.py SPLAT.npz OUT --ring level:8 --ring sweep:-20,40 [--ring over:35,-15:40]
      [--frames 81] [--size 768] [--distance 2.7] [--fov 30] [--target 0 0 0] [--background 112 112 112] [--depth]
      [--height 0] [--closed]

Each ring is NAME:ELEVATION or NAME:LOW,HIGH, optionally followed by :AZIMUTH0.
A LOW,HIGH ring is at LOW on its first frame and HIGH half-way round. OUT/NAME
has the layout vace_ring.py and loop_dataset.py read: control/NNN.png (RGB on
the flat background), alpha/NNN.png, cameras.npz and ring.json. Cameras look
at TARGET with Y up and azimuth 0 on +Z. A narrow FOV around a part's centre
gives a close-up ring on exact cameras. --depth writes a depth control instead
of colour: near is white, the subject spans 0.25 to 1 and the background is
black, as control_ring_render.py does for a mesh, with pits from gaps between
discs filled from their surroundings. It also writes depth/NNN.png,
16-bit distance along the view axis in units of 1/20000 of the subject's height,
0 for background. --height makes portrait frames SIZE wide with FOV measured
vertically. --closed spaces the frames so the last repeats the first, which
puts frames on exact fractions of a turn: 73 frames step 5 degrees.
"""
import argparse
import json
import math
from pathlib import Path

from gsplat import rasterization
import numpy as np
from PIL import Image
import torch
import torch.nn.functional as F


def ring_cameras(frames, low, high, azimuth0, distance, fov, size, target, height=0, closed=False):
    views, elevations = [], []
    steps = frames - 1 if closed else frames
    for index in range(frames):
        elevation = low + (high - low) * (0.5 - 0.5 * math.cos(2 * math.pi * index / steps))
        elevations.append(elevation)
        e, a = math.radians(elevation), math.radians(azimuth0 + 360 * index / steps)
        offset = distance * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
        eye, forward = np.asarray(target) + offset, -offset / distance
        right = np.cross(forward, [0.0, 1.0, 0.0])
        right /= np.linalg.norm(right)
        rotation = np.stack([right, np.cross(forward, right), forward])
        view = np.eye(4)
        view[:3, :3] = rotation
        view[:3, 3] = -rotation @ eye
        views.append(view)
    height = height or size
    focal = (height / 2) / math.tan(math.radians(fov / 2))
    camera = np.array([[focal, 0, size / 2], [0, focal, height / 2], [0, 0, 1]])
    return np.array(views, np.float32), np.tile(camera, (frames, 1, 1)).astype(np.float32), elevations


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("splat", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--ring", action="append", required=True)
    parser.add_argument("--frames", type=int, default=81)
    parser.add_argument("--size", type=int, default=768)
    parser.add_argument("--distance", type=float, default=2.7)
    parser.add_argument("--fov", type=float, default=30)
    parser.add_argument("--target", type=float, nargs=3, default=[0.0, 0.0, 0.0])
    parser.add_argument("--background", type=int, nargs=3, default=[112, 112, 112])
    parser.add_argument("--depth", action="store_true")
    parser.add_argument("--height", type=int, default=0)
    parser.add_argument("--closed", action="store_true")
    args = parser.parse_args()

    with np.load(args.splat) as src:
        means, quats = (torch.from_numpy(src[key]).float().cuda() for key in ("means", "quats"))
        scales = torch.from_numpy(src["scales"]).float().cuda().exp()
        opacities = torch.from_numpy(src["opacities"]).float().cuda().sigmoid()
        colors = torch.from_numpy(src["rgb"]).float().cuda()
    background = torch.tensor(args.background, dtype=torch.float32, device="cuda") / 255

    for spec in args.ring:
        name, elevation, *rest = spec.split(":")
        low, high = (float(v) for v in elevation.split(",")) if "," in elevation else (float(elevation),) * 2
        azimuth0 = float(rest[0]) if rest else 0.0
        views, cameras, elevations = ring_cameras(
            args.frames, low, high, azimuth0, args.distance, args.fov, args.size, args.target, args.height, args.closed)
        height = args.height or args.size
        folder = args.out / name
        (folder / "control").mkdir(parents=True, exist_ok=True)
        (folder / "alpha").mkdir(exist_ok=True)
        if args.depth:
            (folder / "depth").mkdir(exist_ok=True)
        with torch.no_grad():
            for index in range(args.frames):
                image, alpha, _ = rasterization(
                    means, quats, scales, opacities, colors, torch.from_numpy(views[index:index + 1]).cuda(),
                    torch.from_numpy(cameras[index:index + 1]).cuda(), args.size, height, sh_degree=None,
                    packed=False, near_plane=0.01, far_plane=50.0, backgrounds=background[None],
                    render_mode="RGB+ED" if args.depth else "RGB",
                )
                if args.depth:
                    solid = alpha[0, ..., 0] > 0.5
                    depth = image[0, ..., 3]
                    # A gap between discs shows the far side as a pit; a repaint would draw it as a hole.
                    for _ in range(2):
                        around = F.unfold(depth[None, None], 9, padding=4).median(1).values.view_as(depth)
                        depth = torch.where(solid & (around > 0) & (depth - around > 0.004), around, depth)
                    near, far = depth[solid].min(), depth[solid].max()
                    shade = torch.where(solid, 0.25 + 0.75 * (far - depth) / (far - near), torch.zeros_like(depth))
                    metric = torch.where(solid, depth * 20000, torch.zeros_like(depth)).round().clamp(0, 65535)
                    Image.fromarray(metric.cpu().numpy().astype(np.uint16)).save(folder / "depth" / f"{index:03d}.png")
                    image = shade[None, ..., None].expand(-1, -1, -1, 3)
                Image.fromarray((image[0, ..., :3].clamp(0, 1) * 255).byte().cpu().numpy()).save(folder / "control" / f"{index:03d}.png")
                Image.fromarray((alpha[0, ..., 0].clamp(0, 1) * 255).byte().cpu().numpy()).save(folder / "alpha" / f"{index:03d}.png")
        np.savez_compressed(folder / "cameras.npz", viewmats=views, Ks=cameras)
        (folder / "ring.json").write_text(json.dumps({
            "size": [args.size, height], "frames": args.frames, "closed": args.closed, "elevation": elevations, "azimuth0": azimuth0,
            "fov_deg": args.fov, "distance": args.distance, "target": args.target, "depth": args.depth, "depth_units_per_height": 20000 if args.depth else None, "splat": str(args.splat),
        }, indent=1) + "\n")
        print("ring", name, f"{low:g}..{high:g} deg", flush=True)


if __name__ == "__main__":
    main()
