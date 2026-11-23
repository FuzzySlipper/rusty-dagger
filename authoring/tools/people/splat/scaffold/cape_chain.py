#!/usr/bin/env python3
"""EXPERIMENTAL, not the route for capes: Patch (2026-11-19) set the standard as the cape baked separately and attached to the
runtime splat as a layer (Mobile-30 Vampire Ancient; authoring/tools/people/splat/gsplat/layer_rig.py), or a shorter cape. Kept for shawls
and short garments.

A long cape or shawl on bones of its own: a chain down the back that swings, instead of the leg bones it was glued to.

    cape_chain.py rig  SCAFFOLD.glb TRANSFORM.json SPLAT.npz OUT.glb CHAIN.json [--segments 4] [--margin 0.06]
    cape_chain.py bind SPLAT.npz BINDING.npz TRANSFORM.json CHAIN.json OUT_DIR
    cape_chain.py sim  PREP_DIR CHAIN.json [--tags bound400k,body] [--hz 1.4] [--damping 0.35] [--loops 3]

Villager 395's cape hangs from her shoulders to the ground behind her; the bound route weighted its lower part to the
leg bones (the skirt step blends them), so it moved with her legs (Patch 2026-11-19: split the cape onto extra bones).

rig    finds the cape below the hips in SPLAT (the bound bake, bake frame; TRANSFORM is score/transform.json): every
       Gaussian behind the leg line and more than --margin metres outside capsules round the thighs and shins. It hangs a chain of --segments
       bones (Cape1..CapeN, Cape1 a child of Hips at hip height) down the cape's middle to its hem, each node at the
       median of the cape at that height, and writes OUT.glb: the scaffold with those joints added to its skin
       (indices after the scaffold's own, so existing bindings keep their meaning) and CHAIN.json.
bind   moves the cape Gaussians of one body (the full bake or a size, same frame) onto the chain: each on the bone of
       its segment, eased into the bone above near a node, and into its own weights near the top node. OUT_DIR gets
       splat.npz (a copy), binding.npz and cape.json.
sim    rewrites the chain joints in clip_compare.py prep's poses-TAG-CLIP.npz (the rig follows Hips rigidly there):
       each node below the first is a damped spring toward that rigid position (a lag that swings), kept at its
       segment length from the node above and pushed out of capsules round the thighs and shins, run over --loops
       passes of the clip and the last one kept, so a looping clip loops. Each bone is then turned from its rigid
       direction onto its simulated one. The rigid poses are kept as poses-TAG-CLIP.rigid.npz.
"""
import argparse, json, struct, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from skirt_weights import load_rig  # noqa: E402

LEGS = [(s + a, s + b, r) for s in ('Left', 'Right') for a, b, r in (('UpLeg', 'Leg', 0.085), ('Leg', 'Foot', 0.065))]


def to_scaffold(means, T):
    return (np.asarray(means, np.float64) - np.array(T['translation'])) @ np.array(T['rotation']) / T['scale']


def leg_line(J):
    """z of the leg joints (both sides averaged) as a function of height, scaffold frame."""
    ys = np.array([J['LeftUpLeg'][1], J['LeftLeg'][1], J['LeftFoot'][1]]); zs = np.array([(J[s + n][2]) for n in ('UpLeg', 'Leg', 'Foot') for s in ('Left', 'Right')]).reshape(3, 2).mean(1)
    o = np.argsort(ys)
    return lambda y: np.interp(y, ys[o], zs[o])


def leg_clearance(p, J):
    """Distance outside the nearest leg capsule (LEGS radii), metres; negative inside."""
    d = np.full(len(p), 9.0)
    for a, b, r in LEGS:
        A, B = J[a], J[b]; ab = B - A; t = np.clip((p - A) @ ab / (ab @ ab), 0, 1)
        d = np.minimum(d, np.linalg.norm(p - (A + t[:, None] * ab), axis=1) - r)
    return d


def cape_mask(p, J, margin):
    """Below the hips, behind the leg line by more than 2 cm, and more than --margin outside the leg capsules: the cape and its
    lining, which sits within a few cm of the legs (395's red lining tore away at a flat 8 cm depth cut, 2026-11-19)."""
    hy = J['Hips'][1]
    return (p[:, 1] < hy + 0.05) & (p[:, 2] < leg_line(J)(p[:, 1]) - 0.02) & (leg_clearance(p, J) > margin)


def glb_read(path):
    raw = Path(path).read_bytes(); jl = struct.unpack_from('<I', raw, 12)[0]
    return json.loads(raw[20:20 + jl]), bytearray(raw[20 + jl + 8:])


def glb_write(path, doc, binary):
    binary += b'\0' * (-len(binary) % 4); doc['buffers'][0]['byteLength'] = len(binary)
    js = json.dumps(doc, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    Path(path).write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(binary)) + struct.pack('<II', len(js), 0x4E4F534A) + js
                           + struct.pack('<II', len(binary), 0x004E4942) + bytes(binary))


def trs(n):
    from scipy.spatial.transform import Rotation as Rot
    if 'matrix' in n:
        return np.array(n['matrix'], float).reshape(4, 4).T
    m = np.eye(4); m[:3, :3] = Rot.from_quat(n.get('rotation', [0, 0, 0, 1])).as_matrix() * np.array(n.get('scale', [1, 1, 1]))
    m[:3, 3] = n.get('translation', [0, 0, 0]); return m


def cmd_rig(a):
    rig = load_rig(a.scaffold); names = list(rig['names']); J = {m: np.asarray(rig['bind'][i], float) for i, m in enumerate(names)}
    T = json.loads(a.transform.read_text()); p = to_scaffold(np.load(a.splat)['means'], T)
    cape = cape_mask(p, J, a.margin); q = p[cape]
    top, low = J['Hips'][1], float(np.percentile(q[:, 1], 0.5)) + 0.02
    ys = np.linspace(top, low, a.segments + 1); nodes = []
    for y in ys:
        band = q[np.abs(q[:, 1] - y) < 0.06]
        nodes.append([float(np.median(band[:, 0])) if len(band) else 0.0, float(y), float(np.median(band[:, 2])) if len(band) else float(q[:, 2].mean())])
    nodes = np.array(nodes)
    doc, binary = glb_read(a.scaffold)
    sk = doc['skins'][0]; joints = sk['joints']
    parent = {c: i for i, n in enumerate(doc['nodes']) for c in n.get('children', [])}

    def world(i):
        return trs(doc['nodes'][i]) if i not in parent else world(parent[i]) @ trs(doc['nodes'][i])
    hips = next(j for j in joints if doc['nodes'][j]['name'] == 'Hips')
    acc = doc['accessors'][sk['inverseBindMatrices']]; view = doc['bufferViews'][acc['bufferView']]
    off = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
    ibm = np.frombuffer(bytes(binary[off:off + 64 * acc['count']]), '<f4').reshape(-1, 16)
    new_ibm, prev, names_new = [], hips, []
    for k in range(a.segments):
        name = f'Cape{k + 1}'; pw = world(prev)
        local = np.linalg.inv(pw) @ np.r_[nodes[k], 1.0]
        doc['nodes'].append({'name': name, 'translation': [float(x) for x in local[:3]]})
        idx = len(doc['nodes']) - 1; doc['nodes'][prev].setdefault('children', []).append(idx)
        parent[idx] = prev
        w = world(idx); new_ibm.append(np.linalg.inv(w).T.reshape(-1).astype('<f4')); joints.append(idx); names_new.append(name); prev = idx
    data = np.concatenate([ibm.reshape(-1), np.concatenate(new_ibm)]).astype('<f4').tobytes()
    binary += b'\0' * (-len(binary) % 4)
    doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(data)}); binary += data
    doc['accessors'].append({'bufferView': len(doc['bufferViews']) - 1, 'componentType': 5126, 'count': len(joints), 'type': 'MAT4'})
    sk['inverseBindMatrices'] = len(doc['accessors']) - 1
    glb_write(a.out, doc, binary)
    first = len(names)
    seg = [float(np.linalg.norm(nodes[k + 1] - nodes[k])) for k in range(a.segments)]
    chain = {'joints': names_new, 'joint_index': list(range(first, first + a.segments)), 'nodes': nodes.tolist(), 'segment_m': seg,
             'margin_m': a.margin, 'parent': 'Hips', 'scaffold': str(a.scaffold), 'cape_gaussians_in_reference': int(cape.sum()),
             'legs': [[x, y, r] for x, y, r in LEGS]}
    a.chain.write_text(json.dumps(chain, indent=1) + '\n'); print(json.dumps({k: chain[k] for k in ('joints', 'segment_m', 'cape_gaussians_in_reference')}))


def cmd_bind(a):
    C = json.loads(a.chain.read_text()); nodes = np.array(C['nodes']); ji = C['joint_index']; nseg = len(ji)
    rig = load_rig(C['scaffold']); names = list(rig['names']); J = {m: np.asarray(rig['bind'][i], float) for i, m in enumerate(names)}
    T = json.loads(a.transform.read_text()); sp = dict(np.load(a.splat)); b = dict(np.load(a.binding))
    p = to_scaffold(sp['means'], T); bo = b['bones'].astype(np.int64).copy(); w = b['weights'].astype(np.float64).copy()
    cape = cape_mask(p, J, C['margin_m'])
    ys = nodes[:, 1]; ease = 0.3; top_band = 0.08
    for i in np.where(cape)[0]:
        y = p[i, 1]
        k = int(np.clip(np.searchsorted(-ys, -y) - 1, 0, nseg - 1))       # segment: ys[k] >= y > ys[k+1]
        f = float(np.clip((ys[k] - y) / max(ys[k] - ys[k + 1], 1e-6), 0, 1))
        cw = {ji[k]: 1.0}
        if k > 0 and f < ease:
            s = 0.5 * (1 - f / ease); cw = {ji[k]: 1 - s, ji[k - 1]: s}
        keep = float(np.clip((y - (ys[0] - top_band)) / (2 * top_band), 0, 1)) if k == 0 else 0.0   # near the top node: its own weights
        mix = {}
        for bone, wt in zip(bo[i], w[i]):
            if wt > 0:
                mix[int(bone)] = mix.get(int(bone), 0) + keep * wt
        for bone, wt in cw.items():
            mix[bone] = mix.get(bone, 0) + (1 - keep) * wt
        top4 = sorted(mix.items(), key=lambda kv: -kv[1])[:4]; tot = sum(v for _, v in top4)
        bo[i] = [t[0] for t in top4] + [0] * (4 - len(top4)); w[i] = [t[1] / tot for t in top4] + [0.0] * (4 - len(top4))
    a.out.mkdir(parents=True, exist_ok=True)
    b['bones'] = bo.astype(b['bones'].dtype); b['weights'] = w.astype(b['weights'].dtype)
    np.savez(a.out / 'splat.npz', **sp); np.savez(a.out / 'binding.npz', **b)
    rep = {'gaussians': len(p), 'cape': int(cape.sum()), 'chain': C['joints']}
    (a.out / 'cape.json').write_text(json.dumps(rep) + '\n'); print(json.dumps(rep))


def minrot(u, v):
    u = u / np.linalg.norm(u); v = v / np.linalg.norm(v); c = float(u @ v); ax = np.cross(u, v); s = np.linalg.norm(ax)
    if s < 1e-9:
        return np.eye(3)
    ax /= s; K = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]])
    return np.eye(3) + s * K + (1 - c) * K @ K


def push_out(x, a, b, r):
    ab = b - a; t = np.clip((x - a) @ ab / max(ab @ ab, 1e-9), 0, 1); c = a + t * ab; d = x - c; n = np.linalg.norm(d)
    if n >= r:
        return x
    if n < 1e-6:
        d = np.array([0, 0, -1.0]); n = 1.0
    return c + d / n * r


def cmd_sim(a):
    C = json.loads(a.chain.read_text()); nodes = np.array(C['nodes']); ji = C['joint_index']; nseg = len(ji); L = np.array(C['segment_m'])
    rig = load_rig(C['scaffold']); names = list(rig['names']) + C['joints'] if len(rig['names']) < max(ji) + 1 else list(rig['names'])
    w0 = 2 * np.pi * a.hz; kk, cc = w0 * w0, 2 * a.damping * w0
    prep = json.loads((a.prep / 'prep.json').read_text())
    for tag in a.tags.split(','):
        for clip, rec in prep['clips'].items():
            f = a.prep / f'poses-{tag}-{clip}.npz'; rigid = a.prep / f'poses-{tag}-{clip}.rigid.npz'
            if not rigid.exists():
                rigid.write_bytes(f.read_bytes())
            d = dict(np.load(rigid)); S = d['skin'].astype(np.float64); P = d['joints'].astype(np.float64); F = len(S)
            leg_idx = [(names.index(x), names.index(y), r) for x, y, r in C['legs']]
            hi_ = names.index('Hips'); Rh = S[:, hi_, :3, :3] @ np.array([0, 0, 1.0])       # facing (+Z at rest)
            tgt = np.zeros((F, nseg + 1, 3))
            for k in range(nseg):
                tgt[:, k] = (S[:, ji[k]] @ np.r_[nodes[k], 1.0])[:, :3]
            tgt[:, nseg] = (S[:, ji[-1]] @ np.r_[nodes[nseg], 1.0])[:, :3]
            dt = 1.0 / rec['fps']; sub = 6; h = dt / sub
            x = tgt[0].copy(); v = np.zeros_like(x); out = np.zeros_like(tgt)
            for loop in range(a.loops):
                for fr in range(F):
                    for s in range(sub):
                        u = (s + 1) / sub; fn = (fr + 1) % F if fr + 1 < F else fr
                        T_ = tgt[fr] * (1 - u) + tgt[fn] * u
                        x[0] = T_[0]; v[0] = 0
                        acc = kk * (T_[1:] - x[1:]) - cc * v[1:]
                        v[1:] += acc * h; xn = x + v * h; xn[0] = T_[0]
                        for _ in range(3):
                            for k in range(1, nseg + 1):                  # segment lengths from the node above
                                dd = xn[k] - xn[k - 1]; xn[k] = xn[k - 1] + dd / max(np.linalg.norm(dd), 1e-9) * L[k - 1]
                            fwd = Rh[fr] * (1 - u) + Rh[fn] * u; fwd /= np.linalg.norm(fwd)
                            for k in range(1, nseg + 1):                  # behind the legs: the cape is a sheet as wide as the hips, so a
                                for ia, ib, r in leg_idx:                 # leg at the node's height anywhere across it pushes it back
                                    PA = P[fr, ia] * (1 - u) + P[fn, ia] * u; PB = P[fr, ib] * (1 - u) + P[fn, ib] * u
                                    lo, hi = sorted((PA[1], PB[1]))
                                    if not lo - 0.05 <= xn[k, 1] <= hi + 0.05:
                                        continue
                                    t = np.clip((xn[k, 1] - PA[1]) / (PB[1] - PA[1] if abs(PB[1] - PA[1]) > 1e-6 else 1e-6), 0, 1)
                                    q = PA + t * (PB - PA); lim = (q - xn[k]) @ fwd - (r + a.gap)
                                    if lim < 0:
                                        xn[k] += fwd * lim
                        v = (xn - x) / h; x = xn
                    if loop == a.loops - 1:
                        out[fr] = x
            for k in range(nseg):
                R0 = S[:, ji[k], :3, :3]
                for fr in range(F):
                    dr = R0[fr] @ (nodes[k + 1] - nodes[k]); ds = out[fr, k + 1] - out[fr, k]
                    R = minrot(dr, ds) @ R0[fr]
                    S[fr, ji[k], :3, :3] = R; S[fr, ji[k], :3, 3] = out[fr, k] - R @ nodes[k]
                    P[fr, ji[k]] = out[fr, k]
            d['skin'] = S.astype(np.float32); d['joints'] = P.astype(np.float32); np.savez_compressed(f, **d)
            lag = float(np.linalg.norm(out[:, -1] - tgt[:, -1], axis=1).max())
            print(f'{tag} {clip}: {F} frames, hem moved up to {lag:.3f} m from rigid')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); sp = ap.add_subparsers(dest='cmd', required=True)
    r = sp.add_parser('rig'); r.add_argument('scaffold', type=Path); r.add_argument('transform', type=Path); r.add_argument('splat', type=Path)
    r.add_argument('out', type=Path); r.add_argument('chain', type=Path); r.add_argument('--segments', type=int, default=4); r.add_argument('--margin', type=float, default=0.01)
    b = sp.add_parser('bind'); b.add_argument('splat', type=Path); b.add_argument('binding', type=Path); b.add_argument('transform', type=Path)
    b.add_argument('chain', type=Path); b.add_argument('out', type=Path)
    s = sp.add_parser('sim'); s.add_argument('prep', type=Path); s.add_argument('chain', type=Path); s.add_argument('--tags', default='bound400k,body')
    s.add_argument('--hz', type=float, default=1.4); s.add_argument('--damping', type=float, default=0.35); s.add_argument('--loops', type=int, default=3); s.add_argument('--gap', type=float, default=0.04, help='metres kept between a leg and the cape behind it')
    a = ap.parse_args()
    {'rig': cmd_rig, 'bind': cmd_bind, 'sim': cmd_sim}[a.cmd](a)


if __name__ == '__main__':
    main()
