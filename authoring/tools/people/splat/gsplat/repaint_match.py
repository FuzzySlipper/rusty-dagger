#!/usr/bin/env python3
"""Match each VACE-repainted ring frame's colours back to its own round-0 render, and flag frames whose repaint changed content.

  python3 repaint_match.py RING_DIR VACE_DIR OUT_DIR [--regions 8] [--soft 12] [--blob-delta 28] [--blob-area 1200]
      [--ignore-area 300] [--extend-delta 17] [--grow 8] [--max-ignore 0.25] [--max-drop 0.334]

RING_DIR is a train_loop_splat ring render (control/NNN.png = the round-0 render, alpha/NNN.png = its alpha), VACE_DIR the
vace_ring.py repaint (frames/NNN.png, the same framing). OUT_DIR gets frames/NNN.png (the matched repaints, dropped frames left
out), ignore/NNN.png (per-frame ignore masks), flags.json and report.json. loop_dataset.py reads OUT_DIR like a repaint folder and pairs frames with cameras by number.

Why: VACE warms and brightens every ring (skin to orange; measured on the first four villager bakes, 2026-11-13), and round 1
learns that shift. Matching, per frame:
  - Regions: the ring's round-0 subject pixels clustered into REGIONS colour groups (k-means in CIELAB, fixed for the ring; 8, since with 5 a minority colour such as the tunic hem in a legs ring shared a group with darker tones and was over-corrected).
  - Shift per region: repaint minus round-0 colour (Lab, means with 10% cut from each tail) over the pixels the round-0 render labels with that region
    (alpha eroded 3 px, so a slightly moved edge does not count). A region under 1% of the subject in a frame takes the ring's
    median shift for it. A pale region (round-0 L > 75 and chroma < 15: white speckle, glare) gets no shift: the repaint is not
    pulled toward it.
  - Correction: each repaint pixel inside the round-0 alpha (dilated 12 px) is assigned to the regions by ITS OWN colour (soft,
    Gaussian weights of width SOFT in Lab after removing each region's shift), and moved by minus the weighted shift. The
    assignment follows the repaint's own edges, so a misaligned boundary does not leave a halo; shading and detail inside a
    region stay the repaint's (only the region's mean moves). Pixels outside are unchanged (the background is re-keyed later).
Content check (on the raw repaint, not the matched one, so the match cannot hide a fault): pixels inside the round-0 alpha
(eroded 4 px) whose hue/chroma (Lab a*, b*) differs from round 0 plus their round-0 region's measured shift by more than
BLOB_DELTA, opened twice, in connected blobs. Every blob of IGNORE_AREA px or more (at 1024², scaled) seeds the frame's ignore mask: it is
extended into the un-opened difference map (pixels differing by more than EXTEND_DELTA, default 17: at the seeds' own 28 the thin
middle of 386-C's legs-low streak and the belt piece by 383-A's hand stayed outside) wherever connected, then grown by GROW px, into the frame's ignore mask OUT_DIR/ignore/NNN.png (255 = not judged): loop_dataset.py carries it into the round-1 set and
train_loop_splat.py neither scores colour nor alpha there, so the rest of the view still teaches. A frame is FLAGGED (listed with
its blobs in flags.json) when one blob exceeds BLOB_AREA px, and DROPPED (moved to OUT_DIR/dropped/, left out of frames/) only
when its ignore area exceeds MAX_IGNORE of its subject. On the raw repaint 386-C legs-low measures: red streak 1,500-9,400 px,
brown blot 985-2,177, and repaints that FIX round 0 (red hose under the hem where round 0 had teal noise, hand edges) 1,100-1,340 -
hue alone cannot tell a fault from a fix, which is why a blob is ignored rather than its view dropped. If more than MAX_DROP of
the ring's frames are DROPPED the tool exits with status 3 and leaves frames/ empty (the rest go to unused/): stop and look.
The repaint's own background (pixels within 10-60 of its median background colour, faded) gets no correction, so no tinted halo.
"""
import argparse
import glob
import json
import os

import numpy as np
from PIL import Image
from scipy import ndimage


def srgb_to_lab(rgb):
    c = rgb / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    xyz = c @ np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]]).T
    xyz = xyz / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lab_to_srgb(lab):
    fy = (lab[..., 0] + 16) / 116; fx = fy + lab[..., 1] / 500; fz = fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f > 0.206893, f ** 3, (f - 16 / 116) / 7.787) * np.array([0.95047, 1.0, 1.08883])
    c = xyz @ np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]]).T
    c = np.where(c > 0.0031308, 1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055, 12.92 * c)
    return np.clip(c * 255 + 0.5, 0, 255).astype(np.uint8)


def trimmed_mean(x, cut=0.1):
    x = np.sort(x, 0); k = int(len(x) * cut)
    return x[k:len(x) - k].mean(0) if len(x) > 2 * k else x.mean(0)


def load(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.float32)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('ring'); p.add_argument('vace'); p.add_argument('out')
    p.add_argument('--regions', type=int, default=8); p.add_argument('--soft', type=float, default=12.0)
    p.add_argument('--blob-delta', type=float, default=28.0); p.add_argument("--blob-area", type=int, default=1200)
    p.add_argument('--ignore-area', type=int, default=300); p.add_argument('--grow', type=int, default=8)
    p.add_argument('--extend-delta', type=float, default=17.0)
    p.add_argument('--max-ignore', type=float, default=0.25); p.add_argument('--max-drop', type=float, default=0.334)
    a = p.parse_args()
    names = sorted(os.path.basename(f) for f in glob.glob(a.vace + '/frames/*.png'))
    frames = []
    for n in names:
        alpha = np.asarray(Image.open(f'{a.ring}/alpha/{n}').convert('L'))
        r0 = srgb_to_lab(load(f'{a.ring}/control/{n}')); vp = load(f'{a.vace}/frames/{n}')
        if vp.shape[:2] != alpha.shape:
            vp = np.asarray(Image.fromarray(vp.astype(np.uint8)).resize(alpha.shape[::-1], Image.LANCZOS)).astype(np.float32)
        frames.append((n, alpha, r0, vp))
    # ring regions: k-means (Lab) on round-0 subject pixels
    sample = np.concatenate([f[2][f[1] > 200][::61] for f in frames])
    rng = np.random.default_rng(0)
    cen = sample[rng.choice(len(sample), a.regions, replace=False)]
    for _ in range(40):
        lab = ((sample[:, None] - cen[None]) ** 2).sum(-1).argmin(1)
        cen = np.array([sample[lab == k].mean(0) if (lab == k).any() else cen[k] for k in range(a.regions)])
    pale = (cen[:, 0] > 75) & (np.hypot(cen[:, 1], cen[:, 2]) < 15)
    # per-frame region shifts
    shifts = []
    for n, alpha, r0, vp in frames:
        inner = ndimage.binary_erosion(alpha > 200, iterations=3)
        p0 = r0[inner]; p1 = srgb_to_lab(vp)[inner]
        lab = ((p0[:, None] - cen[None]) ** 2).sum(-1).argmin(1)
        sh = np.full((a.regions, 3), np.nan)
        for k in range(a.regions):
            m = lab == k
            if m.sum() >= 0.01 * len(lab):
                sh[k] = trimmed_mean(p1[m]) - trimmed_mean(p0[m])
        shifts.append(sh)
    ring_median = np.nanmedian(np.array(shifts), 0)
    ring_median = np.where(np.isnan(ring_median), 0, ring_median)
    os.makedirs(a.out + '/frames', exist_ok=True)
    rows, flagged, outputs = [], [], []
    for (n, alpha, r0, vp), sh in zip(frames, shifts):
        sh = np.where(np.isnan(sh), ring_median, sh); measured = sh.copy(); sh[pale] = 0
        v = srgb_to_lab(vp)
        bound = ndimage.binary_dilation(alpha > 25, iterations=12)
        d2 = ((v[..., None, :] - sh[None, None] - cen[None, None]) ** 2).sum(-1)          # H, W, K
        w = np.exp(-(d2 - d2.min(-1, keepdims=True)) / (2 * a.soft ** 2)); w /= w.sum(-1, keepdims=True)
        corr = -(w[..., :, None] * sh[None, None]).sum(-2)
        # no correction on the repaint's own background (VACE paints it pale; it is re-keyed later): fade the correction out for
        # pixels within 10-60 (max channel; loop_dataset keys over about the same ramp) of the repaint's background colour, so the silhouette does not get a tinted halo
        empty = ~ndimage.binary_dilation(alpha > 3, iterations=12)
        if empty.sum() > 2000:
            bgd = np.abs(vp - np.median(vp[empty], 0)).max(-1)
            corr *= np.clip((bgd - 10) / 50, 0, 1)[..., None]
        matched_lab = np.where(bound[..., None], v + corr, v)
        out = np.where(bound[..., None], lab_to_srgb(matched_lab), vp.astype(np.uint8))
        # drop check, independent of the match (which follows the repaint's own colours and so can pull a fault toward a
        # neighbouring region): the raw repaint against round 0 plus the general shift measured for each pixel's round-0 region
        inner = ndimage.binary_erosion(alpha > 200, iterations=4)
        own = ((r0[..., None, :] - cen[None, None]) ** 2).sum(-1).argmin(-1)
        expected = r0 + measured[own]
        dab = np.hypot(v[..., 1] - expected[..., 1], v[..., 2] - expected[..., 2])
        hot = ndimage.binary_opening(inner & (dab > a.blob_delta), iterations=2)
        raw = inner & (dab > a.extend_delta)   # the un-opened map the seeds extend into (lower test: bridges a streak's thin part)
        labels, count = ndimage.label(hot)
        sizes = np.bincount(labels.ravel())[1:] if count else np.array([0])
        scale = alpha.size / 1024 ** 2
        biggest = int(sizes.max()); flag = biggest > a.blob_area * scale
        keep = np.nonzero(sizes >= a.ignore_area * scale)[0] + 1 if count else []
        ignore = None
        if len(keep):   # seeds = the opened blobs; each extends into the un-opened difference map it touches (follows a streak
            seeds = np.isin(labels, keep)   # through its thin parts), then grows GROW px
            rlab, _ = ndimage.label(raw)
            ext = np.isin(rlab, np.unique(rlab[seeds & raw])[1:] if (seeds & raw).any() else []) | seeds
            ignore = ndimage.binary_dilation(ext, iterations=max(1, round(a.grow * scale ** 0.5)))
        subject = int((alpha > 127).sum())
        ignore_share = float((ignore & (alpha > 127)).sum() / max(subject, 1)) if ignore is not None else 0.0
        drop = ignore_share > a.max_ignore
        blobs = [{'area': int(sizes[k - 1]), 'centre': [int(v.mean()) for v in np.nonzero(labels == k)]} for k in keep]
        if flag or drop:
            flagged.append({'frame': n, 'blob': biggest, 'ignore_share': round(ignore_share, 3), 'dropped': bool(drop), 'blobs': blobs})
        outputs.append((n, out, ignore, drop))
        rows.append({'frame': n, 'shift': np.round(sh, 1).tolist(), 'measured': np.round(measured, 1).tolist(), 'biggest_blob': biggest,
                     'ignore_share': round(ignore_share, 3), 'flagged': bool(flag), 'dropped': bool(drop)})
    dropped = [f['frame'] for f in flagged if f['dropped']]
    report = {'regions_lab': np.round(cen, 1).tolist(), 'pale': pale.tolist(), 'ring_median_shift': np.round(ring_median, 1).tolist(),
              'frames': rows, 'flagged': [f['frame'] for f in flagged], 'dropped': dropped, 'settings': vars(a)}
    json.dump(flagged, open(a.out + '/flags.json', 'w'), indent=1)
    json.dump(report, open(a.out + '/report.json', 'w'), indent=1)
    share = len(dropped) / max(len(frames), 1)
    print(json.dumps({'ring': a.ring, 'frames': len(frames), 'flagged': len(flagged), 'with_ignore': sum(o[2] is not None for o in outputs),
                      'dropped': len(dropped), 'share_dropped': round(share, 3),
                      'flagged_frames': [f['frame'][:3] + ('D' if f['dropped'] else '') for f in flagged]}))
    stop = share > a.max_drop
    for d in ('ignore', 'dropped') + (('unused',) if stop else ()):
        os.makedirs(f'{a.out}/{d}', exist_ok=True)
    for n, out, ignore, drop in outputs:   # dropped frames to dropped/ for review; the rest to frames/, or unused/ on a stop
        Image.fromarray(out).save(f"{a.out}/{'dropped' if drop else 'unused' if stop else 'frames'}/{n}")
        if ignore is not None:
            Image.fromarray((ignore * 255).astype(np.uint8)).save(f'{a.out}/ignore/{n}')
    if stop:
        print(f'STOP: {len(dropped)} of {len(frames)} frames dropped (over {a.max_drop:.0%}); frames/ left empty, matched frames in unused/')
        raise SystemExit(3)


if __name__ == '__main__':
    main()
