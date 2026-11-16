#!/usr/bin/env python3
"""Make the refreshed building surfaces' relief (rusty-dagger #10859) and record it in the art set's one home for a
classic texture record's texel heights, `artReplacements.groundCover.relief`.

Near the camera a location's walls, doors and roofs march the view ray into their record's heights (`relief.wgsl`, the
pack's `groundCover.surfaceShader`), so each texel stands as a small column under the classic face, and fade to the flat
surface with distance. A record's relief is its refreshed texture with each texel's height as alpha, 1 at the face and 0
at the floor, as every relief entry is, so a remodel could stack it in shells as it stacks the hedges'. A building's adds
`depth`, how far under the face its floor lies, in metres: only an entry with a depth draws on the location's faces.

The heights come from the texture itself, texel for texel, by its record's recipe (`recipes.json`): its sets, each with
its summer archives in every climate (0xx desert, 1xx mountain, 3xx temperate, 4xx swamp) and the winter one above each,
of which a record is covered where a town's exterior blocks draw it (the import's placements, as surfaces/tiling.py reads
them), and for each archive the role each record plays. Climates draw different pictures in one set, so a recipe names the
archive, not the set. A role says:

- `depth`: the floor's depth under the face, metres, before the player's strength;
- `outline`: how much darker than its neighbourhood a texel is to be an outline, mortar or seam, which are the floor;
- `run`: how many joined texels an outline takes at least, so a stone's dark speckles stay on its face;
- `bevel`: how many texels a shape rises over from its outline to its face, so stones and planks read pillowed;
- `levels`: the face's level for each colour region, darkest to lightest (k-means of the texels inside the outlines), so
  a quoin stands proud of its plaster and a bare patch sits under the render; or `courses`, for shingles and tiles, each
  course rising from the course line above to its own lower edge;
- `detail`: how much of the texture's fine detail roughens a face (none for stone, which reads busy at grazing angles);
- `smooth`: how far a lone texel takes its neighbours' region;
- `blur`: how far colours are smoothed before they are split into regions, so a plaster's grain doesn't split its render
  and only its patches stand apart.

Openings, texels near black (a window's glass, an arrow slit), are the floor whatever their role.

An entry may override a role's values (`{"role": "frame", "levels": [...]}`). A winter record whose classic is its
summer's under snow (`reduce.snowed`, as `surfaces/batch.py` draws it) takes its summer's height with the snow lying
proud; a winter the classic redrew takes its own archive's recipe. A covered record with no recipe takes DeepBump's
height from its texture (asset-pipeline's `pbr-material` install), the fallback #10854 measured weaker on beams and
shingles than a recipe.

Every filter wraps, so the heights tile as their textures do. The tool writes only its own entries (files under
`media/relief/`), leaving the ground's and the remodels' relief as groundcover.py made it, and hashes the shader, written
by hand beside the reliefs, into `groundCover.surfaceShader`.

    relief.py KEY...            make and record these, KEY an archive (every covered record of it) or ARCHIVE-RECORD
    relief.py --all             every covered record
    relief.py --look KEY...     only draw the check sheets

Each run draws a check sheet per archive under local/art/review/relief/: per record the texture, its regions, its height
and the height lit by a raking sun, two texels to a pixel.
"""
import argparse, hashlib, json, subprocess, sys, tempfile
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[2]
sys.path.insert(0, str(HERE.parent / "surfaces"))
import reduce, tiling  # noqa: E402,E401

CONTENT = REPOSITORY / "content"
PACK = "worldrpg/packs/daggerfall.art-replacements"
PAYLOAD = CONTENT / PACK / "payload.json"
SURFACES = f"{PACK}/media/surfaces"
MEDIA = f"{PACK}/media/relief"
SHADER = "relief.wgsl"
REFERENCE = REPOSITORY / "local/art/reference/surfaces"
SHEETS = REPOSITORY / "local/art/review/relief"
DEEPBUMP = Path.home() / "dev/asset-pipeline/.tools"
RECIPES = json.loads((HERE / "recipes.json").read_text())
# How a building's relief is drawn if a remodel ever stacks it in shells, as the low walls' masonry is (groundcover.py's
# LOOKS): still, its hollows in shade, no glints, never buried by snow.
LOOK = {"sway": 0, "shade": [.55, .55, .55], "glints": 0, "sinksUnderSnow": False}
SEED = 10859
# Linear lightness under which a texel is an opening, the floor: a window's glass, an arrow slit.
OPENING = .006


def linear(srgb):
    return np.where(srgb <= .04045, srgb / 12.92, ((srgb + .055) / 1.055) ** 2.4)


def lightness(rgb):
    return linear(rgb) @ [.2126, .7152, .0722]


def blur(values, sigma):
    return ndimage.gaussian_filter(values, sigma, mode="wrap")


def wrapped_distance(outside):
    """Each texel's distance to the nearest texel where `outside` is False, wrapping round the edges."""
    h, w = outside.shape
    return ndimage.distance_transform_edt(np.tile(outside, (3, 3)))[h:2 * h, w:2 * w]


def outlines(light, share, run=3):
    """Texels clearly darker than their neighbourhood, or among the darkest, in runs of at least `run` texels."""
    dark = (light < blur(light, 2.5) * share) | (light < np.quantile(light, .05))
    labels, count = ndimage.label(np.tile(dark, (3, 3)))
    sizes = ndimage.sum(np.ones_like(labels), labels, range(count + 1))
    h, w = dark.shape
    return (sizes[labels] >= run)[h:2 * h, w:2 * w] & dark


def regions(rgb, mask, count, smooth, spread=0):
    """Splits the texels under `mask` into `count` colour regions (k-means of their colours, smoothed by `spread`
    texels), numbered darkest to lightest, smoothed so a lone texel takes its neighbours' region; texels outside the mask
    take their nearest region too."""
    if spread:
        rgb = np.stack([blur(rgb[..., c], spread) for c in range(3)], -1)
    lab = np.concatenate([linear(rgb) * 2, lightness(rgb)[..., None] * 3], -1)
    points = lab[mask]
    rng = np.random.default_rng(SEED)
    centres = points[rng.choice(len(points), count, replace=False)]
    for _ in range(30):
        nearest = np.argmin(((points[:, None] - centres[None]) ** 2).sum(-1), 1)
        centres = np.array([points[nearest == c].mean(0) if (nearest == c).any() else centres[c] for c in range(count)])
    order = np.argsort(centres[:, -1])
    nearest = np.argmin(((lab[..., None, :] - centres[order][None, None]) ** 2).sum(-1), -1)
    votes = np.stack([blur((nearest == c).astype(float) * mask, smooth) for c in range(count)], -1)
    return np.argmax(votes, -1)


def courses(light):
    """A course's rise, by rows: from 0 under the course line above to 1 at its own lower edge. The course lines are the
    rows clearly darker than their neighbours, where the course above shades the one below."""
    profile = light.mean(1)
    line = profile < ndimage.gaussian_filter1d(profile, 6, mode="wrap") * .9
    rows = np.flatnonzero(line)
    h = len(profile)
    rise = np.full(h, .7)
    if rows.size:
        for y in range(h):
            above, below = (y - rows) % h, (rows - y) % h
            up = above[above > 0].min(initial=h)
            rise[y] = 0 if line[y] else up / max(up + below[below > 0].min(initial=h), 1)
    return np.repeat(rise[:, None], light.shape[1], 1)


def recipe_height(rgb, role):
    """A texture's heights by a role: (height, colour regions)."""
    light = lightness(rgb)
    dark = outlines(light, role["outline"], role.get("run", 3)) | (light < OPENING)
    rise = np.clip(wrapped_distance(~dark) / role["bevel"], 0, 1)
    rise = 1 - (1 - rise) ** 2
    if role.get("courses"):
        region = np.zeros(light.shape, int)
        face = .3 + .7 * courses(light)
    else:
        levels = np.array(role["levels"])
        region = regions(rgb, ~dark, len(levels), role.get("smooth", .8), role.get("blur", 0))
        face = levels[region]
    detail = light - blur(light, 1.2)
    detail = np.clip(detail / (detail.std() * 4 + 1e-6), -.5, .5)
    h = face * rise + role.get("detail", 0) * detail * rise
    h[dark] = 0
    return np.clip(h / max(h.max(), 1e-6), 0, 1), region


def deepbump_height(rgb):
    """DeepBump's height from a texture's colours (colour to normals, then normals to a seamless height)."""
    with tempfile.TemporaryDirectory() as work:
        colour, normals, height = (Path(work) / name for name in ("colour.png", "normals.png", "height.png"))
        Image.fromarray((rgb * 255 + .5).astype(np.uint8)).save(colour)
        for source, target, module, *extra in ((colour, normals, "color_to_normals", "--color_to_normals-overlap", "LARGE"),
                                               (normals, height, "normals_to_height", "--normals_to_height-seamless", "TRUE")):
            run = subprocess.run([DEEPBUMP / "venv/bin/python", DEEPBUMP / "DeepBump/cli.py", source, target, module, *extra],
                                 cwd=DEEPBUMP / "DeepBump", capture_output=True, text=True)
            if run.returncode:
                raise SystemExit(f"DeepBump {module}: {(run.stderr or run.stdout).strip()}")
        h = np.asarray(Image.open(height).convert("L"), np.float64) / 255
    h = (h - h.min()) / max(np.ptp(h), 1e-6)
    return h, np.zeros(h.shape, int)


def name(archive, record):
    return f"texture-{archive:03d}-{record}.png"


def refreshed(archive, record):
    return np.asarray(Image.open(CONTENT / SURFACES / name(archive, record)).convert("RGB"), np.float64) / 255


def classic(archive, record):
    path = next(REFERENCE.glob(f"*/texture-{archive:03d}/surface/texture-{archive:03d}-record-{record:03d}-frame-000-source.png"), None)
    return Image.open(path).convert("RGB") if path else None


def winter_of(archive):
    """Whether an archive is its set's winter: its reference set is a `-snow` one."""
    return next(REFERENCE.glob(f"*-snow/texture-{archive:03d}"), None) is not None


def snow_over(archive, record):
    """The refreshed snow mask of a winter record that is its summer under snow, as batch.py drew it (reduce.winter: the
    classic winter's snow laid over the refreshed summer, pixels doubled square), or None where it is not."""
    if not winter_of(archive) or not (CONTENT / SURFACES / name(archive - 1, record)).exists():
        return None
    summer, winter = classic(archive - 1, record), classic(archive, record)
    if summer is None or winter is None or not reduce.snowed(summer, winter):
        return None
    s, w = np.asarray(summer, np.int16), np.asarray(winter, np.int16)
    snow = (w.min(-1) - s.min(-1)) > reduce.SNOW
    texture = refreshed(archive, record)
    rows = np.arange(texture.shape[0]) * s.shape[0] // texture.shape[0]
    columns = np.arange(texture.shape[1]) * s.shape[1] // texture.shape[1]
    mask = snow[rows][:, columns]
    # The winter must be that summer where it lies bare, or it was drawn apart and takes its own recipe; one buried
    # whole, as a snowed roof is, shows nothing of its summer and takes its own too.
    bare = ~mask
    if not bare.any():
        return None
    same = np.abs(texture - refreshed(archive - 1, record)).max(-1)[bare] <= 2 / 255
    return mask if same.mean() >= .95 else None


def role_of(archive, record):
    """The record's role with its archive's overrides, or None where its archive has no recipe for it."""
    entry = RECIPES["archives"].get(str(archive), {})
    chosen = entry.get(str(record), entry.get("*"))
    if chosen is None:
        return None
    if isinstance(chosen, str):
        chosen = {"role": chosen}
    return {"name": chosen["role"]} | RECIPES["roles"][chosen["role"]] | {k: v for k, v in chosen.items() if k != "role"}


def height(archive, record):
    """(height, regions, how it was made, depth in metres) for one record."""
    if (snow := snow_over(archive, record)) is not None:
        summer, region, kind, depth = height(archive - 1, record)
        rise = np.clip(wrapped_distance(snow) / 1.5, 0, 1)
        return np.maximum(summer, np.where(snow, .85 + .15 * rise, 0)), np.where(snow, region.max() + 1, region), f"{kind}, snowed", depth
    rgb = refreshed(archive, record)
    if (role := role_of(archive, record)) is not None:
        h, region = recipe_height(rgb, role)
        return h, region, role["name"], role["depth"]
    h, region = deepbump_height(rgb)
    return h, region, "deepbump", RECIPES["deepbumpDepth"]


def outdoors():
    """Every record a town's exterior blocks draw, as the import publishes them (tiling.py): each placed model's records,
    in its base archive and every archive the climate and season swaps draw it as."""
    planes = tiling.meshes()
    swaps, _ = tiling.reach()
    return {(drawn, record) for _, kind, mesh in tiling.placements() if kind == "rmbExterior"
            for archive, record, _, _ in planes.get(mesh, []) for drawn in {archive} | swaps.get((archive, record), set())}


def covered():
    """Every covered record: each refreshed surface of a covered set's archives, summer and winter, in every climate,
    that a town's exterior blocks draw."""
    archives = {climate + offset + set_ for set_ in RECIPES["sets"] for climate in (0, 100, 300, 400) for offset in (0, 1)}
    found = sorted((int(path.stem.split("-")[1]), int(path.stem.split("-")[2])) for path in (CONTENT / SURFACES).glob("texture-*.png"))
    drawn = outdoors()
    return [(archive, record) for archive, record in found if (archive, record) in drawn and archive in archives
            and (archive % 100 in RECIPES["sets"] or winter_of(archive))]


def chosen(keys):
    every = covered()
    picked = []
    for key in keys:
        parts = [int(part) for part in key.split("-")]
        matched = [r for r in every if r[0] == parts[0] and (len(parts) == 1 or r[1] == parts[1])]
        if not matched:
            raise SystemExit(f"{key} is no covered record: its archive is not in a covered set, or it has no refreshed surface.")
        picked += [r for r in matched if r not in picked]
    return picked


def shading(h, depth_texels=3.0, sun=(-.6, .5, .62)):
    """The height lit by a raking sun from the upper left, for the sheet."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * .5 * depth_texels
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * .5 * depth_texels
    n = np.stack([-gx, gy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return np.clip(n @ (np.array(sun) / np.linalg.norm(sun)), 0, 1)


def sheet(archive, made):
    """Per record the texture, its regions, its height and the height lit, two texels to a pixel."""
    palette = np.array([[.1, .1, .4], [.2, .6, .3], [.9, .7, .2], [.8, .3, .3], [.9, .9, 1]])
    rows = []
    for record, (h, region, _, _) in sorted(made.items()):
        rgb = refreshed(archive, record)
        shown = palette[np.clip(region, 0, len(palette) - 1)] * (h[..., None] > 0)
        lit = np.clip(rgb * (.35 + .9 * shading(h)[..., None]), 0, 1)
        gap = np.full((rgb.shape[0], 2, 3), .15)
        rows.append(np.concatenate([rgb, gap, shown, gap, np.repeat(h[..., None], 3, -1), gap, lit], 1))
    width = max(r.shape[1] for r in rows)
    canvas = np.full((sum(r.shape[0] + 4 for r in rows), width, 3), .15)
    y = 0
    for r in rows:
        canvas[y:y + r.shape[0], :r.shape[1]] = r
        y += r.shape[0] + 4
    SHEETS.mkdir(parents=True, exist_ok=True)
    path = SHEETS / f"{archive:03d}.png"
    Image.fromarray(np.repeat(np.repeat((canvas * 255 + .5).astype(np.uint8), 2, 0), 2, 1)).save(path)
    return path


def hashed(path, data=None):
    data = data if data is not None else (CONTENT / path).read_bytes()
    return {"path": path, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def publish(archive, record, h, kind, depth):
    """Writes the record's relief PNG, its texture with the height as alpha, and returns its entry."""
    rgba = np.dstack([(refreshed(archive, record) * 255 + .5).astype(np.uint8), (h * 255 + .5).astype(np.uint8)])
    path = CONTENT / MEDIA / f"relief-{archive:03d}-{record}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(path, optimize=True)
    return hashed(f"{MEDIA}/{path.name}", path.read_bytes()) | {
        "width": rgba.shape[1], "height": rgba.shape[0], "kind": kind, "tallest": round(float(rgba[..., 3].max()) / 255, 4),
        "depth": depth} | LOOK


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("keys", nargs="*", help="an archive (every covered record of it) or ARCHIVE-RECORD")
    ap.add_argument("--all", action="store_true", help="every covered record")
    ap.add_argument("--look", action="store_true", help="only draw the check sheets")
    a = ap.parse_args()
    if bool(a.keys) == a.all:
        ap.error("name the records to make, or --all")
    records = covered() if a.all else chosen(a.keys)
    made = {}
    for archive, record in records:
        made.setdefault(archive, {})[record] = height(archive, record)
        print(f"{archive}-{record}: {made[archive][record][2]}", flush=True)
    for archive in made:
        print(sheet(archive, made[archive]))
    if a.look:
        return
    payload = json.loads(PAYLOAD.read_text())
    cover = payload["artReplacements"]["groundCover"]
    for archive, by_record in made.items():
        for record, (h, _, kind, depth) in by_record.items():
            key = f"{archive}-{record}"
            if key in cover["relief"] and not cover["relief"][key]["path"].startswith(f"{MEDIA}/"):
                raise SystemExit(f"{key}'s relief is another tool's ({cover['relief'][key]['path']}): one record has one relief.")
            cover["relief"][key] = publish(archive, record, h, kind, depth)
    cover["surfaceShader"] = hashed(f"{MEDIA}/{SHADER}")
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"{sum(len(r) for r in made.values())} reliefs recorded")


if __name__ == "__main__":
    main()
