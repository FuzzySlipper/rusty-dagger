#!/usr/bin/env python3
"""Edit or generate one image with Qwen-Image-2.1 on a ComfyUI host.

  qwen_edit.py --image in.png [--image ref2.png ...] --prompt "make the glass amber" --out out.png
  qwen_edit.py --prompt "worn brass plate, top-down" --size 1024x1024 --out out.png   # text to image
  qwen_edit.py --image in.png --crop 200,0,400,400 --mask head-mask.png \
      --prompt "remove hair and painted facial features" --out blank-head.png

The graph is the API form of ComfyUI's `image_qwen_image_2_1_image_edit`
template without its optional prompt enhancer: int8 Qwen-Image-2.1, the
qwen3vl_8b text encoder, 25 euler steps at cfg 1. With images, the first
image sets the output size (resized to about --resolution^2 pixels) and every
image is a reference (up to 10). The host defaults to the owner's 5090;
set COMFY_URL to use another ComfyUI that has the same weights.

Writes OUT and OUT.json (prompt, seed, inputs and their sha256, ComfyUI
prompt id, wall time), plus OUT.prompt.json and OUT.history.json for replay.
With --crop or --mask, the edit is composited onto the first image at its
original size. --mask is a grayscale image on that same canvas: white edits,
black preserves, grey blends. The mask limits compositing, not Qwen sampling.
OUT.edit.png retains the resized edit patch; OUT.mask.png retains the mask.

--guide AREA.png (a grayscale image on the first image's canvas, white = the region to change) shows
Qwen where to edit: OUT.guide.png, the first image with that region tinted translucent magenta
(--guide-alpha), goes in as the second image, and a sentence saying so is put before the prompt
(--no-guide-note leaves the prompt alone). It steers sampling; pass the same file as --mask to also
limit compositing. After StableGen 0.4.0's Masked Edit selection guide.

--ledger NAME also records the output, its patch and inputs in the generation ledger
(tools/lora-training/ledger.py) as pending, tagged by each --ledger-tag key=value, so a later rerun
that overwrites OUT keeps this image and its verdict can be marked.
"""
import argparse
import hashlib
import json
import os
import pathlib
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

COMFY = os.environ.get("COMFY_URL", "http://192.168.1.20:8188")
# The model files: int8 by default (as on the owner's 5090); QWEN_UNET and QWEN_CLIP name others, such as the bf16
# builds (qwen_image_2.1_bf16.safetensors, qwen3vl_8b_bf16.safetensors) for a quantisation check.
UNET = os.environ.get("QWEN_UNET", "qwen_image_2.1_int8_convrot.safetensors")
CLIP = os.environ.get("QWEN_CLIP", "qwen3vl_8b_int8_convrot.safetensors")
# Where QwenImage21Cache keeps its (lossless) prefix cache: auto, gpu, cpu or off; cpu frees VRAM on a 16 GB card.
CACHE_DEVICE = os.environ.get("QWEN_CACHE_DEVICE", "auto")


def post(path, data, ctype="application/json"):
    req = urllib.request.Request(COMFY + path, data, {"Content-Type": ctype})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        body = e.read()
        try:
            return json.loads(body)
        except ValueError:
            raise SystemExit(f"{path}: HTTP {e.code}: {body[:500]!r}")


def get(path):
    with urllib.request.urlopen(COMFY + path, timeout=120) as r:
        return r.read()


def upload(image: pathlib.Path) -> str:
    data = image.read_bytes()
    name = f"qe-{hashlib.sha256(data).hexdigest()[:16]}{image.suffix or '.png'}"
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
            "Content-Type: image/png\r\n\r\n").encode() + data + (
        f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue"
        f"\r\n--{boundary}--\r\n").encode()
    return post("/upload/image", body, f"multipart/form-data; boundary={boundary}")["name"]


def build(names, prompt, negative, seed, steps, resolution, size, prefix, crop=None, mask_name=None):
    g = {
        "1": ("UNETLoader", {"unet_name": UNET, "weight_dtype": "default"}),
        "2": ("QwenImage21Cache", {"model": ["1", 0], "device": CACHE_DEVICE, "dtype": "default"}),
        "3": ("CLIPLoader", {"clip_name": CLIP, "type": "qwen_image", "device": "default"}),
        "4": ("VAELoader", {"vae_name": "qwen_image_2.1_vae_bf16.safetensors"}),
        "5": ("TextEncodeQwenImage21", {"clip": ["3", 0], "vae": ["4", 0], "prompt": prompt,
                                        "negative_prompt": negative, "resolution": resolution}),
    }
    for i, name in enumerate(names, 1):
        g[f"10{i}"] = ("LoadImage", {"image": name})
        g["5"][1][f"images.image_{i}"] = [f"10{i}", 0]
    reference = ["101", 0]
    if crop:
        x, y, w, h = crop
        g["110"] = ("ImageCropV2", {"image": reference, "crop_region": {"x": x, "y": y, "width": w, "height": h}})
        reference = ["110", 0]
        g["5"][1]["images.image_1"] = reference
    if names:
        latent = ["5", 2]
    else:
        w, h = size
        g["6"] = ("EmptyLatentImage", {"width": w, "height": h, "batch_size": 1})
        latent = ["6", 0]
    g["7"] = ("KSampler", {"model": ["2", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": latent,
                           "seed": seed, "steps": steps, "cfg": 1, "sampler_name": "euler",
                           "scheduler": "simple", "denoise": 1})
    g["8"] = ("VAEDecode", {"samples": ["7", 0], "vae": ["4", 0]})
    g["9"] = ("SaveImage", {"images": ["8", 0], "filename_prefix": prefix})
    if crop or mask_name:
        # Restore the actual crop/source dimensions before pasting. Qwen's
        # reference encoder resizes to its pixel budget while sampling.
        g["30"] = ("GetImageSize", {"image": reference})
        g["31"] = ("ImageScale", {"image": ["8", 0], "upscale_method": "lanczos",
                                  "width": ["30", 0], "height": ["30", 1], "crop": "disabled"})
        composite = {"destination": ["101", 0], "source": ["31", 0],
                     "x": crop[0] if crop else 0, "y": crop[1] if crop else 0, "resize_source": False}
        if mask_name:
            g["40"] = ("LoadImage", {"image": mask_name})
            g["41"] = ("ImageToMask", {"image": ["40", 0], "channel": "red"})
            mask = ["41", 0]
            if crop:
                g["42"] = ("CropMask", {"mask": mask, "x": crop[0], "y": crop[1],
                                       "width": crop[2], "height": crop[3]})
                mask = ["42", 0]
            composite["mask"] = mask
            g["43"] = ("MaskToImage", {"mask": ["41", 0]})
            g["91"] = ("SaveImage", {"images": ["43", 0], "filename_prefix": prefix + "-mask"})
        g["33"] = ("ImageCompositeMasked", composite)
        g["9"][1]["images"] = ["33", 0]
        g["90"] = ("SaveImage", {"images": ["31", 0], "filename_prefix": prefix + "-edit"})
    return {k: {"class_type": c, "inputs": i} for k, (c, i) in g.items()}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--image", action="append", default=[], type=pathlib.Path,
                    help="input/reference image; the first sets the output size (repeat, max 10)")
    ap.add_argument("--prompt", required=True)
    ap.add_argument("--negative", default="")
    ap.add_argument("--out", required=True, type=pathlib.Path)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--steps", type=int, default=25)
    ap.add_argument("--resolution", type=int, default=1024,
                    help="reference pixel budget, about N*N (0 keeps each image's own size)")
    ap.add_argument("--size", default="1024x1024", help="WxH for text-to-image (no --image)")
    ap.add_argument("--crop", help="first-image edit crop: x,y,width,height; composite returns the original canvas")
    ap.add_argument("--mask", type=pathlib.Path, help="grayscale compositing mask, same dimensions as the first image")
    ap.add_argument("--guide", type=pathlib.Path, help="region-to-change mask on the first image's canvas; sent as a magenta-tinted second image")
    ap.add_argument("--guide-alpha", type=float, default=0.45, help="opacity of the --guide tint")
    ap.add_argument("--no-guide-note", action="store_true", help="do not add the guide sentence to the prompt")
    ap.add_argument("--ledger", help="record the output in this generation ledger (tools/lora-training/ledger.py)")
    ap.add_argument("--ledger-tag", action="append", default=[], help="key=value tag for the ledger entry (repeat)")
    a = ap.parse_args()
    # These controls need a destination image; silently ignoring them would
    # produce an unrestricted generation instead of the requested local edit.
    if (a.crop or a.mask or a.guide) and not a.image:
        ap.error("--crop/--mask/--guide require --image")
    if a.crop:
        try:
            a.crop = tuple(int(v) for v in a.crop.split(","))
            x, y, w, h = a.crop
            if x < 0 or y < 0 or w <= 0 or h <= 0:
                raise ValueError
        except ValueError:
            ap.error("--crop must be x,y,width,height with nonnegative x/y and positive width/height")
    requested_crop = a.crop
    if a.crop or a.mask:
        from PIL import Image

        with Image.open(a.image[0]) as source:
            canvas_size = source.size
        if a.crop:
            x, y, w, h = a.crop
            # An origin outside the image otherwise produces a successful
            # no-op composite; a partially overhanging crop can safely clip.
            if x >= canvas_size[0] or y >= canvas_size[1]:
                ap.error(f"--crop origin lies outside the {canvas_size[0]}x{canvas_size[1]} source image")
            a.crop = (x, y, min(w, canvas_size[0] - x), min(h, canvas_size[1] - y))
            if a.crop != requested_crop:
                print(f"warning: crop clipped to source canvas: {a.crop}", file=sys.stderr)
        if a.mask:
            with Image.open(a.mask) as mask:
                # Comfy silently resizes mismatched masks, which can apply a
                # face edit to the wrong region of the original canvas.
                if mask.size != canvas_size:
                    ap.error(f"--mask size {mask.size} must match the source canvas {canvas_size}")
    guide_path = None
    if a.guide:
        from PIL import Image

        with Image.open(a.image[0]) as source, Image.open(a.guide) as gm:
            base = source.convert("RGB")
            if gm.size != base.size:
                ap.error(f"--guide size {gm.size} must match the first image {base.size}")
            tint = Image.new("RGB", base.size, (255, 0, 255))
            alpha = gm.convert("L").point(lambda v: int(v * a.guide_alpha))
            guide_path = pathlib.Path(str(a.out) + ".guide.png")
            guide_path.parent.mkdir(parents=True, exist_ok=True)
            Image.composite(tint, base, alpha).save(guide_path)
        a.image.insert(1, guide_path)
        if not a.no_guide_note:
            a.prompt = ("The second image is the first image with the region to change tinted translucent magenta. "
                        "Change only that region of the first image and keep everything else exactly as it is; "
                        "the magenta is only a marker and must not appear in the result. " + a.prompt)
    if len(a.image) > 10:
        raise SystemExit("at most 10 images")
    names = [upload(p) for p in a.image]
    mask_name = upload(a.mask) if a.mask else None
    size = tuple(int(v) for v in a.size.lower().split("x"))
    prefix = f"qwen-edit/{a.out.stem}"
    graph = build(names, a.prompt, a.negative, a.seed, a.steps, a.resolution, size, prefix, a.crop, mask_name)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    pathlib.Path(str(a.out) + ".prompt.json").write_text(json.dumps(graph, indent=1))
    t0 = time.time()
    res = post("/prompt", json.dumps({"prompt": graph, "client_id": f"qe-{uuid.uuid4().hex[:8]}"}).encode())
    pid = res.get("prompt_id")
    if res.get("node_errors") or not pid:
        if pid:
            post("/queue", json.dumps({"delete": [pid]}).encode())
        raise SystemExit(f"node_errors: {json.dumps(res)[:1500]}")
    missing = 0   # fail fast when ComfyUI no longer knows the job (crash/restart): not in the history nor the queue three polls running
    while True:
        time.sleep(2)
        try:
            h = json.loads(get(f"/history/{pid}"))
            if pid in h:
                break
            q = json.loads(get("/queue"))
        except (OSError, ValueError):   # not answering (restarting): keep polling
            time.sleep(10); continue
        if any(item[1] == pid for item in q["queue_running"] + q["queue_pending"]):
            missing = 0
        else:
            missing += 1
            if missing >= 3:
                raise SystemExit(f"job {pid} lost: in neither the history nor the queue (ComfyUI restarted?)")
    hist = h[pid]
    pathlib.Path(str(a.out) + ".history.json").write_text(json.dumps(hist, indent=1))
    if hist.get("status", {}).get("status_str") != "success":
        msgs = [m for m in hist.get("status", {}).get("messages", []) if m[0] == "execution_error"]
        raise SystemExit(f"failed: {json.dumps(msgs)[:1500]}")
    it = hist["outputs"]["9"]["images"][0]
    q = urllib.parse.urlencode({"filename": it["filename"], "subfolder": it.get("subfolder", ""), "type": "output"})
    data = get(f"/view?{q}")
    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_bytes(data)
    for node, suffix in (("90", ".edit.png"), ("91", ".mask.png")):
        if node in hist["outputs"]:
            item = hist["outputs"][node]["images"][0]
            query = urllib.parse.urlencode({"filename": item["filename"], "subfolder": item.get("subfolder", ""), "type": "output"})
            pathlib.Path(str(a.out) + suffix).write_bytes(get(f"/view?{query}"))
    meta = {"prompt": a.prompt, "negative": a.negative, "seed": a.seed, "steps": a.steps, "resolution": a.resolution,
            "inputs": [{"path": str(p), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in a.image],
            "output_sha256": hashlib.sha256(data).hexdigest(), "prompt_id": pid, "comfy": COMFY,
            "model": UNET.removesuffix(".safetensors"), "text_encoder": CLIP.removesuffix(".safetensors"), "host": COMFY, "wall_seconds": round(time.time() - t0, 1)}
    if a.guide:
        meta["guide"] = {"path": str(a.guide), "sha256": hashlib.sha256(a.guide.read_bytes()).hexdigest(),
                         "alpha": a.guide_alpha, "image": str(guide_path), "note": not a.no_guide_note}
    if a.crop or a.mask:
        meta.update({"crop": a.crop, "requested_crop": requested_crop, "mask": {"path": str(a.mask), "sha256": hashlib.sha256(a.mask.read_bytes()).hexdigest()} if a.mask else None,
                     "edit_patch": str(a.out) + ".edit.png", "mask_preview": str(a.out) + ".mask.png" if a.mask else None})
    pathlib.Path(str(a.out) + ".json").write_text(json.dumps(meta, indent=1))
    print(a.out, len(data), f"{meta['wall_seconds']}s")
    if a.ledger:
        sys.path.insert(0, str(pathlib.Path.home() / "dev/asset-pipeline/tools/lora-training"))
        import ledger
        print("ledger", ledger.record(a.out, a.ledger, ledger.tags(a.ledger_tag)))


if __name__ == "__main__":
    main()
