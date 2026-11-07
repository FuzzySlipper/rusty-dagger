#!/usr/bin/env python3
"""Make the whole image a held weapon draws as an icon: in the inventory, in shops and lying on the ground.

The paper doll draws a held weapon over the body's hand, so its accepted layer leaves a gap at the grip (heads.py --worn
trims it there). An icon has no hand in it, so it draws the weapon whole. The generation painted the grip across the
gap, so the cut-out from before the trim is the whole weapon in the accepted art's own pixels; this takes it from the
weapon's study, checks it matches the accepted layer where that is opaque, and keeps the trim's result everywhere else.

Some layers are drawn in pieces in the source as well, so the cut-out is too: a straight grip, haft or stave runs on
across each gap, made from the piece beside it moved along their shared axis by the gap's length, and a bowstring's free
end is drawn on to the bow's lower tip.

Only the records an icon draws need one: a weapon's own record (the katana's right-hand record, as the donor draws it)
and each artifact weapon's record, in both artifact archives. Each goes to gen-raw/worn/whole/KEY.png with the accepted
layer's sidecar, on the same canvas, and promote.py publishes it as the layer's `icon`.

    whole.py [KEY ...]      no key makes every one
"""
import argparse, shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

from doll_preview import AUTHORING

WORN = AUTHORING / "gen-raw/worn"
OUT = WORN / "whole"
STUDIES = AUTHORING / "gen-raw/studies"
WEAPONS = STUDIES / "worn-weapons-shared-bp-20261101/cutout"
ARTIFACTS = STUDIES / "worn-artifacts-bp-20261101/cutout"
# Each weapon template's icon record: its record, or the katana's right-hand one.
ICON_RECORDS = [0, 2, 4, 5, 7, 8, 10, 11, 12, 14, 16, 17, 19, 21, 22, 24, 25, 26]
# The artifact weapons' records (DaggerfallPaperDollPolicy.ArtifactRecords): the shields and the worn artifacts have no gap.
ARTIFACT_RECORDS = [0, 3, 5, 10, 13, 23, 24, 25]
SOLID = 128    # alpha over which a pixel is part of a piece
PIECE = 500    # pixels in the smallest piece; fewer are flecks or a detached ornament, such as the Staff of Magnus's pendant
NEAR = 120     # pixels from the other piece within which a piece's own axis is measured


def sources():
    """Each icon record's art key and its untrimmed cut-out; the flanged mace's design was painted with its gap, so its
    own layer is its source."""
    found = {f"texture-234-record-{r:03d}-frame-000": next(WEAPONS.glob(f"*-{r:03d}.png")) for r in ICON_RECORDS}
    found["texture-mace-record-014-frame-000"] = WORN / "texture-mace-record-014-frame-000.png"
    for r in ARTIFACT_RECORDS:
        male = next(ARTIFACTS.glob(f"*-m{r:03d}.png"))
        found[f"texture-432-record-{r:03d}-frame-000"] = male
        found[f"texture-433-record-{r:03d}-frame-000"] = next(iter(ARTIFACTS.glob(f"*-f{r:03d}.png")), male)
    return found


def pieces(solid):
    labels, count = ndimage.label(solid)
    return [labels == i for i in range(1, count + 1) if (labels == i).sum() >= PIECE]


def bridge(rgba, source, other):
    """Runs `source` on across the gap to `other`, along the axis of the source's end that faces it, by repeating a
    section from the middle of the source across the gap: its ends are often finished, a cap or a ferrule, which would
    repeat. The other piece's end is no guide to the axis: a sword's guard runs across it."""
    to_source, to_other = ndimage.distance_transform_edt(~source), ndimage.distance_transform_edt(~other)
    ys, xs = np.nonzero(source & (to_other < 2 * NEAR))
    axis = np.linalg.eigh(np.cov(np.vstack([xs, ys])))[1][:, 1]
    yy, xx = np.mgrid[:rgba.shape[0], :rgba.shape[1]]
    t = xx * axis[0] + yy * axis[1]
    if t[source].mean() > t[other].mean():
        axis, t = -axis, -t
    start, end = t[source & (to_other < NEAR)].max(), t[other & (to_source < NEAR)].min()
    gap = end - start
    low, high = t[source].min(), t[source].max()
    middle, half = (low + high) / 2, max(1.0, min(gap / 2 + 1, (high - low) / 4))
    section = rgba * (source & (np.abs(t - middle) <= half))[..., None]
    corridor = (t >= start - 1) & (t <= end + 1) & (np.minimum(to_source, to_other) < gap + 20)
    out = rgba.copy()
    placed = start - 1
    while placed < end + 1:
        by = placed - (middle - half)
        moved = ndimage.shift(section, (round(by * axis[1]), round(by * axis[0]), 0), order=0)
        take = corridor & (moved[..., 3] > out[..., 3])
        out[take] = moved[take]
        placed += 2 * half - 1
    return out


def bowstring(rgba):
    """Draws a bowstring that stops short on to the bow's lower tip: the string's end farthest from the upper tip to the
    bow's pixel farthest from it, in the string's own colour and width."""
    solid = rgba[..., 3] > SOLID
    thick = ndimage.binary_opening(solid, iterations=3)
    thin = solid & ~ndimage.binary_dilation(thick, iterations=2)
    sy, sx = np.nonzero(thin)
    by, bx = np.nonzero(thick)
    top = np.argmin(by)
    end = np.argmax((sy - by[top]) ** 2 + (sx - bx[top]) ** 2)
    tip = np.argmax((by - by[top]) ** 2 + (bx - bx[top]) ** 2)
    length = np.hypot(sy[end] - by[top], sx[end] - bx[top])
    width = max(2, round(thin.sum() / max(length, 1)))
    colour = np.median(rgba[thin][:, :3], 0)
    k = 4   # drawn larger and reduced, for a soft edge
    line = Image.new("L", (rgba.shape[1] * k, rgba.shape[0] * k))
    ImageDraw.Draw(line).line([(sx[end] * k, sy[end] * k), (bx[tip] * k, by[tip] * k)], fill=255, width=width * k)
    alpha = np.asarray(line.resize(rgba.shape[1::-1], Image.Resampling.LANCZOS), np.float32) * (rgba[..., 3] < 250)
    out = rgba.copy()
    w = alpha[..., None] / 255
    out[..., :3] = out[..., :3] * (1 - w) + colour * w
    out[..., 3] = np.maximum(out[..., 3], alpha)
    return out


def whole(key, source):
    final = np.asarray(Image.open(WORN / f"{key}.png").convert("RGBA"), np.float32)
    rgba = np.asarray(Image.open(source).convert("RGBA"), np.float32)
    kept = final[..., 3] > 250
    drift = np.abs(rgba[..., :3] - final[..., :3])[kept].mean()
    if drift > 1:
        raise SystemExit(f"{source} is not {key}'s art before its trim: it differs by {drift:.1f} where the layer is opaque.")
    joins, bow = 0, "bow" in source.name

    def solid(rgba):
        # A bow's string joins its limbs whatever gap the stave has, so a bow's pieces are its stave's alone.
        shape = ndimage.binary_closing(rgba[..., 3] > SOLID, iterations=2)
        return ndimage.binary_opening(shape, iterations=3) if bow else shape

    while len(parts := pieces(solid(rgba))) > 1:
        # The closest two pieces; the smaller runs on, so a head is never copied down a haft.
        pairs = [(ndimage.distance_transform_edt(~a)[b].min(), i, j) for i, a in enumerate(parts) for j, b in enumerate(parts) if i < j]
        _, i, j = min(pairs)
        small, large = sorted((parts[i], parts[j]), key=lambda part: part.sum())
        rgba = bridge(rgba, small, large)
        joins += 1
        if joins > 4:
            raise SystemExit(f"{key} is still in {len(parts)} pieces after four joins.")
    if bow:
        rgba = bowstring(rgba)
    OUT.mkdir(exist_ok=True)
    Image.fromarray((np.clip(rgba, 0, 255) + .5).astype(np.uint8), "RGBA").save(OUT / f"{key}.png")
    shutil.copyfile(WORN / f"{key}.json", OUT / f"{key}.json")
    print(key, f"from {source.name}, {joins} join{'s' * (joins != 1)}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("keys", nargs="*", help="art keys to make; none makes every one")
    found = sources()
    for key in parser.parse_args().keys or sorted(found):
        whole(key, found[key])


if __name__ == "__main__":
    main()
