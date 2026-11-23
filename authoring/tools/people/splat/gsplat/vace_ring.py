#!/usr/bin/env python3
"""Repaint a depth-control ring with Wan 2.2 VACE-Fun A14B on a ComfyUI host.

  python3 vace_ring.py RING_DIR REFERENCE.png OUT_DIR [--comfy http://192.168.1.23:8188]
      [--steps 4] [--split 2] [--strength 1.0] [--seed 7] [--prompt ...] [--negative ...] [--two-phase]

RING_DIR comes from control_ring_render.py or train_loop_splat.py (control/NNN.png,
ring.json). The control can be depth or an RGB render to repaint. Pass - as
REFERENCE to repaint an RGB control with no reference image.
--masks names a RING subdirectory of masks (white = VACE generates freely there). The
control frames are packed into an mp4, uploaded with the reference image and
run through the high-noise then low-noise VACE experts with the Wan 2.2 T2V
4-step LoRAs. The returned frames are written to OUT_DIR/frames/NNN.png in
camera order, with the submitted graph and timing in OUT_DIR/run.json.
--two-phase runs the experts as separate prompts and unloads ComfyUI's models
between and after them, passing the half-denoised latent through the input
folder. A shared-memory host then holds one 14B expert at a time.
"""
import argparse, json, os, subprocess, time, urllib.parse, urllib.request, uuid

PROMPT = ('a giant brown rat crouched on four legs, coarse mottled brown and black fur, glowing red eyes, '
          'bared white teeth, long thin tail, the camera slowly circles it, plain black background, studio lighting')
NEGATIVE = 'blurry, low quality, deformed, extra limbs, extra tail, text, watermark, people, human, standing upright, background scenery'


def post(url, data=None, headers=None):
    req = urllib.request.Request(url, data=data, headers=headers or {})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read() or b'{}')


def upload(comfy, path, kind='image'):
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{os.path.basename(path)}"\r\n'
            f'Content-Type: application/octet-stream\r\n\r\n').encode() + open(path, 'rb').read() + \
        f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n--{boundary}--\r\n'.encode()
    return post(f'{comfy}/upload/{kind}', body, {'Content-Type': f'multipart/form-data; boundary={boundary}'})['name']


def graph(a, ring, control_name, ref_name, prefix, mask_name=None):
    W, H = ring['size']; n = ring['frames']
    lora_dir = 'Wan2.2-T2V-A14B-4steps-lora-rank64-Seko-V2.0'
    g = {
        '1': {'class_type': 'UNETLoader', 'inputs': {'unet_name': 'wan2.2_fun_vace_high_noise_14B_fp8_scaled.safetensors', 'weight_dtype': 'default'}},
        '2': {'class_type': 'UNETLoader', 'inputs': {'unet_name': 'wan2.2_fun_vace_low_noise_14B_fp8_scaled.safetensors', 'weight_dtype': 'default'}},
        '3': {'class_type': 'LoraLoaderModelOnly', 'inputs': {'model': ['1', 0], 'lora_name': f'{lora_dir}/high_noise_model.safetensors', 'strength_model': 1.0}},
        '4': {'class_type': 'LoraLoaderModelOnly', 'inputs': {'model': ['2', 0], 'lora_name': f'{lora_dir}/low_noise_model.safetensors', 'strength_model': 1.0}},
        '5': {'class_type': 'ModelSamplingSD3', 'inputs': {'model': ['3', 0], 'shift': 8.0}},
        '6': {'class_type': 'ModelSamplingSD3', 'inputs': {'model': ['4', 0], 'shift': 8.0}},
        '7': {'class_type': 'CLIPLoader', 'inputs': {'clip_name': 'umt5_xxl_fp8_e4m3fn_scaled.safetensors', 'type': 'wan'}},
        '8': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['7', 0], 'text': a.prompt}},
        '9': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['7', 0], 'text': a.negative}},
        '10': {'class_type': 'VAELoader', 'inputs': {'vae_name': 'wan_2.1_vae.safetensors'}},
        '11': {'class_type': 'LoadVideo', 'inputs': {'file': control_name}},
        '12': {'class_type': 'GetVideoComponents', 'inputs': {'video': ['11', 0]}},
        **({'13': {'class_type': 'LoadImage', 'inputs': {'image': ref_name}}} if ref_name else {}),
        **({'20': {'class_type': 'LoadVideo', 'inputs': {'file': mask_name}},
            '21': {'class_type': 'GetVideoComponents', 'inputs': {'video': ['20', 0]}},
            '22': {'class_type': 'ImageToMask', 'inputs': {'image': ['21', 0], 'channel': 'red'}}} if mask_name else {}),
        '14': {'class_type': 'WanVaceToVideo', 'inputs': {'positive': ['8', 0], 'negative': ['9', 0], 'vae': ['10', 0],
                                                          'width': W, 'height': H, 'length': n, 'batch_size': 1,
                                                          'strength': a.strength, 'control_video': ['12', 0],
                                                          **({'reference_image': ['13', 0]} if ref_name else {}),
                                                          **({'control_masks': ['22', 0]} if mask_name else {})}},
        '15': {'class_type': 'KSamplerAdvanced', 'inputs': {'model': ['5', 0], 'add_noise': 'enable', 'noise_seed': a.seed,
                                                            'steps': a.steps, 'cfg': 1.0, 'sampler_name': 'euler', 'scheduler': 'simple',
                                                            'positive': ['14', 0], 'negative': ['14', 1], 'latent_image': ['14', 2],
                                                            'start_at_step': 0, 'end_at_step': a.split, 'return_with_leftover_noise': 'enable'}},
        '16': {'class_type': 'KSamplerAdvanced', 'inputs': {'model': ['6', 0], 'add_noise': 'disable', 'noise_seed': a.seed,
                                                            'steps': a.steps, 'cfg': 1.0, 'sampler_name': 'euler', 'scheduler': 'simple',
                                                            'positive': ['14', 0], 'negative': ['14', 1], 'latent_image': ['15', 0],
                                                            'start_at_step': a.split, 'end_at_step': 10000, 'return_with_leftover_noise': 'disable'}},
        '17': {'class_type': 'TrimVideoLatent', 'inputs': {'samples': ['16', 0], 'trim_amount': ['14', 3]}},
        '18': {'class_type': 'VAEDecode', 'inputs': {'samples': ['17', 0], 'vae': ['10', 0]}},
        '19': {'class_type': 'SaveImage', 'inputs': {'images': ['18', 0], 'filename_prefix': prefix}},
    }
    return g


def run(comfy, g, tag):
    pid = post(f'{comfy}/prompt', json.dumps({'prompt': g, 'client_id': tag}).encode(), {'Content-Type': 'application/json'})['prompt_id']
    print('submitted', pid, flush=True)
    missing = 0   # fail fast when ComfyUI no longer knows the job (crash/restart): not in the history nor the queue three polls running
    while True:
        time.sleep(10)
        try:
            hist = post(f'{comfy}/history/{pid}')
            q = post(f'{comfy}/queue') if pid not in hist else None
        except (OSError, ValueError):   # not answering (restarting): keep polling
            continue
        if q is not None:
            if any(item[1] == pid for item in q['queue_running'] + q['queue_pending']):
                missing = 0
            else:
                missing += 1
                if missing >= 3:
                    raise SystemExit(f'job {pid} lost: in neither the history nor the queue (ComfyUI restarted?)')
            continue
        if pid in hist:
            h = hist[pid]
            if h.get('status', {}).get('status_str') == 'error':
                raise SystemExit(json.dumps(h['status'], indent=1)[:3000])
            if h.get('outputs'):
                return pid, h


def fetch(comfy, item):
    q = urllib.parse.urlencode({'filename': item['filename'], 'subfolder': item['subfolder'], 'type': item['type']})
    with urllib.request.urlopen(f'{comfy}/view?{q}', timeout=300) as r:
        return r.read()


def unload(comfy):
    post(f'{comfy}/free', json.dumps({'unload_models': True, 'free_memory': True}).encode(), {'Content-Type': 'application/json'})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ring'); ap.add_argument('reference'); ap.add_argument('out')
    ap.add_argument('--comfy', default='http://192.168.1.23:8188')
    ap.add_argument('--steps', type=int, default=4); ap.add_argument('--split', type=int, default=2)
    ap.add_argument('--strength', type=float, default=1.0); ap.add_argument('--seed', type=int, default=7)
    ap.add_argument('--prompt', default=PROMPT); ap.add_argument('--negative', default=NEGATIVE)
    ap.add_argument('--masks', help='RING subdirectory of NNN.png masks: white = generate freely, black = follow control')
    ap.add_argument('--two-phase', action='store_true', help='one expert per prompt, unloading models between and after')
    a = ap.parse_args()
    ring = json.load(open(f'{a.ring}/ring.json'))
    os.makedirs(f'{a.out}/frames', exist_ok=True)
    tag = f'rat-vace-{uuid.uuid4().hex[:8]}'
    video = f'{a.out}/{tag}-control.mp4'
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '16', '-i', f'{a.ring}/control/%03d.png',
                    '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '12', video], check=True)
    control_name = upload(a.comfy, video)
    mask_name = None
    if a.masks:
        mvideo = f'{a.out}/{tag}-masks.mp4'
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '16', '-i', f'{a.ring}/{a.masks}/%03d.png',
                        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '0', mvideo], check=True)
        mask_name = upload(a.comfy, mvideo)
    ref_name = None if a.reference == '-' else upload(a.comfy, a.reference)
    g = graph(a, ring, control_name, ref_name, tag, mask_name)
    t0 = time.time()
    if a.two_phase:
        high = {k: v for k, v in g.items() if k not in ('2', '4', '6', '16', '17', '18', '19')}
        high['30'] = {'class_type': 'SaveLatent', 'inputs': {'samples': ['15', 0], 'filename_prefix': f'latents/{tag}'}}
        first, h = run(a.comfy, high, tag)
        latent = f'{a.out}/{tag}.latent'
        open(latent, 'wb').write(fetch(a.comfy, h['outputs']['30']['latents'][0]))
        unload(a.comfy)
        low = {k: v for k, v in g.items() if k not in ('1', '3', '5', '15')}
        low['31'] = {'class_type': 'LoadLatent', 'inputs': {'latent': upload(a.comfy, latent)}}
        low['16']['inputs']['latent_image'] = ['31', 0]
        pid, h = run(a.comfy, low, tag)
        unload(a.comfy)
        g, pid = {'high': high, 'low': low}, [first, pid]
    else:
        pid, h = run(a.comfy, g, tag)
    seconds = round(time.time() - t0, 1)
    images = h['outputs']['19']['images']
    for i, im in enumerate(images):
        open(f'{a.out}/frames/{i:03d}.png', 'wb').write(fetch(a.comfy, im))
    json.dump({'prompt_id': pid, 'seconds': seconds, 'frames': len(images), 'args': vars(a), 'ring': ring, 'graph': g},
              open(f'{a.out}/run.json', 'w'), indent=1)
    print('done', len(images), 'frames in', seconds, 's', flush=True)


if __name__ == '__main__':
    main()
