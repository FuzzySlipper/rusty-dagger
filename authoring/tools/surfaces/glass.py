#!/usr/bin/env python3
"""Cut each refreshed window surface's glass mask from its own pixels (#10891): what the game lights at night.

A classic window record paints its glass in a colour the classic swaps by the hour, and the import cuts its mask from
that (Daggerfall.Import's ClassicWindowGlass). A refreshed window paints its own panes, so its glass is its own: the
pixels of the refreshed surface (its final, else the art pack's image), in the colours glass.json beside this script names for the
record, inside the record's box where it gives one (a colour that also falls in the wall around the window). A lone
stray pixel is dropped, and a highlight painted inside a pane is glass. A winter record names its own colours: snow over a pane is not glass.

glass.json is the authored part. `--suggest` prints, for a record, the refreshed colours most of whose pixels fall in
today's glass (the classic mask, read from an import with --classic-art, for reference only) and the box those colours'
main clusters span in the refreshed surface: a starting point to judge by eye on the sheet, never the mask itself.

Writes local/art/generated/surfaces/glass/texture-AAA-R.png, white glass over opaque black at the surface's size, and
a KEY.json naming the surface pixels it was cut from, which promote.py publishes as the surface entry's `glass` only
with that surface.

    glass.py [KEY ...]                  no key cuts every record glass.json names
    glass.py --suggest KEY ...
    glass.py --sheet OUT.png [KEY ...]  today's and the own glass, each by day, lit at night, and its mask
"""
import argparse, json, re, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "shared"))
from promote import CONTENT, GLASS as OUT, PAYLOAD, WINDOW_GLASS, pixels, staged, surface_finals  # noqa: E402

SPEC = HERE / "glass.json"
KEY = re.compile(r"texture-(\d+)-(\d+)")
SPECK = 2             # a lit patch of at most this many pixels is a stray, not a pane
HIGHLIGHT = 6         # a hole of at most this many pixels inside a pane is a painted highlight on the glass, not a bar
NIGHT = (1.0, .82, .45)   # the glow a sheet shows lit windows in


def surfaces():
    """Each refreshed surface's image, by key: its final where this checkout has one (a surface not yet promoted, or
    redrawn), else the art pack's."""
    payload = json.loads(PAYLOAD.read_text())["artReplacements"]
    return {**{f"texture-{entry['replaces']['archive']:03d}-{entry['replaces']['record']}": CONTENT / entry["path"]
               for entry in payload["surfaces"]}, **surface_finals("surfaces")}


def picture(path):
    return Image.open(path).convert("RGB")


def hexes(pixels):
    return np.array(["%02x%02x%02x" % tuple(p) for p in pixels.reshape(-1, 3)]).reshape(pixels.shape[:2])


def cut(path, spec):
    """The glass of a refreshed surface: its pixels in the named colours (inside the box, if any), less stray specks."""
    drawn = np.asarray(picture(path))
    lit = np.isin(hexes(drawn), spec["colours"])
    if box := spec.get("box"):
        inside = np.zeros_like(lit)
        inside[box[1]:box[3], box[0]:box[2]] = True
        lit &= inside
    patches, count = ndimage.label(lit)
    if count:
        sizes = ndimage.sum_labels(lit, patches, range(1, count + 1))
        lit = np.isin(patches, 1 + np.flatnonzero(sizes > SPECK))
    holes = ndimage.binary_fill_holes(lit) & ~lit
    gaps, count = ndimage.label(holes)
    if count:
        sizes = ndimage.sum_labels(holes, gaps, range(1, count + 1))
        lit |= np.isin(gaps, 1 + np.flatnonzero(sizes <= HIGHLIGHT))
    if not lit.any():
        raise SystemExit(f"{path.stem}: no pixel in {', '.join(spec['colours'])} is glass; name its panes' colours in {SPEC.name}.")
    out = np.zeros(lit.shape + (4,), np.uint8)
    out[..., :3] = lit[..., None] * 255
    out[..., 3] = 255
    return Image.fromarray(out, "RGBA")


def classic(key):
    archive, record = map(int, KEY.fullmatch(key).groups())
    path = staged(WINDOW_GLASS.format(archive=archive, record=record))
    if not path.exists():
        raise SystemExit(f"No classic {path.name} to compare with: import with --classic-art.")
    return Image.open(path).convert("L")


def suggest(key, path, share=.5, least=12, cluster=.15):
    """Colours most of whose pixels fall in today's glass, and the box of the window they paint: the bounds of the lit
    pixels' clusters (a pane's bars closed over) at least `cluster` the size of the largest, which leaves out the wall."""
    drawn = np.asarray(picture(path))
    today = np.asarray(classic(key).resize(drawn.shape[1::-1], Image.Resampling.BILINEAR)) > 127
    names = hexes(drawn)
    picks = []
    for colour in np.unique(names):
        where = names == colour
        if where.sum() >= least and (where & today).sum() / where.sum() >= share:
            picks.append(str(colour))
    lit = ndimage.binary_closing(np.isin(names, picks), iterations=3)
    patches, count = ndimage.label(lit)
    sizes = ndimage.sum_labels(lit, patches, range(1, count + 1))
    ys, xs = np.nonzero(np.isin(patches, 1 + np.flatnonzero(sizes >= cluster * sizes.max())))
    return {"colours": picks, "box": [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]}


def lit(day, mask):
    """A surface at night with its glass lit, as the emission adds the window's glow where the mask is white."""
    base = np.asarray(day, np.float32) / 255 * .25
    glow = np.asarray(mask.convert("L").resize(day.size, Image.Resampling.BILINEAR), np.float32)[..., None] / 255
    return Image.fromarray((np.clip(base + glow * np.array(NIGHT), 0, 1) * 255 + .5).astype(np.uint8))


def sheet(out, keys, spec, every):
    """Each record: today by day, at night and its mask, then the own surface by day, at night and its mask."""
    width, tiles = 96, []
    for key in keys:
        day, today, own = picture(every[key]), classic(key), cut(every[key], spec[key])
        size = (width, round(width * day.height / day.width))
        row = Image.new("RGB", (6 * (width + 4) + 4, size[1] + 18), (24, 24, 24))
        for n, image in enumerate((day, lit(day, today), today.convert("RGB"), day, lit(day, own), own.convert("RGB"))):
            row.paste(image.convert("RGB").resize(size, Image.Resampling.BOX), (4 + n * (width + 4) + (4 if n > 2 else 0), 16))
        ImageDraw.Draw(row).text((4, 2), f"{key}: today (day, night, mask) | own", fill=(255, 220, 90))
        tiles.append(row)
    columns = 4
    height = max(tile.height for tile in tiles)
    page = Image.new("RGB", (columns * (tiles[0].width + 8), (len(tiles) + columns - 1) // columns * (height + 8)), (40, 40, 40))
    for n, tile in enumerate(tiles):
        page.paste(tile, ((n % columns) * (tile.width + 8), n // columns * (height + 8)))
    page.save(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("keys", nargs="*")
    ap.add_argument("--suggest", action="store_true")
    ap.add_argument("--sheet", type=Path)
    a = ap.parse_args()
    every = surfaces()
    spec = json.loads(SPEC.read_text()) if SPEC.exists() else {}
    keys = a.keys or sorted(spec)
    if missing := sorted(set(keys) - set(every)):
        raise SystemExit(f"No refreshed surface final or art pack entry {', '.join(missing)}.")
    if a.suggest:
        for key in keys:
            print(json.dumps({key: suggest(key, every[key])}))
        return
    if unnamed := sorted(set(keys) - set(spec)):
        raise SystemExit(f"{SPEC.name} names no glass for {', '.join(unnamed)}.")
    if a.sheet:
        sheet(a.sheet, keys, spec, every)
        return
    OUT.mkdir(parents=True, exist_ok=True)
    for key in keys:
        cut(every[key], spec[key]).save(OUT / f"{key}.png")
        (OUT / f"{key}.json").write_text(json.dumps({"surface": pixels(Image.open(every[key]))}) + "\n")
    print(f"{len(keys)} glass masks in {OUT}")


if __name__ == "__main__":
    main()
