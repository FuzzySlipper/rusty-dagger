#!/usr/bin/env python3
"""Graft regions of the free round-1 fit (unbound, so sharper) onto a bound runtime body: the face and the hands. The
static-npc graft step runs it (moved from asset-pipeline tools/splat-graft on 2026-11-23, when Patch shipped npc-023 grafted).

    graft_free.py BASE_DIR R1_SPLAT TRANSFORM SCAFFOLD.glb OUT_DIR [--regions face,hands] [--face-cone 70] [--reach 0.06]
                  [--min-alpha 0.08]

BASE_DIR is a bound size (bound_run.sh distil/clean: splat.npz in bake units and binding.npz on the scaffold's bones).
R1_SPLAT is the free round-1 fit of the same bake (pipeline.sh ... r1; the same cameras, so it lands in register through
TRANSFORM, the score step's transform.json). OUT_DIR gets splat.npz and binding.npz in the same form, so bound.py playable
and bound_to_glb.py take it as another size, plus graft.json.

Why: the bound bake holds every Gaussian near the clay scaffold. Where the clay is a poor stand-in, the bake is a blur and the
free fit is not:
- the face inside 021's too-thick hood (docs/r1-face-graft.md: Patch, "bloody amazing");
- npc-023's hands, which are mittens on Meshy's clay: the free r0 fit has fingers and the bound bake a smooth blob
  (2026-11-22).

The 021 graft (graft_r1_face.py) needed face outlines traced by hand and a GPU context. This one finds its regions from the
rig, so it runs on any character:
- **face:** body Gaussians led by Head (binding weight), in the cone of --face-cone degrees round +z (the scaffold's front)
  from the Head joint.
- **hands:** a sphere of --hand-radius (10 cm) round each hand joint: body Gaussians whose extent (2 sigma) reaches into it,
  whatever bone leads them, and the free fit's Gaussians inside it.
- **detail:** after the face and hands, 4 cm cells over the surface where the free fit holds the most colour detail the
  runtime body lacks (opacity-weighted colour spread), taken in order of detail gained per Gaussian added until
  --detail-budget of the body's count is spent (Patch 2026-11-22: "where it gives the most impact... judiciously").
- **fill:** free-fit Gaussians where the body has none: alpha above 0.3, more than --fill-gap from every Gaussian of the full
  bound bake (--dense; the runtime body is too sparse to say where nothing is) but
  within --reach, in a cluster (8 or more within 1.5 cm, so floaters stay out). That is surface the bound bake's shell kept
  out (017's robe, more than 6 cm outside Meshy's clay, see-through in the bound bake). They take full opacity and the
  nearest body Gaussians' bones.

How:
1. Each body Gaussian's membership is its region weight, averaged over its 8 nearest neighbours so the edge is soft.
2. A free-fit Gaussian (alpha above --min-alpha) takes the mean membership of its 8 nearest body Gaussians. It is dropped if
   the nearest is more than --reach away (scaffold metres), so floaters and the inside of a hood stay out.
3. Optical thickness is crossfaded as in graft_r1_face.py: alpha' = 1 - (1 - alpha)^f, with f = 1 - membership for the body
   and f = membership for the graft. Gaussians with f under 0.002 are dropped.
4. Each grafted Gaussian takes the bone weights of its 4 nearest body Gaussians, merged, so it moves as the body around it
   does: rigidly with Head on the face, with the hand bones on the hands.
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree


def rig_joints(glb):
    d = Path(glb).read_bytes(); n = struct.unpack_from('<I', d, 12)[0]; g = json.loads(d[20:20 + n]); b = d[20 + n + 8:]
    sk = g['skins'][0]; a = g['accessors'][sk['inverseBindMatrices']]; v = g['bufferViews'][a['bufferView']]
    ibm = np.frombuffer(b, np.float32, a['count'] * 16, v.get('byteOffset', 0) + a.get('byteOffset', 0)).reshape(-1, 4, 4).transpose(0, 2, 1)
    names = [g['nodes'][j]['name'].split(':')[-1] for j in sk['joints']]
    return names, np.linalg.inv(ibm.astype(np.float64))[:, :3, 3]


def wstd(c, wt):
    m = (c * wt[:, None]).sum(0) / wt.sum()
    return float(np.sqrt(((c - m) ** 2 * wt[:, None]).sum(0).sum() / wt.sum()))


def detail_cells(a, base, pb, r1, to_sc, raw, s):
    """The detail region: 4 cm cells over the body's surface, ranked by colour detail gained per Gaussian added.

    In each cell, detail is the opacity-weighted spread of colour (RGB standard deviation) among the Gaussians there: the free
    fit's surface Gaussians (alpha above 0.3, within 1.5 cm of the full bound bake's), against the runtime body's. Gain is the
    difference times the cell's surface (the full bake's Gaussians in it). Cost is the free-fit Gaussians it would add. Cells are
    taken in order of gain per cost until --detail-budget times the body's count is spent, skipping cells already in the face or
    hands."""
    if not a.dense:
        raise SystemExit('graft_free: detail needs --dense (the full bound bake)')
    dz = dict(np.load(a.dense)); dA = 1 / (1 + np.exp(-dz['opacities'])); dp = to_sc(dz['means'])[dA > 0.3]
    ar1 = 1 / (1 + np.exp(-r1['opacities'])); pr = to_sc(r1['means'])
    near = (ar1 > 0.3) & (cKDTree(dp).query(pr, k=1)[0] < 0.015)
    take_r = (ar1 > a.min_alpha) & (cKDTree(dp).query(pr, k=1)[0] < 0.015)
    cs = 0.04; key = lambda P: np.floor(P / cs).astype(np.int64)
    kb, kd, kr, kt = key(pb), key(dp), key(pr[near]), key(pr[take_r])
    ab = 1 / (1 + np.exp(-base['opacities']))
    def groups(k):
        u, inv = np.unique(k, axis=0, return_inverse=True); return {tuple(x): np.flatnonzero(inv.ravel() == i) for i, x in enumerate(u)}
    gb, gd, gr = groups(kb), groups(kd), groups(kr)
    cost = {tuple(x): c for x, c in zip(*np.unique(kt, axis=0, return_counts=True))}
    rows = []
    for cell, idx in gr.items():
        if len(idx) < 8 or cell not in gb or len(gb[cell]) < 3 or (cell[1] + 1) * cs < 0.08:   # not the floor: free-fit shadow there
            continue
        bi = gb[cell]
        if raw[bi].mean() > 0.5:
            continue
        d_r = wstd(np.clip(r1['rgb'][near][idx], 0, 1), ar1[near][idx]); d_b = wstd(np.clip(base['rgb'][bi], 0, 1), ab[bi])
        gain = max(d_r - d_b, 0) * len(gd.get(cell, ()))
        rows.append((gain / max(cost.get(cell, 1), 1), gain, cell, cost.get(cell, 0), d_r, d_b))
    rows.sort(reverse=True); budget = a.detail_budget * len(pb); spent = 0; chosen = []
    for r_ in rows:
        if r_[1] <= 0 or spent + r_[3] > budget:
            continue
        chosen.append(r_); spent += r_[3]
    cells = {c[2] for c in chosen}
    raw_det = np.array([tuple(k) in cells for k in kb], float)
    rep = {'cells_ranked': len(rows), 'cells_chosen': len(chosen), 'budget': int(budget), 'cost': int(spent),
           'top': [{'centre_m': [round((c + 0.5) * cs, 2) for c in r_[2]], 'detail_free': round(r_[4], 3), 'detail_body': round(r_[5], 3), 'added': int(r_[3])} for r_ in chosen[:12]]}
    return raw_det, rep, take_r


def attenuate(logit, f):
    """alpha' = 1 - (1 - alpha)^f, back to a logit; the exact logit where f >= 1."""
    a = 1 / (1 + np.exp(-logit)); a2 = -np.expm1(np.clip(f, 0, 1) * np.log1p(-np.minimum(a, 1 - 1e-7)))
    a2 = np.clip(a2, 1e-7, 1 - 1e-7)
    return np.where(f >= 1, logit, np.log(a2) - np.log1p(-a2))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('base', type=Path); ap.add_argument('r1'); ap.add_argument('transform'); ap.add_argument('scaffold'); ap.add_argument('out', type=Path)
    ap.add_argument('--regions', default='face,hands'); ap.add_argument('--face-cone', type=float, default=70.0)
    ap.add_argument('--reach', type=float, default=0.06); ap.add_argument('--min-alpha', type=float, default=0.08)
    ap.add_argument('--hidden', type=float, default=0.015, help='leave out free-fit Gaussians this far inside its own surface from every side')
    ap.add_argument('--hand-radius', type=float, default=0.10, help='hands: the sphere round each hand joint (m)')
    ap.add_argument('--detail-budget', type=float, default=0.15, help='detail: Gaussians to add, as a share of the body')
    ap.add_argument('--fill-gap', type=float, default=0.015, help="fill: free-fit Gaussians at least this far from the full bound bake's")
    ap.add_argument('--dense', help='fill: the full bound bake splat.npz (bake units), which says where the bound body has nothing')
    a = ap.parse_args(); regions = a.regions.split(',')
    base = dict(np.load(a.base / 'splat.npz')); bind = dict(np.load(a.base / 'binding.npz')); r1 = dict(np.load(a.r1))
    t = json.load(open(a.transform)); s, R, tr = t['scale'], np.array(t['rotation']), np.array(t['translation'])
    to_sc = lambda m: (m - tr) @ R / s                                       # bake units -> scaffold metres
    names, J = rig_joints(a.scaffold); ix = {n: i for i, n in enumerate(names)}
    pb = to_sc(base['means']); bones = bind['bones'].astype(np.int64); w = bind['weights'].astype(np.float64)
    wsum = lambda bone: np.where(bones == bone, w, 0).sum(1)
    mem = np.zeros(len(pb)); parts = {}
    if 'face' in regions:
        h = ix['Head']; v = pb - J[h]; cosf = v[:, 2] / np.maximum(np.linalg.norm(v, axis=1), 1e-6)
        c0 = np.cos(np.radians(a.face_cone)); front = np.clip((cosf - c0) / max(1 - c0, 1e-6) * 3, 0, 1)
        parts['face'] = wsum(h) * front
    if 'hands' in regions:
        # a sphere of --hand-radius round each hand joint, whatever bone leads it, counting a Gaussian by its extent (2 sigma)
        # reaching in: npc-023's hand from the side was blurred by large Gaussians on Hips and Spine (a short rig has no arm bones)
        # whose centres lie off the hand, which a bone rule never removed (Patch 2026-11-22). The bones alone also took the
        # skirt beside hanging hands (39k Gaussians).
        sig = np.exp(base['scales']).max(1) / s
        parts['hands'] = np.max([np.clip((a.hand_radius - (np.linalg.norm(pb - J[ix[n]], axis=1) - 2 * sig)) / 0.03, 0, 1)
                                 for n in ('LeftHand', 'RightHand') if n in ix], axis=0)
    raw = np.clip(sum(parts.values()), 0, 1) if parts else np.zeros(len(pb))
    detail = {}; raw_det = np.zeros(len(pb)); near_surf = None
    if 'detail' in regions:   # where the free fit holds the most colour detail the runtime body lacks, within a budget
        raw_det, detail, near_surf = detail_cells(a, base, pb, r1, to_sc, raw, s)
    tree = cKDTree(pb); _, nn8 = tree.query(pb, k=8); mem = np.maximum(raw[nn8].mean(1), raw_det[nn8].mean(1))
    # the free fit's Gaussians near the regions
    ar1 = 1 / (1 + np.exp(-r1['opacities'])); pr = to_sc(r1['means'])
    dist, nnr = tree.query(pr, k=8); md_fh = raw[nnr].mean(1); md_det = raw_det[nnr].mean(1)
    if 'hands' in regions:   # the free fit's Gaussians inside the hand spheres by their own position, not their neighbours'
        md_fh = np.maximum(md_fh, np.max([np.clip((a.hand_radius - np.linalg.norm(pr - J[ix[n]], axis=1)) / 0.03, 0, 1)
                                          for n in ('LeftHand', 'RightHand') if n in ix], axis=0))
    take = (ar1 > a.min_alpha) & (dist[:, 0] < a.reach) & (md_fh > 0.002)
    if near_surf is not None:   # detail cells take only the free fit's surface (within 1.5 cm of the full bound bake's)
        take = take | ((ar1 > a.min_alpha) & near_surf & (md_det >= 0.25))   # a quarter inside a chosen cell: the edge spill kept small
    md = np.maximum(md_fh, md_det)
    fill = np.zeros(len(pr), bool)
    if 'fill' in regions:   # where the bound body has nothing: free-fit surface the shell kept out (017's robe, more than 6 cm out)
        if not a.dense:
            raise SystemExit('graft_free: fill needs --dense (the full bound bake: the runtime body is too sparse to say where nothing is)')
        dz = dict(np.load(a.dense)); dz_p = to_sc(dz['means'])[1 / (1 + np.exp(-dz['opacities'])) > 0.1]
        gap = cKDTree(dz_p).query(pr, k=1)[0]
        cand = (ar1 > 0.3) & (gap > a.fill_gap) & (dist[:, 0] < a.reach)
        if cand.any():
            dens = cKDTree(pr[ar1 > 0.3]).query_ball_point(pr[cand], r=0.015, return_length=True)
            ci = np.flatnonzero(cand)[dens >= 8]
            # only what shows outside the bound body: in front of it (or beyond its outline) in at least 15% of 96 views. A free fit
            # also fills the inside of the body, which the bound bake never does, and that must stay out
            import sys as _s; _s.path.insert(0, str(Path(__file__).resolve().parent)); import core_extract as CE
            dA = 1 / (1 + np.exp(-dz['opacities'])); keepd = dA > 0.1
            share = CE.violations(to_sc(dz['means'])[keepd], np.exp(dz['scales'])[keepd] / s, dA[keepd], pr[ci],
                                  CE.views(range(0, 360, 15), (-15, 0, 15, 45)), 0.006, 0.0)
            fill[ci[share >= 0.15]] = True
        md = np.where(fill, 1.0, md); take = take | fill
    # drop what no one sees: free-fit Gaussians more than --hidden behind the free fit's own surface from every one of 96
    # directions (a free fit packs the inside of a body; npc-023's hand spheres took 37k Gaussians, most of them inside)
    hidden = 0
    if a.hidden > 0 and take.any():
        import sys as _s; _s.path.insert(0, str(Path(__file__).resolve().parent)); import core_extract as CE
        ti = np.flatnonzero(take); lo_, hi_ = pr[ti].min(0) - 0.25, pr[ti].max(0) + 0.25
        loc = np.all((pr > lo_) & (pr < hi_), 1)
        mg = CE.clearance(pr[loc], np.exp(r1['scales'][loc]) / s, ar1[loc], pr[ti], CE.views(range(0, 360, 15), (-15, 0, 15, 45)), 0.004, 0.0)
        inner = mg > a.hidden; take[ti[inner]] = False; hidden = int(inner.sum())
    # crossfade
    fb = 1 - mem; fd = md[take]
    keep_b = fb > 0.002; out = {}
    for k in ('means', 'scales', 'quats', 'rgb'):
        out[k] = np.concatenate([base[k][keep_b], r1[k][take]])
    out['opacities'] = np.concatenate([attenuate(base['opacities'][keep_b], fb[keep_b]), attenuate(r1['opacities'][take], fd)]).astype(np.float32)
    # bone weights for the graft: its 4 nearest body Gaussians', merged per bone
    nb = nnr[take][:, :4]; acc = {}
    W = np.zeros((take.sum(), len(names)))
    for k in range(4):
        np.add.at(W, (np.repeat(np.arange(len(nb)), 4), bones[nb[:, k]].ravel()), w[nb[:, k]].ravel() / 4)
    top = np.argsort(-W, 1)[:, :4]; tw = np.take_along_axis(W, top, 1); tw /= np.maximum(tw.sum(1, keepdims=True), 1e-9)
    ob = np.concatenate([bones[keep_b], top]).astype(np.int16); ow = np.concatenate([w[keep_b], tw]).astype(np.float32)
    a.out.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(a.out / 'splat.npz', **{k: v.astype(np.float32) for k, v in out.items()})
    np.savez_compressed(a.out / 'binding.npz', bones=ob, weights=ow)
    rep = {'base': str(a.base), 'r1': a.r1, 'regions': regions, 'face_cone_deg': a.face_cone, 'reach_m': a.reach,
           'base_gaussians': int(len(pb)), 'base_dropped': int((~keep_b).sum()), 'base_attenuated': int(((fb < 1) & keep_b).sum()),
           'grafted': int(take.sum()), 'left_out_hidden': hidden, 'filled': int(fill.sum()), 'total': int(len(out['means'])),
           'grafted_by_bone': {names[i]: int((top[:, 0] == i).sum()) for i in np.unique(top[:, 0])},
           'region_gaussians': {k: int((v > 0.5).sum()) for k, v in parts.items()}, 'detail': detail}
    (a.out / 'graft.json').write_text(json.dumps(rep, indent=1) + '\n')
    print(json.dumps(rep))


if __name__ == '__main__':
    main()
