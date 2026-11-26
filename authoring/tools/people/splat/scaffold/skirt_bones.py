#!/usr/bin/env python3
"""A long skirt on a ring of bones of its own, pushed open by the legs: one bell that never stretches between the legs.

    skirt_bones.py rig   SCAFFOLD.glb TRANSFORM.json SPLAT.npz BINDING.npz OUT_DIR [--columns 8] [--tiers 2] [--slit] [--fade 0.06]
    skirt_weights.py SPLAT.npz BINDING.npz OUT_DIR/scaffold.glb TRANSFORM.json OUT --bones OUT_DIR/skirt.json [--solid 3] ...
    skirt_bones.py drive PREP_DIR SKIRT.json [--tags bound400k,body] [--gap 0.02] [--release 2.5] [--spread 0.7]   (1 tier)
    skirt_bones.py drive PREP_DIR SKIRT.json [--tags bound400k,body] [--cloth-gap 0.018]                          (2 tiers)

Two tiers, the default since 2026-11-20 (Patch: "make the xpbd skirt standard"): each column is an upper bone SkirtK_U under Hips
and a lower one SkirtK_L under it at mid-skirt, the knee of the cloth, and drive solves a cloth sheet for each clip instead of
turning rigid columns. The sheet is a 2N x 7 grid on the skirt's rest surface, pinned at the top to the hips. Its edges are
stiff down the warp and softer across, with shear and bend edges. It is pulled gently back toward its rest shape and pushed out
by thigh and shin capsules (8.2 and 6.4 cm plus --cloth-gap), 8 substeps a frame, over two passes of the clip so a loop loops.
Each column's two bones then follow the sheet's top, middle and hem points. The cloth sim is another session's XPBD prototype
(live-evidence/xpbd-skirt-proto-20261120-r01/xpbd_skirt.py), here on this tool's skirt detection, hem and binding. On 456 it
kept the checks whole at the front at full stride, where one-tier columns smeared the hem into shards
(live-evidence/xpbd-456-ab-20261120-r01). --slit leaves the sheet open at the front below the hips, for a skirt that parts there
(395's wrap): there the columns either side of the front are not blended across. --tiers 1 is the earlier rigid ring.
The settings are the prototype's. Its skirt hangs narrower than the baked bell in a walk: the weft barely gives, so a knee
pushing the front out 16 cm pulls the sides in 10 to 16 cm. On 456 (2026-11-20) --cloth-home 40 --cloth-weft 2e-3 kept the sides
within 6 cm but made the lower checks wavy and lifted the front hem by the boot. Gravity off made little difference.

Why (Patch 2026-11-19, villagers 395 and 397): skirt_weights.py hangs a long skirt on a blend of both legs and the hips. A blend can
only average two legs that swing opposite ways, so a narrow skirt between them stretches, tears into streaks and shows its inside,
which no camera saw (the lining at 395's front slit, 397's hem). Games rig long skirts on bones of their own that the thighs push
out; this is that, worked out per clip in advance, so the Workbench clips carry the skirt bones' motion like any other joint.

rig    finds the skirt as skirt_weights.py does (Gaussians a leg bone leads between the hem and the hip joints, less the boot
       shafts) and hangs --columns bones round it: Skirt0 at the front, then on round through +X, each a child of Hips with its
       pivot on the skirt's surface at hip-joint height. For each column it measures how far the skirt reaches from the hips'
       centre line in the column's direction at 16 heights (90th percentile of the skirt in its sector). OUT_DIR gets
       scaffold.glb (the scaffold with those joints added to its skin after its own, so existing bindings keep their meaning)
       and skirt.json.
bind   (skirt_weights.py --bones, bind_weights here) each skirt Gaussian goes to the two columns either side of its direction, by
       angle; within --fade metres of the top it keeps a share of its own weights, all of them at the top.
drive  rewrites the skirt joints in clip_compare.py prep's poses-TAG-CLIP.npz (the rig moves them rigidly with Hips there). Per
       frame, in the pelvis's frame, each column turns outward about its pivot just far enough that the thighs and shins
       (capsules of 8.5 and 6.5 cm, plus --gap) stay inside the skirt at every height from the top to the hem. A leg asks for no
       more room than it had in the bind pose, so the skirt does not flare at rest. A column falls back no faster than
       --release radians a second, so the cloth settles after a stride instead of snapping back. Two passes over the clip, the
       second kept, so a looping clip loops. Each column then turns at least --spread times its neighbours' turn (repeated
       round the ring), so a knee pushing the front out pulls the sides along like cloth: 456's front column turned 41 degrees
       while its neighbours turned 23, and the checks between them sheared into streaks (2026-11-20). The rigid poses are kept as poses-TAG-CLIP.rigid.npz (drive can be run again).
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cape_chain import glb_read, glb_write, trs   # noqa: E402
from skirt_weights import load_rig, region, smooth   # noqa: E402

LEG_R = {'UpLeg': 0.085, 'Leg': 0.065}   # capsule radii, metres (cape_chain.py's)


def azimuth(p, c):
    """Angle about the vertical through c (x, z): 0 toward +Z (the front), growing toward +X, in [0, 2 pi)."""
    return np.mod(np.arctan2(p[..., 0] - c[0], p[..., 2] - c[1]), 2 * np.pi)


def cmd_rig(a):
    rig = load_rig(a.scaffold); T = json.loads(a.transform.read_text())
    sp = np.load(a.splat); b = np.load(a.binding); M = sp['means'].astype(float)
    G = region(rig, T, M, b['bones'].astype(np.int64), b['weights'].astype(np.float64), shins=False, cloak=a.cloak_margin if a.cloak else None)
    names = G['names']; J = {n: np.asarray(rig['bind'][i], float) for i, n in enumerate(names)}
    P = (M - G['tr']) @ G['rot'] / G['sc']; q = P[G['skirt']]                          # scaffold frame (metres)
    top = float((J['LeftUpLeg'][1] + J['RightUpLeg'][1]) / 2); hem = float(np.percentile(q[:, 1], 1))
    c = np.array([(J['LeftUpLeg'][0] + J['RightUpLeg'][0]) / 2, (J['LeftUpLeg'][2] + J['RightUpLeg'][2]) / 2])
    N = a.columns * (2 if a.tiers == 2 else 1)   # two tiers: measured on a sim grid of twice the columns, bones on the even ones
    off0 = np.pi / N if (a.tiers == 2 and a.slit) else 0.0   # a slit falls between two sim columns, at the front
    th = np.mod(2 * np.pi * np.arange(N) / N + off0, 2 * np.pi)
    az = azimuth(q, c); half = (1.25 if a.tiers == 2 else 1.0) * np.pi / N
    if a.cloak:   # a cloak behind the legs: an open sheet over the cloak's own arc, both edges on bone columns (npc-472, 2026-11-25)
        if a.tiers != 2:
            raise SystemExit('skirt_bones: --cloak needs the two-tier cloth sheet')
        N = 2 * a.columns - 1; lo, hi = np.percentile(az, [3, 97]); th = np.linspace(lo, hi, N); half = 0.625 * (hi - lo) / (N - 1)
    D = np.stack([np.sin(th), np.zeros(N), np.cos(th)], 1)
    ys = np.linspace(top, hem, 16); band = 0.75 * (top - hem) / 15 + 0.01
    rel = q - np.array([c[0], 0, c[1]])
    rad = np.full((N, len(ys)), np.nan)
    for k in range(N):
        dth = np.abs((az - th[k] + np.pi) % (2 * np.pi) - np.pi); sec = dth < half
        proj = rel @ D[k]
        for i, y in enumerate(ys):
            m = sec & (np.abs(q[:, 1] - y) < band)
            if m.sum() >= 5:
                rad[k, i] = float(np.percentile(proj[m], 90))
        ok = ~np.isnan(rad[k])
        if ok.sum() >= 2:
            rad[k] = np.interp(np.arange(len(ys)), np.where(ok)[0], rad[k][ok])
    for k in range(N):   # a column with no skirt measured: its neighbours' mean (a cloak's edge: its one neighbour)
        if np.isnan(rad[k]).any():
            nb = [(k - 1) % N, (k + 1) % N] if not a.cloak else [x for x in (k - 1, k + 1) if 0 <= x < N]
            rad[k] = np.nanmean(rad[nb], 0)
    if a.tiers == 2:
        return rig_two_tier(a, rig, G, names, J, top, hem, c, th, D, ys, rad)
    pivots = np.array([[c[0] + D[k, 0] * rad[k, 0], top, c[1] + D[k, 2] * rad[k, 0]] for k in range(N)])

    doc, binary = glb_read(a.scaffold)
    sk = doc['skins'][0]; joints = sk['joints']
    parent = {ch: i for i, n in enumerate(doc['nodes']) for ch in n.get('children', [])}

    def world(i):
        return trs(doc['nodes'][i]) if i not in parent else world(parent[i]) @ trs(doc['nodes'][i])
    hips = next(j for j in joints if doc['nodes'][j]['name'].split(':')[-1] == 'Hips')
    acc = doc['accessors'][sk['inverseBindMatrices']]; view = doc['bufferViews'][acc['bufferView']]
    off = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
    ibm = np.frombuffer(bytes(binary[off:off + 64 * acc['count']]), '<f4').reshape(-1, 16)
    first = len(joints); new_ibm, new_names = [], []
    hw = world(hips)
    for k in range(N):
        name = f'Skirt{k}'; local = np.linalg.inv(hw) @ np.r_[pivots[k], 1.0]
        doc['nodes'].append({'name': name, 'translation': [float(x) for x in local[:3]]})
        idx = len(doc['nodes']) - 1; doc['nodes'][hips].setdefault('children', []).append(idx); parent[idx] = hips
        new_ibm.append(np.linalg.inv(world(idx)).T.reshape(-1).astype('<f4')); joints.append(idx); new_names.append(name)
    data = np.concatenate([ibm.reshape(-1), np.concatenate(new_ibm)]).astype('<f4').tobytes()
    binary += b'\0' * (-len(binary) % 4)
    doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(data)}); binary += data
    doc['accessors'].append({'bufferView': len(doc['bufferViews']) - 1, 'componentType': 5126, 'count': len(joints), 'type': 'MAT4'})
    sk['inverseBindMatrices'] = len(doc['accessors']) - 1
    a.out.mkdir(parents=True, exist_ok=True); glb_write(a.out / 'scaffold.glb', doc, binary)
    legs = {s: [names.index(s + n) for n in ('UpLeg', 'Leg', 'Foot')] for s in ('Left', 'Right')}
    C = {'joints': new_names, 'joint_index': list(range(first, first + N)), 'theta': th.tolist(), 'centre': c.tolist(), 'top': top, 'hem': hem,
         'ys': ys.tolist(), 'rad': np.round(rad, 5).tolist(), 'pivots': np.round(pivots, 5).tolist(), 'fade': a.fade,
         'hips': names.index('Hips'), 'legs': legs, 'rest': {str(j): J[names[j]].tolist() for s in legs.values() for j in s},
         'bake_scale': G['sc'], 'scaffold': str(a.scaffold), 'skirt_gaussians_in_reference': int(G['skirt'].sum())}
    (a.out / 'skirt.json').write_text(json.dumps(C, indent=1) + '\n')
    print(json.dumps({'joints': new_names, 'top': round(top, 3), 'hem': round(hem, 3), 'reach_at_top_cm': np.round(100 * rad[:, 0], 1).tolist(),
                      'reach_at_hem_cm': np.round(100 * rad[:, -1], 1).tolist(), 'skirt': C['skirt_gaussians_in_reference']}))


def add_joints(scaffold, specs):
    """Append joints to the scaffold's skin after its own (existing bindings keep their meaning). specs: (name, parent name or
    index into specs, world position). Returns the glTF doc and binary, and the new joints' skin indices."""
    doc, binary = glb_read(scaffold)
    sk = doc['skins'][0]; joints = sk['joints']
    parent = {ch: i for i, n in enumerate(doc['nodes']) for ch in n.get('children', [])}

    def world(i):
        return trs(doc['nodes'][i]) if i not in parent else world(parent[i]) @ trs(doc['nodes'][i])
    hips = next(j for j in joints if doc['nodes'][j]['name'].split(':')[-1] == 'Hips')
    acc = doc['accessors'][sk['inverseBindMatrices']]; view = doc['bufferViews'][acc['bufferView']]
    off = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
    ibm = np.frombuffer(bytes(binary[off:off + 64 * acc['count']]), '<f4').reshape(-1, 16)
    new_ibm, made, out = [], [], []
    for name, par, pos in specs:
        pn = hips if par == 'Hips' else made[par]
        local = np.linalg.inv(world(pn)) @ np.r_[pos, 1.0]
        doc['nodes'].append({'name': name, 'translation': [float(x) for x in local[:3]]})
        idx = len(doc['nodes']) - 1; doc['nodes'][pn].setdefault('children', []).append(idx); parent[idx] = pn
        new_ibm.append(np.linalg.inv(world(idx)).T.reshape(-1).astype('<f4')); out.append(len(joints)); joints.append(idx); made.append(idx)
    data = np.concatenate([ibm.reshape(-1), np.concatenate(new_ibm)]).astype('<f4').tobytes()
    binary += b'\0' * (-len(binary) % 4)
    doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(data)}); binary += data
    doc['accessors'].append({'bufferView': len(doc['bufferViews']) - 1, 'componentType': 5126, 'count': len(joints), 'type': 'MAT4'})
    sk['inverseBindMatrices'] = len(doc['accessors']) - 1
    return doc, binary, out


NV = 7   # rows of the cloth sheet, top to hem; the lower bones sit on row 3


def rig_two_tier(a, rig, G, names, J, top, hem, c, th, D, ys, rad):
    nu = len(th); rows = np.linspace(top, hem, NV)
    r = np.stack([np.interp(rows[::-1], ys[::-1], rad[k][::-1])[::-1] for k in range(nu)])
    r = np.maximum(r, 0.04)
    grid = np.array([[[c[0] + D[k, 0] * r[k, j], rows[j], c[1] + D[k, 2] * r[k, j]] for j in range(NV)] for k in range(nu)])
    cols = list(range(0, nu, 2)); specs = []
    for i, k in enumerate(cols):
        specs.append((f'Skirt{i}_U', 'Hips', grid[k, 0])); specs.append((f'Skirt{i}_L', 2 * i, grid[k, 3]))
    doc, binary, idx = add_joints(a.scaffold, specs)
    a.out.mkdir(parents=True, exist_ok=True); glb_write(a.out / 'scaffold.glb', doc, binary)
    legs = {s: [names.index(s + n) for n in ('UpLeg', 'Leg', 'Foot')] for s in ('Left', 'Right')}
    C = {'tiers': 2, 'mode': 'cloak' if a.cloak else 'slit' if a.slit else 'bell', 'cloak_margin': a.cloak_margin if a.cloak else None, 'joints': [n for n, _, _ in specs], 'joint_index': idx[0::2], 'lower_index': idx[1::2],
         'theta': th[cols].tolist(), 'sim_theta': th.tolist(), 'bone_cols': cols, 'nu': nu, 'nv': NV, 'grid0': np.round(grid, 6).tolist(),
         'centre': c.tolist(), 'top': top, 'hem': hem, 'mid': float(rows[3]), 'fade': a.fade, 'hips': names.index('Hips'), 'legs': legs,
         'bake_scale': G['sc'], 'scaffold': str(a.scaffold), 'skirt_gaussians_in_reference': int(G['skirt'].sum())}
    (a.out / 'skirt.json').write_text(json.dumps(C, indent=1) + '\n')
    print(json.dumps({'joints': len(specs), 'tiers': 2, 'mode': C['mode'], 'top': round(top, 3), 'hem': round(hem, 3),
                      'reach_at_top_cm': np.round(100 * r[cols, 0], 1).tolist(), 'reach_at_hem_cm': np.round(100 * r[cols, -1], 1).tolist(),
                      'skirt': C['skirt_gaussians_in_reference']}))


def bind_weights(W, P, skirt, C):
    """Dense weights W (Gaussians x bones; scaffold-frame positions P) with every skirt Gaussian moved onto the two columns either
    side of it, keeping a share of its own weights near the top. Returns the new weights and a short report."""
    ji = C['joint_index']; N = len(ji); lo = C.get('lower_index'); nb = max(W.shape[1], max(ji + (lo or [])) + 1)
    W2 = np.zeros((len(W), nb)); W2[:, :W.shape[1]] = W
    th0 = C['theta'][0]
    if C.get('mode') == 'cloak':   # an open sheet: between the two nearest columns, clamped at the edges, no wrap
        u = np.clip((azimuth(P, C['centre']) - th0) / (C['theta'][1] - th0), 0, N - 1 - 1e-6)
        k0 = np.floor(u).astype(int); k1 = k0 + 1; f = u - k0
    else:
        u = np.mod(azimuth(P, C['centre']) - th0, 2 * np.pi) / (2 * np.pi / N)
        k0 = np.floor(u).astype(int) % N; k1 = (k0 + 1) % N; f = u - np.floor(u)
    keep = smooth((P[:, 1] - (C['top'] - C['fade'])) / C['fade'])
    r = np.where(skirt)[0]; kp = keep[r]
    if C.get('mode') == 'slit':   # a skirt that parts at the front: below the hips, no blend across the front
        cross = (k0[r] == N - 1) & (P[r, 1] < C['top'] - 0.11)
        f = f.copy(); f[r[cross]] = np.where(f[r[cross]] < 0.5, 0.0, 1.0)
    W2[r] *= kp[:, None]
    if lo is None:
        np.add.at(W2, (r, np.array(ji)[k0[r]]), (1 - kp) * (1 - f[r])); np.add.at(W2, (r, np.array(ji)[k1[r]]), (1 - kp) * f[r])
        return W2, {'columns': N, 'fade_m': C['fade'], 'on_columns_wholly': int((skirt & (keep < 0.01)).sum())}
    hw = min(0.11, 0.15 * (C['top'] - C['hem']))   # the upper bone hands over to the lower one across the cloth's knee
    tl = smooth((C['mid'] + hw - P[r, 1]) / (2 * hw))
    for kk, share in ((k0[r], 1 - f[r]), (k1[r], f[r])):
        np.add.at(W2, (r, np.array(ji)[kk]), (1 - kp) * share * (1 - tl)); np.add.at(W2, (r, np.array(lo)[kk]), (1 - kp) * share * tl)
    return W2, {'columns': N, 'tiers': 2, 'fade_m': C['fade'], 'on_columns_wholly': int((skirt & (keep < 0.01)).sum())}


def project_capsule(pts, a, b, rad):
    """Push points out of the capsule a-b of radius rad."""
    ab = b - a; ab2 = float(ab @ ab)
    t = np.zeros(len(pts)) if ab2 < 1e-12 else np.clip(((pts - a) @ ab) / ab2, 0, 1)
    d = pts - (a + t[:, None] * ab); dist = np.linalg.norm(d, axis=1); hit = dist < rad
    pts[hit] = (a + t[hit, None] * ab) + d[hit] / np.maximum(dist[hit, None], 1e-9) * rad
    return pts


def frame_down_out(down, out):
    """Orthonormal frame: column 1 up (minus down), column 2 the outward direction made perpendicular to it."""
    y = -down / max(np.linalg.norm(down), 1e-9); z = out - (out @ y) * y
    if np.linalg.norm(z) < 1e-6:
        z = np.array([0.0, 0.0, 1.0]) - y[2] * y
    z /= max(np.linalg.norm(z), 1e-9); x = np.cross(y, z); return np.stack([x / max(np.linalg.norm(x), 1e-9), y, z], 1)


def cloth_sheet(C, f, weft=3e-4):
    """The sheet's distance constraints (index a, index b, rest length, compliance) from the rest grid in prep units."""
    nu, nv = C['nu'], C['nv']; g = np.array(C['grid0']) * f; slit = C.get('mode') == 'slit'; cloak = C.get('mode') == 'cloak'; E = []

    def edge(k1, j1, k2, j2, comp):
        L = float(np.linalg.norm(g[k1, j1] - g[k2, j2]))
        if L > 1e-5:
            E.append((k1 * nv + j1, k2 * nv + j2, L, comp))
    for k in range(nu):
        for j in range(nv):
            if j + 1 < nv: edge(k, j, k, j + 1, 1e-5)          # warp: barely stretches   # noqa: E701
            if j + 2 < nv: edge(k, j, k, j + 2, 2e-3)          # bend down the warp       # noqa: E701
            kn = (k + 1) % nu
            if not (slit and k == nu - 1 and j >= 2) and not (cloak and k == nu - 1):   # a cloak's sheet is open: no wrap round the front
                edge(k, j, kn, j, weft)                         # weft
                if j + 1 < nv: edge(k, j, kn, j + 1, 1e-3); edge(kn, j, k, j + 1, 1e-3)   # shear   # noqa: E701,E702
            if not (slit and k in (nu - 2, nu - 1) and j >= 2) and not (cloak and k >= nu - 2):
                edge(k, j, (k + 2) % nu, j, 4e-3)               # bend across
    E = np.array(E, dtype=object)
    return g, E[:, 0].astype(int), E[:, 1].astype(int), E[:, 2].astype(float), E[:, 3].astype(float)


def drive_cloth(a, C, prep):
    """Two tiers: solve the cloth sheet per clip and set each column's upper and lower bone from it."""
    f = prep['metres_per_bake_unit'] * C['bake_scale']
    g, ia, ib, l0, comp = cloth_sheet(C, f, a.cloth_weft); nu, nv = C['nu'], C['nv']; hi = C['hips']
    inv_m = np.ones(nu * nv); inv_m[0::nv] = 0.0; wsum = inv_m[ia] + inv_m[ib]; free = inv_m > 0
    radius = {'UpLeg': 0.082, 'Leg': 0.064}; grav = np.array([0.0, -a.cloth_gravity * f, 0.0]); sub = 8
    c0 = np.array([C['centre'][0], 0.0, C['centre'][1]]) * f
    report = {}
    for tag in a.tags.split(','):
        for clip, rec in prep['clips'].items():
            fp = a.prep / f'poses-{tag}-{clip}.npz'; rigid = a.prep / f'poses-{tag}-{clip}.rigid.npz'
            if not rigid.exists():
                rigid.write_bytes(fp.read_bytes())
            d = dict(np.load(rigid)); S = d['skin'].astype(np.float64); P = d['joints'].astype(np.float64); F = len(S)
            caps = [[(P[fr, C['legs'][s][0]], P[fr, C['legs'][s][1]], (radius['UpLeg'] + a.cloth_gap) * f),
                     (P[fr, C['legs'][s][1]], P[fr, C['legs'][s][2]], (radius['Leg'] + a.cloth_gap) * f)] for fr in range(F) for s in ('Left', 'Right')]
            caps = [caps[2 * fr] + caps[2 * fr + 1] for fr in range(F)]
            rigid_at = lambda fr: g.reshape(-1, 3) @ S[fr, hi, :3, :3].T + S[fr, hi, :3, 3]   # noqa: E731
            pos = rigid_at(0).copy(); vel = np.zeros_like(pos); dt = 1.0 / rec['fps'] / sub; rec_pos = np.zeros((F, nu * nv, 3))
            for step in range(2 * F):   # two passes; the second is kept, so a looping clip loops
                fr = step % F; nx = (fr + 1) % F; r0, r1 = rigid_at(fr), rigid_at(nx)
                for s in range(sub):
                    al = (s + 1) / sub; prev = pos.copy(); top = (1 - al) * r0[0::nv] + al * r1[0::nv]
                    vel[free] = vel[free] * 0.92 + grav * dt + (r0[free] - pos[free]) * (a.cloth_home * dt); pos[free] += vel[free] * dt
                    pos[0::nv] = top
                    for _ in range(5):
                        dd = pos[ib] - pos[ia]; dist = np.linalg.norm(dd, axis=1)
                        lam = (dist - l0) / np.maximum(wsum + comp / (dt * dt), 1e-12)
                        corr = dd / np.maximum(dist[:, None], 1e-9) * (0.45 * lam[:, None]); dp = np.zeros_like(pos)
                        np.add.at(dp, ia, corr * inv_m[ia, None]); np.add.at(dp, ib, -corr * inv_m[ib, None]); pos += dp; pos[0::nv] = top
                    for (a0, b0, rr), (a1, b1, _) in zip(caps[fr], caps[nx]):
                        pos = project_capsule(pos, (1 - al) * a0 + al * a1, (1 - al) * b0 + al * b1, rr)
                    pos[0::nv] = top; vel = (pos - prev) / dt
                if step >= F:
                    rec_pos[fr] = pos
            sm = (0.2 * np.roll(rec_pos, -1, 0) + 0.6 * rec_pos + 0.2 * np.roll(rec_pos, 1, 0)).reshape(F, nu, nv, 3)
            dev = []
            for i, k in enumerate(C['bone_cols']):
                p0t, p0m, p0h = g[k, 0], g[k, 3], g[k, nv - 1]; out0 = np.array([p0t[0] - c0[0], 0.0, p0t[2] - c0[2]])
                R0u, R0l = frame_down_out(p0m - p0t, out0), frame_down_out(p0h - p0m, out0)
                for fr in range(F):
                    of = S[fr, hi, :3, :3] @ out0; pt, pm, ph = sm[fr, k, 0], sm[fr, k, 3], sm[fr, k, nv - 1]
                    for ji, R0, p0, q0, q1 in ((C['joint_index'][i], R0u, p0t, pt, pm), (C['lower_index'][i], R0l, p0m, pm, ph)):
                        dR = frame_down_out(q1 - q0, of) @ R0.T; M = np.eye(4); M[:3, :3] = dR; M[:3, 3] = q0 - dR @ p0
                        S[fr, ji] = M; P[fr, ji] = q0
                dev.append(float(np.linalg.norm(sm[:, k, nv - 1] - np.array([rigid_at(fr).reshape(nu, nv, 3)[k, nv - 1] for fr in range(F)]), axis=1).max() / f))
            d['skin'] = S.astype(np.float32); d['joints'] = P.astype(np.float32); np.savez_compressed(fp, **d)
            report[f'{tag} {clip}'] = np.round(100 * np.array(dev), 1).tolist()
    print(json.dumps({'hem_moved_cm_max_per_column': report}))


def cmd_drive(a):
    C = json.loads(a.skirt.read_text()); ji = C['joint_index']; N = len(ji); hi = C['hips']
    prep = json.loads((a.prep / 'prep.json').read_text())
    if C.get('tiers') == 2:
        return drive_cloth(a, C, prep)
    f = prep['metres_per_bake_unit'] * C['bake_scale']   # prep units per scaffold metre (1 when prep ran with --metres 1/scale)
    th = np.array(C['theta']); D = np.stack([np.sin(th), np.zeros(N), np.cos(th)], 1)
    AX = np.stack([-D[:, 2], np.zeros(N), D[:, 0]], 1)    # turning the downward direction toward D
    c3 = np.array([C['centre'][0], 0, C['centre'][1]]) * f
    ys = np.array(C['ys'])[::-1] * f; rad = np.array(C['rad'])[:, ::-1] * f; piv = np.array(C['pivots']) * f
    top, hem, gap = C['top'] * f, C['hem'] * f, a.gap * f
    t = np.linspace(0, 1, 12)
    segs = [(C['legs'][s][i], C['legs'][s][i + 1], LEG_R[n] * f) for s in ('Left', 'Right') for i, n in enumerate(('UpLeg', 'Leg'))]
    rest = {int(k): np.array(v) * f for k, v in C['rest'].items()}

    def samples(pos):   # points along the thighs and shins, with their capsule radius
        pts = [pos[ja] + t[:, None] * (pos[jb] - pos[ja]) for ja, jb, _ in segs]
        return np.concatenate(pts), np.repeat([r for _, _, r in segs], len(t))
    q0, rr = samples(rest)
    reach = lambda k, y: np.interp(y, ys, rad[k])   # noqa: E731
    pr0 = (q0 - c3) @ D.T                                                   # samples x columns
    clr = np.minimum(rr[:, None] + gap, np.stack([reach(k, q0[:, 1]) for k in range(N)], 1) - pr0)   # the room each had at rest

    def need(q):   # the outward turn of each column that keeps these leg points inside the skirt
        pr = (q - c3) @ D.T; out = np.zeros(N)
        ok = (q[:, 1] <= top - 0.02 * f) & (q[:, 1] >= hem - 0.05 * f)
        for k in range(N):
            dr = reach(k, q[ok, 1]) - rad[k, -1]; h = top - q[ok, 1]
            want = pr[ok, k] + clr[ok, k] - rad[k, -1]                      # outward offset needed, from the pivot
            rho = np.hypot(dr, h); phi = np.arctan2(dr, h)
            al = np.arcsin(np.clip(want / np.maximum(rho, 1e-6), -1, 1)) - phi
            al = al[want > dr]
            out[k] = min(float(al.max()), a.max) if len(al) else 0.0
        return np.maximum(out, 0)
    rest_need = need(q0)
    report = {}
    for tag in a.tags.split(','):
        for clip, rec in prep['clips'].items():
            fp = a.prep / f'poses-{tag}-{clip}.npz'; rigid = a.prep / f'poses-{tag}-{clip}.rigid.npz'
            if not rigid.exists():
                rigid.write_bytes(fp.read_bytes())
            d = dict(np.load(rigid)); S = d['skin'].astype(np.float64); P = d['joints'].astype(np.float64); F = len(S)
            req = np.zeros((F, N))
            for fr in range(F):
                inv = np.linalg.inv(S[fr, hi]); pos = {j: (inv @ np.r_[P[fr, j], 1.0])[:3] for j in rest}
                req[fr] = need(samples(pos)[0])
            dt = 1.0 / rec['fps']; al = req[0].copy(); out = np.zeros_like(req)
            for _ in range(2):
                for fr in range(F):
                    al = np.maximum(req[fr], al - a.release * dt); out[fr] = al
            for _ in range(N // 2 if a.spread > 0 else 0):   # never less than a share of a neighbour's turn: only adds, so the legs stay inside
                out = np.maximum(out, a.spread * np.maximum(np.roll(out, 1, 1), np.roll(out, -1, 1)))
            for k in range(N):
                for fr in range(F):
                    R = Rot.from_rotvec(AX[k] * out[fr, k]).as_matrix()
                    L = np.eye(4); L[:3, :3] = R; L[:3, 3] = piv[k] - R @ piv[k]
                    S[fr, ji[k]] = S[fr, hi] @ L; P[fr, ji[k]] = (S[fr, ji[k]] @ np.r_[piv[k], 1.0])[:3]
            d['skin'] = S.astype(np.float32); d['joints'] = P.astype(np.float32); np.savez_compressed(fp, **d)
            report[f'{tag} {clip}'] = np.round(np.degrees(out.max(0)), 1).tolist()
    print(json.dumps({'rest_turn_deg': np.round(np.degrees(rest_need), 2).tolist(), 'max_turn_deg_per_column': report}))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); sp = ap.add_subparsers(dest='cmd', required=True)
    r = sp.add_parser('rig'); r.add_argument('scaffold', type=Path); r.add_argument('transform', type=Path); r.add_argument('splat', type=Path)
    r.add_argument('binding', type=Path); r.add_argument('out', type=Path); r.add_argument('--columns', type=int, default=8)
    r.add_argument('--tiers', type=int, choices=(1, 2), default=2, help='2: upper and lower bone per column, driven by the cloth sheet (the default); 1: one rigid bone per column')
    r.add_argument('--slit', action='store_true', help='two tiers: the skirt parts at the front (a wrap skirt)')
    r.add_argument('--cloak', action='store_true', help='two tiers: a long cloak hanging behind the legs - an open sheet over its own arc (npc-472)')
    r.add_argument('--cloak-margin', type=float, default=0.02, help='--cloak: metres clear of the thigh and shin capsules a Gaussian must stand to be cloak')
    r.add_argument('--fade', type=float, default=0.06, help='metres below the top over which a skirt Gaussian moves from its own weights onto the columns')
    d = sp.add_parser('drive'); d.add_argument('prep', type=Path); d.add_argument('skirt', type=Path); d.add_argument('--tags', default='bound400k,body')
    d.add_argument('--gap', type=float, default=0.02, help='metres kept between a leg capsule and the skirt'); d.add_argument('--release', type=float, default=2.5, help='radians a second a column falls back')
    d.add_argument('--max', type=float, default=0.9, help='largest outward turn of a column, radians')
    d.add_argument('--spread', type=float, default=0.7, help="each column turns at least this share of its neighbours' turn (0: off)")
    d.add_argument('--cloth-gap', type=float, default=0.018, help='two tiers: metres kept between the leg capsules and the cloth sheet')
    d.add_argument('--cloth-home', type=float, default=18.0, help='two tiers: per second, how hard the sheet is pulled back to its rest shape')
    d.add_argument('--cloth-weft', type=float, default=3e-4, help='two tiers: compliance of the edges round the skirt (higher: the circumference gives)')
    d.add_argument('--cloth-gravity', type=float, default=9.81, help='two tiers: m/s^2 on the sheet')
    a = ap.parse_args()
    {'rig': cmd_rig, 'drive': cmd_drive}[a.cmd](a)


if __name__ == '__main__':
    main()
