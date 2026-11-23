#!/usr/bin/env python3
"""make_refs.py CUT.png OUT_DIR EXTENT_Y NAME:FOV:TARGET_Y [...]: crop a front painting's cut-out to a close-up ring's framing
(camera distance 2.7, square frame, target at the centre), on flat grey 128 at 768². The cut-out's height is taken as the
splat's EXTENT_Y, centred at y=0, which is how loop_data.py centres the subject."""
import math, sys
from PIL import Image
cut = Image.open(sys.argv[1]).convert('RGBA'); out = sys.argv[2]; ext = float(sys.argv[3])
w, h = cut.size; ppu = h / ext
for spec in sys.argv[4:]:
    name, fov, ty = spec.split(':'); half = 2.7 * math.tan(math.radians(float(fov)) / 2) * ppu
    cy = h / 2 - float(ty) * ppu; cx = w / 2
    box = tuple(round(v) for v in (cx - half, cy - half, cx + half, cy + half))
    canvas = Image.new('RGBA', (box[2] - box[0], box[3] - box[1]), (128, 128, 128, 255))
    canvas.alpha_composite(cut, (-box[0], -box[1]))
    canvas.convert('RGB').resize((768, 768), Image.LANCZOS).save(f'{out}/{name}.png')
    print(name, box)
