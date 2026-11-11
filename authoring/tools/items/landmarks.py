#!/usr/bin/env python3
"""Daggerfall's paper-doll proportions as a standard, and a check of generated bodies and heads against it.

Every doll layer - body, head, each worn item per body morph - is drawn to the classic doll's proportions, so
the classic sprites are the standard. Landmarks are measured on silhouettes in doll space (the 110x184 grid,
see doll_preview.py), the same way for a classic source and a generated final:

  body: top (first row), shoulders (the widest rows above the armpits, with a span, and the width), crotch (where the legs
        part), each sole and each foot's outer toe tip, height in heads (crown to lowest sole over the head's
        height) and the crotch's fraction of that height;
  head: crown, bottom and sides of the head layer (hair and the neck it carries included).

The outline check compares the whole silhouette: for each band of the classic body (neck and shoulders, arms
and torso, hips and thighs, shins, feet) the 95th-percentile distance from each outline to the other, in doll
units. A landmark or band beyond TOLERANCE (two doll units, plus a gradual landmark's span) fails. Run the check on
bodies and heads before dressing (dress.py) or promoting them.

Every race of a morph wears one generated body shape (bodies.py), so a body is checked by morph: its body
landmarks and outline against the classic body of the morph's base race, and its head box against its own
race's classic head. The classic races of a morph differ by a doll unit or so (the Nord man's shoulders are
narrower than the Breton's), which one shared shape cannot follow.

    landmarks.py standard              # measure the classic sprites, write specs/paper-doll/proportions.json
    landmarks.py check [--paper-doll DIR ...]   # measure generated bodies and heads against it
    landmarks.py check --promoted               # the same for the promoted images, at their payload rects
"""
import argparse, json, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from functools import lru_cache

from bodies import MORPHS
from doll_preview import AUTHORING, FINALS, doll_layer, placed
from promote import CONTENT, PAYLOAD, replaces

STANDARD = AUTHORING / "specs" / "paper-doll" / "proportions.json"
BODIES = [race + offset for offset in (0, 10) for race in range(8)]
K = 4                        # raster pixels per doll unit
FIELD = (-40, -20, 190, 230)  # the doll-space area rasterised: x, y, width, height
TOLERANCE = 2.0              # doll units
SMALL_HOLE = 16              # square doll units
HEADS = range(10)           # head records per FACE file
SMOOTH = 3                   # doll units a leg's widths are averaged over
FOOT = 8                     # doll units above a sole the ankle is looked for
PARTED = 2                   # doll units below an armpit the arm has fully left the torso
FLAT = 0.5                   # doll units of width within which rows count as a narrowest place
RATIOS = ("heads tall", "crotch fraction", "shoulders in heads")
VARIANTS = {"i0": "bodies", "i1": "clothed bodies"}   # unclothed and clothed body layers, by file suffix
BANDS = ("neck and shoulders", "arms and torso", "hips and thighs", "shins", "feet")


def silhouette(image, rect):
    """The layer's alpha > 0.5 on the doll-space field, K pixels per unit.

    Each field pixel samples the image's alpha at the exact doll-space point of its centre, so a layer measures the
    same whatever its pixel size and however its transparent margins are cropped (promotion crops them and moves
    the rect to match). An image coarser than the field (a classic sprite) is sampled by whole pixels, keeping its
    square edges; a finer one (a generated final) bilinearly."""
    x, y, w, h = rect
    alpha = np.asarray(image.getchannel("A"), np.float32)
    rows = ((np.arange(FIELD[3] * K) + 0.5) / K + FIELD[1] - y) * alpha.shape[0] / h - 0.5
    columns = ((np.arange(FIELD[2] * K) + 0.5) / K + FIELD[0] - x) * alpha.shape[1] / w - 0.5
    grid = np.meshgrid(rows, columns, indexing="ij")
    coarse = alpha.shape[1] / w < K
    return ndimage.map_coordinates(alpha, grid, order=0 if coarse else 1, mode="grid-constant", cval=0) > 127


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
    # Shoulders: the widest rows between the top and a third of the way down to the crotch, a gradual widening like
    # the waist's narrowing, so located with a span (see limb_landmarks).
    widths = {y: max(e - s for s, e in runs(a[y])) for y in range(top, top + (crotch - top) // 3)}
    width = max(widths.values())
    rows = [y for y, w in widths.items() if w >= width - FLAT * K]
    return {"top": to_doll(top, 1), "shoulders": to_doll(np.mean(rows), 1), "shoulders span": round((max(rows) - min(rows)) / 2 / K, 1),
            "shoulder width": round(width / K, 1),
            "crotch": to_doll(crotch, 1), "sole left": feet["left"]["sole"], "sole right": feet["right"]["sole"],
            "toe left": feet["left"]["toe"], "toe right": feet["right"]["toe"], "centre": to_doll(centre, 0)}


def limb_landmarks(a, m, legs=True):
    """The arms, waist and legs of the classic pose, every race's: the viewer's left arm hangs with its fist out
    beside the hip, the right is set akimbo with its hand on the hip.

      armpit left/right: where that arm parts from the torso, the top of the longest run of rows (below the
        shoulders, above the crotch) with background between them;
      hand left: the row below the hanging arm's parted run, and the fist's outermost column (hand left out);
      hand right: the row where the akimbo arm's hand closes that gap at the hip;
      waist: the torso's narrowest rows from PARTED units below both arms' parting (the lower armpit, and where the
        akimbo hand closes its gap, so the arm has fully left the torso or settled on the hip) halfway to the
        crotch, and its width;
      knee/ankle left/right: each leg's narrowest rows (widths smoothed over SMOOTH units) in its upper part
        (15-50% of the leg) and its lower part (65% of the leg to FOOT units above the leg's sole).

    A clothed body is its unclothed body with underwear painted on, and a loincloth hides where the legs part, so
    its knees and ankles are measured on the unclothed body only (legs=False).

    A narrowest place is the middle of the rows within FLAT units of the narrowest width, and its "span" is half
    their extent: a leg or waist that narrows gradually locates it no closer than that, so the check allows it on
    top of the tolerance.
    """
    centre = int(round((m["centre"] - FIELD[0]) * K))
    shoulders, crotch = int(round((m["shoulders"] - FIELD[1]) * K)), int(round((m["crotch"] - FIELD[1]) * K))
    left, right, torso = {}, set(), {}
    for y in range(shoulders, crotch):
        row = runs(a[y])
        body = next(((s, e) for s, e in row if s <= centre < e), None)
        if body is None:
            continue
        torso[y] = body
        if any(e <= body[0] for s, e in row):
            left[y] = min(s for s, e in row if e <= body[0])
        if any(s >= body[1] for s, e in row):
            right.add(y)

    def longest(rows):
        blocks, block = [], []
        for y in sorted(rows):
            if block and y != block[-1] + 1:
                blocks.append(block)
                block = []
            block.append(y)
        return max(blocks + [block], key=len) if block else []

    out, parted = {}, []
    if hang := longest(left):
        out["armpit left"], out["hand left"] = to_doll(hang[0], 1), to_doll(hang[-1] + 1, 1)
        out["hand left out"] = to_doll(min(left[y] for y in hang), 0)
        parted.append(hang[0])
    if akimbo := longest(right):
        out["armpit right"], out["hand right"] = to_doll(akimbo[0], 1), to_doll(akimbo[-1] + 1, 1)
        parted.append(akimbo[-1] + 1)
    below = max(parted, default=shoulders) + PARTED * K
    def narrowest(name, widths):
        rows = [y for y, w in widths.items() if w <= min(widths.values()) + FLAT * K]
        out[name], out[f"{name} span"] = to_doll(np.mean(rows), 1), round((max(rows) - min(rows)) / 2 / K, 1)

    waist = {y: e - s for y, (s, e) in torso.items() if below <= y < (below + crotch) / 2}
    if waist:
        narrowest("waist", waist)
        out["waist width"] = round(min(waist.values()) / K, 1)
    if not legs:
        return out
    first = runs(a[crotch + K])
    for side, run in (("left", min(first, key=lambda r: r[1])), ("right", max(first, key=lambda r: r[0]))):
        # Follow the leg down from the crotch: the run that holds the previous row's leg middle.
        middle, widths = (run[0] + run[1]) // 2, {}
        for y in range(crotch + K, a.shape[0]):
            run = next(((s, e) for s, e in runs(a[y]) if s <= middle < e), None)
            if run is None:
                break
            widths[y], middle = run[1] - run[0], (run[0] + run[1]) // 2
        sole = max(widths)
        length = sole - crotch
        ys = np.array(sorted(widths))
        smooth = np.convolve([widths[y] for y in ys], np.ones(SMOOTH * K) / (SMOOTH * K), mode="same")
        for name, lo, hi in (("knee", crotch + 0.15 * length, crotch + 0.5 * length), ("ankle", crotch + 0.65 * length, sole - FOOT * K)):
            part = {int(y): w for y, w in zip(ys, smooth) if lo <= y < hi}
            if part:
                narrowest(f"{name} {side}", part)
    return out


def head_box(a):
    ys, xs = np.nonzero(a)
    return {"crown": to_doll(ys.min(), 1), "head bottom": to_doll(ys.max() + 1, 1),
            "head left": to_doll(xs.min(), 0), "head right": to_doll(xs.max() + 1, 0)}


def ratios(m):
    head = m["head bottom"] - m["crown"]
    height = max(m["sole left"], m["sole right"]) - m["crown"]
    return {"heads tall": round(height / head, 2), "crotch fraction": round((m["crotch"] - m["crown"]) / height, 3),
            "shoulders in heads": round(m["shoulder width"] / head, 2)}


def promoted(family, name, record):
    """The promoted image standing in for a layer and its doll rect, as the game draws it, or None."""
    entry = PROMOTED().get(replaces(f"{family}--{name}--{name}-record-{record:03d}-frame-000"))
    if entry is None:
        return None
    doll = entry["doll"]
    return Image.open(CONTENT / entry["path"]).convert("RGBA"), (doll["x"], doll["y"], doll["width"], doll["height"])


@lru_cache
def PROMOTED():
    return {entry["replaces"]: entry for entry in json.load(open(PAYLOAD))["artReplacements"]["images"]}


def outline(family, name, record, mode):
    """A layer's silhouette, small holes filled: classic, generated ("canvas", the local/art/generated final) or "promoted" (the
    image the game draws, at its payload rect); None when there is no generated or promoted image.

    A large hole stays open: an Argonian's tail encloses the gap between its legs."""
    layer = doll_layer(family, name, record, name)
    if mode == "promoted":
        found = promoted(family, name, record)
        if found is None:
            return None
        a = silhouette(*found)
    elif mode == "canvas" and layer.final is None:
        return None
    else:
        a = silhouette(*placed(layer, mode))
    holes, count = ndimage.label(ndimage.binary_fill_holes(a) & ~a)
    small = np.nonzero(ndimage.sum(np.ones_like(a), holes, range(1, count + 1)) < SMALL_HOLE * K * K)[0] + 1
    return a | np.isin(holes, small)


def measure(number, mode, variant="i0"):
    """The body's landmarks and ratios (with head 0, generated if there is one), and its silhouette."""
    body = outline("bodies", f"body{number:02d}{variant}", 0, mode)
    if body is None:
        return None, None
    head = outline("faces", f"face{number:02d}i0", 0, mode)
    m = body_landmarks(body)
    m |= limb_landmarks(body, m, legs=variant == "i0") | head_box(head if head is not None else outline("faces", f"face{number:02d}i0", 0, "classic"))
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
    bodies, heads = {section: {} for section in VARIANTS.values()}, {}
    for number in BODIES:
        for variant, section in VARIANTS.items():
            bodies[section][f"body{number:02d}"], _ = measure(number, "classic", variant)
        heads[f"face{number:02d}"] = {str(r): head_box(outline("faces", f"face{number:02d}i0", r, "classic")) for r in HEADS}
    STANDARD.write_text(json.dumps({"tolerance": TOLERANCE, **bodies, "heads": heads}, indent=1) + "\n")
    print("wrote", STANDARD)


def morph_base(number):
    """The body number whose classic shape the body's morph takes: its base race, of the same gender."""
    return next(races[0] for races in MORPHS.values() if number % 10 in races) + number - number % 10


def deviations(measured, want, tolerance):
    """Each landmark beyond tolerance (widened by the standard's span for a gradual narrowing), or missing."""
    return [f"{key} missing" if key not in measured else f"{key} {measured[key]} ({measured[key] - value:+.1f})"
            for key, value in want.items() if key not in RATIOS and not key.endswith(" span")
            and (key not in measured or abs(measured[key] - value) > tolerance + want.get(f"{key} span", 0))]


def body_report(spec, number, variant, mode="canvas"):
    """One generated body against the standard: its report line (each landmark or outline band beyond tolerance on
    its own line below), and whether any is."""
    tolerance, section = spec["tolerance"], VARIANTS[variant]
    name = f"body{number:02d}{variant}"
    m, body = measure(number, mode, variant)
    if m is None:
        return f"{name}: no {'promoted' if mode == 'promoted' else 'generated'} body", False
    base = f"body{morph_base(number):02d}"
    head = {key: spec[section][name[:6]][key] for key in ("crown", "head bottom", "head left", "head right")}
    want = spec[section][base] | head
    want |= ratios(want)
    bad = deviations(m, want, tolerance)
    for band, d in outline_deviation(outline("bodies", f"{base}{variant}", 0, "classic"), body, want).items():
        if d is not None and d > tolerance:
            bad.append(f"outline {band} {d}")
    return (f"{name}: {'ok' if not bad else 'FAIL'}{'' if base == name[:6] else f' (shape of {base}{variant})'}  heads tall {m['heads tall']} "
            f"({want['heads tall']}), crotch {m['crotch fraction']} ({want['crotch fraction']}), shoulders {m['shoulders in heads']} heads "
            f"({want['shoulders in heads']})" + "".join(f"\n    {b}" for b in bad)), bool(bad)


def head_report(spec, number, record, mode="canvas"):
    """One generated head against its classic head box: its report line, and whether it is beyond tolerance."""
    name = f"face{number:02d}"
    head = outline("faces", f"{name}i0", record, mode)
    if head is None:
        return f"{name} head {record}: no {'promoted' if mode == 'promoted' else 'generated'} head", False
    bad = deviations(head_box(head), spec["heads"][name][str(record)], spec["tolerance"])
    return f"{name} head {record}: " + ("ok" if not bad else "FAIL  " + ", ".join(bad)), bool(bad)


def check(mode="canvas"):
    spec = json.load(open(STANDARD))
    tolerance, failed = spec["tolerance"], False
    for number, variant in ((n, v) for n in BODIES for v in VARIANTS):
        line, bad = body_report(spec, number, variant, mode)
        print(line)
        failed |= bad
    for number in BODIES:
        for record in HEADS:
            line, bad = head_report(spec, number, record, mode)
            if bad:
                print(line)
            failed |= bad
    return failed


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=("standard", "check"))
    ap.add_argument("--promoted", action="store_true", help="check the promoted images, as the game draws them, not local/art/generated")
    ap.add_argument("--paper-doll", type=Path, action="append", default=[],
                    help="body or head finals directory to look in first; repeat, first wins")
    a = ap.parse_args()
    FINALS["paper-doll"][:0] = [d.resolve() for d in a.paper_doll]
    if a.command == "standard":
        standard()
    else:
        sys.exit(1 if check("promoted" if a.promoted else "canvas") else 0)


if __name__ == "__main__":
    main()
