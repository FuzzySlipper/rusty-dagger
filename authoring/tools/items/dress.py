#!/usr/bin/env python3
"""Dress a generated paper-doll body piece by piece, then lift each piece back off as its own layer.

Instead of generating a worn item alone on a grey canvas, each item is painted onto the body it is worn on,
so it takes that body's outline, light and contact shadows. Items go on in the game's draw order; for each:

  1. the classic item, scaled by whole pixels, is pasted onto the current figure where the doll draws it;
  2. Qwen repaints the figure, and only the item's region (dilated) is composited back through a feathered
     mask, so nothing outside it changes (a magenta --guide tint is not used: Qwen paints the item magenta);
  3. Qwen is asked again, on the dressed figure, to keep only that piece on flat green; the green key of that
     image, inside the repaint region, is the item's alpha, and its colour is the dressed figure's. Keying on
     a difference against the figure beneath fails wherever the piece's colour matches the skin it covers;
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

from doll_preview import GROUND, MORPH, doll_layer, placed, worn_layer

PIPELINE = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
QWEN = PIPELINE / "tools/texture-edit/qwen_edit.py"
REACH = 3            # doll units the repaint may extend past the classic item
KEY = (60, 140)      # distance from pure green over which an isolated pixel fades from key to item

PROMPT = ("The figure wears a crude, pixelated, low-resolution {name}. Repaint only that piece as finely painted "
          "fantasy armour actually worn on this body: fitted to its anatomy and pose, overlapping what it covers, with the "
          "same painting style, brushwork and light from the upper left as the rest of the figure, and soft contact "
          "shadow where it meets the body. Keep the piece's silhouette, the parts of the body it covers, its construction "
          "and exactly its colours and materials as the pixelated piece shows them. Do not change the body, the pose, any "
          "other armour or the flat background, and add nothing else.")
ISOLATE = ("Keep only the {name} exactly as it is, every pixel of it unchanged. Replace everything else - the body, "
           "skin, cloth, all other armour and the background - with flat pure green, RGB 0,255,0.")


def mask_image(alpha, reach):
    """The classic item's silhouette, grown by `reach` pixels and feathered: white repaints, black keeps."""
    grown = alpha.point(lambda a: 255 if a > 16 else 0).filter(ImageFilter.MaxFilter(2 * reach + 1))
    return grown.filter(ImageFilter.GaussianBlur(reach / 3))


def qwen(image, prompt, seed, out, mask=None):
    if out.exists():
        return
    argv = [sys.executable, str(QWEN), "--image", str(image), "--prompt", prompt, "--seed", str(seed), "--out", str(out)]
    subprocess.run(argv + (["--mask", str(mask)] if mask else []), cwd=PIPELINE, check=True)


def lift(dressed, isolated, region, speck):
    """The item layer: the dressed figure where the isolated piece is not green, inside the repaint region."""
    d = np.sqrt(((isolated.astype(np.float32) - (0, 255, 0)) ** 2).sum(2))
    lo, hi = KEY
    a = np.clip((d - lo) / (hi - lo), 0, 1) * region
    core = ndimage.binary_opening(a > 0.5, iterations=speck)
    a = np.where(ndimage.binary_dilation(core, iterations=speck), a, 0)
    a = np.maximum(a, ndimage.binary_fill_holes(core))
    return np.dstack([dressed, (a * 255 + .5).astype(np.uint8)])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--race", type=int, default=0, choices=range(8))
    ap.add_argument("--gender", default="male", choices=("male", "female"))
    ap.add_argument("--body-layer", default="i1", choices=("i0", "i1"))
    ap.add_argument("--item", action="append", required=True, help="worn art key, in draw order")
    ap.add_argument("--name", action="append", help="what each item is (cuirass, boots ...), same order as --item")
    ap.add_argument("--seed", type=int, default=4101)
    a = ap.parse_args()
    a.out = a.out.resolve()
    names = a.name or [key for key in a.item]
    if len(names) != len(a.item):
        raise SystemExit("Give one --name per --item, or none.")

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
    for step, (key, name) in enumerate(zip(a.item, names), 1):
        item = worn_layer(key, role)
        at = (round((item.x - bx) * k), round((item.y - by) * k))
        classic = item.source.resize((item.source.width * k, item.source.height * k), Image.Resampling.NEAREST)
        alpha = Image.new("L", figure.size)
        alpha.paste(classic.getchannel("A"), at)
        init = figure.copy()
        init.paste(classic.convert("RGB"), at, classic.getchannel("A"))
        stem = f"step-{step}-{key}"
        init.save(a.out / f"{stem}-init.png")
        mask = mask_image(alpha, REACH * k)
        mask.save(a.out / f"{stem}-mask.png")
        dressed_path, isolated_path = a.out / f"{stem}.png", a.out / f"{stem}-isolated.png"
        qwen(a.out / f"{stem}-init.png", PROMPT.format(name=name), a.seed, dressed_path, a.out / f"{stem}-mask.png")
        qwen(dressed_path, ISOLATE.format(name=name), a.seed, isolated_path)
        dressed = Image.open(dressed_path).convert("RGB")
        isolated = Image.open(isolated_path).convert("RGB").resize(dressed.size)
        layer = lift(np.array(dressed), np.array(isolated), np.array(mask, np.float32) / 255, k)
        Image.fromarray(layer, "RGBA").save(a.out / "layers" / f"{key}.png")
        (a.out / "layers" / f"{key}.json").write_text(json.dumps({"rect": [bx, by, bw, bh], "body": body.final.name, "seed": a.seed}))
        figure = dressed
        print(f"{stem}: dressed", flush=True)
    figure.save(a.out / "dressed.png")


if __name__ == "__main__":
    main()
