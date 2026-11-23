#!/usr/bin/env python3
"""A bound splat character's matte inset core, from its Engine GLB and scaffold, with no hand-tuned clearances (experiment).

    core_extract.py ID.glb SCAFFOLD.glb OUT_DIR ID [--clips IDLE.glb,LOOK.glb] [--inset 0.024] [--clear 0.015] [--voxel 0.010]
                    [--tris 800] [--px 0.006]

(run with an Open3D python: static-npc's OPEN3D_PY, by default asset-pipeline/.runtime/tools/splat-mesh/bin/python.
Moved from asset-pipeline tools/splat-core on 2026-11-23, when Patch shipped npc-023 with a core.)

The 021 prototype (docs/splat-inset-core-occluder.md, live-evidence/town021-opacity-study-20261122-r01) eroded the scaffold by
24 mm and then carved its face, throat, hood peak, hem and staff by hand-placed bumps. This replaces the bumps with a test
against the character's own opaque splats, so it runs on any figure:

1. The scaffold's signed distance on a --voxel grid (Open3D), eroded --inset everywhere. That drops the thin parts: staff,
   fingers, spikes.
2. Clearance against the splats: the visible Gaussians (alpha > 0.08) are drawn as discs of their own size into orthographic
   front-depth maps from 96 directions (every 15 degrees round, at -15, 0, 15 and 45 degrees up). A core voxel is carved when
   it lies less than --clear behind the nearest splat over it (or in front of it, or outside the outline) in at least
   --carve-share of the views (default 0.15). A point outside the body fails from about half the directions; one behind a
   see-through hole in the bake fails only through the hole, and is kept to seal it. An earlier any-view rule carved the core
   away exactly there (017's robe, Patch 2026-11-22). Where the scaffold sits ahead of the splats (021's recessed face), the
   core is carved back behind them.
3. The largest piece, Surface Nets, quadric decimation to about --tris triangles, 3 Taubin passes.
4. Skin weights from the nearest opaque splats' own weights in the Engine GLB (the 22 Mixamo joints), hand joints left out,
   so the core moves as the body around it does.
5. Matte colour from the surface a viewer sees over each vertex: in views within 35 degrees of its normal, the nearest opaque
   splat at its pixel when that is at most 12 cm in front (2026-11-22: the prototype's 14 cm outward average turned dark next
   to dark cloth and hair, and showed through npc-023's pale chest). Vertices no view sees keep the outward average. Albedo
   is taken the same way from _ALBEDO.
6. With --clips (static_clips.py packs): the core and the splats are posed on every 10th frame and step 2's test is repeated
   from 24 views. The report gives how many core points come within 5 mm of the front splats, or out in front of them.

Writes, in OUT_DIR:
- ID.core.glb: the Engine GLB with the core as a second primitive of the body mesh. It is mode TRIANGLES, extras.splatCore
  true, on the same skin, with POSITION, NORMAL, COLOR_0 (linear float), _ALBEDO (sRGB u8), JOINTS_0, WEIGHTS_0 and indices.
  This is rusty-engine #10990's proposed contract; today's Engine refuses it, so it is a test file beside ID.glb, never in its
  place.
- ID-core.glb: the core alone, skinned to the scaffold's own joints, as the Workbench's opacity.js loads it (playable
  character.json "core"), with sRGB in COLOR_0 because that viewer draws COLOR_0 as it is.
- ID-core.json: the numbers, and the share of splats behind the core from the front, back and sides (the Early-Z saving).
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
import open3d as o3d
from scipy import ndimage
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation as Rot

PREFIX = 'mixamorig:'
HOLE = 0.06   # a margin below -HOLE: the core seen through a hole in the splats (sealing it), not a poke


def read_glb(path):
    d = Path(path).read_bytes(); n = struct.unpack_from('<I', d, 12)[0]
    g = json.loads(d[20:20 + n]); b = d[20 + n + 8:]
    W = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
    T = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}

    def acc(i):
        a = g['accessors'][i]; v = g['bufferViews'][a['bufferView']]; w = W[a['type']]; dt = np.dtype(T[a['componentType']])
        stride = v.get('byteStride') or dt.itemsize * w; off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        if stride == dt.itemsize * w:
            x = np.frombuffer(b, dt, a['count'] * w, off).reshape(a['count'], w)
        else:
            x = np.stack([np.frombuffer(b, dt, w, off + k * stride) for k in range(a['count'])])
        if a.get('normalized'):
            x = x.astype(np.float32) / np.iinfo(dt).max
        return x
    return g, b, acc


def splats_of(glb):
    g, _, acc = read_glb(glb)
    pr = next(p for m in g['meshes'] for p in m['primitives'] if p.get('mode') == 0)
    at = pr['attributes']
    s = {'P': acc(at['POSITION']).astype(np.float64), 'S': acc(at['_SCALE']).astype(np.float64),
         'C': acc(at['_COLOR']).astype(np.float64), 'J': acc(at['JOINTS_0']).astype(np.int64), 'W': acc(at['WEIGHTS_0']).astype(np.float64)}
    s['A'] = s['C'][:, 3]; s['rgb'] = s['C'][:, :3]
    s['alb'] = acc(at['_ALBEDO'])[:, :3].astype(np.float64) if '_ALBEDO' in at else s['rgb']
    joints = g['skins'][0]['joints']; s['names'] = [g['nodes'][j]['name'].removeprefix(PREFIX) for j in joints]
    return g, s


def mesh_of(glb):
    g, _, acc = read_glb(glb)
    pr = g['meshes'][0]['primitives'][0]
    return acc(pr['attributes']['POSITION']).astype(np.float64), acc(pr['indices']).reshape(-1, 3).astype(np.int64)


def scaffold_joints(glb):
    """The scaffold's skin joints as (name, world bind matrix, parent index or -1), in skin order: the joint indices the
    Workbench's .vsplat rig uses."""
    g, _, acc = read_glb(glb); sk = g['skins'][0]; at = {j: k for k, j in enumerate(sk['joints'])}
    par = {c: i for i, nd in enumerate(g['nodes']) for c in nd.get('children', [])}
    ibm = acc(sk['inverseBindMatrices']).astype(np.float64).reshape(-1, 4, 4).transpose(0, 2, 1)
    return [(g['nodes'][j]['name'].removeprefix(PREFIX), np.linalg.inv(m), at.get(par.get(j), -1)) for j, m in zip(sk['joints'], ibm)]


def views(azimuths, elevations):
    out = []
    for e in elevations:
        for az in azimuths:
            a, el = np.radians(az), np.radians(e)
            f = np.array([np.sin(a) * np.cos(el), np.sin(el), np.cos(a) * np.cos(el)])   # from the figure toward the camera
            r = np.cross([0.0, 1.0, 0.0], f); r /= np.linalg.norm(r); u = np.cross(f, r)
            out.append((f, r, u))
    return out


def front_depth(P, S, A, view, px, lo, hi):
    """The nearest visible splat's depth (toward the camera) per pixel: every splat above alpha 0.08, drawn as a disc of 1.5
    sigma, then pinholes of a pixel or two closed. Counting only opaque splats leaves holes where many faint ones make a surface
    (017's shoulder), and the far side then shows through the test."""
    f, r, u = view
    op = A > 0.08; p = P[op]; rad = 1.5 * S[op].max(1)
    t, x, y = p @ f, p @ r, p @ u
    W = int((hi[0] - lo[0]) / px) + 1; H = int((hi[1] - lo[1]) / px) + 1
    D = np.full((H, W), -np.inf)
    ix = np.clip(((x - lo[0]) / px).astype(int), 0, W - 1); iy = np.clip(((y - lo[1]) / px).astype(int), 0, H - 1)
    rc = np.clip(np.round(rad / px).astype(int), 0, 6)
    for k in range(7):
        m = rc == k
        if not m.any():
            continue
        L = np.full((H, W), -np.inf); np.maximum.at(L, (iy[m], ix[m]), t[m])
        if k:
            yy, xx = np.mgrid[-k:k + 1, -k:k + 1]
            L = ndimage.maximum_filter(L, footprint=(xx * xx + yy * yy) <= k * k, mode='constant', cval=-np.inf)
        D = np.maximum(D, L)
    return ndimage.maximum_filter(D, size=3, mode='constant', cval=-np.inf)


def opaque_depth(P, S, A, view, px, lo, hi, T_stop=0.05):
    """Per pixel, the depth (toward the camera) at which the splats over it become opaque: composited front to back, where the
    transmittance first falls below T_stop, else the deepest splat. Each splat is a disc of 2 sigma with Gaussian falloff. The
    core must lie behind this, not merely behind the first splat: the GPU draws a splat at its centre's depth, so a core in
    front of the deeper splats that build up a surface's opacity rejects them and shows through (npc-023's chest, Patch
    2026-11-22)."""
    f, r, u = view
    op = A > 0.02; p = P[op]; a = A[op]; sg = S[op].max(1)
    t, x, y = p @ f, p @ r, p @ u
    W = int((hi[0] - lo[0]) / px) + 1; H = int((hi[1] - lo[1]) / px) + 1
    ix = ((x - lo[0]) / px).astype(int); iy = ((y - lo[1]) / px).astype(int); rc = np.clip(np.ceil(2 * sg / px).astype(int), 0, 5)
    pid, tt, aa = [], [], []
    for k in range(6):
        m = np.flatnonzero(rc == k)
        if not len(m):
            continue
        for dy in range(-k, k + 1):
            for dx in range(-k, k + 1):
                d2 = (dx * dx + dy * dy) * px * px
                if dx * dx + dy * dy > k * k:
                    continue
                yy, xx = iy[m] + dy, ix[m] + dx; ok = (yy >= 0) & (xx >= 0) & (yy < H) & (xx < W)
                pid.append((yy * W + xx)[ok]); tt.append(t[m][ok]); aa.append((a[m] * np.exp(-0.5 * d2 / np.maximum(sg[m], 1e-4) ** 2))[ok])
    pid, tt, aa = (np.concatenate(v) for v in (pid, tt, aa))
    o = np.lexsort((-tt, pid)); pid, tt, aa = pid[o], tt[o], np.minimum(aa[o], 0.99)
    lt = np.log1p(-aa); cs = np.cumsum(lt)
    start = np.r_[0, np.flatnonzero(np.diff(pid)) + 1]; cs = cs - np.repeat(np.r_[0, cs[start[1:] - 1]], np.diff(np.r_[start, len(pid)]))
    done = cs < np.log(T_stop); idx = np.arange(len(pid))
    first = np.minimum.reduceat(np.where(done, idx, len(pid)), start)
    last = np.r_[start[1:] - 1, len(pid) - 1]
    pick = np.where(first < len(pid), first, last)
    D = np.full(H * W, -np.inf); D[pid[start]] = tt[pick]
    return D.reshape(H, W)


def clearance(P, S, A, pts, vs, px, clear, with_dir=False, opaque=False):
    """For each point: the smallest margin (m) by which it lies behind the nearest visible splat over it, over all views
    (-inf where some view sees no splat over it); with_dir, also the direction toward that worst view's camera.

    A small negative margin (a few cm) is a poke: the core just outside the splat surface. A large one (the nearest splat is on
    the far side) is the core seen through a hole in the splats, which is what the core is for (sealing see-through spots)."""
    worst = np.full(len(pts), np.inf); wdir = np.zeros((len(pts), 3))
    for v in vs:
        f, r, u = v
        q = P @ np.stack([r, u], 1); lo = np.floor(q.min(0) / px) * px - 0.05; hi = q.max(0) + 0.05   # the grid from the splats alone,
        D = (opaque_depth if opaque else front_depth)(P, S, A, v, px, lo, hi)                          # so every call sees the same maps
        ix = ((pts @ r - lo[0]) / px).astype(int); iy = ((pts @ u - lo[1]) / px).astype(int)
        inside = (ix >= 0) & (iy >= 0) & (ix < D.shape[1]) & (iy < D.shape[0])
        d = np.full(len(pts), -np.inf); d[inside] = D[iy[inside], ix[inside]]
        m = d - pts @ f; lower = m < worst; worst = np.where(lower, m, worst); wdir[lower] = f
    return (worst, wdir) if with_dir else worst


def front_colour(P, S, A, C, view, px):
    """What a viewer sees first per pixel: the colour and depth of the nearest splat above alpha 0.3, drawn as a disc of 1.5 sigma."""
    f, r, u = view
    op = A > 0.3; p = P[op]; c = C[op]; rad = 1.5 * S[op].max(1)
    t, x, y = p @ f, p @ r, p @ u
    q = P @ np.stack([r, u], 1); lo = np.floor(q.min(0) / px) * px - 0.05; hi = q.max(0) + 0.05
    W = int((hi[0] - lo[0]) / px) + 1; H = int((hi[1] - lo[1]) / px) + 1
    ix = ((x - lo[0]) / px).astype(int); iy = ((y - lo[1]) / px).astype(int); rc = np.clip(np.round(rad / px).astype(int), 0, 4)
    ii, jj, tt, cc = [], [], [], []
    for k in range(5):
        m = np.flatnonzero(rc == k)
        for dy in range(-k, k + 1):
            for dx in range(-k, k + 1):
                if dx * dx + dy * dy <= k * k:
                    ii.append(iy[m] + dy); jj.append(ix[m] + dx); tt.append(t[m]); cc.append(c[m])
    ii, jj, tt, cc = (np.concatenate(v) for v in (ii, jj, tt, cc))
    ok = (ii >= 0) & (jj >= 0) & (ii < H) & (jj < W); o = np.argsort(tt[ok])
    D = np.full((H, W), -np.inf); Cm = np.zeros((H, W, 3))
    D[ii[ok][o], jj[ok][o]] = tt[ok][o]; Cm[ii[ok][o], jj[ok][o]] = cc[ok][o]   # nearest written last
    return D, Cm, lo


def surface_colours(s, cv, cn, vs, px, cone=35.0, depth=0.12):
    """Each core vertex's colour (and albedo) from the surface a viewer sees over it: in every view within --cone degrees of its
    normal, the nearest opaque splat at its pixel, if that lies in front of it by no more than --depth (so not an arm passing in
    front of the body). Weighted by how square the view is to the normal. The old rule averaged every splat within 14 cm on
    the outward side, and next to dark cloth or hair that came out dark where the skin is pale (npc-023's chest, Patch 2026-11-22)."""
    col = np.zeros((len(cv), 3)); alb = np.zeros((len(cv), 3)); wsum = np.zeros(len(cv)); c0 = np.cos(np.radians(cone))
    for v in vs:
        f, r, u = v; w = cn @ f; m = w > c0
        if not m.any():
            continue
        D, Cm, lo = front_colour(s['P'], s['S'], s['A'], s['rgb'], v, px)
        _, Lm, _ = front_colour(s['P'], s['S'], s['A'], s['alb'], v, px)
        ix = ((cv @ r - lo[0]) / px).astype(int); iy = ((cv @ u - lo[1]) / px).astype(int)
        ok = m & (ix >= 0) & (iy >= 0) & (ix < D.shape[1]) & (iy < D.shape[0])
        d = np.full(len(cv), -np.inf); d[ok] = D[iy[ok], ix[ok]]; gap = d - cv @ f
        ok &= (gap >= 0) & (gap <= depth)
        col[ok] += w[ok, None] * Cm[iy[ok], ix[ok]]; alb[ok] += w[ok, None] * Lm[iy[ok], ix[ok]]; wsum[ok] += w[ok]
    have = wsum > 0
    col[have] /= wsum[have, None]; alb[have] /= wsum[have, None]
    return col, alb, have


def violations(P, S, A, pts, vs, px, thr, opaque=False):
    """For each point: the share of views in which it lies less than thr behind the nearest visible splat over it (or in front
    of it, or outside the outline); with opaque, behind the depth where the splats become opaque."""
    n = np.zeros(len(pts))
    for v in vs:
        n += clearance(P, S, A, pts, [v], px, thr, opaque=opaque) < thr
    return n / len(vs)


def surface_nets(phi, lo, voxel):
    phi = np.pad(phi, 1, mode='constant', constant_values=-1.0); lo = lo - voxel; inside = phi >= 0
    c = [inside[i:i + inside.shape[0] - 1, j:j + inside.shape[1] - 1, k:k + inside.shape[2] - 1] for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    cnt = sum(x.astype(int) for x in c); active = (cnt > 0) & (cnt < 8)
    cid = np.full(active.shape, -1, np.int64); ijk = np.argwhere(active); cid[active] = np.arange(len(ijk))
    ai, aj, ak = ijk.T; ps = np.zeros((len(ijk), 3)); pc = np.zeros(len(ijk))
    E = [((0, 0, 0), (1, 0, 0)), ((0, 1, 0), (1, 1, 0)), ((0, 0, 1), (1, 0, 1)), ((0, 1, 1), (1, 1, 1)), ((0, 0, 0), (0, 1, 0)), ((1, 0, 0), (1, 1, 0)),
         ((0, 0, 1), (0, 1, 1)), ((1, 0, 1), (1, 1, 1)), ((0, 0, 0), (0, 0, 1)), ((1, 0, 0), (1, 0, 1)), ((0, 1, 0), (0, 1, 1)), ((1, 1, 0), (1, 1, 1))]
    for e0, e1 in E:
        v0 = phi[ai + e0[0], aj + e0[1], ak + e0[2]]; v1 = phi[ai + e1[0], aj + e1[1], ak + e1[2]]
        x = (v0 >= 0) != (v1 >= 0); t = np.clip(np.where(x, v0 / np.where(x, v0 - v1, 1.0), 0.0), 0, 1)
        pos = np.column_stack([e0[d] + t * (e1[d] - e0[d]) for d in range(3)]); ps[x] += pos[x]; pc[x] += 1
    V = lo + (ijk + ps / np.maximum(pc[:, None], 1)) * voxel
    F = []
    # the four cells round each sign-changing edge, per axis (the 021 prototype's indexing, unchanged)
    a_, b_ = inside[:-1, 1:-1, 1:-1], inside[1:, 1:-1, 1:-1]
    for flip, m in ((False, a_ & ~b_), (True, ~a_ & b_)):
        ii, jj, kk = np.where(m); jj = jj + 1; kk = kk + 1
        q0, q1, q2, q3 = cid[ii, jj - 1, kk - 1], cid[ii, jj, kk - 1], cid[ii, jj, kk], cid[ii, jj - 1, kk]
        F.append(np.column_stack([q0, q2, q1] if flip else [q0, q1, q2])); F.append(np.column_stack([q0, q3, q2] if flip else [q0, q2, q3]))
    a_, b_ = inside[1:-1, :-1, 1:-1], inside[1:-1, 1:, 1:-1]
    for flip, m in ((False, a_ & ~b_), (True, ~a_ & b_)):
        ii, jj, kk = np.where(m); ii = ii + 1; kk = kk + 1
        q0, q1, q2, q3 = cid[ii - 1, jj, kk - 1], cid[ii - 1, jj, kk], cid[ii, jj, kk], cid[ii, jj, kk - 1]
        F.append(np.column_stack([q0, q2, q1] if flip else [q0, q1, q2])); F.append(np.column_stack([q0, q3, q2] if flip else [q0, q2, q3]))
    a_, b_ = inside[1:-1, 1:-1, :-1], inside[1:-1, 1:-1, 1:]
    for flip, m in ((False, a_ & ~b_), (True, ~a_ & b_)):
        ii, jj, kk = np.where(m); ii = ii + 1; jj = jj + 1
        q0, q1, q2, q3 = cid[ii - 1, jj - 1, kk], cid[ii, jj - 1, kk], cid[ii, jj, kk], cid[ii - 1, jj, kk]
        F.append(np.column_stack([q0, q2, q1] if flip else [q0, q1, q2])); F.append(np.column_stack([q0, q3, q2] if flip else [q0, q2, q3]))
    F = np.vstack(F)
    if (F < 0).any():
        raise SystemExit('core_extract: Surface Nets made a face on a cell with no vertex')
    v0, v1, v2 = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    if np.einsum('ij,ij->i', v0, np.cross(v1, v2)).sum() < 0:
        F = F[:, [0, 2, 1]]
    return V, F


def build(a):
    g, s = splats_of(a.glb)
    SV, SF = mesh_of(a.scaffold)
    m = o3d.t.geometry.TriangleMesh(); m.vertex.positions = o3d.core.Tensor(SV.astype(np.float32)); m.triangle.indices = o3d.core.Tensor(SF.astype(np.int32))
    scene = o3d.t.geometry.RaycastingScene(); scene.add_triangles(m)
    lo = SV.min(0) - 0.03; hi = SV.max(0) + 0.03; dims = np.ceil((hi - lo) / a.voxel).astype(int) + 1
    axes = [lo[d] + np.arange(dims[d]) * a.voxel for d in range(3)]
    G = np.stack(np.meshgrid(*axes, indexing='ij'), -1).reshape(-1, 3)
    sdf = scene.compute_signed_distance(o3d.core.Tensor(G.astype(np.float32))).numpy()
    phi = -sdf - a.inset
    cand = phi >= 0
    vs = views(range(0, 360, 15), (-15, 0, 15, 45))
    share = np.maximum(violations(s['P'], s['S'], s['A'], G[cand], vs, a.px, a.clear),
                       violations(s['P'], s['S'], s['A'], G[cand], vs, a.px, a.clear_opaque, opaque=True))
    phi_c = phi.copy(); idx = np.flatnonzero(cand)
    # carve to the clearance: phi is the smaller of the scaffold inset and the margin behind the splats
    phi_c[idx] = np.where(share >= a.carve_share, -0.01, phi[idx])   # carve by consensus: a point outside the body fails from many views,
                                                                    # one behind a see-through hole only through the hole (017's robe)
    phi_c = phi_c.reshape(dims)
    # the carved field is ragged (thin bridges and pinches where views disagree): open it by a voxel, keep the largest piece,
    # fill its holes, and take the surface of a lightly smoothed mask, so the mesh is one closed manifold that decimates
    mask = ndimage.binary_opening(phi_c >= 0, iterations=1)
    lbl, n = ndimage.label(mask)
    if n == 0:
        raise SystemExit('core_extract: nothing survives the clearance test (try a smaller --clear or --inset)')
    sizes = ndimage.sum(mask, lbl, range(1, n + 1)); keep = ndimage.binary_fill_holes(lbl == int(np.argmax(sizes)) + 1)
    keep = ndimage.binary_erosion(keep, iterations=1) | (keep & (phi_c >= a.voxel))   # the smoothed surface stays on kept voxels
    V, F = surface_nets(ndimage.gaussian_filter(keep.astype(float), 0.8) - 0.5, lo, a.voxel)
    mesh = o3d.geometry.TriangleMesh(o3d.utility.Vector3dVector(V), o3d.utility.Vector3iVector(F))
    mesh.remove_non_manifold_edges()
    mesh = mesh.simplify_quadric_decimation(target_number_of_triangles=a.tris)
    mesh.remove_unreferenced_vertices(); mesh.remove_degenerate_triangles()
    mesh = mesh.filter_smooth_taubin(number_of_iterations=3); mesh.compute_vertex_normals()
    cv = np.asarray(mesh.vertices).copy(); cn = np.asarray(mesh.vertex_normals); ct = np.asarray(mesh.triangles)
    # settle: a poke (a vertex within 8 mm of the splats over it, or up to HOLE in front of them, in some view: grazing views
    # along the back and sides, where the surface fit has slack) steps 5 mm inward along its normal, at most 6 cm in all
    # (stepping away from the failing view's camera instead made more pokes on 017). A vertex seen through a hole (the nearest
    # splat more than HOLE behind it) stays: the core is there to seal it.
    moved = 0; total = np.zeros(len(cv))
    for _ in range(12):
        bad = (np.maximum(violations(s['P'], s['S'], s['A'], cv, vs, a.px, 0.008),
                          violations(s['P'], s['S'], s['A'], cv, vs, a.px, 0.0, opaque=True)) >= a.carve_share) & (total < 0.06)
        if not bad.any():
            break
        cv[bad] -= 0.005 * cn[bad]; total[bad] += 0.005; moved += int(bad.sum())
    mesh.vertices = o3d.utility.Vector3dVector(cv); mesh.compute_vertex_normals(); cn = np.asarray(mesh.vertex_normals)
    # weights: the nearest opaque splats' own, hands left out
    op = s['A'] > 0.35; Po = s['P'][op]; Jo = s['J'][op]; Wo = s['W'][op]
    hands = [i for i, n_ in enumerate(s['names']) if n_ in ('LeftHand', 'RightHand')]
    _, nn = cKDTree(Po).query(cv, k=8)
    nj = len(s['names']); acc_w = np.zeros((len(cv), nj))
    for k in range(8):
        np.add.at(acc_w, (np.repeat(np.arange(len(cv)), 4), Jo[nn[:, k]].ravel()), Wo[nn[:, k]].ravel())
    acc_w[:, hands] = 0
    top = np.argsort(-acc_w, 1)[:, :4]; tw = np.take_along_axis(acc_w, top, 1)
    tw = np.where(tw.sum(1, keepdims=True) > 0, tw / np.maximum(tw.sum(1, keepdims=True), 1e-9), np.array([1.0, 0, 0, 0]))
    # colour: splats on the outward side (the prototype's rule)
    vis = s['A'] > 0.12; Pv, Av, Cv, Lv = s['P'][vis], s['A'][vis], s['rgb'][vis], s['alb'][vis]; tree = cKDTree(Pv)
    col = np.zeros((len(cv), 3)); alb = np.zeros((len(cv), 3))
    for i in range(len(cv)):
        ii = tree.query_ball_point(cv[i], r=0.14)
        if len(ii) < 8:
            ii = list(np.atleast_1d(tree.query(cv[i], k=24)[1]))
        dp = Pv[ii] - cv[i]; dist = np.linalg.norm(dp, axis=1); al = np.clip((dp @ cn[i]) / np.maximum(dist, 1e-4), 0, 1)
        w = Av[ii] * (al ** 2 + 0.05) / (dist + 0.02) ** 2
        col[i] = (Cv[ii] * w[:, None]).sum(0) / w.sum(); alb[i] = (Lv[ii] * w[:, None]).sum(0) / w.sum()
    sc_, sa_, have = surface_colours(s, cv, cn, vs, a.px)   # the visible surface over each vertex; the old average where none is seen
    col[have] = sc_[have]; alb[have] = sa_[have]; build.coloured_from_surface = round(float(have.mean()), 4)
    final = clearance(s['P'], s['S'], s['A'], cv, vs, a.px, a.clear)
    build.moved = moved
    return g, s, cv, cn, ct, top, tw, col, alb, final, int(cand.sum()), int(keep.sum())


def early_z(s, cv, ct, px=0.008):
    """The share of splats (and of their area) behind the core from the front, back and sides."""
    rng = np.random.default_rng(0); v0, v1, v2 = cv[ct[:, 0]], cv[ct[:, 1]], cv[ct[:, 2]]
    u = rng.random((len(ct), 24)); w = rng.random((len(ct), 24)); fl = u + w > 1; u[fl] = 1 - u[fl]; w[fl] = 1 - w[fl]
    pts = ((1 - u - w)[..., None] * v0[:, None] + u[..., None] * v1[:, None] + w[..., None] * v2[:, None]).reshape(-1, 3)
    area = np.sort(s['S'], 1)[:, 1] * np.sort(s['S'], 1)[:, 2]; out = {}
    for name, az in (('front', 0), ('back', 180), ('left', 90), ('right', 270)):
        f, r, u_ = views([az], [0])[0]
        allp = np.vstack([s['P'] @ np.stack([r, u_], 1), pts @ np.stack([r, u_], 1)]); lo = allp.min(0) - 0.02
        H, W = (((allp.max(0) - lo) / px).astype(int) + 2)[::-1]
        Z = np.full((H, W), -np.inf)
        np.maximum.at(Z, (((pts @ u_ - lo[1]) / px).astype(int), ((pts @ r - lo[0]) / px).astype(int)), pts @ f)
        iy = ((s['P'] @ u_ - lo[1]) / px).astype(int); ix = ((s['P'] @ r - lo[0]) / px).astype(int)
        behind = np.isfinite(Z[iy, ix]) & (s['P'] @ f < Z[iy, ix])
        out[name] = {'splat_pct': round(100 * float(behind.mean()), 1), 'area_pct': round(100 * float(area[behind].sum() / area.sum()), 1)}
    return out


def posed_check(s, g, cv, top, tw, clip_glbs, every=10):
    """Pose splats and core with each clip pack (static_clips.py: the GLB's joints, rotation channels, Hips translation)."""
    res = {}
    joints = g['skins'][0]['joints']; nodes = g['nodes']
    par = {c: i for i, nd in enumerate(nodes) for c in nd.get('children', [])}
    def trs(nd):
        M = np.eye(4); M[:3, :3] = Rot.from_quat(nd.get('rotation', [0, 0, 0, 1])).as_matrix(); M[:3, 3] = nd.get('translation', [0, 0, 0]); return M
    def world(i, local):
        return (world(par[i], local) if i in par else np.eye(4)) @ local.get(i, trs(nodes[i]))
    bind = np.array([world(j, {}) for j in joints])
    for cg in clip_glbs:
        cgd, _, cacc = read_glb(cg); an = cgd['animations'][0]; cn_ = {nd['name']: i for i, nd in enumerate(cgd['nodes'])}
        ch = {}
        for c in an['channels']:
            nm = cgd['nodes'][c['target']['node']]['name']; sm = an['samplers'][c['sampler']]
            ch[(nm, c['target']['path'])] = cacc(sm['output'])
        nf = len(cacc(an['samplers'][0]['input'])); worst = []; near = []; holes = []; cons = []
        for fr in range(0, nf, every):
            local = {}
            for j in joints:
                nm = nodes[j]['name']; M = trs(nodes[j])
                if (nm, 'rotation') in ch: M[:3, :3] = Rot.from_quat(ch[(nm, 'rotation')][fr]).as_matrix()
                if (nm, 'translation') in ch: M[:3, 3] = ch[(nm, 'translation')][fr]
                local[j] = M
            Wd = np.array([world(j, local) for j in joints]); K = Wd @ np.linalg.inv(bind)
            def skin(P, J, Wt):
                out = np.zeros_like(P)
                for k in range(4):
                    out += Wt[:, k:k + 1] * (np.einsum('nij,nj->ni', K[J[:, k], :3, :3], P) + K[J[:, k], :3, 3])
                return out
            Ps = skin(s['P'], s['J'], s['W'] / np.maximum(s['W'].sum(1, keepdims=True), 1e-9)); Cs = skin(cv, top, tw)
            mg = clearance(Ps, s['S'], s['A'], Cs, views(range(0, 360, 15), (0,)), 0.006, 0)
            worst.append(float(np.min(mg[mg > -HOLE]))); near.append(int(((mg < 0) & (mg > -HOLE)).sum())); holes.append(int((mg <= -HOLE).sum()))
            cons.append(int((violations(Ps, s['S'], s['A'], Cs, views(range(0, 360, 15), (0,)), 0.006, 0.0) >= 0.15).sum()))
        res[Path(cg).name] = {'frames_checked': len(worst), 'worst_poke_mm': round(1000 * min(worst), 1), 'poking_points_max': max(near),
                              'poke_points_consensus_max': max(cons),
                              'points_sealing_holes_max': max(holes)}
    return res


def write_core(path, cv, cn, ct, top, tw, col, alb, names, rig_nodes=None):
    """The core alone, skinned to the joints named in names (a list of (name, world bind matrix)) - the Workbench's file."""
    blob = bytearray(); views_, accs = [], []

    def add(arr, ct_, kind, target=None, bounds=False, norm=False):
        while len(blob) % 4: blob.append(0)
        raw = np.ascontiguousarray(arr).tobytes(); bv = {'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(raw)}
        if target: bv['target'] = target
        views_.append(bv); blob.extend(raw)
        a = {'bufferView': len(views_) - 1, 'componentType': ct_, 'count': len(arr), 'type': kind}
        if bounds: a['min'] = [float(x) for x in arr.min(0)]; a['max'] = [float(x) for x in arr.max(0)]
        if norm: a['normalized'] = True
        accs.append(a); return len(accs) - 1
    # sRGB in COLOR_0 here, not linear: the Workbench's opacity.js core shader writes COLOR_0 straight out as display colour
    # (the 021 prototype stored sRGB), and linear values there drew the core far too dark (npc-023's chest, Patch 2026-11-22).
    # ID.core.glb, the Engine's file, keeps COLOR_0 linear as glTF says.
    attrs = {'POSITION': add(cv.astype('<f4'), 5126, 'VEC3', 34962, True), 'NORMAL': add(cn.astype('<f4'), 5126, 'VEC3', 34962),
             'COLOR_0': add(np.column_stack([col, np.ones(len(cv))]).astype('<f4'), 5126, 'VEC4', 34962),
             'JOINTS_0': add(np.where(tw > 0, top, 0).astype('<u2'), 5123, 'VEC4', 34962), 'WEIGHTS_0': add(tw.astype('<f4'), 5126, 'VEC4', 34962)}
    idx = add(ct.astype('<u4').ravel(), 5125, 'SCALAR', 34963)
    nodes = [{'name': 'core', 'mesh': 0, 'skin': 0}]; jn = []
    for i, (nm, M, pi) in enumerate(names):   # the scaffold's own hierarchy, so the skin has one root
        L = M if pi < 0 else np.linalg.inv(names[pi][1]) @ M
        nodes.append({'name': nm, 'translation': L[:3, 3].tolist(), 'rotation': Rot.from_matrix(L[:3, :3]).as_quat().tolist()}); jn.append(i + 1)
    for i, (_, _, pi) in enumerate(names):
        if pi >= 0:
            nodes[pi + 1].setdefault('children', []).append(i + 1)
    roots = [0] + [i + 1 for i, (_, _, pi) in enumerate(names) if pi < 0]
    ibm = add(np.array([np.linalg.inv(M) for _, M, _ in names]).transpose(0, 2, 1).reshape(-1, 16).astype('<f4'), 5126, 'MAT4')
    while len(blob) % 4: blob.append(0)
    doc = {'asset': {'version': '2.0', 'generator': 'rusty-dagger core_extract.py'}, 'scene': 0, 'scenes': [{'nodes': roots}],
           'nodes': nodes, 'skins': [{'joints': jn, 'inverseBindMatrices': ibm}],
           'meshes': [{'name': 'core', 'primitives': [{'attributes': attrs, 'indices': idx, 'mode': 4}]}],
           'buffers': [{'byteLength': len(blob)}], 'bufferViews': views_, 'accessors': accs}
    pack(path, doc, blob)


def pack(path, doc, blob):
    text = json.dumps(doc, separators=(',', ':')).encode(); text += b' ' * ((-len(text)) % 4)
    while len(blob) % 4: blob.append(0)
    data = b'glTF' + struct.pack('<II', 2, 20 + len(text) + 8 + len(blob)) + struct.pack('<I', len(text)) + b'JSON' + text + struct.pack('<I', len(blob)) + b'BIN\0' + bytes(blob)
    tmp = Path(str(path) + '.part'); tmp.write_bytes(data); tmp.replace(path)


def tidy(top, tw):
    """Zero-weight slots name joint 0 (glTF: an unused slot), and u8 weights sum to exactly 255 (the rounding left on the largest)."""
    top = np.where(tw > 0, top, 0)
    w8 = np.clip(np.rint(tw * 255), 0, 255).astype(np.int64); w8[:, 0] += 255 - w8.sum(1)
    return top, w8.astype(np.uint8)


def with_core(glb, out, cv, cn, ct, top, tw, col, alb):
    """The Engine GLB with the core as a second primitive of its splat mesh (rusty-engine #10990's proposed contract)."""
    d = Path(glb).read_bytes(); n = struct.unpack_from('<I', d, 12)[0]; g = json.loads(d[20:20 + n]); blob = bytearray(d[20 + n + 8:])

    def add(arr, ct_, kind, target=None, bounds=False, norm=False):
        while len(blob) % 4: blob.append(0)
        raw = np.ascontiguousarray(arr).tobytes(); g['bufferViews'].append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(raw), **({'target': target} if target else {})})
        blob.extend(raw); a = {'bufferView': len(g['bufferViews']) - 1, 'componentType': ct_, 'count': len(arr), 'type': kind}
        if bounds: a['min'] = [float(x) for x in arr.min(0)]; a['max'] = [float(x) for x in arr.max(0)]
        if norm: a['normalized'] = True
        g['accessors'].append(a); return len(g['accessors']) - 1
    lin = np.where(col <= 0.04045, col / 12.92, ((col + 0.055) / 1.055) ** 2.4)
    prim = {'attributes': {'POSITION': add(cv.astype('<f4'), 5126, 'VEC3', 34962, True), 'NORMAL': add(cn.astype('<f4'), 5126, 'VEC3', 34962),
                           'COLOR_0': add(np.column_stack([lin, np.ones(len(cv))]).astype('<f4'), 5126, 'VEC4', 34962),
                           '_ALBEDO': add(np.column_stack([np.clip(np.rint(alb * 255), 0, 255), np.full(len(cv), 255)]).astype(np.uint8), 5121, 'VEC4', 34962, norm=True),
                           'JOINTS_0': add(tidy(top, tw)[0].astype(np.uint8), 5121, 'VEC4', 34962),
                           'WEIGHTS_0': add(tidy(top, tw)[1], 5121, 'VEC4', 34962, norm=True)},
            'indices': add(ct.astype('<u4').ravel(), 5125, 'SCALAR', 34963), 'mode': 4,
            'extras': {'splatCore': True, 'note': 'matte inset core (rusty-dagger core_extract.py); rusty-engine #10990 proposed contract, a test file'}}
    body = next(m for m in g['meshes'] if any(p.get('mode') == 0 for p in m['primitives']))
    body['primitives'].append(prim)
    while len(blob) % 4: blob.append(0)
    g['buffers'][0]['byteLength'] = len(blob)
    g['asset'].setdefault('extras', {})['core'] = {'vertices': len(cv), 'triangles': len(ct), 'contract': 'rusty-engine #10990 (proposed)'}
    pack(out, g, blob)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('glb'); ap.add_argument('scaffold'); ap.add_argument('out', type=Path); ap.add_argument('id')
    ap.add_argument('--clips', help='comma-separated clip packs (static_clips.py) to check the core under')
    ap.add_argument('--inset', type=float, default=0.024); ap.add_argument('--clear', type=float, default=0.015)
    ap.add_argument('--clear-opaque', type=float, default=0.005, help='and this far behind the depth where the splats become opaque')
    ap.add_argument('--carve-share', type=float, default=0.15, help='carve a point that is under --clear behind the splats in this share of views')
    ap.add_argument('--voxel', type=float, default=0.010); ap.add_argument('--tris', type=int, default=800); ap.add_argument('--px', type=float, default=0.006)
    a = ap.parse_args(); a.out.mkdir(parents=True, exist_ok=True)
    g, s, cv, cn, ct, top, tw, col, alb, final, ncand, nkeep = build(a)
    # the Workbench file: weights by joint name onto the scaffold's own joints
    sj = scaffold_joints(a.scaffold)
    lut = np.array([next((k for k, (nm, _, _) in enumerate(sj) if nm == n_), -1) for n_ in s['names']])
    wb_top = lut[top]; wb_tw = np.where(wb_top >= 0, tw, 0); wb_top = np.maximum(wb_top, 0)
    unmapped = float(tw[lut[top] < 0].sum() / max(tw.sum(), 1e-9))
    write_core(a.out / f'{a.id}-core.glb', cv, cn, ct, wb_top, wb_tw / np.maximum(wb_tw.sum(1, keepdims=True), 1e-9), col, alb, sj)
    with_core(a.glb, a.out / f'{a.id}.core.glb', cv, cn, ct, top, tw, col, alb)
    dom = top[:, 0]
    rep = {'id': a.id, 'glb': str(a.glb), 'scaffold': str(a.scaffold), 'inset_m': a.inset, 'clear_m': a.clear, 'voxel_m': a.voxel,
           'vertices': int(len(cv)), 'triangles': int(len(ct)), 'voxels_after_inset': ncand, 'voxels_kept': nkeep,
           'settle_steps': build.moved, 'coloured_from_surface': build.coloured_from_surface, 'rest_worst_poke_mm': round(1000 * float(np.min(final[final > -HOLE])), 1),
           'rest_poking_points': int(((final < 0) & (final > -HOLE)).sum()), 'rest_points_sealing_holes': int((final <= -HOLE).sum()),
           'rest_points_under_half_clear': int((final < a.clear * 0.5).sum()),
           'bbox': [cv.min(0).round(3).tolist(), cv.max(0).round(3).tolist()],
           'bones': {s['names'][i]: int((dom == i).sum()) for i in np.unique(dom)}, 'workbench_weight_unmapped': round(unmapped, 4),
           'rest_poke_points_consensus': int((violations(s['P'], s['S'], s['A'], cv, views(range(0, 360, 15), (-15, 0, 15, 45)), a.px, 0.0) >= a.carve_share).sum()),
           'carve_share': a.carve_share, 'early_z': early_z(s, cv, ct)}
    if a.clips:
        rep['posed'] = posed_check(s, g, cv, top, tw, [c for c in a.clips.split(',') if c])
    (a.out / f'{a.id}-core.json').write_text(json.dumps(rep, indent=1) + '\n')
    print(json.dumps(rep))


if __name__ == '__main__':
    main()
