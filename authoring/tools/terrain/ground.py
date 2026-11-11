#!/usr/bin/env python3
"""Ground tiles (rusty-dagger #10498): every climate and season's 56 records, from the export-terrain reference set
(local/art/reference/terrain), to refreshed finals in local/art/generated/terrain.

Every set shares one layout: pure ground records (water, the climate's three grounds, paving, a ploughed field) and the
transitions between them. So a set is a few materials and a map of which pixels show which:

  generate  Each material once, seamless, from the classic record that shows it (MATERIALS), Z-Image base image to image
            with the surfaces' Valheim wording kept low-contrast. Water, paving and the field are shared across
            climates, and a rain set uses its summer's materials, so a record stays the same ground in every set.
  build     For each set (SETS), each material brought to that set's own classic record: surfaces' crunch, then calm
            (lightness spread to CALM of itself, lifted to the classic's 5th percentile). A pure record is its material.
            A transition joins the materials LAYOUT names, so the classic's blended fringe never reads as a third ground.
            Classic pixels are blurred, since one pixel of a speckled ground says little, and each goes to the nearest of
            those materials. The fade follows that contour: each material's signed distance from its edge, wandered a
            little away from the tile's border (so a shoreline crosses into the next tile where the classic has it),
            through a softmax SHORE wide at water and SOFT between land. Weights take classic-sized grain, and at the shore
            the fringe takes the classic tile's own colours, its murk and weed. Pure ground keeps its material's pixels;
            a blended pixel takes the nearest colour the tile's materials or its classic use.

    ground.py generate --host den-m5 --part 0/2
    ground.py build
Writes local/art/generated/terrain/raw/MATERIAL.png, local/art/generated/terrain/CLIMATE-SEASON/texture-AAA-R.png and bases/.
"""
import argparse, csv, os, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

AUTHORING = Path(__file__).resolve().parents[2]
ART = AUTHORING.parent / "local" / "art"
sys.path.insert(0, str(AUTHORING / "tools/surfaces"))
import batch, reduce  # noqa: E402

RAW = ART / "reference/terrain"
OUT = ART / "generated/terrain"
SIZE = 128        # a refreshed tile: twice the classic's 64
GENERATE = 1024
CALM = 0.75
K = 5
BLUR = 1.5      # classic pixels
SOFT, SHORE = 3.0, 8.0    # fade widths, refreshed pixels: land against land, and at water
WANDER, BORDER = 3.0, 16  # how far an edge wanders, and how far in from the tile's border it starts to
GRAIN = 0.45
FRINGE = 0.85   # how much of the classic's colour the shore's middle takes
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
    "water": (302, 0, 2, "Still deep water seen from above: soft mottled blues, broad dim patches of darker and lighter blue blending into each other, a faint diagonal shimmer, no swirls, no ripple lines, no waves, no foam, no spots"),
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
# Which pure records each transition joins: the one layout every set shares.
LAYOUT = {**{r: (1, 0) for r in (4, 5, 6, 7, 8, 48)}, **{r: (1, 2) for r in (9, 10, 11, 12, 13, 51)},
          **{r: (2, 3) for r in (14, 15, 16, 17, 18, 53)}, **{r: (2, 0) for r in (19, 20, 21, 22, 23, 49)},
          **{r: (1, 3) for r in (24, 25, 26, 27, 28, 52)}, **{r: (3, 0) for r in (29, 30, 31, 32, 33, 50)},
          **{r: (0, 1, 2) for r in (34, 37, 40)}, **{r: (0, 2, 3) for r in (35, 41, 44)},
          **{r: (0, 1, 3) for r in (36, 38, 43)}, **{r: (1, 2, 3) for r in (39, 42, 45)}, 47: (1, 46), 55: (2, 46)}
# A swamp summer tile the classic left in the winter set, without snow: it takes the summer's materials.
STRAYS = {(403, 35): 402}
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


def weights(source, archive, record):
    """The materials a transition joins and each pixel's weight for them."""
    pure = SETS[source]
    materials = list(dict.fromkeys(pure[r] for r in LAYOUT[record]))
    trees = [cKDTree(np.concatenate([np.asarray(blurred(classic(source, r)), np.float32).reshape(-1, 3)
                                     for r, m in pure.items() if m == material and r != FIELD])) for material in materials]
    pixels = np.asarray(wrap_upscale(blurred(classic(archive, record)), 1, SIZE), np.float32).reshape(-1, 3)
    distances = np.stack([tree.query(pixels, k=K)[0].mean(1) for tree in trees], 1)
    labels = ndimage.median_filter(distances.argmin(1).reshape(SIZE, SIZE), size=5, mode="wrap")
    wander = ndimage.gaussian_filter(np.random.default_rng(source * 100 + record).normal(0, 1, (SIZE, SIZE)), 7, mode="wrap")
    ramp = np.minimum(np.minimum(np.arange(SIZE), np.arange(SIZE)[::-1]) / BORDER, 1)
    wander *= WANDER / wander.std() * np.outer(ramp, ramp)
    fields = []
    for k, material in enumerate(materials):
        inside = np.tile(labels == k, (3, 3))
        signed = ndimage.distance_transform_edt(inside) - ndimage.distance_transform_edt(~inside)
        fields.append(signed[SIZE:2 * SIZE, SIZE:2 * SIZE] + (wander if material == "water" else -wander))
    w = np.exp(np.clip(np.stack(fields, -1) / (SHORE if "water" in materials else SOFT), -30, 30))
    return materials, w / w.sum(-1, keepdims=True)


def blend(archive, record, materials, w, bases):
    stack = np.stack([np.asarray(bases[m].convert("RGB"), np.float32) for m in materials], -2)
    noise = np.random.default_rng(archive * 100 + record).normal(0, 1, (SIZE // 2, SIZE // 2, w.shape[-1])).repeat(2, 0).repeat(2, 1)
    grained = np.clip(w + noise * GRAIN * (1 - w.max(-1, keepdims=True)), 0, None) + 1e-6
    grained /= grained.sum(-1, keepdims=True)
    mixed = (stack * grained[..., None]).sum(-2)
    tile = classic(archive, record)
    if "water" in materials:
        wet = grained[..., materials.index("water")]
        shore = ((4 * wet * (1 - wet)) ** 0.6 * FRINGE)[..., None]
        mixed = mixed * (1 - shore) + np.asarray(wrap_upscale(tile, 1, SIZE), np.float32) * shore
    colours = np.unique(np.concatenate([stack.reshape(-1, 3), np.asarray(tile, np.float32).reshape(-1, 3)]), axis=0)
    out = colours[cKDTree(colours).query(mixed.reshape(-1, 3))[1]].reshape(mixed.shape)
    pure = grained.max(-1) > 0.97
    out[pure] = stack[pure, grained.argmax(-1)[pure]]
    return Image.fromarray(out.astype(np.uint8))


def build_set(archive, pure, built):
    folder = OUT / folders()[archive]
    (folder / "bases").mkdir(parents=True, exist_ok=True)
    # Each material from the first record that shows it.
    first = {}
    for record, material in sorted(pure.items()):
        first.setdefault(material, record)
    bases = built[archive] = {}
    for material, record in first.items():
        reference = classic(archive, record)
        bases[material] = calm(reduce.crunch(OUT / "raw" / f"{material}.png", reference), reference)
        bases[material].save(folder / "bases" / f"{material}.png")
    for record in range(RECORDS):
        final = folder / f"texture-{archive:03d}-{record}.png"
        if record in pure:
            bases[pure[record]].save(final)
            continue
        source = STRAYS.get((archive, record), archive)
        materials, w = weights(source, archive, record)
        blend(archive, record, materials, w, built[source]).save(final)
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
        built = {}
        for archive, pure in SETS.items():
            build_set(archive, pure, built)


if __name__ == "__main__":
    main()
