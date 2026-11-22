"""The leafless winter oak is defined mostly by the negative spaces between its twigs.
Follow connected painted strokes to their forks, simplify the centre lines, and sweep
wood around them. A fork shares its attachment and depth with the parent stroke.
"""
import math

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import dijkstra

from desert_trees import acacia_pad
from rainforest_trees import Tree, filled


def trace_bare(pixels):
    solid = pixels[..., 3] > 100
    h, w = solid.shape
    solid = solid | ndimage.binary_closing(solid, np.ones((3, 3)))
    # A few strokes, including the high leader, end a few pixels short of
    # their parent in the painting. Join those substantial islands locally.
    labels, _ = ndimage.label(solid, np.ones((3, 3)))
    main = labels == np.argmax(np.bincount(labels[solid]))
    gap, near = ndimage.distance_transform_edt(~main, return_indices=True)
    for label, box in enumerate(ndimage.find_objects(labels), 1):
        island = labels[box] == label
        if main[box][island].any() or island.sum() < h*w*.00028:
            continue
        iy, ix = np.unravel_index(np.where(island, gap[box], np.inf).argmin(), island.shape)
        y, x = iy+box[0].start, ix+box[1].start
        if gap[y, x] < h*.038:
            py, px = near[:, y, x]
            for t in np.linspace(0, 1, 2+round(2*gap[y, x])):
                cy, cx = round(y+(py-y)*t), round(x+(px-x)*t)
                solid[max(0, cy-1):cy+2, max(0, cx-1):cx+2] = True
    radius = ndimage.distance_transform_edt(solid)
    ids = np.arange(h*w).reshape(h, w)
    sources, targets, costs = [], [], []
    for dy, dx in ((0, 1), (1, -1), (1, 0), (1, 1)):
        sy = slice(0, h-dy)
        ty = slice(dy, h)
        sx = slice(max(0, -dx), min(w, w-dx))
        tx = slice(max(0, dx), min(w, w+dx))
        valid = solid[sy, sx]&solid[ty, tx]
        sources.append(ids[sy, sx][valid])
        targets.append(ids[ty, tx][valid])
        edge = np.minimum(radius[sy, sx][valid], radius[ty, tx][valid])
        costs.append(math.hypot(dx, dy)/(.5+edge)**.85)
    # Prefer the middle of a thick stroke over a shortcut along its edge.
    graph = coo_matrix((np.concatenate(costs), (np.concatenate(sources), np.concatenate(targets))), shape=(h*w, h*w)).tocsr()
    root_y = round(.976*(h-1))
    root_x = radius[root_y].argmax()
    root = root_y*w+root_x
    dist, prev = dijkstra(graph, directed=False, indices=root, return_predecessors=True)
    included = np.zeros(h*w, bool)
    included[root] = True
    covered = np.zeros((h, w), bool)
    covered[root_y, root_x] = True
    paths = []
    # Admit the most distant uncovered tip, then cover the width of that limb.
    while True:
        gap = ndimage.distance_transform_edt(~covered)
        score = np.where(np.isfinite(dist.reshape(h, w))&solid, gap, 0)
        end = score.argmax()
        if score.flat[end] < h*.017:
            break
        trail = []
        while not included[end]:
            trail.append(end)
            included[end] = True
            end = prev[end]
        trail = np.array([end, *trail[::-1]])
        y, x = np.divmod(trail, w)
        for cy, cx in zip(y, x):
            r = int(np.ceil(radius[cy, cx]+1))
            ya, yb = max(0, cy-r), min(h, cy+r+1)
            xa, xb = max(0, cx-r), min(w, cx+r+1)
            py, px = np.ogrid[ya:yb, xa:xb]
            covered[ya:yb, xa:xb] |= (py-cy)**2+(px-cx)**2 <= r*r
        paths.append(trail)
    return paths, radius


def simplify(coords, fixed, tolerance):
    """Keep junctions, then resolve bends and radius changes to sub-texel precision."""
    keep = set(np.flatnonzero(fixed))
    stack = list(zip(sorted(keep)[:-1], sorted(keep)[1:]))
    while stack:
        left, right = stack.pop()
        if right-left < 2:
            continue
        line = coords[right]-coords[left]
        mid = coords[left+1:right]-coords[left]
        along = np.clip(mid@line/max(line@line, 1e-9), 0, 1)
        error = np.linalg.norm(mid-along[:, None]*line, axis=1)
        split = int(error.argmax())
        if error[split] > tolerance:
            at = left+1+split
            keep.add(at)
            stack.extend(((left, at), (at, right)))
    return np.array(sorted(keep))


def wood_path(tree, centres, radii, sides, *, turn=0):
    start = len(tree.bark["positions"])//3
    tree.tube(centres, radii, sides, cap=True, wrap_height=1)
    pts = np.array(tree.bark["positions"][start*3:]).reshape(-1, 3)
    uv = tree.uv(pts)
    # Stay inside the painted stroke at its flanks, where a projected UV
    # otherwise pulls a single snowy edge texel across the whole side face.
    rings = len(centres)*(sides+1)
    centre_uv = tree.uv(np.array(centres))
    chart = uv[:rings].reshape(len(centres), sides+1, 2)
    chart[:] = centre_uv[:, None, :]+.82*(chart-centre_uv[:, None, :])
    tree.bark["uvs"][start*2:] = uv.ravel().tolist()
    if turn:
        cosine, sine = math.cos(turn), math.sin(turn)
        rot = np.array([[cosine, 0, -sine], [0, 1, 0], [sine, 0, cosine]])
        pivot = centres[0]
        tree.bark["positions"][start*3:] = ((pts-pivot)@rot+pivot).round(6).ravel().tolist()
        normals = np.array(tree.bark["normals"][start*3:]).reshape(-1, 3)
        tree.bark["normals"][start*3:] = (normals@rot).round(6).ravel().tolist()


def traced_limbs(tree, *, draw=wood_path, spread_forks=False, silhouette=None):
    """Sweep connected wood through the painted strokes, rooted at the actual foot."""
    h, w = tree.pixels.shape[:2]
    paths, radius = trace_bare(tree.pixels if silhouette is None else silhouette)
    root = paths[0][0]
    tree.foot_x = (root%w)/(w-1)
    joint = {path[0] for path in paths[1:]}
    depth = np.zeros(h*w)
    anchors = []
    side_forks = [0, 0]
    for i, path in enumerate(paths):
        yy, xx = np.divmod(path, w)
        steps = np.r_[0, np.cumsum(np.hypot(np.diff(xx), np.diff(yy)))]
        if steps[-1] < 1:
            continue
        length = steps/w
        # Most of the principal trunk stays in profile. Each new fork turns
        # towards or away from it, while the thin ends keep their silhouette.
        direction = .18 if i == 0 else (.70 if i%2 else -.69)*(.84+.28*math.sin(i*2.4))
        if spread_forks and i:
            # Alternate near and far branches on each side of a lopsided tree.
            # Its numerous left-hand forks would otherwise all lie in one plane.
            side = int(xx[-1] > xx[0])
            direction = (.72 if side_forks[side]%2 == 0 else -.72)*(.85+.24*math.cos(i))
            side_forks[side] += 1
        depth[path] = depth[path[0]]+length*direction+.049*np.sin(length*8+i)*np.minimum(length*4, 1)
        smoothed = ndimage.median_filter(radius[yy, xx], size=3)
        # Keep each attachment exactly, as well as corners along winding thin twigs.
        # Subpixel zigzags from eight-neighbour walks would give a limb a sawtooth edge.
        sx = ndimage.gaussian_filter1d(xx.astype(float), 1.25, mode="nearest")
        sy = ndimage.gaussian_filter1d(yy.astype(float), 1.25, mode="nearest")
        fixed = np.array([j == 0 or j == len(path)-1 or p in joint for j, p in enumerate(path)])
        sx[fixed], sy[fixed] = xx[fixed], yy[fixed]
        ids = simplify(np.column_stack([sx, sy, depth[path]*w, 1.3*smoothed]), fixed, 1.04)
        centres = [tree.point(sx[j]/(w-1), sy[j]/(h-1), depth[path[j]]) for j in ids]
        radii = (.89*smoothed[ids]/w).tolist()
        radii[-1] = max(.0016, radii[-1]*.57)
        if i == 0:
            centres.insert(0, tree.point(tree.foot_x, 1.004, 0))
            radii.insert(0, radii[0]*1.04)
        sides = 6 if max(radii) > .035 else 4
        draw(tree, centres, radii, sides)
        # Secondary boughs reach into the near and far quadrants,
        # keeping the long bare crown open from a quarter turn as well.
        if not spread_forks and .45 < yy[0]/h < .69 and steps[-1] > .23*h and i < 12:
            turn = 1.55 if xx[-1] > xx[0] else -1.62
            draw(tree, centres, [r*.76 for r in radii], sides, turn=turn)
        anchors.extend(centres)
    return np.array(anchors)


def build(rgba):
    tree = Tree(15, rgba, ((.49, 1, 0, .08), (.5, .6, 0, .05)))
    anchors = traced_limbs(tree)
    h, w = tree.pixels.shape[:2]
    # Dished cutouts carry the two dark clusters of leaves still clinging to
    # the forks. Their little patches of white belong to the original painting.
    image = Image.fromarray(tree.pixels)
    for x, y, rx, ry in ((.245, .273, .103, .052), (.654, .169, .121, .059), (.723, .174, .063, .039)):
        needle = tree.point(x, y, 0)
        nearest = anchors[np.argmin(np.linalg.norm(anchors[:, :2]-needle[:2], axis=1))]
        centre = tree.point(x, y, nearest[2]/tree.aspect)
        box = (round((x-rx)*w), round((y-ry)*h), round((x+rx)*w), round((y+ry)*h))
        patch = image.crop(box).resize((64, 56), Image.Resampling.NEAREST)
        acacia_pad(tree, patch, centre, rx*tree.aspect, ry)
    images = {"bark":filled(tree.pixels, tree.pixels[..., 3] > 128), "leaves":tree.cluster_atlas()}
    parts = [tree.bark, tree.leaves]
    return {"height":max(max(p["positions"][1::3]) for p in parts), "parts":parts}, images
