#!/usr/bin/env python3
"""Draw the masks that hide hair round a hood or helmet on the paper doll, as the refreshed art's own.

A worn record the classic draws with a mask (hoods, robes and cloaks in TEXTURE.235-242, helmets in 245-252, which the
import's paper-doll catalogue names by `maskMediaId`) clears what the doll drew beneath it where the mask is opaque: the
head, its hair and the body (src/ui/inventory.ts). Each mask here is drawn for the refreshed item over the refreshed heads
and bodies of the body that wears it, never from the classic mask: it covers the hair of every head of that body that
shows outside the item, down to the record's cut row and fading out over FEATHER doll units above it, and it spares

  - the item and whatever it encloses (a pixel with the item above it and on both sides: a visor's or a hood's opening),
  - the face, an ellipse per body,
  - the body, so the neck and shoulders stay and hair lying on them is kept.

So hair standing out round a helmet goes, and long hair falls on below the rim. The authored part is the spec,
authoring/specs/worn/hair-masks.json: each body's face ellipse (centre and radii in doll units) and each masking record's
cut row, or null for a record that hides nothing on the refreshed doll (a hoodless cloak).

Writes local/art/generated/worn/mask/KEY.png and a KEY.json sidecar with its `rect` in doll units (null: no mask), which
promote.py publishes as the worn entry's `mask` and `placement.mask`. Reads the art pack and the import's catalogue, so
run the import first.

    hairmask.py [KEY ...]           no key draws every masking record's
    hairmask.py --sheet OUT.png [KEY ...]
                                    each record at its worst race and face against today's doll, and OUT.json
                                    with each race and face's difference; today is the classic mask, read from an import
                                    with --classic-art, for comparison only
"""
import argparse, json
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

AUTHORING = Path(__file__).resolve().parents[2]
REPOSITORY = AUTHORING.parent
CONTENT = REPOSITORY / "content"
SPEC = AUTHORING / "specs/worn/hair-masks.json"
OUT = REPOSITORY / "local/art/generated/worn/mask"
IMPORTED = REPOSITORY / "local/build/import/staging/worldrpg/payloads/daggerfall.imported.json"
CLASSIC_MASKS = [REPOSITORY / "local/build/import" / tree / "worldrpg/media/character/paper-doll" for tree in ("staging", "classic-art")]

DOLL = (110, 184)   # the doll in doll units
SCALE = 4           # mask pixels a doll unit
FEATHER = 4         # doll units over which a mask fades out above its cut row
# The donor's bodies: which races share a paper-doll body, and the worn archives each body and gender draw from.
BODIES = {"argonian": ["argonian"], "elf": ["dark-elf", "high-elf", "wood-elf"], "human": ["breton", "nord", "redguard"],
          "khajiit": ["khajiit"]}
ARCHIVES = {**{235 + n: (body, "male") for n, body in enumerate(BODIES)}, **{239 + n: (body, "female") for n, body in enumerate(BODIES)},
            **{245 + n: (body, "female") for n, body in enumerate(BODIES)}, **{249 + n: (body, "male") for n, body in enumerate(BODIES)}}


def key_of(archive, record):
    return f"texture-{archive}-record-{record:03d}-frame-000"


@lru_cache(None)
def pack():
    return {entry["replaces"]: entry for entry in json.loads((CONTENT / "worldrpg/packs/daggerfall.art-replacements/payload.json")
                                                              .read_text())["artReplacements"]["images"]}


@lru_cache(None)
def catalogue():
    return json.loads(IMPORTED.read_text())


def masking():
    """The worn records the classic draws with a mask, by key."""
    return {key_of(item["archive"], item["record"]): item for item in catalogue()["paperDoll"]["items"] if item.get("maskMediaId")}


@lru_cache(None)
def layers(race):
    return {layer["layer"]: layer["mediaId"] for layer in catalogue()["characterPresentation"]["layers"] if layer["race"] == race}


def faces(race, gender):
    return sorted(int(name.rsplit(".", 1)[1]) for name in layers(race) if name.startswith(f"head.{gender}."))


@lru_cache(None)
def image(replaces):
    """A refreshed image and its doll rect."""
    entry = pack()[replaces]
    d = entry["doll"]
    return Image.open(CONTENT / entry["path"]).convert("RGBA"), (d["x"], d["y"], d["width"], d["height"])


def place(picture, rect, k):
    """A picture drawn into its doll rect on the doll at k pixels a unit, as premultiplied RGBA floats."""
    x, y, w, h = rect
    pw, ph = max(1, round(w * k)), max(1, round(h * k))
    out = np.zeros((DOLL[1] * k, DOLL[0] * k, 4), np.float32)
    src = np.asarray(picture.resize((pw, ph), Image.Resampling.LANCZOS), np.float32) / 255
    src[..., :3] *= src[..., 3:]
    ox, oy = round(x * k), round(y * k)
    x0, y0, x1, y1 = max(0, ox), max(0, oy), min(out.shape[1], ox + pw), min(out.shape[0], oy + ph)
    if x1 > x0 and y1 > y0:
        out[y0:y1, x0:x1] = src[y0 - oy:y1 - oy, x0 - ox:x1 - ox]
    return out


@lru_cache(None)
def alpha(replaces, k=SCALE):
    return place(*image(replaces), k)[..., 3]


@lru_cache(None)
def covered(body, gender, part):
    """Where any race of the body draws its heads (part "head") or bodies ("body"), at SCALE."""
    names = [f"head.{gender}.{face}" for face in range(64)] if part == "head" else [f"body.{gender}.unclothed", f"body.{gender}.clothed"]
    out = np.zeros((DOLL[1] * SCALE, DOLL[0] * SCALE), np.float32)
    for race in BODIES[body]:
        for name in names:
            if name in layers(race):
                out = np.maximum(out, alpha(layers(race)[name]))
    return out


def face(ellipse):
    cx, cy, rx, ry = ellipse
    y, x = (np.mgrid[:DOLL[1] * SCALE, :DOLL[0] * SCALE] + .5) / SCALE
    return ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1


def draw(key, spec):
    """The record's mask and its rect in doll units, or None where the spec says it hides nothing."""
    item = masking()[key]
    body, gender = ARCHIVES[item["archive"]]
    shape = spec["items"][key]
    if shape is None:
        return None
    worn = alpha(f"paper-doll.{item['archive']}.{item['record']}") > .5
    spared = face(spec["faces"][body][gender]) | (covered(body, gender, "body") > .5)
    # Enclosed: the item above and on both sides, unless reached from the top past the item, the face and the body above
    # the item's lowest row (the gap under a horn, not a visor's opening).
    enclosed = (np.maximum.accumulate(worn, 0) & np.maximum.accumulate(worn, 1)
                & np.maximum.accumulate(worn[:, ::-1], 1)[:, ::-1])
    free = ~worn & ~spared
    free[np.nonzero(worn.any(1))[0].max():] = False
    regions, _ = ndimage.label(free)
    enclosed &= ~np.isin(regions, regions[0][regions[0] > 0])
    hair = ndimage.binary_dilation(covered(body, gender, "head") > .02, iterations=1)
    keep = worn | enclosed | spared
    rows = (np.arange(DOLL[1] * SCALE)[:, None] + .5) / SCALE
    fade = np.clip((shape["cut"] - rows) / FEATHER, 0, 1)
    shown = (hair & ~keep) * fade
    ys, xs = np.nonzero(shown)
    if not len(ys):
        raise SystemExit(f"{key}: the mask hides nothing above row {shape['cut']}; give it null in {SPEC.name}.")
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    out = np.zeros((y1 - y0, x1 - x0, 4), np.uint8)
    out[..., :3] = 255
    out[..., 3] = (shown[y0:y1, x0:x1] * 255 + .5).astype(np.uint8)
    return Image.fromarray(out, "RGBA"), (x0 / SCALE, y0 / SCALE, (x1 - x0) / SCALE, (y1 - y0) / SCALE)


def write(key, mask):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / f"{key}.png").unlink(missing_ok=True)
    if mask is not None:
        mask[0].save(OUT / f"{key}.png")
    (OUT / f"{key}.json").write_text(json.dumps({"rect": None if mask is None else [round(v, 4) for v in mask[1]]}) + "\n")


def doll(key, race, face_index, mask, k=6):
    """The doll with only this record worn, as the UI composes it: body, clothed body and head, then what is drawn so far
    cleared where the mask is opaque, then the item, over the background."""
    item = masking()[key]
    _, gender = ARCHIVES[item["archive"]]
    names = layers(race)
    drawn = np.zeros((DOLL[1] * k, DOLL[0] * k, 4), np.float32)
    for name in (f"body.{gender}.unclothed", f"body.{gender}.clothed", f"head.{gender}.{face_index}"):
        drawn = place(*image(names[name]), k) + drawn * (1 - place(*image(names[name]), k)[..., 3:])
    if mask is not None:
        drawn *= 1 - place(*mask, k)[..., 3:]
    worn = place(*image(f"paper-doll.{item['archive']}.{item['record']}"), k)
    drawn = worn + drawn * (1 - worn[..., 3:])
    background = place(*image(names["background"]), k)
    return np.clip(drawn + background * (1 - drawn[..., 3:]), 0, 1)[..., :3]


def classic(key):
    item = masking()[key]
    name = f"paper-doll-{item['archive']}-{item['record']}-mask.png"
    path = next((root / name for root in CLASSIC_MASKS if (root / name).exists()), None)
    if path is None:
        raise SystemExit(f"No classic {name} to compare with: import with --classic-art.")
    return Image.open(path).convert("RGBA"), (item["x"], item["y"], item["width"], item["height"])


def sheet(out, keys, spec):
    """Each record at the race and face where its mask differs most from today's, two records a row: no mask, today, own,
    and the own doll with what differs from today in magenta."""
    tiles, report, tile, crop = [], [], 200, (22, 0, 88, 66)
    for key in keys:
        item = masking()[key]
        body, gender = ARCHIVES[item["archive"]]
        own, today = draw(key, spec), classic(key)
        worst = None
        for race in BODIES[body]:
            for index in faces(race, gender):
                a, b = doll(key, race, index, today), doll(key, race, index, own)
                differs = np.abs(a - b).max(-1) > 24 / 255
                report.append({"key": key, "race": race, "face": index, "differs": round(float(differs.sum()) / 36, 1)})
                if worst is None or differs.sum() > worst[0]:
                    worst = (differs.sum(), race, index, a, b, differs)
        _, race, index, a, b, differs = worst
        marked = b.copy()
        marked[differs] = (1, 0, 1)
        picture = Image.new("RGB", (4 * tile, tile + 16), (18, 18, 18))
        for n, shown in enumerate((doll(key, race, index, None), a, b, marked)):
            picture.paste(Image.fromarray((shown * 255 + .5).astype(np.uint8)).crop(tuple(v * 6 for v in crop)).resize((tile, tile)),
                          (n * tile, 16))
        cut = "hides nothing" if spec["items"][key] is None else f"cut {spec['items'][key]['cut']}"
        ImageDraw.Draw(picture).text((4, 2), f"{item['archive']}/{item['record']} {cut}, worst {race} face {index}: {worst[0] / 36:.0f} differ",
                                     fill=(255, 220, 90))
        tiles.append(picture)
        print(f"{key}: {worst[0] / 36:.1f} at {race} {index}", flush=True)
    width, height, gap = tiles[0].width, tiles[0].height, 12
    head = 22
    page = Image.new("RGB", (2 * width + gap, head + (len(tiles) + 1) // 2 * (height + gap)), (40, 40, 40))
    ImageDraw.Draw(page).text((4, 5), "each item: no mask | today (classic mask) | own mask | own, magenta where it differs from today (doll units squared)",
                              fill=(255, 255, 255))
    for n, picture in enumerate(tiles):
        page.paste(picture, ((n % 2) * (width + gap), head + n // 2 * (height + gap)))
    page.save(out)
    Path(out).with_suffix(".json").write_text(json.dumps(report, indent=0) + "\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("keys", nargs="*")
    ap.add_argument("--sheet", type=Path)
    a = ap.parse_args()
    spec = json.loads(SPEC.read_text())
    records = masking()
    if set(spec["items"]) != set(records):
        raise SystemExit(f"{SPEC.name} must name exactly the masking records: missing {sorted(set(records) - set(spec['items']))}, "
                         f"extra {sorted(set(spec['items']) - set(records))}.")
    keys = a.keys or sorted(records)
    if unknown := sorted(set(keys) - set(records)):
        raise SystemExit(f"Not a masking record: {', '.join(unknown)}.")
    if a.sheet:
        sheet(a.sheet, keys, spec)
        return
    for key in keys:
        write(key, draw(key, spec))
    print(f"{len(keys)} masks in {OUT}")


if __name__ == "__main__":
    main()
