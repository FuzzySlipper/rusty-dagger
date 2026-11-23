"""Meshy multi-image-to-3D from a pack's views: submit, poll, download the GLB, textures and the task record.

  python3 authoring/tools/people/splat/mesh/meshy_multiview.py OUT_DIR FRONT.png [BACK.png LEFT.png RIGHT.png] \
      [--model latest] [--pose t-pose] [--remesh quad|triangle --polycount 10000] [--texture 2k] [--pbr] \
      [--no-enhance] [--texture-prompt TEXT] [--no-texture]

MESHY_TOKEN comes from the environment or the repository's git-ignored `.api.env`. Images go up as data URIs
(up to four; with Meshy 7.1 the first is the front, the rest in any order). Without --remesh the model's own
mesh is kept (Meshy's advice for the highest quality); --remesh asks Meshy for a quad-dominant or triangle
mesh near --polycount, and also keeps the pre-remesh GLB. Writes OUT_DIR/model.glb, the base-colour (and PBR)
textures, front/right/back/left thumbnails and task.json (the final task object, including
consumed_credits). Failed tasks are refunded by Meshy. API assets expire after a few days, so everything is
downloaded at once.
"""
import base64
import json
import os
import sys
import time
import urllib.request, urllib.error
from pathlib import Path

API = 'https://api.meshy.ai/openapi/v1/multi-image-to-3d'
a = sys.argv[1:]


def opt(name, default):
    return type(default)(a[a.index(name) + 1]) if name in a else default


def token():
    if os.environ.get('MESHY_TOKEN'):
        return os.environ['MESHY_TOKEN']
    env = Path(os.environ.get('API_ENV') or Path.home() / 'dev/asset-pipeline/.api.env')   # MESHY_TOKEN, kept in asset-pipeline's git-ignored .api.env
    for line in env.read_text().splitlines():
        if line.startswith('MESHY_TOKEN='):
            return line.split('=', 1)[1].strip().strip('"\'')
    sys.exit('MESHY_TOKEN not set')


def call(url, body=None):
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body else None, method='POST' if body else 'GET',
                                 headers={'Authorization': f'Bearer {token()}', 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:   # Meshy's reason is in the body (395's clays were refused with 403, 2026-11-19)
        sys.exit(f'meshy {e.code} {e.reason}: {e.read()[:500].decode(errors="replace")}')


def fetch(url, path):
    with urllib.request.urlopen(url, timeout=600) as r:
        path.write_bytes(r.read())


if not a or '-h' in a or '--help' in a:
    sys.exit(__doc__)
out = Path(a[0])
images = [x for x in a[1:] if x.lower().endswith(('.png', '.jpg', '.jpeg', '.webp')) and not x.startswith('--')][:4]
if not images:
    sys.exit('no image paths given\n' + __doc__)
out.mkdir(parents=True, exist_ok=True)
body = {'image_urls': [f'data:image/{Path(p).suffix[1:].replace("jpg", "jpeg")};base64,' + base64.b64encode(Path(p).read_bytes()).decode()
                       for p in images],
        'ai_model': opt('--model', 'latest'), 'pose_mode': opt('--pose', 't-pose'),
        'texture_resolution': opt('--texture', '2k'), 'enable_pbr': '--pbr' in a,
        'should_remesh': '--remesh' in a, 'multi_view_thumbnails': True, 'target_formats': ['glb', 'fbx']}
if '--no-texture' in a:
    body['should_texture'] = False
if '--remesh' in a:
    body.update(topology=opt('--remesh', 'quad'), target_polycount=opt('--polycount', 10000), save_pre_remeshed_model=True)
if '--no-enhance' in a:
    body['image_enhancement'] = False
if '--texture-prompt' in a:
    body['texture_prompt'] = opt('--texture-prompt', '')
(out / 'request.json').write_text(json.dumps({k: (f'{len(v)} data URIs' if k == 'image_urls' else v) for k, v in body.items()}
                                             | {'images': images}, indent=1))
tid = call(API, body)['result']
print('meshy task', tid, flush=True)
t0, last = time.time(), None
while True:
    task = call(f'{API}/{tid}')
    if (task['status'], task['progress']) != last:
        last = (task['status'], task['progress'])
        print(f'  {int(time.time() - t0)}s {task["status"]} {task["progress"]}%', flush=True)
    if task['status'] in ('SUCCEEDED', 'FAILED', 'CANCELED'):
        break
    time.sleep(10)
(out / 'task.json').write_text(json.dumps(task, indent=1))
if task['status'] != 'SUCCEEDED':
    sys.exit(f'meshy {task["status"]}: {task.get("task_error")}')
for fmt, url in task.get('model_urls', {}).items():
    if url and fmt in ('glb', 'fbx', 'pre_remeshed_glb'):
        fetch(url, out / ('model-pre-remesh.glb' if fmt == 'pre_remeshed_glb' else f'model.{fmt}'))
for i, tex in enumerate(task.get('texture_urls') or []):
    for kind, url in tex.items():
        if url:
            fetch(url, out / f'texture{i or ""}-{kind}.png')
for view, url in (task.get('thumbnail_urls') or {}).items():
    fetch(url, out / f'thumb-{view}.png')
print('meshy done', out, 'credits', task.get('consumed_credits'), f'{int(time.time() - t0)}s')
