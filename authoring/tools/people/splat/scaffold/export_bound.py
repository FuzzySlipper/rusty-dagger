#!/usr/bin/env python3
"""Put a shell-bound bake onto its scaffold's rig.

    export_bound.py SPLAT.npz RIGGED.glb TRANSFORM.json OUT.bin [--min-opacity 0.25] [--disc 1.6] [--binding OUT.npz]
                    [--loose-bones LeftHand,RightHand --loose-factor 2.4]

SPLAT.npz is the bake (in the bake's frame); RIGGED.glb is the one skinned scaffold mesh it was
held to, and TRANSFORM.json places that mesh in the bake's frame (score_outline.py). Each Gaussian
is bound to the nearest point of the scaffold and takes the mesh's skin weights and normal there;
its centre is carried back into the mesh's rest space, standing off the surface as it was baked.
OUT.bin is the test page's 32-byte disc buffer (?base=NAME&splats=FILE): one opaque disc per
Gaussian above --min-opacity, with a radius of --disc times the geometric mean of its two largest
axes. That page does not draw Gaussians, so this shows the binding and the motion, not the bake's
exact look. Prints the count and how far the centres stand from the surface.
--loose-bones and --loose-factor are bound_data.py's: the surface those bones lead has that much
more reach, and a Gaussian standing in that room binds to the hand or to the rest of the scaffold
by its neighbours in place and colour (--loose-sure, --loose-colour).
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
ap = argparse.ArgumentParser()
ap.add_argument("splat"); ap.add_argument("glb"); ap.add_argument("transform"); ap.add_argument("out")
ap.add_argument("--min-opacity", type=float, default=0.25); ap.add_argument("--disc", type=float, default=1.6)
ap.add_argument("--loose-bones"); ap.add_argument("--loose-factor", type=float, default=2.4)
ap.add_argument("--loose-sure", type=float, default=0.006, help="metres: nearer the rest of the scaffold than this, a Gaussian belongs to it")
ap.add_argument("--loose-colour", type=float, default=0.1, help="metres one full step of colour counts for when matching undecided Gaussians")
ap.add_argument("--binding", help="also write bones and weights for every Gaussian (in SPLAT.npz order) to this NPZ, for render_posed.py")
a = ap.parse_args()
import struct
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
raw = Path(a.glb).read_bytes()
jl = struct.unpack("<I", raw[12:16])[0]
g = json.loads(raw[20:20 + jl])
blob = raw[20 + jl + 8:]


def acc(i):
    ac = g["accessors"][i]
    bv = g["bufferViews"][ac["bufferView"]]
    n, dt = NC[ac["type"]], np.dtype(CT[ac["componentType"]])
    off, stride = bv.get("byteOffset", 0) + ac.get("byteOffset", 0), bv.get("byteStride") or n * dt.itemsize
    arr = np.frombuffer(blob, dt, ac["count"] * n, off).reshape(ac["count"], n) if stride == n * dt.itemsize else \
        np.stack([np.frombuffer(blob, dt, n, off + k * stride) for k in range(ac["count"])])
    return arr.astype(np.float32) / np.iinfo(dt).max if ac.get("normalized") else arr


pr = g["meshes"][0]["primitives"][0]
at = pr["attributes"]
pos, nrm = acc(at["POSITION"]).astype(float), acc(at["NORMAL"]).astype(float)
jnt, wgt = acc(at["JOINTS_0"]).astype(int), acc(at["WEIGHTS_0"]).astype(float)
tri = acc(pr["indices"]).astype(int).reshape(-1, 3)
nb = len(g["skins"][0]["joints"])
T = json.loads(Path(a.transform).read_text())
s, R, t = T["scale"], np.array(T["rotation"]), np.array(T["translation"])

sp = np.load(a.splat)
op = 1 / (1 + np.exp(-sp["opacities"]))
keep = op > a.min_opacity
means = (sp["means"][keep] - t) @ R / s                       # back to the mesh's rest space (metres)
sc = np.sort(np.exp(sp["scales"][keep]), 1)[:, ::-1] / s
rad = a.disc * np.sqrt(sc[:, 0] * sc[:, 1])
n = len(means)

# Dense points on the scaffold, each remembering its triangle and place in it.
rng = np.random.default_rng(0)
pa, pb, pc = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
area = 0.5 * np.linalg.norm(np.cross(pb - pa, pc - pa), axis=1)
m = 1_500_000
cum = np.cumsum(area)
ti = np.searchsorted(cum, (np.arange(m) + rng.random(m)) / m * cum[-1])
u, w = rng.random(m), rng.random(m)
su = np.sqrt(u)
bary = np.stack([1 - su, su * (1 - w), su * w], 1)
surf = (pos[tri[ti]] * bary[:, :, None]).sum(1)
loose = np.zeros(m, bool)
if a.loose_bones:
    from transplant_hands import led_by
    loose = led_by(a.glb, a.loose_bones.split(","))[2][ti]
trees = [(np.flatnonzero(~loose), cKDTree(surf[~loose]))] + ([(np.flatnonzero(loose), cKDTree(surf[loose]))] if loose.any() else [])


def nearest(q, rgb):
    """Distance to and index of each Gaussian's scaffold point. Without a loose surface that is the nearest point. With one, a
    Gaussian nearer the loose surface takes it; one nearer the rest but standing more than --loose-sure off it, inside the loose
    surface's wider reach, is undecided, and goes with the decided Gaussians most like it in place and colour (a cuff's fringe
    stays with the sleeve, a fingertip beside the tunic with the hand)."""
    i0, t0 = trees[0]
    d0, j0 = t0.query(q, workers=-1)
    if len(trees) == 1:
        return d0, i0[j0]
    i1, t1 = trees[1]
    d1, j1 = t1.query(q, workers=-1)
    hand = d1 < d0
    open_ = ~hand & (d1 / a.loose_factor < d0) & (d0 > a.loose_sure)
    if open_.any():
        sure = ~open_ & (d1 < 0.2)
        feat = lambda m: np.hstack([q[m], rgb[m] * a.loose_colour])
        nn = cKDTree(feat(sure)).query(feat(open_), k=7, workers=-1)[1]
        hand[open_] = hand[sure][nn].mean(1) > 0.5
    return np.where(hand, d1, d0), np.where(hand, i1[j1], i0[j0])


all_means = (sp["means"] - t) @ R / s
d_all, j_all = nearest(all_means, np.clip(sp["rgb"], 0, 1))
d, j = d_all[keep], j_all[keep]
tj, bj = ti[j], bary[j]
nn = (nrm[tri[tj]] * bj[:, :, None]).sum(1)
nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9)
W = np.zeros((n, nb))
for k in range(3):
    for c in range(4):
        np.add.at(W, (np.arange(n), jnt[tri[tj, k], c]), wgt[tri[tj, k], c] * bj[:, k])
top = np.argsort(-W, 1)[:, :4]
tw = np.take_along_axis(W, top, 1)
tw = np.round(tw / tw.sum(1, keepdims=True) * 255).astype(int)
tw[:, 0] += 255 - tw.sum(1)

buf = np.zeros(n, dtype=[("p", "<f4", 3), ("r", "<f4"), ("n", "i1", 4), ("c", "u1", 4), ("b", "u1", 4), ("w", "u1", 4)])
buf["p"], buf["r"] = means, rad
buf["n"][:, :3] = np.round(nn * 127)
buf["n"][:, 3] = rng.integers(-127, 127, n)
buf["c"][:, :3] = np.round(np.clip(sp["rgb"][keep], 0, 1) * 255)
buf["c"][:, 3] = 124
buf["b"], buf["w"] = top, tw
Path(a.out).write_bytes(buf.tobytes())
if a.binding:
    full_b, full_w = np.zeros((keep.size, 4), np.int16), np.zeros((keep.size, 4), np.float32)
    full_b[keep], full_w[keep] = top, tw / 255.0
    # Gaussians under the opacity cut are bound the same way, so the whole splat can be posed.
    rest = ~keep
    if rest.any():
        mr = (sp["means"][rest] - t) @ R / s
        jr = j_all[rest]
        Wr = np.zeros((len(mr), nb))
        for k in range(3):
            for c in range(4):
                np.add.at(Wr, (np.arange(len(mr)), jnt[tri[ti[jr], k], c]), wgt[tri[ti[jr], k], c] * bary[jr][:, k])
        tr = np.argsort(-Wr, 1)[:, :4]
        wr = np.take_along_axis(Wr, tr, 1)
        full_b[rest], full_w[rest] = tr, wr / wr.sum(1, keepdims=True)
    np.savez_compressed(a.binding, bones=full_b, weights=full_w)
print(json.dumps({"gaussians": int(keep.size), "discs": n, "stand_off_mm": {"median": round(float(np.median(d) * 1000), 1),
      "p99": round(float(np.percentile(d, 99) * 1000), 1), "max": round(float(d.max() * 1000), 1)},
      "disc_radius_mm_median": round(float(np.median(rad) * 1000), 2), "bones": nb}))
