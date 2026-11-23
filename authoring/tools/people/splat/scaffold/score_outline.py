#!/usr/bin/env python3
"""Score a scaffold mesh's outline against a baked character's cut-outs.

    score_outline.py MESH.glb FIT_DATA SPLAT.npz OUT_DIR [--views 0:72:3] [--size 544] [--no-icp] [--no-refine] [--yaw DEG]

FIT_DATA is a bake's fit folder (cameras.npz with viewmats and Ks, images/NNN.png with the
cut-out in alpha). SPLAT.npz is the finished splat in the same frame (means, scales, opacities).
The mesh is assumed upright (y up) and facing +z, like the fit frame, or turned --yaw degrees from it (a painting drawn at an
angle opens the orbit on that view: static_rig.py --view-yaw; 017). It is scaled to the
splat's height and centred, refined by a trimmed similarity ICP against the splat's centres,
and finished by fitting scale, shift and turn to the cut-outs themselves. For each view the filled mesh outline is compared with the cut-out:

  iou      overlap / union
  covered  share of the cut-out the mesh covers  (low: the figure sticks out of the scaffold)
  spill    share of the mesh outside the cut-out (high: the scaffold sticks out of the figure)

The finished splat's own centres, drawn as discs, are scored the same way as the reference.
Writes scores.json, overlay.png (cut-out red, mesh green, both yellow) and transform.json.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.spatial import cKDTree

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def load_glb(path):
    """All triangles of a GLB in world space (node transforms applied, skins ignored)."""
    raw = Path(path).read_bytes()
    jl = struct.unpack("<I", raw[12:16])[0]
    g = json.loads(raw[20:20 + jl])
    blob = raw[20 + jl + 8:]

    def acc(i):
        a = g["accessors"][i]
        bv = g["bufferViews"][a["bufferView"]]
        n, dt = NC[a["type"]], np.dtype(CT[a["componentType"]])
        off, stride = bv.get("byteOffset", 0) + a.get("byteOffset", 0), bv.get("byteStride") or n * dt.itemsize
        if stride == n * dt.itemsize:
            return np.frombuffer(blob, dt, a["count"] * n, off).reshape(a["count"], n)
        return np.stack([np.frombuffer(blob, dt, n, off + k * stride) for k in range(a["count"])])

    def local(nd):
        if "matrix" in nd:
            return np.array(nd["matrix"], float).reshape(4, 4).T
        m = np.eye(4)
        x, y, z, w = nd.get("rotation", [0, 0, 0, 1])
        m[:3, :3] = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                              [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                              [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]) * np.array(nd.get("scale", [1, 1, 1]))
        m[:3, 3] = nd.get("translation", [0, 0, 0])
        return m

    verts, faces, base = [], [], 0

    def walk(i, parent):
        nd = g["nodes"][i]
        m = parent @ local(nd)
        nonlocal base
        if "mesh" in nd:
            skinned = "skin" in nd
            for pr in g["meshes"][nd["mesh"]]["primitives"]:
                p = acc(pr["attributes"]["POSITION"]).astype(float)
                if not skinned:
                    p = p @ m[:3, :3].T + m[:3, 3]
                idx = acc(pr["indices"]).astype(np.int64).ravel() if "indices" in pr else np.arange(len(p))
                verts.append(p)
                faces.append(idx.reshape(-1, 3) + base)
                base += len(p)
        for c in nd.get("children", []):
            walk(c, m)

    for r in g["scenes"][g.get("scene", 0)]["nodes"]:
        walk(r, np.eye(4))
    return np.concatenate(verts), np.concatenate(faces)


def surface_points(v, f, n, rng):
    a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    t = rng.choice(len(f), n, p=area / area.sum())
    u, w = rng.random(n), rng.random(n)
    s = np.sqrt(u)
    return a[t] * (1 - s)[:, None] + b[t] * (s * (1 - w))[:, None] + c[t] * (s * w)[:, None]


def umeyama(src, dst):
    ms, md = src.mean(0), dst.mean(0)
    x, y = src - ms, dst - md
    u, d, vt = np.linalg.svd(y.T @ x / len(src))
    s = np.eye(3)
    if np.linalg.det(u) * np.linalg.det(vt) < 0:
        s[2, 2] = -1
    r = u @ s @ vt
    scale = np.trace(np.diag(d) @ s) / (x ** 2).sum(1).mean()
    return scale, r, md - scale * r @ ms


def project(p, vm, k):
    c = p @ vm[:3, :3].T + vm[:3, 3]
    z = np.maximum(c[:, 2], 1e-6)
    return np.stack([k[0, 0] * c[:, 0] / z + k[0, 2], k[1, 1] * c[:, 1] / z + k[1, 2]], 1), c[:, 2]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mesh"); ap.add_argument("fit", type=Path); ap.add_argument("splat"); ap.add_argument("out", type=Path)
    ap.add_argument("--views", default="0:72:3"); ap.add_argument("--size", type=int, default=544); ap.add_argument("--no-icp", action="store_true"); ap.add_argument("--no-refine", action="store_true")
    ap.add_argument("--yaw", type=float, default=0.0, help="start the fit with the mesh turned this many degrees about y")
    a = ap.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(0)
    v, f = load_glb(a.mesh)
    sp = np.load(a.splat)
    op = sp["opacities"]
    op = 1 / (1 + np.exp(-op)) if op.min() < 0 or op.max() > 1 else op
    means = sp["means"][op > 0.3]
    lo, hi = np.percentile(means, 0.5, 0), np.percentile(means, 99.5, 0)
    # Height and centre first, then a trimmed ICP with scale.
    s0 = (hi[1] - lo[1]) / (v[:, 1].max() - v[:, 1].min())
    t0 = (lo + hi) / 2 - s0 * (v.min(0) + v.max(0)) / 2
    c0, s1 = np.cos(np.radians(a.yaw)), np.sin(np.radians(a.yaw))
    rot = np.array([[c0, 0, s1], [0, 1, 0], [-s1, 0, c0]])
    scale, trans = s0, t0
    if not a.no_icp:
        src0 = surface_points(v, f, 40000, rng)
        tree = cKDTree(means)
        for _ in range(40):
            src = scale * src0 @ rot.T + trans
            d, j = tree.query(src)
            keep = d < np.percentile(d, 85)
            scale, rot, trans = umeyama(src0[keep], means[j[keep]])
    cams = np.load(a.fit / "cameras.npz")
    b, e, st = (int(x) for x in a.views.split(":"))
    ids, size = range(b, e, st), a.size
    icp = {"scale": float(scale), "translation": trans.tolist()}
    if not a.no_refine:
        # ICP pulls a surface towards splat centres that sit just inside it, so finish on the outlines
        # themselves: scale, shift and turn about the vertical to the best mean overlap, using a
        # fast dotted outline at half size.
        from scipy import ndimage, optimize
        half = size // 2
        pts = surface_points(v, f, 60000, rng)
        mattes = {}
        for i in ids:
            al = Image.open(a.fit / "images" / f"{i:03d}.png").getchannel("A")
            mattes[i] = (np.asarray(al.resize((half, half), Image.BOX)) > 127, al.size[0])

        def place(x, p):
            c, s_ = np.cos(x[4]), np.sin(x[4])
            ry = np.array([[c, 0, s_], [0, 1, 0], [-s_, 0, c]])
            return (scale * x[0]) * p @ (ry @ rot).T + trans + x[1:4]

        def loss(x):
            w, tot = place(x, pts), 0.0
            for i in ids:
                m, full = mattes[i]
                k = cams["Ks"][i] * (half / full)
                uv, z = project(w, cams["viewmats"][i], k)
                ij = np.round(uv).astype(int)
                ok = (ij[:, 0] >= 0) & (ij[:, 0] < half) & (ij[:, 1] >= 0) & (ij[:, 1] < half) & (z > 0)
                g = np.zeros((half, half), bool)
                g[ij[ok, 1], ij[ok, 0]] = True
                g = ndimage.binary_erosion(ndimage.binary_fill_holes(ndimage.binary_dilation(g, iterations=2)))
                tot += (g & m).sum() / max(1, (g | m).sum())
            return -tot / len(ids)

        res = optimize.minimize(loss, [1, 0, 0, 0, 0], method="Powell", options={"xtol": 2e-3, "ftol": 2e-4, "maxfev": 160})
        x = res.x
        c, s_ = np.cos(x[4]), np.sin(x[4])
        rot = np.array([[c, 0, s_], [0, 1, 0], [-s_, 0, c]]) @ rot
        scale, trans = scale * x[0], trans + x[1:4]
    vw = scale * v @ rot.T + trans
    yaw = float(np.degrees(np.arctan2(rot[0, 2], rot[2, 2])))
    (a.out / "transform.json").write_text(json.dumps({"scale": float(scale), "rotation": rot.tolist(), "translation": np.asarray(trans).tolist(),
                                                       "start_scale": s0, "yaw_deg": yaw, "after_icp": icp}, indent=1))
    rows, tiles = [], []
    sc = np.exp(sp["scales"][op > 0.3]).max(1) if sp["scales"].min() < 0 else sp["scales"][op > 0.3].max(1)
    for i in ids:
        matte = Image.open(a.fit / "images" / f"{i:03d}.png").getchannel("A")
        full = matte.size[0]
        m = np.asarray(matte.resize((size, size), Image.BOX)) > 127
        k = cams["Ks"][i] * (size / full); k[2, 2] = 1
        uv, z = project(vw, cams["viewmats"][i], k)
        im = Image.new("L", (size, size), 0)
        d = ImageDraw.Draw(im)
        tri = uv[f]
        ok = (z[f] > 0).all(1)
        for t in tri[ok]:
            d.polygon([tuple(t[0]), tuple(t[1]), tuple(t[2])], fill=255)
        g = np.asarray(im) > 0
        # Reference: the finished splat's centres as discs of their own size.
        uvs, zs = project(means, cams["viewmats"][i], k)
        ref = Image.new("L", (size, size), 0)
        dr = ImageDraw.Draw(ref)
        rad = np.maximum(0.7, sc * k[0, 0] / np.maximum(zs, 1e-6))
        for (x, y), r in zip(uvs[::4], rad[::4]):
            dr.ellipse([x - r, y - r, x + r, y + r], fill=255)
        rf = np.asarray(ref) > 0
        row = {"view": i}
        for name, s_ in (("mesh", g), ("splat", rf)):
            inter = (s_ & m).sum()
            row[name] = {"iou": round(float(inter / max(1, (s_ | m).sum())), 4), "covered": round(float(inter / max(1, m.sum())), 4),
                         "spill": round(float((s_ & ~m).sum() / max(1, s_.sum())), 4)}
        rows.append(row)
        ov = np.zeros((size, size, 3), np.uint8)
        ov[..., 0] = m * 220; ov[..., 1] = g * 200; ov[..., 2] = 30
        tiles.append(Image.fromarray(ov))
    mean = {n: {k2: round(float(np.mean([r[n][k2] for r in rows])), 4) for k2 in ("iou", "covered", "spill")} for n in ("mesh", "splat")}
    worst = min(rows, key=lambda r: r["mesh"]["iou"])
    (a.out / "scores.json").write_text(json.dumps({"mesh": str(a.mesh), "faces": len(f), "views": list(ids), "mean": mean,
                                                    "worst_view": worst, "per_view": rows, "yaw_deg": yaw, "scale": float(scale)}, indent=1))
    cols = 8
    sheet = Image.new("RGB", (cols * size // 2, -(-len(tiles) // cols) * size // 2))
    for n, t in enumerate(tiles):
        sheet.paste(t.resize((size // 2, size // 2)), ((n % cols) * size // 2, (n // cols) * size // 2))
    sheet.save(a.out / "overlay.png")
    print(json.dumps({"faces": len(f), "mean": mean, "worst_view": worst["view"], "worst_iou": worst["mesh"]["iou"], "yaw_deg": round(yaw, 1), "scale": round(float(scale), 4)}))


if __name__ == "__main__":
    sys.exit(main())
