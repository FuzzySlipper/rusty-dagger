#!/usr/bin/env python3
"""Make refreshed rocks, stones, logs, woodpiles and dead trees procedurally, in the mesh trees' manner (#10849).

Shapes are scripted, as rusty-craftsurvive's scatter rocks and deadwood are, and every surface wears a small generated
tile painted in the colours of the art it replaces, as trees.py paints the mesh trees.

- **A rock** is a sphere of triangles pushed out to the shape it stands for, then pressed in: a cell of it is one stone,
  sunk a little where it meets the next, so a heap reads as stones lying together, and each region of it is cut flat
  across its highest part for broad faces and hard edges (rusty-craftsurvive's make-frozen-rocks.py). Nothing is ever
  pushed out, so a remodel stays inside its classic's volume.
- **A log or a dead tree** is rusty-craftsurvive's branch skeleton (generate-flora.py's tubes and caps, which it winds
  inward and this turns outward): a trunk and limbs of a few sides each, tapering, their ends cut flat.
- **A tile** is drawn in five steps of tone from tiling noise, with dark cracks lit along one side (stone, as
  generate-cave-rock.mjs draws its cave rock), or generate-flora.py's fissured bark, then painted: each step takes the
  median colour of its own band of brightness among the stone's colours, so the piece wears that art's palette and its
  spread of light and dark in a few flat tones, and the most common other colour (lichen, moss) lies over a share of it
  in blotches. A flat source (archive 0's one-colour records) is stepped from its colour. Texels are drawn nearest,
  TEXELS_PER_METRE to a metre, as the mesh trees' bark is.

ARCH3D models (`models`): each recipe below is held to its classic: the classic's surface is the shape it is pressed in
from (a heap's over its footprint), every vertex stays inside its convex hull, and a face takes the material of the
classic face nearest it and facing its way. A face the classic paints with stone or wood is painted for that classic
identity, and once more for each identity a location may draw it as (`extras.drawn`: the classic's climate swap of 141
to 41 in deserts and 341 in temperate towns), so a rock matches the town's stone; any other face keeps the classic's
identity, which the location draws. Finals go to local/art/generated/models/ID.glb in the classic's frame, for
`promote.py --area models --key ID`.

Nature flats: trees.py draws the piles from fieldstones.py and the deadwood from trees/deadwood.py. Placed flats (`placed`): the decoration flats blocks place, as GLBs in
local/art/generated/effects/ for the art pack's `meshes` (promote.py's WORLD_MESHES, `--area effects --key A-R`), each
built across its sprite's width, which the game draws it at from the GLB's own width.

    rocks.py models [ID ...]
    rocks.py placed [ARCHIVE-RECORD ...]

It reads the import's staged classic meshes and, through surfaces/tiling.py, the identities the import's climate and
dungeon tables draw a classic texture as, the art pack's refreshed surfaces and sprites, and rusty-craftsurvive's flora generator (CRAFTSURVIVE, default ~/dev/rusty-craftsurvive).
"""
import argparse, colorsys, io, json, math, os, struct, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import ConvexHull

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "authoring/tools/surfaces"))
import tiling  # noqa: E402
CONTENT = REPOSITORY / "content"
ART = REPOSITORY / "local/art"
GEOMETRY = REPOSITORY / "local/build/import/staging/worldrpg/imports/shared/geometry"
SURFACES = CONTENT / "worldrpg/packs/daggerfall.art-replacements/media/surfaces"

# Texels a metre, as the mesh trees' bark: chunky at arm's length, of a piece with the refreshed sprites.
TEXELS_PER_METRE = 32
# The stone tile's side in texels; each face reads a different part of it, so corners do not line its marks up.
TILE = 64
# Bark texels round a trunk, a tile a metre round as generate-flora.py draws it.
BARK_AROUND = 32
# Points nearer than this are one corner: the classic stores each face's corners apart.
WELD = 1e-3
# Faces whose normals part by less than this share their corners' normals, so a cut face is one flat facet and a
# rounded stone shades round, while a crease between facets stays hard.
SMOOTH_DEGREES = 22


# ---- reading the classic --------------------------------------------------------------------------------------------

def classic(model):
    """The classic model's triangles in its own frame, the classic texture identity of each, and its uvs per corner."""
    data = (GEOMETRY / f"mesh-{model}.rstatmsh").read_bytes()
    if data[:8] != b"RSTATMSH":
        raise SystemExit(f"{model}: not a static mesh")
    length = struct.unpack_from("<I", data, 8)[0]
    head = json.loads(data[12:12 + length])
    payload, base = head["payload"], 12 + length
    source, count = payload["source"], payload["layout"]["vertexCount"]
    positions = np.frombuffer(data, "<f4", count * 3, base + source["positionsByteOffset"]).reshape(-1, 3).astype(float)
    uvs = np.frombuffer(data, "<f4", count * 2, base + source["uvsByteOffset"]).reshape(-1, 2).astype(float)
    indices = np.frombuffer(data, "<u4", payload["layout"]["indexCount"], base + source["indicesByteOffset"]).reshape(-1, 3)
    slots = {slot["slot"]: slot["material"].split("/")[-1] for slot in head["materialSlots"]}
    materials = []
    for group in payload["groups"]:
        materials += [slots[group["materialSlot"]]] * (group["count"] // 3)
    return positions[indices], np.array(materials), uvs[indices]


def hull_planes(points):
    """The convex hull's planes: unit outward normals and offsets, a point inside when normal . point <= offset."""
    hull = ConvexHull(points)
    return hull.equations[:, :3], -hull.equations[:, 3]


def solid_centre(points):
    """The centre of the hull's volume."""
    hull = ConvexHull(points)
    inner = points[hull.vertices].mean(0)
    total, centre = 0.0, np.zeros(3)
    for simplex in hull.simplices:
        a, b, c = points[simplex]
        volume = abs(np.dot(a - inner, np.cross(b - inner, c - inner))) / 6
        total += volume
        centre += volume * (a + b + c + inner) / 4
    return centre / total


def ray_hits(origin, directions, triangles):
    """How far along each direction from origin the farthest classic face lies (Moller-Trumbore), nan where none."""
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    e1, e2 = b - a, c - a
    far = np.full(len(directions), np.nan)
    for i, d in enumerate(directions):
        p = np.cross(d, e2)
        det = np.einsum("ij,ij->i", e1, p)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
        s = origin - a
        u = np.einsum("ij,ij->i", s, p) * inv
        q = np.cross(s, e1)
        v = (q @ d) * inv
        t = np.einsum("ij,ij->i", e2, q) * inv
        hit = ok & (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1 + 1e-6) & (t > 1e-6)
        if hit.any():
            far[i] = t[hit].max()
    return far


def hull_distance(origin, directions, normals, offsets):
    """How far along each direction from origin (inside the hull) its surface lies."""
    reach = (offsets - normals @ origin)[None, :] / np.where(directions @ normals.T > 1e-9, directions @ normals.T, np.nan)
    return np.nanmin(np.where(reach > 0, reach, np.nan), axis=1)


def into_hull(points, normals, offsets, rounds=8):
    """Each point moved onto every hull plane it stands out of, until none does."""
    points = points.copy()
    for _ in range(rounds):
        over = points @ normals.T - offsets
        if over.max() <= 0:
            break
        worst = np.argmax(over, axis=1)
        out = over[np.arange(len(points)), worst]
        points -= np.maximum(out, 0)[:, None] * normals[worst]
    return points


# ---- shapes ---------------------------------------------------------------------------------------------------------

def icosphere(detail):
    """A unit sphere of triangles, wound outward."""
    t = (1 + 5 ** 0.5) / 2
    points = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1),
              (-t, 0, -1), (-t, 0, 1)]
    faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
             (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    points = [np.array(point, float) / np.linalg.norm(point) for point in points]
    for _ in range(detail):
        middles, finer = {}, []

        def middle(a, b):
            key = (min(a, b), max(a, b))
            if key not in middles:
                point = points[a] + points[b]
                points.append(point / np.linalg.norm(point))
                middles[key] = len(points) - 1
            return middles[key]

        for a, b, c in faces:
            ab, bc, ca = middle(a, b), middle(b, c), middle(c, a)
            finer += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        faces = finer
    return np.array(points), np.array(faces)


def mound(triangles, rings, around):
    """A heap's surface as a polar heightfield over the classic's footprint: rings from its middle out to the footprint's
    edge, each point at the height of the classic's top above it, the outer ring on the ground. A heap stands on the
    ground with nothing under its slopes, so this keeps its foot exactly the classic's outline."""
    corners = triangles.reshape(-1, 3)
    ground = corners[:, 1].min()
    flat = corners[:, [0, 2]]
    outline = ConvexHull(flat)
    middle = flat[outline.vertices].mean(0)
    edges = outline.equations
    points, faces = [], []
    angles = np.linspace(0, 2 * np.pi, around, endpoint=False)
    directions = np.stack([np.cos(angles), np.sin(angles)], 1)
    # How far the footprint reaches from its middle in each direction.
    reach = np.nanmin(np.where(directions @ edges[:, :2].T > 1e-9,
                               (-edges[:, 2] - edges[:, :2] @ middle)[None] / np.maximum(directions @ edges[:, :2].T, 1e-9), np.nan), axis=1)
    top = corners[:, 1].max() + 1
    points.append((middle[0], 0, middle[1]))
    for ring in range(1, rings + 1):
        share = ring / rings
        for k in range(around):
            x, z = middle + directions[k] * reach[k] * share
            points.append((x, 0, z))
    points = np.array(points, float)
    down = np.array([0, -1.0, 0])
    for i, point in enumerate(points):
        hit = ray_hits(np.array([point[0], top, point[2]]), [down], triangles)[0]
        points[i, 1] = top - hit if not np.isnan(hit) else ground
    points[-around:, 1] = ground
    for k in range(around):
        faces.append((0, 1 + (k + 1) % around, 1 + k))
    for ring in range(1, rings):
        inner, outer = 1 + (ring - 1) * around, 1 + ring * around
        for k in range(around):
            a, b = inner + k, inner + (k + 1) % around
            c, d = outer + k, outer + (k + 1) % around
            faces += [(a, b, d), (a, d, c)]
    return points, np.array(faces)


def vertex_normals(points, faces):
    corners = points[faces]
    area = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    normals = np.zeros_like(points)
    for k in range(3):
        np.add.at(normals, faces[:, k], area)
    return normals / np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-9)


def spread(points, count, rng):
    """`count` of the points, each the farthest from those chosen before it, so cells come out even."""
    chosen = [int(rng.integers(len(points)))]
    gap = np.linalg.norm(points - points[chosen[0]], axis=1)
    for _ in range(count - 1):
        chosen.append(int(np.argmax(gap * rng.uniform(.85, 1, len(points)))))
        gap = np.minimum(gap, np.linalg.norm(points - points[chosen[-1]], axis=1))
    return points[chosen]


def cells_of(points, seeds):
    distance = np.linalg.norm(points[:, None] - seeds[None], axis=2)
    order = np.sort(distance, axis=1)
    return np.argmin(distance, axis=1), (order[:, 1] - order[:, 0]) / 2 if len(seeds) > 1 else np.full(len(points), np.inf)


def press(points, faces, rng, stones, groove, facets, cut_depth, tilt, wobble):
    """
    Presses a shape in, never out. It reads as `stones` stones, each sunk by up to `groove` (metres) toward where it meets
    the next, and its surface is `facets` regions, each cut flat across its highest part by a plane near its own facing,
    `cut_depth` (a share of the region's size) in: broad faces and hard edges, each where it is, as a knapped stone's.
    A region facing the ground is left as it is. Then a low wobble, inward.
    """
    normals = vertex_normals(points, faces)
    size = np.ptp(points, axis=0).max()
    pressed = points.copy()
    if stones > 1:
        cell, edge = cells_of(points, spread(points, stones, rng))
        reach = size / stones ** .5 * .35
        pressed -= normals * (groove * (1 - np.clip(edge / reach, 0, 1)) ** 2)[:, None]
    region, _ = cells_of(points, spread(points, facets, rng))
    for k in range(facets):
        mine = np.nonzero(region == k)[0]
        if len(mine) < 3:
            continue
        facing = normals[mine].sum(0)
        facing /= max(np.linalg.norm(facing), 1e-9)
        if facing[1] < -.35:
            continue
        normal = facing + rng.normal(size=3) * tilt
        normal /= np.linalg.norm(normal)
        heights = pressed[mine] @ normal
        span = np.ptp(pressed[mine], axis=0).max()
        offset = heights.max() - span * rng.uniform(*cut_depth)
        pressed[mine] -= np.outer(np.maximum(heights - offset, 0), normal)
    if wobble:
        phase = rng.uniform(0, 6.28, 3)
        unit = (points - points.mean(0)) / size
        wave = np.sin(unit[:, 0] * 9.1 + phase[0]) * np.sin(unit[:, 1] * 8.3 + phase[1]) * np.sin(unit[:, 2] * 9.7 + phase[2])
        pressed -= normals * ((wave + 1) / 2 * wobble * size)[:, None]
    # Nothing moves where the shape meets the ground, so its foot keeps the classic's outline instead of fraying.
    rise = points[:, 1] - points[:, 1].min()
    hold = np.clip(rise / (np.ptp(points[:, 1]) * .15), 0, 1)
    return points + (pressed - points) * (hold * hold * (3 - 2 * hold))[:, None]


# ---- textures -------------------------------------------------------------------------------------------------------

def wrap_noise(rng, size, cells):
    """Tiling value noise, smoothly interpolated, wrapping on both axes."""
    lattice = rng.random((cells, cells))
    at = np.arange(size) / size * cells
    i0 = np.floor(at).astype(int)
    f = at - i0
    f = f * f * (3 - 2 * f)
    i0, i1 = i0 % cells, (i0 + 1) % cells
    rows = lattice[i0][:, i0] * (1 - f)[None, :] + lattice[i0][:, i1] * f[None, :]
    rows1 = lattice[i1][:, i0] * (1 - f)[None, :] + lattice[i1][:, i1] * f[None, :]
    return rows * (1 - f)[:, None] + rows1 * f[:, None]


def stone_steps(seed):
    """A stone tile in five steps of tone, 0 (crack) to 4 (lit edge): mottled planes split by dark cracks, each lit along
    the side away from it, as generate-cave-rock.mjs draws its cave rock and generate-flora.py its fissures; and where
    lichen may grow, the most of it first."""
    rng = np.random.default_rng(seed)
    tone = .5 * wrap_noise(rng, TILE, 4) + .3 * wrap_noise(rng, TILE, 8) + .2 * wrap_noise(rng, TILE, 16)
    tone = (tone - tone.min()) / np.ptp(tone)
    steps = 1 + np.clip((tone * 2.999).astype(int), 0, 2)
    ridge = 1 - np.abs(wrap_noise(rng, TILE, 6) * 2 - 1)
    crack = ridge > .94
    steps[crack] = 0
    # The texel above-left of a crack catches the light, a step up from the plane it edges.
    lit = np.roll(np.roll(crack, -1, 0), -1, 1) & ~crack
    steps[lit] = np.minimum(steps[lit] + 1, 4)
    lichen = .7 * wrap_noise(rng, TILE, 8) + .3 * wrap_noise(rng, TILE, 32)
    lichen[crack | lit] = 0
    return steps, lichen


def bark_steps(seed):
    """A bark tile, BARK_AROUND texels round by twice that up, in five steps of tone: generate-flora.py's fissured oak,
    its plates mottled in three steps, dark fissures wandering up it in long runs (0) with a lit ridge beside them (4)."""
    rng = np.random.default_rng(seed)
    w, h = BARK_AROUND, BARK_AROUND * 2
    tone = .6 * wrap_noise(rng, h, 8)[:, :w] + .4 * wrap_noise(rng, h, 16)[:, :w]
    tone = (tone - tone.min()) / np.ptp(tone)
    steps = 1 + np.clip((tone * 2.999).astype(int), 0, 2)
    for start in range(0, w, 6):
        x = start + int(rng.integers(0, 2))
        for y in range(h):
            if rng.random() < .15:
                x += int(rng.choice((-1, 1)))
            if rng.random() < .9:
                steps[y, x % w] = 0
            if rng.random() < .35:
                steps[y, (x + 1) % w] = 4
    return steps, np.zeros(steps.shape)


def end_steps(seed):
    """A log's sawn end, a tile one cut face spans: growth rings in two steps of tone round a dark pith, a dark band of
    bark round its edge, and a few checks split out from the middle (0)."""
    rng = np.random.default_rng(seed)
    y, x = (np.mgrid[:BARK_AROUND, :BARK_AROUND] + .5) / BARK_AROUND * 2 - 1
    r = np.hypot(x, y) * (1 + .1 * (wrap_noise(rng, BARK_AROUND, 4) - .5))
    steps = np.where(np.sin(r * 24 + 2 * wrap_noise(rng, BARK_AROUND, 8)) > .3, 2, 3)
    steps[r < .1] = 1
    steps[r > .8] = 1
    steps[r > .92] = 0
    angle = np.arctan2(y, x)
    for check in rng.uniform(-np.pi, np.pi, 3):
        off = np.abs((angle - check + np.pi) % (2 * np.pi) - np.pi) * r
        steps[(off < .05) & (r > .15) & (r < .75)] = 0
    return steps, np.zeros(steps.shape)


# A colour this saturated (HSV) is not the stone's own grey but what grows on it.
GREY = .2
# The most of a stone's tile its lichen covers, and the least share of the source's colours that gives it any.
LICHEN, LICHEN_LEAST = .1, .04


# A flat source (archive 0's records are each one colour) is stepped through these shares of its own colour instead, as
# rusty-craftsurvive's palette ramps step a hue from shadow to light.
FLAT_STEPS = (.5, .7, .85, 1, 1.18)
# A source whose bands span less than this in brightness (0-255) is flat.
FLAT_SPAN = 12


def ramp(source, steps=5):
    """The source's colours in `steps` equal bands by brightness, each its median colour, darkest first: wood's palette,
    which has no lichen to leave out. A flat source is stepped from its one colour."""
    rgba = np.asarray(Image.open(source).convert("RGBA"), float)
    colours = rgba[..., :3][rgba[..., 3] > 128]
    colours = colours[np.argsort(colours @ [.2126, .7152, .0722])]
    bands = np.array([np.median(band, axis=0) for band in np.array_split(colours, steps)])
    if np.ptp(bands @ [.2126, .7152, .0722]) < FLAT_SPAN:
        bands = np.clip(np.outer(FLAT_STEPS, colours.mean(0)), 0, 255)
    return bands, None


def palette(source, steps=5):
    """The source's colours as a stone's: the median colour of each of `steps` equal bands by brightness of the stone's
    own colours, darkest first, and what grows on it, the most common other colour with the share it covers, or None
    where there is hardly any. Stone is grey where most of the source is (the classic stone surfaces), else its most
    common hue and the greys as dark as it (a sandstone sprite and its shadows); what grows on it is the rest."""
    rgba = np.asarray(Image.open(source).convert("RGBA"), float)
    colours = rgba[..., :3][rgba[..., 3] > 128]
    high, low = colours.max(1), colours.min(1)
    grey = (high - low) / np.maximum(high, 1) < GREY
    hue = (np.array([colorsys.rgb_to_hsv(*(c / 255))[0] for c in colours]) * 12).astype(int) % 12
    if grey.mean() >= .5:
        stone = grey
    else:
        counts = np.bincount(hue[~grey], minlength=12)
        main = np.argmax(counts + np.roll(counts, 1) + np.roll(counts, -1))
        near = ~grey & (np.abs((hue - main + 6) % 12 - 6) <= 1)
        stone = near | grey & (colours @ [.2126, .7152, .0722] <= np.median(colours[near] @ [.2126, .7152, .0722]))
    rock = colours[stone] if stone.sum() >= steps * 4 else colours
    rock = rock[np.argsort(rock @ [.2126, .7152, .0722])]
    ramp = np.array([np.median(band, axis=0) for band in np.array_split(rock, steps)])
    share = 1 - stone.mean()
    if share < LICHEN_LEAST or (~stone & ~grey).sum() < 4:
        return ramp, None
    other = colours[~stone & ~grey]
    other_hue = hue[~stone & ~grey]
    return ramp, (np.median(other[other_hue == np.argmax(np.bincount(other_hue, minlength=12))], axis=0), min(LICHEN, share * .5))


def paint(tile, colours):
    """A tile painted as RGBA: each step its ramp colour, and lichen over the share of it most given to lichen."""
    steps, lichen = tile
    ramp, accent = colours
    rgb = ramp[steps]
    if accent is not None:
        colour, share = accent
        rgb[lichen >= np.quantile(lichen, 1 - share)] = colour
    out = np.empty(steps.shape + (4,), np.uint8)
    out[..., :3] = np.clip(np.round(rgb), 0, 255)
    out[..., 3] = 255
    return out


def png(rgba):
    buffer = io.BytesIO()
    Image.fromarray(rgba, "RGBA").save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def surface(identity):
    """The refreshed surface the classic draws for a texture identity `texture-A-R`."""
    archive, record = identity.split("-")[1:]
    return SURFACES / f"texture-{int(archive):03d}-{record}.png"


# ---- uvs and normals ------------------------------------------------------------------------------------------------

def projected_uvs(corners, normal, tile_metres):
    """Uvs projected on the face's dominant plane, in tiles; each plane reads a different part of the tile."""
    axis = int(np.argmax(np.abs(normal)))
    plane = [(2, 1), (0, 2), (0, 1)][axis]
    return np.stack([corners[:, plane[0]] / tile_metres + axis * .37, -corners[:, plane[1]] / tile_metres + axis * .21], axis=1)


def classic_uvs(corners, triangle, uvs):
    """The classic face's own texture mapping carried onto points of its plane: the affine map its corners define."""
    a, b, c = triangle
    e1, e2 = b - a, c - a
    n = np.cross(e1, e2)
    basis = np.array([e1, e2, n]).T
    weights = np.linalg.solve(basis, (corners - a).T).T
    return uvs[0] + weights[:, :1] * (uvs[1] - uvs[0]) + weights[:, 1:2] * (uvs[2] - uvs[0])


def split_normals(points, faces):
    """Per corner normals: the area-weighted mean of the faces about the corner that turn from this face by less than
    SMOOTH_DEGREES."""
    corners = points[faces]
    cross = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    unit = cross / np.maximum(np.linalg.norm(cross, axis=1, keepdims=True), 1e-12)
    around = [[] for _ in range(len(points))]
    for f, face in enumerate(faces):
        for v in face:
            around[v].append(f)
    limit = math.cos(math.radians(SMOOTH_DEGREES))
    out = np.zeros((len(faces), 3, 3))
    for f, face in enumerate(faces):
        for k, v in enumerate(face):
            near = [g for g in around[v] if unit[g] @ unit[f] >= limit]
            total = cross[near].sum(0)
            out[f, k] = total / max(np.linalg.norm(total), 1e-12)
    return unit, out


# ---- GLB ------------------------------------------------------------------------------------------------------------

def write_glb(path, primitives):
    """A binary glTF of one mesh: each primitive (material name, its PNG or None, its drawn PNGs by identity, positions,
    normals and uvs per corner, three corners a triangle). A material named for a classic texture identity draws the
    location's surface for it, or, with a PNG, is painted for it, its `extras.drawn` holding a PNG for each identity the
    location may draw it as (model-mesh's painted slot)."""
    blob, views, accessors, materials, images, textures, parts = bytearray(), [], [], [], [], [], []

    def view(data, target=None):
        while len(blob) % 4:
            blob.append(0)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)} | ({"target": target} if target else {}))
        blob.extend(data)
        return len(views) - 1

    def image(data):
        images.append({"bufferView": view(data), "mimeType": "image/png"})
        textures.append({"source": len(images) - 1, "sampler": 0})
        return len(textures) - 1

    for name, picture, drawn, positions, normals, uvs in primitives:
        material = {"name": name, "pbrMetallicRoughness": {"metallicFactor": 0, "roughnessFactor": 1}}
        if picture is not None:
            material["pbrMetallicRoughness"]["baseColorTexture"] = {"index": image(picture)}
        if drawn:
            material["extras"] = {"drawn": {identity: image(data) for identity, data in drawn.items()}}
        materials.append(material)
        count = len(positions)
        attributes = {}
        for key, values, kind in (("POSITION", positions, "VEC3"), ("NORMAL", normals, "VEC3"), ("TEXCOORD_0", uvs, "VEC2")):
            values = np.asarray(values, "<f4")
            accessor = {"bufferView": view(values.tobytes(), 34962), "componentType": 5126, "count": count, "type": kind}
            if key == "POSITION":
                accessor |= {"min": values.min(0).tolist(), "max": values.max(0).tolist()}
            accessors.append(accessor)
            attributes[key] = len(accessors) - 1
        indices = np.arange(count, dtype="<u4")
        accessors.append({"bufferView": view(indices.tobytes(), 34963), "componentType": 5125, "count": count, "type": "SCALAR"})
        parts.append({"attributes": attributes, "indices": len(accessors) - 1, "material": len(materials) - 1})
    document = {"asset": {"version": "2.0", "generator": "rusty-dagger rocks.py"}, "scene": 0, "scenes": [{"nodes": [0]}],
                "nodes": [{"mesh": 0}], "meshes": [{"primitives": parts}], "materials": materials, "accessors": accessors,
                "bufferViews": views, "buffers": [{"byteLength": len(blob)}]}
    if images:
        document |= {"images": images, "textures": textures, "samplers": [{"magFilter": 9728, "minFilter": 9984, "wrapS": 10497, "wrapT": 10497}]}
    text = json.dumps(document, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    while len(blob) % 4:
        blob.append(0)
    out = struct.pack("<III", 0x46546C67, 2, 28 + len(text) + len(blob))
    out += struct.pack("<II", len(text), 0x4E4F534A) + text + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    Path(path).write_bytes(out)


# ---- ARCH3D models --------------------------------------------------------------------------------------------------

HEAP, ROCK, STONE = "heap", "rock", "stone"
# Each remodelled classic: its kind and seed, and for a heap or rock how many stones it reads as.
# - heap: the rubble piles and mounds (41704-41719), pyramids of stone in the streets and the dungeons' caves, read as
#   stones lying together, the bigger ones as an outcrop of boulders;
# - rock: the dungeon kit's faceted rocks (60511-60520), one stone or a few;
# - stone: the standing and fallen stones (41722, 41723, 41731-41733), rough stone under a cap of masonry, which stays
#   the classic's surface.
MODELS = {
    41704: (HEAP, 1, 3), 41705: (HEAP, 2, 3), 41706: (HEAP, 3, 4), 41707: (HEAP, 4, 6), 41708: (HEAP, 5, 6),
    41709: (HEAP, 6, 8), 41710: (HEAP, 7, 3), 41711: (HEAP, 8, 6), 41712: (HEAP, 9, 3), 41713: (HEAP, 10, 7),
    41714: (HEAP, 11, 10), 41715: (HEAP, 12, 10), 41716: (HEAP, 13, 10), 41717: (HEAP, 14, 12), 41719: (HEAP, 15, 10),
    60511: (ROCK, 21, 1), 60512: (ROCK, 22, 1), 60513: (ROCK, 23, 1), 60514: (ROCK, 24, 1), 60515: (ROCK, 25, 1),
    60516: (ROCK, 26, 1), 60517: (ROCK, 27, 1), 60518: (ROCK, 28, 2), 60519: (ROCK, 29, 2), 60520: (ROCK, 30, 3),
    41722: (STONE, 41, 1), 41723: (STONE, 42, 1), 41731: (STONE, 43, 1), 41732: (STONE, 44, 1), 41733: (STONE, 45, 1),
}
# The classic textures a remodel paints with its generated stone; any other keeps the classic's identity.
PAINTED = ("texture-141-",)


def drawn_as(identity):
    """The other identities a location may draw a classic texture as: its climate swaps and the dungeons' remaps."""
    archive, record = map(int, identity.split("-")[1:])
    swaps, remaps = tiling.reach()
    return sorted({f"texture-{other}-{record}" for other in swaps.get((archive, record), set()) | remaps.get(archive, set()) if other != archive})


# Metres a face's distance counts against turning fully away from a classic face, choosing the material: a cap takes the
# cut end it faces rather than a side as near it.
FACING_METRES = .25


def nearest_faces(points, triangles, facings=None):
    """The index of the classic triangle nearest each point and, given each point's facing, turned most its way."""
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    best = np.full(len(points), np.inf)
    index = np.zeros(len(points), int)
    for t in range(len(triangles)):
        # Distance to the triangle: its plane where the foot falls inside it, else its nearest edge.
        e = [(a[t], b[t]), (b[t], c[t]), (c[t], a[t])]
        n = np.cross(b[t] - a[t], c[t] - a[t])
        n /= max(np.linalg.norm(n), 1e-12)
        foot = points - np.outer((points - a[t]) @ n, n)
        inside = np.ones(len(points), bool)
        for p, q in e:
            inside &= (np.cross(q - p, foot - p) @ n) >= -1e-9
        edges = []
        for p, q in e:
            s = np.clip(((points - p) @ (q - p)) / max((q - p) @ (q - p), 1e-12), 0, 1)
            edges.append(np.linalg.norm(points - (p + np.outer(s, q - p)), axis=1))
        d = np.where(inside, np.abs((points - a[t]) @ n), np.min(edges, axis=0))
        if facings is not None:
            d = d + FACING_METRES * (1 - facings @ n)
        closer = d < best
        best[closer], index[closer] = d[closer], t
    return index


def shape(model, kind, seed, stones, triangles):
    """The remodel's points and faces, pressed in from the classic's surface."""
    rng = np.random.default_rng(seed)
    corners = triangles.reshape(-1, 3)
    normals, offsets = hull_planes(corners)
    size = np.ptp(corners, axis=0).max()
    if kind == HEAP:
        points, faces = mound(triangles, 7 if size < 6 else 11, 24 if size < 6 else 40)
    else:
        sphere, faces = icosphere(2)
        centre = solid_centre(corners)
        reach = ray_hits(centre, sphere, triangles)
        points = centre + sphere * np.where(np.isnan(reach), hull_distance(centre, sphere, normals, offsets), reach)[:, None]
    if kind == HEAP:
        points = press(points, faces, rng, stones, groove=size * .07, facets=stones * 4, cut_depth=(.08, .25), tilt=.25, wobble=.01)
    elif kind == ROCK:
        points = press(points, faces, rng, stones, groove=size * .07, facets=8 + stones * 2, cut_depth=(.12, .3), tilt=.25, wobble=.01)
    else:
        points = press(points, faces, rng, stones, groove=0, facets=10, cut_depth=(.08, .2), tilt=.2, wobble=.006)
    return into_hull(points, normals, offsets), faces


def model(number):
    kind, seed, stones = MODELS[number]
    triangles, materials, classic_uv = classic(number)
    points, faces = shape(number, kind, seed, stones, triangles)
    unit, corner_normals = split_normals(points, faces)
    corners = points[faces]
    nearest = nearest_faces(corners.mean(1), triangles, unit)
    tile_metres = TILE / TEXELS_PER_METRE
    groups = {}
    for f in range(len(faces)):
        identity = materials[nearest[f]]
        painted = identity.startswith(PAINTED)
        uv = projected_uvs(corners[f], unit[f], tile_metres) if painted else classic_uvs(corners[f], triangles[nearest[f]], classic_uv[nearest[f]])
        group = groups.setdefault(identity, ([], [], []))
        group[0].extend(corners[f]); group[1].extend(corner_normals[f]); group[2].extend(uv)
    steps = stone_steps(seed)
    primitives = []
    for identity, (p, n, uv) in sorted(groups.items()):
        if identity.startswith(PAINTED):
            drawn = {other: png(paint(steps, palette(surface(other)))) for other in drawn_as(identity)}
            primitives.append((identity, png(paint(steps, palette(surface(identity)))), drawn, p, n, uv))
        else:
            primitives.append((identity, None, None, p, n, uv))
    folder = ART / "generated/models"
    folder.mkdir(parents=True, exist_ok=True)
    write_glb(folder / f"{number}.glb", primitives)
    print(f"{number}: {kind}, {len(faces)} triangles (classic {len(triangles)})")


# ---- logs and dead trees --------------------------------------------------------------------------------------------

# rusty-craftsurvive's checkout, whose scripted flora the mesh trees and the deadwood here are built from.
CRAFTSURVIVE = Path(os.environ.get("CRAFTSURVIVE", Path.home() / "dev/rusty-craftsurvive"))
FLORA = CRAFTSURVIVE / "authoring/tools/generators/generate-flora.py"


def flora():
    """rusty-craftsurvive's flora generator, read as a library for its branch tubes and caps."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("generate_flora", FLORA)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# Each remodelled classic log: its seed, how far along it (classic x, metres) its trunk forks, the classic texture its
# bark is painted from, and whether it ends in a point. The fallen dead trees (41734-41737) fork into two to four limbs
# toward the tips their classics reach; the long log 41400 is a straight trunk sharpened to a point at its -x end.
LOGS = {
    41734: (51, 2.4, "texture-0-75", False), 41735: (52, .6, "texture-0-75", False),
    41736: (53, -.1, "texture-0-75", False), 41737: (54, 1.0, "texture-0-75", False),
    41400: (55, -1.45, "texture-67-8", True),
}
# Sides round a trunk and round a limb, as generate-flora.py's dead tree has them.
TRUNK_SIDES, LIMB_SIDES = 7, 5
# The most limbs a fork has, as the classics' crowns.
LIMBS = 3


def limb(rng, start, end, r0, r1, segments, wobble):
    """A wandering path from start to end, its radius narrowing from r0 to r1."""
    along = end - start
    length = np.linalg.norm(along)
    axis = along / length
    side = np.cross(axis, [0, 1, 0])
    side = side / np.linalg.norm(side) if np.linalg.norm(side) > 1e-6 else np.array([1.0, 0, 0])
    up = np.cross(side, axis)
    points = [start]
    for i in range(1, segments):
        t = i / segments
        sway = np.sin(t * np.pi) * length * wobble
        points.append(start + along * t + side * rng.uniform(-1, 1) * sway + up * rng.uniform(-1, 1) * sway)
    points.append(end)
    return np.array(points), np.array([r0 + (r1 - r0) * i / segments for i in range(segments + 1)])


def log_model(number):
    seed, fork_x, bark, pointed = LOGS[number]
    rng = np.random.default_rng(seed)
    triangles, materials, classic_uv = classic(number)
    corners = triangles.reshape(-1, 3)
    normals, offsets = hull_planes(corners)
    lib = flora()
    # The root: the cut end the classic closes with a cap facing -x, or for a pointed log its flat end at +x.
    root_end = corners[:, 0].max() if pointed else corners[:, 0].min()
    root = corners[np.abs(corners[:, 0] - root_end) < .05]
    base = root.mean(0)
    radius = min(np.ptp(root[:, 1]), np.ptp(root[:, 2])) / 2
    near = corners[np.abs(corners[:, 0] - fork_x) < .45]
    fork = np.array([fork_x, near[:, 1].mean(), near[:, 2].mean()])
    fork_radius = min(radius, max(.05, min(np.ptp(near[:, 1]), np.ptp(near[:, 2])) / 2 * .9))
    # The trunk is capped at its root and where its limbs leave it, so no open ring shows between them.
    paths = [(*limb(rng, base, fork, radius, fork_radius, 4, .02), TRUNK_SIDES, ("base", "tip"))]
    if pointed:
        tip = corners[np.argmin(corners[:, 0])]
        paths.append((*limb(rng, fork, tip, fork_radius, .02, 2, 0), TRUNK_SIDES, ()))
    else:
        hull = corners[ConvexHull(corners).vertices]
        far = hull[hull[:, 0] > fork_x + .3]
        tips = []
        for point in far[np.argsort(-np.linalg.norm(far - fork, axis=1))]:
            if len(tips) < LIMBS and all(np.linalg.norm(point - other) > 1 for other in tips):
                tips.append(point)
        for k, tip in enumerate(tips):
            # The longest limb carries on the trunk; the others leave it thinner.
            share = .8 if k == 0 else rng.uniform(.45, .6)
            paths.append((*limb(rng, fork, tip, fork_radius * share, .03, 3, .06), LIMB_SIDES, ()))
    sides, caps = [], []
    for points, radii, count, ends in paths:
        branch = lib.Branch(list(points), list(radii), 0, count)
        p, n, uv, idx = lib.tube(branch, 2.0)
        p, n, uv = np.array(p), np.array(n), np.array(uv)
        for t in range(0, len(idx), 3):
            # generate-flora.py winds its tubes inward, which the Engine culls as their outside: each triangle is turned
            # to face the way its corners' normals point, as trees.py turns the mesh trees' bark.
            corner = list(idx[t:t + 3])
            if np.cross(p[corner[1]] - p[corner[0]], p[corner[2]] - p[corner[0]]) @ n[corner].sum(0) < 0:
                corner[1], corner[2] = corner[2], corner[1]
            sides.append((p[corner], n[corner], uv[corner]))
        for end in ends:
            cp, cn, cuv, cidx = lib.cap(branch, list(p), end)
            cp, cn = np.array(cp), np.array(cn)
            for t in range(0, len(cidx), 6):
                corner = list(cidx[t:t + 3])
                if np.cross(cp[corner[1]] - cp[corner[0]], cp[corner[2]] - cp[corner[0]]) @ cn[0] < 0:
                    corner[1], corner[2] = corner[2], corner[1]
                caps.append((cp[corner], cn[0]))
    every = into_hull(np.concatenate([c for c, _, _ in sides] + [c for c, _ in caps]), normals, offsets)
    sides = [(every[3 * i:3 * i + 3], n, uv) for i, (_, n, uv) in enumerate(sides)]
    caps = [(every[3 * len(sides) + 3 * i:3 * len(sides) + 3 * i + 3], n) for i, (_, n) in enumerate(caps)]
    tile = bark_steps(seed)
    groups = {}
    for corners_, n, uv in sides:
        group = groups.setdefault(bark, ([], [], []))
        group[0].extend(corners_); group[1].extend(n); group[2].extend(uv)
    for corners_, normal in caps:
        index = nearest_faces(corners_.mean(0)[None], triangles, normal[None])[0]
        group = groups.setdefault(materials[index], ([], [], []))
        group[0].extend(corners_); group[1].extend([normal] * 3)
        group[2].extend(classic_uvs(corners_, triangles[index], classic_uv[index]))
    primitives = []
    for name, (p, n, uv) in sorted(groups.items()):
        if name == bark:
            drawn = {other: png(paint(tile, ramp(surface(other)))) for other in drawn_as(bark)}
            primitives.append((name, png(paint(tile, ramp(surface(bark)))), drawn, p, n, uv))
        else:
            primitives.append((name, None, None, p, n, uv))
    folder = ART / "generated/models"
    folder.mkdir(parents=True, exist_ok=True)
    write_glb(folder / f"{number}.glb", primitives)
    print(f"{number}: log, {sum(len(p) for _, _, _, p, _, _ in primitives) // 3} triangles (classic {len(triangles)})")


# ---- nature flats ---------------------------------------------------------------------------------------------------

SPIRE = "spire"
# Of a nature rock's height, the share sunk under the ground, so it sits in it rather than on it.
SUNK = .06


def outline(rgba):
    """The sprite's drawn texels (alpha over half), cropped to them, top row first."""
    solid = rgba[..., 3] > 128
    rows, cols = np.nonzero(solid.any(1))[0], np.nonzero(solid.any(0))[0]
    return solid[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]


def silhouette_reach(solid, centre, directions, height):
    """How far along each direction from centre (metres, y up, the sprite `height` tall) the sprite's outline, given a depth
    equal to its width in each row, still holds the point."""
    rows, cols = solid.shape
    metre = height / rows
    widths = solid.sum(1) * metre
    lefts = np.argmax(solid, 1)
    reach = np.zeros(len(directions))
    steps = np.linspace(0, max(rows, cols) * metre * 1.5, 160)[1:]
    for i, d in enumerate(directions):
        last = 0.0
        for t in steps:
            x, y, z = centre + d * t
            row = rows - 1 - int(y / metre)
            col = int(x / metre)
            if row < 0 or row >= rows or col < 0 or col >= cols or not solid[row, col]:
                break
            middle = (lefts[row] + solid[row].sum() / 2) * metre
            if abs(z) > widths[row] / 2 * np.sqrt(max(0.0, 1 - ((x - middle) / max(widths[row] / 2, 1e-6)) ** 2)) + 1e-6:
                break
            last = t
        reach[i] = last
    return reach


def stalagmite(seed, rgba):
    """The placed cave decoration follows its painted taper, closed across any cutout pockets."""
    rng = np.random.default_rng(seed)
    solid = ndimage.binary_fill_holes(outline(rgba))
    height = 1.0
    sphere, faces = icosphere(2)
    rows = solid.shape[0]
    metre = height / rows
    lower = np.argwhere(solid[int(rows * .6):])
    want = np.array([rows - 1 - height * .12 / metre, np.median(lower[:, 1])])
    drawn = np.argwhere(solid)
    row, col = drawn[np.argmin(np.linalg.norm(drawn - want, axis=1))]
    centre = np.array([(col + .5) * metre, (rows - row - .5) * metre, 0])
    reach = silhouette_reach(solid, centre, sphere, height)
    points = centre + sphere * reach[:, None]
    points = press(points, faces, rng, 1, 0, facets=6, cut_depth=(.05, .15), tilt=.2, wobble=.01)
    points = points - [solid.shape[1] * metre / 2, height * SUNK, 0]
    unit, corner_normals = split_normals(points, faces)
    positions, normals, uvs = [], [], []
    tile_metres = TILE / TEXELS_PER_METRE / 3
    for f, face in enumerate(faces):
        corners = points[face]
        positions.extend(corners); normals.extend(corner_normals[f]); uvs.extend(projected_uvs(corners, unit[f], tile_metres))
    positions = np.array(positions)
    # The rock stands on its foot: whatever sinks under the ground is cut flat at it, and the ground shades its foot.
    positions[:, 1] = np.maximum(positions[:, 1], -height * SUNK)
    tone = .7 + .3 * np.clip((positions[:, 1] + height * SUNK) / (height * .35), 0, 1)
    count = len(positions)
    round4 = lambda values: [round(float(v), 4) for v in np.asarray(values).ravel()]
    mesh = {"height": round(float(positions[:, 1].max()), 4), "parts": [{
        "role": "stone", "positions": round4(positions), "normals": round4(normals), "uvs": round4(uvs),
        "colors": round4(np.stack([tone, tone, tone, np.zeros(count)], 1)), "indices": list(range(count))}]}
    return mesh, paint(stone_steps(seed), palette_of(rgba))


# A texel this bright in every channel is the sprites' snow, which the flora field's shader lays on a winter tree's
# leaves and a winter rock's stone instead.
SNOW = 170


def palette_of(rgba):
    """A sprite's colours as a stone's (palette): its drawn texels but its outline and its snow."""
    colours = rgba[..., :3][rgba[..., 3] > 128]
    light = colours @ [.2126, .7152, .0722]
    colours = colours[(light > 16) & (colours.min(1) < SNOW)]
    image = np.zeros((1, len(colours), 4), np.uint8)
    image[0, :, :3], image[0, :, 3] = colours, 255
    buffer = io.BytesIO()
    Image.fromarray(image, "RGBA").save(buffer, "PNG")
    buffer.seek(0)
    return palette(buffer)


# ---- placed fallen logs and woodpiles -------------------------------------------------------------------------------

LOGS_FORM = "logs"
# A heaped log's radius as a share of the heap's height, as thick as the sprites paint theirs (about 20 of a 68-pixel
# heap), and its broken limbs: stubs pointing up and out, more of them than a forest log's, as the sprites draw them.
LOG_RADIUS = .15
LOG_STUBS = {"count": [5, 8], "start": .1, "end": .95, "angle": [35, 75], "length": [.25, .55], "shrink": 0.0, "radius": .4,
             "sides": 4, "segments": 2, "wobble": .35, "rise": .25, "tip": .3}
# How deep a heaped log lies in the ground, as a share of its thickness: resting on it, where rusty-craftsurvive half
# buries a forest log in leaf litter.
LOG_SINK = .05


def turned_out(part):
    """generate-flora.py winds its tubes inward, which the Engine culls as their outside: each triangle is turned to face
    the way its corners' normals point."""
    p, n, idx = np.array(part["positions"]).reshape(-1, 3), np.array(part["normals"]).reshape(-1, 3), part["indices"]
    for t in range(0, len(idx), 3):
        a, b, c = idx[t:t + 3]
        if np.dot(np.cross(p[b] - p[a], p[c] - p[a]), n[a] + n[b] + n[c]) < 0:
            idx[t + 1], idx[t + 2] = c, b
    return part


def logs(seed, aspect):
    """Two fallen logs, one across the other, as the sprites heap them: generate-flora.py's logs with their broken limbs
    and cut ends, the second turned and laid on the first, each as long as makes the heap as wide for its height as its
    sprite; a prop mesh of one bark part."""
    lib = flora()
    rng = np.random.default_rng(seed)
    # The heap is about a metre high, one log on another and their stubs; crossed, each log spans about the sprite's width.
    length = aspect * 1.05
    lib.KINDS["heaped-log"] = lib.merged(lib.KINDS["log"], {"sink": LOG_SINK, "trunk": {"length": length, "radius": LOG_RADIUS},
                                                            "levels": [LOG_STUBS]})
    first, _, _, _, _ = lib.build("heaped-log", seed, 1.0)
    second, _, _, _, _ = lib.build("heaped-log", seed + 1, 1.0)
    # The logs cross in an X seen from the front, as the sprites paint them: each turned either way of the view's plane,
    # the second resting on the first.
    half = math.radians(rng.uniform(25, 35))
    lift = np.array([rng.uniform(-.1, .1) * length, LOG_RADIUS * 1.7, 0])
    out = {"positions": [], "normals": [], "uvs": [], "colors": [], "indices": []}
    for part, turn, raise_ in ((first[0], -half, np.zeros(3)), (second[0], half, lift)):
        spin = np.array([[math.cos(turn), 0, math.sin(turn)], [0, 1, 0], [-math.sin(turn), 0, math.cos(turn)]])
        p, n = np.array(part["positions"]).reshape(-1, 3), np.array(part["normals"]).reshape(-1, 3)
        p, n = p @ spin.T + raise_, n @ spin.T
        base = len(out["positions"]) // 3
        out["positions"] += [round(float(v), 4) for v in p.ravel()]
        out["normals"] += [round(float(v), 4) for v in n.ravel()]
        out["uvs"] += part["uvs"]; out["colors"] += part["colors"]
        out["indices"] += [base + i for i in part["indices"]]
    out["role"] = "bark"
    p = np.array(out["positions"]).reshape(-1, 3)
    return {"height": round(float(p[:, 1].max()), 4), "parts": [turned_out(out)]}


WOODPILE = "woodpile"
# A stacked woodpile, as the town flats 212-11 and 212-12 paint theirs: split logs a little over a metre long, side by
# side in rows of PILE_ROWS from the ground up, each row in the hollows of the one below and the logs' ends ragged; the
# stack is turned PILE_TURN degrees so its sawn ends face the viewer's right and its long side the left, as the sprites
# draw it. The logs are generate-flora.py's tubes and caps, as the heaped logs above are, lying at LOG_SINK.
PILE_ROWS = (5, 4, 5, 4)
PILE_RADIUS, PILE_LENGTH, PILE_RAGGED, PILE_TURN = .1, 1.05, .07, 45


def woodpile(seed):
    """A woodpile's two parts, each its corners' positions, normals and uvs, three corners a triangle: the logs' bark
    (uvs in the bark tile) and their sawn ends (each spanning the end tile once)."""
    lib = flora()
    rng = np.random.default_rng(seed)
    turn = math.radians(PILE_TURN)
    spin = np.array([[math.cos(turn), 0, -math.sin(turn)], [0, 1, 0], [math.sin(turn), 0, math.cos(turn)]])
    parts = {"bark": ([], [], []), "ends": ([], [], [])}
    step = 2 * PILE_RADIUS
    for row, count in enumerate(PILE_ROWS):
        # The top row is one log short at one end or the other, as a stack being burned down is.
        gone = int(rng.choice((0, count - 1))) if row == len(PILE_ROWS) - 1 else None
        for i in range(count):
            if i == gone:
                continue
            radius = PILE_RADIUS * rng.uniform(.8, 1.12)
            # Each log rests where a log of the stack's radius would, lower by as much as it is thinner.
            height = PILE_RADIUS * (1 - 2 * LOG_SINK) + row * step * math.sqrt(3) / 2 - (PILE_RADIUS - radius)
            across = (i - (count - 1) / 2) * step + rng.uniform(-.012, .012)
            drift = rng.uniform(-.02, .02)
            ends = (-PILE_LENGTH / 2 + rng.uniform(-PILE_RAGGED, PILE_RAGGED), PILE_LENGTH / 2 + rng.uniform(-PILE_RAGGED, PILE_RAGGED))
            branch = lib.Branch([np.array([ends[0], height, across - drift]), np.array([ends[1], height, across + drift])],
                                [radius, radius * rng.uniform(.92, 1)], 0, int(rng.integers(5, 8)))
            p, n, uv, idx = lib.tube(branch, 2.0)
            p, n, uv = np.array(p), np.array(n), np.array(uv)
            for t in range(0, len(idx), 3):
                corner = list(idx[t:t + 3])
                if np.cross(p[corner[1]] - p[corner[0]], p[corner[2]] - p[corner[0]]) @ n[corner].sum(0) < 0:
                    corner[1], corner[2] = corner[2], corner[1]
                parts["bark"][0].extend(p[corner] @ spin.T); parts["bark"][1].extend(n[corner] @ spin.T); parts["bark"][2].extend(uv[corner])
            rim = [(.5, .5)] + [(.5 + .47 * math.cos(2 * math.pi * j / branch.sides), .5 + .47 * math.sin(2 * math.pi * j / branch.sides))
                                for j in range(branch.sides)]
            for end in ("base", "tip"):
                cp, cn, _, cidx = lib.cap(branch, list(p), end)
                cp, normal = np.array(cp), np.array(cn[0])
                for t in range(0, len(cidx), 6):
                    corner = list(cidx[t:t + 3])
                    if np.cross(cp[corner[1]] - cp[corner[0]], cp[corner[2]] - cp[corner[0]]) @ normal < 0:
                        corner[1], corner[2] = corner[2], corner[1]
                    parts["ends"][0].extend(cp[corner] @ spin.T); parts["ends"][1].extend([normal @ spin.T] * 3)
                    parts["ends"][2].extend(rim[c] for c in corner)
    return {role: tuple(np.array(values) for values in part) for role, part in parts.items()}


# ---- placed flats ---------------------------------------------------------------------------------------------------

# The decoration flats blocks place that are rock or deadwood, drawn as meshes in the sprites' place (the art pack's
# `meshes` section, scaled to each sprite's width): the fallen logs towns lay about, the woodpiles stacked in their
# streets, and the dungeons' stalagmites.
PLACED = {(213, 11): (LOGS_FORM, 1), (213, 12): (LOGS_FORM, 2), (212, 11): (WOODPILE, 1), (212, 12): (WOODPILE, 2),
          **{(300, record): (SPIRE, 1) for record in range(6, 16)}}
PAYLOAD = CONTENT / "worldrpg/packs/daggerfall.art-replacements/payload.json"


def sprite(archive, record):
    """A flat's refreshed sprite, its first frame, whose colours a mesh standing in for it is painted in, as trees.py paints
    the mesh trees."""
    flats = json.loads(PAYLOAD.read_text())["artReplacements"]["flats"]
    flat = next((f for f in flats if (f["replaces"]["archive"], f["replaces"]["record"]) == (archive, record)), None)
    if flat is None:
        raise SystemExit(f"{archive}-{record}: the art pack has no refreshed sprite to paint it from")
    f = flat["frames"][0]
    image = Image.open(CONTENT / flat["path"]).convert("RGBA")
    return np.asarray(image.crop((f["x"], f["y"], f["x"] + f["width"], f["y"] + f["height"])))


def wood(rgba):
    """Which of a sprite's texels are its wood: those with no more green than red, where its moss or leaves would turn a
    bark ramp olive. trees.py takes a mesh tree's trunk colours by it, and a woodpile its bark's."""
    return rgba[..., 1] <= rgba[..., 0]


def bark_colours(rgba):
    """A sprite's wood as a bark ramp: its drawn texels but its outline, in five bands by brightness."""
    colours = rgba[..., :3][rgba[..., 3] > 128]
    colours = colours[(colours @ [.2126, .7152, .0722]) > 16]
    colours = colours[np.argsort(colours @ [.2126, .7152, .0722])]
    return np.array([np.median(band, axis=0) for band in np.array_split(colours, 5)]), None


def placed(archive, record):
    """A placed flat's mesh as a GLB, which the game draws at its sprite's width from the GLB's own width across (x)."""
    form, variant = PLACED[(archive, record)]
    rgba = sprite(archive, record)
    seed = archive * 100 + record
    if form == WOODPILE:
        # Painted in the sprite's wood alone: its moss would turn the whole bark olive.
        colours = bark_colours(np.where(wood(rgba)[..., None], rgba, 0))
        tiles = {"bark": bark_steps(seed), "ends": end_steps(seed)}
        primitives = [(role, png(paint(tiles[role], colours)), None, p, n, uv) for role, (p, n, uv) in woodpile(variant).items()]
    else:
        if form == LOGS_FORM:
            solid = outline(rgba)
            mesh = logs(variant, solid.shape[1] / solid.shape[0])
            texture = png(paint(bark_steps(seed), bark_colours(rgba)))
        else:
            mesh, stone = stalagmite(seed, rgba)
            texture = png(stone)
        part = mesh["parts"][0]
        p, n = np.array(part["positions"]).reshape(-1, 3), np.array(part["normals"]).reshape(-1, 3)
        uv, idx = np.array(part["uvs"]).reshape(-1, 2), np.array(part["indices"])
        primitives = [(part["role"], texture, None, p[idx], n[idx], uv[idx])]
    folder = ART / "generated/effects"
    folder.mkdir(parents=True, exist_ok=True)
    name = f"{({LOGS_FORM: 'logs', WOODPILE: 'woodpile'}).get(form, 'stalagmite')}-{archive}-{record}.glb"
    write_glb(folder / name, primitives)
    corners = np.concatenate([primitive[3] for primitive in primitives])
    print(f"{archive}-{record}: {name}, {len(corners) // 3} triangles, {float(np.ptp(corners[:, 0])):.3f} m across")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("what", choices=("models", "placed"))
    parser.add_argument("ids", nargs="*", help="ARCH3D ids, or a placed flat's ARCHIVE-RECORD")
    a = parser.parse_args()
    if a.what == "models":
        for number in map(int, a.ids) if a.ids else [*MODELS, *LOGS]:
            (log_model if number in LOGS else model)(number)
    else:
        for key in [tuple(map(int, i.split("-"))) for i in a.ids] or PLACED:
            placed(*key)


if __name__ == "__main__":
    main()
