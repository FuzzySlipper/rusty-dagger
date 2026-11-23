#!/usr/bin/env python3
"""Measure how much each Gaussian of a splat shows from a set of cameras (gsplat, GPU lab).

  python visible_weight.py SPLAT.npz OUT.npz [--azimuths 90:270:15] [--elevations -30,-10,10,30,50,70]
      [--target 0,0,0] [--distance 2.7] [--fov 26] [--size 512] [--mask-red X,Y,Z:FOV]

For every camera it renders the splat and takes the gradient of the image
with respect to each Gaussian's colour, which is that Gaussian's blending
weight (opacity times transmittance) summed over the pixels it covers. OUT
gets `weight`, the largest per-camera weight of each Gaussian in pixels, and
`total`, the sum over cameras. A Gaussian with zero weight is never seen from
any of the cameras: inside a shell, or behind another surface.

Cameras sit on a sphere round TARGET, azimuth 0 on +Z (the front), as in
loop_review.py and render_rings.py: azimuths FROM:TO:STEP in degrees, each at
every elevation.

--mask-red X,Y,Z:FOV instead weighs only pixels of a close-up on X,Y,Z that
render saturated red (red over 0.45 and over 1.8 times green and blue), so
`weight` says which Gaussians make the red seen there, such as a pair of eyes
inside a hood.
"""
import argparse
import math

import numpy as np
import torch
from gsplat import rasterization


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
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('splat')
    p.add_argument('out')
    p.add_argument('--azimuths', default='90:270:15')
    p.add_argument('--elevations', default='-30,-10,10,30,50,70')
    p.add_argument('--target', default='0,0,0')
    p.add_argument('--distance', type=float, default=2.7)
    p.add_argument('--fov', type=float, default=26)
    p.add_argument('--size', type=int, default=512)
    p.add_argument('--mask-red')
    a = p.parse_args()
    with np.load(a.splat) as src:
        s = {k: torch.from_numpy(src[k]).float().cuda() for k in ('means', 'quats', 'scales', 'opacities', 'rgb')}
    n = len(s['means'])
    target, fov = [float(v) for v in a.target.split(',')], a.fov
    if a.mask_red:
        where, fov = a.mask_red.split(':')
        target, fov = [float(v) for v in where.split(',')], float(fov)
    lo, hi, step = [float(v) for v in a.azimuths.split(':')]
    azimuths = np.arange(lo, hi + 1e-6, step)
    elevations = [float(v) for v in a.elevations.split(',')]
    weight = torch.zeros(n, device='cuda')
    total = torch.zeros(n, device='cuda')
    for az in azimuths:
        for el in elevations:
            view, k = camera(target, az, el, a.distance, fov, a.size)
            colours = s['rgb'].clone().requires_grad_(True)
            image, _, _ = rasterization(s['means'], s['quats'], s['scales'].exp(), s['opacities'].reshape(-1).sigmoid(),
                                        colours, torch.from_numpy(view)[None].cuda(), torch.from_numpy(k)[None].cuda(),
                                        a.size, a.size, sh_degree=None, packed=False, near_plane=0.01, far_plane=50.0)
            image = image[0]
            if a.mask_red:
                with torch.no_grad():
                    r, g, b = image[..., 0], image[..., 1], image[..., 2]
                    mask = ((r > 0.45) & (r > 1.8 * g) & (r > 1.8 * b)).float()
                (image[..., 0] * mask).sum().backward()
                w = colours.grad[:, 0]
            else:
                image.sum().backward()
                w = colours.grad.sum(1) / 3
            weight = torch.maximum(weight, w)
            total += w
    np.savez_compressed(a.out, weight=weight.cpu().numpy(), total=total.cpu().numpy(),
                        cameras=np.array([(az, el) for az in azimuths for el in elevations]))
    w = weight.cpu().numpy()
    print({'gaussians': n, 'cameras': len(azimuths) * len(elevations), 'seen_over_0.05': int((w > 0.05).sum()),
           'seen_over_0.5': int((w > 0.5).sum()), 'unseen': int((w <= 1e-4).sum())})


if __name__ == '__main__':
    main()
