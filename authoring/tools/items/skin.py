#!/usr/bin/env python3
"""Give each race's generated heads the skin of its generated body, as Daggerfall's palette does.

In the classic sprites a head's skin uses exactly the colours of its body's skin, so a head always meets its
neck. Generated heads were made apart from the bodies and drift, mostly in lightness (up to about 0.12 OKLab
darker than the body). This remaps each head's skin onto its body's skin ramp:

  - skin: the classic head pixels whose colour occurs in the classic body (the shared palette ramp), mapped
    through the generation input transform onto the final's canvas and kept where the generated colour is
    within SKIN of that region's core tone (hair sharing the ramp, blond or brown, is not), feathered;
  - the body's ramp: the mean OKLab a, b of the generated body's skin per lightness band;
  - each skin pixel keeps its own shading and its local hue offset (blush, lips) from the head's own ramp,
    takes the body's ramp at its lightness, and has its lightness shifted so its mean skin lightness moves
    LIGHTNESS of the way to the body's. (The step across the neck is no guide: a head's jaw and neck lie in
    their own shadow while the body's neck stump is lit, and it varied from 0.02 to 0.32 between heads of a race.)

Hair, eyes, paint and headgear are outside the mask and keep their colours. Bodies are unchanged: bodies.py
already gives every race of a morph its body colour.

    skin.py OUT [--face 00 --face 10]        # every generated head of those FACE files (default all)

Run it after heads.py has trimmed the heads. Remapped heads go to OUT/finals for doll_preview.py --paper-doll OUT/finals
before they replace gen-raw/paper-doll.
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from doll_preview import doll_layer, placed
from landmarks import BODIES, HEADS
from postprocess import oklab_to_srgb, srgb_to_oklab

BANDS = 16          # lightness bands of a skin ramp
LIGHTNESS = 1.0     # how far a head's mean skin lightness moves to its body's
SKIN = 0.035        # OKLab a-b distance from the head's core skin tone within which a pixel is skin


def lab(image):
    a = np.array(image.convert("RGBA"), np.float32) / 255
    return srgb_to_oklab(a[..., :3]), a[..., 3]


def ramp(L, ab, weights):
    """Mean a, b per lightness band (weighted), as a function of lightness."""
    edges = np.quantile(L[weights > 0.5], np.linspace(0, 1, BANDS + 1))
    centres, means = [], []
    for lo, hi in zip(edges[:-1], edges[1:]):
        inside = (L >= lo) & (L <= hi) & (weights > 0.5)
        if inside.sum() > 8:
            centres.append(L[inside].mean())
            means.append(ab[inside].mean(0))
    centres, means = np.array(centres), np.array(means)
    return lambda x: np.stack([np.interp(x, centres, means[:, i]) for i in (0, 1)], -1)


def canvas_mask(layer, rect, k, source_mask, size):
    """A mask over the layer's source pixels, by whole source pixels on the final's canvas at rect."""
    image = Image.fromarray((source_mask * 255).astype(np.uint8)).resize(
        (round(source_mask.shape[1] * k), round(source_mask.shape[0] * k)), Image.Resampling.NEAREST)
    out = Image.new("L", size)
    out.paste(image, (round((layer.x - rect[0]) * k), round((layer.y - rect[1]) * k)))
    return np.array(out) > 127


def body_skin(number):
    """The generated body's skin in OKLab, its alpha, doll-space rect and px per unit."""
    body = doll_layer("bodies", f"body{number:02d}i0", 0, "body")
    final, rect = placed(body, "canvas")
    L, alpha = lab(final)
    return body, L, alpha, rect, final.width / rect[2]


def remap(number, record, body_ramp, body_L):
    """The head with its skin on the body's ramp, its mean skin lightness moved toward the body's (body_L)."""
    head = doll_layer("faces", f"face{number:02d}i0", record, "head")
    if head.final is None:
        return None
    final, rect = placed(head, "canvas")
    k = final.width / rect[2]
    Lab, alpha = lab(final)
    L, ab = Lab[..., 0], Lab[..., 1:]
    palette = np.array(doll_layer("bodies", f"body{number:02d}i0", 0, "body").source)
    palette = {tuple(p) for p in palette[palette[..., 3] > 0][:, :3]}
    source = np.array(head.source)
    classic = np.array([tuple(p) in palette for p in source[..., :3].reshape(-1, 3)]).reshape(source.shape[:2])
    classic &= source[..., 3] > 0
    region = canvas_mask(head, rect, k, classic, final.size) & (alpha > 0.5)
    if region.sum() < 50 * k * k:
        return final  # no skin to speak of (a closed helm)
    # Skin is what is coloured like the core of that region: hair sharing the palette ramp (blond, brown) is not.
    core = ndimage.binary_erosion(region, iterations=round(k))
    core = core if core.sum() > 20 * k * k else region
    tone = np.median(ab[core], 0)
    skin = region & (np.sqrt(((ab - tone) ** 2).sum(-1)) < SKIN)
    weight = ndimage.gaussian_filter(skin.astype(np.float32), k / 2) * (alpha > 0.5)
    own = ramp(L, ab, weight)
    L2 = np.clip(L + LIGHTNESS * (body_L - L[skin].mean()), 0, 1)
    new = np.concatenate([L2[..., None], body_ramp(L2) + (ab - own(L))], -1)
    w = np.clip(weight, 0, 1)[..., None]
    rgb = oklab_to_srgb(Lab * (1 - w) + new * w)
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 1) * 255 + .5, alpha * 255 + .5]).astype(np.uint8), "RGBA")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--face", action="append", type=int, choices=BODIES, help="FACE file number, e.g. 10")
    a = ap.parse_args()
    (a.out / "finals").mkdir(parents=True, exist_ok=True)
    for number in a.face or BODIES:
        _, Lab, alpha, rect, k = body_skin(number)
        body_ramp = ramp(Lab[..., 0], Lab[..., 1:], (alpha > 0.9).astype(np.float32))
        for record in HEADS:
            image = remap(number, record, body_ramp, Lab[..., 0][alpha > 0.9].mean())
            if image is not None:
                image.save(a.out / "finals" / doll_layer("faces", f"face{number:02d}i0", record, "head").final.name)
        print(f"face{number:02d}: skin from body{number:02d}", flush=True)


if __name__ == "__main__":
    main()
