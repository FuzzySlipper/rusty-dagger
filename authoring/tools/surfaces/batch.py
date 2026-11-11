#!/usr/bin/env python3
"""Convert surface texture families (rusty-dagger #10497): every used record of the named families, from the
export-surfaces reference set (local/art/reference/surfaces, usage.csv), to a refreshed final in local/art/generated/surfaces.

For each record, in order, each step skipped when its output exists:
  1. prepare  the classic record; a floor or ceiling (mostly flat planes) without its bevel frame (reduce.unframe).
     Seamless on an axis where the models repeat the record (texture coordinates spanning past it; 16 units a texel)
     and the classic itself wraps there (reduce.wraps); bands and storeys the classic draws stay as they are.
  2. caption  Qwen3-VL (ComfyUI TextGenerate) says what the classic shows: materials, layout, colours; or the authored
     caption in corrections.json beside this script, for a record it misreads (plaster as wood, a slab as small stones).
  3. generate Z-Image base, image to image from the classic upscaled with wrap on its seamless axes, with
     CraftSurvive's Valheim painterly wording and the caption, seamless on those axes (ComfyUI-Universal-Seamless-Tiles),
     denoise 0.55 where it tiles and 0.5 where it does not (a door, a painting), or the record's own in corrections.json
     (0.4 keeps a fine-grained floor's grit, which higher denoise paints as broad blotches).
  4. reduce   reduce.crunch: twice the classic's size, its colours, a strong unsharp mask, an ART_PAL subset.
A winter record (a set's snow archive, one above its summer) is its refreshed summer under the classic winter's own
snow (reduce.winter); its summer is converted first even when only the winter is used. A winter record the classic
redrew as another picture (not reduce.snowed) is converted like a summer one.

    batch.py --host den-m5 --family exterior-city-a --family exterior-roofs
    batch.py --host den-nimo --families-from list.txt
Writes local/art/generated/surfaces/<set>/texture-AAA-R.png (finals), raw/, and captions.json; a run log on stdout.
"""
import argparse, csv, json, os, shutil, sys, tempfile, time
from pathlib import Path

import numpy as np
from PIL import Image

AUTHORING = Path(__file__).resolve().parents[2]
ART = AUTHORING.parent / "local" / "art"
sys.path.insert(0, str(Path(__file__).resolve().parent))
import reduce  # noqa: E402

AP = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
HOSTS = {"den-m5": "http://192.168.1.24:8188", "den-nimo": "http://192.168.1.23:8188"}
RAW = ART / "reference/surfaces"
OUT = ART / "generated/surfaces"
CORRECTIONS = json.loads((Path(__file__).resolve().parent / "corrections.json").read_text())
TEXELS = 16  # ARCH3D texture-coordinate units per texel
CAPTION = ("Describe this game texture in one sentence of at most 45 words: what surface or object it shows (materials, "
           "how they are laid out, any band, frame, window, door or carving and where it sits) and its main colours. "
           "Do not mention style, pixels, resolution or that it is a texture. Reply with the sentence only.")
STYLE = ("{tiling} Hand-painted painterly texture in the manner of Valheim and classic 2000s games: broad soft colour patches, "
         "readable medium-sized shapes, economical brush marks, no photographic micro-detail, soft even lighting, no cast "
         "shadows, no highlights, no text.")
TILING = {"enable": "Seamless tileable surface texture for a dark fantasy RPG, seen straight on, flat orthographic, fills the "
                    "whole frame edge to edge, continues evenly off all four edges.",
          "x_only": "Seamless tileable surface texture for a dark fantasy RPG, seen straight on, flat orthographic, fills the "
                    "whole frame edge to edge, continues evenly off the left and right edges.",
          "y_only": "Seamless tileable surface texture for a dark fantasy RPG, seen straight on, flat orthographic, fills the "
                    "whole frame edge to edge, continues evenly off the top and bottom edges.",
          "disable": "A single surface texture for a dark fantasy RPG, seen straight on, flat orthographic, filling the whole frame."}
NEGATIVE = ("text, letters, signature, watermark, border, frame, perspective, horizon, sky, photograph, photorealistic, 3d render, "
            "cast shadow, scene, people, cartoon, cel shading, vector art, outlines")


def records():
    return list(csv.DictReader(open(RAW / "usage.csv")))


def used(row):
    """Whether a block can draw the record and it has an image (TEXTURE.000 record 0 is the classic's no-texture sentinel)."""
    return int(row["frames"]) > 0 and int(row["exterior_blocks"]) + int(row["interior_blocks"]) + int(row["dungeon_blocks"]) > 0


def classic(row):
    folder = RAW / row["set"] / f"texture-{int(row['archive']):03d}" / "surface"
    return Image.open(folder / f"texture-{int(row['archive']):03d}-record-{int(row['record']):03d}-frame-000-source.png").convert("RGB")


def prepare(row):
    """The classic as generation starts from it, and the seamless mode."""
    image = classic(row)
    if int(row["flat_planes"]) > int(row["standing_planes"]):
        image = reduce.unframe(image)
    wrap_x, wrap_y = reduce.wraps(image)
    width, height = int(row["width"]), int(row["height"])
    repeat_x = int(row["span_u"]) > width * TEXELS * 1.05
    repeat_y = int(row["span_v"]) > height * TEXELS * 1.05
    x, y = wrap_x and repeat_x, wrap_y and repeat_y
    return image, "enable" if x and y else "x_only" if x else "y_only" if y else "disable"


GENERATE = int(os.environ.get("SURFACE_SIZE", "768"))


def size_for(image):
    """GENERATE pixels on the longer side (768: about twice as fast as 1024 for much the same crunched result; 512 draws
    latent banding and loses the classic's layout), the shorter in proportion, both multiples of 64 and at least 128."""
    k = GENERATE / max(image.size)
    return tuple(max(128, round(side * k / 64) * 64) for side in image.size)


def wrap_upscale(image, size, mode):
    """Bicubic to `size`, wrapping on the seamless axes and clamping the others."""
    a = np.asarray(image)
    rx, ry = (3 if mode in ("enable", "x_only") else 1), (3 if mode in ("enable", "y_only") else 1)
    big = Image.fromarray(np.tile(a, (ry, rx, 1))).resize((size[0] * rx, size[1] * ry), Image.Resampling.BICUBIC)
    left, top = size[0] * (rx // 2), size[1] * (ry // 2)
    return big.crop((left, top, left + size[0], top + size[1]))


def caption(comfy, image, cache, key):
    if "caption" in CORRECTIONS.get(key, {}):
        return CORRECTIONS[key]["caption"]
    if key in cache:
        return cache[key]
    with tempfile.TemporaryDirectory() as work:
        path = Path(work) / f"{key}.png"
        scale = max(1, 512 // max(image.size))
        image.resize((image.width * scale, image.height * scale), Image.Resampling.NEAREST).save(path)
        name = comfy.upload(str(path))["name"]
        g = {"clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
             "image": {"class_type": "LoadImage", "inputs": {"image": name}},
             "text": {"class_type": "TextGenerate", "inputs": {"clip": ["clip", 0], "prompt": CAPTION, "max_length": 120, "sampling_mode": "off", "image": ["image", 0]}},
             "show": {"class_type": "PreviewAny", "inputs": {"source": ["text", 0]}}}
        cache[key] = " ".join(comfy.run_text(g)).strip().replace("\n", " ")
    return cache[key]


def generate_raw(comfy, generate, prompt, init_path, size, mode, denoise, out, negative=NEGATIVE):
    init = comfy.upload(str(init_path))["name"]
    seamless = mode != "disable"
    g = generate.zimage("z_image_bf16.safetensors", prompt, negative, 1, size, generate.MODELS["zimage-base"], [], seamless, None, .35)
    if seamless:
        g["seamless"]["inputs"]["tiling"] = mode
        g["circular"]["inputs"]["tiling"] = mode
    vae = ["circular", 0] if seamless else ["vae", 0]
    g["init"] = {"class_type": "LoadImage", "inputs": {"image": init}}
    g["latent"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["init", 0], "vae": vae}}
    g["sample"]["inputs"]["denoise"] = denoise
    with tempfile.TemporaryDirectory() as work:
        shutil.copy(comfy.run(g, os.path.join(work, "out"))[0], out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=list(HOSTS), required=True)
    ap.add_argument("--family", action="append", default=[])
    ap.add_argument("--families-from")
    a = ap.parse_args()
    families = set(a.family) | (set(Path(a.families_from).read_text().split()) if a.families_from else set())
    os.environ["COMFY_URL"] = HOSTS[a.host]
    sys.path.insert(0, str(AP / "tools/lora-training"))
    import comfy, generate  # noqa: E402,E401

    rows = records()
    by_key = {(int(r["archive"]), int(r["record"])): r for r in rows}
    family_of = lambda r: r["set"].removesuffix("-snow")
    todo = [r for r in rows if used(r) and family_of(r) in families]
    # A used winter record needs its summer converted first.
    for r in list(todo):
        if r["set"].endswith("-snow") and (summer := by_key.get((int(r["archive"]) - 1, int(r["record"])))) and summer not in todo:
            todo.insert(0, summer)
    todo.sort(key=lambda r: r["set"].endswith("-snow"))
    captions_path = OUT / "captions.json"
    OUT.mkdir(parents=True, exist_ok=True)
    cache = json.loads(captions_path.read_text()) if captions_path.exists() else {}
    for row in todo:
        archive, record = int(row["archive"]), int(row["record"])
        key = f"texture-{archive:03d}-{record}"
        final = OUT / row["set"] / f"{key}.png"
        if final.exists():
            continue
        final.parent.mkdir(parents=True, exist_ok=True)
        started = time.time()
        summer_row = by_key.get((archive - 1, record)) if row["set"].endswith("-snow") else None
        if summer_row and reduce.snowed(classic(summer_row), classic(row)):
            summer_final = OUT / summer_row["set"] / f"texture-{archive - 1:03d}-{record}.png"
            reduce.winter(Image.open(summer_final), classic(summer_row), classic(row)).save(final)
            print(f"{key} winter", flush=True)
            continue
        image, mode = prepare(row)
        text = caption(comfy, image, cache, key)
        # Another host's run writes the same file: keep its captions too.
        cache = (json.loads(captions_path.read_text()) if captions_path.exists() else {}) | cache
        captions_path.write_text(json.dumps(cache, indent=1) + "\n")
        size = size_for(image)
        raw = OUT / "raw" / f"{key}.png"
        raw.parent.mkdir(parents=True, exist_ok=True)
        if not raw.exists():
            init = OUT / "raw" / f"{key}-init.png"
            wrap_upscale(image, size, mode).save(init)
            generate_raw(comfy, generate, text + " " + STYLE.format(tiling=TILING[mode]), init, size, mode,
                         CORRECTIONS.get(key, {}).get("denoise", 0.5 if mode == "disable" else 0.55), raw)
        reduce.crunch(raw, classic(row)).save(final)
        print(f"{key} {mode} {time.time() - started:.0f} s: {text}", flush=True)
    print("BATCH-DONE", sorted(families))


if __name__ == "__main__":
    main()
