#!/usr/bin/env python3
"""A static NPC's own clips for the Engine: static_idle.py's idle and look on the 22 Mixamo joints of its skinned splat GLB.

    static_clips.py CHARACTER.glb SCAFFOLD.glb OUT_DIR ID [--clip idle --clip look] [--fps 30] [--turn 20] [--turn-back 15]

CHARACTER.glb is bound_to_glb.py's GLB of a static NPC (asset.extras.static: the short chain Hips..Head and the hands weighted,
the arm and leg joints placed with no weight); SCAFFOLD.glb is the short rig it was bound to (static_rig.py, metres, floor at 0).
Writes OUT_DIR/ID-CLIP.glb for each clip, a clip pack as the art pack's are: the character's 22 joints (mixamorig: names, its own
bind rest), a skin over them and one animation named CLIP, no mesh; and OUT_DIR/ID-clips.json (the checks).

The motion is static_idle.py's, frame for frame (breathing, the sway about the floor under the hips, the head drifting, the look
round): each chain joint's world change is taken as it is. The hands move with Hips there, so a staff held in them stays on its
shaft; here each hand hangs off its arm, so the clip bends each arm (two bones, the elbow bending the way it does at rest; the
shoulder rides Spine2) to put the hand where Hips takes it, and gives the hand the rotation Hips gives it (hand_error_mm: the miss). The legs follow Hips. Every joint has a rotation channel; only
Hips has translation (its sway about the floor), fit k = 1 and A = 0: the clip is in this character's own metres, not X Bot's.
"""
import argparse
import json
import struct
import sys
from pathlib import Path
from types import SimpleNamespace

import numpy as np
from scipy.spatial.transform import Rotation as Rot

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from retarget_clip import load_rig        # noqa: E402
import static_idle                        # noqa: E402

PREFIX = 'mixamorig:'
CHAIN = ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head')


def read_glb(path):
    d = Path(path).read_bytes(); n = struct.unpack_from('<I', d, 12)[0]
    return json.loads(d[20:20 + n])


def trs(node):
    m = np.eye(4)
    m[:3, :3] = Rot.from_quat(node.get('rotation', [0, 0, 0, 1])).as_matrix(); m[:3, 3] = node.get('translation', [0, 0, 0])
    return m


def minimal_rotation(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    ax = np.cross(a, b); s = np.linalg.norm(ax)
    return np.eye(3) if s < 1e-12 else Rot.from_rotvec(ax / s * np.arctan2(s, float(a @ b))).as_matrix()


def skeleton(doc):
    """The character GLB's joints: names (without the prefix), parent index, world bind."""
    joints = doc['skins'][0]['joints']; at = {j: k for k, j in enumerate(joints)}
    parent = {c: i for i, nd in enumerate(doc['nodes']) for c in nd.get('children', [])}
    names = [doc['nodes'][j]['name'].removeprefix(PREFIX) for j in joints]
    par = [at.get(parent.get(j), -1) for j in joints]
    W = [None] * len(joints)

    def world(k):
        if W[k] is None:
            W[k] = (world(par[k]) if par[k] >= 0 else np.eye(4)) @ trs(doc['nodes'][joints[k]])
        return W[k]
    for k in range(len(joints)):
        world(k)
    return names, par, np.array(W), [doc['nodes'][j] for j in joints]


def clip_worlds(names, par, W, D, sidx):
    """Per frame, every joint's world matrix: chain joints take their static_idle.py change, the legs Hips', each arm turned about
    its shoulder (which rides Spine2) by a two-bone solve so its hand lands where Hips takes it. Returns worlds and the hands' miss in metres."""
    ix = {n: i for i, n in enumerate(names)}; nf = len(D)
    out = np.tile(np.eye(4), (nf, len(names), 1, 1)); miss = []
    follow = {n: n for n in CHAIN}
    for s in ('Left', 'Right'):
        follow.update({f'{s}UpLeg': 'Hips', f'{s}Leg': 'Hips', f'{s}Foot': 'Hips', f'{s}ToeBase': 'Hips', f'{s}Shoulder': 'Spine2'})
    for f in range(nf):
        for n, src in follow.items():
            out[f, ix[n]] = D[f, sidx[src]] @ W[ix[n]]
        for s in ('Left', 'Right'):
            sh, arm, fore, hand = (ix[f'{s}{b}'] for b in ('Shoulder', 'Arm', 'ForeArm', 'Hand'))
            Dh = D[f, sidx[f'{s}Hand']]
            want = Dh[:3, :3] @ W[hand][:3, 3] + Dh[:3, 3]
            S = out[f, sh]                                                     # the shoulder as Spine2 carries it
            A = S @ np.linalg.inv(W[sh]) @ W[arm]; F = A @ np.linalg.inv(W[arm]) @ W[fore]; H = F @ np.linalg.inv(W[fore]) @ W[hand]
            a_, e0, h0 = A[:3, 3], F[:3, 3], H[:3, 3]
            l1, l2 = np.linalg.norm(e0 - a_), np.linalg.norm(h0 - e0)
            t = want - a_; d = np.clip(np.linalg.norm(t), abs(l1 - l2) + 1e-6, l1 + l2 - 1e-6); th = t / np.linalg.norm(t)
            b = (e0 - a_) - ((e0 - a_) @ th) * th; b /= np.linalg.norm(b)        # the elbow bends the way it does at rest
            ca = (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d); e = a_ + l1 * (ca * th + np.sqrt(max(0.0, 1 - ca * ca)) * b)
            A = A.copy(); A[:3, :3] = minimal_rotation(e0 - a_, e - a_) @ A[:3, :3]; out[f, arm] = A
            F = A @ np.linalg.inv(W[arm]) @ W[fore]
            F[:3, :3] = minimal_rotation((F @ np.linalg.inv(W[fore]) @ W[hand])[:3, 3] - F[:3, 3], want - F[:3, 3]) @ F[:3, :3]; out[f, fore] = F
            H = F @ np.linalg.inv(W[fore]) @ W[hand]                           # where the arm now puts the hand
            miss.append(float(np.linalg.norm(H[:3, 3] - want)))
            H[:3, :3] = Dh[:3, :3] @ W[hand][:3, :3]                           # with the turn Hips gives it
            out[f, hand] = H
    return out, miss


def write_pack(path, clip, names, par, W, nodes, worlds, fps):
    nf = len(worlds); nj = len(names)
    doc = {'asset': {'version': '2.0', 'generator': 'asset-pipeline static_clips.py',
                     'extras': {'clip': clip, 'fit': {'k': 1.0, 'A': 0.0}, 'note': "a static NPC's own clip, in its own metres"}},
           'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [{'name': 'character', 'children': [1]}], 'skins': [], 'animations': [],
           'accessors': [], 'bufferViews': [], 'buffers': []}
    blob = bytearray()

    def acc(arr, kind, minmax=False):
        while len(blob) % 4:
            blob.append(0)
        raw = np.ascontiguousarray(arr, np.float32).tobytes()
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(raw)}); blob.extend(raw)
        a = {'bufferView': len(doc['bufferViews']) - 1, 'componentType': 5126, 'count': len(arr), 'type': kind}
        if minmax:
            a['min'] = [float(x) for x in np.min(arr, 0).ravel()]; a['max'] = [float(x) for x in np.max(arr, 0).ravel()]
        doc['accessors'].append(a); return len(doc['accessors']) - 1
    kids = [[] for _ in range(nj)]
    for k, p in enumerate(par):
        if p >= 0:
            kids[p].append(1 + k)
    for k, n in enumerate(names):
        nd = {'name': PREFIX + n, 'translation': nodes[k].get('translation', [0, 0, 0]), 'rotation': nodes[k].get('rotation', [0, 0, 0, 1])}
        if kids[k]:
            nd['children'] = kids[k]
        doc['nodes'].append(nd)
    ibm = np.array([np.linalg.inv(w) for w in W]).transpose(0, 2, 1).reshape(-1, 16)
    doc['skins'].append({'name': 'body', 'joints': list(range(1, nj + 1)), 'skeleton': 1, 'inverseBindMatrices': acc(ibm, 'MAT4')})
    times = acc((np.arange(nf) / fps).reshape(-1, 1), 'SCALAR', minmax=True)
    samplers, channels = [], []
    for k in range(nj):
        local = np.array([(np.linalg.inv(worlds[f, par[k]]) if par[k] >= 0 else np.eye(4)) @ worlds[f, k] for f in range(nf)])
        q = Rot.from_matrix(local[:, :3, :3]).as_quat()
        for f in range(1, nf):                                                 # one hemisphere, so LINEAR blends the short way
            if q[f] @ q[f - 1] < 0:
                q[f] = -q[f]
        samplers.append({'input': times, 'output': acc(q, 'VEC4'), 'interpolation': 'LINEAR'})
        channels.append({'sampler': len(samplers) - 1, 'target': {'node': 1 + k, 'path': 'rotation'}})
        if par[k] < 0:
            samplers.append({'input': times, 'output': acc(local[:, :3, 3], 'VEC3'), 'interpolation': 'LINEAR'})
            channels.append({'sampler': len(samplers) - 1, 'target': {'node': 1 + k, 'path': 'translation'}})
    doc['animations'].append({'name': clip, 'samplers': samplers, 'channels': channels})
    while len(blob) % 4:
        blob.append(0)
    doc['buffers'].append({'byteLength': len(blob)})
    text = json.dumps(doc, separators=(',', ':')).encode(); text += b' ' * ((-len(text)) % 4)
    data = (b'glTF' + struct.pack('<II', 2, 12 + 8 + len(text) + 8 + len(blob)) + struct.pack('<I', len(text)) + b'JSON' + text
            + struct.pack('<I', len(blob)) + b'BIN\0' + bytes(blob))
    tmp = Path(str(path) + '.part'); tmp.write_bytes(data); tmp.replace(path)
    return len(data)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('character'); ap.add_argument('scaffold'); ap.add_argument('out', type=Path); ap.add_argument('id')
    ap.add_argument('--clip', action='append', choices=sorted(static_idle.LENGTH)); ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--turn', type=float, default=20.0); ap.add_argument('--turn-back', type=float, default=15.0)
    a = ap.parse_args()
    doc = read_glb(a.character)
    if not doc['asset'].get('extras', {}).get('static'):
        raise SystemExit(f'{a.character}: not a static NPC GLB (bound_to_glb.py writes asset.extras.static for a short chain)')
    names, par, W, nodes = skeleton(doc)
    rig = load_rig(a.scaffold, 1.0)
    sidx = {n: i for i, n in enumerate(rig['names'])}
    err = max(float(np.abs(rig['bind'][sidx[n]] - W[names.index(n)][:3, 3]).max()) for n in CHAIN + ('LeftHand', 'RightHand'))
    if err > 1e-4:
        raise SystemExit(f'{a.scaffold}: joints {err * 1000:.2f} mm from the GLB\'s: not the rig it was written from')
    a.out.mkdir(parents=True, exist_ok=True); rep = {'character': a.character, 'scaffold': a.scaffold, 'fps': a.fps, 'clips': {}}
    for clip in a.clip or ['idle', 'look']:
        D, _ = static_idle.poses(rig, clip, SimpleNamespace(fps=a.fps, turn=a.turn, turn_back=a.turn_back))
        worlds, miss = clip_worlds(names, par, W, D, sidx)
        out = a.out / f'{a.id}-{clip}.glb'
        size = write_pack(out, clip, names, par, W, nodes, worlds, a.fps)
        head = worlds[:, names.index('Head')]
        yaw = np.degrees(np.arctan2(head[:, 0, 2], head[:, 2, 2]) - np.arctan2(W[names.index('Head')][0, 2], W[names.index('Head')][2, 2]))
        rep['clips'][clip] = {'file': out.name, 'frames': len(worlds), 'seconds': static_idle.LENGTH[clip], 'bytes': size,
                              'hand_error_mm_max': round(1000 * max(miss), 3), 'head_yaw_deg_range': [round(float(yaw.min()), 1), round(float(yaw.max()), 1)],
                              'loops': bool(np.allclose(worlds[0], worlds[-1], atol=1e-5))}
    (a.out / f'{a.id}-clips.json').write_text(json.dumps(rep, indent=1) + '\n')
    print(json.dumps(rep['clips']))


if __name__ == '__main__':
    main()
