#!/usr/bin/env python3
"""Rig a garment layer (a separate splat in the body's frame) onto a humanoid_splat_rig.py character.

  python3 layer_rig.py LAYER.npz BODY-rig.npz OUT_DIR NAME [--modes rigid,weights,chain,pelvis,thighs]
      [--top 0.24] [--hem auto] [--segments 4] [--chain-bones index_metacarpal_l,index_01_l,index_02_l,index_03_l]
      [--shoulder 0.06,0.14] [--centre 0.04] [--leg-max 0.8] [--leg-split 0.6] [--leg-ramp 0.05] [--leg-from 0.25]
      [--leg-sides 0.5] [--leg-start Y] [--body-top Y1,Y2] [--cover BODY.npz --cover-top Y] [--merge MODE] [--npz]

LAYER.npz must be in the same frame as the splat the body was rigged from
(authoring/tools/people/splat/mesh-splat/cloak_layer.py writes it so). It is moved into the body's
SDK frame with the body's own scale and offset; the body's trunk is not
re-posed by the rig, so the layer's bind pose is the body's rest there.

The page loads each layer file with Character.addVariantSplat, which links
it to the body's armature: the layer's own joints block is ignored, so it
uses the body's bones. One file per attachment mode:
  rigid    every Gaussian on spine_05 (upper spine).
  weights  the top on spine_05, its corners on the clavicles (shoulders),
           falling off down the length through spine_03 to the pelvis at the
           hem; the hem then turns with the hips, not the legs.
  chain    the top as in weights, the rest on a chain of --segments bones
           hanging from the upper back down the layer's centre line to its
           hem. The skeleton has no spare chain, so the body's own left
           index finger chain (unused by the body's weights) is placed down
           the back: the page drives those bones with a damped follow.
Skirt modes (a skirt hung from the waist; the layer's top is the waist):
  pelvis   every Gaussian on the pelvis: the bell turns and bobs with the hips only.
  thighs   the pelvis at the waist, blending down the bell toward the thighs (none above --leg-from of the way
           from waist to hem, --leg-max at the hem), each side leaning to its own leg across the line between the
           knees (--leg-split, 1 = fully, over a ramp of --leg-ramp either side); --leg-sides scales the leg share at
           the bell's sides (90 deg from the front) against the front and back, which the knees push. The knee in
           front then pushes the cloth forward and the hem swings with the stride.
--cover BODY.npz (the splat the body was rigged from, same order): also writes NAME-covered.vsplat, the body without
the trunk-region Gaussians the layer covers (static height under --cover-top, in a 4 cm x 15 deg cell about its axis
(its bounding-box centre) that the layer fills, and inside its outer surface there or less than --cover-margin outside it): an underskirt and
peplum under a dress, which show through a thin back. NAME-under.vsplat is that hidden part alone: the page shows it
(as a linked variant) whenever no covering layer is on, so the covered body plus it is the whole body. Arms and
legs are kept. --merge then joins the layer to this covered body.
--npz also writes NAME-MODE-rig.npz in humanoid_splat_rig.py's rig layout (rest_means, bones, weights, joints...),
so pose_splat_preview.py can pose the layer offline with the body.
--merge MODE also writes the body and that mode of the layer as one file
(NAME-merged-MODE.vsplat), for a character drawn as one splat instead of two.
For chain, OUT_DIR/NAME-body.vsplat is the body re-written with those joints
(same Gaussians and weights), and NAME-chain.json gives the chain's rest
points in the SDK frame for the page. Hat-shaped weights along the chain
blend each Gaussian between neighbouring segments by height. Writes
OUT_DIR/NAME-MODE.vsplat and NAME.json (counts, bytes, chain).
"""
import argparse
import json
import os
import sys

import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from humanoid_splat_rig import DEFAULT_SKELETON, load_skeleton, smoothstep, world_rest  # noqa: E402
from make_skinned_vsplat import write_vsplat  # noqa: E402
from pose_splat_preview import fk  # noqa: E402


def pack(bones, weights):
    """Global bone ids and weights (N x 4) -> palette-indexed arrays for write_vsplat."""
    palette = sorted(set(bones[weights > 0].tolist()))
    lookup = {b: k for k, b in enumerate(palette)}
    stored = np.vectorize(lambda b: lookup.get(b, 0))(bones)
    return stored, np.where(np.isin(bones, palette), weights, 0), palette


def top4(W):
    top = np.argsort(-W, 1)[:, :4]
    w = np.take_along_axis(W, top, 1); w /= np.maximum(w.sum(1, keepdims=True), 1e-9)
    return top, w


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('layer'); ap.add_argument('rig'); ap.add_argument('out'); ap.add_argument('name')
    ap.add_argument('--modes', default='rigid,weights,chain'); ap.add_argument('--skeleton', default=DEFAULT_SKELETON)
    ap.add_argument('--top', type=float, default=0.24, help='height (layer frame) of the chain root / end of the fixed top band')
    ap.add_argument('--segments', type=int, default=4)
    ap.add_argument('--chain-bones', default='index_metacarpal_l,index_01_l,index_02_l,index_03_l')
    ap.add_argument('--shoulder', default='0.06,0.14', help='|x| ramp over which the top band moves from spine_05 to the clavicles')
    ap.add_argument('--centre', type=float, default=0.04, help='half-width of the centre strip that finds the chain line')
    ap.add_argument('--leg-max', type=float, default=0.8, help='thighs: leg share at the hem')
    ap.add_argument('--leg-split', type=float, default=0.6, help='thighs: how far each side leans to its own leg (1 = fully)')
    ap.add_argument('--leg-ramp', type=float, default=0.05, help='thighs: half-width of the left/right ramp, layer units')
    ap.add_argument('--leg-from', type=float, default=0.25, help='thighs: no leg share above this fraction of waist to hem')
    ap.add_argument('--leg-sides', type=float, default=0.5, help="thighs: leg share at the bell's sides against front/back")
    ap.add_argument('--leg-start', type=float, help='thighs: height (layer frame) where the leg share starts, instead of --leg-from')
    ap.add_argument('--body-top', help="thighs: Y1,Y2: above Y1 the nearest body Gaussians' weights, blending to the thigh weights by Y2")
    ap.add_argument('--cover', help='the body splat (training frame) to write NAME-covered.vsplat from')
    ap.add_argument('--cover-top', type=float, default=0.115, help='--cover: only body Gaussians under this height')
    ap.add_argument('--cover-margin', type=float, default=0.02, help='--cover: also hide those this far outside the layer surface')
    ap.add_argument('--npz', action='store_true', help='also write NAME-MODE-rig.npz for pose_splat_preview.py')
    ap.add_argument('--merge', help='also write NAME-merged-MODE.vsplat: the body and this mode of the layer in one file')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    names, parents, can_t, can_r = load_skeleton(a.skeleton)
    idx = {n: i for i, n in enumerate(names)}
    Rw, _ = world_rest(parents, can_t, can_r)
    r = np.load(a.rig)
    scale, offset, to_sdk = float(r['scale']), r['offset'], r['to_sdk']
    T = r['joints'].copy()
    rest_world = fk(parents, can_r, T)[:, :3, 3]

    L = np.load(a.layer)
    m = L['means']; n = len(m)
    means = scale * (m @ to_sdk.T) + offset
    q = (Rot.from_matrix(np.broadcast_to(to_sdk, (n, 3, 3))) * Rot.from_quat(L['quats'][:, [1, 2, 3, 0]])).as_quat()[:, [3, 0, 1, 2]]
    scales = L['scales'] + np.log(scale)
    y, x = m[:, 1], m[:, 0]
    opac = 1 / (1 + np.exp(-L['opacities'].reshape(-1)))
    hem = float(np.percentile(y[opac > 0.3], 0.2))
    top = float(np.percentile(y[opac > 0.3], 99.8))
    s0, s1 = (float(v) for v in a.shoulder.split(','))
    side = smoothstep(s0, s1, np.abs(x))
    nb = len(names)
    report = {'layer': a.layer, 'rig': a.rig, 'gaussians': n, 'hem': hem, 'top': top, 'files': {}}

    def top_band(W, share):
        W[:, idx['spine_05']] += share * (1 - side)
        W[:, idx['clavicle_l']] += share * side * (x > 0)
        W[:, idx['clavicle_r']] += share * side * (x <= 0)

    layer_weights = {}

    def write(mode, W):
        bones, w = top4(W)
        layer_weights[mode] = (bones, w)
        stored, infl, palette = pack(bones, w)
        path = os.path.join(a.out, f'{a.name}-{mode}.vsplat')
        total = write_vsplat(path, means, scales, q, L['opacities'], L['rgb'], stored, infl, palette, T)
        report['files'][mode] = {'path': path, 'bytes': total, 'bones': [names[b] for b in palette]}
        if a.npz:
            np.savez_compressed(os.path.join(a.out, f'{a.name}-{mode}-rig.npz'), rest_means=means, scales=scales, quats=q,
                                opacities=L['opacities'], rgb=L['rgb'], bones=bones, weights=w, joints=T, scale=scale, offset=offset, to_sdk=to_sdk)
        print(mode, total, 'bytes,', len(palette), 'bones')

    modes = a.modes.split(',')
    if 'rigid' in modes:
        W = np.zeros((n, nb)); W[:, idx['spine_05']] = 1
        write('rigid', W)
    if 'pelvis' in modes:
        W = np.zeros((n, nb)); W[:, idx['pelvis']] = 1
        write('pelvis', W)
    if 'thighs' in modes:
        # The line between the knees in the layer's frame (the body's rest knees, back through scale/offset/to_sdk).
        to_layer = lambda p: ((np.asarray(p) - offset) / scale) @ to_sdk  # noqa: E731
        knees = to_layer(rest_world[[idx['calf_l'], idx['calf_r']]])
        cx, cz = knees[:, 0].mean(), knees[:, 2].mean()
        u = np.clip((top - y) / (top - hem), 0, 1)
        theta = np.arctan2(x - cx, m[:, 2] - cz)                      # 0 = front (+Z), 90 deg = her left (+X)
        sector = a.leg_sides + (1 - a.leg_sides) * np.abs(np.cos(theta))
        leg_from = (top - a.leg_start) / (top - hem) if a.leg_start is not None else a.leg_from
        leg = a.leg_max * smoothstep(leg_from, 1.0, u) * sector
        left = 0.5 + a.leg_split * (smoothstep(-a.leg_ramp, a.leg_ramp, x - cx) - 0.5)
        W = np.zeros((n, nb))
        W[:, idx['pelvis']] = 1 - leg
        W[:, idx['thigh_l']] = leg * left
        W[:, idx['thigh_r']] = leg * (1 - left)
        if a.body_top:
            y1, y2 = (float(v) for v in a.body_top.split(','))
            from scipy.spatial import cKDTree
            trunk = np.where(r['region'] == 0)[0]
            _, nb8 = cKDTree(r['rest_means'][trunk]).query(means, k=8)
            Wb = np.zeros((n, nb))
            for j in range(8):
                rows = trunk[nb8[:, j]]
                np.add.at(Wb, (np.repeat(np.arange(n), 4), r['bones'][rows].reshape(-1)), r['weights'][rows].reshape(-1) / 8)
            t = smoothstep(y2, y1, y)[:, None]          # 1 above Y1, 0 below Y2
            W = t * Wb + (1 - t) * W
            report['body_top'] = [y1, y2]
        report['thighs'] = {'knee_line': [float(cx), float(cz)], 'leg_max': a.leg_max, 'leg_split': a.leg_split, 'leg_ramp': a.leg_ramp,
                            'leg_from': float(leg_from), 'leg_sides': a.leg_sides}
        write('thighs', W)
    if 'weights' in modes:
        u = np.clip((top - y) / (top - hem), 0, 1)
        W = np.zeros((n, nb))
        w_top = 1 - smoothstep(0.1, 0.5, u)
        w_mid = np.clip(1 - np.abs(u - 0.5) / 0.4, 0, 1)
        w_low = smoothstep(0.5, 0.95, u)
        tot = w_top + w_mid + w_low
        top_band(W, w_top / tot)
        W[:, idx['spine_03']] += w_mid / tot
        W[:, idx['pelvis']] += w_low / tot
        write('weights', W)
    if 'chain' in modes:
        chain = a.chain_bones.split(',')[:a.segments]
        heights = np.linspace(a.top, hem, a.segments + 1)
        centre = (np.abs(x) < a.centre) & (opac > 0.3) & (m[:, 2] < 0)
        pts = []
        for h in heights:
            band = centre & (np.abs(y - h) < 0.01)
            z = float(np.median(m[band, 2])) if band.sum() > 5 else float(np.median(m[centre & (np.abs(y - h) < 0.03), 2]))
            pts.append([0.0, h, z])
        P = scale * (np.array(pts) @ to_sdk.T) + offset
        for i, b in enumerate(chain):
            j = idx[b]; p = parents[j]
            T[j] = Rw[p].T @ (P[i] - (rest_world[p] if i == 0 else P[i - 1]))
        sgm = (a.top - y) / ((a.top - hem) / a.segments)   # segment coordinate: 0 at the root, SEGMENTS at the hem
        W = np.zeros((n, nb))
        w_top = 1 - smoothstep(-0.6, 0.4, sgm)
        top_band(W, w_top)
        hats = np.stack([np.clip(1 - np.abs(np.clip(sgm, 0.5, a.segments - 0.5) - (i + 0.5)), 0, 1) for i in range(a.segments)], 1)
        hats /= hats.sum(1, keepdims=True)
        for i, b in enumerate(chain):
            W[:, idx[b]] += (1 - w_top) * hats[:, i]
        write('chain', W)
        # The body with the chain joints placed (Gaussians and weights unchanged).
        stored, infl, palette = pack(r['bones'], r['weights'])
        body = os.path.join(a.out, f'{a.name}-body.vsplat')
        total = write_vsplat(body, r['rest_means'], r['scales'], r['quats'], r['opacities'], r['rgb'], stored, infl, palette, T)
        sp = Rw[idx['spine_05']]
        chain_info = {'bones': chain, 'points': P.tolist(), 'lengths': np.linalg.norm(np.diff(P, axis=0), axis=1).tolist(),
                      'spine_05_rest': rest_world[idx['spine_05']].tolist(), 'spine_05_rest_rot': Rot.from_matrix(sp).as_quat().tolist(), 'pelvis_rest_rot': Rot.from_matrix(Rw[idx['pelvis']]).as_quat().tolist(),
                      'bone_rest_rots': [Rot.from_matrix(Rw[idx[b]]).as_quat().tolist() for b in chain], 'body': body}
        json.dump(chain_info, open(os.path.join(a.out, f'{a.name}-chain.json'), 'w'), indent=1)
        report['chain'] = chain_info; report['files']['body'] = {'path': body, 'bytes': total}
        print('body', total, 'bytes; chain lengths', [round(v, 3) for v in chain_info['lengths']])
    body_keep = np.ones(len(r['rest_means']), bool)
    if a.cover:
        bm = np.load(a.cover)['means']
        lop = opac > 0.3
        c = (m[lop][:, [0, 2]].min(0) + m[lop][:, [0, 2]].max(0)) / 2         # the bell's own axis (its box centre)
        def cells(p):
            return (np.floor(p[:, 1] / 0.04).astype(int), (np.degrees(np.arctan2(p[:, 0] - c[0], p[:, 2] - c[1])) % 360 // 15).astype(int),
                    np.hypot(p[:, 0] - c[0], p[:, 2] - c[1]))
        lb, la, lr = cells(m[lop]); outer = {}
        for key in set(zip(lb.tolist(), la.tolist())):
            sel = (lb == key[0]) & (la == key[1])
            if sel.sum() >= 3: outer[key] = np.percentile(lr[sel], 90)
        bb, ba, br = cells(bm)
        cand = np.where((r['region'] == 0) & (bm[:, 1] < a.cover_top))[0]
        hide = [i for i in cand if (int(bb[i]), int(ba[i])) in outer and br[i] < outer[(int(bb[i]), int(ba[i]))] + a.cover_margin]
        body_keep[hide] = False
        stored, infl, palette = pack(r['bones'][body_keep], r['weights'][body_keep])
        path = os.path.join(a.out, f'{a.name}-covered.vsplat')
        total = write_vsplat(path, r['rest_means'][body_keep], r['scales'][body_keep], r['quats'][body_keep], r['opacities'].reshape(-1)[body_keep],
                             r['rgb'][body_keep], stored, infl, palette, T)
        report['files']['covered'] = {'path': path, 'bytes': total, 'gaussians': int(body_keep.sum()), 'hidden': int((~body_keep).sum()), 'cover_top': a.cover_top}
        np.save(os.path.join(a.out, f'{a.name}-covered-keep.npy'), body_keep)
        u = ~body_keep                                  # the covered part alone, for the page to add when no layer covers it
        stored, infl, palette = pack(r['bones'][u], r['weights'][u])
        upath = os.path.join(a.out, f'{a.name}-under.vsplat')
        utotal = write_vsplat(upath, r['rest_means'][u], r['scales'][u], r['quats'][u], r['opacities'].reshape(-1)[u], r['rgb'][u], stored, infl, palette, T)
        report['files']['under'] = {'path': upath, 'bytes': utotal, 'gaussians': int(u.sum())}
        print('covered body', total, 'bytes,', int(body_keep.sum()), 'Gaussians,', int((~body_keep).sum()), 'hidden')
    if a.merge:
        # One splat per character instead of two: body Gaussians then the layer's, one palette, the body's joints
        # (with the chain joints when chain mode was built).
        lb, lw = layer_weights[a.merge]
        k = body_keep
        bones = np.concatenate([r['bones'][k], lb]); w = np.concatenate([r['weights'][k], lw])
        stored, infl, palette = pack(bones, w)
        cat = lambda key, v: np.concatenate([r[key][k], v.astype(r[key].dtype)])  # noqa: E731
        path = os.path.join(a.out, f'{a.name}-merged-{a.merge}.vsplat')
        total = write_vsplat(path, cat('rest_means', means), cat('scales', scales), cat('quats', q),
                             np.concatenate([r['opacities'].reshape(-1)[k], L['opacities'].reshape(-1)]), cat('rgb', L['rgb']), stored, infl, palette, T)
        report['files']['merged-' + a.merge] = {'path': path, 'bytes': total, 'gaussians': int(len(bones))}
        print('merged', a.merge, total, 'bytes,', len(bones), 'Gaussians')
    json.dump(report, open(os.path.join(a.out, f'{a.name}.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
