#!/usr/bin/env python3
"""Daggerfall's paper-doll proportions as a standard, and a check of generated bodies and heads against it.

Every doll layer - body, head, each worn item per body morph - is drawn to the classic doll's proportions, so
the classic sprites are the standard. Landmarks are measured on silhouettes in doll space (the 110x184 grid,
see doll_preview.py), the same way for a classic source and a generated final:

  body: top (first row), shoulders (the widest row above the armpits, and its width), crotch (where the legs
        part), each sole and each foot's outer toe tip, height in heads (crown to lowest sole over the head's
        height) and the crotch's fraction of that height;
  head: crown, bottom and sides of the head layer (hair and the neck it carries included).

The outline check compares the whole silhouette: for each band of the classic body (neck and shoulders, arms
and torso, hips and thighs, shins, feet) the 95th-percentile distance from each outline to the other, in doll
units. A landmark or band beyond TOLERANCE fails.

    landmarks.py standard              # measure the classic sprites, write specs/paper-doll/proportions.json
    landmarks.py check [--paper-doll DIR ...]   # measure generated bodies and heads against it
"""
import argparse, json, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from doll_preview import AUTHORING, FINALS, doll_layer, placed

STANDARD = AUTHORING / "specs" / "paper-doll" / "proportions.json"
BODIES = [race + offset for offset in (0, 10) for race in range(8)]
K = 4                        # raster pixels per doll unit
FIELD = (-40, -20, 190, 230)  # the doll-space area rasterised: x, y, width, height
TOLERANCE = 2.0              # doll units
SMALL_HOLE = 16              # square doll units
HEADS = range(10)           # head records per FACE file
RATIOS = ("heads tall", "crotch fraction", "shoulders in heads")
BANDS = ("neck and shoulders", "arms and torso", "hips and thighs", "shins", "feet")


def silhouette(image, rect):
    """The layer's alpha > 0.5 on the doll-space field, K pixels per unit."""
    x, y, w, h = rect
    field = Image.new("L", (FIELD[2] * K, FIELD[3] * K))
    alpha = image.getchannel("A").resize((max(1, round(w * K)), max(1, round(h * K))), Image.Resampling.BILINEAR)
    field.paste(alpha, (round((x - FIELD[0]) * K), round((y - FIELD[1]) * K)))
    return np.array(field) > 127


def to_doll(px, axis):
    return round(px / K + FIELD[axis], 1)


def runs(row):
    d = np.diff(np.r_[0, row.astype(np.int8), 0])
    return list(zip(np.nonzero(d == 1)[0], np.nonzero(d == -1)[0]))


def body_landmarks(a):
    rows = np.nonzero(a.any(1))[0]
    top, bottom = rows.min(), rows.max()
    height = bottom - top
    # The torso's centre line: the mean column of the body between a fifth and half of its height.
    band = a[top + height // 5:top + height // 2]
    centre = int(round(np.nonzero(band)[1].mean()))
    # The crotch: the first row below the torso's middle where the centre line (within 3 units) opens a gap with
    # body on both sides that persists 3 units down. Arm gaps lie off the centre line; a tail crossing the
    # legs lower down closes the gap only below the crotch.
    near = slice(centre - 3 * K, centre + 3 * K)
    def parted(y):
        row = a[y]
        return any(not row[x] and row[:x].any() and row[x:].any() for x in range(near.start, near.stop))
    crotch = next(y for y in range(top + height // 2, bottom) if all(parted(y + d) for d in range(0, 3 * K)))
    feet = {}
    for side, columns in (("left", slice(0, centre)), ("right", slice(centre, None))):
        leg = np.zeros_like(a)
        leg[crotch:, columns] = a[crotch:, columns]
        ys, xs = np.nonzero(leg)
        sole = ys.max()
        foot = ys > sole - 10 * K
        feet[side] = {"sole": to_doll(sole + 1, 1),
                      "toe": to_doll(xs[foot].min() if side == "left" else xs[foot].max() + 1, 0)}
    # Shoulders: the widest row between the top and a third of the way down to the crotch.
    upper = range(top, top + (crotch - top) // 3)
    widths = [(max(e - s for s, e in runs(a[y])), y) for y in upper]
    width, shoulder = max(widths)
    return {"top": to_doll(top, 1), "shoulders": to_doll(shoulder, 1), "shoulder width": round(width / K, 1),
            "crotch": to_doll(crotch, 1), "sole left": feet["left"]["sole"], "sole right": feet["right"]["sole"],
            "toe left": feet["left"]["toe"], "toe right": feet["right"]["toe"], "centre": to_doll(centre, 0)}


def head_box(a):
    ys, xs = np.nonzero(a)
    return {"crown": to_doll(ys.min(), 1), "head bottom": to_doll(ys.max() + 1, 1),
            "head left": to_doll(xs.min(), 0), "head right": to_doll(xs.max() + 1, 0)}


def ratios(m):
    head = m["head bottom"] - m["crown"]
    height = max(m["sole left"], m["sole right"]) - m["crown"]
    return {"heads tall": round(height / head, 2), "crotch fraction": round((m["crotch"] - m["crown"]) / height, 3),
            "shoulders in heads": round(m["shoulder width"] / head, 2)}


def outline(family, name, record, mode):
    """A layer's silhouette, classic or generated (None when it has no generated final), small holes filled.

    A large hole stays open: an Argonian's tail encloses the gap between its legs."""
    layer = doll_layer(family, name, record, name)
    if mode == "canvas" and layer.final is None:
        return None
    a = silhouette(*placed(layer, mode, 1024, 0.74))
    holes, count = ndimage.label(ndimage.binary_fill_holes(a) & ~a)
    small = np.nonzero(ndimage.sum(np.ones_like(a), holes, range(1, count + 1)) < SMALL_HOLE * K * K)[0] + 1
    return a | np.isin(holes, small)


def measure(number, mode):
    """The body's landmarks and ratios (with head 0, generated if there is one), and its silhouette."""
    body = outline("bodies", f"body{number:02d}i0", 0, mode)
    if body is None:
        return None, None
    head = outline("faces", f"face{number:02d}i0", 0, mode)
    m = body_landmarks(body) | head_box(head if head is not None else outline("faces", f"face{number:02d}i0", 0, "classic"))
    return m | ratios(m), body


def band_rows(m):
    """The classic body's bands, in doll y."""
    cuts = [m["top"], m["shoulders"] + 8, m["crotch"], m["crotch"] + (max(m["sole left"], m["sole right"]) - m["crotch"]) * 0.55,
            min(m["sole left"], m["sole right"]) - 10, max(m["sole left"], m["sole right"])]
    return list(zip(BANDS, cuts[:-1], cuts[1:]))


def outline_deviation(standard, generated, m):
    """Per band, the 95th-percentile distance (doll units) between the two outlines, each to the other."""
    def edge(a):
        return a & ~ndimage.binary_erosion(a)
    to_standard = ndimage.distance_transform_edt(~edge(standard)) / K
    to_generated = ndimage.distance_transform_edt(~edge(generated)) / K
    out = {}
    for band, y0, y1 in band_rows(m):
        rows = slice(int((y0 - FIELD[1]) * K), int((y1 - FIELD[1]) * K))
        d = np.r_[to_standard[rows][edge(generated)[rows]], to_generated[rows][edge(standard)[rows]]]
        out[band] = round(float(np.percentile(d, 95)), 1) if d.size else None
    return out


def standard():
    bodies, heads = {}, {}
    for number in BODIES:
        bodies[f"body{number:02d}"], _ = measure(number, "classic")
        heads[f"face{number:02d}"] = {str(r): head_box(outline("faces", f"face{number:02d}i0", r, "classic")) for r in HEADS}
    STANDARD.write_text(json.dumps({"tolerance": TOLERANCE, "bodies": bodies, "heads": heads}, indent=1) + "\n")
    print("wrote", STANDARD)


def deviations(measured, want, tolerance):
    return [f"{key} {measured[key]} ({measured[key] - value:+.1f})" for key, value in want.items()
            if key not in RATIOS and abs(measured[key] - value) > tolerance]


def check():
    spec = json.load(open(STANDARD))
    tolerance, failed = spec["tolerance"], False
    for number in BODIES:
        name = f"body{number:02d}"
        m, body = measure(number, "canvas")
        if m is None:
            print(f"{name}: no generated body")
            continue
        want = spec["bodies"][name]
        bad = deviations(m, want, tolerance)
        for band, d in outline_deviation(outline("bodies", f"{name}i0", 0, "classic"), body, want).items():
            if d is not None and d > tolerance:
                bad.append(f"outline {band} {d}")
        print(f"{name}: {'ok' if not bad else 'FAIL'}  heads tall {m['heads tall']} ({want['heads tall']}), "
              f"crotch {m['crotch fraction']} ({want['crotch fraction']}), shoulders {m['shoulders in heads']} heads "
              f"({want['shoulders in heads']})" + "".join(f"\n    {b}" for b in bad))
        failed |= bool(bad)
    for number in BODIES:
        name = f"face{number:02d}"
        for record, want in spec["heads"][name].items():
            head = outline("faces", f"{name}i0", int(record), "canvas")
            if head is None:
                continue
            bad = deviations(head_box(head), want, tolerance)
            if bad:
                print(f"{name} head {record}: FAIL  " + ", ".join(bad))
            failed |= bool(bad)
    return failed


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=("standard", "check"))
    ap.add_argument("--paper-doll", type=Path, action="append", default=[],
                    help="body or head finals directory to look in first; repeat, first wins")
    a = ap.parse_args()
    FINALS["paper-doll"][:0] = [d.resolve() for d in a.paper_doll]
    if a.command == "standard":
        standard()
    else:
        sys.exit(1 if check() else 0)


if __name__ == "__main__":
    main()
