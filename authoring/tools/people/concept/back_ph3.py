#!/usr/bin/env python3
"""Generate matching back-view turnaround candidates from an approved front painting using MiniMax H3 Image (p-h3-image_w4a8).

Single-stage (~4.6s warm on RTX 5090), preserves exact inked gouache style, true 3D object permanence
(rotated hood tip, front-side pouches peeking only at the edges), and anatomical 5-finger hands in the
canonical 22-degree curled-hand rest pose (or legacy 30-degree open-palm wide-A via --pose wide-a).
"""
import argparse
import json
import time
import urllib.request
import uuid
from pathlib import Path
from PIL import Image, ImageDraw

DEFAULT_PROMPT_BASE = (
    "Edit <Picture 1> to show the exact same character viewed straight from behind, turned 180 degrees away from the viewer. "
    "Keep the exact same standing pose, scale, framing, flat slate-grey background, lighting, and inked gouache painting style. "
    "{extra}"
    "{hands}"
)
HANDS_SENTENCE_REST = (
    "Both hanging hands match the exact reverse of <Picture 1>'s relaxed pose—palms facing inward toward the thighs, "
    "fingers together and gently curled forward/inward, and thumbs tucked toward the front away from the viewer."
)
HANDS_SENTENCE_WIDE_A = (
    "Both hands match the reverse of <Picture 1>'s relaxed open pose, showing the smooth backs of both hands "
    "(palms facing away) with five clear, naturally proportioned fingers and the thumb along the outer upper edge on each hand."
)
HAND_CROPS = {
    "rest": ((0.14, 0.45, 0.36, 0.62), (0.64, 0.45, 0.86, 0.62)),
    "wide-a": ((0.01, 0.42, 0.24, 0.59), (0.76, 0.42, 0.99, 0.59)),
}


def comfy_upload(comfy: str, path: Path, remote_name: str) -> str:
    boundary = "----BackPH3Upload7MA4YWxkTrZu0gW"
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="image"; filename="{remote_name}"\r\n'
        f"Content-Type: image/png\r\n\r\n"
    ).encode() + path.read_bytes() + (
        f"\r\n--{boundary}\r\n"
        f'Content-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n'
        f"--{boundary}--\r\n"
    ).encode()
    req = urllib.request.Request(
        f"{comfy.rstrip('/')}/upload/image", data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    return json.loads(urllib.request.urlopen(req, timeout=120).read())["name"]


def submit_and_fetch(comfy: str, graph: dict, save_node_id: str) -> bytes:
    req = urllib.request.Request(
        f"{comfy.rstrip('/')}/prompt",
        json.dumps({"prompt": graph, "client_id": uuid.uuid4().hex}).encode(),
        {"Content-Type": "application/json"},
    )
    pid = json.loads(urllib.request.urlopen(req, timeout=120).read())["prompt_id"]
    while True:
        hist = json.loads(urllib.request.urlopen(f"{comfy.rstrip('/')}/history/{pid}", timeout=120).read())
        if pid in hist:
            break
        time.sleep(1.5)
    st = hist[pid]["status"]
    if st["status_str"] != "success":
        raise SystemExit(f"ComfyUI failed: {st}")
    im_meta = hist[pid]["outputs"][save_node_id]["images"][0]
    fn, sub, typ = im_meta["filename"], im_meta["subfolder"], im_meta["type"]
    return urllib.request.urlopen(f"{comfy.rstrip('/')}/view?filename={fn}&subfolder={sub}&type={typ}", timeout=120).read()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--front", required=True, type=Path, help="Approved front painting (e.g. 832x1216)")
    ap.add_argument("--out", required=True, type=Path, help="Output directory for back candidates")
    ap.add_argument("--extra", default="", help="Optional 1-sentence back costume note (e.g. 'The short teal hood covers the back of his head with no cape.')")
    ap.add_argument("--pose", choices=["rest", "wide-a"], default="rest", help="Front pose style: rest (canonical 22-deg curled palm-inward pose-lock; default) or wide-a (legacy 30-deg open-palm)")
    ap.add_argument("--omit-hands", action="store_true", help="Omit the default hands sentence (for cloaked/static poses or when specifying custom hand wording in --extra)")
    ap.add_argument("--seeds", default="612001,612002,612003,612004,612005,612006")
    ap.add_argument("--width", type=int, default=832)
    ap.add_argument("--height", type=int, default=1216)
    ap.add_argument("--comfy", default="http://192.168.1.20:8188")
    args = ap.parse_args()

    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    extra = (args.extra.strip() + " ") if args.extra.strip() else ""
    hands_default = HANDS_SENTENCE_REST if args.pose == "rest" else HANDS_SENTENCE_WIDE_A
    hands = "" if args.omit_hands else hands_default
    prompt = DEFAULT_PROMPT_BASE.format(extra=extra, hands=hands).strip()
    (out / "back.prompt.txt").write_text(prompt + "\n")

    up_front = comfy_upload(args.comfy, args.front.resolve(), f"back-ph3-{out.name}-front.png")
    tpl = {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "p-h3-image_w4a8.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "p-h3-image_text_encoder_w4a8.safetensors", "type": "minimax", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "p-h3-image_vae_int8_convrot.safetensors"}},
        "13": {"class_type": "LoadImage", "inputs": {"image": up_front}},
        "4": {
            "class_type": "PH3ImageConditioning",
            "inputs": {
                "clip": ["2", 0], "vae": ["3", 0], "prompt": prompt,
                "width": args.width, "height": args.height, "images.image_1": ["13", 0],
            },
        },
        "5": {"class_type": "MiniMaxH3SigmaShift", "inputs": {"model": ["1", 0], "shift_video": 1.0, "shift_audio": 1.0}},
        "6": {"class_type": "BasicGuider", "inputs": {"model": ["5", 0], "conditioning": ["4", 0]}},
        "7": {"class_type": "ManualSigmas", "inputs": {"sigmas": "1.0, 0.984753, 0.967905, 0.951688, 0.925372, 0.881923, 0.810083, 0.528718, 0.0"}},
        "8": {"class_type": "KSamplerSelect", "inputs": {"sampler_name": "euler"}},
        "9": {"class_type": "RandomNoise", "inputs": {"noise_seed": 612001}},
        "10": {"class_type": "SamplerCustomAdvanced", "inputs": {"noise": ["9", 0], "guider": ["6", 0], "sampler": ["8", 0], "sigmas": ["7", 0], "latent_image": ["4", 1]}},
        "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["3", 0]}},
        "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": f"concept/back-ph3-{out.name}"}},
    }

    seeds = [int(x) for x in args.seeds.split(",") if x.strip()]
    backs = []
    for i, s in enumerate(seeds, 1):
        g = json.loads(json.dumps(tpl))
        g["9"]["inputs"]["noise_seed"] = s
        g["12"]["inputs"]["filename_prefix"] = f"concept/back-ph3-{out.name}-s{i}"
        t0 = time.monotonic()
        p = out / f"back-s{i}.png"
        p.write_bytes(submit_and_fetch(args.comfy, g, "12"))
        (out / f"back-s{i}.png.graph.json").write_text(json.dumps(g, indent=1) + "\n")
        backs.append((f"s{i} ({s})", p))
        print(f"[back-ph3 s{i}] seed={s} {time.monotonic() - t0:.1f}s -> {p}", flush=True)

    # Contact sheet with zoomed left/right hand crops
    cols = min(3, len(backs))
    rows = (len(backs) + cols - 1) // cols
    tw, th, ch = 360, 526, 195
    cell_h = 24 + th + ch
    sheet = Image.new("RGB", (tw * cols, cell_h * rows), (18, 20, 24))
    d = ImageDraw.Draw(sheet)
    for i, (tag, p) in enumerate(backs):
        r_i, c_i = divmod(i, cols)
        x0, y0 = c_i * tw, r_i * cell_h
        im = Image.open(p).convert("RGB")
        sheet.paste(im.resize((tw, th), Image.LANCZOS), (x0, y0 + 24))
        w_im, h_im = im.size
        lbox, rbox = HAND_CROPS[args.pose]
        lh = im.crop((int(w_im * lbox[0]), int(h_im * lbox[1]), int(w_im * lbox[2]), int(h_im * lbox[3]))).resize((tw // 2, ch), Image.LANCZOS)
        rh = im.crop((int(w_im * rbox[0]), int(h_im * rbox[1]), int(w_im * rbox[2]), int(h_im * rbox[3]))).resize((tw // 2, ch), Image.LANCZOS)
        sheet.paste(lh, (x0, y0 + 24 + th))
        sheet.paste(rh, (x0 + tw // 2, y0 + 24 + th))
        d.text((x0 + 8, y0 + 5), f"back-ph3 | {tag}", fill=(245, 235, 195))
    sheet.save(out / "back_sheet.jpg", quality=94)
    print("Wrote", out / "back_sheet.jpg", flush=True)


if __name__ == "__main__":
    main()
