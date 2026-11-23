#!/usr/bin/env python3
"""hem_check.py RIG.npz STATIC.npz CLIP.glb T0 PERIOD N --waist Y --hem Y [--abduct DEG --forearm DEG]: a one-piece tunic or kilt over a
walk cycle (copied from townsman 385's runtime/hem_check.py, live-evidence/town385-hires-20261112-r01; his waist 0.075 and hem
-0.135 are now --waist / --hem, from the character's landmarks) (pose_splat_preview.py, N phases of one cycle from T0), per phase:
  legs out   opaque leg/shoe Gaussians (rig regions 3/4, under the hem) OUTSIDE the posed tunic above its zigzag teeth (per
             10-deg sector: the tunic's 2nd-percentile height + 4 cm): per 2 cm height band and
             10-deg sector about the tunic's waist axis, further out than the tunic's outer surface there (90th-percentile
             radius of the tunic band: region 0 and the hung pouch/case between the hem and the belt), in cells the tunic
             covers (10+ opaque Gaussians). This is the striped leg showing through the cloth.
  hem torn   opaque tunic Gaussians (static y hem-0.015..waist) more than 3 cm (his size) from all 8 of their static nearest
             neighbours after posing; split front (z > axis) / back.
  hands in   opaque arm Gaussians under the belt INSIDE that outer surface by more than 1 cm (a hand cutting into the
             tunic, pouch or case), and the deepest in cm.
Prints per phase counts and worst values."""
import os, subprocess, sys, tempfile
import numpy as np
from scipy.spatial import cKDTree
C = os.path.join(os.environ['ASSET_CPU_REPO'], 'gsplat/pose_splat_preview.py') if os.environ.get('ASSET_CPU_REPO') else \
    os.path.join(os.path.dirname(os.path.abspath(__file__)), '../gsplat/pose_splat_preview.py')   # on a Halo: the asset-cpu repo copy
rig, static, clip, t0, period, n = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4]), float(sys.argv[5]), int(sys.argv[6])
args = sys.argv[7:]
WAIST = float(args[args.index('--waist') + 1]); HEM = float(args[args.index('--hem') + 1])
extra = [x for i, x in enumerate(args) if x not in ('--waist', '--hem') and (i == 0 or args[i - 1] not in ('--waist', '--hem'))]
times = [t0 + i * period / n for i in range(n)]
tmp = tempfile.mkdtemp(prefix='hem_check-')
subprocess.run([sys.executable, '-I', C, rig, tmp] + [f'p{i}={clip}@{t:.3f}' for i, t in enumerate(times)] + extra, check=True, stdout=subprocess.DEVNULL)
r = np.load(rig); S = float(r['scale']); reg = r['region']
st = np.load(static); m0 = st['means']; y0 = m0[:, 1]
op = 1 / (1 + np.exp(-st['opacities'].reshape(-1))) > 0.3
tunic = op & np.isin(reg, [0, 5]) & (y0 < WAIST) & (y0 > HEM - 0.02)
legs = op & np.isin(reg, [3, 4]) & (y0 < HEM - 0.01)
arms = op & np.isin(reg, [1, 2]) & (y0 < WAIST)
band = op & (reg == 0) & (y0 < WAIST) & (y0 > HEM - 0.015)
_, nb = cKDTree(m0).query(m0, k=9); nb = nb[:, 1:]
where = []
res = {'legs': [], 'torn_f': [], 'torn_b': [], 'hands': []}; worst_leg = worst_hand = worst_gap = 0.0
for i in range(n):
    m = np.load(f'{tmp}/p{i}/splat.npz')['means']
    T = m[tunic]; top = T[T[:, 1] > np.percentile(T[:, 1], 95)]; c = np.median(top[:, [0, 2]], 0)
    pol = lambda p: (np.hypot(p[:, 0] - c[0], p[:, 2] - c[1]), (np.degrees(np.arctan2(p[:, 0] - c[0], p[:, 2] - c[1])) % 360 // 10).astype(int), np.floor(p[:, 1] / 0.02).astype(int))  # noqa: E731
    tr, ta, tb = pol(T); outer = {}
    keys = ta * 1000 + tb
    for key in np.unique(keys):
        sel = keys == key
        if sel.sum() >= 10: outer[int(key)] = np.percentile(tr[sel], 90)
    # The hem is zigzag: legs between the teeth are not through the cloth. Per 10-deg sector, only legs above the
    # teeth (2nd-percentile tunic height + 4 cm) count.
    hem_top = {int(a_): np.percentile(T[ta == a_, 1], 2) + 0.04 for a_ in np.unique(ta) if (ta == a_).sum() >= 20}
    for name, sel_mask, outside in (('legs', legs, True), ('hands', arms, False)):
        P = m[sel_mask]; pr, pa, pb = pol(P); cnt = 0; worst = 0.0
        for j in range(len(pr)):
            o = outer.get(int(pa[j] * 1000 + pb[j]))
            if o is None: continue
            if outside and P[j, 1] < hem_top.get(int(pa[j]), 1.0): continue
            d = (pr[j] - o) * S * 100 if outside else (o - pr[j]) * S * 100
            if d > (0 if outside else 1.0):
                cnt += 1; worst = max(worst, d)
                if os.environ.get('WHERE'): where.append((name, i, *np.round(m0[sel_mask][j], 3).tolist(), int(reg[sel_mask][j]), round(float(d), 1)))
        res[name].append(cnt)
        if name == 'legs': worst_leg = max(worst_leg, worst)
        else: worst_hand = max(worst_hand, worst)
    gap = np.linalg.norm(m[nb] - m[:, None], axis=2).min(1) * S
    torn = band & (gap > 0.03); front = m0[:, 2] > np.median(m0[band, 2])
    res['torn_f'].append(int((torn & front).sum())); res['torn_b'].append(int((torn & ~front).sum()))
    worst_gap = max(worst_gap, float(gap[band].max()))
    if os.environ.get('WHERE') and i == 0:
        for j in np.where(torn)[0]: print('  torn (static x y z, region, gap cm):', np.round(m0[j], 3).tolist(), int(reg[j]), round(float(gap[j] * 100), 1))
for name in ('legs', 'hands'):
    w = sorted([x for x in where if x[0] == name], key=lambda x: -x[-1])
    if w: print(f'  worst {name} (phase, static x y z, region, cm):', w[:12])
print(f"{os.path.basename(rig)} {' '.join(extra)}: phases at {[round(t, 3) for t in times]}")
print(f"  legs out per phase {res['legs']}  worst {worst_leg:.1f} cm  (of {legs.sum()} leg Gaussians)")
print(f"  hem torn >3 cm front {res['torn_f']} back {res['torn_b']}  worst gap {worst_gap * 100:.1f} cm  (of {band.sum()} tunic)")
print(f"  hands in >1 cm {res['hands']}  deepest {worst_hand:.1f} cm  (of {arms.sum()} arm Gaussians under the belt)")
