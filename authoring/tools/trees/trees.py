#!/usr/bin/env python3
"""Make refreshed tree meshes and record them in the art set (`artReplacements.trees`).

A tree mesh stands in for a refreshed flora sprite near the player. The meshes are rusty-craftsurvive's scripted trees
(its scripts/generate-flora.py: low-poly trunks and branches in tiling bark, leaf cards in an atlas of four, their
alpha the share each vertex flutters), copied as they are. Their textures are repainted in the sprite's own colours:
each texel takes the colour of the sprite's texel at the same rank of brightness, leaves from the sprite's crown and bark
from its trunk, so the tree wears the painted sprite's palette and its spread of light and dark; and each leaf's edge
is darkened a step, as the sprites outline theirs.

The game draws a mesh at the sprite's drawn height (DaggerfallTreeMesh), so this records that height as a share of the
sprite's frame, with where its foot stands.

    trees.py
"""
import hashlib, json, shutil
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage, stats

REPOSITORY = Path(__file__).resolve().parents[3]
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed/trees"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"
CRAFTSURVIVE = REPOSITORY.parent / "rusty-craftsurvive/content/game"

# The trees with meshes, by nature archive and record, and the rusty-craftsurvive tree drawn for each: its model, its
# leaves and its bark. Every tree wears the oak's bark, whose vertical furrows are what the sprites paint; the pine's
# plates read as brickwork at a sprite's scale, and the fir's is nearly one pale tone.
TREES = {(506, 13): ("fir-p1", "fir", "oak"), (506, 30): ("pine-p1", "pine", "oak"), (506, 15): ("cork-oak-p1", "cork", "oak"),
         (506, 16): ("oak-p1", "oak", "oak")}
SOLID = 128
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


def palette(rgba, m):
    """The sprite's crown and trunk colours, each sorted by brightness."""
    solid = rgba[..., 3] > SOLID
    crown = rgba[m["top"]:m["crown"]][solid[m["top"]:m["crown"]]][:, :3]
    # The trunk's middle, below where the crown's leaves hang over it, and none of its green.
    half = max(1, int(m["trunk"] * .3))
    trunk = rgba[m["crown"] + int((m["foot"] - m["crown"]) * .3):m["foot"], m["centre"] - half:m["centre"] + half + 1]
    trunk = trunk[(trunk[..., 3] > SOLID) & (trunk[..., 1] <= trunk[..., 0])][:, :3]
    return crown[np.argsort(light(crown))], trunk[np.argsort(light(trunk))]


def repaint(source, colours, outline):
    """Each drawn texel takes the colour at its own rank of brightness among the sorted colours; leaves' edges a step darker."""
    rgba = np.asarray(Image.open(source).convert("RGBA"), np.float64)
    drawn = rgba[..., 3] > 0
    values = light(rgba[..., :3][drawn])
    # Equal texels share a rank, so a flat tone stays one colour.
    ranks = (stats.rankdata(values, method="average") - 1) / max(1, len(values) - 1)
    out = rgba.copy()
    out[..., :3][drawn] = colours[np.round(ranks * (len(colours) - 1)).astype(int)]
    if outline:
        edge = drawn & ~ndimage.binary_erosion(drawn)
        out[..., :3][edge] *= EDGE
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def publish(name, path):
    data = path.read_bytes()
    return {"path": f"{MEDIA}/{name}", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def main():
    payload = json.loads(PAYLOAD.read_text())
    flats = {(f["replaces"]["archive"], f["replaces"]["record"]): f for f in payload["artReplacements"]["flats"]}
    folder = CONTENT / MEDIA
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*"):
        old.unlink()
    trees = {}
    for (archive, record), (model, leaves, bark) in TREES.items():
        rgba = frame(flats[(archive, record)])
        m = measures(rgba)
        crown, trunk = palette(rgba, m)
        mesh = json.loads((CRAFTSURVIVE / f"models/trees/{model}.prop-mesh.json").read_text())
        shutil.copyfile(CRAFTSURVIVE / f"models/trees/{model}.prop-mesh.json", folder / f"{model}.prop-mesh.json")
        name = f"tree-{archive:03d}-{record:02d}"
        repaint(CRAFTSURVIVE / f"textures/trees/{leaves}-leaves.png", crown, True).save(folder / f"{name}-leaves.png")
        repaint(CRAFTSURVIVE / f"textures/trees/{bark}-bark.png", trunk, False).save(folder / f"{name}-bark.png")
        h = rgba.shape[0]
        trees[f"{archive}-{record}"] = {
            "mesh": publish(f"{model}.prop-mesh.json", folder / f"{model}.prop-mesh.json") | {"height": mesh["height"]},
            # The sprite's drawn height and where its foot stands, as shares of its frame height.
            "height": round(m["height"] / h, 4),
            "foot": round((h - 1 - m["foot"]) / h, 4),
            "leaves": publish(f"{name}-leaves.png", folder / f"{name}-leaves.png"),
            "bark": publish(f"{name}-bark.png", folder / f"{name}-bark.png"),
        }
    payload["artReplacements"]["trees"] = trees
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(json.dumps({key: {"mesh": tree["mesh"]["path"], "height": tree["height"]} for key, tree in trees.items()}, indent=1))


if __name__ == "__main__":
    main()
