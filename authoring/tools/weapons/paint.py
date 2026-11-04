#!/usr/bin/env python3
"""Paints a built weapon in its icon's own colours on a grid of world cells: the crunch treatment.

  python3 authoring/tools/weapons/paint.py BUILD_DIR ICON.png OUT_DIR [--cell 0.002] [--steps 6] [--dither 0.2]
      [--light 0.15] [--size 512]

BUILD_DIR is build.py's output. Each part is a palette_cells.py region (asset-pipeline tools/texture-edit),
constrained to its own object and harvesting a ramp of `--steps` colours from its own icon pixels, so a cell
--cell metres across takes one flat colour from that ramp by the icon's brightness there, a soft light and an
ordered dither. The defaults are the set's `crunch` in authoring/specs/weapons/weapons.json (Patch chose 2 mm
cells); the options exist to compare treatments. A part's `light` in the spec overrides --light: a round part the
flat icon says little about (a club's ball) takes more of the soft light. Writes OUT_DIR/model.glb (build.py's
model with the painted `metal` and `trim` textures, sampled nearest, matte) and palette_cells' report.
"""
import argparse
import json
import os
import subprocess
from pathlib import Path

PIPELINE = Path(os.environ.get('ASSET_PIPELINE', Path(__file__).resolve().parents[4] / 'asset-pipeline'))


def main():
    crunch = json.loads((Path(__file__).resolve().parents[2] / 'specs/weapons/weapons.json').read_text())['crunch']
    parser = argparse.ArgumentParser()
    parser.add_argument('build', type=Path)
    parser.add_argument('icon', type=Path)
    parser.add_argument('out', type=Path)
    parser.add_argument('--cell', type=float, default=crunch['cell'])
    parser.add_argument('--steps', type=int, default=crunch['steps'])
    parser.add_argument('--dither', type=float, default=crunch['dither'])
    parser.add_argument('--light', type=float, default=crunch['light'], help='weight of the soft light against the icon brightness')
    parser.add_argument('--size', type=int, default=crunch['size'])
    args = parser.parse_args()
    build, out = args.build.resolve(), args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    parts = json.loads((build / 'build.json').read_text())['parts']
    light = lambda name: parts[name].get('light', args.light)
    spec = {
        'reference_glb': str(build / 'reference.glb'),
        'target_glb': str(build / 'model.glb'),
        'sprites': [str(args.icon.resolve())],
        'sprite_ignore': [[0, 0, 0], [14, 14, 12]],   # the icon's ink outline
        'cell_size': args.cell,
        'targets': {slot: {'size': args.size, 'out': f'{slot}.png'} for slot in {p['slot'] for p in parts.values()}},
        'regions': [{'name': name, 'objects': [name], 'generator': p['seeds'], 'sprite': p['seeds'], 'steps': args.steps,
                     'light': {'weight': light(name), 'generator_weight': 1 - light(name), 'dither': args.dither}}
                    for name, p in parts.items()],
        # The broad face (+X after import) lit from a little toward the tip (+Y) and the up edge (+Z).
        'light': {'direction': [1, 0.3, 0.6], 'weight': args.light, 'generator_weight': 1 - args.light,
                  'dither': args.dither},
        'smooth_passes': 2,
    }
    (out / 'spec.json').write_text(json.dumps(spec, indent=1) + '\n')
    subprocess.run(['blender', '-b', '-noaudio', '--factory-startup', '--python-exit-code', '1', '--python',
                    str(PIPELINE / 'legacy/experiments/detail-separated-prop/blender_driver.py'), '--',
                    str(PIPELINE / 'tools/texture-edit/palette_cells.py'), str(out / 'spec.json'), str(out)],
                   check=True, stdout=subprocess.DEVNULL)
    swaps = [f'{slot}={out / target["out"]}' for slot, target in spec['targets'].items()]
    subprocess.run(['python3', str(PIPELINE / 'tools/texture-edit/glb_images.py'), str(build / 'model.glb'),
                    str(out / 'model.glb'), *swaps, '--nearest', '--matte'], check=True, stdout=subprocess.DEVNULL)
    print(out / 'model.glb')


if __name__ == '__main__':
    main()
