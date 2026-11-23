#!/usr/bin/env python3
"""Assemble the next round's training set for train_loop_splat.py.

  python3 loop_dataset.py OUT --anchors R0_DATA --poses ROUND/poses.npz --init ROUND/splat.npz
      --ring ROUND/ring:ROUND/vace [--ring ...] [--dilate 12] [--exact-every N] [--fill-holes PIXELS]
      [--background-from-alpha]

Anchors (and the held-out view) are copied from the round-0 dataset with the
refined cameras from the latest round's poses.npz. A repaint folder's ignore/NNN.png (repaint_match.py) is carried into
OUT/ignore/ under the view's number; train_loop_splat.py does not judge those pixels. Each --ring pairs a ring
rendered by train_loop_splat.py (exact cameras, splat alpha) with its VACE
repaint (vace_ring.py frames/). A repainted frame's alpha is keyed against
the flat grey background, but only inside the splat's own silhouette dilated
by DILATE pixels, so a repaint can reshape edges a little and cannot add
stray background. Init points are the previous splat's reasonably opaque
Gaussians, so the round starts from where the last one ended. --exact-every N
keeps every Nth repainted frame out of training as an `exact` view, scored on
its known camera. --fill-holes closes keyed-out islands of up to PIXELS inside
the subject, such as a grey buckle on the grey background. --background-from-alpha
measures each repaint's background colour and its variation where the splat is
absent, instead of at the image corners. Use it for close-up rings, whose
corners often hold the subject, and for repaints with a mottled backdrop.
"""
import argparse, json, os, shutil, sys
import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from anchor_dataset import key_alpha  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('out'); ap.add_argument('--anchors', required=True); ap.add_argument('--poses', required=True)
    ap.add_argument('--init', required=True); ap.add_argument('--ring', action='append', default=[])
    ap.add_argument('--dilate', type=int, default=12)
    ap.add_argument('--exact-every', type=int, default=0)
    ap.add_argument('--fill-holes', type=int, default=0)
    ap.add_argument('--background-from-alpha', action='store_true')
    a = ap.parse_args()
    os.makedirs(a.out + '/images', exist_ok=True)
    meta = json.load(open(a.anchors + '/views.json'))
    poses = np.load(a.poses)
    nA = len(meta['views'])
    viewmats = list(poses['viewmats'][:nA]); Ks = list(poses['Ks'][:nA])
    rows = []
    for i, v in enumerate(meta['views']):
        shutil.copy(f'{a.anchors}/images/{i:03d}.png', f'{a.out}/images/{i:03d}.png')
        rows.append(v)
    k = nA
    for spec in a.ring:
        ring, vace = spec.split(':')
        cams = np.load(ring + '/cameras.npz')
        frames = sorted(f for f in os.listdir(vace + '/frames') if f.endswith('.png'))
        for f in frames:
            j = int(os.path.splitext(f)[0])   # the ring frame number, so a repaint folder with dropped frames keeps cameras and alphas aligned
            rgb = np.asarray(Image.open(f'{vace}/frames/{f}').convert('RGB'))
            bound = Image.open(f'{ring}/alpha/{j:03d}.png').point(lambda x: 255 if x > 25 else 0)
            bound = np.asarray(bound.filter(ImageFilter.MaxFilter(2 * (a.dilate // 2) + 1)), np.float32) / 255
            if bound.shape != rgb.shape[:2]:
                rgb = np.asarray(Image.fromarray(rgb).resize(bound.shape[::-1], Image.LANCZOS))
            alpha, _ = key_alpha(rgb)
            empty = np.asarray(Image.open(f'{ring}/alpha/{j:03d}.png').filter(ImageFilter.MaxFilter(25))) < 3
            if a.background_from_alpha and empty.sum() > 2000:
                distance = np.abs(rgb.astype(np.float32) - np.median(rgb[empty], 0)).max(-1)
                start = float(np.clip(np.percentile(distance[empty], 95), 10, 36))
                keyed = Image.fromarray((np.clip((distance - start) / 24, 0, 1) * 255).astype(np.uint8))
                alpha = np.asarray(keyed.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3)), np.float32) / 255
            if a.fill_holes:
                from scipy import ndimage
                labels, count = ndimage.label(alpha < 0.5)
                sizes = np.bincount(labels.ravel())
                edge = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
                small = [i for i in range(1, count + 1) if sizes[i] <= a.fill_holes and i not in edge]
                alpha = np.where(np.isin(labels, small), 1.0, alpha).astype(np.float32)
            alpha = alpha * bound
            Image.fromarray(np.concatenate([rgb, (alpha[..., None] * 255).astype(np.uint8)], -1), 'RGBA').save(f'{a.out}/images/{k:03d}.png')
            if os.path.exists(f'{vace}/ignore/{f}'):   # repaint_match.py's ignore mask: carried at the view's size, nearest-neighbour
                os.makedirs(a.out + '/ignore', exist_ok=True)
                Image.open(f'{vace}/ignore/{f}').convert('L').resize(bound.shape[::-1], Image.NEAREST).save(f'{a.out}/ignore/{k:03d}.png')
            viewmats.append(cams['viewmats'][j]); Ks.append(cams['Ks'][j])
            rows.append({'index': k, 'name': f'{os.path.basename(os.path.dirname(ring))}-ring-{j:03d}', 'role': 'exact' if a.exact_every and j % a.exact_every == a.exact_every // 2 else 'ring', 'source': f'{vace}/frames/{f}'})
            k += 1
    np.savez_compressed(a.out + '/cameras.npz', viewmats=np.array(viewmats, np.float32), Ks=np.array(Ks, np.float32))
    s = np.load(a.init)
    keep = 1 / (1 + np.exp(-s['opacities'])) > 0.03
    init = np.load(a.anchors + '/init_points.npz')
    np.savez_compressed(a.out + '/init_points.npz', points=s['means'][keep].astype(np.float32),
                        colors=s['rgb'][keep].astype(np.float32), spacing=float(init['spacing']))
    json.dump({**{k2: v for k2, v in meta.items() if k2 != 'views'}, 'views': rows, 'rings': a.ring, 'init': a.init},
              open(a.out + '/views.json', 'w'), indent=1)
    print(a.out, nA, 'anchors +', k - nA, 'ring frames; init', int(keep.sum()), 'points')


if __name__ == '__main__':
    main()
