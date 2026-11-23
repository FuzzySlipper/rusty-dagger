#!/usr/bin/env python3
"""Write a rigged splat character (VIGGCHAR v1 .vsplat) as a skinned splat GLB for the Engine.

  python3 -I vsplat_to_glb.py CHARACTER.vsplat OUT.glb [--rig mixamo|viggle441] [--mixamo-reference MIXAMO.glb]
      [--skeleton .../skeleton-data-441.ts] [--check-ply REST.ply] [--expect-joints N] [--surface SURFACE.npz]

--rig mixamo (the default) writes the 22 Mixamo body bones (mixamorig:Hips ...) in
the character's own bind, framed on X Bot so Mixamo clips play by name; see
mixamo_skin(). --rig viggle441 writes the 441-rig contract below, unchanged.

This is the asset rusty-engine #10805 admits (spec: Den task asset-pipeline
#10813). The Engine does not read .vsplat or carry Viggle's skeleton tables,
so this step supplies names, parents and bind rotations from the SDK's
canonical 441-joint tables (`npm run inspect:sdk` extracts them).

The GLB:
- GLB 2.0, one embedded BIN chunk, no URIs, materials or animations.
- +Y up, metres, soles on y = 0, in the .vsplat's own space: the bind faces
  +X (left -Z). An untransformed non-joint node `character` at the scene root
  is the parent of `root`. Every PINOC 441 clip holds root turned 180 deg
  about Y, which is what makes the played character face and walk +Z; a turn
  node would make him face -X (corrected spec, rusty-engine #10805 P2).
- One skin. Its joints are every bone with a non-zero weight plus all their
  ancestors up to `root`, in canonical order, with canonical names and
  PARENTS_441 restricted to that set. Each joint node's rest: translation =
  the bone's row of the .vsplat `joints` block (local, parent frame),
  rotation = REST_ROTATIONS_441 (xyzw), scale 1. inverseBindMatrices are the
  inverses of each joint's world rest.
- Mesh `body`, drawn by scene-root node `body` with the skin and an identity
  transform: one POINTS primitive in the bind space with
  POSITION (float VEC3, min/max), _ROTATION (float VEC4 xyzw, unit),
  _SCALE (float VEC3, sigma in metres, exp of the stored log scale),
  _COLOR (u8 normalized VEC4: the .vsplat colour bytes unchanged, alpha
  round(sigmoid(logit) * 255)), JOINTS_0 (u8 VEC4, indices into skin.joints)
  and WEIGHTS_0 (u8 normalized VEC4, summing to 255).
- With --surface (splat_surface_normals.py's npz for the same .vsplat; asset-pipeline
  #10820, rusty-engine #10815 lighting), two optional attributes as well:
  _NORMAL (float VEC3, unit, bind space like POSITION, out of the figure) and
  _ALBEDO (u8 normalized VEC4: sRGB colour with the bake's fitted shading
  divided out; A copies _COLOR's alpha and is unread).
- Pelvis fits, arm offsets and palettes are product data and stay out.

Checks run on every write (the step fails if one fails): the GLB is read
back from its bytes by an independent reader and checked structurally
(chunks, alignment, accessor bounds and counts, min/max, unit quaternions,
weight sums, joint indices, parents before children, IBM x world rest =
identity); posing the rest skin (and undoing the Mixamo rig's baked facing turn) reproduces the .vsplat's
decoded rest Gaussians. --check-ply compares against inspect_vsplat.py's
rest.ply as well.
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np
from scipy.spatial.transform import Rotation as Rot

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from humanoid_splat_rig import DEFAULT_SKELETON, load_skeleton  # noqa: E402

ARRAY_BUFFER = 34962
FLOAT, UBYTE = 5126, 5121
# The .vsplat faces +X. The Mixamo rig bakes a -90 deg turn about +Y into its rest, taking +X to +Z (and the left,
# -Z, to +X), so Mixamo clips face +Z. The 441 rig keeps the .vsplat's space: its clips' 180 deg root turn faces him +Z.
TURN = Rot.from_euler('y', -90, degrees=True)


# ---------------------------------------------------------------- reading the .vsplat

def read_vsplat(path):
    """Decode a VIGGCHAR v1 file exactly as inspect_vsplat.py (and the SDK's decode.ts) do."""
    data = open(path, 'rb').read()
    if data[:8] != b'VIGGCHAR' or struct.unpack_from('<I', data, 8)[0] != 1:
        raise SystemExit(f'{path}: not a VIGGCHAR v1 file')
    json_length = struct.unpack_from('<I', data, 16)[0]
    directory = json.loads(data[20:20 + json_length])
    start = 20 + json_length
    n = directory['numSplats']; blocks = directory['blocks']
    for name in ('position', 'scale', 'rotation', 'color', 'opacity', 'boneIndex', 'boneWeight', 'joints'):
        if name not in blocks:
            raise SystemExit(f'{path}: no {name} block; only rigged characters convert')

    def planes(name):
        b = blocks[name]
        dtype = {'u8': np.uint8, 'u16': np.uint16, 'f32': np.float32}[b['dtype']]
        raw = np.frombuffer(data, dtype, count=b['components'] * b['count'], offset=start + b['offset'])
        return raw.reshape(b['components'], b['count']).T

    def dequantize(name):
        b = blocks[name]; levels = 65535 if b['dtype'] == 'u16' else 255
        lo = np.array(b['min'], np.float64); hi = np.array(b['max'], np.float64)
        return lo + planes(name).astype(np.float64) * ((hi - lo) / levels)

    xyz = planes('rotation').astype(np.float64) / 255 * 2 - 1
    w = np.sqrt(np.maximum(0, 1 - (xyz ** 2).sum(1)))
    stored = planes('boneWeight').astype(np.int64)
    return {
        'n': n,
        'position': dequantize('position'),
        'log_scale': dequantize('scale'),
        'quat_xyzw': np.column_stack([xyz, w]),
        'color': planes('color').copy(),
        'logit': dequantize('opacity')[:, 0],
        'bones': np.array(blocks['boneIndex']['palette'], np.int64)[planes('boneIndex').astype(np.int64)],
        'weights': np.column_stack([stored, np.maximum(0, 255 - stored.sum(1))]),
        'joints': planes('joints').astype(np.float64),
    }


# ---------------------------------------------------------------- the rig

def matrix(q_xyzw, t):
    m = np.eye(4); m[:3, :3] = Rot.from_quat(q_xyzw).as_matrix(); m[:3, 3] = t
    return m


def pruned_skin(names, parents, rest_rots, v, turn_rot=None):
    """Weighted bones plus their ancestors, in canonical order, with local rests and world rests (turned by turn_rot)."""
    turn_rot = turn_rot or Rot.identity()
    if len(v['joints']) != len(names):
        raise SystemExit(f"joints block has {len(v['joints'])} rows; the canonical skeleton has {len(names)}")
    if any(p >= i for i, p in enumerate(parents)):
        raise SystemExit('PARENTS_441 is not parent-before-child; the pruned order would need a sort')
    used = set(np.unique(v['bones'][v['weights'] > 0]).tolist())
    keep = set()
    for b in used:
        while b >= 0 and b not in keep:
            keep.add(b); b = parents[b]
    order = sorted(keep)
    if parents[order[0]] != -1:
        raise SystemExit('pruned skeleton does not reach the root')
    index = {b: i for i, b in enumerate(order)}
    turn = np.eye(4); turn[:3, :3] = turn_rot.as_matrix()
    world = []
    for b in order:
        local = matrix(rest_rots[b], v['joints'][b])
        world.append((turn if parents[b] < 0 else world[index[parents[b]]]) @ local)
    return {
        'order': order, 'index': index, 'names': [names[b] for b in order],
        'parents': [index[parents[b]] if parents[b] >= 0 else -1 for b in order],
        'translation': [v['joints'][b] for b in order], 'rotation': [rest_rots[b] for b in order],
        'world': np.array(world), 'character_rotation': turn_rot.as_quat(), 'bind_turn': turn_rot,
        'feet': ('foot_l', 'foot_r', 'ball_l', 'ball_r'), 'rig': 'viggle441',
        'bone_to_joint': dict(index),
    }


# ---------------------------------------------------------------- Mixamo target

# name, parent, 441 bone whose bind position the joint takes, 441 bones whose weights it takes, aim child.
# The merge is the #10813 contract: root and pelvis to Hips, spine_01-02 to Spine, spine_03 to Spine1,
# spine_04-05 to Spine2, neck_01-02 to Neck, clavicle to Shoulder, ball to ToeBase. Each Mixamo joint sits
# at the first 441 bone it takes, so the Gaussians it carries hang below its children, as they did in the 441 rig.
MIXAMO = [
    ('Hips', None, 'pelvis', ['root', 'pelvis'], 'Spine'),
    ('Spine', 'Hips', 'spine_01', ['spine_01', 'spine_02'], 'Spine1'),
    ('Spine1', 'Spine', 'spine_03', ['spine_03'], 'Spine2'),
    ('Spine2', 'Spine1', 'spine_04', ['spine_04', 'spine_05'], 'Neck'),
    ('Neck', 'Spine2', 'neck_01', ['neck_01', 'neck_02'], 'Head'),
    ('Head', 'Neck', 'head', ['head'], None),
]
for side, x in (('Left', 'l'), ('Right', 'r')):
    MIXAMO += [
        (f'{side}Shoulder', 'Spine2', f'clavicle_{x}', [f'clavicle_{x}'], f'{side}Arm'),
        (f'{side}Arm', f'{side}Shoulder', f'upperarm_{x}', [f'upperarm_{x}'], f'{side}ForeArm'),
        (f'{side}ForeArm', f'{side}Arm', f'lowerarm_{x}', [f'lowerarm_{x}'], f'{side}Hand'),
        (f'{side}Hand', f'{side}ForeArm', f'hand_{x}', [f'hand_{x}'], None),
        (f'{side}UpLeg', 'Hips', f'thigh_{x}', [f'thigh_{x}'], f'{side}Leg'),
        (f'{side}Leg', f'{side}UpLeg', f'calf_{x}', [f'calf_{x}'], f'{side}Foot'),
        (f'{side}Foot', f'{side}Leg', f'foot_{x}', [f'foot_{x}'], f'{side}ToeBase'),
        (f'{side}ToeBase', f'{side}Foot', f'ball_{x}', [f'ball_{x}'], None),
    ]
PREFIX = 'mixamorig:'   # exactly as PINOC's mixamo-glb clips name their bones; the Engine binds names exactly
# PINOC's free library Walking as mixamo-glb, kept on den-agents only (PINOC clips are never committed or packaged).
MIXAMO_REFERENCE = '/data/assets/splat-characters/pinoc-clips/mixamo/walking.glb'


def glb_node_worlds(path):
    """Names and world matrices of every node of a GLB (wrapper nodes included), from its node TRS."""
    data = open(path, 'rb').read()
    doc = json.loads(data[20:20 + struct.unpack_from('<I', data, 12)[0]])
    nodes = doc['nodes']
    parent = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    world = {}

    def node_world(i):
        if i not in world:
            n = nodes[i]
            m = np.array(n['matrix'], np.float64).reshape(4, 4).T if 'matrix' in n else \
                matrix(n.get('rotation', [0, 0, 0, 1]), n.get('translation', [0, 0, 0])) @ np.diag([*n.get('scale', [1, 1, 1]), 1])
            world[i] = m if i not in parent else node_world(parent[i]) @ m
        return world[i]
    return {n.get('name'): node_world(i) for i, n in enumerate(nodes)}


def minimal_rotation(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    axis = np.cross(a, b); s = np.linalg.norm(axis); c = float(a @ b)
    if s < 1e-12:
        return np.eye(3)
    return Rot.from_rotvec(axis / s * math.atan2(s, c)).as_matrix()


def mixamo_skin(names, parents, rest_rots, v, reference):
    """A Mixamo skeleton in the character's own bind pose, with X Bot's joint frames.

    The 441 world rest (turned to face +Z) gives each joint's position. Each joint's frame is X Bot's world
    rest frame (from the reference Mixamo GLB, its Z-up wrapper included, so Y-up) turned by the smallest
    rotation that takes X Bot's bone (joint to aim child) onto the character's; a joint with no aim child
    keeps X Bot's frame relative to its parent. Local +Y then runs along each bone as in Mixamo, and clips
    authored on X Bot set local rotations that mean the same thing here. The character node is identity:
    a turn above Hips would add a yaw to every clip's Hips rotation."""
    base = pruned_skin(names, parents, rest_rots, v, TURN)
    P441 = {n: base['world'][i][:3, 3] for i, n in enumerate(base['names'])}
    pos = {name: P441[bone] for name, _, bone, _, _ in MIXAMO}
    order, index, translation, rotation, world, hips_ref = mixamo_frames(pos, reference)
    by_441 = {names.index(b): index[name] for name, _, _, merged, _ in MIXAMO for b in merged}
    unmapped = set(np.unique(v['bones'][v['weights'] > 0]).tolist()) - set(by_441)
    if unmapped:
        raise SystemExit('weighted 441 bones with no Mixamo joint: ' + ', '.join(names[b] for b in sorted(unmapped)))
    return {
        'order': order, 'index': index, 'names': [PREFIX + n for n in order],
        'parents': [index[p] if p else -1 for _, p, *_ in MIXAMO],
        'translation': translation, 'rotation': rotation, 'world': np.array(world),
        'character_rotation': np.array([0.0, 0.0, 0.0, 1.0]), 'bind_turn': TURN,
        'feet': tuple(PREFIX + n for n in ('LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase')), 'rig': 'mixamo',
        'bone_to_joint': by_441,
        'hips_height_ratio': float(pos['Hips'][1] / hips_ref),
    }


def mixamo_frames(pos, reference):
    """The MIXAMO joints at positions pos (name -> world position, facing +Z) with X Bot's frames turned onto the
    character's bones (see mixamo_skin): order, index, local translations and rotations (xyzw), world rests, X Bot's hips height."""
    ref = glb_node_worlds(reference)
    X = {}
    for name, *_ in MIXAMO:
        if PREFIX + name not in ref:
            raise SystemExit(f'{reference}: no {PREFIX}{name}')
        m = ref[PREFIX + name]
        u, _, vt = np.linalg.svd(m[:3, :3])                       # strip any scale from the reference
        X[name] = (u @ vt, m[:3, 3])
    frame = {}
    for name, parent, _, _, aim in MIXAMO:                         # parents come first in MIXAMO
        if aim:
            frame[name] = minimal_rotation(X[aim][1] - X[name][1], pos[aim] - pos[name]) @ X[name][0]
        else:
            frame[name] = frame[parent] @ (X[parent][0].T @ X[name][0])
    order = [m[0] for m in MIXAMO]
    index = {n: i for i, n in enumerate(order)}
    world, translation, rotation = [], [], []
    for name, parent, *_ in MIXAMO:
        w = np.eye(4); w[:3, :3] = frame[name]; w[:3, 3] = pos[name]
        world.append(w)
        local = w if parent is None else np.linalg.inv(world[index[parent]]) @ w
        translation.append(local[:3, 3]); rotation.append(Rot.from_matrix(local[:3, :3]).as_quat())
    return order, index, translation, rotation, world, X['Hips'][1][1]


def splat_attributes(v, skin):
    """Every attribute in the skin's bind space (turned for Mixamo, the .vsplat's own for 441), in the spec's encodings.
    _NORMAL and _ALBEDO only when v carries a surface (read_surface)."""
    turn = skin['bind_turn']
    position = turn.apply(v['position']).astype(np.float32)
    rotation = (turn * Rot.from_quat(v['quat_xyzw'])).as_quat()          # xyzw, scipy normalises
    rotation = (rotation * np.where(rotation[:, 3:] < 0, -1, 1)).astype(np.float32)
    scale = np.exp(v['log_scale']).astype(np.float32)
    alpha = np.rint(255 / (1 + np.exp(-v['logit'])))
    color = np.column_stack([v['color'], alpha]).astype(np.uint8)
    lut = np.full(int(v['bones'].max()) + 1, -1, np.int64)
    for bone, joint in skin['bone_to_joint'].items():
        if bone < len(lut):
            lut[bone] = joint
    W = v['weights'].astype(np.int64).copy()
    mapped = np.where(W > 0, lut[v['bones']], -1)
    if (mapped[W > 0] < 0).any():
        raise SystemExit('a weighted bone has no joint in the skin')
    # Slots that land on one joint (several 441 bones merged onto one Mixamo joint) become one slot.
    for i in range(4):
        for k in range(i + 1, 4):
            same = (mapped[:, k] == mapped[:, i]) & (mapped[:, i] >= 0)
            W[:, i] += np.where(same, W[:, k], 0)
            W[:, k] = np.where(same, 0, W[:, k]); mapped[:, k] = np.where(same, -1, mapped[:, k])
    order = np.argsort(-W, axis=1, kind='stable')
    W = np.take_along_axis(W, order, 1); mapped = np.take_along_axis(mapped, order, 1)
    # A zero-weight slot may name a bone outside the skin; it points at joint 0 with weight 0.
    joints = np.where(W > 0, mapped, 0).astype(np.uint8)
    weights = W.astype(np.uint8)
    out = {'POSITION': position, '_ROTATION': rotation, '_SCALE': scale, '_COLOR': color,
           'JOINTS_0': joints, 'WEIGHTS_0': weights}
    if 'normal' in v:
        normal = turn.apply(v['normal'])
        out['_NORMAL'] = (normal / np.linalg.norm(normal, axis=1, keepdims=True)).astype(np.float32)
        out['_ALBEDO'] = np.column_stack([v['albedo'], alpha]).astype(np.uint8)
    return out


def read_surface(path, v):
    """splat_surface_normals.py's npz: per .vsplat row, a unit normal in the .vsplat's space and an sRGB albedo."""
    z = np.load(path)
    normal = z['normal'].astype(np.float64); albedo = z['albedo']
    if len(normal) != v['n'] or albedo.shape != (v['n'], 3):
        raise SystemExit(f'{path}: {len(normal)} rows, the .vsplat has {v["n"]}')
    length = np.linalg.norm(normal, axis=1)
    if not (np.abs(length - 1) < 2e-3).all():
        raise SystemExit(f'{path}: normals not unit')
    v['normal'] = normal / length[:, None]; v['albedo'] = albedo


# ---------------------------------------------------------------- writing the GLB

def build_glb(skin, attrs, extras):
    doc = {'asset': {'version': '2.0', 'generator': 'asset-pipeline vsplat_to_glb.py', 'extras': extras},
           'scene': 0, 'scenes': [{'nodes': []}], 'nodes': [], 'meshes': [], 'skins': [],
           'accessors': [], 'bufferViews': [], 'buffers': []}
    blob = bytearray()

    def view(array, target=None):
        while len(blob) % 4:
            blob.append(0)
        raw = np.ascontiguousarray(array).tobytes()
        entry = {'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(raw)}
        if target:
            entry['target'] = target
        doc['bufferViews'].append(entry); blob.extend(raw)
        return len(doc['bufferViews']) - 1

    def accessor(array, kind, normalized=False, bounds=False, target=ARRAY_BUFFER):
        component = FLOAT if array.dtype == np.float32 else UBYTE
        entry = {'bufferView': view(array, target), 'componentType': component, 'count': len(array), 'type': kind}
        if normalized:
            entry['normalized'] = True
        if bounds:
            entry['min'] = [float(x) for x in array.min(0)]; entry['max'] = [float(x) for x in array.max(0)]
        doc['accessors'].append(entry)
        return len(doc['accessors']) - 1

    # Nodes: 0 character, 1 body (mesh + skin), 2.. joints.
    first_joint = 2
    character = {'name': 'character', 'children': [first_joint]}
    if not np.allclose(skin['character_rotation'], [0, 0, 0, 1]):
        character['rotation'] = [float(x) for x in skin['character_rotation']]
    doc['nodes'].append(character)
    doc['nodes'].append({'name': 'body', 'mesh': 0, 'skin': 0})
    children = [[] for _ in skin['names']]
    for i, p in enumerate(skin['parents']):
        if p >= 0:
            children[p].append(first_joint + i)
    for i, name in enumerate(skin['names']):
        node = {'name': name, 'translation': [float(x) for x in skin['translation'][i]],
                'rotation': [float(x) for x in skin['rotation'][i]]}
        if children[i]:
            node['children'] = children[i]
        doc['nodes'].append(node)
    doc['scenes'][0]['nodes'] = [0, 1]

    inverse = np.array([np.linalg.inv(w) for w in skin['world']])
    ibm = inverse.transpose(0, 2, 1).reshape(-1, 16).astype(np.float32)     # column-major
    doc['skins'].append({'name': 'body', 'joints': [first_joint + i for i in range(len(skin['names']))],
                         'skeleton': first_joint, 'inverseBindMatrices': accessor(ibm, 'MAT4', target=None)})

    attributes = {
        'POSITION': accessor(attrs['POSITION'], 'VEC3', bounds=True),
        '_ROTATION': accessor(attrs['_ROTATION'], 'VEC4'),
        '_SCALE': accessor(attrs['_SCALE'], 'VEC3'),
        '_COLOR': accessor(attrs['_COLOR'], 'VEC4', normalized=True),
        'JOINTS_0': accessor(attrs['JOINTS_0'], 'VEC4'),
        'WEIGHTS_0': accessor(attrs['WEIGHTS_0'], 'VEC4', normalized=True),
    }
    if '_NORMAL' in attrs:
        attributes['_NORMAL'] = accessor(attrs['_NORMAL'], 'VEC3')
        attributes['_ALBEDO'] = accessor(attrs['_ALBEDO'], 'VEC4', normalized=True)
    doc['meshes'].append({'name': 'body', 'primitives': [{'attributes': attributes, 'mode': 0}]})
    while len(blob) % 4:
        blob.append(0)
    doc['buffers'].append({'byteLength': len(blob)})

    text = json.dumps(doc, separators=(',', ':')).encode()
    text += b' ' * ((-len(text)) % 4)
    total = 12 + 8 + len(text) + 8 + len(blob)
    return (b'glTF' + struct.pack('<II', 2, total) + struct.pack('<I', len(text)) + b'JSON' + text
            + struct.pack('<I', len(blob)) + b'BIN\0' + bytes(blob))


# ---------------------------------------------------------------- checks (from the bytes)

def read_glb(data):
    """An independent reader of what was written: structure first, then typed arrays."""
    magic, version, total = struct.unpack_from('<4sII', data, 0)
    assert magic == b'glTF' and version == 2 and total == len(data), 'GLB header'
    json_len, json_type = struct.unpack_from('<I4s', data, 12)
    assert json_type == b'JSON' and json_len % 4 == 0, 'JSON chunk'
    doc = json.loads(data[20:20 + json_len])
    at = 20 + json_len
    bin_len, bin_type = struct.unpack_from('<I4s', data, at)
    assert bin_type == b'BIN\0' and bin_len % 4 == 0 and at + 8 + bin_len == len(data), 'one BIN chunk, last'
    binary = data[at + 8:]
    assert len(doc['buffers']) == 1 and 'uri' not in doc['buffers'][0] and doc['buffers'][0]['byteLength'] <= bin_len
    for key in ('materials', 'animations', 'images', 'textures', 'cameras'):
        assert key not in doc, f'unexpected {key}'
    size = {FLOAT: 4, UBYTE: 1}; width = {'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

    def accessor(i):
        a = doc['accessors'][i]; v = doc['bufferViews'][a['bufferView']]
        assert 'byteStride' not in v and v['byteOffset'] % 4 == 0
        n = a['count'] * width[a['type']]
        assert v['byteOffset'] + a.get('byteOffset', 0) + n * size[a['componentType']] <= v['byteOffset'] + v['byteLength']
        assert v['byteOffset'] + v['byteLength'] <= doc['buffers'][0]['byteLength']
        dtype = np.float32 if a['componentType'] == FLOAT else np.uint8
        return a, np.frombuffer(binary, dtype, count=n, offset=v['byteOffset'] + a.get('byteOffset', 0)).reshape(a['count'], -1)
    return doc, accessor


def check(data, v, skin, expected_joints=None, ply=None):
    doc, accessor = read_glb(data)
    report = {}
    nodes = doc['nodes']
    scene = doc['scenes'][doc['scene']]['nodes']
    char = next(i for i in scene if nodes[i]['name'] == 'character')
    body = next(i for i in scene if nodes[i].get('skin') is not None)
    assert 'mesh' not in nodes[char] and 'skin' not in nodes[char]
    assert all(k not in nodes[body] for k in ('translation', 'rotation', 'scale', 'matrix')), 'body node not identity'
    s = doc['skins'][0]; joints = s['joints']
    assert len(doc['skins']) == 1 and char not in joints, 'turn sits on a joint'
    assert nodes[char]['children'] == [joints[0]] and nodes[joints[0]]['name'] == skin['names'][0]
    if expected_joints is not None:
        assert len(joints) == expected_joints, f'{len(joints)} joints, expected {expected_joints}'
    names = [nodes[j]['name'] for j in joints]
    assert len(set(names)) == len(names) and names == skin['names']
    parent = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}
    for k, j in enumerate(joints[1:], 1):
        assert parent[j] in joints[:k], f'{names[k]}: parent not an earlier joint'
    # World rests from the nodes (TRS), the character node included, against the IBMs.
    world = {}

    def node_world(i):
        if i not in world:
            n = nodes[i]
            m = matrix(n.get('rotation', [0, 0, 0, 1]), n.get('translation', [0, 0, 0]))
            world[i] = m if i not in parent else node_world(parent[i]) @ m
        return world[i]
    _, ibm = accessor(s['inverseBindMatrices'])
    ibm = ibm.reshape(-1, 4, 4).transpose(0, 2, 1).astype(np.float64)
    palette = np.array([node_world(j) @ ibm[k] for k, j in enumerate(joints)])
    report['ibm_identity_max_error'] = float(np.abs(palette - np.eye(4)).max())
    assert report['ibm_identity_max_error'] < 1e-5

    mesh = doc['meshes'][nodes[body]['mesh']]
    assert mesh['name'] == 'body' and len(mesh['primitives']) == 1
    prim = mesh['primitives'][0]
    spec = {'POSITION': (FLOAT, 'VEC3', False), '_ROTATION': (FLOAT, 'VEC4', False), '_SCALE': (FLOAT, 'VEC3', False),
            '_COLOR': (UBYTE, 'VEC4', True), 'JOINTS_0': (UBYTE, 'VEC4', False), 'WEIGHTS_0': (UBYTE, 'VEC4', True)}
    if 'normal' in v:
        spec.update({'_NORMAL': (FLOAT, 'VEC3', False), '_ALBEDO': (UBYTE, 'VEC4', True)})
    assert prim['mode'] == 0 and set(prim['attributes']) == set(spec), sorted(prim['attributes'])
    got = {}
    for name, (component, kind, normalized) in spec.items():
        a, arr = accessor(prim['attributes'][name])
        assert (a['componentType'], a['type'], bool(a.get('normalized'))) == (component, kind, normalized), name
        assert a['count'] == v['n'], name
        got[name] = arr
    a = doc['accessors'][prim['attributes']['POSITION']]
    assert np.allclose(a['min'], got['POSITION'].min(0)) and np.allclose(a['max'], got['POSITION'].max(0)), 'min/max'
    report['rotation_norm_max_error'] = float(np.abs(np.linalg.norm(got['_ROTATION'], axis=1) - 1).max())
    assert report['rotation_norm_max_error'] < 1e-5
    assert (got['_SCALE'] > 0).all()
    sums = got['WEIGHTS_0'].astype(int).sum(1)
    assert (sums == 255).all(), 'weights do not sum to 255'
    assert (got['JOINTS_0'] < len(joints)).all()
    report['sole_y'] = float(a['min'][1])
    # Each joint carries exactly the weight of the .vsplat bones merged into it (all of them, for the 441 rig).
    for j in range(len(joints)):
        bones = [b for b, k in skin['bone_to_joint'].items() if k == j]
        expect = int(v['weights'][np.isin(v['bones'], bones)].sum())
        assert int(got['WEIGHTS_0'][got['JOINTS_0'] == j].astype(int).sum()) == expect, f'{names[j]}: weight total differs'
    report['joint_weight_totals'] = 'equal to the merged .vsplat totals'
    # Facing +Z: the toes (ball joints) lie forward of the ankles in +Z.
    feet = [names.index(n) for n in skin['feet'] if n in names]
    if len(feet) == 4:
        P = np.array([node_world(joints[k])[:3, 3] for k in feet])
        # The .vsplat faces +X with its left on -Z; the skin's bind turn (if any) carries both.
        # (a source that already faces +Z, as the bound scaffold rig does, names its own: source_forward, source_left)
        forward = skin['bind_turn'].apply(skin.get('source_forward', [1.0, 0.0, 0.0])); left = skin['bind_turn'].apply(skin.get('source_left', [0.0, 0.0, -1.0]))
        assert ((P[2:] - P[:2]) @ forward > 0).all(), 'toes not forward of the ankles'
        assert P[0] @ left > 0 > P[1] @ left, 'left foot not on the left'
        report['bind_faces'] = [int(round(x)) for x in forward]

    # Pose the rest skin (LBS with the node rests and IBMs) and undo any baked turn: the .vsplat's rest Gaussians come back.
    w = got['WEIGHTS_0'].astype(np.float64) / 255
    J = got['JOINTS_0'].astype(np.int64)
    blend = np.einsum('nk,nkij->nij', w, palette[J])
    pos = np.einsum('nij,nj->ni', blend[:, :3, :3], got['POSITION'].astype(np.float64)) + blend[:, :3, 3]
    back = skin['bind_turn'].inv().apply(pos)
    report['rest_position_max_error_m'] = float(np.abs(back - v['position']).max())
    step = (v['position'].max(0) - v['position'].min(0)) / 65535
    report['position_quantum_m'] = float(step.max())
    assert report['rest_position_max_error_m'] < step.max() / 2, 'posed rest does not reproduce the .vsplat'
    rot = (skin['bind_turn'].inv() * Rot.from_quat(got['_ROTATION'].astype(np.float64)))
    dot = np.abs(np.sum(rot.as_quat() * v['quat_xyzw'] / np.linalg.norm(v['quat_xyzw'], axis=1, keepdims=True), 1))
    report['rest_rotation_max_angle_deg'] = float(np.degrees(2 * np.arccos(np.clip(dot.min(), -1, 1))))
    assert report['rest_rotation_max_angle_deg'] < 0.01
    assert np.allclose(np.log(got['_SCALE'].astype(np.float64)), v['log_scale'], atol=1e-5)
    assert (got['_COLOR'][:, :3] == v['color']).all()
    assert (np.abs(got['_COLOR'][:, 3].astype(int) - 255 / (1 + np.exp(-v['logit']))) <= 0.5 + 1e-6).all()
    if 'normal' in v:
        normal = got['_NORMAL'].astype(np.float64)
        report['normal_norm_max_error'] = float(np.abs(np.linalg.norm(normal, axis=1) - 1).max())
        assert report['normal_norm_max_error'] < 1e-5
        # Posing the rest skin turns each normal by its blended rotation; undoing the bind turn gives the sidecar's.
        R = blend[:, :3, :3] / np.linalg.norm(blend[:, :3, :3], axis=1, keepdims=True)
        posed = skin['bind_turn'].inv().apply(np.einsum('nij,nj->ni', R, normal))
        angle = np.arctan2(np.linalg.norm(np.cross(posed, v['normal']), axis=1), (posed * v['normal']).sum(1))
        report['rest_normal_max_angle_deg'] = float(np.degrees(angle.max()))
        assert report['rest_normal_max_angle_deg'] < 0.01
        assert (got['_ALBEDO'][:, :3] == v['albedo']).all() and (got['_ALBEDO'][:, 3] == got['_COLOR'][:, 3]).all()
        # A coarse outward check: the normal against the offset from the Gaussian's heaviest joint (folds and the
        # hood's inside may disagree, so this is reported, not required).
        heavy = np.array([node_world(j)[:3, 3] for j in joints])[J[:, 0]]     # slot 0 carries the most weight
        away = ((got['POSITION'] - heavy) * normal).sum(1) > 0
        report['normal_away_from_heaviest_joint_share'] = round(float(away.mean()), 4)

    if ply:
        ref = read_rest_ply(ply)
        report['ply_position_max_error_m'] = float(np.abs(back - ref['xyz']).max())
        assert report['ply_position_max_error_m'] < step.max() / 2 + 1e-6, 'posed rest does not match rest.ply'
        dot = np.abs(np.sum(rot.as_quat() * ref['quat_xyzw'] / np.linalg.norm(ref['quat_xyzw'], axis=1, keepdims=True), 1))
        report['ply_rotation_max_angle_deg'] = float(np.degrees(2 * np.arccos(np.clip(dot.min(), -1, 1))))
        assert report['ply_rotation_max_angle_deg'] < 0.01
        report['ply_log_scale_max_error'] = float(np.abs(np.log(got['_SCALE'].astype(np.float64)) - ref['log_scale']).max())
        assert report['ply_log_scale_max_error'] < 1e-4
        sigmoid = 255 / (1 + np.exp(-ref['opacity']))
        assert (np.abs(got['_COLOR'][:, 3] - sigmoid) <= 0.5 + 1e-3).all()
        SH_C0 = 0.28209479177387814
        rgb = np.rint((ref['f_dc'] * SH_C0 + 0.5) * 255)
        assert (rgb == got['_COLOR'][:, :3]).all(), 'colour bytes differ from rest.ply'
    report['joints'] = len(joints)
    report['splats'] = int(v['n'])
    return report


def read_rest_ply(path):
    data = open(path, 'rb').read()
    end = data.index(b'end_header\n') + len(b'end_header\n')
    props = [line.split()[-1] for line in data[:end].decode().splitlines() if line.startswith('property')]
    rows = np.frombuffer(data, np.float32, offset=end).reshape(-1, len(props)).astype(np.float64)
    col = {p: rows[:, i] for i, p in enumerate(props)}
    return {'xyz': np.column_stack([col['x'], col['y'], col['z']]),
            'quat_xyzw': np.column_stack([col['rot_1'], col['rot_2'], col['rot_3'], col['rot_0']]),
            'log_scale': np.column_stack([col['scale_0'], col['scale_1'], col['scale_2']]),
            'opacity': col['opacity'], 'f_dc': np.column_stack([col['f_dc_0'], col['f_dc_1'], col['f_dc_2']])}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('vsplat')
    ap.add_argument('out')
    ap.add_argument('--skeleton', default=DEFAULT_SKELETON, help='SDK skeleton-data-441.ts (npm run inspect:sdk)')
    ap.add_argument('--check-ply', help="inspect_vsplat.py --export-dir's rest.ply for the same .vsplat")
    ap.add_argument('--expect-joints', type=int, help='fail unless the skin has this many joints')
    ap.add_argument('--rig', choices=('mixamo', 'viggle441'), default='mixamo',
                    help="mixamo (default, the product route): the 22 Mixamo body bones, for Mixamo clips; "
                         "viggle441: the 441-rig contract as filed in #10813, for PINOC's splat-skeleton clips")
    ap.add_argument('--mixamo-reference', default=MIXAMO_REFERENCE,
                    help='a Mixamo GLB whose rest gives the joint frames (default: PINOC\'s library Walking, mixamo-glb)')
    ap.add_argument('--surface', help="splat_surface_normals.py's npz for this .vsplat: adds _NORMAL and _ALBEDO")
    a = ap.parse_args()

    names, parents, _, rest_rots = load_skeleton(a.skeleton)
    v = read_vsplat(a.vsplat)
    if a.surface:
        read_surface(a.surface, v)
    if a.rig == 'mixamo':
        if not os.path.exists(a.mixamo_reference):
            raise SystemExit(f'no Mixamo reference at {a.mixamo_reference}; pass --mixamo-reference')
        skin = mixamo_skin(names, parents, rest_rots, v, a.mixamo_reference)
    else:
        skin = pruned_skin(names, parents, rest_rots, v)
    attrs = splat_attributes(v, skin)
    extras = {'source': os.path.basename(a.vsplat), 'rig': skin['rig'], 'splats': int(v['n']),
              'spec': 'asset-pipeline #10813 / rusty-engine #10805'}
    if a.surface:
        extras['surface'] = {'source': os.path.basename(a.surface), 'spec': 'asset-pipeline #10820 / rusty-engine #10815'}
    data = build_glb(skin, attrs, extras)
    report = check(data, v, skin, a.expect_joints, a.check_ply)
    tmp = a.out + '.part'
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, a.out)
    report.update({'out': a.out, 'bytes': len(data), 'rig': skin['rig'], 'joint_names': skin['names']})
    if 'hips_height_ratio' in skin:
        report['hips_height_over_reference'] = skin['hips_height_ratio']
    print(json.dumps(report, indent=1))


if __name__ == '__main__':
    main()
