#!/usr/bin/env python3
"""birefnet_masks.py FRAMES_DIR OUT_DIR [--comfy URL] [--first N --last M]: a BiRefNet figure mask per frame (OUT_DIR/NNN.png, 0-255),
from the Strix Halos' ComfyUI (den-m5 serves birefnet.safetensors; about a second a frame; --comfy URL[,URL] spreads the frames
over both Halos, which run identical installs). All of a clip's frames are queued at once, then collected.

The request is Rusty Dagger's (authoring/tools/shared/matte.py, ask_mask(); task 10667): LoadImage -> LoadBackgroundRemovalModel
(birefnet.safetensors) -> RemoveBackground -> MaskToImage -> SaveImage, copied here, not imported, so this repository stays
independently runnable. orbit_views.py --birefnet reads the masks. A frame whose mask already exists is skipped (reruns are cheap).
Jobs go into the shared queue in order; nothing is withdrawn."""
import argparse, io, json, os, sys, time, urllib.request, uuid
from pathlib import Path
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument('frames', type=Path); ap.add_argument('out', type=Path)
ap.add_argument('--comfy', default=os.environ.get('COMFY_URL', 'http://192.168.1.24:8188'))
ap.add_argument('--first', type=int, default=0); ap.add_argument('--last', type=int, default=10 ** 6); a = ap.parse_args()


def _open(req, tries=3):   # every ComfyUI call times out and retries: a dropped connection hung the 386-C mattes for 40 min (2026-11-16)
    for k in range(tries):
        try:
            return urllib.request.urlopen(req, timeout=60)
        except Exception:
            if k == tries - 1:
                raise
            time.sleep(5)

def submit(C, image):
    buf = io.BytesIO(); image.save(buf, 'PNG'); b = uuid.uuid4().hex
    body = (f'--{b}\r\nContent-Disposition: form-data; name="image"; filename="{b}.png"\r\nContent-Type: image/png\r\n\r\n').encode() + buf.getvalue() + f'\r\n--{b}--\r\n'.encode()
    name = json.load(_open(urllib.request.Request(f'{C}/upload/image', body, {'Content-Type': f'multipart/form-data; boundary={b}'})))['name']
    graph = {'1': {'class_type': 'LoadImage', 'inputs': {'image': name}},
             '2': {'class_type': 'LoadBackgroundRemovalModel', 'inputs': {'bg_removal_name': 'birefnet.safetensors'}},
             '3': {'class_type': 'RemoveBackground', 'inputs': {'bg_removal_model': ['2', 0], 'image': ['1', 0]}},
             '4': {'class_type': 'MaskToImage', 'inputs': {'mask': ['3', 0]}},
             '5': {'class_type': 'SaveImage', 'inputs': {'images': ['4', 0], 'filename_prefix': 'orbit-matte'}}}
    return json.load(_open(urllib.request.Request(f'{C}/prompt', json.dumps({'prompt': graph}).encode(), {'Content-Type': 'application/json'})))['prompt_id']

def fetch(C, pid):
    st = json.load(_open(f'{C}/history/{pid}')).get(pid)
    if st and st['status']['status_str'] == 'error': raise SystemExit(f'ComfyUI refused the mask: {st["status"]}')
    if not (st and st['status']['completed']): return None
    f = st['outputs']['5']['images'][0]
    return Image.open(io.BytesIO(_open(f"{C}/view?filename={f['filename']}&type={f['type']}&subfolder={f['subfolder']}").read())).convert('L')

files = sorted(a.frames.glob('*.png')); a.out.mkdir(parents=True, exist_ok=True); t0 = time.time()
urls = [u.rstrip('/') for u in a.comfy.split(',')]; jobs = {}
for k, i in enumerate(i for i in range(len(files)) if a.first <= i <= a.last and not (a.out / f'{i:03d}.png').exists()):
    C = urls[k % len(urls)]; jobs[i] = (C, submit(C, Image.open(files[i]).convert('RGB')))   # all queued at once, in order
n = 0
while jobs:
    for i, (C, pid) in list(jobs.items()):
        m = fetch(C, pid)
        if m is None: continue
        size = Image.open(files[i]).size
        (m if m.size == size else m.resize(size, Image.BILINEAR)).save(a.out / f'{i:03d}.png'); del jobs[i]; n += 1
    if jobs: time.sleep(1)
print(json.dumps({'masks': n, 'seconds': round(time.time() - t0, 1), 'hosts': urls, 'out': str(a.out)}))
