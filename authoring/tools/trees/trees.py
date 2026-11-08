#!/usr/bin/env python3
"""Make refreshed tree meshes' art and record it in the art set (`artReplacements.trees`).

A tree mesh stands in for a refreshed flora sprite near the player: a procedural trunk and branches wearing bark, and leaf
cards wearing pieces of the sprite's own painted crown, so it keeps the sprite's look while it stands in the round and
sways leaf by leaf. The ruleset grows the mesh from a few measures of the sprite (DaggerfallTreeMesh); this tool takes
them, each as a share of the sprite's frame height, and cuts two textures from the sprite:

- the leaves: an atlas of CELLS by CELLS pieces of the crown, each a window onto it cut to a ragged round edge, so a card
  shows a clump of painted foliage rather than a square;
- the bark: a strip of the trunk, between its foot and the crown, mirrored so it tiles.

    trees.py
"""
import hashlib, json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

REPOSITORY = Path(__file__).resolve().parents[3]
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed/trees"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"

# The trees with meshes, by nature archive and record, and their form: a conifer's branches ring its trunk to the top in
# shrinking whorls; a broadleaf's trunk forks into a crown.
TREES = {(506, 13): "conifer", (506, 30): "conifer", (506, 15): "broadleaf", (506, 16): "broadleaf"}
CELLS = 4
SOLID = 128

rng = np.random.default_rng(10647)


def frame(flat):
    image = Image.open(CONTENT / flat["path"]).convert("RGBA")
    f = flat["frames"][0]
    return np.asarray(image.crop((f["x"], f["y"], f["x"] + f["width"], f["y"] + f["height"])), np.float64)


def measures(rgba):
    """The sprite's drawn rows and columns, its trunk's width near the foot, and the row its crown begins at."""
    solid = rgba[..., 3] > SOLID
    rows = np.nonzero(solid.any(1))[0]
    top, foot = rows[0], rows[-1]
    height = foot - top + 1
    widths = solid.sum(1)
    # The trunk's width: the drawn width a tenth up from the foot, where roots have narrowed to it.
    trunk = max(2, int(np.median(widths[foot - int(height * .2): foot - int(height * .06) + 1])))
    # The crown begins where the drawn width first spreads well past the trunk, from the foot up.
    crown = foot
    for row in range(foot - int(height * .1), top, -1):
        if widths[row] > trunk * 2.6:
            crown = row
            break
    columns = np.nonzero(solid[top:crown + 1].any(0))[0]
    return dict(top=top, foot=foot, height=height, trunk=trunk, crown=crown, crown_width=columns[-1] - columns[0] + 1,
                centre=int(np.median(np.nonzero(solid[foot - int(height * .15): foot])[1])))


def piece(m):
    """A leaf card's piece of the crown, in sprite texels: a fifth of the crown's width, so a card draws at the sprite's
    own texel size and many of them make the crown."""
    return max(8, int(m["crown_width"] * .2))


def leaves(rgba, m):
    """CELLS x CELLS pieces of the crown, each a window cut to a ragged round edge. The windows richest in foliage (green
    or blossom, lit) are taken, and the crown's near-black outlines are cut away, so the gaps between leaves show through."""
    crown = rgba[m["top"]:m["crown"] + 1].copy()
    rgb = crown[..., :3] / 255
    light = rgb @ [.2126, .7152, .0722]
    crown[..., 3] = np.where(light < .09, 0, crown[..., 3])
    solid = crown[..., 3] > SOLID
    foliage = solid & (light > .16) & ((rgb[..., 1] >= rgb[..., 0] * .95) | (rgb[..., 0] > rgb[..., 2] * 1.6) & (light > .3))
    size = piece(m)
    candidates = []
    for _ in range(3000):
        y = int(rng.integers(0, max(1, crown.shape[0] - size + 1)))
        x = int(rng.integers(0, max(1, crown.shape[1] - size + 1)))
        if crown[y:y + size, x:x + size].shape[:2] == (size, size):
            candidates.append((foliage[y:y + size, x:x + size].mean(), y, x))
    candidates.sort(reverse=True)
    chosen = []
    for score, y, x in candidates:
        # Pieces apart from one another, so the atlas holds the crown's variety rather than one patch many times.
        if all(abs(y - cy) + abs(x - cx) > size * .35 for cy, cx in chosen):
            chosen.append((y, x))
        if len(chosen) == CELLS * CELLS:
            break
    atlas = np.zeros((size * CELLS, size * CELLS, 4))
    for i, (y, x) in enumerate(chosen):
        window = crown[y:y + size, x:x + size].copy()
        yy, xx = (np.mgrid[:size, :size] - (size - 1) / 2) / (size / 2)
        edge = np.hypot(yy, xx) + ndimage.gaussian_filter(rng.random((size, size)), 1.2) * .9 - .45
        window[..., 3] = np.where(edge < .85, window[..., 3], 0)
        atlas[(i // CELLS) * size:(i // CELLS + 1) * size, (i % CELLS) * size:(i % CELLS + 1) * size] = window
    return Image.fromarray(atlas.astype(np.uint8), "RGBA")


def bark(rgba, m):
    """The trunk between its foot and the crown, mirrored across and along so it tiles."""
    low, high = m["crown"] + int((m["foot"] - m["crown"]) * .15), m["foot"] - int(m["height"] * .06)
    if high - low < 4:
        low, high = m["crown"], m["foot"]
    # The trunk's middle only: its edges are the sprite's dark outline, which would stripe the bark.
    half = max(1, int(m["trunk"] * .3))
    strip = rgba[low:high, m["centre"] - half:m["centre"] + half + 1, :3]
    tile = np.concatenate([strip, strip[:, ::-1]], 1)
    tile = np.concatenate([tile, tile[::-1]], 0)
    return Image.fromarray(tile.astype(np.uint8), "RGB").convert("RGBA")


def publish(name, image):
    path = CONTENT / MEDIA / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)
    data = path.read_bytes()
    return {"path": f"{MEDIA}/{name}.png", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(),
            "width": image.width, "height": image.height}


def main():
    payload = json.loads(PAYLOAD.read_text())
    flats = {(f["replaces"]["archive"], f["replaces"]["record"]): f for f in payload["artReplacements"]["flats"]}
    trees = {}
    for (archive, record), form in TREES.items():
        rgba = frame(flats[(archive, record)])
        m = measures(rgba)
        # Every measure is a share of the sprite's frame height, which the game draws at the sprite's world height.
        h = rgba.shape[0]
        bark_image = bark(rgba, m)
        trees[f"{archive}-{record}"] = {
            "form": form,
            "foot": round((h - 1 - m["foot"]) / h, 4),
            "height": round(m["height"] / h, 4),
            "crownBase": round((m["foot"] - m["crown"]) / h, 4),
            "crownRadius": round(m["crown_width"] / 2 / h, 4),
            "trunkRadius": round(m["trunk"] / 2 / h, 4),
            "cardSize": round(piece(m) / h, 4),
            # The bark tile's size, as the sprite draws it: across its width, up its height.
            "barkSize": [round(bark_image.width / h, 4), round(bark_image.height / h, 4)],
            "leaves": publish(f"tree-{archive:03d}-{record:02d}-leaves", leaves(rgba, m)) | {"cells": CELLS},
            "bark": publish(f"tree-{archive:03d}-{record:02d}-bark", bark_image),
        }
    payload["artReplacements"]["trees"] = trees
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(json.dumps({key: {k: v for k, v in tree.items() if k not in ("leaves", "bark")} for key, tree in trees.items()}, indent=1))


if __name__ == "__main__":
    main()
