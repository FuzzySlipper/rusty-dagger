#!/usr/bin/env python3
"""Register one splat onto another of the same figure with a similarity transform fitted on the torso and hood.

  python3 register_splats.py MOVING.npz FIXED.npz OUT_DIR [--torso-x 0.13] [--torso-y -0.05] [--front-z 0.0]
      [--head-y 0.30] [--trim 0.7]
      [--iterations 60] [--min-opacity 0.5] [--scale height|free|S] [--rotation yaw|full]

For two static splats of one character made from different orbit videos
(for example a cloaked and a bare version), whose frames differ by a rotation,
scale and shift and whose arms and legs differ slightly in pose. Both are in
the loop's frame: subject centred, about 1 tall, Y up, front toward +Z.

The fit uses only Gaussians above opacity MIN_OPACITY in the torso-and-hood
region, which should agree between the two: the front of the torso
(|x| < TORSO_X, y > TORSO_Y, z > FRONT_Z) and everything above HEAD_Y (hood
and shoulder mantle). Arms, hands, legs and the back below the shoulders,
where a cloak hangs in one splat and not the other, are left out. With the
back included, the extra cloth makes the fit slide forward and tilt a little
more on every iteration. It is a trimmed ICP with scale (Umeyama):
each iteration pairs every moving point with its nearest fixed point, keeps the
closest TRIM share of pairs (so cloth one splat has and the other lacks, such
as a cloak, does not pull the fit), and solves the rotation and shift.
Trimmed ICP with a free scale shrinks the moving splat (smaller is always
closer), so by default the scale is fixed first from the ratio of the two
figures' heights (0.1 to 99.9 percentile of y); --scale free solves it with
the rotation (Umeyama), and a number fixes it. The region is
re-selected in the moving splat's current pose at every iteration.

By default only the turn about the vertical axis is solved (--rotation yaw).
Both splats take their up axis from a level orbit of cameras, so they already
agree on it; a torso and hood alone cannot fix a forward or sideways tilt,
because tilting about a point near the head barely moves them, and a free
rotation drifts by several degrees and swings the feet centimetres apart.

OUT_DIR gets splat.npz (MOVING with means, scales and rotations transformed into
FIXED's frame) and registration.json: the transform, the trimmed RMS
before and after, and how far apart the hands and boots end up (centroid of
each side's hand and boot region in both splats; hands are |x| > HAND_X near
hip height, boots below BOOT_Y).
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation


def load(path):
    with np.load(path) as src:
        return {k: src[k] for k in src.files}


def opaque(splat, threshold):
    return 1 / (1 + np.exp(-splat['opacities'].ravel())) > threshold


def umeyama(src, dst, fixed_scale=None, yaw_only=False):
    mu_s, mu_d = src.mean(0), dst.mean(0)
    a, b = src - mu_s, dst - mu_d
    if yaw_only:   # rotation about +Y: x' = c x + s z, z' = -s x + c z
        sxx = (a[:, 0] * b[:, 0] + a[:, 2] * b[:, 2]).sum()
        sxz = (a[:, 2] * b[:, 0] - a[:, 0] * b[:, 2]).sum()
        t = np.arctan2(sxz, sxx)
        rot = np.array([[np.cos(t), 0, np.sin(t)], [0, 1, 0], [-np.sin(t), 0, np.cos(t)]])
        scale = fixed_scale if fixed_scale is not None else (b * (a @ rot.T)).sum() / (a ** 2).sum()
        return scale, rot, mu_d - scale * rot @ mu_s
    cov = b.T @ a / len(src)
    u, d, vt = np.linalg.svd(cov)
    s = np.eye(3)
    if np.linalg.det(u) * np.linalg.det(vt) < 0:
        s[2, 2] = -1
    rot = u @ s @ vt
    scale = np.trace(np.diag(d) @ s) / a.var(0).sum() if fixed_scale is None else fixed_scale
    return scale, rot, mu_d - scale * rot @ mu_s


def region(points, torso_x, torso_y, front_z=-1.0, head_y=1.0):
    return ((np.abs(points[:, 0]) < torso_x) & (points[:, 1] > torso_y) & (points[:, 2] > front_z)) | (points[:, 1] > head_y)


def part_centroids(points, hand_x, boot_y):
    out = {}
    for side, sign in (('+x', 1), ('-x', -1)):
        hand = (sign * points[:, 0] > hand_x) & (points[:, 1] > -0.2) & (points[:, 1] < 0.05)
        boot = (points[:, 1] < boot_y) & (sign * points[:, 0] > 0)
        out[f'hand{side}'] = points[hand].mean(0) if hand.any() else None
        out[f'boot{side}'] = points[boot].mean(0) if boot.any() else None
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('moving', type=Path)
    p.add_argument('fixed', type=Path)
    p.add_argument('out', type=Path)
    p.add_argument('--torso-x', type=float, default=0.13)
    p.add_argument('--torso-y', type=float, default=-0.05)
    p.add_argument('--front-z', type=float, default=0.0)
    p.add_argument('--head-y', type=float, default=0.30)
    p.add_argument('--trim', type=float, default=0.7)
    p.add_argument('--iterations', type=int, default=60)
    p.add_argument('--min-opacity', type=float, default=0.5)
    p.add_argument('--scale', default='height')
    p.add_argument('--rotation', choices=('yaw', 'full'), default='yaw')
    p.add_argument('--hand-x', type=float, default=0.17)
    p.add_argument('--boot-y', type=float, default=-0.40)
    a = p.parse_args()
    moving, fixed = load(a.moving), load(a.fixed)
    mv = moving['means'][opaque(moving, a.min_opacity)].astype(np.float64)
    fx = fixed['means'][opaque(fixed, a.min_opacity)].astype(np.float64)
    target = fx[region(fx, a.torso_x, a.torso_y, a.front_z, a.head_y)]
    tree = cKDTree(target)
    height = lambda pts: np.subtract(*np.percentile(pts[:, 1], [99.9, 0.1]))
    fixed_scale = None if a.scale == 'free' else height(fx) / height(mv) if a.scale == 'height' else float(a.scale)
    scale, rot, shift = fixed_scale or 1.0, np.eye(3), np.zeros(3)
    rms = []
    for _ in range(a.iterations):
        cur = scale * mv @ rot.T + shift
        sel = region(cur, a.torso_x, a.torso_y, a.front_z, a.head_y)
        dist, idx = tree.query(cur[sel])
        keep = dist <= np.quantile(dist, a.trim)
        rms.append(float(np.sqrt((dist[keep] ** 2).mean())))
        s, r, t = umeyama(mv[sel][keep], target[idx[keep]], fixed_scale, a.rotation == 'yaw')
        if abs(s - scale) < 1e-7 and np.abs(r - rot).max() < 1e-7 and np.abs(t - shift).max() < 1e-7:
            break
        scale, rot, shift = s, r, t
    out = dict(moving)
    out['means'] = (scale * moving['means'].astype(np.float64) @ rot.T + shift).astype(moving['means'].dtype)
    out['scales'] = (moving['scales'] + np.log(scale)).astype(moving['scales'].dtype)
    q = moving['quats'].astype(np.float64)   # w, x, y, z
    r_old = Rotation.from_quat(np.column_stack([q[:, 1:], q[:, :1]]))
    r_new = (Rotation.from_matrix(rot) * r_old).as_quat()
    out['quats'] = np.column_stack([r_new[:, 3:], r_new[:, :3]]).astype(moving['quats'].dtype)
    a.out.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(a.out / 'splat.npz', **out)
    reg = scale * mv @ rot.T + shift
    cm, cf = part_centroids(reg, a.hand_x, a.boot_y), part_centroids(fx, a.hand_x, a.boot_y)
    parts = {k: {'moving': None if cm[k] is None else [round(float(v), 4) for v in cm[k]],
                 'fixed': None if cf[k] is None else [round(float(v), 4) for v in cf[k]],
                 'apart': None if cm[k] is None or cf[k] is None else round(float(np.linalg.norm(cm[k] - cf[k])), 4)}
             for k in cm}
    euler = Rotation.from_matrix(rot).as_euler('yxz', degrees=True)
    report = {'moving': str(a.moving), 'fixed': str(a.fixed), 'scale_mode': a.scale, 'rotation_mode': a.rotation, 'scale': round(float(scale), 5),
              'rotation_yxz_degrees': [round(float(v), 3) for v in euler], 'shift': [round(float(v), 5) for v in shift],
              'matrix': (np.column_stack([scale * rot, shift])).round(6).tolist(),
              'trimmed_rms_first': round(rms[0], 5), 'trimmed_rms_last': round(rms[-1], 5), 'iterations': len(rms),
              'torso_points': int(region(fx, a.torso_x, a.torso_y, a.front_z, a.head_y).sum()), 'parts': parts}
    (a.out / 'registration.json').write_text(json.dumps(report, indent=1) + '\n')
    print(json.dumps({k: report[k] for k in ('scale', 'rotation_yxz_degrees', 'shift', 'trimmed_rms_first',
                                             'trimmed_rms_last', 'iterations')}))
    print(json.dumps({k: v['apart'] for k, v in parts.items()}))


if __name__ == '__main__':
    main()
