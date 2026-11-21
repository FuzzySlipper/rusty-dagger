#!/usr/bin/env python3
"""Step 2: the concept front painting from a costume brief (New B text-to-image with the turnaround wording, "stB").

    python3 front.py ID [--seeds 1,2,3] [--out DIR] [--comfy URL] [--print]

ID is a key of briefs.json (a texture number, or your own id for a new character). The prompt is put together from four parts:
  1. the style line             "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration."
  2. the costume brief          briefs.json, the only part that changes per character
  3. the turnaround stance      costume fitting / turnaround sheet front view, symmetrical, wide A arms, slate ground, light overhead
  4. the art-direction paragraph  ink and gouache, crosshatching the same on both sides, readable face
and run through front-template.graph.json (krea2_turbo_fp8_scaled, LoRAs Brom 0.35 + Elmore 0.45 + RetroInk 0.45, 832x1216,
8 steps, cfg 1, euler/simple): the graph villager 456's approved front was made with. Writes OUT/front-sN.png, its
.graph.json and front.prompt.txt (default OUT: local/art/generated/studies/people-concept/ID). --print writes the prompt only, without generating.

Default ComfyUI is the GPU lab's (192.168.1.20:8188, the 5090): about 6 s an image.
"""
import argparse, json, time, urllib.request, uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
DEFAULT_OUTPUT = REPO / 'local/art/generated/studies/people-concept'
STYLE = 'Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.'
ART = ('Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber pen-and-ink '
       'contour lines, fine crosshatching only inside the folds of the cloth and under overhangs, the same on both sides of the '
       'figure, and opaque matte gouache colour planes (4 to 6 distinct painted value groups per material). Keep the face and eyes '
       'well-lit, expressive and clearly readable in warm shaded skin tones—do not obscure the eyes or face in pitch-black '
       'shadow, and avoid glossy photorealistic airbrushing.')


def stance(pro):
    pos = 'his' if pro == 'He' else 'her'
    return (f'Full length, the whole figure visible with room around it, shown as the front view of a costume fitting or character '
            f'turnaround sheet, in a symmetrical neutral standing pose facing the viewer, feet a little apart with both shoes whole. '
            f'{pos.capitalize()} arms hang relaxed and slightly away from {pos} body, with a clear gap of background between each arm '
            f'and {pos} waist, hands open and empty. Against a plain flat dark slate-grey background, RGB 48,52,59, with no floor, '
            f'scenery, cast shadow, canvas frame, border, lettering or other people. Soft light from directly overhead lights both '
            f'sides of {pos} body equally. {pro} is the only person in the picture. Natural adult proportions.')


def prompt(entry):
    return f"{STYLE} {entry['brief'].rstrip('.')}. {stance(entry['pronoun'])}\n\n{ART}"


def run(comfy, graph, out, seed):
    g = json.loads(json.dumps(graph))
    next(n for n in g.values() if n['class_type'] == 'KSampler')['inputs']['seed'] = seed
    for n in g.values():
        if n['class_type'] == 'SaveImage':
            n['inputs']['filename_prefix'] = f'concept/front-s{seed}'
    req = urllib.request.Request(comfy + '/prompt', json.dumps({'prompt': g, 'client_id': uuid.uuid4().hex}).encode(),
                                 {'Content-Type': 'application/json'})
    pid = json.loads(urllib.request.urlopen(req).read())['prompt_id']
    while True:   # the lab's queue is shared: wait our turn, never withdraw anything
        hist = json.loads(urllib.request.urlopen(f'{comfy}/history/{pid}').read())
        if pid in hist:
            break
        time.sleep(2)
    if hist[pid]['status']['status_str'] != 'success':
        raise SystemExit(f'seed {seed}: {hist[pid]["status"]}')
    im = next(i for o in hist[pid]['outputs'].values() for i in o.get('images', []))
    q = f"filename={im['filename']}&subfolder={im['subfolder']}&type={im['type']}"
    (out / f'front-s{seed}.png').write_bytes(urllib.request.urlopen(f'{comfy}/view?{q}').read())
    (out / f'front-s{seed}.png.graph.json').write_text(json.dumps(g, indent=1) + '\n')
    print(out / f'front-s{seed}.png')


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('id'); ap.add_argument('--seeds', default='1,2,3'); ap.add_argument('--out', type=Path)
    ap.add_argument('--comfy', default='http://192.168.1.20:8188'); ap.add_argument('--print', action='store_true')
    a = ap.parse_args()
    briefs = json.loads((HERE / 'briefs.json').read_text())['briefs']
    if a.id not in briefs:
        raise SystemExit(f'{a.id} has no brief in briefs.json: write one first (describe.md)')
    p = prompt(briefs[a.id]); out = a.out or DEFAULT_OUTPUT / a.id; out.mkdir(parents=True, exist_ok=True)
    (out / 'front.prompt.txt').write_text(p + '\n')
    if a.print:
        print(p); return
    graph = json.loads((HERE / 'front-template.graph.json').read_text())
    next(n for n in graph.values() if n['class_type'] == 'CLIPTextEncode')['inputs']['text'] = p
    for s in (int(x) for x in a.seeds.split(',')):
        run(a.comfy, graph, out, s)


if __name__ == '__main__':
    main()
