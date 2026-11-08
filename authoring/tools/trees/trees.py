#!/usr/bin/env python3
"""Make refreshed tree meshes and record them in the art set (`artReplacements.trees`).

A tree mesh stands in for a refreshed flora sprite near the player. The meshes are rusty-craftsurvive's scripted trees
(its scripts/generate-flora.py: low-poly trunks and branches in tiling bark, leaf cards in an atlas of four, their
alpha the share each vertex flutters), copied with the leaf cards below the sprite's crown left out, so the trunk shows
under the crown as the sprite's does. Their textures are repainted in the sprite's own colours:
each texel takes the colour of the sprite's texel at the same rank of brightness, leaves from the sprite's crown and bark
from its trunk (leaving out its darkest, the outline), so the tree wears the painted sprite's palette and its spread of
light and dark; leaves lean toward the darks, which lit cards lose, and each leaf's edge is darkened a step, as the
sprites outline theirs.

The game draws a mesh at the sprite's drawn height (DaggerfallTreeMesh), so this records that height as a share of the
sprite's frame, with where its foot stands.

    trees.py
"""
import hashlib, json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage, stats

REPOSITORY = Path(__file__).resolve().parents[3]
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed/trees"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"
CRAFTSURVIVE = REPOSITORY.parent / "rusty-craftsurvive/content/game"

# The trees with meshes, by nature archive and record: every big tree of every climate but the few whose shapes are their
# own (the swamps' vine-wrapped and bulb-trunked plants, the desert's cacti, the rainforest's mossy hollow tree, every
# massive gnarled bare tree), which stay sprites, as do bushes, rocks and small plants. Each names the rusty-craftsurvive tree drawn for it by form:
# conifers a fir or pine, layered flat-topped trees a cork oak, round crowns a beech or chestnut, palms a palm, bare
# slender bare trees the dead tree; a winter set's tree is its summer one's, in its snowy colours. Variants (p1 to p3) differ from
# record to record, so a stand is not one tree repeated.
FIR, PINE, CORK, BEECH, CHESTNUT, PALM, DEAD = "fir", "pine", "cork-oak", "beech", "chestnut", "palm", "dead"
FORMS = {
    500: {12: (PALM, 1), 14: (PALM, 2), 15: (PALM, 3), 30: (CHESTNUT, 1)},
    501: {11: (PALM, 1), 12: (PALM, 2), 13: (PALM, 3), 16: (PALM, 1), 30: (PALM, 2)},
    502: {13: (CORK, 1), 16: (CORK, 2), 17: (CORK, 3), 18: (CHESTNUT, 2)},
    503: {5: (CORK, 2), 11: (DEAD, 1), 13: (PALM, 1)},
    504: {11: (CORK, 1), 12: (CORK, 3), 13: (FIR, 1), 14: (CORK, 2), 15: (CORK, 1), 16: (BEECH, 3), 17: (BEECH, 1),
          18: (CHESTNUT, 1), 24: (DEAD, 1), 25: (PINE, 2)},
    505: {11: (CORK, 1), 12: (CORK, 3), 13: (FIR, 1), 14: (CORK, 2), 16: (BEECH, 3), 17: (BEECH, 1),
          18: (CHESTNUT, 1), 24: (DEAD, 1), 25: (PINE, 2)},
    506: {11: (FIR, 2), 12: (FIR, 3), 13: (FIR, 1), 14: (CORK, 2), 15: (CORK, 1), 16: (BEECH, 3), 24: (DEAD, 1),
          25: (PINE, 2), 30: (PINE, 1)},
    507: {11: (FIR, 2), 12: (FIR, 3), 13: (FIR, 1), 14: (CORK, 2), 16: (BEECH, 3), 24: (DEAD, 1), 25: (PINE, 2)},
    508: {13: (CHESTNUT, 3), 15: (CORK, 1), 24: (DEAD, 1)},
    509: {24: (DEAD, 1)},
    510: {11: (FIR, 2), 12: (FIR, 3), 13: (FIR, 1), 15: (CORK, 1), 24: (DEAD, 1), 25: (PINE, 2), 30: (PINE, 1)},
    511: {11: (FIR, 2), 13: (FIR, 1), 24: (DEAD, 1), 25: (PINE, 2)},
}
# Each rusty-craftsurvive tree's leaves and bark textures. Every tree wears the oak's bark, whose vertical furrows are
# what the sprites paint, but the palm, whose rings are its own.
TEXTURES = {FIR: ("fir", "oak"), PINE: ("pine", "oak"), CORK: ("cork", "oak"), BEECH: ("beech", "oak"),
            CHESTNUT: ("chestnut", "oak"), PALM: ("palm", "palm"), DEAD: (None, "oak")}
SOLID = 128
# How far the leaves' colours lean toward the sprite's darks: a rank is raised to this power, so the painted crown's deep
# shadow, which lit cards would otherwise lose, comes back.
LEAF_DARKEN = 1.2
# How dark, as a share of full brightness, a sprite's outline texels are.
OUTLINE = .06
# The most of a crown's leaves its other hues (blossoms, a summer tree's autumn touches) may colour.
ACCENT = .1
# A texel this bright in every channel is snow.
SNOW = 170
# The share of the trunk's darkest texels left out of its bark's colours: they are the sprite's outline, not its bark.
BARK_OUTLINE = .15
# A leaf card is a quad drawn both ways: four vertices and twelve indices.
CARD_INDICES = 12
# How far from its axis, in the mesh's metres, a trunk's bark reaches at its widest (its flared foot).
TRUNK_REACH = .9
# How much darker a leaf's edge is drawn than its body.
EDGE = .6


def frame(flat):
    image = Image.open(CONTENT / flat["path"]).convert("RGBA")
    f = flat["frames"][0]
    return np.asarray(image.crop((f["x"], f["y"], f["x"] + f["width"], f["y"] + f["height"])), np.float64)


def measures(rgba):
    """The sprite's drawn rows, its trunk's width near the foot, the row its crown begins at and the trunk's centre."""
    solid = rgba[..., 3] > SOLID
    rows = np.nonzero(solid.any(1))[0]
    top, foot = rows[0], rows[-1]
    height = foot - top + 1
    widths = solid.sum(1)
    trunk = max(2, int(np.median(widths[foot - int(height * .2): foot - int(height * .06) + 1])))
    crown = foot
    for row in range(foot - int(height * .1), top, -1):
        if widths[row] > trunk * 2.6:
            crown = row
            break
    centre = int(np.median(np.nonzero(solid[foot - int(height * .15): foot])[1]))
    return dict(top=top, foot=foot, height=height, trunk=trunk, crown=crown, centre=centre)


def light(rgb):
    return rgb @ [.2126, .7152, .0722]


def hues(rgb):
    """Each colour's hue in degrees and its saturation, 0 to 1."""
    high, low = rgb.max(1), rgb.min(1)
    span = np.maximum(high - low, 1e-6)
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    hue = np.where(high == r, (g - b) / span % 6, np.where(high == g, (b - r) / span + 2, (r - g) / span + 4)) * 60
    return hue, (high - low) / np.maximum(high, 1e-6)


def palette(rgba, m):
    """The sprite's crown and trunk colours, each sorted by brightness."""
    solid = rgba[..., 3] > SOLID
    crown = rgba[m["top"]:m["crown"]][solid[m["top"]:m["crown"]]][:, :3]
    # The crown's outline is near black; a leaf ranked among it would be drawn black, so its colours are left out.
    crown = crown[light(crown) > OUTLINE * 255]
    # A leaf is one flat tone, so a colour the sprite only dabs (its blossoms, the branches in its crown, its snow) would
    # paint whole leaves. Leaves take the colours of the crown's own hue (green, or an autumn tree's orange) and its
    # shadows, then the other hues only their own share up to ACCENT, as the brightest; snow is left to the weather.
    crown = crown[crown.min(1) < SNOW]
    hue, saturation = hues(crown)
    coloured = saturation > .12
    if coloured.sum() >= 20:
        bins = np.bincount((hue[coloured] // 30).astype(int) % 12, minlength=12)
        # A hue spreads over its neighbouring bins (greens run from yellow-green to blue-green), so each bin counts them too.
        main = (np.argmax(bins + np.roll(bins, 1) + np.roll(bins, -1)) + .5) * 30
        near = np.abs((hue - main + 180) % 360 - 180) <= 45
        foliage = crown[(near & coloured) | (~coloured & (light(crown) < 90))]
        others = crown[coloured & ~near]
        others = others[np.argsort(light(others))][-int(len(foliage) * min(ACCENT, len(others) / max(1, len(foliage) + len(others)))):] \
            if len(others) and len(foliage) else others[:0]
        crown = np.concatenate([foliage[np.argsort(light(foliage))], others])
    else:
        crown = crown if len(crown) else rgba[rgba[..., 3] > SOLID][:, :3]
        crown = crown[np.argsort(light(crown))]
    # The trunk's middle, below where the crown's leaves hang over it, and none of its green.
    half = max(1, int(m["trunk"] * .3))
    # A sprite whose crown reaches its foot shows little trunk there: then the trunk's column the whole way up, then all
    # its drawn texels that are not green.
    for low, high, left, right in ((m["crown"] + int((m["foot"] - m["crown"]) * .3), m["foot"], m["centre"] - half, m["centre"] + half + 1),
                                   (m["top"], m["foot"], m["centre"] - half, m["centre"] + half + 1), (0, rgba.shape[0], 0, rgba.shape[1])):
        trunk = rgba[low:high, max(0, left):right]
        trunk = trunk[(trunk[..., 3] > SOLID) & (trunk[..., 1] <= trunk[..., 0])][:, :3]
        if len(trunk) >= 20:
            break
    else:
        raise SystemExit("A sprite has no texel of bark to take its colours from.")
    trunk = trunk[np.argsort(light(trunk))]
    return crown, trunk[int(len(trunk) * BARK_OUTLINE):]


def repaint(source, colours, outline, darken=1.0):
    """Each drawn texel takes the colour at its own rank of brightness among the sorted colours; leaves' edges a step darker."""
    rgba = np.asarray(Image.open(source).convert("RGBA"), np.float64)
    drawn = rgba[..., 3] > 0
    values = light(rgba[..., :3][drawn])
    # Equal texels share a rank, so a flat tone stays one colour.
    ranks = ((stats.rankdata(values, method="average") - 1) / max(1, len(values) - 1)) ** darken
    out = rgba.copy()
    out[..., :3][drawn] = colours[np.round(ranks * (len(colours) - 1)).astype(int)]
    if outline:
        edge = drawn & ~ndimage.binary_erosion(drawn)
        out[..., :3][edge] *= EDGE
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def trim(mesh, below):
    """Leaves out the leaf cards whose middle stands below a height, and the branches' bark there (any triangle beyond
    TRUNK_REACH of the trunk's axis), so the bare trunk shows under the crown as the sprite's does."""
    for part in mesh["parts"]:
        x, y, z = part["positions"][0::3], part["positions"][1::3], part["positions"][2::3]
        size = CARD_INDICES if part["role"] == "leaves" else 3
        kept = []
        for start in range(0, len(part["indices"]), size):
            shape = part["indices"][start:start + size]
            corners = set(shape)
            middle_y = sum(y[i] for i in corners) / len(corners)
            reach = max(np.hypot(x[i], z[i]) for i in corners)
            if middle_y >= below or (part["role"] == "bark" and reach <= TRUNK_REACH):
                kept += shape
        part["indices"] = kept
    return mesh


def outward(mesh):
    """Turns each bark triangle to face the way its vertices' normals point. rusty-craftsurvive winds its tubes inward,
    which this Engine culls as their outside, so a trunk showed its far wall from within."""
    for part in mesh["parts"]:
        if part["role"] != "bark":
            continue
        p, n, idx = np.array(part["positions"]).reshape(-1, 3), np.array(part["normals"]).reshape(-1, 3), part["indices"]
        for t in range(0, len(idx), 3):
            a, b, c = idx[t:t + 3]
            if np.dot(np.cross(p[b] - p[a], p[c] - p[a]), n[a] + n[b] + n[c]) < 0:
                idx[t + 1], idx[t + 2] = c, b
    return mesh


def publish(name, path):
    data = path.read_bytes()
    return {"path": f"{MEDIA}/{name}", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def tree(flat, archive, record, species, variant, folder):
    """One tree's mesh, trimmed under its sprite's crown, and its textures in the sprite's colours."""
    rgba = frame(flat)
    m = measures(rgba)
    crown, trunk = palette(rgba, m)
    model = f"{species}-p{variant}"
    mesh = json.loads((CRAFTSURVIVE / f"models/trees/{model}.prop-mesh.json").read_text())
    # The sprite's crown begins this far up its drawn height; the mesh's leaves start no lower, a little short of it. A
    # bare tree keeps its low branches, as its sprite does.
    base = 0 if species == DEAD else round((m["foot"] - m["crown"]) / m["height"] * mesh["height"] * .85, 1)
    # Trees trimmed alike share one file.
    mesh_name = f"{model}-from-{base:g}.prop-mesh.json"
    if not (folder / mesh_name).exists():
        (folder / mesh_name).write_text(json.dumps(outward(trim(mesh, base)), separators=(",", ":")))
    leaves, bark = TEXTURES[species]
    name = f"tree-{archive:03d}-{record:02d}"
    h = rgba.shape[0]
    entry = {
        "mesh": publish(mesh_name, folder / mesh_name) | {"height": mesh["height"]},
        # The sprite's drawn height and where its foot stands, as shares of its frame height.
        "height": round(m["height"] / h, 4),
        "foot": round((h - 1 - m["foot"]) / h, 4),
    }
    if leaves:
        repaint(CRAFTSURVIVE / f"textures/trees/{leaves}-leaves.png", crown, True, LEAF_DARKEN).save(folder / f"{name}-leaves.png")
        entry["leaves"] = publish(f"{name}-leaves.png", folder / f"{name}-leaves.png")
    repaint(CRAFTSURVIVE / f"textures/trees/{bark}-bark.png", trunk, False).save(folder / f"{name}-bark.png")
    entry["bark"] = publish(f"{name}-bark.png", folder / f"{name}-bark.png")
    return entry


def main():
    payload = json.loads(PAYLOAD.read_text())
    flats = {(f["replaces"]["archive"], f["replaces"]["record"]): f for f in payload["artReplacements"]["flats"]}
    folder = CONTENT / MEDIA
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*"):
        old.unlink()
    trees = {}
    for archive, forms in FORMS.items():
        for record, (species, variant) in forms.items():
            trees[f"{archive}-{record}"] = tree(flats[(archive, record)], archive, record, species, variant, folder)
    payload["artReplacements"]["trees"] = trees
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"{len(trees)} trees, {len(list(folder.glob('*.prop-mesh.json')))} meshes")


if __name__ == "__main__":
    main()
