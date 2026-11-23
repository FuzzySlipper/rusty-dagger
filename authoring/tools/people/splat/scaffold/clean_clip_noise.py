#!/usr/bin/env python3
"""Snap a clip GLB's still translation channels to exact constants.

    clean_clip_noise.py CLIP.glb OUT.glb [--tolerance 1e-4]

A Mixamo clip keys a translation on every bone, and on all but the hips it is the clip skeleton's bone length, still but for
float noise. PINOC's Mixamo Walking carries up to 5.6e-6 of it on 40 of its 65 bone channels, unevenly between the sides
(LeftFoot and RightUpLeg, not RightFoot or LeftUpLeg). The Engine keeps a clip's translation channel when any key differs from
the first by more than 1e-6 (rusty-engine animation-pose rig.rs moves()), so those channels put X Bot's bone lengths on a
figure bound by joint names: on the bound villagers the left shin took X Bot's 44.5 cm against 389's 36.4 cm and the right
toe stood 1-7 cm off the ground in the walk (Patch 2026-11-19: clean the clip). This sets every key of a translation channel
whose values spread less than --tolerance (in the channel's own units, as the Engine compares them) to its
first key, so the Engine drops it and the figure keeps its own bone lengths. Rotation and scale channels, and translation
channels that move (the hips), are untouched. The binary layout is unchanged; accessor min/max are rewritten.
"""
import argparse, json, struct
from pathlib import Path
import numpy as np


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('clip', type=Path); ap.add_argument('out', type=Path); ap.add_argument('--tolerance', type=float, default=1e-4)
    a = ap.parse_args()
    raw = bytearray(a.clip.read_bytes())
    jl = struct.unpack_from('<I', raw, 12)[0]; doc = json.loads(raw[20:20 + jl])
    bin_start = 20 + jl + 8
    seen, snapped, kept = set(), [], []
    for anim in doc.get('animations', []):
        for ch in anim['channels']:
            if ch['target'].get('path') != 'translation':
                continue
            acc_i = anim['samplers'][ch['sampler']]['output']
            if acc_i in seen:
                continue
            seen.add(acc_i)
            acc = doc['accessors'][acc_i]; view = doc['bufferViews'][acc['bufferView']]
            if acc['componentType'] != 5126 or acc['type'] != 'VEC3':
                continue
            stride = view.get('byteStride', 12)
            off = bin_start + view.get('byteOffset', 0) + acc.get('byteOffset', 0)
            interp = anim['samplers'][ch['sampler']].get('interpolation', 'LINEAR')
            n = acc['count']
            vals = np.array([struct.unpack_from('<3f', raw, off + i * stride) for i in range(n)], np.float64)
            keys = vals[1::3] if interp == 'CUBICSPLINE' else vals
            name = doc['nodes'][ch['target']['node']].get('name', str(ch['target']['node']))
            spread = float(np.ptp(keys, 0).max()) if len(keys) > 1 else 0.0
            if spread == 0.0 or spread >= a.tolerance:
                (kept if spread else []).append((name, spread)); continue
            first = keys[0].astype(np.float32)
            for i in range(n):
                v = first if interp != 'CUBICSPLINE' or i % 3 == 1 else np.zeros(3, np.float32)   # spline tangents to zero
                struct.pack_into('<3f', raw, off + i * stride, *v.tolist())
            acc['min'] = [float(x) for x in np.minimum(first, 0) ] if interp == 'CUBICSPLINE' else [float(x) for x in first]
            acc['max'] = [float(x) for x in np.maximum(first, 0)] if interp == 'CUBICSPLINE' else [float(x) for x in first]
            snapped.append((name, spread))
    js = json.dumps(doc, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    body = bytes(raw[20 + jl:])                                   # BIN chunk header and data, as they were
    out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + len(body)) + struct.pack('<II', len(js), 0x4E4F534A) + js + body
    tmp = a.out.with_suffix('.part'); tmp.write_bytes(out); tmp.replace(a.out)
    print(json.dumps({'clip': str(a.clip), 'out': str(a.out), 'tolerance': a.tolerance, 'snapped': len(snapped),
                      'largest_snapped': max((s for _, s in snapped), default=0), 'moving_kept': [[n, round(s, 4)] for n, s in kept]}))


if __name__ == '__main__':
    main()
