#!/usr/bin/env python3
"""Remove small detached bits from a transparent final: connected alpha regions much smaller than the largest.

    despeckle.py FINAL.png [FINAL.png ...] [--min-fraction 0.02]

A region is kept when its pixel count is at least `--min-fraction` of the largest region's. Soft alpha
under 8/255 is ignored when finding regions. Use it on a final whose generation left floating flecks;
effects that are meant to carry embers and sparks should not be despeckled.
"""
import argparse
from collections import deque
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("files", nargs="+")
ap.add_argument("--min-fraction", type=float, default=0.02)
args = ap.parse_args()
for path in args.files:
    im = np.array(Image.open(path).convert("RGBA"))
    solid = im[..., 3] >= 8
    label = np.zeros(solid.shape, np.int32)
    sizes = []
    for y, x in zip(*np.nonzero(solid)):
        if label[y, x]:
            continue
        sizes.append(0)
        n = len(sizes)
        label[y, x] = n
        queue = deque([(y, x)])
        while queue:
            cy, cx = queue.popleft()
            sizes[-1] += 1
            for ny in range(max(cy - 1, 0), min(cy + 2, solid.shape[0])):
                for nx in range(max(cx - 1, 0), min(cx + 2, solid.shape[1])):
                    if solid[ny, nx] and not label[ny, nx]:
                        label[ny, nx] = n
                        queue.append((ny, nx))
    keep = [i + 1 for i, s in enumerate(sizes) if s >= args.min_fraction * max(sizes)]
    # Soft alpha next to a dropped region goes with it; everything else is untouched.
    drop = solid & ~np.isin(label, keep)
    grown = np.array(Image.fromarray(drop.astype(np.uint8) * 255).filter(__import__("PIL.ImageFilter", fromlist=["x"]).MaxFilter(5))) > 0
    im[grown & ~np.isin(label, keep), 3] = 0
    Image.fromarray(im, "RGBA").save(path)
    print(f"{path}: kept {len(keep)} of {len(sizes)} regions")
