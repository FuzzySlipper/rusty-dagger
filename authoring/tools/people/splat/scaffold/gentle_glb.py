#!/usr/bin/env python3
"""gentle_clip.py's softening applied to a GLB clip, for the Engine.

    gentle_glb.py CLIP.glb OUT.glb [--amount 0.75] [--root mixamorig:Hips]

Every rotation channel's keys are slerped from the channel's clip-average rotation toward the key by --amount (1: unchanged,
0: frozen in the average pose). The root's height bob (its translation along world up, through its parents' rest transforms)
is scaled the same way about its mean; its travel across the ground is kept. Nothing else in the file changes: the keys are
rewritten in place in the binary chunk, and the accessors' min/max are refreshed.

The stride shrinks but a clip that travels (root motion) still covers the same ground, so planted feet slide unless the
product scales the root's travel to the new stride. The Engine's clip fit (authoring/tools/people/splat/gsplat/splat_glb_clip_fit.py,
"least planted-toe drift") finds that scale per character: fit the gentle clip as its own entry.

Why (Patch 2026-11-21): long skirts on skirt bones "could work fine" with "a slightly less vigorous walk". The bench plays
gentle_clip.py's 0.75 copy of the in-place Mixamo walk; rusty-dagger #10968 plays PINOC's Mixamo Walking in the Engine and
asked for the same softening on that clip.
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot


def read_glb(p):
    b = p.read_bytes()
    n = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + n])
    o = 20 + n
    bl = struct.unpack('<I', b[o:o + 4])[0]
    return j, bytearray(b[o + 8:o + 8 + bl])


def write_glb(p, j, binc):
    js = json.dumps(j, separators=(',', ':')).encode()
    js += b' ' * (-len(js) % 4)
    binc = bytes(binc) + b'\0' * (-len(binc) % 4)
    total = 12 + 8 + len(js) + 8 + len(binc)
    p.write_bytes(struct.pack('<III', 0x46546C67, 2, total) + struct.pack('<II', len(js), 0x4E4F534A) + js
                  + struct.pack('<II', len(binc), 0x004E4942) + binc)


def view(j, binc, acc, ncomp):
    a = j['accessors'][acc]
    bv = j['bufferViews'][a['bufferView']]
    assert a['componentType'] == 5126 and not bv.get('byteStride'), 'float32, tightly packed only'
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    return off, np.frombuffer(bytes(binc[off:off + 4 * ncomp * a['count']]), '<f4').reshape(a['count'], ncomp).astype(float)


def put(j, binc, acc, off, arr):
    binc[off:off + arr.size * 4] = arr.astype('<f4').tobytes()
    a = j['accessors'][acc]
    if 'min' in a:
        a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('clip', type=Path); ap.add_argument('out', type=Path)
    ap.add_argument('--amount', type=float, default=0.75); ap.add_argument('--root', default='mixamorig:Hips')
    a = ap.parse_args()
    j, binc = read_glb(a.clip)
    nodes = j['nodes']; parent = {c: i for i, nd in enumerate(nodes) for c in nd.get('children', [])}
    report = {'from': str(a.clip), 'amount': a.amount, 'rotation_channels': 0}
    for an in j['animations']:
        outs = [an['samplers'][c['sampler']]['output'] for c in an['channels']]
        assert len(outs) == len(set(outs)), 'shared sampler outputs'
        for c in an['channels']:
            node, path = c['target']['node'], c['target']['path']
            s = an['samplers'][c['sampler']]
            if path == 'rotation':
                off, q = view(j, binc, s['output'], 4)
                if s.get('interpolation') == 'CUBICSPLINE':
                    raise SystemExit('CUBICSPLINE rotations not handled')
                m = Rot.from_quat(q).mean()
                d = (m.inv() * Rot.from_quat(q)).as_rotvec() * a.amount
                q2 = (m * Rot.from_rotvec(d)).as_quat()
                q2 *= np.sign(np.sum(q2 * q, 1, keepdims=True) + 1e-12)   # keep each key's hemisphere: no flips between keys
                put(j, binc, s['output'], off, q2); report['rotation_channels'] += 1
            elif path == 'translation' and nodes[node].get('name') == a.root:
                off, t = view(j, binc, s['output'], 3)
                R = Rot.identity(); k = parent.get(node)
                while k is not None:   # parents' rest rotations (the Armature wrapper turns Z-up into Y-up)
                    R = Rot.from_quat(nodes[k].get('rotation', [0, 0, 0, 1])) * R; k = parent.get(k)
                up = R.inv().apply([0, 1, 0]); up /= np.linalg.norm(up)
                h = t @ up; h2 = h.mean() + a.amount * (h - h.mean())
                put(j, binc, s['output'], off, t + np.outer(h2 - h, up))
                report['root_bob'] = {'axis_in_parent': np.round(up, 4).tolist(), 'range_before': float(np.ptp(h)), 'range_after': float(np.ptp(h2))}
    j.setdefault('asset', {}).setdefault('extras', {})['gentle'] = {'from': a.clip.name, 'amount': a.amount, 'tool': 'asset-pipeline authoring/tools/people/splat/scaffold/gentle_glb.py'}
    a.out.parent.mkdir(parents=True, exist_ok=True); write_glb(a.out, j, binc)
    print(json.dumps(report))


if __name__ == '__main__':
    main()
