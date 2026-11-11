#!/usr/bin/env python3
"""Builds the H3 turnaround request for one weapon in authoring/specs/weapons/weapons.json.

  python3 authoring/tools/weapons/h3.py join flail [--seed 41]
  python3 authoring/tools/weapons/h3.py prepare dagger r01 [--size 512]

`join` has Qwen rejoin an icon the paper doll draws in pieces. `prepare` writes
authoring/gen-raw/weapons/<name>/h3-<rev>/: icon.png (the joined icon, or else the refreshed paper-doll layer with the
gap the hand left in a straight grip filled, stood upright, point up; a spec `flip` turns a joined icon over) and spec.json for asset-pipeline's
legacy/experiments/h3-turnaround/run_turnaround.py. A changed request needs a new rev, so an older
attempt is never mistaken for the new one.

Look at joined.png before `prepare`: a join can leave a gap or redraw a fitting. A weapon with no paper-doll layer copies
its accepted item icon to joined.png. Run the request on the lab 5090 with run_turnaround.py inside
`asset-resources run --host gpu-lab --resource lab-comfy`, from the asset-pipeline root, two seeds at 25 steps. Look at
every attempt, then copy eight frames of the chosen orbit to h3-<rev>/views/ for sheet.py.
"""
import argparse
import json
import os
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
SPEC = ROOT / 'authoring/specs/weapons/weapons.json'
WORN = ROOT / 'authoring/gen-raw/worn'
OUT = ROOT / 'authoring/gen-raw/weapons'
PIPELINE = Path(os.environ.get('ASSET_PIPELINE', ROOT.parent / 'asset-pipeline'))
COMFY = os.environ.get('COMFY_URL', 'http://192.168.1.20:8188')
GREEN = (0, 177, 64)
SOLID = 250


def stand(image, flip, haft=False):
    """Turns a whole weapon so its long axis is vertical, a half turn further when the spec says its head came out below.
    With `haft` the axis is the wooden haft's alone, and the heavier end goes up: a big head would otherwise tilt it."""
    pixels = np.asarray(image).astype(int)
    chosen = pixels[..., 3] > 20
    if haft:
        chosen &= (pixels[..., 0] > pixels[..., 2] + 30) & (pixels[..., 0] > pixels[..., 1] + 15)
    ys, xs = np.nonzero(chosen)
    _, vectors = np.linalg.eigh(np.cov(np.vstack([xs, ys])))
    axis = vectors[:, 1]
    turned = image.rotate(float(np.degrees(np.arctan2(axis[1], axis[0]))) + 90 + (180 if flip else 0), Image.BICUBIC, expand=True)
    turned = turned.crop(turned.getbbox())
    alpha = np.asarray(turned)[..., 3] > 20
    half = alpha.shape[0] // 2
    return turned.rotate(180) if haft and alpha[:half].sum() < alpha[half:].sum() else turned


def grip_gap(image):
    """The rows, along the vertical axis, where the hand left the layer's grip empty, or None."""
    pixels = np.asarray(image)
    centre = pixels.shape[1] // 2
    band = pixels[:, max(0, centre - 4):centre + 5, 3].max(axis=1)
    solid = np.nonzero(band >= SOLID)[0]
    gaps = np.nonzero(np.diff(solid) > 1)[0]
    return (int(solid[gaps[-1]] + 1), int(solid[gaps[-1] + 1])) if len(gaps) else None


def upright(image):
    """Turns the layer so its long axis is vertical with the grip, where the hand's gap is, below the middle."""
    alpha = np.asarray(image)[..., 3]
    ys, xs = np.nonzero(alpha > 20)
    _, vectors = np.linalg.eigh(np.cov(np.vstack([xs, ys])))
    axis = vectors[:, 1]
    turned = image.rotate(float(np.degrees(np.arctan2(axis[1], axis[0]))) + 90, Image.BICUBIC, expand=True)
    turned = turned.crop(turned.getbbox())
    gap = grip_gap(turned)
    return turned.rotate(180) if gap and sum(gap) < turned.height else turned


def fill_grip(image):
    """Fills the rows the hand covered with the lower grip piece's own rows, mirrored back and forth so no seam repeats."""
    gap = grip_gap(image)
    if not gap:
        return image, None
    pixels = np.asarray(image).copy()
    top, bottom = gap
    source = list(range(bottom, min(bottom + 40, pixels.shape[0])))
    cycle = source + source[-2:0:-1]
    for i, y in enumerate(range(top, bottom)):
        pixels[y] = pixels[cycle[i % len(cycle)]]
    return Image.fromarray(pixels), [int(top), int(bottom)]


JOIN = ('Picture 1 is a painted fantasy RPG inventory illustration of a weapon on a flat green background, drawn in separate '
        'pieces with gaps where a hand holds it. The weapon: {subject} Redraw it as one complete, unbroken weapon: paint the '
        'missing parts of the grip, haft, stave or string continuously across every gap, so nothing is left floating. Keep its '
        'angle, position, size, colours, fittings and painting style exactly. Add nothing else. Flat green background.')


def join(name, seed):
    """Has Qwen rejoin an icon the paper doll draws in pieces, in place, and keys it: joined.png beside the weapon's runs.
    A row-by-row fill can only serve a straight grip, not a haft, a chain or a bow."""
    spec = json.loads(SPEC.read_text())
    weapon = next(w for w in spec['weapons'] if w['name'] == name)
    out = OUT / name
    out.mkdir(parents=True, exist_ok=True)
    icon = Image.open(WORN / f'texture-{weapon.get("art", 234)}-record-{weapon["icon"]:03d}-frame-000.png').convert('RGBA')
    flat = Image.new('RGB', icon.size, GREEN)
    flat.paste(icon, (0, 0), icon)
    flat.save(out / 'join-source.png')
    subprocess.run(['python3', 'tools/texture-edit/qwen_edit.py', '--image', str(out / 'join-source.png'), '--prompt',
                    JOIN.format(subject=weapon['subject']), '--seed', str(seed), '--out', str(out / 'join-edit.png')],
                   cwd=PIPELINE, check=True, stdout=subprocess.DEVNULL, env={**os.environ, 'COMFY_URL': COMFY})
    edit = Image.open(out / 'join-edit.png').convert('RGB').resize(icon.size, Image.LANCZOS)
    rgb = np.asarray(edit).astype(int)
    keyed = ~((rgb[..., 1] > rgb[..., 0] + 40) & (rgb[..., 1] > rgb[..., 2] + 30))
    alpha = Image.fromarray((keyed * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    edit.putalpha(alpha)
    edit.save(out / 'joined.png')
    print(out / 'joined.png')


def prepare(name, rev, size):
    spec = json.loads(SPEC.read_text())
    weapon = next(w for w in spec['weapons'] if w['name'] == name)
    if not weapon.get('subject'):
        raise SystemExit(f'{name} has no subject in {SPEC}: write its design first')
    out = OUT / name / f'h3-{rev}'
    if (out / 'spec.json').exists():
        raise SystemExit(f'{out} already exists: use a new rev')
    out.mkdir(parents=True)
    joined = OUT / name / 'joined.png'
    if joined.exists():
        icon, filled = stand(Image.open(joined).convert('RGBA'), weapon.get('flip', False), weapon.get('axis') == 'haft'), 'by join'
    else:
        icon = Image.open(WORN / f'texture-{weapon.get("art", 234)}-record-{weapon["icon"]:03d}-frame-000.png').convert('RGBA')
        icon, filled = fill_grip(upright(icon))
    icon.save(out / 'icon.png')
    role = ('is a painted inventory illustration of this weapon, drawn flat with its broad face toward the viewer. '
            'Use it as the design authority for its outline, proportions, colours and fittings. The middle of the grip, '
            'once hidden by a hand, has been filled in: the grip is one continuous piece from guard to pommel.')
    subject = ' '.join(['ISOLATED ITEM ONLY:', weapon['subject'], weapon.get('invent', ''), spec['pose'], spec['style']])
    request = {'name': f'weapon-{name}-{rev}', 'out': str(out), 'profile': 'verbatim', 'background': [128, 128, 128],
               'scale': 1, 'width': size, 'height': size, 'frames': 124,
               'references': [{'name': 'icon', 'path': str(out / 'icon.png'), 'role': role}],
               'subject': subject, 'orbit': spec['orbit'],
               'note': f'Weapon {weapon["item"]} {name} from refreshed record 234/{weapon["icon"]}; grip rows filled {filled}.'}
    (out / 'spec.json').write_text(json.dumps(request, indent=1) + '\n')
    print(out / 'spec.json')


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='command', required=True)
    prep = sub.add_parser('prepare')
    prep.add_argument('name')
    prep.add_argument('rev')
    prep.add_argument('--size', type=int, default=512)
    joining = sub.add_parser('join')
    joining.add_argument('name')
    joining.add_argument('--seed', type=int, default=41)
    args = parser.parse_args()
    if args.command == 'join':
        join(args.name, args.seed)
    else:
        prepare(args.name, args.rev, args.size)


if __name__ == '__main__':
    main()
