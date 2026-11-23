#!/usr/bin/env python3
"""Cut a garment layer out of a clothed bound body and attach it to the same character's bare bound body (bound route).

    bound_layer.py CLOTHED.vsplat BARE_DIR NAME [--far 0.015] [--k 16] [--vote 0.5] [--margin 0.006] [--clothed-rig SCAFFOLD.glb]

Patch 2026-11-19: capes, cloaks and shawls are baked separately and attached to the runtime body as a layer (the Mobile-30 Vampire
Ancient way), not bound into one body where they take the legs' weights. The character is baked twice from orbits with the same stick
guide and seed: clothed (397) and bare (397-B, the garment painted out). BARE_DIR is the bare body's playable folder (09-game/bound/
playable or 08-final/bound/playable: character.json, its body .vsplat and scaffold.glb); CLOTHED.vsplat is the clothed body at the
matching size. Both are .vsplat in their own scaffold's frame (+Y up, facing +Z, the joints block = the scaffold's bind positions).

1. Register. The two scaffolds share their 36 joint names, so a similarity transform (Umeyama) from the clothed body's joints onto the
   bare body's starts it; trimmed ICP from the clothed body's Gaussians that lie on the bare body (the front, the arms, the legs) to
   their nearest bare Gaussians refines it.
2. Cut. Each body is bound within a few centimetres of its own scaffold mesh, and only the clothed scaffold has the garment. A
   clothed scaffold vertex whose distance to the bare scaffold (median over its K mesh neighbours) is more than FAR is garment
   surface; a clothed Gaussian is garment when most of its 8 nearest clothed vertices are. Nothing at the feet is kept, and a
   neighbour vote (share VOTE of K) removes specks and fills pinholes. Colour only trims it: a Gaussian whose local colour (median over
   its K opaque neighbours; the bake leaves black Gaussians inside cloth) is greener than red by GREEN is clothing under the garment
   (397's sea-green skirt flares wider under the shawl), and one lying on the bare body with the bare colour there (within SAME) is
   body. Above the neck joint (a hood) everything but what matches the bare body is garment: the bare hair and bun fill the hood, so
   its crown and back never stand off the bare scaffold (--no-hood for a garment without one). Pieces (Gaussians within LINK of each other) under PIECE of the garment are dropped. Colour alone cannot cut the garment: a shawl's shaded back has the skin's tone.
3. Weights on the bare rig (the layer uses the body's armature; its joints block is ignored by the player): above the neck joint on
   Head, blending through neck to the upper spine at the shoulders; below, the upper spine (Meshy 'Spine', Mixamo Spine2) with the
   clavicle of its side at the shoulder corners, falling off down the length to Hips at the hem, so the hem turns with the hips, not
   the legs (authoring/tools/people/splat/gsplat/layer_rig.py's weights mode, on the scaffold's bones).
4. Lift. Each layer Gaussian moves out along the bare scaffold's normal until every bare Gaussian within LIFT_R of that line (its
   centre plus twice its largest scale, at most 2 cm; forearms and hands left out) is MARGIN behind it (397's bun came through the hood and the shawl sat half inside her body, 2026-11-19); the lift is spread over its
   neighbours so the garment moves as one sheet. With a hood, the bare hair and bun behind the face are not cleared but covered: they
   move from the body to its 'under' part (BODY-covered.vsplat + BODY-under.vsplat, the layer 'covers': the Workbench shows 'under'
   only while no covering layer is on); the manifest keeps the whole body's name as bodyWhole.

Writes BARE_DIR/NAME-weights.vsplat, adds layers.NAME to BARE_DIR/character.json (defaultLayer NAME:weights) and BARE_DIR/NAME.json
(counts, registration residuals).
"""
import argparse, json, sys
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent / 'gsplat'))
from retarget_clip import read_glb                       # noqa: E402
import vsplat_to_glb as V                                # noqa: E402
from make_skinned_vsplat import write_vsplat             # noqa: E402


def umeyama(src, dst):
    ms, md = src.mean(0), dst.mean(0); a, b = src - ms, dst - md
    U, S, Vt = np.linalg.svd(b.T @ a / len(src)); D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt; s = (S * np.diag(D)).sum() / (a ** 2).sum(1).mean()
    return s, R, md - s * R @ ms


def raw_palette(path):
    import struct
    data = open(path, 'rb').read(); n = struct.unpack_from('<I', data, 16)[0]
    return json.loads(data[20:20 + n])['blocks']['boneIndex']['palette']


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('clothed', type=Path); ap.add_argument('bare', type=Path); ap.add_argument('name')
    ap.add_argument('--far', type=float, default=0.015, help="metres the clothed scaffold stands off the bare one (scaled by the figure height / 1.7)")
    ap.add_argument('--green', type=float, default=0.0, help='drop Gaussians whose local green exceeds red by this (0..1): clothes under a tan/brown garment; 1 to keep all')
    ap.add_argument('--same', type=float, default=0.1, help='drop Gaussians on the bare body whose local colour is within this of the bare colour there')
    ap.add_argument('--link', type=float, default=0.025, help='metres: Gaussians this close are one piece'); ap.add_argument('--piece', type=float, default=0.03, help='drop pieces under this share of the garment')
    ap.add_argument('--clothed-rig', type=Path, help="the clothed body's scaffold GLB (default: scaffold.glb beside CLOTHED.vsplat)")
    ap.add_argument('--k', type=int, default=16); ap.add_argument('--vote', type=float, default=0.5)
    ap.add_argument('--dump', type=Path, help='write the cut masks (npz) here, to see which rule kept or dropped a Gaussian')
    ap.add_argument('--no-hood', dest='hood', action='store_false', help='cut the head by the scaffolds like the rest (no hood)')
    ap.add_argument('--face', type=float, default=0.03, help='metres in front of the head joint where the hair a hood covers ends (the face stays)')
    ap.add_argument('--face-hull', type=float, default=0.9, help="the face's outline (front view), scaled about its centre: hood Gaussians in front of the face inside it are dropped")
    ap.add_argument('--margin', type=float, default=0.006, help='metres the layer stands clear of the bare body after the lift (negative: no lift)')
    ap.add_argument('--lift-r', type=float, default=0.035, help='metres sideways of a layer Gaussian that the bare body must be behind it')
    ap.add_argument('--lift-max', type=float, default=0.12, help='metres: the most a layer Gaussian is moved out')
    ap.add_argument('--off-body', type=float, default=0.0, help="metres (0: off): below the neck, a clothed Gaussian this far from the bare body's Gaussians and not of the bare colour there is garment too, where the two scaffolds lie together (452's cloak over the upper back)")
    ap.add_argument('--trim-colour', type=float, default=0.0, help="(0: off) below the neck, drop garment whose local colour is further than this from the garment's median (452: a band of the clothed skirt's hem)")
    ap.add_argument('--despeckle', type=float, default=0.0, help="(0: off) a layer Gaussian further than this from its neighbours' median colour takes that colour (the unseen inside of a cloak bakes in random colours)")
    a = ap.parse_args()
    man = json.loads((a.bare / 'character.json').read_text())
    whole = man.get('bodyWhole', man['body'])                     # a rerun reads the whole bare body, not the covered one
    B = V.read_vsplat(str(a.bare / whole)); A = V.read_vsplat(str(a.clothed))
    # the joints both rigs name (36 body joints; skirt bones after them may differ, 452 one tier against 452-B two, 2026-11-21)
    na = names_of(a.clothed_rig or a.clothed.parent / 'scaffold.glb'); nb = names_of(a.bare / man['rig'])
    if len(na) != len(A['joints']) or len(nb) != len(B['joints']):
        sys.exit('a rig does not match its body\'s joints block')
    common = [n for n in na if n in set(nb) and not n.startswith('Skirt')]
    ja = A['joints'][[na.index(n) for n in common]]; jb = B['joints'][[nb.index(n) for n in common]]
    if len(common) < 20:
        sys.exit(f'the two bodies share only {len(common)} joints')

    # 1. register
    s, R, t = umeyama(ja, jb)
    pa = (s * A['position'] @ R.T) + t
    ob = B['logit'] > 0; pb = B['position'][ob]; tb = cKDTree(pb)
    H = float(np.percentile(B['position'][:, 1], 99.5) - np.percentile(B['position'][:, 1], 0.5)); u = H / 1.7
    for _ in range(8):
        d, i = tb.query(pa); keep = d < np.percentile(d, 60)      # the part of the clothed body that lies on the bare one
        s2, R2, t2 = umeyama(pa[keep], pb[i[keep]]); pa = s2 * pa @ R2.T + t2
    d, i = tb.query(pa)
    reg = {'joint_rms_mm': float(np.sqrt(((s * ja @ R.T + t - jb) ** 2).sum(1).mean()) * 1000), 'joints_registered': len(common),
           'on_body_median_mm': float(np.median(d[d < np.percentile(d, 60)]) * 1000), 'figure_height': H}

    # 2. cut: where the clothed scaffold's surface stands off the bare scaffold's, every clothed Gaussian bound there is garment
    J = dict(zip(names_of(a.bare / man['rig']), B['joints']))
    va = mesh_vertices(a.clothed_rig or a.clothed.parent / 'scaffold.glb'); vb = mesh_vertices(a.bare / man['rig'])
    sT, RT, tT = umeyama(A['position'], pa); va = sT * va @ RT.T + tT                # the registration, applied to the clothed mesh
    dv, _ = cKDTree(vb).query(va); tv = cKDTree(va)
    _, nv = tv.query(va, k=a.k); off = np.median(dv[nv], 1)                          # smoothed over the mesh around each vertex
    _, ig = tv.query(pa, k=8); garment = np.median(off[ig], 1) > a.far * u
    oa = A['logit'] > 0; lin = A['color'].astype(np.float64) / 255
    _, ia = cKDTree(pa[oa]).query(pa, k=a.k); cA = np.median(lin[oa][ia], 1)          # median: the bake leaves black Gaussians in cloth
    _, ib = tb.query(pa, k=a.k); cB = np.median((B['color'][ob].astype(np.float64) / 255)[ib], 1)
    other = cA[:, 1] > cA[:, 0] + a.green                                             # green or teal: the clothes under the garment
    same = (d < 0.5 * a.far * u) & (np.linalg.norm(cA - cB, axis=1) < a.same)          # lies on the bare body and has its colour there
    dump = {'pa': pa, 'far': garment.copy(), 'other': other, 'same': same, 'cA': cA, 'cB': cB, 'd': d}
    garment &= ~other & ~same
    nk0 = J.get('neck', J.get('Neck'))[1]; body = pa[:, 1] < nk0
    if a.trim_colour > 0 and (garment & oa).any():   # Patch 2026-11-20: 452's cloak layer took a band of her skirt's hem
        g = np.median(cA[garment & oa], 0); dump['garment_median'] = g
        garment &= ~(body & (np.linalg.norm(cA - g, axis=1) > a.trim_colour))
    if a.off_body > 0:   # the cloak over 452's upper back stands 7 cm off the bare Gaussians where the scaffolds lie together
        g = np.median(cA[garment & oa], 0) if (garment & oa).any() else np.zeros(3)
        grow = body & (d > a.off_body * u) & (np.linalg.norm(cA - cB, axis=1) >= 3 * a.same) & ~other   # clearly not the bare colour
        if a.trim_colour > 0:
            grow &= np.linalg.norm(cA - g, axis=1) <= a.trim_colour
        dump['off_body'] = grow; garment |= grow
    if a.hood:   # above the neck the bare body's hair and bun fill the hood, so the hood's crown and back never stand off the bare
        nk = J.get('neck', J.get('Neck'))   # scaffold (397, 2026-11-19): there everything is garment but what matches the bare body (face, front hair)
        hood = (pa[:, 1] > nk[1]) & ~same & ~other & ((d > 0.5 * a.far * u) | (np.linalg.norm(cA - cB, axis=1) >= a.same))
        hd = J['Head']; Pb = B['position'][ob]                # the face's edge (chin, cheeks, hair at the brow) shaded by the hood is not
        face = (Pb[:, 1] > nk[1]) & (Pb[:, 1] < hd[1] + 0.08 * u) & (Pb[:, 2] > hd[2] + 0.05 * u)   # 'same': drop skin-coloured or dark
        skin = np.median(B['color'][ob][face].astype(np.float64) / 255, 0) if face.any() else np.ones(3)   # Gaussians in front of the head joint
        front = pa[:, 2] > hd[2] + 0.02 * u
        dump.update(hood=hood, front=front, skin=skin)
        garment |= hood & ~(front & ((np.linalg.norm(cA - skin, axis=1) < 0.18) | (cA.mean(1) < 0.12)))
    ankle = max(J['LeftFoot'][1], J['RightFoot'][1]) + 0.04 * u
    garment &= pa[:, 1] > ankle
    dump['prevote'] = garment.copy()
    tall = cKDTree(pa); _, iv = tall.query(pa, k=a.k)
    for _ in range(2):
        share = garment[iv].mean(1); garment = (garment & (share >= a.vote)) | (~garment & (share >= 0.85))
    from scipy.sparse.csgraph import connected_components                        # keep the garment's large pieces: a face set back in
    gi = np.nonzero(garment)[0]; pairs = cKDTree(pa[gi]).query_pairs(a.link * u, output_type='ndarray')   # a hood, specks at the hem
    from scipy.sparse import coo_matrix
    _, lab = connected_components(coo_matrix((np.ones(len(pairs)), (pairs[:, 0], pairs[:, 1])), shape=(len(gi), len(gi))), directed=False)
    size = np.bincount(lab); garment[gi[size[lab] < a.piece * len(gi)]] = False
    if a.dump:
        np.savez(a.dump, final=garment, **dump)
    hem = float(np.percentile(pa[garment & oa, 1], 1)) if garment.any() else ankle
    top = np.percentile(B['position'][:, 1], 99.5)
    crown = garment & oa & (pa[:, 1] > J['Head'][1] + 0.6 * (top - J['Head'][1]))
    ref = np.median(A['color'][crown], 0) / 255 if crown.any() else np.zeros(3)
    reg['mesh_vertices_off_bare'] = int((off > a.far * u).sum()); reg['mesh_vertices'] = len(va)
    idx = np.nonzero(garment)[0]

    # 3. weights on the bare rig, by name
    skin_names = names_of(a.bare / man['rig'], skin_order=True)
    sid = {n: k for k, n in enumerate(skin_names)}
    neck = 'neck' if 'neck' in sid else 'Neck'; upper = 'Spine' if 'Spine02' in sid else 'Spine2'; mid = 'Spine01' if 'Spine02' in sid else 'Spine1'
    p = pa[idx]; y = p[:, 1]
    sh_y = (J['LeftArm'][1] + J['RightArm'][1]) / 2; head_y = J['Head'][1]
    W = np.zeros((len(idx), 4)); Bn = np.zeros((len(idx), 4), np.int64)
    th = np.clip((y - sh_y) / max(head_y - sh_y, 1e-6), 0, 1)                      # 0 at the shoulders .. 1 at the head joint
    tl = np.clip((sh_y - y) / max(sh_y - hem, 1e-6), 0, 1)                         # 0 at the shoulders .. 1 at the hem
    side = np.clip((np.abs(p[:, 0] - J['Hips'][0]) - 0.06 * u) / (0.10 * u), 0, 1) * np.clip(1 - tl * 4, 0, 1)
    above = y >= sh_y
    w_head = np.where(above, th, 0); w_neck = np.where(above, 1 - th, 0) * 0.5
    w_up = np.where(above, (1 - th) * 0.5, (1 - tl) * (1 - side))
    w_cl = np.where(above, 0, (1 - tl) * side); w_mid = np.where(above, 0, np.clip(1 - np.abs(tl - 0.5) * 2, 0, 1) * 0.5)
    w_hip = np.where(above, 0, tl)
    cl = np.where(p[:, 0] > J['Hips'][0], sid['LeftShoulder'], sid['RightShoulder'])   # the scaffold's left is +X
    cand = [(np.full(len(idx), sid['Head']), w_head), (np.full(len(idx), sid[neck]), w_neck), (np.full(len(idx), sid[upper]), w_up),
            (cl, w_cl), (np.full(len(idx), sid[mid]), w_mid), (np.full(len(idx), sid['Hips']), w_hip)]
    bi = np.stack([c[0] for c in cand], 1); wi = np.stack([c[1] for c in cand], 1)
    top = np.argsort(-wi, 1)[:, :4]; Bn = np.take_along_axis(bi, top, 1); W = np.take_along_axis(wi, top, 1)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)

    # 4. lift: the layer must clear the bare body it is worn on (Patch 2026-11-19: 397's bun came through the hood and the shawl sat half
    # inside her; the hair the hood covers is moved to 'under' above, not cleared). Outward is the bare scaffold's surface normal; along it, the opaque bare Gaussians within LIFT_R sideways (centres
    # plus twice their largest scale; not the forearms and hands) must be MARGIN behind the layer. The lift is spread over the
    # layer (largest nearby, then averaged) so the garment moves out as one sheet rather than tearing round the bun.
    under = np.zeros(B['n'], bool)
    if a.hood:   # the hood hides the hair and bun behind the face (397's bun came through it): they move to the body's 'under' part,
        nk = J.get('neck', J.get('Neck')); hd = J['Head']; P0 = B['position']   # shown only while the hood is off (the Workbench's covers)
        top_y = np.percentile(P0[:, 1], 99.5)
        under = (P0[:, 1] > nk[1] + 0.03 * u) & ((P0[:, 2] < hd[2] + a.face * u) | (P0[:, 1] > top_y - 0.04 * u))
        # nothing of the hood in front of the face: the clothed bake leaves smears of the hood's inside and of its own face there
        fm = ob & (P0[:, 1] > nk[1]) & (P0[:, 2] > hd[2] + 0.05 * u) & ~under
        if fm.sum() > 10:
            from scipy.spatial import Delaunay
            fx = P0[fm][:, :2]; c0 = fx.mean(0); hull = Delaunay(c0 + (fx - c0) * a.face_hull)
            over = (p[:, 2] > hd[2] + a.face * u) & (hull.find_simplex(p[:, :2]) >= 0)
            keep = ~over; idx, p, W, Bn = idx[keep], p[keep], W[keep], Bn[keep]; reg['dropped_over_face'] = int(over.sum())
    lift = np.zeros(len(idx))
    if a.margin >= 0 and len(idx):
        doc, acc = read_glb(a.bare / man['rig'])
        prim = [q for m in doc['meshes'] for q in m['primitives'] if 'NORMAL' in q['attributes']]
        vp = np.concatenate([acc(q['attributes']['POSITION']) for q in prim]).astype(np.float64)
        vn = np.concatenate([acc(q['attributes']['NORMAL']) for q in prim]).astype(np.float64)
        dn, inn = cKDTree(vp).query(p, k=16); n = np.einsum('nk,nkj->nj', 1 / np.maximum(dn, 1e-4), vn[inn])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        names = names_of(a.bare / man['rig']); dom = B['bones'][np.arange(B['n']), np.argmax(B['weights'], 1)]
        arm = np.array(['ForeArm' in names[j] or 'Hand' in names[j] for j in dom])   # the forearms swing free of a garment
        keep_b = ob & ~arm & ~under                                    # what the layer covers is hidden under it, not cleared
        qb = B['position'][keep_b]; ext = np.minimum(2 * np.exp(B['log_scale'][keep_b].max(1)), 0.02 * u)
        if len(qb) > 80000:
            pick = np.random.default_rng(397).choice(len(qb), 80000, replace=False); qb, ext = qb[pick], ext[pick]
        tq = cKDTree(qb); reach = np.hypot(a.lift_r, a.lift_max) * u
        for k, nb in enumerate(tq.query_ball_point(p, reach)):   # a column along the normal: the 16k layer is sparse, and a bun between
            if not nb: continue                                  # its Gaussians is beside each one, not at it
            v = qb[nb] - p[k]; h = v @ n[k]; lat = np.linalg.norm(v - h[:, None] * n[k], axis=1)
            m = (lat < a.lift_r * u) & (h > -0.03 * u)
            if m.any(): lift[k] = max(0.0, float((h[m] + ext[nb][m]).max()) + a.margin * u)
        lift = np.minimum(lift, a.lift_max * u)
        _, nl = cKDTree(p).query(p, k=min(32, len(p)))
        lift = np.percentile(lift[nl], 90, axis=1); lift = lift[nl].mean(1)
        p = p + lift[:, None] * n
        reg['lift_mm'] = {'median': round(float(np.median(lift)) * 1000, 1), 'p90': round(float(np.percentile(lift, 90)) * 1000, 1),
                          'max': round(float(lift.max()) * 1000, 1)}

    # write in the bare body's frame and armature
    pal = raw_palette(a.bare / man['body'])
    q = A['quat_xyzw'][idx]; from scipy.spatial.transform import Rotation as Rot
    rot = Rot.from_quat(q); Rt = Rot.from_matrix(total_rotation(A['joints'], pa, A['position']))
    qw = (Rt * rot).as_quat()                                                       # xyzw
    sc = A['log_scale'][idx] + np.log(total_scale(A['position'], pa))
    out = a.bare / f'{a.name}-weights.vsplat'
    col = A['color'][idx].astype(np.float64) / 255
    if a.despeckle > 0 and len(idx) > 16:   # the inside of 452's cloak, never seen by the orbit, is purple, red and cyan specks
        ol = A['logit'][idx] > 0; _, nn = cKDTree(p[ol]).query(p, k=16); med = np.median(col[ol][nn], 1)
        sp = np.linalg.norm(col[:, :3] - med[:, :3], axis=1) > a.despeckle; col[sp, :3] = med[sp, :3]; reg['despeckled'] = int(sp.sum())
    write_vsplat(out, p, sc, qw[:, [3, 0, 1, 2]], A['logit'][idx], col, Bn, W, pal, B['joints'])
    man.setdefault('layers', {})[a.name] = {'modes': {'weights': out.name}, 'default': 'weights'}
    if under.any():
        inv = {int(j): k for k, j in enumerate(pal)}; stem = Path(whole).stem
        for part, m in ((f'{stem}-covered.vsplat', ~under), (f'{stem}-under.vsplat', under)):
            write_vsplat(a.bare / part, B['position'][m], B['log_scale'][m], B['quat_xyzw'][m][:, [3, 0, 1, 2]], B['logit'][m], B['color'][m] / 255,
                         np.vectorize(inv.get)(B['bones'][m]), B['weights'][m], pal, B['joints'])
        man['bodyWhole'] = whole; man['body'] = f'{stem}-covered.vsplat'; man['under'] = f'{stem}-under.vsplat'
        man['layers'][a.name]['covers'] = True; reg['under_gaussians'] = int(under.sum())
    man['defaultLayer'] = f'{a.name}:weights'
    (a.bare / 'character.json').write_text(json.dumps(man, indent=2) + '\n')
    rep = {'clothed': str(a.clothed), 'bare': str(a.bare / man['body']), 'layer': str(out), 'gaussians': int(len(idx)), 'of': int(A['n']),
           'hem_y': float(hem), 'garment_colour': (ref * 255).round().tolist(), 'registration': reg}
    (a.bare / f'{a.name}.json').write_text(json.dumps(rep, indent=1) + '\n'); print(json.dumps(rep))


def mesh_vertices(rig):
    doc, acc = read_glb(rig)
    return np.concatenate([acc(p['attributes']['POSITION']).astype(np.float64) for m in doc['meshes'] for p in m['primitives']])


def names_of(rig, skin_order=False):
    doc, _ = read_glb(rig); joints = doc['skins'][0]['joints']
    if skin_order:
        return [doc['nodes'][j]['name'] for j in joints]
    from bound_to_glb import depth_first
    return [doc['nodes'][joints[k]]['name'] for k in depth_first(doc, joints)]


def total_rotation(src_joints, pa, pos):
    """The rotation part of the whole registration, from the original positions to the registered ones (least squares)."""
    s, R, t = umeyama(pos, pa); return R


def total_scale(pos, pa):
    s, R, t = umeyama(pos, pa); return s


if __name__ == '__main__':
    main()
