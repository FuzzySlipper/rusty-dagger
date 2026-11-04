#!/usr/bin/env python3
"""Stands a shield's refreshed paper-doll layer square to the view, as a turnaround stands a weapon's icon.

  python3 authoring/tools/weapons/shield.py NAME OUT.png

The paper doll draws a shield turned a little from the front. A shield's `face` in authoring/specs/weapons/weapons.json
is how wide it is in metres, so its layer is cropped and stretched to its true width for its `length`, which build.py
then reads as any icon. `mirror` makes a whole shield of a layer that shows only its near half, turned about its left edge.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[3]
HEIGHT = 1024
RIM = (46, 43, 41, 255)


def closed(image):
    """A mirrored layer with the gaps its edge leaves down the join filled in the rim's dark steel."""
    pixels = np.array(image)
    for row in pixels:
        solid = np.nonzero(row[:, 3] > 128)[0]
        if solid.size:
            gap = row[solid[0]:solid[-1] + 1]
            gap[gap[:, 3] <= 128] = RIM
    return Image.fromarray(pixels)


def main():
    name, out = sys.argv[1], Path(sys.argv[2])
    shield = next(w for w in json.loads((ROOT / 'authoring/specs/weapons/weapons.json').read_text())['weapons'] if w['name'] == name)
    layer = Image.open(ROOT / f'authoring/gen-raw/worn/texture-{shield["art"]}-record-{shield["icon"]:03d}-frame-000.png').convert('RGBA')
    layer = layer.crop(layer.getbbox())
    if shield.get('mirror'):
        whole = Image.new('RGBA', (layer.width * 2, layer.height))
        whole.paste(ImageOps.mirror(layer), (0, 0))
        whole.paste(layer, (layer.width, 0))
        layer = closed(whole)
    out.parent.mkdir(parents=True, exist_ok=True)
    layer.resize((round(HEIGHT * shield['face'] / shield['length']), HEIGHT), Image.LANCZOS).save(out)


if __name__ == '__main__':
    main()
