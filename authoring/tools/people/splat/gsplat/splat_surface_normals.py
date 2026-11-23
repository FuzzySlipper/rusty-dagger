#!/usr/bin/env python3
"""Surface normals (and a de-lit albedo) for a rigged splat character, from the static fit it was distilled from.

  python3 -I splat_surface_normals.py SURFACE.npz DISTIL.npz RIG.npz OUT.npz [--views 64] [--png SHEET.png]

SURFACE.npz is the large static fit (townsman 385: the 400k final), DISTIL.npz
the distil in the same frame (fit48/splat.npz), RIG.npz humanoid_splat_rig.py's
'-rig.npz' for that distil (its rows are the .vsplat's rows, in order).
OUT.npz holds, per .vsplat row:

  normal   float16 (n, 3)  unit, in the .vsplat's bind space (SDK frame, faces +X), out of the figure
  albedo   uint8   (n, 3)  sRGB colour with the fitted baked shading divided out
  shading  float16 (n,)    that shading factor (linear light; albedo = colour / shading)
  seen     float16 (n,)    share of the views that see the Gaussian on the surface

vsplat_to_glb.py --surface OUT.npz writes them as _NORMAL and _ALBEDO.

Normals. The fit is not a clean shell (it has Gaussians inside the figure as
well as on it, and gaps), so neither a voxel hull nor a local plane fit gives a
reliable outward side. Instead the fit's opaque Gaussians (alpha >= --min-alpha)
are depth-buffered from --views directions spread over the sphere (orthographic,
--pixel cells): each view's frontmost depth is hole-filled and smoothed
(--depth-smooth pixels), and its gradient gives that view's surface normal,
which by construction points toward the viewer, out of the figure. A distil
Gaussian seen by a view (within --tolerance of its frontmost depth) takes that
view's normal at its pixel; the views that see it are averaged, weighted by how
squarely they see the surface. Gaussians no view sees (inside the figure, in a
deep fold) take the mean normal of their nearest seen neighbours. The normal is
then turned into bind space by the rig's own per-Gaussian rotation (the one
humanoid_splat_rig.py applied to the Gaussian's quaternion: rig quat * distil
quat^-1), so the straightened legs and re-posed arms carry their normals.

Albedo. The bake's colours carry the lighting of the frames it was fitted to,
fixed in the figure's frame. Shading is fitted as a function of the
source-frame normal plus occlusion (1 - seen), shared by the whole figure, with
a free level per material (k-means on chromaticity x dominant bone), on log
luminance: log Y = level[material] + b . n + c (1 - seen). The fitted shading
(normalised to a luminance-weighted mean of 1, clamped to --shade-range) is
divided out of the linear colour. The report states how much of the luminance
within materials it explains: if that is small, the bake holds little lighting
to remove and the albedo is close to the baked colour.
"""
import argparse
import json

import numpy as np
from scipy import ndimage
from scipy.cluster.vq import kmeans2
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation as Rot


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def sphere_directions(count):
    """Fibonacci directions over the whole sphere."""
    i = np.arange(count) + 0.5
    y = 1 - 2 * i / count; r = np.sqrt(1 - y * y); phi = np.pi * (3 - np.sqrt(5)) * i
    return np.column_stack([r * np.cos(phi), y, r * np.sin(phi)])


def view_basis(v):
    up = np.array([0.0, 1.0, 0.0]) if abs(v[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
    right = np.cross(up, v); right /= np.linalg.norm(right)
    return right, np.cross(v, right)


def depth_view(surface_pts, v, pixel, smooth):
    """Frontmost depth of the fit seen from direction v (the viewer sits at +v), hole-filled and smoothed, and its
    per-pixel outward normal. Returns a projector p -> (pixel indices, depth) and the maps."""
    right, up = view_basis(v)
    x = surface_pts @ right; y = surface_pts @ up; d = surface_pts @ v
    lo = np.array([x.min(), y.min()]) - 4 * pixel
    ix = ((x - lo[0]) / pixel).astype(int); iy = ((y - lo[1]) / pixel).astype(int)
    W, H = ix.max() + 5, iy.max() + 5
    depth = np.full((H, W), -np.inf)
    np.maximum.at(depth, (iy, ix), d)
    covered = np.isfinite(depth)
    # Fill pinholes inside the silhouette from the nearest covered pixel; the silhouette itself is closed by 2 px.
    silhouette = ndimage.binary_fill_holes(ndimage.binary_closing(covered, iterations=2))
    _, (ny, nx) = ndimage.distance_transform_edt(~covered, return_indices=True)
    filled = np.where(covered, depth, depth[ny, nx])
    # Smooth inside the silhouette only (normalised convolution), then differentiate.
    m = silhouette.astype(np.float64)
    z = np.where(silhouette, filled, 0.0)
    zs = ndimage.gaussian_filter(z, smooth) / np.maximum(ndimage.gaussian_filter(m, smooth), 1e-6)
    gy, gx = np.gradient(zs, pixel)
    normal = -gx[..., None] * right - gy[..., None] * up + v
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)

    def project(p):
        jx = np.clip(((p @ right - lo[0]) / pixel).astype(int), 0, W - 1)
        jy = np.clip(((p @ up - lo[1]) / pixel).astype(int), 0, H - 1)
        return jy, jx, p @ v
    return project, filled, silhouette, normal


def surface_normals(surface_pts, points, views, pixel, smooth, tolerance, neighbours):
    """Outward normal per point from the depth views that see it; (normals, seen share, seen by any).

    Each seeing view's normal is weighted by how squarely it sees the surface and by how close the point is to the
    frontmost depth (exp(-gap / tolerance)). Views from the far side of thin cloth would cancel the near side's, so
    the views that disagree with the weighted mean (dot < 0) are dropped and the mean taken again."""
    per = np.zeros((views, len(points), 3), np.float32); wts = np.zeros((views, len(points)), np.float32)
    for k, v in enumerate(sphere_directions(views)):
        project, depth, silhouette, normal = depth_view(surface_pts, v, pixel, smooth)
        jy, jx, d = project(points)
        gap = np.maximum(depth[jy, jx] - d, 0)
        seen = silhouette[jy, jx] & (gap <= 2 * tolerance)
        n = normal[jy[seen], jx[seen]]
        per[k, seen] = n
        wts[k, seen] = np.clip(n @ v, 0.05, 1) * np.exp(-gap[seen] / tolerance)
    hits = (wts > 0).sum(0)
    mean = (wts[..., None] * per).sum(0)
    agree = (per * mean[None]).sum(2) > 0
    out = ((wts * agree)[..., None] * per).sum(0)
    seen_any = np.linalg.norm(out, axis=1) > 1e-9
    out[seen_any] /= np.linalg.norm(out[seen_any], axis=1, keepdims=True)
    if (~seen_any).any():
        _, nb = cKDTree(points[seen_any]).query(points[~seen_any], k=neighbours)
        m = out[seen_any][nb].mean(1)
        out[~seen_any] = m / np.maximum(np.linalg.norm(m, axis=1, keepdims=True), 1e-12)
    return out.astype(np.float64), hits / views, seen_any


def fit_shading(rgb_lin, normals, occ, materials, fit_rows, shade_range):
    """Fit log Y = level[material] + b . n + c occ on fit_rows; returns (shading for every row, report).

    Occlusion can only darken: if its fitted gain comes out positive (more enclosed, brighter), it is a confound
    (material, interior Gaussians), and the fit is redone without it."""
    Y = rgb_lin @ np.array([0.2126, 0.7152, 0.0722])
    use = fit_rows & (Y > 1e-3)
    logY = np.log(np.maximum(Y, 1e-3))
    m = materials.max() + 1

    def solve(with_occ):
        X = np.zeros((len(Y), m + 4))
        X[np.arange(len(Y)), materials] = 1
        X[:, m:m + 3] = normals
        if with_occ:
            X[:, m + 3] = occ
        keep = use.copy()
        for _ in range(3):   # trimmed least squares: refit without the worst 10 % (highlights, stray floaters)
            beta, *_ = np.linalg.lstsq(X[keep], logY[keep], rcond=None)
            r = np.abs(logY - X @ beta)
            keep = use & (r <= np.percentile(r[use], 90))
        return X, beta
    X, beta = solve(True)
    occlusion_used = beta[m + 3] < 0
    if not occlusion_used:
        X, beta = solve(False)
    b = beta[m:m + 3]; c = beta[m + 3] if occlusion_used else 0.0
    log_shade = normals @ b + c * occ
    log_shade -= np.average(log_shade[use], weights=Y[use])
    shading = np.clip(np.exp(log_shade), *shade_range)
    within = logY - X[:, :m] @ beta[:m]            # what is left after the material levels
    explained = 1 - np.var((within - X[:, m:] @ beta[m:])[use]) / np.var(within[use])
    report = {'fitted_on': int(use.sum()),
              'light_direction_source_frame': (b / max(np.linalg.norm(b), 1e-12)).round(4).tolist(),
              'directional_log_gain': round(float(np.linalg.norm(b)), 4),
              'lit_to_unlit_side_ratio': round(float(np.exp(2 * np.linalg.norm(b))), 3),
              'occlusion_log_gain': round(float(c), 4), 'occlusion_used': bool(occlusion_used),
              'materials': int(m),
              'within_material_luminance_variance_explained': round(float(explained), 4),
              'shading_percentiles_1_50_99': np.percentile(shading, [1, 50, 99]).round(3).tolist()}
    return shading, report


def fallback_normals(G):
    """The Engine's admission fallback, approximately: the shortest axis, signed away from the heaviest joint (here
    stood in for by the centroid of the Gaussians that joint carries most), in bind space."""
    n = len(G['rest_means']); rest = G['rest_means']
    R = Rot.from_quat(G['quats'][:, [1, 2, 3, 0]]).as_matrix()
    f = R[np.arange(n), :, np.argmin(G['scales'], 1)]
    heavy = G['bones'][np.arange(n), np.argmax(G['weights'], 1)]
    centre = {b: rest[heavy == b].mean(0) for b in np.unique(heavy)}
    away = rest - np.array([centre[b] for b in heavy])
    return f * np.where((f * away).sum(1, keepdims=True) < 0, -1, 1)


def frontmost(points, keep, v, pixel, radius=2):
    """Index of the frontmost kept point per cell seen from +v, each point covering the (2 radius + 1)^2 cells around
    its own so a sparse cloud still hides its far side (-1 where empty)."""
    right, up = view_basis(np.asarray(v, np.float64)); p = points[keep]
    ix = ((p @ right - (p @ right).min()) / pixel).astype(int) + radius
    iy = ((p @ up - (p @ up).min()) / pixel).astype(int) + radius
    d = p @ v
    shape = (iy.max() + radius + 1, ix.max() + radius + 1)
    depth = np.full(shape, -np.inf); front = np.full(shape, -1)
    offsets = [(oy, ox) for oy in range(-radius, radius + 1) for ox in range(-radius, radius + 1)]
    for oy, ox in offsets:
        np.maximum.at(depth, (iy + oy, ix + ox), d)
    for oy, ox in offsets:
        hit = depth[iy + oy, ix + ox] == d
        front[iy[hit] + oy, ix[hit] + ox] = keep[hit]
    return front


AXIS_VIEWS = (('front', [1, 0, 0]), ('his left', [0, 0, -1]), ('back', [-1, 0, 0]), ('his right', [0, 0, 1]),
              ('top', [0, 1, 0]))


def facing_check(surface_pts, points, normals, pixel, smooth, tolerance):
    """Per axis view of the source fit: among the points that view's depth map sees, the share whose normal faces the
    viewer (n . v > 0), the share clearly facing away (n . v < -0.3), and the median n . v. Normals in the fit frame."""
    out = {}
    for name, v in (('+X', [1, 0, 0]), ('-X', [-1, 0, 0]), ('+Y top', [0, 1, 0]), ('-Y below', [0, -1, 0]),
                    ('+Z', [0, 0, 1]), ('-Z', [0, 0, -1])):
        v = np.array(v, np.float64)
        project, depth, silhouette, _ = depth_view(surface_pts, v, pixel, smooth)
        jy, jx, d = project(points)
        seen = silhouette[jy, jx] & (depth[jy, jx] - d <= tolerance)
        out[name] = {k: {'facing': round(float((n[seen] @ v > 0).mean()), 4), 'away': round(float((n[seen] @ v < -0.3).mean()), 4),
                         'median_cos': round(float(np.median(n[seen] @ v)), 3)} for k, n in normals.items()}
    return out


def sheet(path, points, alpha, panels, pixel):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    views = AXIS_VIEWS[:4]
    fig, axes = plt.subplots(len(panels), len(views), figsize=(4 * len(views), 6.5 * len(panels)), dpi=120)
    keep = np.where(alpha > 0.3)[0]
    for c, (vname, v) in enumerate(views):
        front = frontmost(points, keep, v, pixel)
        for r, (title, colour) in enumerate(panels):
            img = np.ones(front.shape + (3,)); img[front >= 0] = np.clip(colour[front[front >= 0]], 0, 1)
            ax = axes[r, c]; ax.imshow(img, origin='lower', interpolation='nearest'); ax.axis('off')
            ax.set_title(f'{title}: {vname}', fontsize=9)
    fig.suptitle('bind pose, frontmost Gaussian per 4 mm cell (each covering 5 x 5 cells); normals as 0.5 + 0.5 n (x red, y green, z blue); '
                 'shaded = n . L, L from his front-left-above', fontsize=10)
    fig.savefig(path, bbox_inches='tight')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('surface'); ap.add_argument('distil'); ap.add_argument('rig'); ap.add_argument('out')
    ap.add_argument('--views', type=int, default=64, help='depth views over the sphere')
    ap.add_argument('--pixel', type=float, default=0.006, help='depth cell in the fit frame (townsman 385: 1 unit = 1.64 m)')
    ap.add_argument('--min-alpha', type=float, default=0.2, help='fit Gaussians below this alpha do not occlude')
    ap.add_argument('--depth-smooth', type=float, default=2.0, help='blur of each depth map before its gradient, pixels')
    ap.add_argument('--tolerance', type=float, default=0.01, help='how far behind the frontmost depth still counts as seen')
    ap.add_argument('--neighbours', type=int, default=6, help='seen neighbours averaged for an unseen Gaussian')
    ap.add_argument('--materials', type=int, default=12, help='chromaticity clusters (each also split by dominant bone)')
    ap.add_argument('--shade-range', type=float, nargs=2, default=(0.5, 2.0))
    ap.add_argument('--png')
    a = ap.parse_args()

    S = np.load(a.surface); D = np.load(a.distil); G = np.load(a.rig)
    n = len(D['means'])
    if len(G['rest_means']) != n or not np.array_equal(G['opacities'], D['opacities']):
        raise SystemExit('the rig npz is not this distil')
    fit_alpha = 1 / (1 + np.exp(-S['opacities'].astype(np.float64)))
    surface_pts = S['means'][fit_alpha >= a.min_alpha].astype(np.float64)
    pts = D['means'].astype(np.float64)
    n_src, seen, seen_any = surface_normals(surface_pts, pts, a.views, a.pixel, a.depth_smooth, a.tolerance, a.neighbours)

    # Into bind space with the rig's own per-Gaussian rotation.
    q_src = Rot.from_quat(D['quats'][:, [1, 2, 3, 0]].astype(np.float64))
    q_rig = Rot.from_quat(G['quats'][:, [1, 2, 3, 0]].astype(np.float64))
    n_bind = (q_rig * q_src.inv()).apply(n_src)
    n_bind /= np.linalg.norm(n_bind, axis=1, keepdims=True)

    fallback = fallback_normals(G)
    fallback_src = (q_rig * q_src.inv()).inv().apply(fallback)
    angle = np.degrees(np.arccos(np.clip((fallback * n_bind).sum(1), -1, 1)))
    alpha = 1 / (1 + np.exp(-D['opacities'].astype(np.float64)))
    rest = G['rest_means']
    cell = 0.004   # metres, for the bind-pose sheet

    # Albedo: the fitted baked shading divided out, in the source frame (where the bake's lighting is fixed).
    rgb = D['rgb'].astype(np.float64); lin = srgb_to_linear(rgb)
    occ = 1 - seen
    chroma = rgb / np.maximum(rgb.sum(1, keepdims=True), 1e-3)
    _, cluster = kmeans2(chroma[:, :2], a.materials, seed=385, minit='++')
    heavy = G['bones'][np.arange(n), np.argmax(G['weights'], 1)]
    bone = np.unique(heavy, return_inverse=True)[1]
    materials = np.unique(cluster * (bone.max() + 1) + bone, return_inverse=True)[1]
    fit_rows = (seen >= 0.1) & (alpha >= 0.5)
    shading, shade_report = fit_shading(lin, n_src, occ, materials, fit_rows, tuple(a.shade_range))
    albedo = np.rint(255 * linear_to_srgb(lin / shading[:, None])).astype(np.uint8)

    report = {
        'surface': a.surface, 'distil': a.distil, 'rig': a.rig, 'gaussians': n,
        'views': a.views, 'pixel': a.pixel, 'min_alpha': a.min_alpha, 'depth_smooth_px': a.depth_smooth,
        'tolerance': a.tolerance, 'surface_gaussians': int(len(surface_pts)),
        'seen_by_a_view': round(float(seen_any.mean()), 4),
        'seen_share_p10_p50_p90': np.percentile(seen, [10, 50, 90]).round(3).tolist(),
        'facing_check_fit_frame': facing_check(surface_pts, pts, {'surface': n_src, 'fallback': fallback_src}, a.pixel,
                                               a.depth_smooth, a.tolerance),
        'fallback_vs_surface_angle_deg_p25_p50_p75': np.percentile(angle, [25, 50, 75]).round(1).tolist(),
        'albedo': shade_report,
    }
    np.savez_compressed(a.out, normal=n_bind.astype(np.float16), albedo=albedo, shading=shading.astype(np.float16),
                        seen=seen.astype(np.float16))
    print(json.dumps(report, indent=1))

    if a.png:
        L = np.array([0.6, 0.6, -0.5]); L /= np.linalg.norm(L)
        lambert = lambda nn: np.repeat(np.clip(0.15 + 0.85 * (nn @ L), 0, 1)[:, None], 3, 1)  # noqa: E731
        sheet(a.png, rest, alpha, [('baked colour', rgb), ('surface normal', 0.5 + 0.5 * n_bind),
                                   ('surface normal shaded', lambert(n_bind)),
                                   ('Engine fallback (approx.)', 0.5 + 0.5 * fallback),
                                   ('fallback shaded', lambert(fallback)),
                                   ('fitted baked shading (x 0.5)', np.repeat(np.clip(shading / 2, 0, 1)[:, None], 3, 1)),
                                   ('albedo', albedo / 255)], cell)


if __name__ == '__main__':
    main()
