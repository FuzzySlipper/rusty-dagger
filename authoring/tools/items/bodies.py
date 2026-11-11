#!/usr/bin/env python3
"""Give the races that share a body morph one generated body shape, and make each clothed body its unclothed one.

The classic game draws every race of a morph (the three humans, the three elves) with one silhouette, and a
race's clothed body (I1) is its unclothed body (I0) with underwear painted on; worn items are drawn once per
morph on that shape. Generated bodies were each made separately, so their shapes drift apart. This rebuilds
them from one base body per morph and gender:

  - the base I0: conformed to the classic silhouette (`conform`), so the morph takes Daggerfall's proportions
    (landmarks.py checks them);
  - I0 of another race in the morph: the base I0 recoloured to that race's skin by matching the mean (and,
    within limits, the spread) of the race's current generated body in OKLab, so the base's shape and detail stay exact (Qwen,
    asked to recolour, pushed Nords to chalk white and High Elves to flat yellow);
  - I1 of every race: its I0 with the classic underwear (the pixels where classic I1 differs from I0) pasted
    on by whole pixels and repainted by Qwen in a feathered mask a doll unit around it, the alpha being the I0 silhouette
    plus whatever the underwear adds past it (keyed against the flat background, and at least the classic's own cloth
    across a gap the I0 encloses, which Qwen sometimes paints as background).

Finals keep the paper-doll naming and the base's generation canvas, with a sidecar JSON stating the base's
doll-space rect (every race of the morph is drawn there, not at its own classic offset), so
doll_preview.py --paper-doll OUT/finals draws them and they can replace local/art/generated/paper-doll. Inputs, Qwen records and the finals stay in OUT.

    bodies.py OUT [--gender male --gender female] [--base human=00 --base elf=03]
"""
import argparse, json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
from PIL import Image
from scipy import ndimage

from doll_preview import ART, GROUND, doll_layer, placed
from dress import mask_image, qwen
from postprocess import cutout, oklab_to_srgb, srgb_to_oklab

MORPHS = {"human": [0, 1, 2], "elf": [3, 4, 5], "khajiit": [6], "argonian": [7]}
GENERATED = ART / "generated" / "paper-doll"
CLOTH_MIN = 60  # source pixels: smaller differences between classic I1 and I0 are not underwear
CLOTH_SHADE = 0.03  # OKLab distance between classic I1 and I0 under which a pixel is the same skin redrawn
CLOTH_REACH = 1  # doll units the underwear's repaint may extend past it: Qwen paints glossy blotches on the skin it reaches
KEY = (40, 90)  # distance from the flat background over which a repainted pixel fades into the figure

UNDERWEAR = ("The figure wears crude, pixelated, low-resolution simple cloth underwear. Repaint only that underwear as "
             "finely painted cloth actually worn on this body, fitted to it, in the figure's own painting style and light, "
             "with soft contact shadow. Keep its shape, the parts of the body it covers and exactly its colours as the "
             "pixelated cloth shows them. Do not change the body, the pose or the flat background, and add nothing else.")
CONFORM = ("Image 1 is a crude, pixelated, low-resolution figure with no head. Repaint it as a finely painted figure in "
           "exactly the painting style, skin, light and anatomical detail of image 2, but with image 1's exact silhouette "
           "and proportions: the neck, shoulders, arms, hands, legs, feet, toes and any tail exactly where and as large as image 1 "
           "has them, nothing wider, longer or higher, and nothing left out.{tail} Keep the headless neck and the flat background.")
# Qwen leaves a classic tail out unless its path is named.
TAIL = {"khajiit": " Image 1's long thin tail hangs from behind the hips and curls down between the legs to the ground; paint "
                   "it as a furred tail exactly along that path.",
        "argonian": " Image 1's long thin tail hangs from behind the hips and curls down between the legs to the ground; "
                    "paint it as a scaled tail exactly along that path."}
# Qwen paints a loincloth as a brief unless its flap is named: the classic elf, Khajiit and Argonian men wear one.
LOINCLOTH = (" The underwear is a loincloth: a band of cloth round the hips, knotted at the front, with a broad flap of "
             "the same cloth hanging straight down in front over the groin to mid-thigh. Keep the hanging flap exactly as "
             "long and as wide as the pixelated cloth shows it; it is not a brief.")
CLOTHS = {3: LOINCLOTH, 4: LOINCLOTH, 5: LOINCLOTH, 6: LOINCLOTH, 7: LOINCLOTH}
# Nor the Khajiit woman's arm held out from her hip: the style reference has none there.
LIMB = {16: " Image 1's right arm (on the left of the picture) is held out from the body, angled down and away from the "
            "hip with the hand clear of the thigh; paint the whole arm and hand exactly along that path, in skin."}


def on_ground(rgba):
    figure = Image.new("RGBA", rgba.size, GROUND)
    figure.alpha_composite(rgba)
    return figure.convert("RGB")


def recolour(base, reference):
    """The base figure with the reference figure's colour: OKLab mean matched, and spread within limits."""
    def lab(image):
        a = np.array(image, np.float32) / 255
        return srgb_to_oklab(a[..., :3]), a[..., 3] > 0.5
    b, inside = lab(base)
    r, theirs = lab(reference)
    # A near-grey base has almost no chroma spread; scaling it to a coloured race's would amplify its noise.
    spread = np.clip(r[theirs].std(0) / b[inside].std(0), 0.5, 1.5)
    matched = (b - b[inside].mean(0)) * spread + r[theirs].mean(0)
    # Colour scales with lightness up to the race's average, as skin's does: a constant shift oversaturates the
    # shadows, the base's own hue shifts at full strength turn its near-black creases blue, and highlights are
    # lighter than the average but no more coloured.
    shade = np.clip(matched[..., :1] / r[theirs][:, 0].mean(), 0, 1)
    matched[..., 1:] = (r[theirs][:, 1:].mean(0) + (b[..., 1:] - b[inside][:, 1:].mean(0)) * spread[1:]) * shade
    rgb = (oklab_to_srgb(matched) * 255 + .5).astype(np.uint8)
    return Image.fromarray(np.dstack([rgb, np.array(base.getchannel("A"))]), "RGBA")


def conform(out, number, morph, seed):
    """The base I0 repainted on the classic silhouette, in the style of its current generated body.

    A body generated from the classic sprite alone drifts from its proportions (wider shoulders, longer feet), and
    every head and worn item is drawn to those proportions. Here the classic sprite, by whole pixels on the
    generated body's canvas, is repainted with that body as the style reference, in a mask of the classic
    silhouette grown by one doll unit, so the figure cannot grow past it; the background is cut out as the
    item finals' is. Returns the RGBA body and its doll-space rect."""
    layer = doll_layer("bodies", f"body{number:02d}i0", 0, "base")
    reference, rect = placed(layer, "canvas")
    bx, by, bw, _ = rect
    k = round(reference.width / bw)
    source = layer.source.resize((layer.source.width * k, layer.source.height * k), Image.Resampling.NEAREST)
    at = (round((layer.x - bx) * k), round((layer.y - by) * k))
    init = Image.new("RGB", reference.size, GROUND[:3])
    init.paste(source.convert("RGB"), at, source.getchannel("A"))
    alpha = Image.new("L", reference.size)
    alpha.paste(source.getchannel("A"), at)
    stem = out / f"body{number:02d}i0"
    init.save(f"{stem}-init.png")
    on_ground(reference).save(f"{stem}-reference.png")
    mask_image(alpha, k).save(f"{stem}-mask.png")
    qwen([Path(f"{stem}-init.png"), Path(f"{stem}-reference.png")], CONFORM.format(tail=TAIL.get(morph, "") + LIMB.get(number, "")), seed, Path(f"{stem}.png"), Path(f"{stem}-mask.png"))
    painted = np.array(Image.open(f"{stem}.png").convert("RGB"), np.float32) / 255
    placement = SimpleNamespace(scale=k, x=at[0], y=at[1], w=layer.source.width, h=layer.source.height,
                                alpha=(np.array(layer.source.getchannel("A")) > 0).astype(np.float32))
    cut = cutout(placement, painted)
    # The cut-out decides only the edge, as a dark skin's shadows match the ground: within the classic
    # silhouette less a doll unit the figure is opaque, and past the grown mask it is gone. The classic's
    # enclosed gaps (an arm on the hip, a tail beside the legs) are narrower than that band and get painted
    # shut, so they stay open as the classic draws them.
    classic = np.array(alpha) > 16
    inside = ndimage.binary_erosion(classic, iterations=k)[..., None]
    rgba = np.where(inside, np.dstack([painted, np.ones(painted.shape[:2])]), cut)
    gaps = ndimage.gaussian_filter((ndimage.binary_fill_holes(classic) & ~classic).astype(np.float32), k / 3)
    rgba[..., 3] *= np.array(Image.open(f"{stem}-mask.png"), np.float32) / 255 * np.clip(1 - 2 * gaps, 0, 1)
    return Image.fromarray((rgba * 255 + .5).astype(np.uint8), "RGBA"), rect


def name(number, layer):
    return f"bodies--body{number:02d}{layer}--body{number:02d}{layer}-record-000-frame-000.png"


def underwear(out, number, i0, rect, seed):
    """The clothed body: the classic underwear painted onto this race's I0, which lies at doll-space `rect`."""
    bare, dressed = doll_layer("bodies", f"body{number:02d}i0", 0, "i0"), doll_layer("bodies", f"body{number:02d}i1", 0, "i1")
    bx, by, bw, _ = rect
    k = round(i0.width / bw)
    a, b = np.array(bare.source), np.array(dressed.source)
    # The classic I1 also differs from I0 in stray pixels: a shade off along outlines and down the legs (the
    # elves' palette), which joins the underwear and Qwen paints as streaks, and specks of chest shading.
    shade = np.sqrt(((srgb_to_oklab(a[..., :3] / 255) - srgb_to_oklab(b[..., :3] / 255)) ** 2).sum(-1))
    cloth = (shade > CLOTH_SHADE) & (b[..., 3] > 0)
    labels, count = ndimage.label(cloth, np.ones((3, 3)))
    cloth = np.isin(labels, np.nonzero(ndimage.sum(cloth, labels, range(1, count + 1)) >= CLOTH_MIN)[0] + 1)
    pixels = Image.fromarray(np.dstack([b[..., :3], cloth * 255]).astype(np.uint8), "RGBA")
    pixels = pixels.resize((pixels.width * k, pixels.height * k), Image.Resampling.NEAREST)
    at = (round((dressed.x - bx) * k), round((dressed.y - by) * k))
    init = on_ground(i0)
    init.paste(pixels.convert("RGB"), at, pixels.getchannel("A"))
    alpha = Image.new("L", i0.size)
    alpha.paste(pixels.getchannel("A"), at)
    stem = out / f"body{number:02d}i1"
    init.save(f"{stem}-init.png")
    mask = mask_image(alpha, CLOTH_REACH * k)
    mask.save(f"{stem}-mask.png")
    qwen(Path(f"{stem}-init.png"), UNDERWEAR + CLOTHS.get(number, ""), seed, Path(f"{stem}.png"), Path(f"{stem}-mask.png"))
    painted = np.array(Image.open(f"{stem}.png").convert("RGB"))
    d = np.sqrt(((painted.astype(np.float32) - GROUND[:3]) ** 2).sum(2))
    added = np.clip((d - KEY[0]) / (KEY[1] - KEY[0]), 0, 1) * np.array(mask, np.float32) / 255
    # Cloth the classic lays across a gap the bare body encloses (a loincloth's band under the hand on the hip) stays,
    # even where Qwen paints it as background: there it keeps the pasted cloth's colour.
    bare = np.array(i0.getchannel("A")) > 127
    past = (np.array(alpha) > 127) & ndimage.binary_fill_holes(bare) & ~bare
    lost = past & (added < 0.5)
    painted[lost] = np.array(init)[lost]
    silhouette = np.maximum(np.maximum(np.array(i0.getchannel("A"), np.float32) / 255, added), past)
    return Image.fromarray(np.dstack([painted, (silhouette * 255 + .5).astype(np.uint8)]), "RGBA")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--gender", action="append", choices=("male", "female"))
    ap.add_argument("--base", action="append", default=[], help="MORPH=RACE, the race whose shape the morph takes (default its first)")
    ap.add_argument("--seed", type=int, default=5101)
    a = ap.parse_args()
    out = a.out.resolve()
    (out / "finals").mkdir(parents=True, exist_ok=True)
    bases = {morph: races[0] for morph, races in MORPHS.items()} | {m: int(r) for m, r in (b.split("=") for b in a.base)}
    for gender in a.gender or ["male", "female"]:
        offset = 10 if gender == "female" else 0
        for morph, races in MORPHS.items():
            base = bases[morph] + offset
            # Every race of the morph is drawn where the base is: its own classic offset would move the shape.
            base_i0, rect = conform(out, base, morph, a.seed)
            for race in races:
                number = race + offset
                if number == base:
                    i0 = base_i0
                else:
                    i0 = recolour(base_i0, Image.open(GENERATED / name(number, "i0")).convert("RGBA"))
                for layer, image in (("i0", i0), ("i1", underwear(out, number, i0, rect, a.seed))):
                    image.save(out / "finals" / name(number, layer))
                    (out / "finals" / name(number, layer)).with_suffix(".json").write_text(json.dumps({"rect": rect, "base": f"body{base:02d}i0"}))
                print(f"body{number:02d}: {morph} {gender} from body{base:02d}", flush=True)


if __name__ == "__main__":
    main()
