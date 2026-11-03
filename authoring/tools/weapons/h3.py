#!/usr/bin/env python3
"""Builds the H3 turnaround request for one weapon in authoring/specs/weapons/weapons.json.

  python3 authoring/tools/weapons/h3.py prepare dagger r01 [--size 512]

writes authoring/gen-raw/weapons/<name>/h3-<rev>/: icon.png (the refreshed paper-doll layer stood upright,
point up, with the gap the hand left in the grip filled) and spec.json for asset-pipeline's
legacy/experiments/h3-turnaround/run_turnaround.py. A changed request needs a new rev, so an older
attempt is never mistaken for the new one.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
SPEC = ROOT / 'authoring/specs/weapons/weapons.json'
WORN = ROOT / 'authoring/gen-raw/worn'
OUT = ROOT / 'authoring/gen-raw/weapons'
SOLID = 250


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


def prepare(name, rev, size):
    spec = json.loads(SPEC.read_text())
    weapon = next(w for w in spec['weapons'] if w['name'] == name)
    if not weapon.get('subject'):
        raise SystemExit(f'{name} has no subject in {SPEC}: write its design first')
    out = OUT / name / f'h3-{rev}'
    if (out / 'spec.json').exists():
        raise SystemExit(f'{out} already exists: use a new rev')
    out.mkdir(parents=True)
    icon = Image.open(WORN / f'texture-234-record-{weapon["icon"]:03d}-frame-000.png').convert('RGBA')
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
    args = parser.parse_args()
    prepare(args.name, args.rev, args.size)


if __name__ == '__main__':
    main()
