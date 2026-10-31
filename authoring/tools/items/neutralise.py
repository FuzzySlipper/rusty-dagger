#!/usr/bin/env python3
"""Write a copy of a worn-item source tree with the dye-placeholder blue turned into pale grey.

Daggerfall clothing sprites are drawn in blue; the game tints them per dye. A neutral grey base converts
cleanly and tints later. Pixels whose blue clearly leads red and green become a pale warm grey, their luminance stretched
(1.127 x luminance + 21.5) so the shading stays readable; everything else is copied unchanged, as are the manifests.

    neutralise.py DF_ITEMS_DIR OUT_DIR ITEM_DIR...        e.g. mens-clothing/item-163-plain-robes
"""
import argparse, shutil
from pathlib import Path

import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("source"); ap.add_argument("out"); ap.add_argument("items", nargs="+")
a = ap.parse_args()
n = 0
for item in a.items:
    for src in sorted((Path(a.source) / item).rglob("*")):
        if not src.is_file():
            continue
        dst = Path(a.out) / src.relative_to(a.source)
        dst.parent.mkdir(parents=True, exist_ok=True)
        if "/player/" not in src.as_posix() or not src.name.endswith("-source.png"):
            shutil.copyfile(src, dst)
            continue
        im = np.array(Image.open(src).convert("RGBA")).astype(int)
        r, g, b = im[..., 0], im[..., 1], im[..., 2]
        blue = (b > r + 15) & (b > g) & (im[..., 3] > 0)
        lum = 1.127 * (0.299 * r + 0.587 * g + 0.114 * b) + 21.5
        im[..., 0] = np.where(blue, lum.round(), r)
        im[..., 1] = np.where(blue, (lum * 0.987).round(), g)
        im[..., 2] = np.where(blue, (lum * 0.972).round(), b)
        Image.fromarray(np.clip(im, 0, 255).astype("uint8"), "RGBA").save(dst)
        n += 1
print("neutralised", n, "images")
