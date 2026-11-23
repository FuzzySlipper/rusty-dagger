#!/usr/bin/env python3
"""Rebind a long skirt so it stretches between the legs instead of tearing where they part. An edit of the binding: no new bake.

    skirt_weights.py SPLAT.npz BINDING.npz SCAFFOLD.glb TRANSFORM.json OUT_DIR [--width 1.0] [--hem Y] [--top Y] [--dark-under 0.3] [--hips 0.5]
                     [--solid 3] [--bones SKIRT.json]

A mesher's rig gives each half of a skirt to the leg behind it, so a bound bake's skirt is led by four bones (left and right thigh
and shin) and splits down the middle when one leg lifts; the inside of the skirt, which no camera saw, then shows (villager 454,
2026-11-16). This keeps every skirt Gaussian's own split between thigh and shin and between legs and hips, and changes only which
SIDE it follows: instead of all of one leg, a share of each, running smoothly from the right leg's axis to the left leg's at the
Gaussian's own height (--width widens that band: 1.0 = from axis to axis). Straight in front of or behind a leg nothing changes,
so the hem still covers that boot; between the legs the cloth takes a blend of both and stretches like a web.

The skirt is every Gaussian a leg bone leads between the hem and the top: --hem (default: found on the scaffold, the lowest height
where its depth front to back is over 0.6 of its largest below the hips) and --top (default: the hip joints' height). Gaussians
within 6 cm of a shin's axis below the knee stay as they are (boot shafts inside the skirt); down to 3 cm under the hem line a
Gaussian still counts as skirt when it is more than 8 cm from both shins.

--hips F moves a share of each skirt Gaussian's leg weight onto Hips, from none at the top to F at the hem: the skirt then hangs
from the pelvis and follows the legs only partly, rather than walking as two wide trouser legs (397-B, 2026-11-19).

--solid K makes the skirt Gaussians more opaque, 1-(1-a)^K: a thin baked skirt back otherwise shows the legs inside (397-B).

--bones SKIRT.json (skirt_bones.py rig) puts the skirt on a ring of skirt bones instead of the legs: --width and --hips are then
unused, and --drop-inside (default 3 cm) removes the legs inside the bell: on skirt bones they rode the cloth and hung out of the
hem as dark strands in a stride (398, 397-B, 453; Patch 2026-11-21: "bits of leg stay attached to the dress"), and the skirt hangs from the pelvis as one bell that the legs push open (skirt_bones.py drive, per clip). Boot shafts
above the hem then go with the skirt too: the bell swings clear of the shins, and what stayed on them showed as specks.

--dark-under F recolours the skirt Gaussians that sit on the scaffold's downward-facing surface (the underside of the hem) to F
times the skirt's median colour, so what shows under a lifted hem reads as shadow rather than as noise.

Writes OUT_DIR/binding.npz and, with --dark-under, OUT_DIR/splat.npz (else a link to SPLAT.npz), and skirt.json (counts, heights).
All heights are in the bake's units.
"""
import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

sys.path.insert(0, str(Path(__file__).parent))
from retarget_clip import load_rig   # noqa: E402


def smooth(x):
    x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)


def region(rig, T, M, bones, weights, hem=None, top=None, shins=True):
    """The skirt of a bound bake (bake frame): every Gaussian a leg bone leads between the hem and the top, less the boot shafts
    (shins=False: with them; on skirt bones the bell swings clear of the legs, and what stayed on a shin above the hem showed as
    skirt-coloured specks round the forward foot: 398, 456, 2026-11-20).
    Returns the rig's joints and vertices in the bake frame, the dense weights, the mask and the heights."""
    names = list(rig["names"]); sc, rot, tr = T["scale"], np.array(T["rotation"]), np.array(T["translation"])
    J = {n: sc * np.asarray(rig["bind"][i], float) @ rot.T + tr for i, n in enumerate(names)}
    V = sc * np.asarray(rig["positions"], float) @ rot.T + tr
    n = len(M)
    idx = {k: names.index(k) for k in ("Hips", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot")}
    top = top if top is not None else float((J["LeftUpLeg"][1] + J["RightUpLeg"][1]) / 2)
    if hem is None:   # the scaffold's depth by height below the hips: boots are shallow, the skirt's bell is deep
        floor = max(J["LeftFoot"][1], J["RightFoot"][1]) + 0.03 * sc   # never below the ankles: 397-B's sandals made the feet the deepest
        # part and put the hem at the floor, so her shins were skirt
        ys = np.linspace(floor, top, 60); depth = np.array([np.ptp(V[(V[:, 1] >= lo) & (V[:, 1] < hi), 2]) if ((V[:, 1] >= lo) & (V[:, 1] < hi)).sum() > 5 else 0 for lo, hi in zip(ys[:-1], ys[1:])])
        hem = float(ys[:-1][np.argmax(depth > 0.6 * depth.max())])
    # per Gaussian: the weight on each bone
    W = np.zeros((n, len(names))); np.add.at(W, (np.repeat(np.arange(n), bones.shape[1]), bones.ravel()), weights.ravel())
    legs = [idx[k] for k in ("LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg")]
    skirt = np.isin(W.argmax(1), legs) & (M[:, 1] < top)

    def shin_dist(side):
        k, f = J[side + "Leg"], J[side + "Foot"]; d = f - k; t = np.clip(((M - k) @ d) / (d @ d), 0, 1)
        return np.linalg.norm(M - (k + t[:, None] * d), axis=1), t
    near = np.full(n, np.inf)
    for side in ("Left", "Right"):
        dist, t = shin_dist(side); near = np.minimum(near, dist)
        if shins:
            skirt &= ~((dist < 0.06 * sc) & (t > 0.0) & (M[:, 1] < J[side + "Leg"][1]))
    # the hem's lowest edge hangs a little under the height found on the scaffold: down to 3 cm below it a Gaussian is still skirt
    # when it is clear of both shins (8 cm), else boot (454: hem pieces just under the line stayed on one shin and flew off the hem)
    skirt &= (M[:, 1] > hem) | ((M[:, 1] > hem - 0.03 * sc) & (near > 0.08 * sc))
    return dict(names=names, J=J, V=V, W=W, idx=idx, legs=legs, skirt=skirt, hem=hem, top=top, sc=sc, rot=rot, tr=tr)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("splat", type=Path); ap.add_argument("binding", type=Path); ap.add_argument("scaffold", type=Path); ap.add_argument("transform", type=Path); ap.add_argument("out", type=Path)
    ap.add_argument("--width", type=float, default=1.0); ap.add_argument("--hem", type=float); ap.add_argument("--top", type=float); ap.add_argument("--dark-under", type=float)
    ap.add_argument("--solid", type=float, help="skirt opacity becomes 1-(1-a)^SOLID (e.g. 3): a thin baked skirt back no longer shows the legs inside")
    ap.add_argument("--hips", type=float, default=0.0, help="share of every skirt Gaussian's leg weight moved onto Hips, growing from 0 at the top to this at the hem")
    ap.add_argument("--drop-inside", type=float, help="with --bones: metres (default 0.03; 0: off). Drop skirt Gaussians lying this far inside the skirt's outer shell (the legs under a long skirt, which on skirt bones ride the cloth and show at the hem; Patch 2026-11-21)")
    ap.add_argument("--drop-hem-dark", type=float, default=0.0, help="with --bones: luminance (0: off). Drop near-black Gaussians a shin leads within 5 cm of the hem: the shadow under an ankle-length hem baked onto the ankles, which trails the back heel in a stride (453, Patch 2026-11-21)")
    ap.add_argument("--bones", type=Path, help="skirt_bones.py rig's skirt.json: put the skirt on that ring of bones instead (SCAFFOLD is then its rig)")
    a = ap.parse_args()
    rig = load_rig(a.scaffold); T = json.loads(a.transform.read_text())
    s = dict(np.load(a.splat)); b = np.load(a.binding); M = s["means"].astype(float); n = len(M)
    G = region(rig, T, M, b["bones"].astype(np.int64), b["weights"].astype(np.float64), a.hem, a.top, shins=a.bones is None)
    names, J, V, W, idx, legs, skirt, hem, top, rot = (G[k] for k in ("names", "J", "V", "W", "idx", "legs", "skirt", "hem", "top", "rot"))

    def axis_x(side, y):   # the leg's axis (hip joint, knee, ankle) at height y
        h, k, f = J[side + "UpLeg"], J[side + "Leg"], J[side + "Foot"]
        return np.where(y > k[1], h[0] + (k[0] - h[0]) * (h[1] - y) / (h[1] - k[1]), k[0] + (f[0] - k[0]) * (k[1] - y) / (k[1] - f[1]))
    if a.bones:   # 395/397 (Patch 2026-11-19): the skirt on its own ring of bones, pushed out by the legs (skirt_bones.py)
        from skirt_bones import bind_weights
        C = json.loads(a.bones.read_text()); W2, wrep = bind_weights(W, (M - G["tr"]) @ rot / G["sc"], skirt, C)
        rep_extra = {"bones": C["joints"], **wrep}
    else:
        xl, xr = axis_x("Left", M[:, 1]), axis_x("Right", M[:, 1])
        mid, half = (xl + xr) / 2, np.maximum((xl - xr) / 2 * a.width, 1e-6)
        wl = smooth(0.5 + 0.5 * (M[:, 0] - mid) / half)   # share of the left leg: 0 at the right leg's axis, 1 at the left's
        thigh = W[:, idx["LeftUpLeg"]] + W[:, idx["RightUpLeg"]]; shin = W[:, idx["LeftLeg"]] + W[:, idx["RightLeg"]]
        W2 = W.copy()
        W2[skirt, idx["LeftUpLeg"]] = (thigh * wl)[skirt]; W2[skirt, idx["RightUpLeg"]] = (thigh * (1 - wl))[skirt]
        W2[skirt, idx["LeftLeg"]] = (shin * wl)[skirt]; W2[skirt, idx["RightLeg"]] = (shin * (1 - wl))[skirt]
        if a.hips > 0:   # 397-B (Patch 2026-11-19): with the legs leading, a long skirt walks as two wide trouser legs; the hips carry part of it
            f = a.hips * smooth((top - M[:, 1]) / max(top - hem, 1e-6)) * skirt
            lw = W2[:, legs].sum(1); W2[:, legs] *= (1 - f)[:, None]; W2[:, idx["Hips"]] += f * lw
        rep_extra = {"width": a.width, "shared_between_legs": int((skirt & (wl > 0.05) & (wl < 0.95)).sum()),
                     "half_and_half": int((skirt & (wl > 0.4) & (wl < 0.6)).sum()), "hips": a.hips}
    drop = np.zeros(n, bool)
    if a.drop_inside is None:
        a.drop_inside = 0.03 if a.bones else 0.0
    if a.bones and a.drop_inside > 0:   # the legs inside the bell: on skirt bones they ride the cloth and hang out of the hem in a stride
        P = (M - G["tr"]) @ rot / G["sc"]; c = np.array(C["centre"]); rel = P[:, [0, 2]] - c
        az = np.arctan2(rel[:, 0], rel[:, 1]); rr = np.linalg.norm(rel, axis=1)
        S, H = 32, 24; si = np.floor((az + np.pi) / (2 * np.pi) * S).astype(int) % S
        hy = np.clip(((C["top"] - P[:, 1]) / max(C["top"] - C["hem"], 1e-6) * H).astype(int), 0, H - 1)
        shell = np.full((S, H), np.nan)
        for i in range(S):
            for j in range(H):
                m = skirt & (si == i) & (hy == j)
                if m.sum() >= 8:
                    shell[i, j] = np.percentile(rr[m], 90)
        for i in range(S):   # fill a sector with no skirt from the heights either side, then from the sectors either side
            ok = ~np.isnan(shell[i])
            if ok.sum() >= 2:
                shell[i] = np.interp(np.arange(H), np.where(ok)[0], shell[i][ok])
        for i in range(S):
            if np.isnan(shell[i]).any():
                shell[i] = np.nanmean(shell[[(i - 1) % S, (i + 1) % S]], 0)
        below = P[:, 1] < C["top"] - 2 * C["fade"]   # leave the waist, where the skirt lies on the body
        drop = skirt & below & (rr < shell[si, hy] - a.drop_inside)
        rep_extra["dropped_inside"] = int(drop.sum()); rep_extra["drop_inside_m"] = a.drop_inside
    if a.bones and a.drop_hem_dark > 0:
        lum = np.clip(s["rgb"].astype(float), 0, 1) @ np.array([0.3, 0.59, 0.11])
        shin = np.isin(W.argmax(1), [idx["LeftLeg"], idx["RightLeg"]])
        dark = ~skirt & shin & (np.abs(M[:, 1] - hem) < 0.05 * G["sc"]) & (lum < a.drop_hem_dark)
        drop |= dark; rep_extra["dropped_hem_dark"] = int(dark.sum())
    order = np.argsort(-W2, 1)[:, :4]; tw = np.take_along_axis(W2, order, 1); tw /= tw.sum(1, keepdims=True)
    a.out.mkdir(parents=True, exist_ok=True); (a.out / "binding.npz").unlink(missing_ok=True)   # a new file: the old one may be hard-linked into a character folder
    if drop.any() and a.dark_under is not None:
        raise SystemExit("--drop-inside and --dark-under together are not supported")
    keep = ~drop
    if drop.any():
        s = {k: (v[keep] if getattr(v, "shape", ()) and v.shape[0] == n else v) for k, v in s.items()}
        order, tw, skirt = order[keep], tw[keep], skirt[keep]
    np.savez_compressed(a.out / "binding.npz", bones=order.astype(b["bones"].dtype), weights=tw.astype(np.float32))
    rep = {"gaussians": n, "skirt": int(skirt.sum()), "hem": round(hem, 4), "top": round(top, 4), **rep_extra}
    out_splat = a.out / "splat.npz"
    if out_splat.is_symlink() or out_splat.exists():
        out_splat.unlink()
    if a.solid:   # 397-B's skirt back baked at median opacity 0.24: the legs inside showed through it (Patch 2026-11-19)
        o = 1 / (1 + np.exp(-s["opacities"].astype(np.float64))); o2 = 1 - (1 - o) ** a.solid
        lo = np.log(np.clip(o2, 1e-6, 1 - 1e-6) / np.clip(1 - o2, 1e-6, 1)); new = s["opacities"].copy()
        new[skirt] = lo[skirt].reshape(new[skirt].shape).astype(new.dtype); s["opacities"] = new; rep["solid"] = a.solid
    if a.dark_under is not None:
        if "NORMAL" not in rig["attributes"]:
            raise SystemExit("--dark-under needs the scaffold's normals")
        nrm = np.asarray(rig["acc"](rig["attributes"]["NORMAL"]), float) @ rot.T   # attributes holds accessor numbers
        j = cKDTree(V).query(M, workers=-1)[1]
        under = skirt & (nrm[j, 1] < -0.5) & (M[:, 1] < hem + 0.25 * (top - hem))
        rgb = np.clip(s["rgb"].astype(float), 0, 1); base = np.median(rgb[skirt & ~under], 0) * a.dark_under
        new = s["rgb"].copy(); new[under] = base.astype(new.dtype); s["rgb"] = new
        rep.update(dark_under=a.dark_under, recoloured=int(under.sum()), colour=np.round(base, 3).tolist())
    if a.solid or a.dark_under is not None or drop.any():
        np.savez_compressed(out_splat, **s)
    else:
        os.symlink(a.splat.resolve(), out_splat)
    (a.out / "skirt.json").write_text(json.dumps(rep, indent=1) + "\n"); print(json.dumps(rep))


if __name__ == "__main__":
    main()
