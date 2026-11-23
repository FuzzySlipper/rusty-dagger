#!/usr/bin/env python3
"""Bind a trained splat to a skinned GLB's rig and write a VIGGCHAR v1 file.

  python3 make_skinned_vsplat.py RIG.glb splat.npz OUT.vsplat

Each Gaussian takes the skin weights of its nearest mesh vertices (inverse-
distance blend of the 4 closest, then the 4 strongest bones). Bone indices are
written in the order the Viggle SDK's MeshCharacter builds its armature: a
depth-first walk of the glTF scene collecting skin joints. The joints block
carries one entry per rig bone, so the SDK binds the indices directly with no
canonical-skeleton remap. OUT.json lists the bone names in that order so the
viewer can check the armature agrees.
"""
import json, struct, sys
import numpy as np

COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def read_glb(path):
    data = open(path, 'rb').read()
    n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n])
    binary = data[20 + n + 8:]

    def accessor(i):
        a = doc['accessors'][i]; v = doc['bufferViews'][a['bufferView']]
        dtype = COMPONENT[a['componentType']]; width = WIDTH[a['type']]
        start = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = v.get('byteStride', 0) or np.dtype(dtype).itemsize * width
        raw = np.frombuffer(binary, np.uint8, count=stride * (a['count'] - 1) + np.dtype(dtype).itemsize * width, offset=start)
        rows = np.lib.stride_tricks.as_strided(raw, (a['count'], np.dtype(dtype).itemsize * width), (stride, 1))
        out = np.ascontiguousarray(rows).view(dtype).reshape(a['count'], width).astype(np.float64)
        if a.get('normalized'):
            out /= np.iinfo(dtype).max
        return out
    return doc, accessor


def write_vsplat(path, means, scales, quats, opacities, rgb, bones, infl, palette, joint_pos, *, scale_bounds=None):
    """Write VIGGCHAR v1. The palette maps bone indices to the rig.

    For a matched pair, pass the per-axis log-scale bounds of both bodies together
    so identical Gaussians get identical encoded scales.
    """
    n = len(means)
    blocks, payload = {}, []
    offset = 0
    def add(name, planes, dtype, extra=None):
        nonlocal offset
        arr = np.ascontiguousarray(np.stack(planes).astype(dtype))
        raw = arr.tobytes()
        entry = {'offset': offset, 'length': len(raw), 'dtype': {np.uint8: 'u8', np.uint16: 'u16', np.float32: 'f32'}[dtype],
                 'components': len(planes), 'count': len(planes[0])}
        entry.update(extra or {})
        blocks[name] = entry; payload.append(raw); offset += len(raw)
        # The decoder requires every block to start on a 4-byte boundary.
        pad = (-offset) % 4
        if pad:
            payload.append(b'\0' * pad); offset += pad

    def quantize(values, levels, bounds=None):
        lo, hi = (values.min(0), values.max(0)) if bounds is None else bounds
        span = np.where(hi > lo, hi - lo, 1.0)
        q = np.rint((values - lo) / span * levels)
        return [q[:, c] for c in range(values.shape[1])], {'min': lo.tolist(), 'max': hi.tolist()}

    planes, mm = quantize(means, 65535); add('position', planes, np.uint16, mm)
    planes, mm = quantize(scales.astype(np.float64), 255, scale_bounds); add('scale', planes, np.uint8, mm)
    q = quats.astype(np.float64)                       # wxyz
    q = q * np.where(q[:, :1] < 0, -1, 1)                   # positive w is reconstructed
    add('rotation', [np.rint((q[:, c] + 1) / 2 * 255) for c in (1, 2, 3)], np.uint8)
    add('color', [np.rint(np.clip(rgb[:, c], 0, 1) * 255) for c in range(3)], np.uint8)
    planes, mm = quantize(np.clip(opacities.astype(np.float64), -9.21, 9.21)[:, None], 255); add('opacity', planes, np.uint8, mm)
    add('boneIndex', [bones[:, c] for c in range(4)], np.uint16, {'palette': [int(b) for b in palette]})
    w8 = np.rint(infl[:, :3] * 255)
    w8 = np.minimum(w8, 255 - np.concatenate([np.zeros((n, 1)), np.cumsum(w8, 1)[:, :-1]], 1).clip(0, 255))
    add('boneWeight', [w8[:, c] for c in range(3)], np.uint8)
    add('joints', [joint_pos[:, c] for c in range(3)], np.float32)

    directory = json.dumps({'numSplats': n, 'blocks': blocks}, separators=(',', ':')).encode()
    directory += b' ' * ((-len(directory)) % 4)
    total = 20 + len(directory) + offset
    with open(path, 'wb') as f:
        f.write(b'VIGGCHAR'); f.write(struct.pack('<III', 1, total, len(directory))); f.write(directory)
        for raw in payload:
            f.write(raw)
    return total


def main():
    rig_path, splat_path, out_path = sys.argv[1:4]
    doc, accessor = read_glb(rig_path)
    skin = doc['skins'][0]
    prim = next(p for m in doc['meshes'] for p in m['primitives'] if 'JOINTS_0' in p['attributes'])
    verts = accessor(prim['attributes']['POSITION'])
    joints = accessor(prim['attributes']['JOINTS_0']).astype(int)
    weights = accessor(prim['attributes']['WEIGHTS_0'])
    weights /= np.maximum(weights.sum(1, keepdims=True), 1e-9)

    # Armature order: depth-first over the scene, keeping skin joints only.
    joint_set = set(skin['joints'])
    order = []
    def walk(i):
        if i in joint_set:
            order.append(i)
        for c in doc['nodes'][i].get('children', []):
            walk(c)
    for root in doc['scenes'][doc.get('scene', 0)]['nodes']:
        walk(root)
    skin_to_rig = np.array([order.index(j) for j in skin['joints']])
    names = [doc['nodes'][i].get('name', f'node_{i}') for i in order]

    s = np.load(splat_path)
    means = s['means'].astype(np.float64)
    n = len(means)
    bones = np.zeros((n, 4), np.int64); infl = np.zeros((n, 4))
    for start in range(0, n, 2048):
        p = means[start:start + 2048]
        d2 = ((p[:, None, :] - verts[None, :, :]) ** 2).sum(-1)
        near = np.argpartition(d2, 4, axis=1)[:, :4]
        nd = np.take_along_axis(d2, near, 1)
        w = 1.0 / (np.sqrt(nd) + 1e-4); w /= w.sum(1, keepdims=True)
        acc = np.zeros((len(p), len(order)))
        for k in range(4):
            vj = skin_to_rig[joints[near[:, k]]]          # (m, 4) rig bone indices
            vw = weights[near[:, k]] * w[:, k:k + 1]
            for c in range(4):
                np.add.at(acc, (np.arange(len(p)), vj[:, c]), vw[:, c])
        top = np.argsort(-acc, 1)[:, :4]
        tw = np.take_along_axis(acc, top, 1); tw /= np.maximum(tw.sum(1, keepdims=True), 1e-9)
        bones[start:start + len(p)] = top; infl[start:start + len(p)] = tw

    # Rig rest joint positions, in the same metre space as the splats.
    ibm = accessor(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    bind = np.linalg.inv(ibm)
    joint_pos = np.zeros((len(order), 3))
    for k, j in enumerate(skin['joints']):
        joint_pos[skin_to_rig[k]] = bind[k][:3, 3]

    total = write_vsplat(out_path, means, s['scales'], s['quats'], s['opacities'], s['rgb'], bones, infl, range(len(order)), joint_pos)
    json.dump({'rig': rig_path, 'bones': names, 'splats': n,
               'bonesUsed': int(len(np.unique(bones[infl > 0.01])))}, open(out_path.rsplit('.', 1)[0] + '.json', 'w'), indent=1)
    print('wrote', out_path, total, 'bytes;', n, 'splats;', len(order), 'bones;',
          'bounds', means.min(0).round(3), means.max(0).round(3))


if __name__ == '__main__':
    main()
