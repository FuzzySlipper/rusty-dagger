#!/usr/bin/env python3
"""splat-people: the gsplat townspeople that `people.splat` draws every walker as, and the art pack's `splatPeople` section.

Takes asset-pipeline's ready dagger characters (outputs/dagger-characters/ready.json: 09-game approved by Patch, each with
the Engine's skinned-splat GLB on the 22 Mixamo body bones and its fit to PINOC's Mixamo Walking and Standing Idle) and
writes into content/worldrpg/packs/daggerfall.art-replacements/media/splat-people/:

  ID.glb          each character's skinned-splat GLB, as asset-pipeline wrote it
  clips/walk.glb  PINOC's Mixamo Walking as a clip pack: its skeleton, skin and one clip named walk, without the
  clips/idle.glb  X Bot mesh it is exported with; and Standing Idle the same, named idle. The two exports name their
                  clips alike (Armature.001|Scene), and one character carries both packs, so each takes its own name.

and states artReplacements.splatPeople in payload.json: the clips, the joint the fits move (the top joint, Hips), and
per character its name, body and the pelvis fit of each clip (k and A: Hips at (k x, A + k y, k z)) with the speed
the fitted walk travels. Rerun it when asset-pipeline marks another character ready or refits one.

    authoring/tools/people/splat-people.py [--characters DIR] [--clips DIR]
"""
import argparse
import hashlib
import json
import shutil
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'
MEDIA = PACK / 'media/splat-people'
CHARACTERS = ROOT.parent / 'asset-pipeline/outputs/dagger-characters'
CLIPS = Path('/data/assets/splat-characters/pinoc-clips/mixamo')
# Each pack's clip, by the name the game plays it under, from the PINOC Mixamo export it is cut from.
PACKS = {'walk': 'walking.glb', 'idle': 'standing-idle.glb'}
TOP_JOINT = 'mixamorig:Hips'


def read_glb(path):
    data = path.read_bytes()
    length = struct.unpack_from('<I', data, 12)[0]
    gltf = json.loads(data[20:20 + length])
    at = 20 + length
    binary = data[at + 8:at + 8 + struct.unpack_from('<I', data, at)[0]]
    return gltf, binary


def write_glb(path, gltf, binary):
    text = json.dumps(gltf, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    binary += b'\0' * (-len(binary) % 4)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(binary))
                     + struct.pack('<II', len(text), 0x4E4F534A) + text + struct.pack('<II', len(binary), 0x004E4942) + binary)


def clip_pack(source, name, out):
    """The export's skeleton, skin and one clip, renamed name: nodes keep their order, meshes and their data go."""
    gltf, binary = read_glb(source)
    if len(gltf.get('animations', [])) != 1:
        raise SystemExit(f'{source} carries {len(gltf.get("animations", []))} clips, not one')
    animation = gltf['animations'][0]
    skins = gltf['skins']
    kept = sorted({sampler[key] for sampler in animation['samplers'] for key in ('input', 'output')}
                  | {skin['inverseBindMatrices'] for skin in skins})
    accessors, views, data = [], [], bytearray()
    for index in kept:
        accessor = dict(gltf['accessors'][index])
        view = gltf['bufferViews'][accessor.pop('bufferView')]
        if 'byteStride' in view:
            raise SystemExit(f'{source} interleaves accessor {index}')
        data += b'\0' * (-len(data) % 4)
        views.append({'buffer': 0, 'byteOffset': len(data), 'byteLength': view['byteLength']})
        data += binary[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
        accessors.append(accessor | {'bufferView': len(views) - 1})
    remap = {old: new for new, old in enumerate(kept)}
    nodes = [{key: value for key, value in node.items() if key not in ('mesh', 'skin')} for node in gltf['nodes']]
    scene_nodes = [index for index in gltf['scenes'][gltf.get('scene', 0)]['nodes'] if 'mesh' not in gltf['nodes'][index]]
    pack = {
        'asset': {'version': '2.0', 'generator': 'rusty-dagger authoring/tools/people/splat-people.py'},
        'scene': 0, 'scenes': [{'nodes': scene_nodes}], 'nodes': nodes,
        'skins': [skin | {'inverseBindMatrices': remap[skin['inverseBindMatrices']]} for skin in skins],
        'animations': [{'name': name, 'channels': animation['channels'],
                        'samplers': [sampler | {'input': remap[sampler['input']], 'output': remap[sampler['output']]}
                                     for sampler in animation['samplers']]}],
        'accessors': accessors, 'bufferViews': views, 'buffers': [{'byteLength': len(data)}],
    }
    write_glb(out, pack, bytes(data))


def entry(path):
    data = path.read_bytes()
    return {'path': path.relative_to(ROOT / 'content').as_posix(), 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--characters', type=Path, default=CHARACTERS, help="asset-pipeline's outputs/dagger-characters")
    parser.add_argument('--clips', type=Path, default=CLIPS, help="PINOC's Mixamo exports of Walking and Standing Idle")
    args = parser.parse_args()
    if MEDIA.exists():
        shutil.rmtree(MEDIA)
    (MEDIA / 'clips').mkdir(parents=True)
    clips = {}
    for name, file in PACKS.items():
        clip_pack(args.clips / file, name, MEDIA / 'clips' / f'{name}.glb')
        clips[name] = entry(MEDIA / 'clips' / f'{name}.glb')
    characters = {}
    for ready in json.loads((args.characters / 'ready.json').read_text())['ready']:
        body = args.characters / ready['engine_glb']
        fit = json.loads(body.with_name(f'{ready["id"]}-clip-fit.json').read_text())['clips']
        if {fit[name]['path'] for name in PACKS} != set(PACKS.values()):
            raise SystemExit(f'{ready["id"]} was fitted to {sorted(fit[name]["path"] for name in PACKS)}, not {sorted(PACKS.values())}')
        shutil.copyfile(body, MEDIA / f'{ready["id"]}.glb')
        characters[ready['id']] = {
            'name': ready['name'], 'body': entry(MEDIA / f'{ready["id"]}.glb'),
            'walk': {'k': fit['walk']['k'], 'A': fit['walk']['A'], 'speed': round(fit['walk']['fitted']['speed_m_s'], 4)},
            'idle': {'k': fit['idle']['k'], 'A': fit['idle']['A']},
        }
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    payload['artReplacements']['splatPeople'] = {
        'clips': clips, 'joint': TOP_JOINT,
        'producer': 'PINOC library (Viggle), Mixamo rig', 'license': 'PINOC library clip',
        'characters': characters}
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print(f'wrote {len(characters)} characters and the walk and idle packs, and stated artReplacements.splatPeople')


if __name__ == '__main__':
    main()
