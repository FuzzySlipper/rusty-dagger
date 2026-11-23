#!/usr/bin/env python3
"""Copy a train_loop_splat.py dataset with its seed cut down to a Gaussian budget.

  python3 budget_seed.py DATA_DIR SPLAT.npz OUT_DIR --count 47200 [--seed 0] [--link] [--region MASK.npy --share 0.25]

train_loop_splat.py's --cap only limits MCMC growth; it never shrinks the seed.
A dataset whose init_points.npz holds 355k points therefore cannot be refitted
at 48k or 16k as it stands. This writes OUT_DIR with the same images, cameras
and views.json (symlinked with --link, copied otherwise) and a new
init_points.npz of COUNT points drawn without replacement from a fitted
SPLAT.npz, weighted by opacity, coloured with that splat's RGB. The seed
spacing grows with the square root of the reduction, so the starting
Gaussians cover the surface the dense fit covered.

--region MASK.npy --share S gives one part of the figure a set share of the
budget (a face on a small body): MASK is one boolean per Gaussian of SPLAT.npz,
round(S * COUNT) points are drawn inside it and the rest outside, each by
opacity. Without it the draw is as before.
"""
import argparse
import json
import shutil
from pathlib import Path

import numpy as np


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('data', type=Path)
    ap.add_argument('splat', type=Path)
    ap.add_argument('out', type=Path)
    ap.add_argument('--count', type=int, required=True)
    ap.add_argument('--seed', type=int, default=0)
    ap.add_argument('--link', action='store_true', help='symlink images instead of copying them')
    ap.add_argument('--region', type=Path, help='boolean mask over the splat: the part that gets --share of the budget')
    ap.add_argument('--share', type=float, default=0.25)
    a = ap.parse_args()

    a.out.mkdir(parents=True, exist_ok=True)
    for name in ('cameras.npz', 'views.json'):
        shutil.copy2(a.data / name, a.out / name)
    images = a.out / 'images'
    if images.exists() or images.is_symlink():
        if images.is_symlink() or images.is_file():
            images.unlink()
        else:
            shutil.rmtree(images)
    if a.link:
        images.symlink_to((a.data / 'images').resolve())
    else:
        shutil.copytree(a.data / 'images', images)

    s = np.load(a.splat)
    opacity = 1 / (1 + np.exp(-s['opacities'].astype(np.float64)))
    rng = np.random.default_rng(a.seed)
    region = None
    if a.region:
        region = np.load(a.region).astype(bool)
        if region.shape != opacity.shape:
            raise SystemExit(f'{a.region}: {region.shape} entries for {opacity.shape} Gaussians')
        k = int(round(a.share * a.count))
        inside, outside = np.flatnonzero(region), np.flatnonzero(~region)
        draw = lambda idx, m: rng.choice(idx, size=m, replace=False, p=opacity[idx] / opacity[idx].sum())
        pick = np.concatenate([draw(inside, k), draw(outside, a.count - k)])
    else:
        pick = rng.choice(len(opacity), size=a.count, replace=False, p=opacity / opacity.sum())
    old = np.load(a.data / 'init_points.npz')
    spacing = float(old['spacing']) * np.sqrt(len(old['points']) / a.count)
    np.savez(a.out / 'init_points.npz', points=s['means'][pick].astype(np.float32),
             colors=np.clip(s['rgb'][pick], 0, 1).astype(np.float32), spacing=np.float64(spacing))
    report = {'data': str(a.data), 'splat': str(a.splat), 'count': a.count, 'from': int(len(opacity)),
              'spacing': spacing, 'mean_seed_opacity': float(opacity[pick].mean())}
    if region is not None:
        report['region'] = {'mask': str(a.region), 'share': a.share, 'points': int(region[pick].sum()),
                            'opacity_share_in_source': float(opacity[region].sum() / opacity.sum())}
    (a.out / 'seed.json').write_text(json.dumps(report, indent=1))
    print(json.dumps(report))


if __name__ == '__main__':
    main()
