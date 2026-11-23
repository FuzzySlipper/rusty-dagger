#!/usr/bin/env python3
"""Write the page's foot-tracking terms for a humanoid_splat_rig.py character's shoes.

  python3 shoe_track.py OUT-rig.npz OUT.json [--below M] [--skeleton ...]

The mean of a group of linearly skinned Gaussians is itself linear in the bone matrices:
mean_g(sum_b w_gb M_b p_g) = sum_b M_b (q_b, s_b) with q_b = mean_g(w_gb p_g) and s_b = mean_g(w_gb). So the
centre of each shoe as rendered (whatever its weights: per Gaussian, or one damped blend with the legs) is
tracked exactly from the bone matrices. --below M keeps only the Gaussians whose rest height is under M metres:
for a figure whose region under the hem is the whole lower leg (a knee-length skirt), so the point is the shoe. character.html (?track=OUT.json, or the manifest's "track") applies
sum_b world_b * inverseBind_b * (q_b, s_b) every frame, and Record feet logs it beside the ball and ankle bones.
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from humanoid_splat_rig import DEFAULT_SKELETON, load_skeleton  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('rig'); ap.add_argument('out'); ap.add_argument('--skeleton', default=DEFAULT_SKELETON)
    ap.add_argument('--below', type=float, help='only Gaussians whose rest height is under this (metres)')
    a = ap.parse_args()
    names = load_skeleton(a.skeleton)[0]
    r = np.load(a.rig)
    points = []
    for code, name in ((3, 'shoe_l'), (4, 'shoe_r')):
        keep = r['region'] == code
        if a.below is not None:
            keep &= r['rest_means'][:, 1] < a.below
        rows = np.where(keep)[0]
        if not len(rows):
            continue
        p = r['rest_means'][rows]; b = r['bones'][rows]; w = r['weights'][rows]
        terms = []
        for bone in np.unique(b[w > 0]):
            wb = np.where(b == bone, w, 0).sum(1)
            if wb.sum() <= 0:
                continue
            terms.append({'bone': names[bone], 'q': (wb[:, None] * p).mean(0).tolist(), 's': float(wb.mean())})
        points.append({'name': name, 'gaussians': int(len(rows)), 'terms': terms})
    json.dump({'rig': a.rig, 'points': points}, open(a.out, 'w'), indent=1)
    print(a.out, [(p['name'], p['gaussians'], len(p['terms'])) for p in points])


if __name__ == '__main__':
    main()
