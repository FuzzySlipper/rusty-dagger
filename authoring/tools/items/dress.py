#!/usr/bin/env python3
"""Dress a generated paper-doll body piece by piece, then lift each piece back off as its own layer.

Instead of generating a worn item alone on a grey canvas, each item is painted onto the body it is worn on,
so it takes that body's outline, light and contact shadows. Items go on in the game's draw order; for each:

  1. the classic item, scaled by whole pixels, is pasted onto the current figure where the doll draws it;
  2. Qwen repaints the figure, and only the item's region (dilated) is composited back through a feathered
     mask, so nothing outside it changes (a magenta --guide tint is not used: Qwen paints the item magenta);
  3. Qwen is asked again, on the dressed figure, to keep only that piece on flat green, with no skin. The
     green key of that image, inside the repaint region, is the item's alpha. Keying on a difference against
     the figure beneath alone fails wherever the piece's colour matches the skin it covers, so the body's colour
     only trims what the classic item does not cover: a pixel within EDGE doll units of the key's edge, outside
     the classic silhouette, whose hue is the body's beneath it is the body showing through or repainted (bare
     toes in a sabaton), not the item. Inside the classic silhouette a bronze plate may share the skin's hue. The colour is the dressed
     figure's, unmixed from the known figure beneath where the alpha is partial, so no base skin tints the
     rim when the layer is drawn on another race's body;
  4. the dressed figure becomes the base for the next item.

Everything happens on the body final's own canvas (about 5 px per doll unit). Each layer is written as a
1024x1024 RGBA PNG on that canvas with a sidecar JSON giving its doll-space rect, which doll_preview.py
draws directly (`--worn OUT/layers`). The figure after each step, the inputs and Qwen's records stay in OUT.

    dress.py OUT --race 1 --gender male --item texture-251-record-000-frame-000 ... [--seed 4101]

Items are listed in draw order (the template's drawOrderOrEffect, then slot). Qwen runs on the owner's
5090 by default; set COMFY_URL for another host.
"""
import argparse, json, os, subprocess, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

from postprocess import srgb_to_oklab
from doll_preview import FINALS, GROUND, MORPH, doll_layer, placed, worn_layer

PIPELINE = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
QWEN = PIPELINE / "tools/texture-edit/qwen_edit.py"
REACH = 3            # doll units the repaint may extend past the classic item
KEY = (60, 140)      # distance from pure green over which an isolated pixel fades from key to item
BODY_HUE = (0.02, 0.05)  # OKLab a-b distance from the body beneath over which a rim pixel fades from body to item
EDGE = 3             # doll units from the key's edge in which body-coloured pixels are trimmed
ISOLATION_TRIES = 3

PROMPT = ("The figure wears a crude, pixelated, low-resolution {name}. Repaint only that piece as finely painted "
          "fantasy armour actually worn on this body: fitted to its anatomy and pose, overlapping what it covers, with the "
          "same painting style, brushwork and light from the upper left as the rest of the figure, and soft contact "
          "shadow where it meets the body. Keep the piece's silhouette, the parts of the body it covers, its construction "
          "and exactly its colours and materials as the pixelated piece shows them. Do not change the body, the pose, any "
          "other armour or the flat background, and add nothing else.")
ISOLATE = ("Keep only the {name} exactly as it is, every pixel of it unchanged. Replace everything else - the body, "
           "all skin including toes, fingers and the neck, cloth, all other armour and the background - with flat pure "
           "green, RGB 0,255,0.")


def mask_image(alpha, reach):
    """The classic item's silhouette, grown by `reach` pixels and feathered: white repaints, black keeps."""
    grown = alpha.point(lambda a: 255 if a > 16 else 0).filter(ImageFilter.MaxFilter(2 * reach + 1))
    return grown.filter(ImageFilter.GaussianBlur(reach / 3))


def qwen(images, prompt, seed, out, mask=None):
    """One Qwen edit of the first image (the others are references), skipped when OUT already exists."""
    if out.exists():
        return
    argv = [sys.executable, str(QWEN), "--prompt", prompt, "--seed", str(seed), "--out", str(out)]
    for image in images if isinstance(images, list) else [images]:
        argv += ["--image", str(image)]
    subprocess.run(argv + (["--mask", str(mask)] if mask else []), cwd=PIPELINE, check=True)


def ramp(x, bounds):
    return np.clip((x - bounds[0]) / (bounds[1] - bounds[0]), 0, 1)


def lift(dressed, beneath, isolated, region, classic, k):
    """The item layer: where the isolated piece is not green, less body-coloured rim beyond the classic item."""
    dressed, beneath = dressed.astype(np.float32) / 255, beneath.astype(np.float32) / 255
    a = ramp(np.sqrt(((isolated.astype(np.float32) - (0, 255, 0)) ** 2).sum(2)), KEY) * region
    trusted = ndimage.binary_erosion(a > 0.5, iterations=EDGE * k) | ndimage.binary_dilation(classic > 16, iterations=k)
    hue = np.sqrt(((srgb_to_oklab(dressed)[..., 1:] - srgb_to_oklab(beneath)[..., 1:]) ** 2).sum(2))
    a *= np.maximum(ramp(hue, BODY_HUE), trusted)
    core = ndimage.binary_opening(a > 0.5, iterations=k)
    a = np.where(ndimage.binary_dilation(core, iterations=k), a, 0)
    a = np.maximum(a, ndimage.binary_fill_holes(core))
    # Over the rim, the dressed pixel is the item over the figure beneath: take the figure back out.
    held = a[..., None]
    colour = np.where(held > 0.05, (dressed - (1 - held) * beneath) / np.maximum(held, 0.05), dressed)
    return np.dstack([np.clip(colour, 0, 1) * 255 + .5, a * 255 + .5]).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--race", type=int, default=0, choices=range(8))
    ap.add_argument("--gender", default="male", choices=("male", "female"))
    ap.add_argument("--body-layer", default="i1", choices=("i0", "i1"))
    ap.add_argument("--item", action="append", required=True, help="worn art key, in draw order")
    ap.add_argument("--name", action="append", help="what each item is (cuirass, boots ...), same order as --item")
    ap.add_argument("--reach", action="append", type=int, help=f"doll units each item's repaint may extend past the classic "
                    f"item (default {REACH}), same order as --item; footwear needs more, as generated feet reach past "
                    "classic boots")
    ap.add_argument("--seed", type=int, default=4101)
    ap.add_argument("--paper-doll", type=Path, help="body finals directory to look in first (e.g. bodies.py OUT/finals)")
    a = ap.parse_args()
    if a.paper_doll:
        FINALS["paper-doll"].insert(0, a.paper_doll.resolve())
    a.out = a.out.resolve()
    names = a.name or [key for key in a.item]
    reaches = a.reach or [REACH] * len(a.item)
    if len(names) != len(a.item) or len(reaches) != len(a.item):
        raise SystemExit("Give one --name and one --reach per --item, or none.")

    number = a.race + (10 if a.gender == "female" else 0)
    body = doll_layer("bodies", f"body{number:02d}{a.body_layer}", 0, "body")
    if body.final is None:
        raise SystemExit(f"No generated body final for body{number:02d}{a.body_layer}.")
    final, (bx, by, bw, bh) = placed(body, "canvas", 1024, 0.74)
    k = final.width / bw  # canvas pixels per doll unit
    if abs(k - round(k)) > 1e-6:
        raise SystemExit(f"The body canvas is {k} px per doll unit; whole pixels keep the classic items crisp.")
    k = round(k)
    figure = Image.new("RGBA", final.size, GROUND)
    figure.alpha_composite(final)
    figure = figure.convert("RGB")

    (a.out / "layers").mkdir(parents=True, exist_ok=True)
    figure.save(a.out / "step-0-body.png")
    role = f"{a.gender}-{MORPH[a.race]}"
    for step, (key, name, reach) in enumerate(zip(a.item, names, reaches), 1):
        item = worn_layer(key, role)
        at = (round((item.x - bx) * k), round((item.y - by) * k))
        classic = item.source.resize((item.source.width * k, item.source.height * k), Image.Resampling.NEAREST)
        alpha = Image.new("L", figure.size)
        alpha.paste(classic.getchannel("A"), at)
        init = figure.copy()
        init.paste(classic.convert("RGB"), at, classic.getchannel("A"))
        stem = f"step-{step}-{key}"
        init.save(a.out / f"{stem}-init.png")
        mask = mask_image(alpha, reach * k)
        mask.save(a.out / f"{stem}-mask.png")
        dressed_path = a.out / f"{stem}.png"
        qwen(a.out / f"{stem}-init.png", PROMPT.format(name=name), a.seed, dressed_path, a.out / f"{stem}-mask.png")
        dressed = Image.open(dressed_path).convert("RGB")
        for attempt in range(ISOLATION_TRIES):
            # A failed isolation keeps nothing, or tints the whole figure green; try the next seed.
            isolated_path = a.out / f"{stem}-isolated{'' if attempt == 0 else f'-{attempt}'}.png"
            qwen(dressed_path, ISOLATE.format(name=name), a.seed + attempt, isolated_path)
            isolated = Image.open(isolated_path).convert("RGB").resize(dressed.size)
            layer = lift(np.array(dressed), np.array(figure), np.array(isolated), np.array(mask, np.float32) / 255,
                         np.array(alpha), k)
            kept = layer[..., 3].sum() / max(1, np.array(alpha).sum())
            if 0.15 < kept < 3:
                break
            print(f"{stem}: isolation {attempt} kept {kept:.2f} of the classic item's area; retrying", flush=True)
        else:
            raise SystemExit(f"{stem}: no isolation kept the piece; inspect the {stem}-isolated*.png images.")
        Image.fromarray(layer, "RGBA").save(a.out / "layers" / f"{key}.png")
        (a.out / "layers" / f"{key}.json").write_text(json.dumps({"rect": [bx, by, bw, bh], "body": str(body.final), "seed": a.seed}))
        figure = dressed
        print(f"{stem}: dressed", flush=True)
    figure.save(a.out / "dressed.png")


if __name__ == "__main__":
    main()
