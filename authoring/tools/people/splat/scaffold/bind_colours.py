#!/usr/bin/env python3
"""Surface-bound splats on a rigged scaffold, coloured from a finished baked splat. No bake.

    bind_colours.py RIGGED.glb TRANSFORM.json SPLAT.npz OUT.bin [--count 400000] [--sigma-mm 3] [--reach-mm 30]
    bind_colours.py RIGGED.glb TRANSFORM.json SPLAT.npz OUT.bin --frames FIT_DATA [--views 0:72:1] [--sharp 8]

RIGGED.glb is one skinned mesh. TRANSFORM.json (from score_outline.py on the same file) places
it in the baked splat's frame. Points are scattered evenly by area on the mesh; each takes the
mesh's normal and skin weights there, and its colour from the baked Gaussians around it
(opacity times a falloff of --sigma-mm). A point with no baked Gaussian within --reach-mm stays
grey: the scaffold sticks out of the figure there. Nothing is kept of Gaussians standing off the
surface, so the outline is the scaffold's.

With --frames the colour comes from the bake's own pictures instead (FIT_DATA: cameras.npz and
images/NNN.png with the cut-out in alpha). Each point is projected into every view that sees it
(it faces the camera, nothing of the scaffold is in front, and it lands inside the cut-out) and
takes a blend weighted by how squarely the view faces it, raised to --sharp so the squarest view
dominates. A point no view sees stays grey. SPLAT.npz is not read in this mode. OUT.bin is the 32-byte-per-splat buffer the
tests/procedural-splats page draws (?base=NAME&splats=FILE); the count and the share left grey
are printed.
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
ap = argparse.ArgumentParser()
ap.add_argument("glb"); ap.add_argument("transform"); ap.add_argument("splat"); ap.add_argument("out")
ap.add_argument("--frames", default=""); ap.add_argument("--views", default="0:72:1"); ap.add_argument("--sharp", type=float, default=8)
ap.add_argument("--count", type=int, default=400000); ap.add_argument("--sigma-mm", type=float, default=3); ap.add_argument("--reach-mm", type=float, default=30)
a = ap.parse_args()
raw = Path(a.glb).read_bytes()
jl = struct.unpack("<I", raw[12:16])[0]
g = json.loads(raw[20:20 + jl])
blob = raw[20 + jl + 8:]


def acc(i):
    ac = g["accessors"][i]
    bv = g["bufferViews"][ac["bufferView"]]
    n, dt = NC[ac["type"]], np.dtype(CT[ac["componentType"]])
    off, stride = bv.get("byteOffset", 0) + ac.get("byteOffset", 0), bv.get("byteStride") or n * dt.itemsize
    if stride == n * dt.itemsize:
        arr = np.frombuffer(blob, dt, ac["count"] * n, off).reshape(ac["count"], n)
    else:
        arr = np.stack([np.frombuffer(blob, dt, n, off + k * stride) for k in range(ac["count"])])
    return arr.astype(np.float32) / np.iinfo(dt).max if ac.get("normalized") else arr


pr = g["meshes"][0]["primitives"][0]
at = pr["attributes"]
pos, nrm = acc(at["POSITION"]).astype(float), acc(at["NORMAL"]).astype(float)
jnt, wgt = acc(at["JOINTS_0"]).astype(int), acc(at["WEIGHTS_0"]).astype(float)
tri = acc(pr["indices"]).astype(int).reshape(-1, 3)
nb = len(g["skins"][0]["joints"])
rng = np.random.default_rng(0)
pa, pb, pc = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
area = 0.5 * np.linalg.norm(np.cross(pb - pa, pc - pa), axis=1)
n = a.count
# Stratified over the area: one point per equal slice of the running total, so the cover is even.
cum = np.cumsum(area)
t = np.searchsorted(cum, (np.arange(n) + rng.random(n)) / n * cum[-1])
u, w = rng.random(n), rng.random(n)
s = np.sqrt(u)
bary = np.stack([1 - s, s * (1 - w), s * w], 1)
p = (pos[tri[t]] * bary[:, :, None]).sum(1)
nn = (nrm[tri[t]] * bary[:, :, None]).sum(1)
nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9)
W = np.zeros((n, nb))
for k in range(3):
    for j in range(4):
        np.add.at(W, (np.arange(n), jnt[tri[t, k], j]), wgt[tri[t, k], j] * bary[:, k])
top = np.argsort(-W, 1)[:, :4]
tw = np.take_along_axis(W, top, 1)
tw = np.round(tw / tw.sum(1, keepdims=True) * 255).astype(int)
tw[:, 0] += 255 - tw.sum(1)

T = json.loads(Path(a.transform).read_text())
scale, rot, trans = T["scale"], np.array(T["rotation"]), np.array(T["translation"])
height_m = pos[:, 1].max() - pos[:, 1].min()
unit = scale                                      # fit-frame units per mesh unit (the rigged mesh is in metres)
pf = scale * p @ rot.T + trans
if a.frames:
    from PIL import Image
    fit = Path(a.frames)
    cams = np.load(fit / "cameras.npz")
    nf = nn @ rot.T
    b_, e_, st_ = (int(x) for x in a.views.split(":"))
    acc_c, acc_w = np.zeros((n, 3)), np.zeros(n)
    for i in range(b_, e_, st_):
        im = np.asarray(Image.open(fit / "images" / f"{i:03d}.png").convert("RGBA"))
        size = im.shape[0]
        vm, K = cams["viewmats"][i], cams["Ks"][i]
        c = pf @ vm[:3, :3].T + vm[:3, 3]
        z = c[:, 2]
        uv = np.stack([K[0, 0] * c[:, 0] / z + K[0, 2], K[1, 1] * c[:, 1] / z + K[1, 2]], 1)
        ij = np.round(uv).astype(int)
        inside = (z > 0) & (ij[:, 0] >= 0) & (ij[:, 0] < size) & (ij[:, 1] >= 0) & (ij[:, 1] < size)
        # Depth of the scaffold's nearest point per coarse pixel: anything clearly behind it is hidden.
        cell = 4
        gi = np.clip(ij // cell, 0, size // cell - 1)
        flat = gi[:, 1] * (size // cell) + gi[:, 0]
        zmin = np.full((size // cell) ** 2, np.inf)
        np.minimum.at(zmin, flat[inside], z[inside])
        seen = inside & (z <= zmin[flat] + 0.012 * unit)
        view_dir = -c / np.linalg.norm(c, axis=1, keepdims=True)
        facing = ((nf @ vm[:3, :3].T) * view_dir).sum(1)
        ok = seen & (facing > 0.15)
        px = im[ij[ok, 1], ij[ok, 0]]
        ok_idx = np.flatnonzero(ok)[px[:, 3] > 200]
        wv = facing[ok_idx] ** a.sharp
        acc_c[ok_idx] += px[px[:, 3] > 200, :3] / 255.0 * wv[:, None]
        acc_w[ok_idx] += wv
    far = acc_w <= 0
    col = acc_c / np.maximum(acc_w, 1e-12)[:, None]
    col[far] = 0.55
    d = np.zeros((n, 1))
else:
    sp = np.load(a.splat)
    op = 1 / (1 + np.exp(-sp["opacities"]))
    keep = op > 0.15
    means, rgb, op = sp["means"][keep], np.clip(sp["rgb"][keep], 0, 1), op[keep]
    d, j = cKDTree(means).query(pf, k=12, workers=-1)
    sig = a.sigma_mm / 1000 * unit
    wk = op[j] * np.exp(-0.5 * (d / sig) ** 2) + 1e-12
    col = (rgb[j] * wk[:, :, None]).sum(1) / wk.sum(1, keepdims=True)
    far = d[:, 0] > a.reach_mm / 1000 * unit
    col[far] = 0.55

buf = np.zeros(n, dtype=[("p", "<f4", 3), ("r", "<f4"), ("n", "i1", 4), ("c", "u1", 4), ("b", "u1", 4), ("w", "u1", 4)])
buf["p"] = p
buf["r"] = 2.0 * np.sqrt(area.sum() / (n * np.pi))
buf["n"][:, :3] = np.round(nn * 127)
buf["n"][:, 3] = rng.integers(-127, 127, n)
buf["c"][:, :3] = np.round(col * 255)
buf["c"][:, 3] = np.where(far, 0, 124)            # painted: keeps the bake's own shading
buf["b"], buf["w"] = top, tw
Path(a.out).write_bytes(buf.tobytes())
print(json.dumps({"splats": n, "grey_share": round(float(far.mean()), 4), "median_gap_mm": round(float(np.median(d[:, 0]) / unit * 1000), 2),
                  "radius_mm": round(float(buf["r"][0] * 1000), 2), "bones": nb, "mesh_height_m": round(float(height_m), 3)}))
