#!/usr/bin/env python3
"""Cut a GLB clip to a seamless loop, for an Engine that repeats it.

    loop_clip.py CLIP.glb OUT.glb [--min-seconds 3] [--window FIRST:LAST] [--joints mixamorig:]

PINOC's generated motions (text to motion, Mixamo rig) run 6-8 s and end in another pose than they start: played on repeat
they snap 14-60 degrees at the wrap. This finds the two keyframes at least --min-seconds apart whose body poses agree best
(the largest joint rotation between them, over the skin's joints named with --joints, fingers and end joints left out), keeps
the keys from the first to the last, and closes the loop exactly: each rotation channel's remaining difference at the seam is
spread over the loop (the key at u of the way through is turned by u of it), and the top joint's translation is detrended
along every axis but the one it travels (the one that moves furthest from first to last key, when it moves 0.5 m or more;
an idle is detrended along all three), so the loop's last key is its first. Times start at 0, so the clip's length is one
cycle. --window picks the keyframes by hand. Every other channel is cut to the same keys. Run clean_clip_noise.py on the
result as on any Mixamo clip, then fit it per character (splat_glb_clip_fit.py) as its own entry.

Prints the window, its seam before closing, and what was detrended, as JSON.

Written 2026-11-21 by a rusty-dagger session for #10978 (Patch: mild variety of walks and idles for the gsplat townsfolk);
its loops are in /data/assets/splat-characters/pinoc-clips/mixamo/ (relaxed-stroll*.glb, *-idle.glb, walking-easy.glb).
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot, Slerp

SKIP = ('Thumb', 'Index', 'Middle', 'Ring', 'Pinky', 'HeadTop_End', 'ToeBase_End', 'End')


def read_glb(path):
    data = path.read_bytes()
    length = struct.unpack_from('<I', data, 12)[0]
    gltf = json.loads(data[20:20 + length])
    at = 20 + length
    return gltf, bytearray(data[at + 8:at + 8 + struct.unpack_from('<I', data, at)[0]])


def write_glb(path, gltf, binary):
    text = json.dumps(gltf, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    binary = bytes(binary) + b'\0' * (-len(binary) % 4)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(binary))
                     + struct.pack('<II', len(text), 0x4E4F534A) + text + struct.pack('<II', len(binary), 0x004E4942) + binary)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('clip', type=Path)
    ap.add_argument('out', type=Path)
    ap.add_argument('--min-seconds', type=float, default=3.0)
    ap.add_argument('--window', help='FIRST:LAST keyframe indices, in place of the search')
    ap.add_argument('--joints', default='mixamorig:', help='prefix of the body joints the seam is measured on')
    ap.add_argument('--top', default='mixamorig:Hips')
    a = ap.parse_args()
    gltf, binary = read_glb(a.clip)
    if len(gltf.get('animations', [])) != 1:
        raise SystemExit(f'{a.clip} carries {len(gltf.get("animations", []))} clips, not one')
    animation = gltf['animations'][0]
    names = [node.get('name', '') for node in gltf['nodes']]

    def values(index):
        accessor = gltf['accessors'][index]
        view = gltf['bufferViews'][accessor['bufferView']]
        if 'byteStride' in view or accessor['componentType'] != 5126:
            raise SystemExit(f'{a.clip}: accessor {index} is not tightly packed floats')
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
        start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
        return np.frombuffer(bytes(binary[start:start + 4 * width * accessor['count']]), '<f4').reshape(accessor['count'], width).astype(np.float64)

    inputs = {sampler['input'] for sampler in animation['samplers']}
    counts = {index: gltf['accessors'][index]['count'] for index in inputs}
    keyed = max(counts, key=counts.get)
    times = values(keyed)[:, 0]
    frames = len(times)
    for index, count in counts.items():
        if count not in (frames, 2):
            raise SystemExit(f'{a.clip}: a channel keys {count} times, neither every frame ({frames}) nor still (2)')

    channels = []   # (channel, sampler, joint name, path, keys or None when still)
    for channel in animation['channels']:
        sampler = animation['samplers'][channel['sampler']]
        name = names[channel['target']['node']]
        keys = values(sampler['output']) if sampler['input'] == keyed else None
        channels.append((channel, sampler, name, channel['target']['path'], keys))
    body = [keys for _, sampler, name, path, keys in channels
            if path == 'rotation' and keys is not None and name.startswith(a.joints) and not any(part in name for part in SKIP)]
    if not body:
        raise SystemExit(f'{a.clip}: no keyed rotation on a joint named {a.joints}...')
    quats = np.stack(body, 1)                                     # frames x joints x 4
    quats /= np.linalg.norm(quats, axis=-1, keepdims=True)
    agree = np.abs(np.einsum('ijk,ljk->ilj', quats, quats)).clip(0, 1)
    apart = np.degrees(2 * np.arccos(agree)).max(-1)              # frames x frames: the largest joint rotation between two poses
    least = round(a.min_seconds * (frames - 1) / (times[-1] - times[0]))
    if a.window:
        first, last = (int(value) for value in a.window.split(':'))
    else:
        pairs = [(apart[i, k], i, k) for i in range(frames) for k in range(i + least, frames)]
        if not pairs:
            raise SystemExit(f'{a.clip}: {frames} keys are fewer than --min-seconds {a.min_seconds} needs')
        _, first, last = min(pairs)
    if not 0 <= first < last < frames:
        raise SystemExit(f'{a.clip}: window {first}:{last} is not within 0:{frames - 1}')
    seam = float(apart[first, last])
    u = (np.arange(first, last + 1) - first) / (last - first)

    data = bytearray(binary)
    gltf['accessors'] = list(gltf['accessors'])
    gltf['bufferViews'] = list(gltf['bufferViews'])

    def add(array, kind):
        array = np.ascontiguousarray(array, '<f4')
        data.extend(b'\0' * (-len(data) % 4))
        gltf['bufferViews'].append({'buffer': 0, 'byteOffset': len(data), 'byteLength': array.nbytes})
        data.extend(array.tobytes())
        accessor = {'bufferView': len(gltf['bufferViews']) - 1, 'componentType': 5126, 'count': len(array), 'type': kind,
                    'min': array.reshape(len(array), -1).min(0).tolist(), 'max': array.reshape(len(array), -1).max(0).tolist()}
        gltf['accessors'].append(accessor)
        return len(gltf['accessors']) - 1

    cut_times = add((times[first:last + 1] - times[first]).reshape(-1, 1)[:, 0], 'SCALAR')
    still_times = add(np.array([0.0, times[last] - times[first]]), 'SCALAR')
    detrended = {}
    for channel, sampler, name, path, keys in channels:
        if keys is None:
            sampler['input'] = still_times
            continue
        cut = keys[first:last + 1].copy()
        if path == 'rotation':
            turns = Rot.from_quat(cut[0]) * Rot.from_quat(cut[-1]).inv()   # what turns the last key onto the first
            spread = Slerp([0, 1], Rot.from_quat([[0, 0, 0, 1], turns.as_quat()]))(u)
            cut = (spread * Rot.from_quat(cut)).as_quat()
            cut *= np.sign((cut * keys[first:last + 1]).sum(-1, keepdims=True) + 1e-12)   # keep each key's hemisphere
        elif path == 'translation' and name == a.top:
            moved = cut[-1] - cut[0]
            travel = int(np.argmax(np.abs(moved))) if np.abs(moved).max() >= 0.5 else None
            for axis in range(3):
                if axis != travel:
                    cut[:, axis] -= u * moved[axis]
                    detrended['xyz'[axis]] = round(float(moved[axis]), 4)
        sampler['input'] = cut_times
        sampler['output'] = add(cut, {3: 'VEC3', 4: 'VEC4'}[cut.shape[1]])
    gltf['buffers'] = [{'byteLength': len(data)}]
    write_glb(a.out, gltf, data)
    print(json.dumps({'clip': a.clip.name, 'window': [first, last], 'seconds': round(float(times[last] - times[first]), 4),
                      'seam_deg': round(seam, 2), 'top_detrended': detrended}))


if __name__ == '__main__':
    main()
