#!/usr/bin/env python3
"""Package clip_compare.py's bound splats and their own rig for Workbench.

Use the scaffold and alignment transform used in `clip_compare.py prep`.
The first --body ID=TAG is the default. Every body shares the baked clips.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation

sys.path.insert(0, str(Path(__file__).resolve().parent))
from retarget_clip import REPO, read_glb

sys.path.insert(0, str(REPO / 'authoring/tools/people/splat/gsplat'))
from make_skinned_vsplat import write_vsplat


def joint_order(doc, joints):
    indices = {node: index for index, node in enumerate(joints)}
    order = []

    def visit(node):
        if node in indices:
            order.append(indices[node])
        for child in doc['nodes'][node].get('children', []):
            visit(child)

    for root in doc['scenes'][doc.get('scene', 0)]['nodes']:
        visit(root)
    return np.asarray(order)


def write_animated_rig(path, source, doc, joints, bind, clips):
    raw = source.read_bytes()
    json_length, = struct.unpack_from('<I', raw, 12)
    binary = bytearray(raw[28 + json_length:])
    parents = {child: node for node, info in enumerate(doc['nodes']) for child in info.get('children', [])}
    joint_index = {node: index for index, node in enumerate(joints)}

    def accessor(values, kind, **bounds):
        packed = np.asarray(values, dtype='<f4').tobytes()
        views = doc['bufferViews']
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(packed)})
        binary.extend(packed)
        accessors = doc['accessors']
        accessors.append({'bufferView': len(views) - 1, 'componentType': 5126, 'count': len(values), 'type': kind, **bounds})
        return len(accessors) - 1

    animations = []
    for name, (skin, fps) in clips.items():
        worlds = skin @ bind
        inverse_worlds = np.linalg.inv(worlds)
        times = np.arange(len(skin)) / fps
        time = accessor(times, 'SCALAR', min=[0], max=[float(times[-1])])
        channels, samplers = [], []
        for index, node in enumerate(joints):
            parent = joint_index.get(parents.get(node))
            local = worlds[:, index] if parent is None else inverse_worlds[:, parent] @ worlds[:, index]
            rotations = Rotation.from_matrix(local[:, :3, :3]).as_quat()
            # Keep consecutive keys in the same quaternion hemisphere.
            flips = (rotations[1:] * rotations[:-1]).sum(1) < 0
            rotations[1:] *= np.cumprod(np.where(flips, -1, 1))[:, None]
            for channel, keys, kind in [('translation', local[:, :3, 3], 'VEC3'), ('rotation', rotations, 'VEC4')]:
                channels.append({'sampler': len(samplers), 'target': {'node': node, 'path': channel}})
                samplers.append({'input': time, 'output': accessor(keys, kind), 'interpolation': 'LINEAR'})
        animations.append({'name': name, 'samplers': samplers, 'channels': channels})

    doc['animations'] = animations
    doc['buffers'][0]['byteLength'] = len(binary)
    encoded = json.dumps(doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(encoded) + len(binary))
                     + struct.pack('<II', len(encoded), 0x4E4F534A) + encoded
                     + struct.pack('<II', len(binary), 0x004E4942) + binary)


def export(scaffold, transform, data, output, bodies):
    prep = json.loads((data / 'prep.json').read_text())
    # Undo only the comparison's uniform change of units, back to the rig GLB.
    unit = prep['metres_per_bake_unit'] * json.loads(transform.read_text())['scale']
    doc, read_accessor = read_glb(scaffold)
    skin = doc['skins'][0]
    joints = skin['joints']
    bind = np.linalg.inv(read_accessor(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1))
    order = joint_order(doc, joints)
    palette = np.argsort(order).tolist()

    clips = {}
    for name, info in prep['clips'].items():
        samples = [np.load(data / f'poses-{tag}-{name}.npz')['skin'] for _, tag in bodies]
        if any(not np.array_equal(samples[0], poses) for poses in samples[1:]):
            raise ValueError(f'{name} differs between bodies. Export bodies with different motions in separate sets.')
        matrices = samples[0].astype(np.float64)
        matrices[:, :, :3, 3] /= unit
        clips[name] = matrices, info['fps']

    output.mkdir(parents=True, exist_ok=True)
    rig_file = 'scaffold.glb'
    write_animated_rig(output / rig_file, scaffold, doc, joints, bind, clips)
    for ident, tag in bodies:
        with np.load(data / f'{tag}.npz') as splat, np.load(data / f'{tag}-binding.npz') as binding:
            write_vsplat(output / f'{ident}.vsplat', splat['means'] / unit, splat['scales'] - np.log(unit),
                         splat['quats'], splat['opacities'], splat['rgb'], binding['bones'], binding['weights'],
                         palette, bind[order, :3, 3])

    manifest = {'body': f'{bodies[0][0]}.vsplat', 'rig': rig_file,
                'variants': {ident: f'{ident}.vsplat' for ident, _ in bodies[1:]},
                'clips': {name: {'file': rig_file, 'animation': name, 'label': name.replace('-', ' ').title()}
                          for name in clips}}
    (output / 'character.json').write_text(json.dumps(manifest, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('scaffold', type=Path)
    parser.add_argument('transform', type=Path)
    parser.add_argument('data', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--body', action='append', required=True, metavar='ID=TAG',
                        help='name of the exported body and figure tag from clip_compare.py prep')
    args = parser.parse_args()
    bodies = {}
    for value in args.body:
        ident, _, tag = value.partition('=')
        if not ident or not tag or ident in bodies:
            parser.error('--body needs a distinct ID=TAG for each body')
        bodies[ident] = tag
    export(args.scaffold, args.transform, args.data, args.output, list(bodies.items()))


if __name__ == '__main__':
    main()
