#!/usr/bin/env python3
"""A coloured copy of the scaffold to draw UNDER a bound bake, so gaps in the splats show the figure's colour instead of what is behind.

    underlayer.py RIGGED.glb TRANSFORM.json SOURCE.npz OUT_DIR [--count 150000] [--inset-mm 4] [--sigma-mm 10] [--reach-mm 40]
                  [--beneath-mm 6 [--deepest-mm 30]] [--body BODY.npz:BINDING.npz]

Patch's idea (2026-11-16): "if there was a roughly textured via splats mesh under it ... it could smooth out holes by providing a
colour underneath kinda like transparent color fringe on a texture for filtering". The scaffold a bound bake was held to is still
there under the Gaussians (they stand a median 9 to 10 mm off its surface), so it only shows where the splats are thin.

Points are spread evenly over the scaffold (area-weighted, as bind_colours.py), pulled --inset-mm inside along the normal so the
copy stays within the figure's outline, and coloured from SOURCE.npz, the full-size bound bake: a broad average of the Gaussians
round each point (opacity times a falloff of --sigma-mm), so the colour is the figure's local colour, not its detail. A point with
no Gaussian within --reach-mm takes the colour of the nearest point that has one.

A fixed inset is not enough (381, first try): a bound bake's Gaussians are held within a reach of the scaffold on BOTH sides, so
where the scaffold is fuller than the figure the baked surface lies inside it, and a copy 4 mm in sits in front of the splats there
and smears the face, the lacing and the hem. --beneath-mm M puts each point M below the baked surface where that is lower: the
lower quartile of the nearby Gaussians' heights above the scaffold along its normal, less M, smoothed over neighbouring points and
no deeper than --deepest-mm.

For testing without a mesh renderer the copy is written as opaque flat Gaussians in the bake's frame, bound to the rig with the
mesh's own weights: OUT_DIR/under.npz and under-binding.npz (render_posed.py, clip_compare.py --bound). With --body the body's
Gaussians are added in front: OUT_DIR/with-under.npz and with-under-binding.npz. A runtime would draw the scaffold mesh with these
colours on its vertices instead; OUT_DIR/vertex-colours.npy holds one colour per scaffold vertex for that.
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("glb", type=Path); ap.add_argument("transform", type=Path); ap.add_argument("source", type=Path); ap.add_argument("out", type=Path)
    ap.add_argument("--count", type=int, default=150000); ap.add_argument("--inset-mm", type=float, default=4.0)
    ap.add_argument("--sigma-mm", type=float, default=10.0); ap.add_argument("--reach-mm", type=float, default=40.0)
    ap.add_argument("--beneath-mm", type=float, help="keep every point at least this far under the baked surface (see above)")
    ap.add_argument("--deepest-mm", type=float, default=30.0)
    ap.add_argument("--body", help="BODY.npz:BINDING.npz to write with the underlayer added")
    a = ap.parse_args()
    raw = a.glb.read_bytes(); jl = struct.unpack("<I", raw[12:16])[0]; g = json.loads(raw[20:20 + jl]); blob = raw[20 + jl + 8:]

    def acc(i):
        ac = g["accessors"][i]; bv = g["bufferViews"][ac["bufferView"]]; n, dt = NC[ac["type"]], np.dtype(CT[ac["componentType"]])
        off, stride = bv.get("byteOffset", 0) + ac.get("byteOffset", 0), bv.get("byteStride") or n * dt.itemsize
        arr = (np.frombuffer(blob, dt, ac["count"] * n, off).reshape(ac["count"], n) if stride == n * dt.itemsize
               else np.stack([np.frombuffer(blob, dt, n, off + k * stride) for k in range(ac["count"])]))
        return arr.astype(np.float32) / np.iinfo(dt).max if ac.get("normalized") else arr

    pr = g["meshes"][0]["primitives"][0]; at = pr["attributes"]
    pos, nrm = acc(at["POSITION"]).astype(float), acc(at["NORMAL"]).astype(float)
    jnt, wgt = acc(at["JOINTS_0"]).astype(int), acc(at["WEIGHTS_0"]).astype(float)
    tri = acc(pr["indices"]).astype(int).reshape(-1, 3); nb = len(g["skins"][0]["joints"])
    rng = np.random.default_rng(0); n = a.count
    pa, pb, pc = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(pb - pa, pc - pa), axis=1); cum = np.cumsum(area)
    t = np.searchsorted(cum, (np.arange(n) + rng.random(n)) / n * cum[-1]); u, w = rng.random(n), rng.random(n); s = np.sqrt(u)
    bary = np.stack([1 - s, s * (1 - w), s * w], 1)
    p = (pos[tri[t]] * bary[:, :, None]).sum(1); nn = (nrm[tri[t]] * bary[:, :, None]).sum(1)
    nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9)
    W = np.zeros((n, nb))
    for k in range(3):
        for j in range(4):
            np.add.at(W, (np.arange(n), jnt[tri[t, k], j]), wgt[tri[t, k], j] * bary[:, k])
    top = np.argsort(-W, 1)[:, :4]; tw = np.take_along_axis(W, top, 1); tw /= tw.sum(1, keepdims=True)
    T = json.loads(a.transform.read_text()); scale, rot, trans = T["scale"], np.array(T["rotation"]), np.array(T["translation"])
    mm = scale / 1000   # one millimetre of the scaffold (it is in metres) in the bake's units

    def to_bake(x):
        return scale * x @ rot.T + trans
    sp = np.load(a.source); op = 1 / (1 + np.exp(-sp["opacities"].reshape(-1))); keep = op > 0.15
    means, rgb, op = sp["means"][keep], np.clip(sp["rgb"][keep], 0, 1), op[keep]; tree = cKDTree(means)

    def colour(x_bake):
        d, j = tree.query(x_bake, k=24, workers=-1)
        wk = op[j] * np.exp(-0.5 * (d / (a.sigma_mm * mm)) ** 2) + 1e-12
        col = (rgb[j] * wk[:, :, None]).sum(1) / wk.sum(1, keepdims=True); far = d[:, 0] > a.reach_mm * mm
        if far.any() and (~far).any():   # nothing baked near it: the nearest coloured point's colour
            col[far] = col[~far][cKDTree(x_bake[~far]).query(x_bake[far], workers=-1)[1]]
        return col, far
    surf = to_bake(p); col, far = colour(surf); nf = nn @ rot.T
    depth = np.full(n, -a.inset_mm * mm); placed = {}
    if a.beneath_mm is not None:
        d, j = tree.query(surf, k=32, workers=-1)
        h = ((means[j] - surf[:, None, :]) * nf[:, None, :]).sum(-1)          # height of each nearby Gaussian above the scaffold, along its normal
        h = np.where((d < 20 * mm) & (op[j] > 0.3), h, np.nan)
        with np.errstate(all="ignore"):
            low = np.nanpercentile(h, 25, axis=1)
        want = np.where(np.isfinite(low), np.minimum(depth, low - a.beneath_mm * mm), depth)
        want = np.maximum(want, -a.deepest_mm * mm)
        nb_ = cKDTree(surf).query(surf, k=12, workers=-1)[1]; depth = want[nb_].mean(1)   # smooth, so the copy is not bumpy
        placed = {"in_front_of_the_bake_at_plain_inset": round(float((np.isfinite(low) & (low < -a.inset_mm * mm)).mean()), 4),
                  "depth_mm_median": round(float(np.median(-depth) / mm), 2), "depth_mm_p95": round(float(np.percentile(-depth, 95) / mm), 2)}
    inner = surf + nf * depth[:, None]
    spacing = float(np.sqrt(area.sum() / n)) * scale; r = 1.3 * spacing
    # flat opaque discs facing along the normal: rotation taking +z to the normal, as a wxyz quaternion
    z = np.array([0.0, 0.0, 1.0]); ax = np.cross(np.tile(z, (n, 1)), nf); sn = np.linalg.norm(ax, axis=1); cs = nf @ z
    ax = np.where(sn[:, None] > 1e-8, ax / np.maximum(sn, 1e-8)[:, None], np.array([1.0, 0, 0]))
    q = Rotation.from_rotvec(ax * np.arctan2(sn, cs)[:, None]).as_quat()   # x y z w
    under = {"means": inner.astype(np.float32), "quats": q[:, [3, 0, 1, 2]].astype(np.float32),
             "scales": np.log(np.tile([r, r, 0.2 * r], (n, 1))).astype(np.float32),
             "opacities": np.full(n, 5.3, np.float32), "rgb": col.astype(np.float32)}
    bind = {"bones": top.astype(np.int16), "weights": tw.astype(np.float32)}
    a.out.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(a.out / "under.npz", **under); np.savez_compressed(a.out / "under-binding.npz", **bind)
    vcol, vfar = colour(to_bake(pos)); np.save(a.out / "vertex-colours.npy", np.round(vcol * 255).astype(np.uint8))
    rep = {"points": n, "spacing_mm": round(spacing / mm, 2), "disc_radius_mm": round(r / mm, 2), "inset_mm": a.inset_mm, "sigma_mm": a.sigma_mm,
           "no_bake_within_reach": round(float(far.mean()), 4), "vertices": len(pos), "vertices_no_bake_within_reach": round(float(vfar.mean()), 4), **placed}
    if a.body:
        b, bb = a.body.split(":"); body, bbind = np.load(b), np.load(bb)
        np.savez_compressed(a.out / "with-under.npz", **{k: np.concatenate([body[k].reshape((-1,) + under[k].shape[1:]), under[k]]) for k in under})
        np.savez_compressed(a.out / "with-under-binding.npz", bones=np.concatenate([bbind["bones"].astype(np.int16), bind["bones"]]),
                            weights=np.concatenate([bbind["weights"].astype(np.float32), bind["weights"]]))
        rep["body_gaussians"] = int(len(body["means"]))
    (a.out / "underlayer.json").write_text(json.dumps(rep, indent=1) + "\n"); print(json.dumps(rep))


if __name__ == "__main__":
    main()
