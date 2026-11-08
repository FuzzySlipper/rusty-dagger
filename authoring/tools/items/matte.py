#!/usr/bin/env python3
"""Clean the cut-out of finished finals with a BiRefNet mask, as a last step once their art is settled.

The cut-out keys on the flat generation ground, which leaves that ground wherever it sits inside the classic silhouette,
such as the gap under an arm, and opens holes in art near its colour, such as grey skin. BiRefNet tells figure from ground
there, but it also drops what stands apart from a figure, such as a floating orb or a long staff. So its mask only decides
the ground-coloured parts: each final loses the ground the mask calls background and regains what its figure holds inside.
Everything else stays as it was, including anything edited by hand.

Each area says whether its holes fill. People fill: their cut-out opens holes in grey skin and hair. Flats only lose ground:
their cut-out already opens the gaps their source draws open, such as an hourglass's waist or a chain's links, so a filled
hole there would pack ground back in.

The first run keeps each final under gen-raw/AREA/pre-matte/, with the mask it was given, and every later run starts from
those, so reruns are safe. Each final's generation image comes from its study: the one its opaque pixels match.

    matte.py [--area people|flats] [KEY ...]      e.g. matte.py texture-184-record-015-frame-000; no key cleans the area
"""
import argparse
import glob
import io
import json
import os
import re
import shutil
import time
import urllib.request
import uuid
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from doll_preview import AUTHORING
from postprocess import blur

COMFY = os.environ.get("COMFY_URL", "http://192.168.1.24:8188")
# Each area's finals, its study images by archive, record and frame, its finals' names, whether its holes fill, and how far
# from the ground colour a leftover may stray. Flats paint some art in the ground's own grey, such as a potion's cloth, which
# sits a little further from it than leftover ground does, so they take a tighter match.
AREAS = {
    "people": dict(studies="people-*-bp-*/images/person-{archive}-{record}", key=r"texture-(\d+)-record-(\d+)-frame-(000)", fill=True, ground=0.05),
    "flats": dict(studies="flats-*-bp-*/images/flat-{archive}-{record}{frame}", key=r"texture-(\d+)-record-(\d+)-frame-(\d+)", fill=False, ground=0.025),
}
LO, HI = 0.035, 0.11   # the cut-out's distances from the ground: below LO is ground, above HI is art
PATCH = 40             # the fewest pixels of leftover ground; fewer are flecks in the art
INSET = 3              # pixels inside the mask's edge before a hole counts as held by the figure
RIM = 4                # pixels beside removed ground that get a soft edge, as the cut-out's


def generation(area, key, final):
    """The study image a final was cut from: of those made for its figure, the closest over its opaque pixels. A frame
    after the first has its own study folder, ending -fN."""
    archive, record, frame = (int(n) for n in re.fullmatch(AREAS[area]["key"], key).groups())
    folder = AREAS[area]["studies"].format(archive=archive, record=record, frame=f"-f{frame}" if frame else "")
    paths = glob.glob(str(AUTHORING / f"gen-raw/studies/{folder}/bp-broad-half.png"))
    if not paths:
        raise SystemExit(f"{key} has no study image.")
    solid = final[..., 3] > 0.99
    images = [np.array(Image.open(path).convert("RGB"), np.float32) / 255 for path in paths]
    return min(images, key=lambda image: np.abs(image - final[..., :3])[solid].mean())


def ask_mask(image):
    """BiRefNet's mask of an RGB image, 0 to 1, from ComfyUI."""
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{boundary}.png"\r\nContent-Type: image/png\r\n\r\n'
            ).encode() + buffer.getvalue() + f"\r\n--{boundary}--\r\n".encode()
    name = json.load(urllib.request.urlopen(urllib.request.Request(
        f"{COMFY}/upload/image", body, {"Content-Type": f"multipart/form-data; boundary={boundary}"})))["name"]
    graph = {"1": {"class_type": "LoadImage", "inputs": {"image": name}},
             "2": {"class_type": "LoadBackgroundRemovalModel", "inputs": {"bg_removal_name": "birefnet.safetensors"}},
             "3": {"class_type": "RemoveBackground", "inputs": {"bg_removal_model": ["2", 0], "image": ["1", 0]}},
             "4": {"class_type": "MaskToImage", "inputs": {"mask": ["3", 0]}},
             "5": {"class_type": "SaveImage", "inputs": {"images": ["4", 0], "filename_prefix": "matte"}}}
    prompt = json.load(urllib.request.urlopen(urllib.request.Request(
        f"{COMFY}/prompt", json.dumps({"prompt": graph}).encode(), {"Content-Type": "application/json"})))["prompt_id"]
    while True:
        status = json.load(urllib.request.urlopen(f"{COMFY}/history/{prompt}")).get(prompt)
        if status and status["status"]["status_str"] == "error":
            raise SystemExit(f"ComfyUI refused the mask: {status['status']}")
        if status and status["status"]["completed"]:
            break
        time.sleep(0.5)
    file = status["outputs"]["5"]["images"][0]
    view = f"{COMFY}/view?filename={file['filename']}&type={file['type']}&subfolder={file['subfolder']}"
    return Image.open(io.BytesIO(urllib.request.urlopen(view).read())).convert("L")


def cleaned(final, painted, mask, fill=True, ground_match=0.05):
    """A final, 0 to 1 RGBA, cleaned against the mask. `painted` is the final over its generation image. With `fill`,
    holes the figure holds take their painted colour back. A leftover is ground-coloured within `ground_match`."""
    border = np.concatenate([painted[:16].reshape(-1, 3), painted[-16:].reshape(-1, 3)])
    ground_colour = np.median(border, 0)
    distance = np.abs(painted - ground_colour).max(-1)
    labels, count = ndimage.label((mask < 0.5) & (distance < ground_match))
    sizes = ndimage.sum(np.ones_like(labels), labels, np.arange(1, count + 1))
    ground = np.isin(labels, 1 + np.flatnonzero(sizes >= PATCH))
    alpha, colour = final[..., 3].copy(), final[..., :3].copy()
    # A hole the figure holds takes its painted colour back, and ground taken out leaves a soft rim.
    held = ndimage.binary_erosion(mask > 0.75, iterations=INSET) & (alpha < 0.99) & fill
    alpha[held], colour[held] = 1, painted[held]
    taken = ground & (alpha > 0)
    near = ndimage.binary_dilation(taken, iterations=RIM)
    soft = np.clip(blur(np.where(ground, 0.0, np.clip((distance - LO) / (HI - LO), 0, 1)), 0.6), 0, 1)
    rim = near & ~held & (soft < alpha)
    alpha[rim] = soft[rim]
    unmixed = (painted - (1 - alpha[..., None]) * ground_colour) / np.maximum(alpha[..., None], 0.02)
    colour[rim] = np.clip(unmixed, 0, 1)[rim]
    return np.dstack([colour, np.where(ground, 0, alpha)])


def clean(area, key):
    finals = AUTHORING / "gen-raw" / area
    kept = finals / "pre-matte" / f"{key}.png"
    if not kept.exists():
        kept.parent.mkdir(exist_ok=True)
        shutil.copyfile(finals / f"{key}.png", kept)
    final = np.array(Image.open(kept).convert("RGBA"), np.float32) / 255
    alpha = final[..., 3:]
    painted = final[..., :3] * alpha + generation(area, key, final) * (1 - alpha)
    masked = kept.parent / f"{key}-mask.png"
    if not masked.exists():
        ask_mask(Image.fromarray((painted * 255 + 0.5).astype(np.uint8))).save(masked)
    result = cleaned(final, painted, np.array(Image.open(masked), np.float32) / 255, AREAS[area]["fill"], AREAS[area]["ground"])
    Image.fromarray((result * 255 + 0.5).astype(np.uint8), "RGBA").save(finals / f"{key}.png")
    print(key, f"opaque {(final[..., 3] > 0.5).mean():.1%} to {(result[..., 3] > 0.5).mean():.1%}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--area", choices=sorted(AREAS), default="people")
    parser.add_argument("keys", nargs="*", help="finals to clean; none cleans the whole area")
    args = parser.parse_args()
    for key in args.keys or sorted(path.stem for path in (AUTHORING / "gen-raw" / args.area).glob("texture-*.png")):
        clean(args.area, key)


if __name__ == "__main__":
    main()
