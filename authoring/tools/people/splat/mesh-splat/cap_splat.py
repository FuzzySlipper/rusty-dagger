#!/usr/bin/env python3
"""Cover a splat's unseen top, or close its hollow underside, with a layer of plain Gaussians.

  python3 cap_splat.py SPLAT.npz OUT.npz [--top Y] [--bottom Y --shade 0.3] [--shade-below Y [--max-scale S]] [--cell 0.004]
      [--report OUT.json]

A splat fitted from eye-level pictures has nothing reliable where no picture
looked: the crown of a head is a patch of stray colours, and a skirt is an
open tube with noise inside. Both read as damage from above or below, and a
repaint of such a view draws the patch as an object.

--top Y lays Gaussians on the splat's upper surface wherever it rises above
height Y, coloured with the median colour of the opaque Gaussians in the band
just below Y. --bottom Y fills the splat's outline at height Y with a flat
layer facing down, coloured with the band just above Y times SHADE.
--shade-below Y adds nothing: it recolours the splat's own Gaussians below
height Y with the median of the band 0.03 above Y times SHADE. Use it on
shoe soles, which no picture looks at and which come out as bright speckle
from below; a flat --bottom layer there shows as a slab under a sole that
does not lie flat. --max-scale S also clamps every axis of those Gaussians
to at most S: oversized sole Gaussians reach past the toe and heel as a flat
paddle once the foot moves (the mobile-30 runtime clamped them to 0.005). The
caps are plain on purpose: a later repaint can draw detail on the top one,
and the bottom one only has to look like shadow. Heights are in the splat's
units (subject height about 1, Y up).
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy import ndimage


def layer(xz, y, colour, cell, flat):
    count = len(xz)
    means = np.column_stack([xz[:, 0], y, xz[:, 1]])
    spread = np.log(0.7 * cell)
    scales = np.column_stack([np.full(count, spread), np.full(count, np.log(1e-4) if flat else np.log(0.3 * cell)), np.full(count, spread)])
    quats = np.tile([1.0, 0.0, 0.0, 0.0], (count, 1))
    return {"means": means, "scales": scales, "quats": quats, "opacities": np.full(count, np.log(0.97 / 0.03)),
            "rgb": np.tile(colour, (count, 1))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("splat", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--top", type=float)
    parser.add_argument("--bottom", type=float)
    parser.add_argument("--shade", type=float, default=0.3)
    parser.add_argument("--shade-below", type=float)
    parser.add_argument("--max-scale", type=float)
    parser.add_argument("--cell", type=float, default=0.004)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    with np.load(args.splat) as src:
        splat = {key: src[key] for key in src.files}
    means = splat["means"].astype(np.float64)
    solid = 1 / (1 + np.exp(-splat["opacities"])) > 0.3
    report, added = {"splat": str(args.splat), "cell": args.cell}, []

    def grid(points):
        low = points.min(0) - 3 * args.cell
        shape = np.ceil((points.max(0) + 3 * args.cell - low) / args.cell).astype(int)
        index = np.floor((points - low) / args.cell).astype(int)
        return low, shape, index

    if args.top is not None:
        near = solid & (means[:, 1] > args.top - 0.03)
        low, shape, index = grid(means[near][:, [0, 2]])
        height = np.full(shape, -np.inf)
        np.maximum.at(height, (index[:, 0], index[:, 1]), means[near, 1])
        # Fill pits and pinholes so the cap bridges the patch that nothing painted.
        height = ndimage.grey_closing(np.where(np.isfinite(height), height, args.top - 0.05), size=(7, 7))
        cells = np.argwhere(height > args.top)
        band = solid & (means[:, 1] > args.top - 0.04) & (means[:, 1] <= args.top)
        colour = np.median(splat["rgb"][band], axis=0)
        xz = low + (cells + 0.5) * args.cell
        added.append(layer(xz, height[cells[:, 0], cells[:, 1]] + 0.5 * args.cell, colour, args.cell, flat=False))
        report["top"] = {"above": args.top, "gaussians": len(cells), "colour": [round(float(v), 3) for v in colour]}
    if args.bottom is not None:
        band = solid & (np.abs(means[:, 1] - args.bottom - 0.015) < 0.015)
        low, shape, index = grid(means[band][:, [0, 2]])
        outline = np.zeros(shape, bool)
        outline[index[:, 0], index[:, 1]] = True
        filled = ndimage.binary_fill_holes(ndimage.binary_closing(outline, iterations=4))
        cells = np.argwhere(ndimage.binary_erosion(filled, iterations=1))
        colour = np.median(splat["rgb"][band], axis=0) * args.shade
        xz = low + (cells + 0.5) * args.cell
        added.append(layer(xz, np.full(len(cells), args.bottom), colour, args.cell, flat=True))
        report["bottom"] = {"at": args.bottom, "gaussians": len(cells), "colour": [round(float(v), 3) for v in colour]}
    if args.shade_below is not None:
        below = means[:, 1] < args.shade_below
        band = solid & (means[:, 1] >= args.shade_below) & (means[:, 1] < args.shade_below + 0.03)
        colour = np.median(splat["rgb"][band], axis=0) * args.shade
        splat["rgb"] = splat["rgb"].copy()
        splat["rgb"][below] = colour.astype(splat["rgb"].dtype)
        report["shade_below"] = {"below": args.shade_below, "gaussians": int(below.sum()),
                                 "colour": [round(float(v), 3) for v in colour]}
        if args.max_scale is not None:
            limit = np.log(args.max_scale)
            over = below & (splat["scales"].max(1) > limit)
            splat["scales"] = splat["scales"].copy()
            splat["scales"][below] = np.minimum(splat["scales"][below], limit)
            report["shade_below"]["clamped"] = int(over.sum())
            report["shade_below"]["max_scale"] = args.max_scale
    for piece in added:
        for key in ("means", "scales", "quats", "opacities", "rgb"):
            splat[key] = np.concatenate([splat[key], piece[key].astype(splat[key].dtype)])
    for key in splat:   # per-Gaussian extras such as a seen count
        if key not in ("means", "scales", "quats", "opacities", "rgb") and len(splat[key]) == len(means):
            splat[key] = np.concatenate([splat[key], np.zeros((len(splat["means"]) - len(means), *splat[key].shape[1:]), splat[key].dtype)])
    np.savez_compressed(args.out, **splat)
    report["gaussians"] = len(splat["means"])
    if args.report:
        args.report.write_text(json.dumps(report, indent=1) + "\n")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
