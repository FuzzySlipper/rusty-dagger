#!/usr/bin/env python3
"""Cut the frames of a closed orbit video into views for fit_views.py.

  python3 orbit_views.py FRAMES_DIR OUT --direction 1|-1 [--every 1] [--last 71] [--step 5] [--slack 12.5]
      [--threshold 26]

FRAMES_DIR holds the orbit's frames on a flat background, in order, starting
at the front. OUT gets views/NNN.png (RGBA, the subject keyed from the
background) and views.json for fit_views.py. The first row searches azimuth
-10 to 10. Every later row searches from the previous frame's fitted azimuth:
DIRECTION times the expected STEP per frame, less SLACK/2 to plus SLACK, since
a generated orbit does not turn evenly. DIRECTION 1 turns the camera toward
+X first (the subject's nose swings to the left of the picture). --last drops
the closing frames, which repeat the front. The matte keeps pixels further
than THRESHOLD from the border's median colour, closes pinholes and keeps the
largest region with its holes filled, so a subject close to the background's
colour needs checking on mattes.png.

--open-holes AREA reopens enclosed background (the grey between an arm and the
body, or between the legs, which hole filling otherwise keeps as subject): a
filled hole is cut back out when it is at least AREA pixels (at 2176², scaled
by frame size) and flat background - median distance from the border colour
at most --hole-flat DIST (default 6) and per-channel spread at most STD
(default 3.5). Smaller or textured holes stay filled, so a garment of the
background's colour keeps its pieces (villager 383-A's grey sash leaves holes
of 100-400 px with spread 3.3-4.9; the background patches are 700-33,000 px).
OUT/holes.json lists per frame the pixels removed and every hole of 100 px or
more that was kept, with its size, distance, spread and centre.

Two more ways a filled hole opens (2026-11-15; the depth-controlled lit-clay
clips have a pale glow and a gradient in the grey, so the background trapped
between an arm and the tunic sits at distance 7-8 from the border colour and
the flat test keeps it - the pale film came back). Neither touches 383-A's
grey sash, whose holes are under 500 px with spread 3.3-4.9:
--hole-large AREA,STD   a hole of at least AREA px (2176 scale) with spread at
                        most STD opens at any distance (the bake uses 4000,2.5)
--guide DIR             the clip's guide frames (depth or clay, background black
                        or flat, frame N for clip frame N): the guide's silhouette
                        is aligned to the frame's (shift and scale, best overlap;
                        H3's camera is a few degrees off, so the guide is a prior,
                        not the matte), and a hole of at least AREA (--open-holes)
                        that lies at least --guide-frac (0.5) on the guide's
                        background opens if its spread is at most 5.
holes.json then also lists each opened hole with its reason.

--birefnet DIR (2026-11-15, Patch: "the gpu cutout they use for rusty-daggers
art"; the idea is Rusty Dagger's authoring/tools/shared/matte.py, task 10667,
copied here): a BiRefNet figure mask per frame (birefnet_masks.py, DIR/NNN.png)
decides the ground-coloured parts instead of the flat / large / guide tests.
The colour key stays the authority for everything else. An enclosed hole of
at least 100 px (2176 scale) opens when the mask's mean over it is under 0.5
and stays filled otherwise. Ground-coloured pixels the mask holds well inside
the figure (mask > 0.75, eroded by 3 px at 1088) are regained, such as grey
cloth the key cut. BiRefNet drops things that stand apart from a figure and runs
per frame, so each decision is compared with the previous frame's: holes.json
marks a hole decided the other way from where most of its pixels went in the
frame before ("flip").
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


def guide_background(guide, filled):
    """The guide's background, aligned to the frame's filled silhouette (scale .92-1.08, shift +-48 px at 1088, by IoU at 1/4 size)."""
    g = np.asarray(guide.convert("L").resize(filled.shape[::-1], Image.NEAREST)).astype(np.int16)
    border = np.concatenate([g[:8].ravel(), g[-8:].ravel(), g[:, :8].ravel(), g[:, -8:].ravel()])
    g = np.abs(g - np.median(border)) > 8
    H, W = g.shape; step = max(1, H // 272); fs, gs = filled[::step, ::step], g[::step, ::step]
    def warp(m, sc, dy, dx):
        h, w = m.shape; yy, xx = np.mgrid[0:h, 0:w]
        sy = ((yy - dy - h / 2) / sc + h / 2).round().astype(int); sx = ((xx - dx - w / 2) / sc + w / 2).round().astype(int)
        ok = (sy >= 0) & (sy < h) & (sx >= 0) & (sx < w); o = np.zeros_like(m); o[ok] = m[sy[ok], sx[ok]]; return o
    best = (-1.0, 1.0, 0, 0); r = max(2, 48 * H // 1088 // step)
    for sc in (0.92, 0.96, 1.0, 1.04, 1.08):
        for dy in range(-r, r + 1, 2):
            for dx in range(-r, r + 1, 2):
                w_ = warp(gs, sc, dy, dx); iou = (w_ & fs).sum() / max((w_ | fs).sum(), 1)
                if iou > best[0]: best = (iou, sc, dy, dx)
    return ~warp(g, best[1], best[2] * step, best[3] * step), round(float(best[0]), 3)


def matte(image, threshold, open_holes=0, flat=(6, 3.5), report=None, large=None, guide=None, guide_frac=0.5, bmask=None, prev=None, bedge=None):
    pixels = np.asarray(image.convert("RGB")).astype(np.int16)
    border = np.concatenate([pixels[:8].reshape(-1, 3), pixels[-8:].reshape(-1, 3), pixels[:, :8].reshape(-1, 3), pixels[:, -8:].reshape(-1, 3)])
    distance = np.abs(pixels - np.median(border, 0)).max(-1)
    # Morphology on an edge-padded mask: scipy treats outside the image as background, so its erosion trimmed a figure that leaves the
    # frame (a head-and-shoulders path cut off at the sash) by 2 rows at the edge - a false silhouette for the alpha loss (2026-11-14).
    P = 4
    subject = ndimage.binary_opening(np.pad(distance > threshold, P, mode='edge'), iterations=1)[P:-P, P:-P]
    labels, count = ndimage.label(subject)
    if count > 1:
        sizes = ndimage.sum(subject, labels, range(1, count + 1))
        subject = labels == 1 + int(np.argmax(sizes))
    closed = ndimage.binary_closing(np.pad(subject, P, mode='edge'), iterations=2)[P:-P, P:-P]
    subject = ndimage.binary_fill_holes(closed)
    if bmask is not None:
        scale = (pixels.shape[1] / 2176) ** 2
        b = np.asarray(bmask.convert("L").resize(subject.shape[::-1], Image.BILINEAR)).astype(np.float32) / 255
        labels, count = ndimage.label(subject & ~closed)
        opened_map, kept_map = np.zeros_like(subject), np.zeros_like(subject)
        removed, kept, opened = 0, [], []
        for index, box in enumerate(ndimage.find_objects(labels), 1):
            hole = labels[box] == index
            area = int(hole.sum())
            if area < 100 * scale:
                continue
            fig = float(b[box][hole].mean())
            row = {"area": area, "distance": round(float(np.median(distance[box][hole])), 1), "spread": round(float(pixels[box][hole].std(0).max()), 1),
                   "centre": [int(v.mean()) + bb.start for v, bb in zip(np.nonzero(hole), box)], "mask": round(fig, 2)}
            if prev is not None:
                po, pk = prev[0][box][hole].mean(), prev[1][box][hole].mean()
                row["prev"] = "new" if po + pk < 0.5 else ("same" if (po > pk) == (fig < 0.5) else "flip")
            if fig < 0.5:
                subject[box][hole] = False; removed += area; opened.append(row); opened_map[box] |= hole
            else:
                kept.append(row); kept_map[box] |= hole
        inset = max(1, round(3 * pixels.shape[1] / 1088))
        held = ndimage.binary_erosion(b > 0.75, iterations=inset) & ~subject
        subject |= held
        trimmed = 0
        if bedge is not None:   # --birefnet-edge: the outline too is the mask's, widened by a little - the key keeps a clip's pale glow as figure
            cap = ndimage.binary_dilation(b > 0.5, iterations=max(1, round(bedge * pixels.shape[1] / 1088)))
            trimmed = int((subject & ~cap).sum()); subject &= cap
        if report is not None:
            report.update(removed=removed, kept=kept, opened=opened, regained=int(held.sum()), trimmed=trimmed, maps=(opened_map, kept_map))
        soft = ndimage.gaussian_filter(subject.astype(np.float32), 0.7)
        return np.where(subject, np.maximum(soft, 0.55), soft * 0.5)
    if open_holes:
        scale = (pixels.shape[1] / 2176) ** 2
        labels, count = ndimage.label(subject & ~closed)
        removed, kept, opened, gbg, giou = 0, [], [], None, None
        filled0 = subject.copy()
        for index, box in enumerate(ndimage.find_objects(labels), 1):
            hole = labels[box] == index
            area = int(hole.sum())
            if area < 100 * scale:
                continue
            median = float(np.median(distance[box][hole]))
            spread = float(pixels[box][hole].std(0).max())
            centre = [int(v.mean()) + b.start for v, b in zip(np.nonzero(hole), box)]
            reason = None
            if area >= open_holes * scale and median <= flat[0] and spread <= flat[1]:
                reason = "flat"
            elif large and area >= large[0] * scale and spread <= large[1]:
                reason = "large"
            elif guide is not None and area >= open_holes * scale and spread <= 5:
                if gbg is None:
                    gbg, giou = guide_background(guide, filled0)
                frac = float(gbg[box][hole].mean())
                if frac >= guide_frac:
                    reason = f"guide {frac:.2f}"
            if reason:
                subject[box][hole] = False
                removed += area
                opened.append({"area": area, "distance": round(median, 1), "spread": round(spread, 1), "centre": centre, "reason": reason})
            else:
                kept.append({"area": area, "distance": round(median, 1), "spread": round(spread, 1), "centre": centre})
        if report is not None:
            report.update(removed=removed, kept=kept)
            if large or guide is not None:
                report.update(opened=opened)
                if giou is not None:
                    report.update(guide_iou=giou)
    # A soft edge one pixel wide, so the cut-out does not carry a hard stair-step.
    soft = ndimage.gaussian_filter(subject.astype(np.float32), 0.7)
    return np.where(subject, np.maximum(soft, 0.55), soft * 0.5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--direction", type=int, choices=(1, -1), required=True)
    parser.add_argument("--every", type=int, default=1)
    parser.add_argument("--last", type=int)
    parser.add_argument("--step", type=float, default=5.0)
    parser.add_argument("--slack", type=float, default=12.5)
    parser.add_argument("--threshold", type=int, default=26)
    parser.add_argument("--open-holes", type=int, default=0, metavar="AREA")
    parser.add_argument("--hole-flat", default="6,3.5", metavar="DIST,STD")
    parser.add_argument("--hole-large", default="", metavar="AREA,STD")
    parser.add_argument("--guide", type=Path)
    parser.add_argument("--guide-frac", type=float, default=0.5)
    parser.add_argument("--birefnet", type=Path)
    parser.add_argument("--birefnet-edge", type=float, metavar="PX",
                        help="with --birefnet: also cut the matte's outline back to the mask widened by PX pixels (at 1088; scaled with the frame). "
                             "For clips with a pale glow round the figure (depth-controlled lit-clay clips), which the colour key keeps as a 2-3 px grey "
                             "fringe and as filled gaps between fingers (villager 381, 2026-11-16)")
    args = parser.parse_args()
    (args.out / "views").mkdir(parents=True, exist_ok=True)
    files = sorted(args.frames.glob("*.png"))
    last = len(files) - 1 if args.last is None else args.last
    rows, thumbs, holes = [], [], {}
    flat = tuple(float(v) for v in args.hole_flat.split(","))
    large = tuple(float(v) for v in args.hole_large.split(",")) if args.hole_large else None
    guides = sorted(args.guide.glob("*.png")) if args.guide else []
    bdir = args.birefnet; prev = None
    for index in range(0, last + 1, args.every):
        image = Image.open(files[index]).convert("RGB")
        report = {}
        guide = Image.open(guides[index]) if index < len(guides) else None
        bmask = Image.open(bdir / f"{index:03d}.png") if bdir else None
        alpha = matte(image, args.threshold, args.open_holes, flat, report, large, guide, args.guide_frac, bmask, prev, args.birefnet_edge if bdir else None)
        if bdir:
            prev = report.pop("maps")
        if args.open_holes or bdir:
            holes[f"{index:03d}"] = report
        cut = np.dstack([np.asarray(image), (alpha * 255).astype(np.uint8)])
        name = f"{index:03d}"
        Image.fromarray(cut, "RGBA").save(args.out / "views" / f"{name}.png")
        row = {"name": f"orbit-{name}", "image": f"views/{name}.png", "frame": index}
        if not rows:
            row["azimuth"] = [-10, 10]
        else:
            expected = args.direction * args.step * args.every
            row["turn"] = sorted([expected - args.direction * args.slack / 2, expected + args.direction * args.slack])
        rows.append(row)
        check = Image.new("RGB", image.size, (255, 0, 255))
        check.paste(image, mask=Image.fromarray((alpha * 255).astype(np.uint8)))
        thumbs.append(check.resize((192, 192 * image.size[1] // image.size[0]), Image.LANCZOS))
    (args.out / "views.json").write_text(json.dumps(rows, indent=1) + "\n")
    if args.open_holes or bdir:
        (args.out / "holes.json").write_text(json.dumps(holes, indent=1) + "\n")
    columns = 12
    height = thumbs[0].size[1]
    sheet = Image.new("RGB", (192 * columns, height * -(-len(thumbs) // columns)), (255, 0, 255))
    for index, thumb in enumerate(thumbs):
        sheet.paste(thumb, (192 * (index % columns), height * (index // columns)))
    sheet.save(args.out / "mattes.png")
    print(args.out, len(rows), "views")


if __name__ == "__main__":
    main()
