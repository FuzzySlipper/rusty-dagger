#!/usr/bin/env python3
"""Step 1 input: a sheet of one classic townsperson's five directional frames, to write its costume brief from.

    python3 sheet.py TEXTURE [--out DIR] [--scale 6]

Reads rusty-dagger's reference export (classic_root in briefs.json): texture-NNN-<race>-<sex>-outfit-N/{front,front-right,
right,back-right,back}.png. Each frame is enlarged with nearest-neighbour (no smoothing, so nothing is invented) onto the
slate ground the concept paintings use, side by side and labelled. Writes OUT/TEXTURE-classic.png (default DIR: local/art/generated/studies/people-concept/TEXTURE).
"""
import argparse, glob, json, os
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
DEFAULT_OUTPUT = REPO / 'local/art/generated/studies/people-concept'
ROOT = REPO / json.loads((HERE / 'briefs.json').read_text())['classic_root']
VIEWS = ('front', 'front-right', 'right', 'back-right', 'back')

ap = argparse.ArgumentParser(); ap.add_argument('texture'); ap.add_argument('--out', type=Path); ap.add_argument('--scale', type=int, default=6)
a = ap.parse_args()
d = glob.glob(str(ROOT / f'texture-{a.texture}-*'))
if not d:
    raise SystemExit(f'no classic frames for texture {a.texture} under {ROOT}')
frames = [Image.open(Path(d[0]) / f'{v}.png').convert('RGBA') for v in VIEWS]
h = max(f.height for f in frames) * a.scale; w = max(f.width for f in frames) * a.scale; pad = 24
sheet = Image.new('RGB', (len(frames) * (w + pad) + pad, h + 2 * pad + 20), (48, 52, 59)); dr = ImageDraw.Draw(sheet)
for k, (v, f) in enumerate(zip(VIEWS, frames)):
    big = f.resize((f.width * a.scale, f.height * a.scale), Image.NEAREST)
    x = pad + k * (w + pad) + (w - big.width) // 2
    sheet.paste(big, (x, pad + 20 + h - big.height), big); dr.text((pad + k * (w + pad), 6), v, fill=(230, 230, 230))
dr.text((sheet.width - 260, 6), os.path.basename(d[0]), fill=(230, 230, 230))
out = a.out or DEFAULT_OUTPUT / a.texture; out.mkdir(parents=True, exist_ok=True)
sheet.save(out / f'{a.texture}-classic.png'); print(out / f'{a.texture}-classic.png')
