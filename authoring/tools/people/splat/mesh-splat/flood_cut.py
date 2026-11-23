#!/usr/bin/env python3
"""Cut a painted figure off a flat background, and place cut-outs as orbit keyframes.

  python3 flood_cut.py cut PAINTING OUT.png [--tolerance 16] [--speck 400] [--border 4]
  python3 flood_cut.py keyframes OUT_DIR VIEW=CUT.png ... [--size 768] [--height 670 | --scale S] [--floor 719] [--grey 128]

`cut` writes an RGBA cut-out cropped to the figure. The background is every
pixel within TOLERANCE (largest per-channel difference) of the median colour
of a BORDER-pixel frame round the picture *and* connected to that frame
(4-connected flood fill). Everything else is the figure: holes inside it are
filled, and separate regions smaller than SPECK pixels are dropped. Unlike
orbit_views.py's threshold matte, a dark boot or hand that comes close to a
dark background stays attached as long as it does not touch the border through
background-coloured pixels, so it suits dark-ground paintings. The alpha is
binary; check the cut-out on a contrasting ground before using it, since a
background gap between arm and body that is fully enclosed would be filled as
a hole (use --keep-holes for figures where that matters).

`keyframes` scales every cut-out by one common factor, so that the tallest
cut-out is HEIGHT pixels tall, and pastes each centred horizontally with its
lowest row on FLOOR, on a SIZE x SIZE canvas of flat GREY. Pass --scale to
reuse another set's factor, so two variants of one figure (cloaked and bare)
share a scale. This is the input
fl2va_anchor.py's --view-dir expects (views/<view>.png). Writes scale.json with
the factor and each view's placed size.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


def figure_mask(rgb, tolerance=16, speck=400, border=4, keep_holes=False):
    px = rgb.astype(np.int16)
    frame = np.concatenate([px[:border].reshape(-1, 3), px[-border:].reshape(-1, 3),
                            px[:, :border].reshape(-1, 3), px[:, -border:].reshape(-1, 3)])
    near = np.abs(px - np.median(frame, axis=0)).max(-1) <= tolerance
    labels, _ = ndimage.label(near)
    edge = np.zeros_like(near)
    edge[:border], edge[-border:], edge[:, :border], edge[:, -border:] = True, True, True, True
    touching = np.unique(labels[edge & near])
    background = np.isin(labels, touching[touching > 0])
    fig = ~background
    if not keep_holes:
        fig = ndimage.binary_fill_holes(fig)
    labels, n = ndimage.label(fig)
    if n:
        sizes = ndimage.sum(fig, labels, range(1, n + 1))
        fig = np.isin(labels, 1 + np.nonzero(sizes >= speck)[0])
    return fig


def cut(args):
    im = Image.open(args.painting).convert('RGB')
    rgb = np.asarray(im)
    fig = figure_mask(rgb, args.tolerance, args.speck, args.border, args.keep_holes)
    ys, xs = np.nonzero(fig)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    out = np.dstack([rgb, fig.astype(np.uint8) * 255])[y0:y1, x0:x1]
    Image.fromarray(out, 'RGBA').save(args.out)
    print(json.dumps({'painting': str(args.painting), 'out': str(args.out), 'crop': [int(x0), int(y0), int(x1), int(y1)],
                      'figure_pixels': int(fig.sum())}))


def keyframes(args):
    cuts = {}
    for spec in args.views:
        view, path = spec.split('=', 1)
        im = Image.open(path).convert('RGBA')
        box = im.getchannel('A').getbbox()
        cuts[view] = im.crop(box)
    scale = args.scale or args.height / max(c.size[1] for c in cuts.values())
    # --equal-height: each view gets its own scale so every figure is exactly HEIGHT tall (head top, hair included, to the lowest
    # foot pixel of the alpha box) with its top and floor on the same rows; a rigid figure is the same height from every side.
    scales = {v: (args.height / c.size[1] if args.equal_height else scale) for v, c in cuts.items()}
    out = Path(args.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    placed = {}
    for view, c in cuts.items():
        w, h = round(c.size[0] * scales[view]), round(c.size[1] * scales[view])
        small = c.resize((w, h), Image.LANCZOS)
        canvas = Image.new('RGBA', (args.size, args.size), (args.grey,) * 3 + (255,))
        canvas.alpha_composite(small, ((args.size - w) // 2, args.floor + 1 - h))
        canvas.convert('RGB').save(out / f'{view}.png')
        placed[view] = [w, h]
    (out / 'scale.json').write_text(json.dumps({'scale': scale, 'scales': scales, 'equal_height': args.equal_height, 'size': args.size,
                                                'floor': args.floor, 'grey': args.grey, 'placed': placed}, indent=1))
    print(json.dumps({'scale': round(scale, 4), 'scales': {v: round(x, 4) for v, x in scales.items()}, 'placed': placed}))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest='cmd', required=True)
    c = sub.add_parser('cut')
    c.add_argument('painting')
    c.add_argument('out')
    c.add_argument('--tolerance', type=int, default=16)
    c.add_argument('--speck', type=int, default=400)
    c.add_argument('--border', type=int, default=4)
    c.add_argument('--keep-holes', action='store_true')
    k = sub.add_parser('keyframes')
    k.add_argument('out_dir')
    k.add_argument('views', nargs='+')
    k.add_argument('--size', type=int, default=768)
    k.add_argument('--height', type=int, default=670)
    k.add_argument('--scale', type=float)
    k.add_argument('--floor', type=int, default=719)
    k.add_argument('--grey', type=int, default=128)
    k.add_argument('--equal-height', action='store_true', help='scale each view on its own to exactly --height')
    args = p.parse_args()
    cut(args) if args.cmd == 'cut' else keyframes(args)


if __name__ == '__main__':
    main()
