#!/usr/bin/env python3
"""ref2va_orbit.py OUT_DIR --video MOTION.mp4 --image FRONT.png --image BACK.png --prompt-file P.txt [--size 1088] [--frames 73]
    [--recipe stock|fasth3-omni|hyperflow-flash] [--steps N] [--seed N] [--guide IMAGE:FRAME ...] [--deadline 14400]

H3 REF2VA (minimax_h3_ref2va_pruned_int8_convrot, the graph of tools/h3-df-sprite-walk/run.py: euler, linear_quadratic, stock steps,
no LoRA) with the paintings as reference images (<Picture 1>, <Picture 2>...) and one reference video (<Video 1>) for camera and
timing. --guide adds MiniMaxH3AddGuide frames on REF2VA's conditioning and latent (as FL2VA's back:36), e.g. FRONT:0, BACK:36,
FRONT:72; whether the model honours them is part of the test. Uploads to COMFY_URL (default the GPU lab), submits one job, waits for it
in the queue (never withdraws; DEADLINE seconds then exits non-zero leaving the job queued), saves OUT_DIR/frames/frame_NNNNN.png,
workflow-api.json, run.json. Run under the lab lock and asset-resources run."""
import argparse, hashlib, json, os, time, urllib.parse, urllib.request, uuid
from pathlib import Path

COMFY = os.environ.get('COMFY_URL', 'http://192.168.1.20:8188')
RECIPES = {
    'stock': ('minimax_h3_ref2va_pruned_int8_convrot.safetensors', 25),
    'fasth3-omni': ('fasth3_omniref_8step_pruned_int8_convrot.safetensors', 8),
    'hyperflow-flash': ('minimax_h3_ref2va_hyperflow_flash_14b-int8_convrot.safetensors', 6),
}
FUN_CONTROL = 'minimax_h3_fun_controlnet_union_pruned_int8_convrot.safetensors'
HYPERFLOW_FUN_CONTROL = 'minimax_h3_fun_controlnet_union_hyperflow_flash_ref2va.safetensors'


def upload(path, name):
    b = '----r2v-' + uuid.uuid4().hex
    body = (f'--{b}\r\nContent-Disposition: form-data; name="image"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode() \
        + Path(path).read_bytes() + f'\r\n--{b}\r\nContent-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n--{b}--\r\n'.encode()
    req = urllib.request.Request(COMFY + '/upload/image', data=body); req.add_header('Content-Type', f'multipart/form-data; boundary={b}')
    info = json.loads(urllib.request.urlopen(req, timeout=120).read())
    return '/'.join(x for x in [info.get('subfolder', ''), info['name']] if x)


def call(path, data=None):
    req = urllib.request.Request(COMFY + path, data=data, headers={'Content-Type': 'application/json'} if data else {})
    return json.loads(urllib.request.urlopen(req, timeout=120).read())


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('out', type=Path); p.add_argument('--video', required=True); p.add_argument('--image', action='append', required=True)
    p.add_argument('--prompt-file', required=True); p.add_argument('--size', type=int, default=1088); p.add_argument('--frames', type=int, default=73)
    p.add_argument('--recipe', choices=RECIPES, default='stock',
                   help='Use the model and sampling setup for this REF2VA checkpoint')
    p.add_argument('--steps', type=int, help='Override the recipe step count')
    p.add_argument('--seed', type=int, default=610800110)
    p.add_argument('--guide', action='append', default=[]); p.add_argument('--deadline', type=int, default=14400)
    p.add_argument('--ref-size', type=int, help='scale each reference picture to this square size first (the node only scales down, to the '
                   'generation area: 1920 px refs cost about 14k tokens each at every step; Patch 2026-11-25: CMS views are 832x1216 anyway)')
    p.add_argument('--control', action='append', default=[], help='KIND:VIDEO:STRENGTH[:START:END], H3 Fun ControlNet Union, chained')
    p.add_argument('--lora'); p.add_argument('--lora-strength', type=float, default=1.0)
    p.add_argument('--clip-device', default='default', choices=['default', 'cpu'], help='CLIPLoader device: cpu keeps the 15 GB text encoder off the card')
    p.add_argument('--free-after-encode', action='store_true', help='VRAM_Debug (kjnodes) after the reference conditioning: unload all models before sampling')
    p.add_argument('--cond-save', help='H3CondSave (tools/comfy-nodes/h3-cond-cache) the reference conditioning as NAME after encoding')
    p.add_argument('--cond-load', help='H3CondLoad NAME instead of encoding: no text encoder, no reference node; latent from EmptyMiniMaxH3LatentAV')
    p.add_argument('--sampler', default='euler'); p.add_argument('--scheduler')
    p.add_argument('--sigmas', type=lambda s: [float(x) for x in s.split(',')],
                   help='Comma-separated ManualSigmas values for the stock and FastH3 recipes')
    a = p.parse_args(); a.out.mkdir(parents=True, exist_ok=True)
    unet, default_steps = RECIPES[a.recipe]
    steps = a.steps if a.steps is not None else default_steps
    models = {
        'unet': os.environ.get('H3_UNET', unet),
        # NVFP4 needs Blackwell. Strix Halo uses the INT8 ConvRot encoder.
        'clip': os.environ.get('H3_CLIP', 'qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors'),
        'vae': 'minimax_h3_video_vae_fp16.safetensors',
    }
    h = lambda f: hashlib.sha256(Path(f).read_bytes()).hexdigest()[:12]
    prompt = Path(a.prompt_file).read_text().strip()
    if a.ref_size:   # smaller reference tokens; the guide frames (--guide) stay at the generation size
        from PIL import Image
        small = []
        for f in a.image:
            t = a.out / f'ref-{a.ref_size}-{Path(f).name}'
            Image.open(f).convert('RGB').resize((a.ref_size, a.ref_size), Image.LANCZOS).save(t); small.append(str(t))
        a.image = small
    imgs = [upload(f, f'r2v-ref-{h(f)}.png') for f in a.image]
    vid = upload(a.video, f'r2v-motion-{h(a.video)}.mp4')
    enc = {'clip': ['2', 0], 'vae': ['3', 0], 'prompt': prompt, 'width': a.size, 'height': a.size, 'length': a.frames,
           'ref_image_size': 'match', 'ref_videos.ref_video_0': ['22', 0]}
    g = {'1': {'class_type': 'UNETLoader', 'inputs': {'unet_name': models['unet'], 'weight_dtype': 'default'}},
         '2': {'class_type': 'CLIPLoader', 'inputs': {'clip_name': models['clip'], 'type': 'minimax', 'device': a.clip_device}},
         '3': {'class_type': 'VAELoader', 'inputs': {'vae_name': models['vae']}},
         '21': {'class_type': 'LoadVideo', 'inputs': {'file': vid}},
         '22': {'class_type': 'GetVideoComponents', 'inputs': {'video': ['21', 0]}},
         '20': {'class_type': 'MiniMaxH3ReferenceToVideo', 'inputs': enc}}
    for i, name in enumerate(imgs):
        g[str(10 + i)] = {'class_type': 'LoadImage', 'inputs': {'image': name}}; enc[f'ref_images.ref_image_{i}'] = [str(10 + i), 0]
    pos = ['20', 0]; guides = []; lat = ['20', 1]
    if a.cond_save:
        g['24'] = {'class_type': 'H3CondSave', 'inputs': {'conditioning': ['20', 0], 'name': a.cond_save}}; pos = ['24', 0]
    if a.cond_load:   # the cached conditioning; the reference node, the text encoder and the reference uploads drop out of the graph
        for k in ['2', '20', '21', '22'] + [k for k in list(g) if g[k]['class_type'] == 'LoadImage']:
            g.pop(k, None)
        g['24'] = {'class_type': 'H3CondLoad', 'inputs': {'name': a.cond_load}}
        g['26'] = {'class_type': 'EmptyMiniMaxH3LatentAV', 'inputs': {'width': a.size, 'height': a.size, 'length': a.frames}}
        pos = ['24', 0]; lat = ['26', 0]
    if a.free_after_encode:   # the text encoder's staged weights out before the video model loads (the load spike, lead 2026-11-14)
        g['25'] = {'class_type': 'VRAM_Debug', 'inputs': {'empty_cache': True, 'gc_collect': True, 'unload_all_models': True, 'any_input': pos}}
        pos = ['25', 0]
    for k, spec in enumerate(a.guide):
        img, frame = spec.rsplit(':', 1); name = upload(img, f'r2v-guide-{h(img)}.png'); node = str(60 + k)
        g[str(40 + k)] = {'class_type': 'LoadImage', 'inputs': {'image': name}}
        g[node] = {'class_type': 'MiniMaxH3AddGuide', 'inputs': {'positive': pos, 'latent': lat, 'vae': ['3', 0],
                                                                  'image': [str(40 + k), 0], 'frame_idx': int(frame)}}
        pos = [node, 0]; guides.append([img, int(frame)])
    model = ['1', 0]; controls = []
    if a.lora:
        g['4'] = {'class_type': 'LoraLoaderModelOnly', 'inputs': {'model': model, 'lora_name': a.lora, 'strength_model': a.lora_strength}}
        model = ['4', 0]
    if a.recipe == 'hyperflow-flash':
        g['1'] = {'class_type': 'HyperFlowLoader', 'inputs': {'unet_name': models['unet']}}
    if a.recipe == 'fasth3-omni':
        g['sigma_shift'] = {'class_type': 'MiniMaxH3SigmaShift',
                            'inputs': {'model': model, 'shift_video': 12.0, 'shift_audio': 3.0}}
        g['dense_attention'] = {'class_type': 'ModelAttentionBackend',
                                 'inputs': {'model': ['sigma_shift', 0], 'attention': 'comfy kitchen attention'}}
        g['sparse_attention'] = {'class_type': 'BlockSparseAttention', 'inputs': {
            'model': ['dense_attention', 0], 'selection': 'vsa', 'selection.keep_percent': 10.0,
            'start_percent': 0.0, 'end_percent': 1.0, 'dense_blocks': '', 'min_tokens': 12288,
            'extra_tokens': 256, 'sink_conditioning': 'exact_kv_and_rows', 'verbose': True,
        }}
        # Fun ControlNet wraps the existing block patches, so apply it after VSA.
        model = ['sparse_attention', 0]
    if a.control:
        hyperflow_control = a.recipe == 'hyperflow-flash'
        control_model = HYPERFLOW_FUN_CONTROL if hyperflow_control else FUN_CONTROL
        g['60p'] = {'class_type': 'ModelPatchLoader', 'inputs': {'name': control_model}}
        for k, spec in enumerate(a.control):
            kind, path, st, *rest = spec.split(':')
            up = upload(path, f'r2v-control-{kind}-{h(path)}.mp4')
            g[f'7{k}c'] = {'class_type': 'LoadVideo', 'inputs': {'file': up}}
            g[f'8{k}c'] = {'class_type': 'GetVideoComponents', 'inputs': {'video': [f'7{k}c', 0]}}
            control_inputs = {'model': model, 'model_patch': ['60p', 0], 'vae': ['3', 0],
                              'strength': float(st), 'start_percent': float(rest[0]) if rest else 0.0,
                              'end_percent': float(rest[1]) if len(rest) > 1 else 1.0,
                              'control_video': [f'8{k}c', 0]}
            control_node = 'MiniMaxH3FunControlNetApply'
            if hyperflow_control:
                control_node = 'HyperFlowH3FunControlNetApply'
                control_inputs['control_basis_model'] = RECIPES['stock'][0]
            g[f'9{k}c'] = {'class_type': control_node, 'inputs': control_inputs}
            model = [f'9{k}c', 0]; controls.append(spec)
    scheduler = a.scheduler or ('simple' if a.recipe == 'fasth3-omni' else 'linear_quadratic')
    g.update({'30': {'class_type': 'KSamplerSelect', 'inputs': {'sampler_name': a.sampler}},
              '31': {'class_type': 'BasicScheduler', 'inputs': {'model': model, 'scheduler': scheduler, 'steps': steps, 'denoise': 1.0}},
              '32': {'class_type': 'BasicGuider', 'inputs': {'model': model, 'conditioning': pos}},
              '33': {'class_type': 'RandomNoise', 'inputs': {'noise_seed': a.seed}},
              '34': {'class_type': 'SamplerCustomAdvanced', 'inputs': {'noise': ['33', 0], 'guider': ['32', 0], 'sampler': ['30', 0], 'sigmas': ['31', 0], 'latent_image': lat}},
              '35': {'class_type': 'VAEDecode', 'inputs': {'samples': ['34', 0], 'vae': ['3', 0]}},
              '36': {'class_type': 'SaveImage', 'inputs': {'images': ['35', 0], 'filename_prefix': f'r2v-orbit/{a.out.name}/frame'}}})
    if a.recipe == 'hyperflow-flash':
        scheduler = 'HyperFlowSigmas'
        g['31'] = {'class_type': scheduler, 'inputs': {'model': ['1', 0], 'steps': steps}}
    elif a.sigmas:
        scheduler = 'manual'
        steps = len(a.sigmas) - 1
        g['31'] = {'class_type': 'ManualSigmas', 'inputs': {'sigmas': ', '.join(map(str, a.sigmas))}}
    (a.out / 'workflow-api.json').write_text(json.dumps(g, indent=1))
    t0 = time.time(); res = call('/prompt', json.dumps({'prompt': g, 'client_id': 'r2v-orbit'}).encode())
    pid = res.get('prompt_id')
    if res.get('node_errors') or not pid:
        (a.out / 'node_errors.json').write_text(json.dumps(res, indent=1)); raise SystemExit(f'node_errors: {json.dumps(res)[:1500]}')
    missing = 0   # fail fast when ComfyUI no longer knows the job (crash/restart)
    while True:
        try:
            hist = call(f'/history/{pid}').get(pid)
            q = None if hist else call('/queue')
        except (OSError, ValueError):
            time.sleep(10); continue
        if hist and hist.get('status', {}).get('completed') is not None:
            break
        if q is not None:
            if any(item[1] == pid for item in q['queue_running'] + q['queue_pending']):
                missing = 0
            else:
                missing += 1
                if missing >= 3:
                    raise SystemExit(f'job {pid} lost: in neither the history nor the queue (ComfyUI restarted?)')
        if time.time() - t0 > a.deadline:
            raise SystemExit(f'deadline: job {pid} still queued/running (not withdrawn)')
        time.sleep(10)
    st = hist['status']
    if st.get('status_str') != 'success':
        (a.out / 'error.json').write_text(json.dumps(st, indent=1)); raise SystemExit(f'failed: {json.dumps(st)[:1500]}')
    (a.out / 'frames').mkdir(exist_ok=True); n = 0
    for node, out in hist['outputs'].items():
        if node != '36':   # only the SaveImage node's frames (LoadVideo previews are outputs too)
            continue
        for im in out.get('images', []):
            q = urllib.parse.urlencode({'filename': im['filename'], 'subfolder': im['subfolder'], 'type': im['type']})
            (a.out / 'frames' / im['filename']).write_bytes(urllib.request.urlopen(f'{COMFY}/view?{q}', timeout=120).read()); n += 1
    run = {'status': 'success', 'prompt_id': pid, 'seconds': round(time.time() - t0, 1), 'frames': n, 'size': a.size,
           'steps': steps,
           'seed': a.seed, 'models': models, 'lora': a.lora, 'control': controls, 'sampler': [a.sampler, scheduler], 'video': a.video, 'images': a.image, 'clip_device': a.clip_device, 'cond_save': a.cond_save, 'cond_load': a.cond_load, 'free_after_encode': a.free_after_encode, 'guides': guides, 'prompt': prompt, 'comfy': COMFY}
    if a.recipe != 'stock':
        run['recipe'] = a.recipe
    if scheduler == 'manual':
        run['sigmas'] = a.sigmas
    (a.out / 'run.json').write_text(json.dumps(run, indent=1)); print(json.dumps({k: run[k] for k in ('status', 'seconds', 'frames')}))


if __name__ == '__main__':
    main()
