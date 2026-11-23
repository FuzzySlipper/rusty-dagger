#!/usr/bin/env python3
"""Turn a Dagger pack's eight directional views into a splat training set.

  python3 anchor_dataset.py PACK_DIR OUT_DIR [--heldout front-right] [--elevation 8] [--fov 30] [--distance 3]
  python3 anchor_dataset.py FRAMES_DIR OUT_DIR --frames 16      (evenly spaced turnaround frames)

The views (PACK/views/body-<direction>.png, H3 turnaround frames on flat grey)
become anchors with nominal cameras: 45 degrees apart, one shared elevation,
looking at the origin. With --frames N, PACK_DIR is instead a turnaround's
frames/ directory and N evenly spaced frames become anchors at azimuth
360*i/N, the last-but-one quarter's frame held out. The cameras are only a prior; train_loop_splat.py
refines each anchor's pose. Conventions match orbit_render.py: Y up, OpenCV
camera axes, azimuth 0 = camera on +Z looking at a subject that faces +Z.
The turnaround runs front -> front-left -> left (face toward image left) ->
back..., so azimuth grows in that order.

Alpha is keyed against the grey background. A visual hull carved from the
silhouettes (a voxel survives if all but one view see it, with a few pixels of
dilation for pose error) seeds init_points.npz. views.json records each
image's role: anchor, or heldout (never trained on, scored every round).
"""
import argparse, json, math, os
import numpy as np
from PIL import Image, ImageFilter

DIRECTIONS = ['front', 'front-left', 'left', 'back-left', 'back', 'back-right', 'right', 'front-right']


def camera(az, el, dist, size, fov):
    e, a = math.radians(el), math.radians(az)
    pos = dist * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
    fwd = -pos / np.linalg.norm(pos)
    right = np.cross(fwd, [0.0, 1.0, 0.0]); right /= np.linalg.norm(right)
    down = np.cross(fwd, right)
    R = np.stack([right, down, fwd], 1)
    M = np.eye(4); M[:3, :3] = R.T; M[:3, 3] = -R.T @ pos
    f = (size / 2) / math.tan(math.radians(fov / 2))
    K = np.array([[f, 0, size / 2], [0, f, size / 2], [0, 0, 1]])
    return M, K


def key_alpha(rgb, soft=(10, 34)):
    corners = np.concatenate([rgb[:8, :8].reshape(-1, 3), rgb[:8, -8:].reshape(-1, 3), rgb[-8:, :8].reshape(-1, 3), rgb[-8:, -8:].reshape(-1, 3)])
    bg = np.median(corners, 0)
    d = np.abs(rgb.astype(np.float32) - bg).max(-1)
    a = np.clip((d - soft[0]) / (soft[1] - soft[0]), 0, 1)
    img = Image.fromarray((a * 255).astype(np.uint8))
    img = img.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))   # drop specks
    return np.asarray(img, np.float32) / 255, bg


def project(points, M, K):
    pc = points @ M[:3, :3].T + M[:3, 3]
    uv = pc[:, :2] / pc[:, 2:3] * [K[0, 0], K[1, 1]] + [K[0, 2], K[1, 2]]
    return uv


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('pack'); ap.add_argument('out')
    ap.add_argument('--heldout', default='front-right'); ap.add_argument('--elevation', type=float, default=8)
    ap.add_argument('--fov', type=float, default=30); ap.add_argument('--distance', type=float, default=3.0)
    ap.add_argument('--grid', type=int, default=112); ap.add_argument('--dilate', type=int, default=6)
    ap.add_argument('--frames', type=int, default=0)
    a = ap.parse_args()
    os.makedirs(a.out + '/images', exist_ok=True)
    viewmats, Ks, alphas, rgbs, rows = [], [], [], [], []
    if a.frames:
        files = sorted(f for f in os.listdir(a.pack) if f.endswith('.png'))
        picks = [round(i * len(files) / a.frames) for i in range(a.frames)]
        sources = [(f'frame-{k:03d}', f'{a.pack}/{files[k]}', 360 * i / a.frames) for i, k in enumerate(picks)]
        a.heldout = sources[a.frames * 7 // 8][0]
    else:
        sources = [(name, f'{a.pack}/views/body-{name}.png', 45 * i) for i, name in enumerate(DIRECTIONS)]
    for i, (name, path, az) in enumerate(sources):
        rgb = np.asarray(Image.open(path).convert('RGB'))
        size = rgb.shape[0]
        alpha, bg = key_alpha(rgb)
        Image.fromarray(np.concatenate([rgb, (alpha[..., None] * 255).astype(np.uint8)], -1), 'RGBA').save(f'{a.out}/images/{i:03d}.png')
        M, K = camera(az, a.elevation, a.distance, size, a.fov)
        viewmats.append(M); Ks.append(K); alphas.append(alpha); rgbs.append(rgb)
        rows.append({'index': i, 'name': name, 'azimuth': az, 'elevation': a.elevation,
                     'role': 'heldout' if name == a.heldout else 'anchor', 'source': path,
                     'background': bg.tolist()})
    np.savez_compressed(a.out + '/cameras.npz', viewmats=np.array(viewmats, np.float32), Ks=np.array(Ks, np.float32))

    # Visual hull over the frame's reach at the subject.
    half = a.distance * math.tan(math.radians(a.fov / 2)) * 1.05
    g = np.linspace(-half, half, a.grid)
    pts = np.stack(np.meshgrid(g, g, g, indexing='ij'), -1).reshape(-1, 3)
    votes = np.zeros(len(pts), int); col = np.zeros((len(pts), 3)); ncol = np.zeros(len(pts))
    for M, K, alpha, rgb in zip(viewmats, Ks, alphas, rgbs):
        loose = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * a.dilate + 1)), np.float32) / 255
        uv = np.rint(project(pts, M, K)).astype(int)
        ok = (uv[:, 0] >= 0) & (uv[:, 0] < rgb.shape[1]) & (uv[:, 1] >= 0) & (uv[:, 1] < rgb.shape[0])
        inside = np.zeros(len(pts), bool)
        inside[ok] = loose[uv[ok, 1], uv[ok, 0]] > 0.5
        votes += inside
        tight = np.zeros(len(pts), bool); tight[ok] = alpha[uv[ok, 1], uv[ok, 0]] > 0.5
        col[tight] += rgb[uv[tight, 1], uv[tight, 0]] / 255; ncol += tight
    keep = votes >= len(sources) - (2 if a.frames else 1)
    occ = keep.reshape(a.grid, a.grid, a.grid)
    # Surface voxels: occupied with at least one empty neighbour.
    pad = np.pad(occ, 1)
    full = np.ones_like(occ)
    for ax in range(3):
        for s in (-1, 1):
            full &= np.roll(pad, s, ax)[1:-1, 1:-1, 1:-1]
    surface = (occ & ~full).reshape(-1)
    p = pts[surface]; c = np.where(ncol[surface, None] > 0, col[surface] / np.maximum(ncol[surface, None], 1), 0.4)
    spacing = float(g[1] - g[0])
    np.savez_compressed(a.out + '/init_points.npz', points=p.astype(np.float32), colors=c.astype(np.float32), spacing=spacing)
    json.dump({'size': size, 'fov_deg': a.fov, 'distance': a.distance, 'elevation': a.elevation, 'views': rows,
               'hull': {'grid': a.grid, 'occupied': int(keep.sum()), 'surface_points': int(len(p)), 'spacing': spacing}},
              open(a.out + '/views.json', 'w'), indent=1)
    print(a.out, 'anchors', len(rows), 'hull voxels', int(keep.sum()), 'surface init', len(p))


if __name__ == '__main__':
    main()
