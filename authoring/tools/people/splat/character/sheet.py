#!/usr/bin/env python3
"""sheet.py OUT.png COLS ROWS PATTERN [--width 1000] [--margin 8] [--preview 2400]: a labelled grid of held captures, each tile
cropped to the full frame height and centred on the figure (not a fixed box: 385's sheet.py assumed 2560x1440 frames and cut
1920x1080 tiles off-centre). The figure is what is left of the frame after the background and its thin grid lines are removed
(pixels far from the background colour, opened by a 7 px minimum filter so 1-2 px lines go). The check: no tile's figure may
touch the tile edge (the figure must sit at least --margin px inside the crop on every side); exit 3 naming each tile that does."""
import argparse, json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ap = argparse.ArgumentParser(); ap.add_argument('out'); ap.add_argument('cols'); ap.add_argument('rows'); ap.add_argument('pattern')
ap.add_argument('--width', type=int, default=1000); ap.add_argument('--margin', type=int, default=8)
ap.add_argument('--preview', type=int, default=2400)
a = ap.parse_args()
cols, rows = a.cols.split(','), a.rows.split(',')


def figure_box(im):
    x = np.asarray(im, np.int16); bg = np.median(x[:40].reshape(-1, 3), 0)
    far = (np.abs(x - bg).max(2) > 45).astype(np.uint8) * 255
    m = np.asarray(Image.fromarray(far).filter(ImageFilter.MinFilter(7)).filter(ImageFilter.MaxFilter(7))) > 0
    ys, xs = np.where(m)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


try: font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 30)
except Exception: font = ImageFont.load_default()
w = a.width; h = None; sheet = None; d = None; bad = []; boxes = {}
for r, row in enumerate(rows):
    for c, col in enumerate(cols):
        im = Image.open(a.pattern.format(row=row, col=col)).convert('RGB')
        if sheet is None:
            h = im.height; sheet = Image.new('RGB', (w * len(cols), (h + 44) * len(rows)), (20, 22, 28)); d = ImageDraw.Draw(sheet)
        fb = figure_box(im); label = f'{row} | {col}'
        cx = (fb[0] + fb[2]) // 2 if fb else im.width // 2
        x0 = min(max(cx - w // 2, 0), im.width - w); box = (x0, 0, x0 + w, h)
        boxes[label] = {'figure': fb, 'crop': box}
        if fb is None or fb[0] - x0 < a.margin or x0 + w - fb[2] < a.margin or fb[1] < a.margin or h - fb[3] < a.margin:
            bad.append(label)
        sheet.paste(im.crop(box), (c * w, r * (h + 44) + 44))
        d.text((c * w + 10, r * (h + 44) + 6), label, fill=(255, 120, 120) if label in bad else (235, 235, 235), font=font)
sheet.save(a.out)
p = sheet.copy(); p.thumbnail((a.preview, a.preview * 4)); p.save(a.out.replace('.png', '-preview.jpg'), quality=88)
json.dump({'tiles': boxes, 'touching_edge': bad, 'margin': a.margin}, open(a.out.replace('.png', '-tiles.json'), 'w'), indent=1)
print(a.out, sheet.size, 'figure touches the tile edge: ' + (', '.join(bad) if bad else 'none'))
sys.exit(3 if bad else 0)
