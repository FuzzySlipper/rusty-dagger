#!/usr/bin/env python3
"""Offline heightmap synthesis for interior and dungeon surfaces (rusty-dagger #11067).

Produces seamless [0, 1] heightmaps tuned for both inward voxel relief (`interior-relief.wgsl`)
and outward silhouette-POM (`interior-extrude.wgsl`, default cutoff 0.35):
  - 0.00..0.18 : true mortar joints, plank cracks, dark openings, recessed glass/niches
  - 0.22..0.32 : flat background wall planes (plaster behind proud timber, painting canvases)
  - 0.42..0.72 : architectural relief bodies (ashlar blocks, bricks, wood boards, flagstones)
  - 0.75..1.00 : proud sculptural & structural features (carved lions, beams, picture frames)

Loads DeepBump's ONNX model once in-process when available (asset-pipeline's venv), and falls
back cleanly to `relief.deepbump_height` or pure-NumPy shading so unit tests run without ONNX.
"""
import json
import sys
from pathlib import Path

import numpy as np
from scipy import ndimage

HERE = Path(__file__).resolve().parent
DEEPBUMP_DIR = Path.home() / "dev/asset-pipeline/.tools/DeepBump"
INTERIOR_RECIPES = json.loads((HERE / "interior-recipes.json").read_text())

_DB_SESSION = None
_DB_FAILED = False


def _linear(srgb):
    return np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)


def _lightness(rgb):
    return _linear(rgb) @ np.array([0.2126, 0.7152, 0.0722])


def _blur(values, sigma):
    return ndimage.gaussian_filter(values, sigma, mode="wrap")


def _wrapped_distance(outside):
    h, w = outside.shape
    return ndimage.distance_transform_edt(np.tile(outside, (3, 3)))[h:2 * h, w:2 * w]


def _normalize01(x, lo_q=0.02, hi_q=0.98):
    lo, hi = np.quantile(x, lo_q), np.quantile(x, hi_q)
    if hi - lo < 1e-6:
        return np.full_like(x, 0.5, dtype=np.float64)
    return np.clip((x - lo) / (hi - lo), 0.0, 1.0)


def _clean_mortar(light, share=0.60, run=10, low_q=0.04):
    dark = (light < _blur(light, 2.5) * share) | (light < min(0.012, float(np.quantile(light, low_q))))
    labels, count = ndimage.label(np.tile(dark, (3, 3)))
    sizes = ndimage.sum(np.ones_like(labels), labels, range(count + 1))
    h, w = dark.shape
    return (sizes[labels] >= run)[h:2 * h, w:2 * w] & dark


def _kmeans_regions(rgb, mask, count, smooth=1.2, spread=0.8, seed=11067):
    if spread:
        rgb_s = np.stack([_blur(rgb[..., c], spread) for c in range(3)], -1)
    else:
        rgb_s = rgb
    lab = np.concatenate([_linear(rgb_s) * 2.0, _lightness(rgb_s)[..., None] * 3.0], -1)
    points = lab[mask]
    if len(points) < count:
        return np.zeros(mask.shape, dtype=int)
    rng = np.random.default_rng(seed)
    centres = points[rng.choice(len(points), count, replace=False)]
    for _ in range(25):
        nearest = np.argmin(((points[:, None] - centres[None]) ** 2).sum(-1), 1)
        centres = np.array([
            points[nearest == c].mean(0) if (nearest == c).any() else centres[c]
            for c in range(count)
        ])
    order = np.argsort(centres[:, -1])
    nearest = np.argmin(((lab[..., None, :] - centres[order][None, None]) ** 2).sum(-1), -1)
    votes = np.stack([_blur((nearest == c).astype(float) * mask, smooth) for c in range(count)], -1)
    return np.argmax(votes, -1)


def _box_mask(shape, box):
    h, w = shape
    x0, y0, x1, y1 = box
    m = np.zeros((h, w), dtype=bool)
    m[max(0, y0):min(h, y1), max(0, x0):min(w, x1)] = True
    return m


def _boxes_mask(shape, spec):
    m = np.zeros(shape, dtype=bool)
    if "recessBox" in spec:
        m |= _box_mask(shape, spec["recessBox"])
    for box in spec.get("recessBoxes", []):
        m |= _box_mask(shape, box)
    return m


def deepbump_cached(rgb):
    """Seamless [0, 1] sculptural height via cached in-process ONNX DeepBump when available."""
    global _DB_SESSION, _DB_FAILED
    if _DB_FAILED:
        return _normalize01(_lightness(rgb), 0.04, 0.96)
    try:
        if _DB_SESSION is None:
            if str(DEEPBUMP_DIR) not in sys.path:
                sys.path.insert(0, str(DEEPBUMP_DIR))
            import onnxruntime as ort
            import utils_inference
            import module_normals_to_height
            model_path = DEEPBUMP_DIR / "deepbump256.onnx"
            session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
            _DB_SESSION = (session, utils_inference, module_normals_to_height)

        session, utils_inference, mod_h = _DB_SESSION
        chw = np.transpose(rgb.astype(np.float32), (2, 0, 1))
        img = np.mean(chw, axis=0, keepdims=True)
        tiles, paddings = utils_inference.tiles_split(img, (256, 256), (16, 16), "LARGE")
        pred = utils_inference.tiles_infer(tiles, session)
        normals = utils_inference.tiles_merge(pred, (256, 256), (16, 16), paddings, "LARGE")
        normals = normals.astype(np.float32)
        h = np.asarray(mod_h.apply(normals, True, None), np.float64)
        if h.ndim == 3:
            h = h[0]
        return (h - h.min()) / max(np.ptp(h), 1e-6)
    except Exception:
        _DB_FAILED = True
        return _normalize01(_lightness(rgb), 0.04, 0.96)


def role_of(archive, record):
    """Resolves an interior/dungeon record's merged role dict, or None if not in interior-recipes.json."""
    entry = INTERIOR_RECIPES["archives"].get(str(archive))
    if entry is None:
        return None
    chosen = entry.get(str(record), entry.get("*"))
    if chosen is None:
        return None
    if isinstance(chosen, str):
        chosen = {"role": chosen}
    base = INTERIOR_RECIPES["roles"][chosen["role"]]
    return {"name": chosen["role"]} | base | {k: v for k, v in chosen.items() if k != "role"}


def _build_stone_or_brick(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.58), spec.get("run", 10)) | (light < 0.008)
    rise = np.clip(_wrapped_distance(~dark) / spec.get("bevel", 2.4), 0.0, 1.0)
    rise = 1.0 - (1.0 - rise) ** 2
    db_n = _normalize01(db, 0.04, 0.97)
    floor_bias = spec.get("floorBias", 0.44)
    ceiling_cap = spec.get("ceilingCap", 1.0)
    h = (floor_bias + (ceiling_cap - floor_bias) * db_n) * rise
    h[dark] = 0.0
    reg = _kmeans_regions(rgb, ~dark, 3, smooth=1.0, spread=0.6)
    return np.clip(h / max(h.max(), 1e-6) * ceiling_cap, 0.0, 1.0), reg


def _build_timber_plaster(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.58), spec.get("run", 8), low_q=0.03) | (light < 0.006)
    reg = _kmeans_regions(rgb, ~dark, 3, smooth=spec.get("smooth", 1.4), spread=spec.get("spread", 1.2))
    proud_regions = tuple(spec.get("proudRegions", [0]))
    proud_raw = np.isin(reg, proud_regions)
    # Remove isolated <3px dark smudges inside plaster panels, and fill tiny grain dark lines inside solid timber!
    proud = ndimage.binary_opening(proud_raw, structure=np.ones((3, 3)))
    proud = ndimage.binary_closing(proud, structure=np.ones((3, 3)))
    beam_rise = np.clip(_wrapped_distance(proud) / spec.get("bevel", 1.8), 0.0, 1.0)
    beam_rise = 1.0 - (1.0 - beam_rise) ** 2
    seam_rise = np.clip(_wrapped_distance(~dark) / 1.5, 0.0, 1.0)
    db_n = _normalize01(db, 0.05, 0.95)
    levels = np.array(spec.get("levels", [0.96, 0.30, 0.26]), dtype=np.float64)
    face = levels[np.clip(reg, 0, len(levels) - 1)]
    # Solid oak beams stay cohesive (wood grain / bolts recess gently to ~0.52 rather than slicing 0.0 air gaps);
    # plaster panels stay cleanly recessed at 0.26..0.30 below the 0.35 outward extrude cutoff.
    beam_h = np.where(dark, 0.50 + 0.14 * beam_rise, (0.74 + 0.26 * db_n) * (0.50 + 0.50 * beam_rise))
    plaster_h = np.where(dark, 0.0, face * seam_rise + (db_n - 0.5) * 0.04)
    h = np.where(proud, beam_h, plaster_h)
    return np.clip(h, 0.0, 1.0), reg


def _detect_window_glass(rgb, light):
    h, w = light.shape
    yy, xx = np.mgrid[0:h, 0:w]
    inner = (xx >= 10) & (xx < w - 10) & (yy >= 8) & (yy < h - 28)
    blue = inner & ((rgb[..., 2] - rgb[..., 0]) > 0.05) & (rgb[..., 2] > 0.14)
    seed = ndimage.binary_opening(blue, structure=np.ones((2, 2)))
    reg = ndimage.binary_closing(seed, structure=np.ones((7, 7)))
    reg = ndimage.binary_fill_holes(reg) & inner
    lbl, cnt = ndimage.label(reg)
    if cnt > 0:
        sizes = ndimage.sum(np.ones_like(lbl), lbl, range(cnt + 1))
        best = int(np.argmax(sizes[1:]) + 1)
        if 120 <= sizes[best] <= (h * w * 0.48):
            return lbl == best
    dark_hole = inner & (light < 0.025)
    dreg = ndimage.binary_closing(ndimage.binary_opening(dark_hole, structure=np.ones((3, 3))), structure=np.ones((5, 5)))
    dreg = ndimage.binary_fill_holes(dreg) & inner
    lbl, cnt = ndimage.label(dreg)
    if cnt > 0:
        sizes = ndimage.sum(np.ones_like(lbl), lbl, range(cnt + 1))
        best = int(np.argmax(sizes[1:]) + 1)
        if 100 <= sizes[best] <= (h * w * 0.45):
            return lbl == best
    return np.zeros((h, w), dtype=bool)


def _build_window_wall(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.60), spec.get("run", 8), low_q=0.03) | (light < 0.006)
    glass = _boxes_mask(light.shape, spec) if ("recessBox" in spec or "recessBoxes" in spec) else _detect_window_glass(rgb, light)
    wall_mask = ~dark & ~glass
    reg = _kmeans_regions(rgb, wall_mask, 3, smooth=1.2, spread=0.9)
    levels = np.array(spec.get("levels", [0.94, 0.64, 0.48]), dtype=np.float64)
    face = levels[np.clip(reg, 0, len(levels) - 1)]
    rise = np.clip(_wrapped_distance(wall_mask) / spec.get("bevel", 1.8), 0.0, 1.0)
    rise = 1.0 - (1.0 - rise) ** 2
    db_n = _normalize01(db, 0.05, 0.96)
    wall_h = (face * 0.74 + db_n * 0.26) * rise
    # Glass panes sit deeply recessed (0.08..0.18), lead muntins subtle at 0.20 (both safely < 0.35!)
    glass_h = np.where(dark, 0.20, 0.08 + 0.10 * db_n)
    h = np.where(glass, glass_h, wall_h)
    h[dark & ~glass] = 0.0
    return np.clip(h / max(h.max(), 1e-6), 0.0, 1.0), np.where(glass, 3, reg)


def _build_hero_carving(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.56), spec.get("run", 8), low_q=0.04) | (light < 0.008)
    db_n = _normalize01(db, 0.03, 0.98)
    if "heroBox" in spec:
        hero = _box_mask(light.shape, spec["heroBox"])
        rise = np.clip(_wrapped_distance(~dark) / spec.get("bevel", 2.2), 0.0, 1.0)
        rise = 1.0 - (1.0 - rise) ** 2
        if spec.get("plasterBg"):
            bg_h, reg = _build_timber_plaster(rgb, db, spec)
            bg_h = np.minimum(bg_h, 0.85)
        else:
            bg_h = (0.42 + 0.24 * db_n) * rise
            bg_h[dark] = 0.0
            reg = np.zeros(light.shape, dtype=int)
        # Keep carved plaque / medallion as a solid cohesive relief (base 0.48, sculpted peaks up to 1.00)
        # so dark eye/mane creases sculpt smoothly on top of solid stone/bronze without hollow floating shards!
        hero_rise = np.clip(_wrapped_distance(hero) / 3.0, 0.0, 1.0)
        hero_rise = 1.0 - (1.0 - hero_rise) ** 2
        sculpt = 0.48 + 0.52 * np.power(db_n, 0.90)
        # Subtle carved crevices (never punched to 0 air) preserve crisp sculptural shadow inside mane/features
        hero_h = np.where(dark, np.maximum(0.42, sculpt * 0.72), sculpt) * (0.55 + 0.45 * hero_rise)
        h = np.where(hero, np.maximum(bg_h, hero_h), bg_h)
        return np.clip(h / max(h.max(), 1e-6), 0.0, 1.0), np.where(hero, 2, reg)
    hp = light - _blur(light, 3.0)
    hp_n = np.clip(hp / (hp.std() * 3.5 + 1e-6), -0.5, 0.5)
    h = np.clip(0.32 + 0.58 * db_n + 0.24 * hp_n, 0.0, 1.0)
    h[dark] = 0.0
    return np.clip(h / max(h.max(), 1e-6), 0.0, 1.0), np.zeros(light.shape, dtype=int)


def _build_niche_shelf(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.60), spec.get("run", 8), low_q=0.04) | (light < 0.008)
    if "recessBox" in spec or "recessBoxes" in spec:
        niche = _boxes_mask(light.shape, spec)
    else:
        very_dark = light < 0.032
        niche = ndimage.binary_opening(very_dark, structure=np.ones((5, 5)))
        niche = ndimage.binary_closing(niche, structure=np.ones((7, 7)))
        niche = ndimage.binary_fill_holes(niche)
    rise = np.clip(_wrapped_distance(~dark & ~niche) / spec.get("bevel", 2.2), 0.0, 1.0)
    rise = 1.0 - (1.0 - rise) ** 2
    db_n = _normalize01(db, 0.04, 0.97)
    stone_h = (0.52 + 0.38 * db_n) * rise
    # Objects & cavity inside the niche stay cleanly recessed (<= 0.30, below 0.35 extrude cutoff!)
    niche_h = np.where(light < 0.025, 0.02, 0.14 + 0.15 * db_n)
    h = np.where(niche, niche_h, stone_h)
    h[dark & ~niche] = 0.0
    return np.clip(h / max(h.max(), 1e-6), 0.0, 1.0), np.where(niche, 3, 1)


def _build_bookshelf(rgb, db, spec):
    light = _lightness(rgb)
    h_px, w_px = light.shape
    very_dark = light < 0.032
    yy, xx = np.mgrid[0:h_px, 0:w_px]
    bw = spec.get("frameWidth", 5)
    outer_frame = (xx < bw) | (xx >= w_px - bw) | (yy < bw * 2) | (yy >= h_px - bw)
    row_luma = light.mean(1)
    shelf_rows = row_luma > np.quantile(row_luma, 0.72)
    top_arch = (yy < int(h_px * spec.get("topArchFrac", 0.24))) & (light > 0.06)
    frame = (outer_frame | shelf_rows[:, None] | top_arch) & (light > 0.025)
    if "recessBox" in spec or "recessBoxes" in spec:
        frame = frame & ~_boxes_mask(light.shape, spec)
    db_n = _normalize01(db, 0.05, 0.95)
    h = np.where(frame, 0.72 + 0.26 * db_n, np.where(very_dark, 0.02, 0.14 + 0.16 * db_n))
    return np.clip(h, 0.0, 1.0), np.where(frame, 2, 0)


def _build_framed_art(rgb, db, spec):
    h_px, w_px = rgb.shape[:2]
    bw = spec.get("frameWidth") or max(3, min(8, round(min(h_px, w_px) * 0.095)))
    yy, xx = np.mgrid[0:h_px, 0:w_px]
    dist_edge = np.minimum(np.minimum(xx, w_px - 1 - xx), np.minimum(yy, h_px - 1 - yy))
    frame_mask = dist_edge < bw
    mid = bw * 0.48
    mould = 1.0 - np.clip(np.abs(dist_edge - mid) / max(mid, 1.0), 0.0, 1.0)
    mould = 0.68 + 0.32 * (1.0 - (1.0 - mould) ** 2)
    db_n = _normalize01(db, 0.05, 0.95)
    rabbet = (dist_edge >= bw) & (dist_edge < bw + 1)
    h = np.where(frame_mask, np.clip(mould * (0.86 + 0.14 * db_n), 0.0, 1.0), np.where(rabbet, 0.14, 0.28))
    h[dist_edge == 0] = 0.20
    return h, np.where(frame_mask, 2, 0)


def _build_marble_tile(rgb, db, spec):
    light = _lightness(rgb)
    dark = _clean_mortar(light, spec.get("outline", 0.56), spec.get("run", 10), low_q=0.03) | (light < 0.008)
    rise = np.clip(_wrapped_distance(~dark) / spec.get("bevel", 1.8), 0.0, 1.0)
    rise = 1.0 - (1.0 - rise) ** 2
    db_n = _normalize01(db, 0.06, 0.94)
    h = (0.48 + 0.28 * db_n) * rise
    h[dark] = 0.0
    return np.clip(h, 0.0, 1.0), np.zeros(light.shape, dtype=int)


def _build_flat(rgb, _db, _spec):
    h_px, w_px = rgb.shape[:2]
    # Uniform 0.28: valid tallest > 0, zero outward extrusion (below 0.35 + extrude=0), zero cavity darkening.
    return np.full((h_px, w_px), 0.28, dtype=np.float64), np.zeros((h_px, w_px), dtype=int)


BUILDERS = {
    "dungeon-stone": _build_stone_or_brick,
    "sewer-brick": _build_stone_or_brick,
    "cave-rock": _build_stone_or_brick,
    "stone-floor": _build_stone_or_brick,
    "wood-floor": _build_stone_or_brick,
    "furniture": _build_stone_or_brick,
    "timber-plaster": _build_timber_plaster,
    "window-wall": _build_window_wall,
    "hero-carving": _build_hero_carving,
    "niche-shelf": _build_niche_shelf,
    "bookshelf": _build_bookshelf,
    "framed-art": _build_framed_art,
    "marble-tile": _build_marble_tile,
    "flat": _build_flat,
}


def interior_height(rgb, role, db=None):
    """Computes (height [0..1], regions, kind, depth_or_none, extrude_metres) for one interior/dungeon record."""
    kind = role["name"]
    builder = BUILDERS[kind]
    if db is None:
        db = np.full(rgb.shape[:2], 0.5, dtype=np.float64) if kind == "flat" else deepbump_cached(rgb)
    h, region = builder(rgb, db, role)
    return h, region, kind, role.get("depth"), role.get("extrude")
