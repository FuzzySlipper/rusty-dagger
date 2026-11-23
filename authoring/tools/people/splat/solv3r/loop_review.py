#!/usr/bin/env python3
"""Render loop rounds from the same cameras: whole figure and close-ups (gsplat, GPU lab).

  python loop_review.py OUT_DIR LABEL=ROUND_DIR [LABEL=ROUND_DIR ...] [--tile 1080] [--orbit 96] [--orbit-swing 12]
      [--detail 'NAME:X,Y,Z:AZIMUTH:ELEVATION:FOV' ...]

Each ROUND_DIR holds a splat.npz from train_loop_splat.py. Rows are rounds.
`views.png` has four in-between azimuths at 8 degrees and a high and a low
quarter view. `steep.png` has three views from high above and three from far
below. `details.png` has the head, one hand, the feet and the rear hem. The close-up targets are measured once on the first round, so every
row uses identical cameras. --orbit N also writes orbit/NNN.png, the rounds
side by side on one turning camera whose elevation is 10 degrees plus or minus
ORBIT_SWING. Each --detail adds a close-up on a named point, for figures whose
parts the measured targets miss. Subject at the origin, Y up, front toward +Z.
"""
import argparse
import math
from pathlib import Path

from gsplat import rasterization
import numpy as np
from PIL import Image, ImageDraw
import torch

VIEWS = [(22.5, 8), (112.5, 8), (202.5, 8), (292.5, 8), (45, 40), (-40, -30)]
STEEP = [(0, 70), (120, 70), (240, 62), (0, -65), (120, -58), (240, -65)]


def camera(target, azimuth, elevation, distance, fov, size):
    e, a = math.radians(elevation), math.radians(azimuth)
    eye = np.asarray(target) + distance * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
    forward = np.asarray(target) - eye
    forward /= np.linalg.norm(forward)
    right = np.cross(forward, [0.0, 1.0, 0.0])
    right /= np.linalg.norm(right)
    rotation = np.stack([right, np.cross(forward, right), forward])
    view = np.eye(4, dtype=np.float32)
    view[:3, :3] = rotation
    view[:3, 3] = -rotation @ eye
    focal = (size / 2) / math.tan(math.radians(fov / 2))
    return view, np.array([[focal, 0, size / 2], [0, focal, size / 2], [0, 0, 1]], np.float32)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("out", type=Path)
    parser.add_argument("rounds", nargs="+")
    parser.add_argument("--tile", type=int, default=1080)
    parser.add_argument("--orbit", type=int, default=0)
    parser.add_argument("--orbit-swing", type=float, default=12)
    parser.add_argument("--detail", action="append", default=[])
    parser.add_argument("--background", type=int, nargs=3, default=[115, 115, 115])
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    background = torch.tensor(args.background, dtype=torch.float32, device="cuda") / 255
    rounds = [spec.split("=", 1) for spec in args.rounds]

    def load(folder):
        with np.load(Path(folder) / "splat.npz") as src:
            return {key: torch.from_numpy(src[key]).float().cuda() for key in src.files}

    first = load(rounds[0][1])
    solid = first["means"][first["opacities"].sigmoid() > 0.5].cpu().numpy()
    low, high = np.quantile(solid, [0.005, 0.995], axis=0)
    hand = solid[solid[:, 0] > high[0] - 0.12 * (high[0] - low[0])]
    hood = solid[solid[:, 1] > high[1] - 0.16 * (high[1] - low[1])]
    feet = solid[solid[:, 1] < low[1] + 0.07 * (high[1] - low[1])]
    details = [
        ("Head", np.median(hood, axis=0), 20, 5, 9),
        ("Hand", np.median(hand, axis=0), 15, 25, 6),
        ("Feet", np.median(feet, axis=0), 25, 20, 8),
        ("Rear hem", [0.0, low[1] + 0.33 * (high[1] - low[1]), 0.0], 180, 5, 13),
    ]
    for spec in args.detail:
        name, target, azimuth, elevation, fov = spec.split(":")
        details.append((name, [float(v) for v in target.split(",")], float(azimuth), float(elevation), float(fov)))
    sheets = {
        "views": [(f"{az:g} deg, {el:g} up", camera([0, 0, 0], az, el, 2.7, 26, args.tile)) for az, el in VIEWS],
        "steep": [(f"{az:g} deg, {el:g} up", camera([0, 0, 0], az, el, 2.7, 26, args.tile)) for az, el in STEEP],
        "details": [(name, camera(target, az, el, 2.7, fov, args.tile)) for name, target, az, el, fov in details],
    }
    def draw_splat(splat, view, intrinsics):
        with torch.no_grad():
            image, _, _ = rasterization(
                splat["means"], splat["quats"], splat["scales"].exp(), splat["opacities"].sigmoid(), splat["rgb"],
                torch.from_numpy(view)[None].cuda(), torch.from_numpy(intrinsics)[None].cuda(),
                args.tile, args.tile, sh_degree=None, packed=False, near_plane=0.01, far_plane=50.0,
                backgrounds=background[None],
            )
        return Image.fromarray((image[0].clamp(0, 1) * 255).byte().cpu().numpy())

    splats = [first] + [load(folder) for _, folder in rounds[1:]]
    for sheet_name, cameras in sheets.items():
        sheet = Image.new("RGB", (len(cameras) * args.tile, len(rounds) * args.tile))
        draw = ImageDraw.Draw(sheet)
        for row, (label, folder) in enumerate(rounds):
            splat = splats[row]
            for column, (name, (view, intrinsics)) in enumerate(cameras):
                sheet.paste(draw_splat(splat, view, intrinsics), (column * args.tile, row * args.tile))
                draw.text((column * args.tile + 16, row * args.tile + 12), f"{label}: {name}", fill="white", font_size=30)
        sheet.save(args.out / f"{sheet_name}.png")
        print("wrote", args.out / f"{sheet_name}.png", sheet.size, flush=True)
    if args.orbit:
        (args.out / "orbit").mkdir(exist_ok=True)
        for index in range(args.orbit):
            turn = 2 * math.pi * index / args.orbit
            view, intrinsics = camera([0, 0, 0], math.degrees(turn), 10 + args.orbit_swing * math.sin(turn), 2.7, 26, args.tile)
            frame = Image.new("RGB", (len(rounds) * args.tile, args.tile))
            draw = ImageDraw.Draw(frame)
            for column, (label, _) in enumerate(rounds):
                frame.paste(draw_splat(splats[column], view, intrinsics), (column * args.tile, 0))
                draw.text((column * args.tile + 16, 12), label, fill="white", font_size=30)
            frame.save(args.out / "orbit" / f"{index:03d}.png")
        print("wrote", args.orbit, "orbit frames", flush=True)


if __name__ == "__main__":
    main()
