#!/usr/bin/env python3
"""Mark the Gaussians that paint a splat character's eyes, for the Engine's group tint (blinks), in its skinned-splat GLB.

    eye_mask.py CHARACTER.glb OUT.glb REPORT.png [--eyes X1,Y1,X2,Y2] [--write-anyway]

Writes OUT.glb: CHARACTER.glb with `_GROUP` (one unsigned byte per Gaussian, SCALAR, 0 none, 1 the eyes; the Engine's
splat Gaussian groups, rusty-engine #10976) and asset.extras.eyes {"group": 1, "lid": "#rrggbb", ...}, and
REPORT.png (the face open, the mask outlined, and the eyes shut at the lid colour) with REPORT.json beside it.

The rule is #10974's (rusty-dagger local/evidence/10974-splat-blinks/tools/eyes.py), on its renderer (the Engine layer's
falloff, display-space blending): a 512 px face render from 0.6 m at 30 degrees, centred between the Head joint and the top of
the head; an ellipse 2.8 x 1.4 cm round each eye; the mask is every Gaussian that puts at least 15 % of its drawn weight
inside an ellipse (here 1.25 times that size by default, --scale; see below). Everything is drawn from _ALBEDO where the
GLB has it (the game draws the townspeople lit, and a lit figure's group tint replaces _ALBEDO; rusty-engine #10976). Then, since a few missed Gaussians leave a sliver of iris when the eye shuts (the Engine's own test): with
the mask at the lid colour, any pixel in an ellipse that still differs from the lid by more than REMNANT and was eye-coloured
when open pulls in the Gaussians drawing it (up to three rounds). Lid: the cheek just below each eye (the median of the
drawn albedo there) times 0.9, averaged over both eyes. No lash line: the Engine tints a whole group toward one colour, and #10974's lash line didn't show on the
3 px grid (rusty-dagger, 2026-11-22).

Eye centres: found in the face render as the symmetric pair of dark spots (each eye-sized patch darker than its surround,
mirrored about the face's centre column, 45-72 px apart from the centre) unless --eyes gives them in render pixels.
Exit 3 when a check fails (not written unless --write-anyway): both eyes found, 12-100 Gaussians, eye remnant (iris or eye-white
contrast in the inner three quarters of an ellipse that the shut eye moved less than half way to the lid) under 5 %
(what is left is drawn by large skin and brow Gaussians centred outside the eye, which a blink must not recolour), no mask Gaussian centred more than 1.1 cm
above an eye centre (brow) - those are left out of the mask from the start, as are Gaussians centred well below or beside an eye.

Written 2026-11-22 for rusty-dagger #10977 (blinks on the 24 townspeople; Patch's go relayed by rusty-dagger.lead).
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

W = H = 512
FOVY = 30.0
DIST = 0.6
HW, HH = 22, 11           # ellipse half axes in px: 1.4 x 0.7 cm at 0.6 m (f = 955 px)
REMNANT = 0.12            # luminance difference from the lid that counts as eye still showing (iris dark, or the eye's white)
BROW = 18                 # px (1.1 cm) above an eye centre: Gaussians centred higher are brow, never in the mask
BG = np.array([0.48, 0.48, 0.48])


def read_glb(path):
    d = Path(path).read_bytes(); n = struct.unpack_from('<I', d, 12)[0]; g = json.loads(d[20:20 + n]); at = 20 + n
    return g, bytearray(d[at + 8:at + 8 + struct.unpack_from('<I', d, at)[0]])


def write_glb(path, g, b):
    js = json.dumps(g, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); b = bytes(b) + b'\0' * (-len(b) % 4)
    Path(path).write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(b)) + struct.pack('<II', len(js), 0x4E4F534A) + js
                           + struct.pack('<II', len(b), 0x004E4942) + b)


def acc(g, b, i):
    a = g['accessors'][i]; v = g['bufferViews'][a['bufferView']]
    w = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
    dt = {5126: np.float32, 5121: np.uint8, 5123: np.uint16}[a['componentType']]
    st = v.get('byteStride'); off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    if st and st != w * np.dtype(dt).itemsize:
        raw = np.frombuffer(bytes(b[off:off + st * a['count']]), np.uint8).reshape(a['count'], st)[:, :w * np.dtype(dt).itemsize]
        return np.ascontiguousarray(raw).view(dt).reshape(a['count'], w)
    return np.frombuffer(bytes(b), dt, a['count'] * w, off).reshape(a['count'], w)


def quat_mat(q):   # xyzw
    x, y, z, w = q.T
    return np.stack([1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 2 * (x * y + z * w), 1 - 2 * (x * x + z * z),
                     2 * (y * z - x * w), 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)], -1).reshape(-1, 3, 3)


def look_at(eye, target, up=(0, 1, 0)):
    eye = np.asarray(eye, float); f = np.asarray(target, float) - eye; f /= np.linalg.norm(f)
    r = np.cross(f, up); r /= np.linalg.norm(r); u = np.cross(r, f)
    V = np.eye(4); V[0, :3] = r; V[1, :3] = u; V[2, :3] = -f; V[:3, 3] = -V[:3, :3] @ eye
    return V


def render(P, R, S, rgb, alpha, V, masks=()):
    """#10974's render.py: front-to-back, the Engine layer's falloff. Returns premultiplied colour, coverage, and per Gaussian
    u, v, its total drawn weight and its drawn weight inside each of `masks`."""
    f = H / 2 / np.tan(np.radians(FOVY) / 2)
    pc = (V[:3, :3] @ P.T).T + V[:3, 3]; z = -pc[:, 2]
    Rc = V[:3, :3][None] @ R; cov = Rc @ (S[:, :, None] ** 2 * np.transpose(Rc, (0, 2, 1)))
    x, y = pc[:, 0], pc[:, 1]; zz = np.maximum(z, 1e-6)
    J = np.zeros((len(P), 2, 3)); J[:, 0, 0] = f / zz; J[:, 0, 2] = f * x / zz ** 2; J[:, 1, 1] = -f / zz; J[:, 1, 2] = -f * y / zz ** 2
    c2 = J @ cov @ np.transpose(J, (0, 2, 1)); c2[:, 0, 0] += 0.3; c2[:, 1, 1] += 0.3
    u = W / 2 + f * x / zz; v = H / 2 - f * y / zz
    col = np.zeros((H, W, 3)); T = np.ones((H, W)); e4 = np.exp(-4)
    total = np.zeros(len(P)); inm = np.zeros((len(masks), len(P)))
    for i in np.argsort(z):
        if z[i] < 0.05 or alpha[i] < 1 / 255:
            continue
        C = c2[i]; det = C[0, 0] * C[1, 1] - C[0, 1] ** 2
        if det <= 0:
            continue
        lam = 0.5 * (C[0, 0] + C[1, 1]) + np.sqrt(max(0.25 * (C[0, 0] - C[1, 1]) ** 2 + C[0, 1] ** 2, 0))
        rad = int(np.ceil(2 * np.sqrt(2 * lam))) + 1
        x0, x1 = max(int(u[i]) - rad, 0), min(int(u[i]) + rad + 1, W); y0, y1 = max(int(v[i]) - rad, 0), min(int(v[i]) + rad + 1, H)
        if x0 >= x1 or y0 >= y1:
            continue
        Ci = np.array([[C[1, 1], -C[0, 1]], [-C[0, 1], C[0, 0]]]) / det
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5 - u[i], np.arange(y0, y1) + 0.5 - v[i])
        A = (Ci[0, 0] * gx * gx + 2 * Ci[0, 1] * gx * gy + Ci[1, 1] * gy * gy) / 8
        a = alpha[i] * np.where(A < 1, (np.exp(-4 * A) - e4) / (1 - e4), 0); a[a < 1 / 255] = 0
        t = T[y0:y1, x0:x1]; ta = t * a; total[i] = ta.sum()
        for k, m in enumerate(masks):
            inm[k, i] = (ta * m[y0:y1, x0:x1]).sum()
        col[y0:y1, x0:x1] += ta[..., None] * rgb[i]; T[y0:y1, x0:x1] = t * (1 - a)
    return col, 1 - T, u, v, total, inm


def ellipse(cx, cy, s=1.0):
    gx, gy = np.meshgrid(np.arange(W) + 0.5, np.arange(H) + 0.5)
    return (((gx - cx) / (HW * s)) ** 2 + ((gy - cy) / (HH * s)) ** 2 < 1).astype(float)


def find_eyes(img, cover):
    """The symmetric pair of eye-sized patches darker than their surround."""
    L = img @ np.array([0.3, 0.59, 0.11]); L = np.where(cover > 0.9, L, np.nan)
    I = np.nan_to_num(L).cumsum(0).cumsum(1); N = (~np.isnan(L)).astype(float).cumsum(0).cumsum(1)
    I = np.pad(I, ((1, 0), (1, 0))); N = np.pad(N, ((1, 0), (1, 0)))

    def box(arr, x0, y0, x1, y1):
        return arr[y1, x1] - arr[y0, x1] - arr[y1, x0] + arr[y0, x0]

    def contrast(x, y):
        ix, iy, ox, oy = int(HW * 0.55), int(HH * 0.6), int(HW * 1.5), int(HH * 2.4)
        si, ni = box(I, x - ix, y - iy, x + ix, y + iy), box(N, x - ix, y - iy, x + ix, y + iy)
        so, no = box(I, x - ox, y - oy, x + ox, y + oy), box(N, x - ox, y - oy, x + ox, y + oy)
        if ni < 0.9 * (2 * ix) * (2 * iy) or no - ni < 0.8 * ((2 * ox) * (2 * oy) - (2 * ix) * (2 * iy)):
            return -1.0
        return (so - si) / (no - ni) - si / ni
    best = (-9, None)
    for y in range(190, 345, 2):
        for xm in range(236, 277, 2):
            for s in range(45, 73, 2):
                cl, cr = contrast(xm - s, y), contrast(xm + s, y)
                if cl <= 0 or cr <= 0:
                    continue
                sc = cl + cr - 0.7 * abs(cl - cr)
                if sc > best[0]:
                    best = (sc, ((xm - s, y), (xm + s, y)))
    # refine each eye on its own (faces are not quite symmetric)
    if best[1] is None:
        return None, 0.0
    eyes = []
    for ex, ey in best[1]:
        cand = [(contrast(x, y), x, y) for x in range(ex - 6, ex + 7) for y in range(ey - 4, ey + 5)]
        c, x, y = max(cand); eyes.append((x, y))
    return eyes, float(best[0])


def hexc(c):
    return '#' + ''.join(f'{int(round(min(max(v, 0), 1) * 255)):02x}' for v in c)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('glb'); ap.add_argument('out'); ap.add_argument('report')
    ap.add_argument('--eyes', help='X1,Y1,X2,Y2 eye centres in the 512 px face render, in place of the search')
    ap.add_argument('--scale', type=float, default=1.25, help='ellipse size against 2.8 x 1.4 cm (default 1.25: at 1.0 the larger painted eyes kept their rim or white)')
    ap.add_argument('--write-anyway', action='store_true')
    a = ap.parse_args()
    global HW, HH, BROW
    HW, HH, BROW = HW * a.scale, HH * a.scale, BROW * a.scale
    g, b = read_glb(a.glb); prim = g['meshes'][0]['primitives'][0]; att = prim['attributes']
    P = acc(g, b, att['POSITION']).astype(float); R = quat_mat(acc(g, b, att['_ROTATION']).astype(float))
    S = acc(g, b, att['_SCALE']).astype(float); C = acc(g, b, att['_COLOR']) / 255.0
    if '_ALBEDO' in att:   # the game draws the townspeople lit, and a lit figure's group tint replaces _ALBEDO (sRGB; its alpha unread)
        C[:, :3] = acc(g, b, att['_ALBEDO'])[:, :3] / 255.0
    if '_GROUP' in att:
        raise SystemExit(f'{a.glb} already has _GROUP: start from the GLB without it (bound.py keeps ID.pre-eyes.glb)')
    skin = g['skins'][0]; names = [g['nodes'][j]['name'] for j in skin['joints']]
    ibm = acc(g, b, skin['inverseBindMatrices']).reshape(-1, 4, 4)
    h = names.index('mixamorig:Head'); hj = np.linalg.inv(ibm[h].T)[:3, 3]
    J0 = acc(g, b, att['JOINTS_0']); W0 = acc(g, b, att['WEIGHTS_0'])
    dom = J0[np.arange(len(P)), W0.argmax(1)]
    top = P[dom == h][:, 1].max(); c = np.array([hj[0], (hj[1] + top) / 2, hj[2]])
    near = np.linalg.norm(P - c, axis=1) < 0.25   # only the head and shoulders are drawn (speed)
    idx_near = np.nonzero(near)[0]
    Pn, Rn, Sn, Cn = P[near], R[near], S[near], C[near]
    V = look_at(c + [0, 0, DIST], c)
    col, cov, u, v, total, _ = render(Pn, Rn, Sn, Cn[:, :3], Cn[:, 3], V)
    img = col + (1 - cov)[..., None] * BG
    if a.eyes:
        q = [float(t) for t in a.eyes.split(',')]; eyes = [(q[0], q[1]), (q[2], q[3])]; found = 'given'
    else:
        eyes, score = find_eyes(img, cov); found = f'search (score {score:.3f})'
    checks = {}
    if not eyes:
        checks['both_eyes'] = False; rep = {'glb': a.glb, 'checks': checks}
        Path(a.report).with_suffix('.json').write_text(json.dumps(rep, indent=1)); print(json.dumps(rep)); sys.exit(3)
    ell = [ellipse(x, y) for x, y in eyes]; both = np.maximum(*ell)
    _, _, _, _, total, inm = render(Pn, Rn, Sn, Cn[:, :3], Cn[:, 3], V, masks=[both])
    share = inm[0] / np.maximum(total, 1e-9)
    # a Gaussian centred above the eye (brow) or far below it (cheek) stays out, so a blink doesn't recolour the brow
    ok = np.zeros(len(Pn), bool)
    for ex, ey in eyes:
        ok |= (v >= ey - BROW) & (v <= ey + 2.2 * HH) & (np.abs(u - ex) <= 2.0 * HW)
    sel = (share > 0.15) & (inm[0] > 0.3) & ok
    lids = []
    for ex, ey in eyes:
        cb = img[int(ey + 1.3 * HH):int(ey + 2.8 * HH), int(ex - 0.7 * HW):int(ex + 0.7 * HW)]
        cv = cov[int(ey + 1.3 * HH):int(ey + 2.8 * HH), int(ex - 0.7 * HW):int(ex + 0.7 * HW)]
        lids.append(np.median(cb[cv > 0.9], 0))   # the front-facing cheek as drawn: opacity-weighted by the render itself
    lid = np.mean(lids, 0) * 0.9
    lum = lambda x: x @ np.array([0.3, 0.59, 0.11])   # noqa: E731
    inner = np.maximum(*[ellipse(x, y, 0.75) for x, y in eyes])
    eye_open = (np.abs(lum(img) - lum(lid)) > REMNANT) & (inner > 0) & (cov > 0.5)
    rounds = []
    for r in range(4):
        C2 = Cn[:, :3].copy(); C2[sel] = lid
        col2, cov2, _, _, _, _ = render(Pn, Rn, Sn, C2, Cn[:, 3], V)
        shut = col2 + (1 - cov2)[..., None] * BG
        # still showing: the blink moved the pixel less than half way from its open colour to the lid (an untouched iris or
        # eye-white); lash lines and creases that darken a shut lid are moved further and do not count
        rem = eye_open & (np.abs(lum(shut) - lum(img)) < 0.5 * np.abs(lum(img) - lum(lid)))
        rounds.append({'gaussians': int(sel.sum()), 'remnant_px': int(rem.sum()), 'remnant_share': round(float(rem.sum() / max(inner.sum(), 1)), 4),
                       'cover_at_remnant': round(float(np.median(cov2[rem])), 3) if rem.any() else None})
        if rem.sum() == 0 or r == 3:
            break
        _, _, _, _, tot_r, in_r = render(Pn, Rn, Sn, Cn[:, :3], Cn[:, 3], V, masks=[rem.astype(float)])
        add = (in_r[0] > 0.05) & (in_r[0] / np.maximum(tot_r, 1e-9) > 0.05) & ok & ~sel
        cand = (in_r[0] > 0.05) & ~sel
        rounds[-1]['why_not'] = {'drawing_remnant': int(cand.sum()), 'share_under_5pct': int((cand & ~(in_r[0] / np.maximum(tot_r, 1e-9) > 0.05)).sum()),
                                 'outside_eye_region': int((cand & ~ok).sum()), 'added': int(add.sum())}
        if not add.any():
            break
        sel |= add
    # above the brow: mask centres more than 2.2 cm above an eye centre (f = 955 px; 2.2 cm at 0.6 m = 35 px)
    ey_min = min(y for _, y in eyes)
    high = int(((v[sel] < ey_min - BROW - 1)).sum())
    n = int(sel.sum())
    checks = {'both_eyes': True, 'count_12_100': 12 <= n <= 100, 'remnant_under_5pct': rounds[-1]['remnant_share'] < 0.05,
              'no_brow': high == 0}
    group = np.zeros(len(P), np.uint8); group[idx_near[sel]] = 1
    extras = {'group': 1, 'lid': hexc(lid), 'gaussians': n, 'ellipse_scale': a.scale,
              'rule': f'rusty-dagger #10974/#10977: >= 15 % of drawn weight inside a {2.8 * a.scale:.1f} x {1.4 * a.scale:.2f} cm ellipse per '
                      'eye (no brow), then the shut eye checked for remnants; lid = cheek below the eye x0.9 '
                      '(asset-pipeline eye_mask.py)'}
    rep = {'glb': a.glb, 'scale': a.scale, 'eyes_px': [list(map(float, e)) for e in eyes], 'found': found, 'face_centre': c.round(4).tolist(),
           'gaussians': n, 'lid': extras['lid'], 'rounds': rounds, 'checks': checks,
           'mask_above_brow': high}
    # report image: open with outlines | mask | shut
    def pil(x):
        return Image.fromarray((np.clip(x, 0, 1) * 255).astype(np.uint8))
    ey_mid = int(np.mean([y for _, y in eyes])); crop = (128, ey_mid - 112, 384, ey_mid + 112)
    o = pil(img); d = ImageDraw.Draw(o)
    for ex, ey in eyes:
        d.ellipse([ex - HW, ey - HH, ex + HW, ey + HH], outline=(0, 255, 0))
    Cm = Cn[:, :3].copy(); Cm[sel] = [1, 0, 1]
    cm, cvm, _, _, _, _ = render(Pn, Rn, Sn, Cm, Cn[:, 3], V); mk = pil(cm + (1 - cvm)[..., None] * BG)
    rimg = shut.copy(); rimg[rem] = [1, 0, 0]   # what the remnant check still counts, in red
    tiles = [t.crop(crop).resize((512, 448), Image.LANCZOS) for t in (o, mk, pil(shut), pil(rimg))]
    sheet = Image.new('RGB', (2048, 448)); [sheet.paste(t, (512 * k, 0)) for k, t in enumerate(tiles)]
    ImageDraw.Draw(sheet).text((6, 6), f"{Path(a.glb).stem}  n={n}  remnant {rounds[-1]['remnant_share']:.3f}  lid {extras['lid']}  "
                               + ('OK' if all(checks.values()) else 'CHECK ' + ','.join(k for k, ok in checks.items() if not ok)), fill=(255, 255, 0))
    sheet.save(a.report)
    Path(a.report).with_suffix('.json').write_text(json.dumps(rep, indent=1) + '\n')
    print(json.dumps({k: rep[k] for k in ('gaussians', 'lid', 'found', 'checks')}))
    if not all(checks.values()) and not a.write_anyway:
        sys.exit(3)
    # write _GROUP (stride 4, so the glTF validator's 4-byte alignment holds) and the extras
    gb = bytearray(b); gb.extend(b'\0' * (-len(gb) % 4)); off = len(gb)
    padded = np.zeros((len(P), 4), np.uint8); padded[:, 0] = group; gb.extend(padded.tobytes())
    g['bufferViews'].append({'buffer': 0, 'byteOffset': off, 'byteLength': padded.nbytes, 'byteStride': 4, 'target': 34962})
    att.pop('_GROUP', None)
    g['accessors'].append({'bufferView': len(g['bufferViews']) - 1, 'componentType': 5121, 'count': len(P), 'type': 'SCALAR'})
    att['_GROUP'] = len(g['accessors']) - 1
    g['buffers'][0]['byteLength'] = len(gb)
    g.setdefault('asset', {}).setdefault('extras', {})['eyes'] = extras
    write_glb(a.out, g, gb)


if __name__ == '__main__':
    main()
