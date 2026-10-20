#!/usr/bin/env python3
"""Generates and processes the authored UI art kit (#10110) with the recipe the owner chose on #10120.

    scripts/ui-art-kit.py generate [SOURCE ...] [--host den-m5|den-nimo] [--seeds 1-2]
    scripts/ui-art-kit.py process  [--pick SOURCE=SEED ...]
    scripts/ui-art-kit.py manifest

This is an operator tool, not part of scripts/regenerate-content.sh: it needs the LAN GPU hosts. Its output is
the processed originals in data/ui-original/ (committed) and their entries in data/ui-authored-assets.json;
regenerate-content.sh then publishes those as usual. The recipe (Den document rusty-dagger/ui-art-recipe):

1. Generate: Krea 2 turbo (krea2_turbo_fp8_scaled, qwen3vl_4b_fp8_scaled, qwen_image_vae; 8 steps, cfg 1, euler,
   simple) with the Retro Dark Fantasy Illustration LoRA (Civitai model 1041229, version 3358183, installed on
   den-m5 and den-nimo as civitai_retro_dark_fantasy_krea2.safetensors) at strength 0.8, through
   asset-pipeline's tools/lora-training/generate.py, on a flat green ground.
2. Key: alpha = 1 - smoothstep(0.35 k, 0.75 k) of greenness g - max(r, b), k = the corner key's greenness;
   green spill clamped; crop to alpha > 8. Cut sheets into their largest blobs.
3. Fit to the published size (every original is at most 256 px, so the importer publishes it unresampled and
   the 9-slice insets stay exact), with a premultiplied Lanczos resize. States are derived from one base so
   their slices line up: focus (brighter, a solid brass rim), pressed (darker, shifted down), disabled (grey,
   dim), invalid (red rim).
4. Palette pass, treatment G1: envy-ai ComfyUI-Krea2-Pixel-Art-Refiner (commit 0b01ea37, node
   Krea2PixelArtRefiner, installed on den-m5 only) with grid 1 and 64 colours plus the key: alpha cut at 128,
   transparent area filled with #00FF00, then colours reduced and the green keyed out. Hard alpha, no pixel grid.

Requires Python 3 with Pillow and numpy, and ASSET_PIPELINE (default /home/agent/dev/asset-pipeline).
Raw generations and graphs go to RAW (default ./local/ui-art-kit, which is ignored).
"""
import argparse
import hashlib
import io
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIPELINE = os.environ.get("ASSET_PIPELINE", "/home/agent/dev/asset-pipeline")
RAW = os.environ.get("RAW", os.path.join(ROOT, "local", "ui-art-kit"))
ORIGINALS = os.path.join(ROOT, "data", "ui-original")
MANIFEST = os.path.join(ROOT, "data", "ui-authored-assets.json")
HOSTS = {"den-m5": "http://192.168.1.24:8188", "den-nimo": "http://192.168.1.23:8188"}
REFINER_HOST = "den-m5"

UNET = "krea2_turbo_fp8_scaled.safetensors"
LORA = "civitai_retro_dark_fantasy_krea2.safetensors"
LORA_STRENGTH = 0.8
TRIGGER = "dark_fantasy_illustration, "
GROUND = "isolated on a plain flat solid pure green background, original design not copied from any game, no text, no letters"
GENERATOR = (f"Krea 2 turbo ({UNET}) + Retro Dark Fantasy Illustration LoRA (Civitai 1041229/3358183) at {LORA_STRENGTH}, "
             "seed {seed}; keyed, fitted within {size}, {states}envy-ai Krea2PixelArtRefiner 0b01ea37 palette pass (grid 1, 64 colours, "
             "hard alpha 128); scripts/ui-art-kit.py")

STONE = "weathered grey stone with polished brass fittings and rivets"
PARCHMENT = "aged cream parchment with a thin carved dark oak edge"
ICON = ("each drawn in full natural colours with only small brass and stone accents, bold readable silhouettes, "
        "evenly spaced and well separated, two rows of three")
SILHOUETTE = ("each a flat pale bone-grey engraved silhouette with a faint darker outline and no other colour, "
              "bold simple readable shapes, evenly spaced and well separated, two rows of three")

KEY_G_LO, KEY_G_HI, SPILL, ALPHA_T, COLORS = 0.35, 0.75, 6, 128, 64
TILE_CONTRAST = 0.8
BRASS = (230, 182, 92)
INVALID = (190, 52, 40)

# Generation sources: id -> (size, prompt). Every prompt gets TRIGGER first and GROUND last.
SOURCES = {
    "stone-window": ("1024x1024", f"game UI asset: an empty square fantasy RPG window frame border, straight-on front view, {STONE}, "
                                  "ornate brass corner brackets, identical straight uniform edges on all four sides, no plate, no title bar, "
                                  "the inside is empty, thin border, centred with margin"),
    "stone-panel": ("1024x1024", f"game UI asset: a single recessed square inset panel, straight-on front view, a thin bevelled "
                                 f"{STONE} rim with small brass corner studs, flat plain dark slate interior, centred and filling most of the frame"),
    "stone-title": ("1536x512", "game UI asset: a single wide blank title bar plate, straight-on front view, polished brass plate with "
                                "rivets and pointed ends set in weathered grey stone, flat blank centre, centred and filling most of the frame"),
    "stone-divider": ("1536x512", "game UI asset: a single thin horizontal divider bar, straight-on front view, a slim polished brass rod "
                                  "with a small ornate stone boss at its centre, very thin, centred and spanning the frame"),
    "stone-button": ("1536x512", f"game UI asset: a single blank wide rectangular fantasy RPG menu button, straight-on front view, "
                                 "weathered grey stone button plate with a thin brass trim, bevelled edge, flat blank centre, centred and filling most of the frame"),
    "stone-tab": ("1024x512", "game UI asset: a single blank fantasy RPG tab, straight-on front view, a weathered grey stone tab with "
                              "rounded top corners and a thin brass trim along its top and sides, open flat bottom edge, flat blank centre, "
                              "centred and filling most of the frame"),
    "stone-scroll": ("1024x1024", "game UI asset: two separate vertical scrollbar parts side by side, straight-on front view: on the left "
                                  "a long thin recessed dark slate groove with brass end caps, on the right a short thin polished brass "
                                  "slider handle with a stone grip, well separated"),
    "stone-slot": ("1024x1024", "game UI asset: a single empty square fantasy RPG inventory slot, straight-on front view, recessed "
                                "dark slate socket with a weathered grey stone rim and tiny brass corner studs, centred and filling most of the frame"),
    "stone-texture": ("1024x1024", "game UI asset: a flat even seamless surface texture of smooth dark grey slate stone, very low contrast, "
                                   "fine subtle grain, no cracks, no objects, no border, filling the whole frame edge to edge"),
    "parchment-window": ("1024x1024", "game UI asset: an empty square fantasy RPG reading panel frame, straight-on front view, a border of "
                                      "carved dark oak with small darkened iron corner caps, identical straight uniform edges on all four "
                                      "sides, the inside is plain aged cream parchment, thin border, centred with margin"),
    "parchment-texture": ("1024x1024", "game UI asset: a flat even seamless texture of aged cream parchment paper, very low contrast, "
                                       "soft fibres, no stains near the edges, no objects, no border, filling the whole frame edge to edge"),
    "icons-menu-1": ("1536x1024", f"game UI asset sheet: six fantasy RPG menu icons: a leather backpack, a hooded adventurer's bust, "
                                  f"an open spellbook with a glowing blue rune, a jewelled ring and amulet, a bound journal with a quill, "
                                  f"a small notebook with a pencil; {ICON}"),
    "icons-menu-2": ("1536x1024", f"game UI asset sheet: six fantasy RPG menu icons: a rolled parchment map, a bedroll by a campfire, "
                                  f"a horse-drawn cart wheel and road sign, a wax-sealed scroll with a down arrow, an open scroll with an "
                                  f"up arrow, a brass gear wheel; {ICON}"),
    "icons-items": ("1536x1024", f"game UI asset sheet: six fantasy RPG item category icons: a crossed sword and shield, a glowing "
                                 f"magic staff with a crystal, a folded tunic and boots, a mortar and pestle with dried brown herbs and red berries, a small stack "
                                 f"of gold coins, a bulging leather sack; {ICON}"),
    "icons-activation": ("1536x1024", f"game UI asset sheet: six fantasy RPG action icons: a sneaking hand reaching for a coin purse, "
                                      f"an open grasping hand, an eye with a magnifying lens, a speech bubble with two faces, a closed "
                                      f"fist, an open door; {ICON}"),
    "icons-equip-1": ("1536x1024", f"game UI asset sheet: six fantasy equipment slot silhouettes: a knight's helmet, a shoulder "
                                   f"pauldron, a cloak, a tunic shirt, a plate cuirass, a pair of gauntlet gloves; {SILHOUETTE}"),
    "icons-equip-2": ("1536x1024", f"game UI asset sheet: six fantasy equipment slot silhouettes: a longsword, a round shield, a pair "
                                   f"of plate greaves, a pair of trousers, a pair of boots, a vambrace bracer; {SILHOUETTE}"),
    "icons-equip-3": ("1536x1024", f"game UI asset sheet: six fantasy equipment slot silhouettes: an amulet on a chain, a bracelet, "
                                   f"a ring, a mystic mark sigil, a faceted crystal, a pouch; {SILHOUETTE}"),
    "cursor-normal": ("1024x1024", "game UI asset: a single fantasy RPG mouse pointer, a polished brass arrowhead with a dark iron "
                                   "outline and a light rim, pointing diagonally up and to the left with the tip at the upper left, "
                                   "bold readable silhouette, centred"),
    "cursor-pointer": ("1024x1024", "game UI asset: a single fantasy RPG mouse cursor, a gauntleted hand of dark iron with bright steel "
                                    "highlights and a brown leather cuff, the index finger pointing diagonally up and to the left with "
                                    "the fingertip at the upper left, bold readable silhouette with a light rim, centred"),
    "cursor-grab": ("1024x1024", "game UI asset: a single fantasy RPG mouse cursor, a gauntleted hand of dark iron with bright steel "
                                 "highlights and a brown leather cuff, closed into a gripping fist as if holding something, seen from "
                                 "the back, bold readable silhouette with a light rim, centred"),
}

# Published assets. kind: single (whole keyed source), sheet (blob n of a sheet, row-major), tile (seamless
# texture, opaque). size: published pixels (fit inside, keeping aspect, for single and sheet; exact for tile).
# display: the 9-slice insets in published px (top, right, bottom, left), CSS px per published px at UI scale 1,
# and a cursor hotspot in published px. states: derived variants (suffix -> transform).
A = []


def asset(aid, source, size, kind="single", index=None, display=None, states=None, sheet=6):
    A.append(dict(id=aid, source=source, size=size, kind=kind, index=index, display=display or {}, states=states or {}, sheet=sheet))


asset("ui.stone.window.v2", "stone-window", (256, 256), display={"slice": [36, 36, 36, 36], "scale": 0.5})
asset("ui.stone.panel.v2", "stone-panel", (256, 256), display={"slice": [36, 36, 36, 36], "scale": 0.5})
asset("ui.stone.title.v2", "stone-title", (256, 86), display={"slice": [0, 48, 0, 48], "scale": 0.5})
asset("ui.stone.divider.v2", "stone-divider", (256, 32), display={"slice": [0, 20, 0, 20], "scale": 0.5})
asset("ui.stone.button.v2", "stone-button", (256, 86), display={"slice": [18, 22, 18, 22], "scale": 0.5},
      states={"focus": "focus", "pressed": "pressed", "disabled": "disabled"})
asset("ui.stone.tab.v2", "stone-tab", (192, 96), display={"slice": [16, 24, 8, 24], "scale": 0.5}, states={"active": "focus"})
asset("ui.stone.scroll-track.v2", "stone-scroll", (48, 256), kind="sheet", index=1, sheet=2, display={"slice": [24, 0, 24, 0], "scale": 0.5})
asset("ui.stone.scroll-thumb.v2", "stone-scroll", (48, 128), kind="sheet", index=2, sheet=2, display={"slice": [24, 0, 12, 0], "scale": 0.5})
asset("ui.stone.slot.v2", "stone-slot", (128, 128), display={"slice": [20, 20, 20, 20], "scale": 0.5},
      states={"focus": "focus", "invalid": "invalid"})
asset("ui.stone.texture.v2", "stone-texture", (256, 256), kind="tile", display={"scale": 0.5})
asset("ui.parchment.window.v2", "parchment-window", (256, 256), display={"slice": [24, 24, 24, 24], "scale": 0.5})
asset("ui.parchment.texture.v2", "parchment-texture", (256, 256), kind="tile", display={"scale": 0.5})
for sheet, names in [
    ("icons-menu-1", ["inventory", "character", "spells", "magic-items", "journal", "notebook"]),
    ("icons-menu-2", ["map", "rest", "travel", "save", "load", "settings"]),
]:
    for n, name in enumerate(names, 1):
        asset(f"ui.icon.menu.{name}.v2", sheet, (128, 128), kind="sheet", index=n, display={"scale": 0.5})
for n, name in enumerate(["weapons-armor", "magic-items", "clothing-misc", "ingredients", "gold", "all"], 1):
    asset(f"ui.icon.category.{name}.v2", "icons-items", (128, 128), kind="sheet", index=n, display={"scale": 0.5})
for n, name in enumerate(["steal", "grab", "info", "talk"], 1):
    asset(f"ui.icon.activation.{name}.v2", "icons-activation", (128, 128), kind="sheet", index=n, display={"scale": 0.5})
for sheet, names in [
    ("icons-equip-1", ["head", "arm", "cloak", "chest-clothes", "chest-armor", "gloves"]),
    ("icons-equip-2", ["weapon", "shield", "legs-armor", "legs-clothes", "feet", "bracer"]),
    ("icons-equip-3", ["amulet", "bracelet", "ring", "mark", "crystal", None]),
]:
    for n, name in enumerate(names, 1):
        if name:
            asset(f"ui.icon.equip.{name}.v2", sheet, (96, 96), kind="sheet", index=n, display={"scale": 0.5})
asset("ui.cursor.normal.v2", "cursor-normal", (64, 64), display={"scale": 0.5, "hotspot": [2, 2]})
asset("ui.cursor.pointer.v2", "cursor-pointer", (64, 64), display={"scale": 0.5, "hotspot": [2, 2]})
asset("ui.cursor.grab.v2", "cursor-grab", (64, 64), display={"scale": 0.5, "hotspot": [32, 32]})


def prompt(source):
    return TRIGGER + SOURCES[source][1] + ", " + GROUND


# ---------- generation ----------

def generate(sources, host, seeds):
    env = dict(os.environ, COMFY_URL=HOSTS[host])
    for source in sources:
        size, _ = SOURCES[source]
        out = os.path.join(RAW, source)
        os.makedirs(out, exist_ok=True)
        subprocess.run([sys.executable, os.path.join(PIPELINE, "tools/lora-training/generate.py"), out,
                        "--model", "krea2-turbo", "--unet", UNET, "--lora", f"{LORA}:{LORA_STRENGTH}",
                        "--prompt", prompt(source), "--seeds", seeds, "--size", size, "--tag", source], env=env, check=True)


# ---------- processing ----------

def key(img):
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    patches = np.concatenate([a[:16, :16].reshape(-1, 3), a[:16, -16:].reshape(-1, 3),
                              a[-16:, :16].reshape(-1, 3), a[-16:, -16:].reshape(-1, 3)])
    kc = np.median(patches, axis=0)
    kg = kc[1] - max(kc[0], kc[2])
    if kg <= 30:
        raise SystemExit("the source has no green key in its corners")
    gness = a[..., 1] - np.maximum(a[..., 0], a[..., 2])
    t = np.clip((gness - KEY_G_LO * kg) / ((KEY_G_HI - KEY_G_LO) * kg), 0, 1)
    alpha = 1 - t * t * (3 - 2 * t)
    lim = np.maximum(a[..., 0], a[..., 2]) + SPILL
    a[..., 1] = np.where(alpha < 0.999, np.minimum(a[..., 1], lim), a[..., 1])
    return Image.fromarray(np.dstack([a, alpha * 255]).clip(0, 255).astype(np.uint8), "RGBA")


def bbox(img, thresh=8):
    ys, xs = np.where(np.asarray(img)[..., 3] > thresh)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def blobs(mask, down=4, gap=1):
    h, w = mask.shape[0] // down, mask.shape[1] // down
    m = mask[: h * down, : w * down].reshape(h, down, w, down).any(axis=(1, 3))
    m = np.asarray(Image.fromarray(m.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(2 * gap + 1))) > 0
    lab = np.zeros(m.shape, int)
    out, cur = [], 0
    for y in range(h):
        for x in range(w):
            if m[y, x] and not lab[y, x]:
                cur += 1
                stack, ys, xs = [(y, x)], [], []
                lab[y, x] = cur
                while stack:
                    cy, cx = stack.pop()
                    ys.append(cy); xs.append(cx)
                    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                        if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = cur
                            stack.append((ny, nx))
                out.append((min(xs) * down, min(ys) * down, (max(xs) + 1) * down, (max(ys) + 1) * down))
    return out


def sheet_cells(keyed, count):
    boxes = blobs(np.asarray(keyed)[..., 3] > 128)
    boxes = sorted(boxes, key=lambda b: -(b[2] - b[0]) * (b[3] - b[1]))[:count]
    if len(boxes) < count:
        raise SystemExit(f"the sheet has {len(boxes)} separate shapes where {count} are needed")
    rowh = keyed.height / 4
    boxes.sort(key=lambda b: (round(((b[1] + b[3]) / 2) / rowh), (b[0] + b[2]) / 2))
    pad = 6
    return [keyed.crop((max(0, x0 - pad), max(0, y0 - pad), min(keyed.width, x1 + pad), min(keyed.height, y1 + pad)))
            for x0, y0, x1, y1 in boxes]


def premul_resize(img, size):
    arr = np.asarray(img).astype(np.float32)
    al = arr[..., 3:4] / 255
    pm = np.dstack([arr[..., :3] * al, arr[..., 3:4]]).astype(np.float32)
    out = np.dstack([np.asarray(Image.fromarray(pm[..., i]).resize(size, Image.LANCZOS)) for i in range(4)])
    a2 = np.clip(out[..., 3:4], 1e-3, 255) / 255
    rgb = np.where(out[..., 3:4] > 0.5, out[..., :3] / a2, 0)
    return Image.fromarray(np.dstack([rgb, out[..., 3:4]]).clip(0, 255).astype(np.uint8), "RGBA")


def fit(img, size, square=False, top_left=False):
    """Crop to content and fit inside size keeping aspect; icons and cursors are centred on a square transparent canvas."""
    img = img.crop(bbox(img))
    s = min(size[0] / img.width, size[1] / img.height)
    w, h = max(1, round(img.width * s)), max(1, round(img.height * s))
    r = premul_resize(img, (w, h))
    if not square:
        return r  # frames keep the drawn proportions; their insets are set against the processed size
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    # a pointing cursor's tip is the content's upper-left corner, so it sits on the canvas corner (its hotspot)
    canvas.alpha_composite(r, (0, 0) if top_left else ((size[0] - w) // 2, (size[1] - h) // 2))
    return canvas


def tile(img, size):
    """Seamless tile: centre crop, cross-fade with a half-offset copy along each axis in turn, remove large-scale
    shading (a blur of size/8 replaced by the mean colour) and scale contrast by TILE_CONTRAST."""
    rgb = img.convert("RGB")
    side = min(rgb.size) * 3 // 4
    x0, y0 = (rgb.width - side) // 2, (rgb.height - side) // 2
    out = np.asarray(rgb.crop((x0, y0, x0 + side, y0 + side)).resize(size, Image.LANCZOS)).astype(np.float32)
    # one axis at a time: blend with a copy shifted by half, weighted 1 at the edges (where the shifted copy
    # wraps continuously) and 0 at the centre line (where the shifted copy has its seam)
    for axis, n in ((1, size[0]), (0, size[1])):
        shifted = np.roll(out, n // 2, axis=axis)
        t = np.abs(np.arange(n) / (n - 1) * 2 - 1)
        w = (t * t * (3 - 2 * t)).reshape((1, n, 1) if axis == 1 else (n, 1, 1))
        out = out * (1 - w) + shifted * w
    # a panel ground sits behind text: flatten large-scale shading so the repeat does not show
    wrapped = Image.fromarray(np.tile(out.clip(0, 255).astype(np.uint8), (3, 3, 1)))  # blur as a tile, so edges wrap
    blur = np.asarray(wrapped.filter(ImageFilter.GaussianBlur(size[0] / 8))).astype(np.float32)[size[1]:2 * size[1], size[0]:2 * size[0]]
    out = out - blur + blur.reshape(-1, 3).mean(axis=0)
    out = out.mean(axis=(0, 1)) + (out - out.mean(axis=(0, 1))) * TILE_CONTRAST
    return Image.fromarray(np.dstack([out, np.full(size[::-1], 255.0)]).clip(0, 255).astype(np.uint8), "RGBA")


def state(img, kind):
    arr = np.asarray(img).astype(np.float32)
    rgb, al = arr[..., :3], arr[..., 3]
    if kind == "focus":
        rgb = rgb * np.array([1.18, 1.14, 1.04])
        return rim(Image.fromarray(np.dstack([rgb, al]).clip(0, 255).astype(np.uint8), "RGBA"), BRASS)
    if kind == "pressed":
        rgb = rgb * 0.78
        out = Image.fromarray(np.dstack([rgb, al]).clip(0, 255).astype(np.uint8), "RGBA")
        shifted = Image.new("RGBA", out.size, (0, 0, 0, 0))
        shifted.alpha_composite(out.crop((0, 0, out.width, out.height - 2)), (0, 2))
        return shifted
    if kind == "disabled":
        grey = rgb.mean(axis=2, keepdims=True)
        rgb = (rgb * 0.25 + grey * 0.75) * 0.62
        return Image.fromarray(np.dstack([rgb, al]).clip(0, 255).astype(np.uint8), "RGBA")
    if kind == "invalid":
        return rim(img, INVALID)
    raise ValueError(kind)


def rim(img, colour, width=3):
    """A solid rim drawn just inside the opaque edge, so the state never changes the asset's size or slices."""
    a = np.asarray(img)[..., 3] >= ALPHA_T
    # pad with transparency first: the filter repeats edge pixels, so art that touches the image edge
    # would otherwise get no rim along it
    padded = np.pad(a, width + 1)
    inner = np.asarray(Image.fromarray((padded * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(2 * width + 1))) > 0
    inner = inner[width + 1:-(width + 1), width + 1:-(width + 1)]
    ring = a & ~inner
    arr = np.asarray(img).copy()
    arr[ring, :3] = colour
    return Image.fromarray(arr, "RGBA")


def palette_pass(img):
    """G1: Krea2PixelArtRefiner at grid 1 with COLORS + 1 colours on den-m5, then the key green removed."""
    sys.path.insert(0, os.path.join(PIPELINE, "tools/lora-training"))
    os.environ["COMFY_URL"] = HOSTS[REFINER_HOST]
    import comfy  # noqa: E402

    arr = np.asarray(img)
    keep = arr[..., 3] >= ALPHA_T
    flat = Image.fromarray(np.where(keep[..., None], arr[..., :3], np.array([0, 255, 0], np.uint8)).astype(np.uint8), "RGB")
    buf = io.BytesIO(); flat.save(buf, "PNG")
    digest = hashlib.sha256(buf.getvalue()).hexdigest()[:24]
    work = os.path.join(RAW, "refine")
    os.makedirs(work, exist_ok=True)
    src = os.path.join(work, digest + "-in.png")
    flat.save(src)
    comfy.upload(src)
    graph = {
        "load": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(src)}},
        "refine": {"class_type": "Krea2PixelArtRefiner", "inputs": {"image": ["load", 0], "width": flat.width, "height": flat.height,
                                                                  "colors": COLORS + 1, "color_reduction_first": True, "scale_to_original": True}},
        "save": {"class_type": "SaveImage", "inputs": {"images": ["refine", 0], "filename_prefix": "dagger-ui-kit/" + digest}},
    }
    out = comfy.run(graph, os.path.join(work, digest), timeout=600)[0]
    a = np.asarray(Image.open(out).convert("RGB")).astype(np.int16)
    alpha = np.where(a[..., 1] - np.maximum(a[..., 0], a[..., 2]) > 128, 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([a.astype(np.uint8), alpha]), "RGBA")


def file_name(aid):
    return aid.replace(".", "-") + ".png"


def process(picks):
    os.makedirs(ORIGINALS, exist_ok=True)
    keyed_cache = {}
    record = {}
    for spec in A:
        source, seed = spec["source"], picks.get(spec["source"], 1)
        raw = os.path.join(RAW, source, f"{source}-{seed}.png")
        if spec["kind"] == "tile":
            base = tile(Image.open(raw), spec["size"])
        else:
            if (source, seed) not in keyed_cache:
                keyed_cache[(source, seed)] = key(Image.open(raw))
            keyed = keyed_cache[(source, seed)]
            if spec["kind"] == "sheet":
                cells = sheet_cells(keyed, spec["sheet"])
                square = spec["sheet"] == 6
                base = fit(cells[spec["index"] - 1], spec["size"], square=square)
            else:
                cursor = spec["id"].startswith("ui.cursor")
                base = fit(keyed, spec["size"], square=cursor, top_left=cursor and spec["display"]["hotspot"][0] < 8)
        variants = {"": base} | {f".{name}": state(base, kind) for name, kind in spec["states"].items()}
        for suffix, img in variants.items():
            aid = spec["id"].replace(".v2", f"{suffix}.v2")
            out = palette_pass(img)
            if spec["kind"] == "tile":
                out.putalpha(255)
            out.save(os.path.join(ORIGINALS, file_name(aid)), optimize=True)
            record[aid] = dict(source=source, seed=seed, size=spec["size"], state=suffix[1:] or None, display=spec["display"])
            print(aid, out.size, flush=True)
    with open(os.path.join(RAW, "processed.json"), "w") as f:
        json.dump(record, f, indent=1)


def generated_prompt(source, seed):
    """The prompt the picked image was actually generated with, from generate.py's runs.jsonl."""
    found = None
    with open(os.path.join(RAW, source, "runs.jsonl")) as runs:
        for line in runs:
            run = json.loads(line)
            if run.get("seed") == seed and os.path.basename(run.get("image", "")) == f"{source}-{seed}.png":
                found = run["prompt"]
    if found is None:
        raise SystemExit(f"no generation record for {source} seed {seed}")
    return found


def manifest():
    record = json.load(open(os.path.join(RAW, "processed.json")))
    assets = []
    for aid, r in sorted(record.items()):
        states = f"{r['state']} state derived, " if r["state"] else ""
        entry = {
            "id": aid,
            "file": file_name(aid),
            "sourceFile": file_name(aid),
            "generator": GENERATOR.format(seed=r["seed"], size="x".join(map(str, r["size"])), states=states),
            "prompt": generated_prompt(r["source"], r["seed"]),
        }
        if r["display"]:
            entry["display"] = r["display"]
        assets.append(entry)
    with open(MANIFEST, "w") as f:
        json.dump({"assets": assets}, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(MANIFEST, len(assets), "assets")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("generate")
    g.add_argument("sources", nargs="*")
    g.add_argument("--host", default="den-m5", choices=HOSTS)
    g.add_argument("--seeds", default="1")
    p = sub.add_parser("process")
    p.add_argument("--pick", action="append", default=[], help="SOURCE=SEED (default seed 1)")
    sub.add_parser("manifest")
    a = ap.parse_args()
    if a.cmd == "generate":
        generate(a.sources or list(SOURCES), a.host, a.seeds)
    elif a.cmd == "process":
        process({k: int(v) for k, v in (p.split("=") for p in a.pick)})
    else:
        manifest()


if __name__ == "__main__":
    main()
