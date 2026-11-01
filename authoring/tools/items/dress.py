#!/usr/bin/env python3
"""Dress a generated paper-doll body piece by piece, then lift each piece back off as its own layer.

Instead of generating a worn item alone on a grey canvas, each item is painted onto the body it is worn on,
so it takes that body's outline, light and contact shadows. Items go on in the game's draw order; for each:

  1. the classic item, scaled by whole pixels, is pasted onto the current figure where the doll draws it. The
     body (and the head, for helms) is recoloured to a violet key, its lightness kept, so skin left in the dressed
     piece (an open shirt's chest, a hand resting on trousers, a thigh above a boot) is told from the piece and
     left out of the layer: every race of the morph has this body's shape, so its own skin shows through there.
     Metal pieces (spec "metal") and cloak linings ("behind") are dressed on the body's own skin, as steel beside
     violet skin reflects it and a lining is painted violet in its shadow;
  2. Qwen repaints the figure, and only the item's region (dilated, its narrow gaps closed) is composited back
     through a feathered mask, so nothing outside it changes. A gap left out of the mask (an open shirt's belly)
     comes back painted along its straight pixel rows; inside the mask, skin Qwen paints there is keyed away;
  3. the dressed figure is keyed against the figure beneath (OKLab difference). Where that keeps COVERED of the
     classic item, it is the item's alpha: it costs no Qwen call. A piece the colour of what it covers keys away in
     holes, so then Qwen is asked, on the dressed figure, to keep only that piece on flat green (then, failing
     that, blue, which brown and tan pieces survive), and the key of that image, inside the repaint region, is
     the item's alpha. The colour is the dressed figure's, unmixed from the figure beneath where the alpha is
     partial, with the violet the skin cast on it taken out, so no key tints the rim on another race's body;
  4. the dressed figure becomes the base for the next item.

Everything happens on the body final's own canvas (about 5 px per doll unit). Each layer is written as a
1024x1024 RGBA PNG on that canvas with a sidecar JSON giving its doll-space rect, which doll_preview.py
draws directly (`--worn OUT/layers`). The figure after each step, the inputs and Qwen's records stay in OUT.

    dress.py OUT --race 1 --gender male --item texture-251-record-000-frame-000 ... [--seed 4101]

Items are listed in draw order (the template's drawOrderOrEffect, then slot). Qwen runs on Patch's
5090 by default; set COMFY_URL for another host.
"""
import argparse, json, os, subprocess, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

from postprocess import oklab_to_srgb, srgb_to_oklab
from doll_preview import FINALS, GROUND, MORPH, WORN_SOURCES, doll_layer, placed, worn_layer

PIPELINE = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
QWEN = PIPELINE / "tools/texture-edit/qwen_edit.py"
REACH = 3            # doll units the repaint may extend past the classic item
GAP = 6              # doll units: gaps in the classic item narrower than twice this are repainted with it
KEY = (60, 140)      # distance from the flat key colour over which an isolated pixel fades from key to item
SKIN_KEY = np.arctan2(-0.12, 0.06)  # OKLab hue the body is dressed in (violet): no worn piece is violet
SKIN_HUE = (0.35, 0.6)  # radians from SKIN_KEY over which a pixel fades from skin to item
SKIN_CHROMA = (0.6, 0.8)  # share of the key's chroma at a pixel's lightness over which it is surely skin
FAINT_CHROMA = (0.25, 0.5)  # ... over which it is skin if as light as the body beneath: skin in shadow (a fist)
                            # keeps that much, and so does grey cloth or steel beside the skin, which is darker
SAME_SKIN = (0.06, 0.12)  # OKLab lightness from the body beneath over which a faintly violet pixel is not skin
# Isolations to try, in order: (key colour name, RGB, seed offset, file suffix). Green first; Qwen erases some
# brown and tan pieces on green (trousers, leather), and keeps them on blue.
ISOLATIONS = [("green", (0, 255, 0), 0, ""), ("green", (0, 255, 0), 1, "-1"), ("green", (0, 255, 0), 2, "-2"),
              ("blue", (0, 0, 255), 0, "-blue")]
DIFFERENCE = (0.03, 0.08)  # OKLab distance from the figure beneath over which a pixel fades from body to item
GHOST = 20           # median cast toward the key colour of kept pixels past which an isolation is a translucent ghost
COVERED = 0.9        # share of the classic item a difference key must keep to stand without a Qwen isolation
STEPS = 15           # Qwen sampling steps: 25 painted the same pieces about a quarter slower

PROMPT = ("The figure wears a crude, pixelated, low-resolution {name}. Repaint only that piece, finely painted, as "
          "it would actually be worn on this body: fitted to its anatomy and pose, overlapping what it covers, with the "
          "same painting style, brushwork and light from the upper left as the rest of the figure, and soft contact "
          "shadow where it meets the body. Keep the piece's silhouette, the parts of the body it covers, its construction "
          "and exactly its colours and materials as the pixelated piece shows them. Do not change the body, the head, the "
          "pose, any other clothing or armour or the flat background, and add nothing else.")
ISOLATE = ("Keep only the {name} exactly as it is, every pixel of it unchanged. Replace everything else - the body, "
           "all skin including toes, fingers and the neck, cloth, all other armour and the background - with flat pure "
           "{key}, RGB {rgb}.")


def mask_image(alpha, reach):
    """The classic item's silhouette, grown by `reach` pixels and feathered: white repaints, black keeps."""
    grown = alpha.point(lambda a: 255 if a > 16 else 0).filter(ImageFilter.MaxFilter(2 * reach + 1))
    return grown.filter(ImageFilter.GaussianBlur(reach / 3))


def qwen(images, prompt, seed, out, mask=None, steps=STEPS):
    """One Qwen edit of the first image (the others are references), skipped when OUT already exists."""
    if out.exists():
        return
    argv = [sys.executable, str(QWEN), "--prompt", prompt, "--seed", str(seed), "--steps", str(steps), "--out", str(out)]
    for image in images if isinstance(images, list) else [images]:
        argv += ["--image", str(image)]
    subprocess.run(argv + (["--mask", str(mask)] if mask else []), cwd=PIPELINE, check=True)


def ramp(x, bounds):
    return np.clip((x - bounds[0]) / (bounds[1] - bounds[0]), 0, 1)


def key_cast(dressed, isolated, region, key):
    """How much more toward the key colour the isolation keeps the piece than the dressed figure shows it (median
    over kept pixels; for green, G less mean R, B).

    Qwen sometimes isolates a pale piece as a translucent key-tinted ghost, which keys to scattered fragments."""
    kept = (np.sqrt(((isolated.astype(np.float32) - key) ** 2).sum(2)) > KEY[1]) & (region > 0.5)
    if not kept.any():
        return 0.0
    toward = np.array(key, np.float32) - np.mean(key)
    toward /= toward.max()
    return float(np.median(((isolated.astype(np.float32) - dressed) @ toward)[kept]))


def difference_isolation(dressed, beneath, onto=None):
    """An isolation made by keying the dressed figure against the figure beneath (OKLab distance): tried first,
    and the fallback when every Qwen isolation fails. It holds for a piece unlike the skin it covers (pale cloth,
    steel); a piece the colour of that skin (bronze plate, tan trousers) keys away in holes. With `onto` (a Qwen
    isolation), what it keeps is added to that isolation instead of drawn on flat green."""
    lab = lambda a: srgb_to_oklab(a.astype(np.float32) / 255)
    t = ramp(np.sqrt(((lab(dressed) - lab(beneath)) ** 2).sum(2)), DIFFERENCE)[..., None]
    ground = np.array(ISOLATIONS[0][1], np.float32) if onto is None else onto.astype(np.float32)
    return (t * dressed + (1 - t) * ground + .5).astype(np.uint8)


def key_chroma(L):
    return 0.11 * np.clip(L / 0.65, 0.7, 1.1)


def skin_key(rgba):
    """The figure with its skin (and everything else it has) in the violet key hue, its lightness kept."""
    lab = srgb_to_oklab(np.array(rgba, np.float32)[..., :3] / 255)
    chroma = key_chroma(lab[..., 0])
    lab[..., 1], lab[..., 2] = chroma * np.cos(SKIN_KEY), chroma * np.sin(SKIN_KEY)
    rgb = np.clip(oklab_to_srgb(lab), 0, 1) * 255 + .5
    return Image.fromarray(np.dstack([rgb, np.array(rgba)[..., 3]]).astype(np.uint8), "RGBA")


def near_key(lab):
    """How near each pixel's hue is the violet key, 0 to 1."""
    off = np.abs(np.angle(np.exp(1j * (np.arctan2(lab[..., 2], lab[..., 1]) - SKIN_KEY))))
    return 1 - ramp(off, SKIN_HUE)


def skinness(rgb, beneath=None):
    """How surely each pixel is the violet-keyed skin, 0 to 1: near its hue, and about as saturated, or more
    faintly violet but as light as the body beneath."""
    lab = srgb_to_oklab(rgb)
    share = np.sqrt((lab[..., 1:] ** 2).sum(-1)) / key_chroma(lab[..., 0])
    skin = ramp(share, SKIN_CHROMA)
    if beneath is not None:
        same = 1 - ramp(np.abs(lab[..., 0] - srgb_to_oklab(beneath)[..., 0]), SAME_SKIN)
        skin = np.maximum(skin, ramp(share, FAINT_CHROMA) * same)
    return near_key(lab) * skin


def lift(dressed, beneath, isolated, region, k, key=ISOLATIONS[0][1]):
    """The item layer: where the isolated piece is not the key colour, less any skin (violet) it holds."""
    dressed, beneath = dressed.astype(np.float32) / 255, beneath.astype(np.float32) / 255
    a = ramp(np.sqrt(((isolated.astype(np.float32) - key) ** 2).sum(2)), KEY) * region
    # Skin in the piece (an open shirt's chest, a hand resting on trousers, a thigh above a boot) is left out:
    # every race of the morph has this body's shape, so its own skin shows through in the same place.
    skin = skinness(dressed, beneath)
    a *= 1 - skin
    core = ndimage.binary_opening(a > 0.5, iterations=k)
    a = np.where(ndimage.binary_dilation(core, iterations=k), a, 0)
    a = np.maximum(a, ndimage.binary_fill_holes(core) & (skin < 0.5))
    # Over the rim, the dressed pixel is the item over the figure beneath: take the figure back out.
    held = a[..., None]
    colour = np.where(held > 0.05, (dressed - (1 - held) * beneath) / np.maximum(held, 0.05), dressed)
    # Over the body, unmixing the saturated key overshoots to its complement (a yellow rim). Take the violet out
    # of every pixel whose hue is near it instead: no piece is violet, but steel beside the skin reflects it.
    lab = srgb_to_oklab(dressed)
    toward = np.array([np.cos(SKIN_KEY), np.sin(SKIN_KEY)], np.float32)
    lab[..., 1:] -= np.maximum(lab[..., 1:] @ toward, 0)[..., None] * toward * near_key(lab)[..., None]
    colour = np.where(skinness(beneath)[..., None] > 0.5, oklab_to_srgb(lab), colour)
    return np.dstack([np.clip(colour, 0, 1) * 255 + .5, a * 255 + .5]).astype(np.uint8)


def on_canvas(layer, rect, k, size):
    """A generated layer (a head) drawn on the body canvas, whose doll-space rect is `rect`, at k px per unit."""
    image, (x, y, w, h) = placed(layer, "canvas", 1024, 0.74)
    drawn = image.resize((round(w * k), round(h * k)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", size)
    canvas.paste(drawn, (round((x - rect[0]) * k), round((y - rect[1]) * k)))
    return canvas


def items_from(a):
    """The items to dress, in order: --spec entries, or --item with its --name and --reach."""
    if a.spec:
        return [{"reach": REACH} | entry for entry in json.load(open(a.spec))["items"]]
    names = a.name or list(a.item)
    reaches = a.reach or [REACH] * len(a.item)
    if len(names) != len(a.item) or len(reaches) != len(a.item):
        raise SystemExit("Give one --name and one --reach per --item, or none.")
    return [{"item": key, "name": name, "reach": reach} for key, name, reach in zip(a.item, names, reaches)]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--race", type=int, default=0, choices=range(8))
    ap.add_argument("--gender", default="male", choices=("male", "female"))
    ap.add_argument("--body-layer", default="i1", choices=("i0", "i1"))
    ap.add_argument("--spec", type=Path, help="JSON {\"items\": [{\"item\", \"name\", \"reach\", \"head\", \"behind\", \"metal\"}]}, "
                    "in draw order; head: dress it over head --face (helms); behind: the layer is drawn behind the body (cloak "
                    "linings); metal: dress it on the body's own skin, not the violet key (steel reflects the violet)")
    ap.add_argument("--item", action="append", help="worn art key, in draw order")
    ap.add_argument("--name", action="append", help="what each item is (cuirass, boots ...), same order as --item")
    ap.add_argument("--reach", action="append", type=int, help=f"doll units each item's repaint may extend past the classic "
                    f"item (default {REACH}), same order as --item")
    ap.add_argument("--each", action="store_true", help="dress every item alone on the body, not each over the last: a "
                    "library of layers that combine freely")
    ap.add_argument("--face", type=int, default=0, help="head record drawn for items that need the head (default 0)")
    ap.add_argument("--source", type=Path, action="append", default=[],
                    help="worn source edit to read first, in the export's layout (e.g. gen-raw/source-edits/worn-neutral-mens)")
    ap.add_argument("--seed", type=int, default=4101)
    ap.add_argument("--steps", type=int, default=STEPS, help="Qwen sampling steps")
    ap.add_argument("--paper-doll", type=Path, action="append", default=[],
                    help="body or head finals directory to look in first; repeat, first wins")
    a = ap.parse_args()
    if not a.spec and not a.item:
        raise SystemExit("Give --spec or --item.")
    FINALS["paper-doll"][:0] = [d.resolve() for d in a.paper_doll]
    WORN_SOURCES[:0] = [d.resolve() for d in a.source]
    a.out = a.out.resolve()
    items = items_from(a)

    number = a.race + (10 if a.gender == "female" else 0)
    body = doll_layer("bodies", f"body{number:02d}{a.body_layer}", 0, "body")
    if body.final is None:
        raise SystemExit(f"No generated body final for body{number:02d}{a.body_layer}.")
    natural, rect = placed(body, "canvas", 1024, 0.74)
    bx, by, bw, bh = rect
    k = natural.width / bw  # canvas pixels per doll unit
    if abs(k - round(k)) > 1e-6:
        raise SystemExit(f"The body canvas is {k} px per doll unit; whole pixels keep the classic items crisp.")
    k = round(k)
    # Metal and linings are dressed on the body's own skin: steel beside the violet key reflects it, and a lining
    # (which encloses no skin) is painted in its shadow, and either keys away as skin. Pieces dressed one over
    # another share one figure, the natural one if any is either.
    def natural_skin(entry):
        return entry.get("metal") or entry.get("behind")

    def violet(entry):
        return not natural_skin(entry) and (a.each or not any(natural_skin(e) for e in items))
    bodies, heads = {}, {}
    for keyed in {violet(entry) for entry in items}:
        bodies[keyed] = skin_key(natural) if keyed else natural
        if any(entry.get("head") for entry in items):
            head = on_canvas(doll_layer("faces", f"face{number:02d}i0", a.face, "head"), rect, k, natural.size)
            heads[keyed] = skin_key(head) if keyed else head

    def bare(keyed, head=False):
        figure = Image.new("RGBA", natural.size, GROUND)
        figure.alpha_composite(bodies[keyed])
        if head:
            figure.alpha_composite(heads[keyed])
        return figure.convert("RGB")
    figure = bare(violet(items[0]))

    (a.out / "layers").mkdir(parents=True, exist_ok=True)
    figure.save(a.out / "step-0-body.png")
    role = f"{a.gender}-{MORPH[a.race]}"
    failed = []
    for step, entry in enumerate(items, 1):
        key, name, reach = entry["item"], entry["name"], entry["reach"]
        if a.each:
            figure = bare(violet(entry), entry.get("head"))
        item = worn_layer(key, role)
        at = (round((item.x - bx) * k), round((item.y - by) * k))
        classic = item.source.resize((item.source.width * k, item.source.height * k), Image.Resampling.NEAREST)
        alpha = Image.new("L", figure.size)
        alpha.paste(classic.getchannel("A"), at)
        if entry.get("behind"):
            # Drawn behind the body: only what shows past the figure is the item.
            init = Image.new("RGBA", figure.size, GROUND)
            init.paste(classic, at, classic.getchannel("A"))
            init.alpha_composite(bodies[violet(entry)])
            init = init.convert("RGB")
        else:
            init = figure.copy()
            init.paste(classic.convert("RGB"), at, classic.getchannel("A"))
        stem = f"step-{step}-{key}"
        init.save(a.out / f"{stem}-init.png")
        # A gap in the classic item (an open shirt's belly) is repainted too: masked out, Qwen paints the piece's
        # edges along the gap's straight pixel rows. Skin it paints there is left out of the layer, and background
        # (between the legs) is not the piece.
        closed = ndimage.binary_closing(np.pad(np.array(alpha) > 16, GAP * k), iterations=GAP * k)[GAP * k:-GAP * k, GAP * k:-GAP * k]
        mask = mask_image(Image.fromarray(ndimage.binary_fill_holes(closed).astype(np.uint8) * 255), reach * k)
        mask.save(a.out / f"{stem}-mask.png")
        dressed_path = a.out / f"{stem}.png"
        qwen(a.out / f"{stem}-init.png", PROMPT.format(name=name), a.seed, dressed_path, a.out / f"{stem}-mask.png", a.steps)
        dressed = Image.open(dressed_path).convert("RGB")
        region, classic = np.array(mask, np.float32) / 255, np.array(alpha)
        keyed = difference_isolation(np.array(dressed), np.array(figure))
        layer = lift(np.array(dressed), np.array(figure), keyed, region, k)
        covered = ((layer[..., 3] > 127) & (classic > 16)).sum() / max(1, (classic > 16).sum())
        if covered >= COVERED:
            Image.fromarray(keyed).save(a.out / f"{stem}-isolated-difference.png")
            print(f"{stem}: isolated by difference from the figure beneath (covers {covered:.2f})", flush=True)
        else:
            for colour, key_rgb, offset, suffix in ISOLATIONS:
                # A failed isolation keeps nothing, floods the figure with the key or keeps a key-tinted ghost; try the next.
                isolated_path = a.out / f"{stem}-isolated{suffix}.png"
                qwen(dressed_path, ISOLATE.format(name=name, key=colour, rgb=",".join(map(str, key_rgb))), a.seed + offset,
                     isolated_path, steps=a.steps)
                isolated = Image.open(isolated_path).convert("RGB").resize(dressed.size)
                # On the violet key, whatever changed from the figure beneath and is not skin is the piece: Qwen's
                # isolation sometimes trims parts of it (a strap, a shoulder cap) that the difference keeps.
                kept_by = difference_isolation(np.array(dressed), np.array(figure), np.array(isolated)) if violet(entry) else np.array(isolated)
                layer = lift(np.array(dressed), np.array(figure), kept_by, region, k, key_rgb)
                kept = layer[..., 3].sum() / max(1, classic.sum())
                cast = key_cast(np.array(dressed), np.array(isolated), region, key_rgb)
                if 0.15 < kept < 3 and cast < GHOST:
                    break
                print(f"{stem}: {colour} isolation{suffix} kept {kept:.2f} of the classic item's area, cast {cast:.0f}; trying the next", flush=True)
            else:
                Image.fromarray(keyed).save(a.out / f"{stem}-isolated-difference.png")
                layer = lift(np.array(dressed), np.array(figure), keyed, region, k)
                kept = layer[..., 3].sum() / max(1, classic.sum())
                if not 0.15 < kept < 3:
                    print(f"{stem}: no isolation kept the piece; inspect the {stem}-isolated*.png images", flush=True)
                    failed.append(key)
                    continue
                print(f"{stem}: isolated by difference from the figure beneath (kept {kept:.2f})", flush=True)
        Image.fromarray(layer, "RGBA").save(a.out / "layers" / f"{key}.png")
        (a.out / "layers" / f"{key}.json").write_text(json.dumps({"rect": [bx, by, bw, bh], "body": str(body.final), "seed": a.seed}))
        if not a.each:
            figure = dressed
        print(f"{stem}: dressed", flush=True)
    if not a.each:
        figure.save(a.out / "dressed.png")
    if failed:
        raise SystemExit(f"No layer for {len(failed)} items: {' '.join(failed)}")


if __name__ == "__main__":
    main()
