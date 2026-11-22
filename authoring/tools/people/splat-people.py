#!/usr/bin/env python3
"""splat-people: the gsplat townspeople every town walker draws as, and the art pack's `splatPeople` section.

Takes the ready Dagger characters (local/art/generated/people/dagger-characters/ready.json: 09-game approved by Patch, each with
the Engine's skinned-splat GLB on the 22 Mixamo body bones and its fit to every clip in PACKS) and writes into
content/worldrpg/packs/daggerfall.art-replacements/media/splat-people/:

  ID.glb             each character's skinned-splat GLB, as asset-pipeline wrote it
  clips/NAME.glb     each clip of PACKS as a clip pack: the PINOC Mixamo export's skeleton, skin and one clip named NAME,
                     without the X Bot mesh it is exported with. The exports name their clips alike (Armature.001|Scene),
                     and one character carries several packs, so each takes its own name. The Engine binds a pack by
                     joint names only when it names every joint of the character, so each pack also names the skirt
                     joints the characters carry, under their own parents with no channels.

The clips (rusty-dagger #10978: walkers vary, not clones) are PINOC's library Walking and Standing Idle, softened
Walking (gentle), and motions generated on Patch's PINOC account, each cut to a seamless loop (asset-pipeline
loop_clip.py) and cleaned (clean_clip_noise.py), some softened (gentle_glb.py). Library clips are working material until
Patch settles their licence (2026-11-19), so a new clip is a generated one. A walker walks one of WALKS (one of SKIRT_WALKS, softer, in a long
skirt) and stands in one of IDLES, picked by its seed in the game.

It states artReplacements.splatPeople in payload.json: the clips, the three sets, the joint the fits move (the top
joint, Hips), and per character its name, the classic townsperson archive it draws (each walker wearing that art draws
as it), its body, the pelvis fit of each clip it plays (k and A: Hips at (k x, A + k y, k z)) with the speed a fitted
walk travels, and its palette: PALETTE sRGB colours k-means picks from its own Gaussians, weighted by opacity, which a
pixel finish can snap its blocks to. A character with a long skirt on joints of its own (its GLB's asset.extras.skirt,
from asset-pipeline's skirt_bones.py rig) states its skirt: two tiers as the cloth sheet's columns, rows, rest grid and
bone columns, one tier as the ring's joints, directions, pivots and reach by height. Rerun it when asset-pipeline marks
another character ready or refits one.

    authoring/tools/people/splat-people.py [--characters DIR] [--clips DIR]
"""
import argparse
import hashlib
import json
import os
import shutil
import struct
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'
MEDIA = PACK / 'media/splat-people'
CHARACTERS = ROOT / 'local/art/generated/people/dagger-characters'
CLIPS = Path('/data/assets/splat-characters/pinoc-clips/mixamo')
# Each pack's clip, by the name the game plays it under, from the PINOC Mixamo export it is cut from.
PACKS = {'walk': 'walking.glb', 'stroll': 'relaxed-stroll.glb', 'stroll-easy': 'relaxed-stroll-easy.glb',
         'gentle': 'walking-gentle.glb', 'stroll-gentle': 'relaxed-stroll-gentle.glb',
         'idle': 'standing-idle.glb', 'idle-relaxed': 'relaxed-idle.glb', 'idle-crossed': 'crossed-arms-idle.glb', 'idle-hips': 'hands-on-hips-idle.glb'}
# The walks a walker picks from; the softer ones a walker in a long skirt picks from instead; the idles any walker picks from.
WALKS = ('walk', 'stroll', 'stroll-easy')
SKIRT_WALKS = ('gentle', 'stroll-gentle')
IDLES = ('idle', 'idle-relaxed', 'idle-crossed', 'idle-hips')
TOP_JOINT = 'mixamorig:Hips'
PALETTE = 32


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


def skin_joints(path):
    """A GLB's skin joints, by name, each with its parent's name (None at the top)."""
    gltf, _ = read_glb(path)
    parent = {child: index for index, node in enumerate(gltf['nodes']) for child in node.get('children', [])}
    named = lambda index: gltf['nodes'][index].get('name')   # noqa: E731
    return {named(j): (named(parent[j]) if j in parent else None) for j in gltf['skins'][0]['joints']}


def clip_pack(source, name, out, extra):
    """The export's skeleton, skin and one clip, renamed name: nodes keep their order, meshes and their data go. Each
    joint of extra (name: parent name) the export lacks joins its skin under its parent, unmoved and without channels."""
    gltf, binary = read_glb(source)
    if len(gltf.get('animations', [])) != 1:
        raise SystemExit(f'{source} carries {len(gltf.get("animations", []))} clips, not one')
    if len(gltf['skins']) != 1:
        raise SystemExit(f'{source} carries {len(gltf["skins"])} skins, not one')
    animation = gltf['animations'][0]
    skin = gltf['skins'][0]
    accessors, views, data = [], [], bytearray()

    def add(block, accessor):
        nonlocal data
        data += b'\0' * (-len(data) % 4)
        views.append({'buffer': 0, 'byteOffset': len(data), 'byteLength': len(block)})
        data += block
        accessors.append(accessor | {'bufferView': len(views) - 1})
        return len(accessors) - 1

    def block(index):
        accessor = dict(gltf['accessors'][index])
        view = gltf['bufferViews'][accessor.pop('bufferView')]
        if 'byteStride' in view:
            raise SystemExit(f'{source} interleaves accessor {index}')
        return binary[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']], accessor

    remap = {index: add(*block(index)) for index in sorted({sampler[key] for sampler in animation['samplers'] for key in ('input', 'output')})}
    nodes = [{key: value for key, value in node.items() if key not in ('mesh', 'skin')} for node in gltf['nodes']]
    joints, at = list(skin['joints']), {node.get('name'): index for index, node in enumerate(nodes)}
    binds, ibm = block(skin['inverseBindMatrices'])
    binds = list(np.frombuffer(binds, '<f4', 16 * ibm['count'], ibm.get('byteOffset', 0)).reshape(-1, 16))
    pending = {joint: parent for joint, parent in extra.items() if joint not in at}
    while pending:
        ready = [joint for joint, parent in pending.items() if parent in at]
        if not ready:
            raise SystemExit(f'{source} has no parent for {sorted(pending)}')
        for joint in ready:
            parent = at[pending.pop(joint)]
            nodes.append({'name': joint})
            at[joint] = len(nodes) - 1
            nodes[parent]['children'] = nodes[parent].get('children', []) + [at[joint]]
            # Unmoved under its parent, it binds where its parent does.
            binds.append(binds[joints.index(parent)])
            joints.append(at[joint])
    inverse = add(np.array(binds, '<f4').tobytes(), {'componentType': 5126, 'count': len(joints), 'type': 'MAT4'})
    scene_nodes = [index for index in gltf['scenes'][gltf.get('scene', 0)]['nodes'] if 'mesh' not in gltf['nodes'][index]]
    pack = {
        'asset': {'version': '2.0', 'generator': 'rusty-dagger authoring/tools/people/splat-people.py'},
        'scene': 0, 'scenes': [{'nodes': scene_nodes}], 'nodes': nodes,
        'skins': [skin | {'joints': joints, 'inverseBindMatrices': inverse}],
        'animations': [{'name': name, 'channels': animation['channels'],
                        'samplers': [sampler | {'input': remap[sampler['input']], 'output': remap[sampler['output']]}
                                     for sampler in animation['samplers']]}],
        'accessors': accessors, 'bufferViews': views, 'buffers': [{'byteLength': len(data)}],
    }
    write_glb(out, pack, bytes(data))


def palette(body):
    """PALETTE sRGB colours (#rrggbb) that k-means picks from the body's Gaussians, weighted by their opacity."""
    gltf, binary = read_glb(body)
    accessor = gltf['accessors'][gltf['meshes'][0]['primitives'][0]['attributes']['_COLOR']]
    if (accessor['componentType'], accessor['type'], accessor.get('normalized')) != (5121, 'VEC4', True):
        raise SystemExit(f'{body} keeps _COLOR as {accessor["componentType"]} {accessor["type"]}, not normalized bytes')
    view = gltf['bufferViews'][accessor['bufferView']]
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    rgba = np.frombuffer(binary, np.uint8, accessor['count'] * 4, start).reshape(-1, 4).astype(np.float64)
    colours, weights = rgba[:, :3], rgba[:, 3] / 255
    rng = np.random.default_rng(10922)
    centres = colours[rng.choice(len(colours), PALETTE, replace=False, p=weights / weights.sum())]
    for _ in range(40):
        nearest = np.argmin(((colours[:, None] - centres[None]) ** 2).sum(-1), axis=1)
        for k in range(PALETTE):
            mine = nearest == k
            if weights[mine].sum() > 0:
                centres[k] = (colours[mine] * weights[mine, None]).sum(0) / weights[mine].sum()
    return [f'#{r:02x}{g:02x}{b:02x}' for r, g, b in np.clip(np.round(centres), 0, 255).astype(int)]


def entry(path):
    data = path.read_bytes()
    return {'path': path.relative_to(ROOT / 'content').as_posix(), 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}


def skirt(body):
    """The long skirt a body's GLB carries on joints of its own (asset.extras.skirt), as the payload states it, or None."""
    rest = read_glb(body)[0]['asset'].get('extras', {}).get('skirt')
    if rest is None:
        return None
    legs = {'left': rest['legs']['Left'], 'right': rest['legs']['Right']}
    rounded = lambda values: [round(value, 6) for value in values]   # noqa: E731
    if rest.get('tiers') == 2:
        return {'tiers': 2, 'hips': rest['hips'], 'legs': legs, 'centre': rounded(rest['centre']),
                'columns': rest['nu'], 'rows': rest['nv'], 'grid': [rounded(point) for column in rest['grid0'] for point in column],
                'boneColumns': rest['bone_cols'], 'upper': rest['joints'][0::2], 'lower': rest['joints'][1::2], 'slit': rest['mode'] == 'slit'}
    return {'tiers': 1, 'hips': rest['hips'], 'legs': legs, 'centre': rounded(rest['centre']), 'top': round(rest['top'], 6),
            'hem': round(rest['hem'], 6), 'joints': rest['joints'], 'theta': rounded(rest['theta']),
            'pivots': [rounded(point) for point in rest['pivots']], 'heights': rounded(rest['ys']), 'reach': [rounded(column) for column in rest['rad']]}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--characters', type=Path, default=os.environ.get('DAGGER_CHAR_ROOT') or CHARACTERS,
                        help="character stage root containing ready.json")
    parser.add_argument('--clips', type=Path, default=CLIPS, help="the PINOC Mixamo exports PACKS names, looped and cleaned")
    args = parser.parse_args()
    ready_list = json.loads((args.characters / 'ready.json').read_text())['ready']
    bodies, fits, skirts, extra = {}, {}, {}, {}
    for ready in ready_list:
        body = bodies[ready['id']] = args.characters / ready['engine_glb']
        fits[ready['id']] = json.loads(body.with_name(f'{ready["id"]}-clip-fit.json').read_text())['clips']
        skirts[ready['id']] = skirt(body)
        packs = (SKIRT_WALKS if skirts[ready['id']] else WALKS) + IDLES
        if any(fits[ready['id']].get(name, {}).get('path') != PACKS[name] for name in packs):
            raise SystemExit(f'{ready["id"]} was not fitted to {", ".join(PACKS[name] for name in packs)}')
        for joint, parent in skin_joints(body).items():
            if extra.setdefault(joint, parent) != parent:
                raise SystemExit(f'{ready["id"]} hangs {joint} from {parent}, and another character from {extra[joint]}')
    if MEDIA.exists():
        shutil.rmtree(MEDIA)
    (MEDIA / 'clips').mkdir(parents=True)
    clips = {}
    for name, file in PACKS.items():
        clip_pack(args.clips / file, name, MEDIA / 'clips' / f'{name}.glb', extra)
        clips[name] = entry(MEDIA / 'clips' / f'{name}.glb')
    characters = {}
    for ready in ready_list:
        cid, fit = ready['id'], fits[ready['id']]
        shutil.copyfile(bodies[cid], MEDIA / f'{cid}.glb')
        walks = SKIRT_WALKS if skirts[cid] else WALKS
        characters[cid] = {
            'name': ready['name'],
            'archive': int(cid.split('-')[0]),  # the stage names a character ARCHIVE or ARCHIVE-VARIANT
            'body': entry(MEDIA / f'{cid}.glb'),
            'fits': {name: {'k': fit[name]['k'], 'A': fit[name]['A']} | ({'speed': round(fit[name]['fitted']['speed_m_s'], 4)} if name in walks else {})
                     for name in walks + IDLES},
            'palette': palette(MEDIA / f'{cid}.glb'),
        }
        if skirts[cid]:
            characters[cid]['skirt'] = skirts[cid]
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    payload['artReplacements']['splatPeople'] = {
        'clips': clips, 'walks': list(WALKS), 'skirtWalks': list(SKIRT_WALKS), 'idles': list(IDLES), 'joint': TOP_JOINT,
        'producer': 'PINOC (Viggle): library and generated motions, Mixamo rig', 'license': 'PINOC clip',
        'characters': characters}
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print(f'wrote {len(characters)} characters ({sum(1 for c in characters.values() if "skirt" in c)} skirted) and the {", ".join(PACKS)} packs '
          f'({sum(clip["byteLength"] for clip in clips.values()) / 1e6:.2f} MB), and stated artReplacements.splatPeople')


if __name__ == '__main__':
    main()
