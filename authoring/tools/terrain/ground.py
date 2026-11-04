#!/usr/bin/env python3
"""Ground tiles (rusty-dagger #10498): every climate and season's 56 records, from the export-terrain reference set
(authoring/df-raw/terrain), to refreshed finals in authoring/gen-raw/terrain.

Every set shares one layout: pure ground records (water, the climate's three grounds, paving, a ploughed field) and the
transitions between them. So a set is a few materials and a map of which pixels show which:

  generate  Each material once, seamless, from the classic record that shows it (MATERIALS), Z-Image base image to image
            with the surfaces' Valheim wording kept low-contrast. Water, paving and the field are shared across
            climates, and a rain set uses its summer's materials, so a record stays the same ground in every set.
  build     For each set (SETS), each material brought to that set's own classic record: surfaces' crunch, then calm
            (lightness spread to CALM of itself, lifted to the classic's 5th percentile). A pure record is its material.
            A transition is labelled at the refreshed size. Classic pixels are blurred first, since one pixel of a speckled
            ground says little; each pixel of the classic upscaled with wrap goes to the material whose classic pixels lie
            nearest (mean of K); materials under SHARE of the tile are dropped as its blended fringe, the rest take the
            tile, and a wrapping 5x5 majority makes organic edges. Only the paved records draw paving.

    ground.py generate --host den-m5 --part 0/2
    ground.py build
Writes gen-raw/terrain/raw/MATERIAL.png, gen-raw/terrain/CLIMATE-SEASON/texture-AAA-R.png and bases/.
"""
import argparse, csv, os, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

AUTHORING = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(AUTHORING / "tools/surfaces"))
import batch, reduce  # noqa: E402

RAW = AUTHORING / "df-raw/terrain"
OUT = AUTHORING / "gen-raw/terrain"
SIZE = 128        # a refreshed tile: twice the classic's 64
GENERATE = 1024
CALM = 0.75
K = 5
BLUR = 1.5      # classic pixels
SHARE = 0.08
RECORDS = 56
GROUND = ("Seamless tileable square ground texture for a dark fantasy RPG, seen straight down from above, flat orthographic, "
          "fills the whole frame edge to edge, continues evenly off all four edges with no focal point. Hand-painted "
          "painterly texture in the manner of Valheim and classic 2000s games: an even fine-grained low-contrast surface "
          "with economical brush marks, no dark shadows or holes, no photographic micro-detail, soft flat even lighting, "
          "no cast shadows, no highlights, no objects, no text.")
NEGATIVE = batch.NEGATIVE + ", dark holes, deep shadows"
# Each material: the classic archive and record it starts from, how many times that tile repeats across the refreshed
# one (2 makes its features half the size: a ground, not a paving stone), and what it is.
MATERIALS = {
    "water": (302, 0, 2, "Calm open water surface: an even deep cool blue with fine soft lighter blue ripple streaks spread evenly everywhere, no spots, bubbles, blobs, lily pads or stones"),
    "dirt": (302, 1, 2, "Packed earth road surface: warm tan and ochre dirt, finely grained, with scattered tiny pebbles and faint wheel-worn streaks, no large stones"),
    "grass": (302, 2, 2, "Dense short turf grass covering everything evenly: countless fine blades in mid and dark greens, a fine even texture with no separate tufts, no bare soil, no holes or dark spots"),
    "gravel": (302, 3, 2, "Fine loose gravel: thousands of small grey stone chips, each smaller than a thumbnail, densely packed into an even fine-grained grey surface, no large stones or cobbles"),
    "paving": (302, 46, 1, "Old cobblestone paving: irregular pale grey flagstones and cobbles fitted with dark narrow joints"),
    "field": (302, 54, 1, "Ploughed farm field: straight parallel furrows of tilled tan earth running from top to bottom, evenly spaced soft ridges and troughs, no plants"),
    "mountain-earth": (102, 1, 2, "Rough upland earth: dark brown soil with thin patches of dry olive grass, fine and even, no stones"),
    "mountain-moss": (102, 2, 2, "Upland moss and heath: low dark green moss and scrubby grass mottled with brown soil, fine and even"),
    "mountain-rock": (102, 3, 2, "Weathered dark grey rock surface with fine grit and faint cracks, flat and even, no boulders"),
    "bog-moss": (402, 1, 2, "Wet bog moss: dark blue-green sphagnum and fine marsh weed spread evenly, no water, no flowers"),
    "marsh-earth": (402, 2, 2, "Boggy reddish-brown peat mottled with dark green weed, fine and even, no stones"),
    "marsh-grass": (402, 3, 2, "Dense dark marsh grass: long fine blades lying in soft swathes of deep green, covering everything evenly"),
    "mud-stones": (404, 46, 1, "Rain-soaked mud with rounded tan stones half sunk in dark wet earth and small green weeds between them"),
    "fine-sand": (2, 1, 2, "Fine pale desert sand: an even warm tan surface of tiny grains with soft faint ripples, no stones"),
    "pebbled-sand": (2, 2, 2, "Coarse desert ground: warm tan sand packed with small rounded pebbles and a few faint purple grains, fine and even"),
    "cracked-clay": (2, 3, 2, "Dry cracked desert clay: hard reddish-brown baked earth with a fine network of thin shallow cracks"),
    "snow": (303, 1, 2, "Fresh even snow: a soft bright white surface with faint pale blue-grey shading and gentle shallow drifts, no objects"),
    "snow-patches": (403, 1, 1, "Soft white snow patches lying on grey frozen marsh ground, sparse frosted weeds between them"),
    "snow-tufts": (403, 2, 2, "Fresh even snow with a few thin frosted green marsh blades showing through it"),
    "snow-grass": (403, 3, 1, "Fresh snow with dark green marsh grass tufts breaking through in small clumps"),
}
FIELD = 54
PAVED = (47, 55)                     # the transitions that draw paving
UNBLENDED = {"field", "mud-stones"}  # pure records alone: no transition draws them
SUMMER = {0: "water", 46: "paving", FIELD: "field"}
WINTER = {0: "water", 1: "snow", 2: "snow", 3: "snow", 46: "paving", FIELD: "snow"}
TEMPERATE = SUMMER | {1: "dirt", 2: "grass", 3: "gravel"}
MOUNTAIN = SUMMER | {1: "mountain-earth", 2: "mountain-moss", 3: "mountain-rock"}
SWAMP = SUMMER | {1: "bog-moss", 2: "marsh-earth", 3: "marsh-grass"}
DESERT = SUMMER | {1: "fine-sand", 2: "pebbled-sand", 3: "cracked-clay"}
# Each set's pure records and their materials. Swamp rain's records 46-47 are stones in mud and 54-55 its field, in place
# of the others' paving and paving transitions.
SETS = {
    2: DESERT, 4: DESERT, 102: MOUNTAIN, 103: WINTER, 104: MOUNTAIN, 302: TEMPERATE, 303: WINTER, 304: TEMPERATE,
    402: SWAMP, 403: WINTER | {1: "snow-patches", 2: "snow-tufts", 3: "snow-grass"},
    404: SWAMP | {46: "mud-stones", 47: "mud-stones", 55: "field"},
}


def folders():
    return {int(r["archive"]): f"{r['climate']}-{r['season']}" for r in csv.DictReader(open(RAW / "tiles.csv"))}


def classic(archive, record):
    name = f"texture-{archive:03d}-record-{record:03d}-frame-000-source.png"
    return Image.open(RAW / folders()[archive] / f"texture-{archive:03d}" / "tile" / name).convert("RGB")


def wrap_upscale(tile, repeats, size):
    """The tile repeated `repeats` x `repeats`, upscaled bicubic to `size` with wrap (a 3x3 border cropped away)."""
    block = np.tile(np.asarray(tile), (repeats, repeats, 1))
    big = Image.fromarray(np.tile(block, (3, 3, 1))).resize((size * 3, size * 3), Image.Resampling.BICUBIC)
    return big.crop((size, size, size * 2, size * 2))


def blurred(image):
    """The classic tile blurred with wrap."""
    return Image.fromarray(ndimage.gaussian_filter(np.asarray(image, np.float32), (BLUR, BLUR, 0), mode="wrap").astype(np.uint8))


def calm(image, reference):
    """Lightness spread compressed to CALM of itself, darkest values lifted to the reference's 5th percentile."""
    values, target = reduce.lab(image), reduce.lab(reference)
    lightness = values[..., 0]
    mean = lightness.mean()
    values[..., 0] = np.maximum(mean + (lightness - mean) * CALM, np.percentile(target[..., 0], 5))
    return reduce.unlab(values)


def generate_materials(host, part):
    index, parts = map(int, part.split("/"))
    os.environ["COMFY_URL"] = batch.HOSTS[host]
    sys.path.insert(0, str(batch.AP / "tools/lora-training"))
    import comfy, generate  # noqa: E402,E401
    (OUT / "raw").mkdir(parents=True, exist_ok=True)
    todo = [name for name in MATERIALS if not (OUT / "raw" / f"{name}.png").exists()]
    for name in todo[index::parts]:
        archive, record, repeats, subject = MATERIALS[name]
        init = OUT / "raw" / f"{name}-init.png"
        wrap_upscale(classic(archive, record), repeats, GENERATE).save(init)
        batch.generate_raw(comfy, generate, f"{subject}. {GROUND}", init, (GENERATE, GENERATE), "enable", 0.55,
                           OUT / "raw" / f"{name}.png", NEGATIVE)
        print(name, flush=True)
    print("GENERATE-DONE", flush=True)


def build_set(archive, pure):
    folder = OUT / folders()[archive]
    (folder / "bases").mkdir(parents=True, exist_ok=True)
    # Each material from the first record that shows it, and that record's pixels stand for it in labelling.
    first = {}
    for record, material in sorted(pure.items()):
        first.setdefault(material, record)
    bases = {}
    for material, record in first.items():
        reference = classic(archive, record)
        bases[material] = calm(reduce.crunch(OUT / "raw" / f"{material}.png", reference), reference)
        bases[material].save(folder / "bases" / f"{material}.png")
    trees = {material: cKDTree(np.concatenate([np.asarray(blurred(classic(archive, r)), np.float32).reshape(-1, 3)
                                               for r, m in pure.items() if m == material and r != FIELD]))
             for material in {m for r, m in pure.items() if r != FIELD} - UNBLENDED}
    for record in range(RECORDS):
        final = folder / f"texture-{archive:03d}-{record}.png"
        if record in pure:
            bases[pure[record]].save(final)
            continue
        pixels = np.asarray(wrap_upscale(blurred(classic(archive, record)), 1, SIZE), np.float32).reshape(-1, 3)
        materials = [m for m in trees if m != "paving" or record in PAVED]
        distances = np.stack([trees[m].query(pixels, k=K)[0].mean(1) for m in materials], 1)
        shares = np.bincount(distances.argmin(1), minlength=len(materials)) / len(pixels)
        kept = [k for k, share in enumerate(shares) if share >= SHARE]
        nearest = np.array(kept)[distances[:, kept].argmin(1)].reshape(SIZE, SIZE)
        votes = np.stack([nearest == k for k in kept], -1).astype(np.int32)
        labels = np.array(kept)[sum(np.roll(np.roll(votes, dy, 0), dx, 1) for dy in range(-2, 3) for dx in range(-2, 3)).argmax(-1)]
        out = np.zeros((SIZE, SIZE, 3), np.uint8)
        for k in kept:
            out[labels == k] = np.asarray(bases[materials[k]].convert("RGB"))[labels == k]
        Image.fromarray(out).save(final)
    print(folder.name, len(first), "materials", flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("step", choices=("generate", "build"))
    ap.add_argument("--host", choices=list(batch.HOSTS))
    ap.add_argument("--part", default="0/1")
    a = ap.parse_args()
    if a.step == "generate":
        generate_materials(a.host, a.part)
    else:
        for archive, pure in SETS.items():
            build_set(archive, pure)


if __name__ == "__main__":
    main()
