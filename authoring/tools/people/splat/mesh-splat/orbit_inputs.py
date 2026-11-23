#!/usr/bin/env python3
"""Turn orbit_views.py's keyed views into Solv3R inputs and a train/reserved split.

  python3 orbit_inputs.py VIEWS_DIR OUT [--reserve-every 8] [--reserve-offset 5]

VIEWS_DIR is orbit_views.py's OUT/views (NNN.png, RGBA). OUT gets
images/capture_NNNN.png (the frame's RGB), masks/capture_NNNN.png (its alpha
as 0/255) and split.json with every RESERVE_EVERY-th frame from
RESERVE_OFFSET reserved and the rest for training. The defaults reproduce the
townswoman 453 orbit run: 72 frames, nine reserved (5, 13, ..., 69).
solve.py reads OUT/images and --split OUT/split.json; fuse.py and loop_data.py
read OUT (images/ and masks/).
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('views', type=Path)
    p.add_argument('out', type=Path)
    p.add_argument('--reserve-every', type=int, default=8)
    p.add_argument('--reserve-offset', type=int, default=5)
    a = p.parse_args()
    (a.out / 'images').mkdir(parents=True, exist_ok=True)
    (a.out / 'masks').mkdir(parents=True, exist_ok=True)
    files = sorted(a.views.glob('[0-9]*.png'))
    for i, f in enumerate(files):
        im = Image.open(f).convert('RGBA')
        im.convert('RGB').save(a.out / 'images' / f'capture_{i:04d}.png')
        alpha = (np.asarray(im.getchannel('A')) > 127).astype(np.uint8) * 255
        Image.fromarray(alpha, 'L').save(a.out / 'masks' / f'capture_{i:04d}.png')
    reserved = [i for i in range(len(files)) if i % a.reserve_every == a.reserve_offset % a.reserve_every]
    train = [i for i in range(len(files)) if i not in reserved]
    (a.out / 'split.json').write_text(json.dumps({'train': train, 'reserved': reserved}))
    print(json.dumps({'frames': len(files), 'train': len(train), 'reserved': reserved}))


if __name__ == '__main__':
    main()
