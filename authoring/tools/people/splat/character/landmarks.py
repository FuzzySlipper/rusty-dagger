"""landmarks.py SPLAT OUT.json OUT.png [--waist Y --hem Y] [--hip-frac 0.51]: joint landmarks for humanoid_splat_rig.py, measured
on the levelled 400k final (Y up, front +Z, the figure's left +X) the way townsman 385's were (live-evidence/town385-hires-20261112-r01/
runtime/landmarks.json "about"), as fractions of the height H (sole = 0.1% lowest opaque centre, crown = 99.9%):
  legs    per 2 cm band below the hem, the centroid of each side's opaque Gaussians; the shin is the straight line through the
          centroids from 0.08 H to 0.20 H above the sole (or to the hem if lower); knees on each leg's centroid at 0.26 H; ankles on
          the shin line at 0.045 H; hips (under the tunic) ESTIMATED at --hip-frac H (0.51 as 385) and +-0.054 H from the trunk centre
  feet    toe tip = the shoe Gaussians (under 0.03 H) farthest from the ankle (99th percentile), ball 65% of the way at 0.02 H
  head    neck at 0.82 H, head at 0.87 H (centroids of the band within the head's width), head top at the crown
  arms    per band, the Gaussians beyond the widest gap (> 1 cm) between trunk and arm, tracked down from the top band by band
          (each within 3 cm of the one above; below 0.6 H only bands that are not mostly cloth - teal or blue, or a yellow hem band; dark points, a shadowed hand or a black glove, count as hand); shoulder at the armpit band (0.77 H), hand
          tip at the arm's lowest, wrist 0.11 H above it, elbow 38% of the way from wrist to shoulder (385's proportions)
  skirt   waist (belt) and hem from --waist / --hem when given; otherwise the hem is the lowest band whose centre column (|x| < 1.5 cm)
          is filled (cloth between the legs), the waist the narrowest trunk band between the hem and the armpits
Every landmark is drawn over front and side projections (OUT.png) to be checked by eye; the numbers it used go in OUT.json "about"."""
import argparse, json, numpy as np
ap = argparse.ArgumentParser(); ap.add_argument('splat'); ap.add_argument('out'); ap.add_argument('png')
ap.add_argument('--waist', type=float); ap.add_argument('--hem', type=float); ap.add_argument('--hip-frac', type=float, default=0.51)
a = ap.parse_args()
s = np.load(a.splat); m = s['means'].astype(float); op = 1 / (1 + np.exp(-s['opacities'].reshape(-1))); k = op > 0.5; P = m[k]; RGB = np.clip(s['rgb'][k], 0, 1)
sole, crown = np.percentile(P[:, 1], [0.1, 99.9]); H = crown - sole; yv = lambda f: sole + f * H
def band(y0, y1, sel=None):
    q = (P[:, 1] >= y0) & (P[:, 1] < y1); return q if sel is None else q & sel
# hem: scanning up from the knees, the first band whose centre column is filled
hem = a.hem
if hem is None:
    for y in np.arange(yv(0.15), yv(0.6), 0.01):
        if band(y, y + 0.01, np.abs(P[:, 0]) < 0.015).sum() > 40:
            hem = round(float(y), 3); break
# legs below the hem
legs = {}
for side, sgn in (('l', 1), ('r', -1)):
    sel = (np.sign(P[:, 0]) == sgn); ys, cs = [], []
    for y in np.arange(yv(0.06), min(hem, yv(0.30)), 0.02):
        q = band(y, y + 0.02, sel)
        if q.sum() > 30:
            c = P[q][:, [0, 2]]; med = np.median(c, 0); c = c[np.linalg.norm(c - med, axis=1) < 0.07]; ys.append(y + 0.01); cs.append(c.mean(0))
    ys, cs = np.array(ys), np.array(cs); legs[side] = (ys, cs)
J = {}; about = []
for side in 'lr':
    ys, cs = legs[side]; sh = (ys >= yv(0.08)) & (ys <= min(yv(0.20), hem))
    A = np.c_[ys[sh], np.ones(sh.sum())]; fx = np.linalg.lstsq(A, cs[sh, 0], rcond=None)[0]; fz = np.linalg.lstsq(A, cs[sh, 1], rcond=None)[0]
    res = np.sqrt(np.mean((A @ fx - cs[sh, 0]) ** 2 + (A @ fz - cs[sh, 1]) ** 2))
    ya = yv(0.045); J[f'foot_{side}'] = [fx[0] * ya + fx[1], ya, fz[0] * ya + fz[1]]
    kn = np.argmin(np.abs(ys - yv(0.26))); J[f'calf_{side}'] = [cs[kn, 0], yv(0.26), cs[kn, 1]] if abs(ys[kn] - yv(0.26)) < 0.03 else \
        [fx[0] * yv(0.26) + fx[1], yv(0.26), fz[0] * yv(0.26) + fz[1]]
    about.append(f"{side} shin line dx/dy {fx[0]:.3f} dz/dy {fz[0]:.3f}, residual {res * 100:.1f} cm over {sh.sum()} bands")
    # toe: shoe Gaussians under 0.03 H nearest this ankle, the 99th percentile farthest
    an = np.array(J[f'foot_{side}']); q = band(sole - 0.01, yv(0.03)) & (np.sign(P[:, 0]) == (1 if side == 'l' else -1))
    d = np.linalg.norm(P[q][:, [0, 2]] - an[[0, 2]], axis=1); far = P[q][d >= np.percentile(d, 99)]
    toe = far.mean(0); toe[1] = min(toe[1], yv(0.01)); J[f'toe_tip_{side}'] = toe.tolist()
    J[f'ball_{side}'] = [an[0] + 0.65 * (toe[0] - an[0]), yv(0.02), an[2] + 0.65 * (toe[2] - an[2])]
    turn = np.degrees(np.arctan2(toe[0] - an[0], toe[2] - an[2])); about.append(f"{side} foot turned {turn:+.0f} deg from +Z")
# trunk centre at hip height (Gaussians within 0.12 of the axis), hips
q = band(yv(args_h := a.hip_frac) - 0.02, yv(a.hip_frac) + 0.02, np.abs(P[:, 0]) < 0.12); tc = P[q][:, [0, 2]].mean(0)
for side, sgn in (('l', 1), ('r', -1)):
    J[f'thigh_{side}'] = [tc[0] + sgn * 0.054 * H, yv(a.hip_frac), tc[1]]
J['pelvis'] = [tc[0], yv(a.hip_frac) + 0.025 * H, tc[1]]
# head and neck
for name, f, w in (('neck_01', 0.82, 0.06), ('head', 0.87, 0.08)):
    q = band(yv(f) - 0.01, yv(f) + 0.01, np.abs(P[:, 0] - tc[0]) < w); c = P[q].mean(0); J[name] = [c[0], yv(f), c[2]]
q = P[:, 1] > np.percentile(P[:, 1], 99.5); J['head_top'] = [P[q][:, 0].mean(), crown, P[q][:, 2].mean()]
# arms: per band, beyond the widest gap between trunk and arm
arm = {'l': [], 'r': []}
for y in np.arange(yv(0.35), yv(0.80), 0.01):
    for side, sgn in (('l', 1), ('r', -1)):
        q = band(y, y + 0.01, np.sign(P[:, 0] - tc[0]) == sgn)
        if q.sum() < 20:
            continue
        x = np.sort(np.abs(P[q][:, 0] - tc[0])); g = np.diff(x); ok = (x[:-1] > 0.04) & (x[:-1] < 0.25)
        if not ok.any() or g[ok].max() < 0.01:
            continue
        i = np.flatnonzero(ok)[np.argmax(g[ok])]; cut = x[i] + 1e-6; sel_ = np.abs(P[q][:, 0] - tc[0]) > cut; pts = P[q][sel_]
        c_ = RGB[q][sel_]; cloth = ((c_[:, 2] > c_[:, 0] + 0.03) | ((c_[:, 0] > 0.3) & (c_[:, 1] > c_[:, 0] - 0.04) & (c_[:, 2] < c_[:, 1] - 0.1))).mean() if sel_.any() else 1
        if len(pts) > 8 and (y > yv(0.6) or cloth < 0.5):   # below the waist an arm band must not be mostly cloth (teal / blue, or a yellow hem band); dark (shadowed hand, black glove) counts as hand
            arm[side].append((y + 0.005, pts.mean(0), len(pts), pts[:, 1].min()))
for side in 'lr':
    rows = sorted(arm[side], key=lambda r: -r[0])
    if not rows:
        raise SystemExit(f'no {side} arm found')
    # track the arm down from its top: each band's cluster within 3 cm (x, z) of the band above; stop at the first that is not
    # (below the hand the "arm" side of the gap is the tunic's edge)
    keep = [rows[0]]
    for r in rows[1:]:
        if abs(r[0] - keep[-1][0]) <= 0.025 and np.linalg.norm(r[1][[0, 2]] - keep[-1][1][[0, 2]]) < 0.03:
            keep.append(r)
        elif r[0] < keep[-1][0] - 0.025:
            break
    rows = keep; ys = np.array([r[0] for r in rows]); cs = np.array([r[1] for r in rows])
    low = rows[-1][3]; top = ys.max()
    sh_y = min(top, yv(0.77)); i = np.argmin(np.abs(ys - sh_y)); J[f'upperarm_{side}'] = [cs[i, 0], yv(0.77), cs[i, 2]]
    tip = P[(P[:, 1] < low + 0.01) & (P[:, 1] >= low - 0.005) & (np.linalg.norm(P[:, [0, 2]] - cs[-1, [0, 2]], axis=1) < 0.05)]
    J[f'hand_tip_{side}'] = [tip[:, 0].mean(), low, tip[:, 2].mean()]
    wy = low + 0.11 * H; i = np.argmin(np.abs(ys - wy)); J[f'hand_{side}'] = [cs[i, 0], wy, cs[i, 2]]
    ey = wy + 0.38 * (yv(0.77) - wy); i = np.argmin(np.abs(ys - ey)); J[f'lowerarm_{side}'] = [cs[i, 0], ey, cs[i, 2]]
    about.append(f"{side} arm tracked from y {top:.3f} down to {ys.min():.3f} ({len(rows)} bands); hand tip {low:.3f}")
# waist: the narrowest trunk band between hem and armpits, unless given
waist = a.waist
if waist is None:
    best = None
    for y in np.arange(hem + 0.05, yv(0.70), 0.01):
        q = band(y, y + 0.01, np.abs(P[:, 0] - tc[0]) < 0.2); x = P[q][:, 0]
        if len(x) > 50:
            w = np.percentile(x, 98) - np.percentile(x, 2)
            if best is None or w < best[0]:
                best = (w, y)
    waist = round(float(best[1]), 3)
J = {k_: [round(float(v), 4) for v in p] for k_, p in J.items()}
out = {'about': f"Measured by authoring/tools/people/splat/character/landmarks.py on {a.splat} (levelled final: Y up, front +Z, left +X). Sole {sole:.3f}, crown {crown:.3f} (H {H:.3f}). "
       f"Hips ESTIMATED at {a.hip_frac} H, +-0.054 H. Hem {hem} ({'given' if a.hem is not None else 'first filled centre column'}), waist {waist} ({'given' if a.waist is not None else 'narrowest trunk band'}). " + '; '.join(about),
       'sole': round(float(sole), 4), 'joints': J, 'skirt': {'waist': waist, 'hem': hem}}
json.dump(out, open(a.out, 'w'), indent=1)
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
fig, ax = plt.subplots(1, 2, figsize=(14, 13), dpi=100)
for axx, (c, lab) in zip(ax, ((0, 'front: x (his left to the right)'), (2, 'side: z (his front to the right)'))):
    o = np.argsort(P[:, 2] if c == 0 else P[:, 0]); axx.scatter(P[o, c], P[o, 1], s=0.2, c=RGB[o], alpha=0.5)
    for n, p in J.items():
        axx.plot(p[c], p[1], 'o', ms=7, mfc='none', mew=1.6, mec='cyan' if n.endswith('_l') else 'magenta' if n.endswith('_r') else 'lime')
        axx.annotate(n, (p[c], p[1]), fontsize=7, color='w')
    for yy, nm in ((waist, 'waist'), (hem, 'hem')):
        axx.axhline(yy, color='yellow', lw=0.8, ls='--'); axx.annotate(nm, (axx.get_xlim()[0], yy), color='yellow', fontsize=8)
    axx.set_facecolor('#222'); axx.set_aspect('equal'); axx.set_xlabel(lab); axx.grid(alpha=0.2)
ax[0].set_title('front (cyan = his left, magenta = his right)'); ax[1].set_title('side')
plt.tight_layout(); plt.savefig(a.png)
print(json.dumps({'sole': out['sole'], 'H': round(float(H), 3), 'waist': waist, 'hem': hem, 'notes': about}))
