#!/usr/bin/env python3
"""Surface normals and de-lit albedo for a bound body, from the scaffold mesh it is bound to.

    bound_surface.py FINAL_DIR OUT.npz [--neighbours 8] [--materials 12]

FINAL_DIR is a character's 09-game/bound/final/ (character.json names the body .vsplat and the scaffold rig). Every bound
Gaussian lies within a few centimetres of the scaffold surface (bound_run.sh bound holds it there), so its normal is the
scaffold's: the mesh's own vertex normals near the Gaussian, inverse-distance weighted over --neighbours vertices (32), in the
.vsplat's bind space (the scaffold frame, metres, +Y up, facing +Z). The free bake's splat_surface_normals.py has to depth-render
a loose fit from 64 directions to find an outward side; the scaffold already knows it.

Albedo: the baked lighting is divided out as splat_surface_normals.py does it (fit_shading: log luminance = level per material
+ b . normal, materials from chromaticity clusters split by dominant bone, trimmed least squares), without the occlusion term,
since the scaffold gives no visibility. OUT.npz holds normal (float16, unit), albedo (uint8 sRGB), shading (float16), one row
per .vsplat row - the --surface input of vsplat_to_glb.py and bound_to_glb.py (Patch 2026-11-19: lit splat characters).
"""
import argparse, json, sys
from pathlib import Path
import numpy as np
from scipy.cluster.vq import kmeans2
from scipy.spatial import cKDTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent / 'gsplat'))
from retarget_clip import read_glb                                              # noqa: E402
import vsplat_to_glb as V                                                       # noqa: E402
from splat_surface_normals import fit_shading, srgb_to_linear, linear_to_srgb  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('final', type=Path); ap.add_argument('out', type=Path)
    ap.add_argument('--neighbours', type=int, default=32, help='mesh vertices averaged (32: neighbour agreement 0.90 on 389 against 0.82 at 8; the small folds of a Meshy mesh otherwise speckle the lighting)'); ap.add_argument('--materials', type=int, default=12)
    ap.add_argument('--shade-range', type=float, nargs=2, default=(0.5, 1.6))
    a = ap.parse_args()
    man = json.loads((a.final / 'character.json').read_text())
    v = V.read_vsplat(str(a.final / man['body']))
    doc, acc = read_glb(a.final / man['rig'])
    prim = next(p for m in doc['meshes'] for p in m['primitives'] if 'NORMAL' in p['attributes'])
    vp = acc(prim['attributes']['POSITION']).astype(np.float64); vn = acc(prim['attributes']['NORMAL']).astype(np.float64)
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-9)
    d, i = cKDTree(vp).query(v['position'], k=a.neighbours)
    w = 1 / np.maximum(d, 1e-4); n = np.einsum('nk,nkj->nj', w, vn[i])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    out_side = float((np.einsum('nj,nj->n', n, v['position'] - vp[i[:, 0]]) >= -0.005).mean())   # share not behind their surface
    lin = srgb_to_linear(v['color'].astype(np.float64) / 255)
    alpha = 1 / (1 + np.exp(-v['logit']))
    chroma = lin / np.maximum(lin.sum(1, keepdims=True), 1e-6)
    _, cluster = kmeans2(chroma[:, :2], a.materials, seed=385, minit='++')
    bone = v['bones'][np.arange(len(v['bones'])), np.argmax(v['weights'], 1)]
    materials = np.unique(cluster * (bone.max() + 1) + bone, return_inverse=True)[1]
    shading, rep = fit_shading(lin, n, np.zeros(len(n)), materials, alpha >= 0.5, tuple(a.shade_range))
    albedo = np.rint(255 * linear_to_srgb(lin / shading[:, None])).astype(np.uint8)
    np.savez_compressed(a.out, normal=n.astype(np.float16), albedo=albedo, shading=shading.astype(np.float16))
    report = {'gaussians': int(v['n']), 'scaffold_vertices': len(vp), 'nearest_vertex_mm_p50_p99': (np.percentile(d[:, 0], [50, 99]) * 1000).round(1).tolist(),
              'not_behind_surface_share': round(out_side, 4), 'albedo': rep}
    a.out.with_suffix('.json').write_text(json.dumps(report, indent=1) + '\n'); print(json.dumps(report))


if __name__ == '__main__':
    main()
